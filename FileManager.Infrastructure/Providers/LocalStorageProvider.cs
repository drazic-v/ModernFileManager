using FileManager.Core.Models;
using FileManager.Core.Providers;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;


namespace FileManager.Infrastructure.Providers
{
    public class LocalStorageProvider : IStorageProvider
    {
        public string ProviderId { get; } = "local";

        // Converts a StoragePath value to a native path string for the current OS.
        internal static string ToNativePath(string value) =>
        OperatingSystem.IsWindows() ? value.Replace('/', '\\') : value;

        // Converts a native path string to a StoragePath value.
        internal static string ToStoragePathValue(string nativePath) =>
            nativePath.Replace('\\', '/');
        internal static bool IsValidFileName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return false;

            return true;
        }

        internal static void CopyDirectoryRecursively(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destinationDir, Path.GetFileName(file));
                File.Copy(file, destFile);
            }
            foreach (var directory in Directory.GetDirectories(sourceDir))
            {
                var destDir = Path.Combine(destinationDir, Path.GetFileName(directory));
                CopyDirectoryRecursively(directory, destDir);
            }
        }


        public async Task<StorageItem> GetInfoAsync(StoragePath path, CancellationToken ct = default)
        {
            var nativePath = ToNativePath(path.Value);
            var isDir = Directory.Exists(nativePath);
            FileSystemInfo info = isDir ? new DirectoryInfo(nativePath) : new FileInfo(nativePath);

            return new StorageItem
            {
                Path = path,
                Name = path.Name,
                Kind = isDir ? StorageItemKind.Directory : StorageItemKind.File,
                SizeInBytes = isDir ? null : ((FileInfo)info).Length,
                LastModifiedUtc = info.LastWriteTimeUtc,
                CreatedUtc = info.CreationTimeUtc,
                ContentType = isDir ? null : MimeMapping.MimeUtility.GetMimeMapping(nativePath),
                Attributes = File.GetAttributes(nativePath)
            };
        }

        public async IAsyncEnumerable<StorageItem> ListAsync(StoragePath folder, [EnumeratorCancellation] CancellationToken ct = default) 
        {
            var nativePath = ToNativePath(folder.Value);
            var dirInfo = new DirectoryInfo(nativePath);

            FileInfo[] files;
            DirectoryInfo[] directories;

            try
            {
                files = dirInfo.GetFiles();
                directories = dirInfo.GetDirectories();
            }
            catch (UnauthorizedAccessException)
            {
                yield break; // Skip this instance if access is denied
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var childPath = new StoragePath { ProviderId = ProviderId, Value = ToStoragePathValue(file.FullName) };
                yield return await GetInfoAsync(childPath, ct);
            }

            foreach (var directory in directories)
            {
                ct.ThrowIfCancellationRequested();
                var childPath = new StoragePath { ProviderId = ProviderId, Value = ToStoragePathValue(directory.FullName) };
                yield return await GetInfoAsync(childPath, ct);
            }
        }

        public async Task<StorageItem> CreateDirectoryAsync(StoragePath parent, string name, CancellationToken ct = default) 
        {
            name = await UniqueNameGenerator.GenerateAsync(this, parent, name, StorageItemKind.Directory, ct); // Ensure unique name
            
            var childPath = parent.Combine(name);
            var childNativePath = ToNativePath(childPath.Value);
            Directory.CreateDirectory(childNativePath);

            return await GetInfoAsync(childPath, ct);
        }
        public async Task DeleteAsync(StoragePath path, CancellationToken ct = default)
        {
            var nativePath = ToNativePath(path.Value);
            try
            {
                if (Directory.Exists(nativePath))
                    Directory.Delete(nativePath, recursive: true);
                else if (File.Exists(nativePath))
                    File.Delete(nativePath);
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException)
            {
                // vanished between the Exists check and the Delete call - already gone is already success
            }
        }
        public async Task<StorageItem> RenameAsync(StoragePath path, string newName, CancellationToken ct = default)
        {
            if (!IsValidFileName(newName))
                throw new ArgumentException("New name is not valid.", nameof(newName));

            var nativePath = ToNativePath(path.Value);
            var attr = File.GetAttributes(nativePath);
            var kind = attr.HasFlag(FileAttributes.Directory) ? StorageItemKind.Directory : StorageItemKind.File;

            if (newName == path.Name)
                return await GetInfoAsync(path, ct);

            var parentPath = path.Parent() ?? throw new InvalidOperationException("Cannot rename a root item.");
            newName = await UniqueNameGenerator.GenerateAsync(this, parentPath, newName, kind, ct);

            var newPath = parentPath.Combine(newName);
            var newNativePath = ToNativePath(newPath.Value);

            if (kind == StorageItemKind.Directory)
                Directory.Move(nativePath, newNativePath);
            else
                File.Move(nativePath, newNativePath);

            return await GetInfoAsync(newPath, ct);
        }
        public async Task<StorageItem> CopyAsync(StoragePath source, StoragePath destinationFolder, CancellationToken ct = default)
        {
            var nativeSource = ToNativePath(source.Value);            
            var attr = File.GetAttributes(nativeSource);
            var kind = attr.HasFlag(FileAttributes.Directory) ? StorageItemKind.Directory : StorageItemKind.File;

            var newName = await UniqueNameGenerator.GenerateAsync(this, destinationFolder, source.Name, kind, ct);
            var destinationPath = destinationFolder.Combine(newName);
            var nativeDestination = ToNativePath(destinationPath.Value);

            if (kind == StorageItemKind.Directory)
                CopyDirectoryRecursively(nativeSource, nativeDestination); // For directories, use recursive copy
            else if (kind == StorageItemKind.File)
                File.Copy(nativeSource, nativeDestination);

            return await GetInfoAsync(new StoragePath { ProviderId = ProviderId, Value = ToStoragePathValue(nativeDestination) }, ct);
        }
        public Task<StorageItem> MoveAsync(StoragePath source, StoragePath destinationFolder, CancellationToken ct = default) => throw new NotImplementedException();
        public Task OpenAsync(StoragePath path, CancellationToken ct = default) => throw new NotImplementedException();

        public IAsyncEnumerable<StorageItem> SearchAsync(StoragePath root, string query, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Stream> OpenReadAsync(StoragePath path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Stream> OpenWriteAsync(StoragePath destination, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
