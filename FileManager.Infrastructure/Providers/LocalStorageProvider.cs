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

        internal static void CopyDirectoryRecursively(
    string sourceDir, string destinationDir, IProgress<TransferProgress>? progress, CancellationToken ct)
        {
            CopyDirectoryRecursiveCore(sourceDir, destinationDir, new TransferAccumulator(), progress, ct);
        }

        private static void CopyDirectoryRecursiveCore(
            string sourceDir, string destinationDir, TransferAccumulator acc,
            IProgress<TransferProgress>? progress, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(destinationDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                ct.ThrowIfCancellationRequested();
                var destFile = Path.Combine(destinationDir, Path.GetFileName(file));
                File.Copy(file, destFile);
                acc.Bytes += new FileInfo(destFile).Length;
                acc.Files++;
                progress?.Report(new TransferProgress(acc.Bytes, acc.Files));
            }
            foreach (var directory in Directory.GetDirectories(sourceDir))
            {
                CopyDirectoryRecursiveCore(directory, Path.Combine(destinationDir, Path.GetFileName(directory)), acc, progress, ct);
            }
        }

        internal async Task MergeDirectoriesRecursivelyAsync(
    string sourceDir, string destinationDir, ConflictResolver resolver,
    IProgress<TransferProgress>? progress, CancellationToken ct)
        {
            await MergeCoreAsync(sourceDir, destinationDir, resolver, new TransferAccumulator(), progress, ct);
        }

        private async Task MergeCoreAsync(
            string sourceDir, string destinationDir, ConflictResolver resolver,
            TransferAccumulator acc, IProgress<TransferProgress>? progress, CancellationToken ct)
        {
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                ct.ThrowIfCancellationRequested();
                var destFile = Path.Combine(destinationDir, Path.GetFileName(file));

                if (!File.Exists(destFile))
                {
                    File.Copy(file, destFile);
                }
                else
                {
                    var destPath = new StoragePath { ProviderId = ProviderId, Value = ToStoragePathValue(destFile) };
                    var policy = await resolver(destPath, StorageItemKind.File, ct);
                    switch (policy)
                    {
                        case NameCollisionPolicy.Replace:
                            File.Replace(file, destFile, null);
                            break;
                        case NameCollisionPolicy.GenerateUnique:
                            var destFolder = new StoragePath { ProviderId = ProviderId, Value = ToStoragePathValue(destinationDir) };
                            var uniqueName = await UniqueNameGenerator.GenerateAsync(this, destFolder, Path.GetFileName(file), StorageItemKind.File, ct);
                            destFile = Path.Combine(destinationDir, uniqueName);
                            File.Copy(file, destFile);
                            break;
                        case NameCollisionPolicy.Skip:
                            continue; // nothing copied - don't touch acc or report
                        case NameCollisionPolicy.Fail:
                            throw new IOException($"An item named '{Path.GetFileName(file)}' already exists at the destination.");
                        case NameCollisionPolicy.Merge:
                            throw new ArgumentException("Merge is not a valid resolution for a file conflict.", nameof(policy));
                    }
                }

                acc.Bytes += new FileInfo(destFile).Length;
                acc.Files++;
                progress?.Report(new TransferProgress(acc.Bytes, acc.Files));
            }

            foreach (var directory in Directory.GetDirectories(sourceDir))
            {
                ct.ThrowIfCancellationRequested();
                var destDir = Path.Combine(destinationDir, Path.GetFileName(directory));

                if (!Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                    CopyDirectoryRecursiveCore(directory, destDir, acc, progress, ct); // same accumulator - one running total across the whole merge
                }
                else
                {
                    await MergeCoreAsync(directory, destDir, resolver, acc, progress, ct);
                }
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
        private static async Task CopyItemAsync(
    string nativeSource, string nativeDestination, StorageItemKind kind,
    IProgress<TransferProgress>? progress, CancellationToken ct)
        {
            if (kind == StorageItemKind.Directory)
            {
                await Task.Run(() => CopyDirectoryRecursively(nativeSource, nativeDestination, progress, ct), ct);
            }
            else
            {
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested(); // can bail before a huge single file starts; can't interrupt mid-copy - File.Copy has no cancellable overload
                    File.Copy(nativeSource, nativeDestination);
                    progress?.Report(new TransferProgress(new FileInfo(nativeDestination).Length, 1));
                }, ct);
            }
        }

        public async Task<StorageItem> CopyAsync(
            StoragePath source, StoragePath destinationFolder, ConflictResolver resolver,
            IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
        {
            var nativeSource = ToNativePath(source.Value);
            var attr = File.GetAttributes(nativeSource);
            var kind = attr.HasFlag(FileAttributes.Directory) ? StorageItemKind.Directory : StorageItemKind.File;

            var destinationPath = destinationFolder.Combine(source.Name);
            var nativeDestination = ToNativePath(destinationPath.Value);
            var conflicts = kind == StorageItemKind.Directory ? Directory.Exists(nativeDestination) : File.Exists(nativeDestination);

            if (!conflicts)
            {
                await CopyItemAsync(nativeSource, nativeDestination, kind, progress, ct);
                return await GetInfoAsync(destinationPath, ct);
            }

            var policy = await resolver(destinationPath, kind, ct);
            switch (policy)
            {
                case NameCollisionPolicy.GenerateUnique:
                    var newName = await UniqueNameGenerator.GenerateAsync(this, destinationFolder, source.Name, kind, ct);
                    destinationPath = destinationFolder.Combine(newName);
                    nativeDestination = ToNativePath(destinationPath.Value);
                    await CopyItemAsync(nativeSource, nativeDestination, kind, progress, ct);
                    break;
                case NameCollisionPolicy.Replace:
                    if (kind == StorageItemKind.Directory)
                    {
                        await DeleteAsync(destinationPath, ct);
                        await CopyItemAsync(nativeSource, nativeDestination, kind, progress, ct);
                    }
                    else
                    {
                        await Task.Run(() =>
                        {
                            File.Replace(nativeSource, nativeDestination, null);
                            progress?.Report(new TransferProgress(new FileInfo(nativeDestination).Length, 1));
                        }, ct);
                    }
                    break;
                case NameCollisionPolicy.Merge:
                    if (kind != StorageItemKind.Directory)
                        throw new ArgumentException("Merge only applies when both source and destination are directories.", nameof(policy));
                    await MergeDirectoriesRecursivelyAsync(nativeSource, nativeDestination, resolver, progress, ct);
                    break;
                case NameCollisionPolicy.Skip:
                    return await GetInfoAsync(destinationPath, ct);
                case NameCollisionPolicy.Fail:
                    throw new IOException($"An item named '{source.Name}' already exists at the destination.");
            }

            return await GetInfoAsync(destinationPath, ct);
        }
        public async Task<StorageItem> CopyAsync(
            StoragePath source, StoragePath destinationFolder,
            NameCollisionPolicy policy = NameCollisionPolicy.GenerateUnique,
            IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
            => await CopyAsync(source, destinationFolder, (_, _, _) => Task.FromResult(policy), progress, ct);

        public Task<StorageItem> MoveAsync(StoragePath source, StoragePath destinationFolder, ConflictResolver resolver, IProgress<TransferProgress>? progress = null, CancellationToken ct = default) => throw new NotImplementedException();

        public async Task<StorageItem> MoveAsync(
            StoragePath source, StoragePath destinationFolder,
            NameCollisionPolicy policy = NameCollisionPolicy.GenerateUnique,
            IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
            => await MoveAsync(source, destinationFolder, (_, _, _) => Task.FromResult(policy), progress, ct);

        public Task OpenAsync(StoragePath path, CancellationToken ct = default) => throw new NotImplementedException();

        public IAsyncEnumerable<StorageItem> SearchAsync(StoragePath root, string query, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Stream> OpenReadAsync(StoragePath path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Stream> OpenWriteAsync(StoragePath destination, CancellationToken ct = default) => throw new NotImplementedException();
    }

    sealed class TransferAccumulator
    {
        public long Bytes;
        public int Files;
    }
}
