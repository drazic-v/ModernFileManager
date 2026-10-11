using FileManager.Core.Models;
using FileManager.Core.Providers;
using FileManager.Infrastructure.Providers;
using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.Infrastructure.Tests.Providers
{
    public partial class LocalStorageProviderTests
    {
        [Fact]
        public async Task CopyAsync_OnExistingDestination_GeneratesUniqueName()
        {
            var filePath = Path.Combine(_tempDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "hello", TestContext.Current.CancellationToken);
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);
            await File.WriteAllTextAsync(Path.Combine(destFolder, "test.txt"), "world", TestContext.Current.CancellationToken);
            var item = await _provider.CopyAsync(PathFor(filePath), PathFor(destFolder), ct:TestContext.Current.CancellationToken);
            Assert.Equal(StorageItemKind.File, item!.Kind);
            Assert.Equal("test (2).txt", item!.Name);
            Assert.True(File.Exists(LocalStorageProvider.ToNativePath(item!.Path.Value)));
        }

        [Fact]
        public async Task CopyAsync_OnNonExistingSource_ThrowsFileNotFoundException()
        {
            var nonExistingPath = Path.Combine(_tempDir, "nonexistent.txt");
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);
            await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            {
                await _provider.CopyAsync(PathFor(nonExistingPath), PathFor(destFolder), ct:TestContext.Current.CancellationToken);
            });
        }

        [Fact]
        public async Task CopyAsync_OnNonExistingDestinationFolder_ThrowsDirectoryNotFoundException()
        {
            var filePath = Path.Combine(_tempDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "hello", TestContext.Current.CancellationToken);
            var nonExistingDestFolder = Path.Combine(_tempDir, "nonexistent");
            await Assert.ThrowsAsync<DirectoryNotFoundException>(async () =>
            {
                await _provider.CopyAsync(PathFor(filePath), PathFor(nonExistingDestFolder), ct:TestContext.Current.CancellationToken);
            });
        }

        [Fact]
        public async Task CopyAsync_OnDirectoryWithExistingDestination_GeneratesUniqueName()
        {
            var dirPath = Path.Combine(_tempDir, "subfolder");
            Directory.CreateDirectory(dirPath);
            await File.WriteAllTextAsync(Path.Combine(dirPath, "file.txt"), "hello", TestContext.Current.CancellationToken);
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);
            Directory.CreateDirectory(Path.Combine(destFolder, "subfolder")); // existing destination
            var item = await _provider.CopyAsync(PathFor(dirPath), PathFor(destFolder), ct:TestContext.Current.CancellationToken);
            Assert.Equal(StorageItemKind.Directory, item!.Kind);
            Assert.Equal("subfolder (2)", item.Name);
            var copiedDirPath = LocalStorageProvider.ToNativePath(item.Path.Value);
            Assert.True(Directory.Exists(copiedDirPath));
            Assert.True(File.Exists(Path.Combine(copiedDirPath, "file.txt")));
        }

        [Fact]
        public async Task CopyAsync_OnDirectoryWithNestedContents_CopiesAllContents()
        {
            var dirPath = Path.Combine(_tempDir, "subfolder");
            Directory.CreateDirectory(dirPath);
            var nestedDirPath = Path.Combine(dirPath, "nested");
            Directory.CreateDirectory(nestedDirPath);
            await File.WriteAllTextAsync(Path.Combine(nestedDirPath, "deep.txt"), "world", TestContext.Current.CancellationToken);
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);
            var item = await _provider.CopyAsync(PathFor(dirPath), PathFor(destFolder), ct:TestContext.Current.CancellationToken);
            Assert.Equal(StorageItemKind.Directory, item!.Kind);
            Assert.Equal("subfolder", item.Name);
            var copiedDirPath = LocalStorageProvider.ToNativePath(item.Path.Value);
            Assert.True(Directory.Exists(copiedDirPath));
            Assert.True(Directory.Exists(Path.Combine(copiedDirPath, "nested")));
            Assert.True(File.Exists(Path.Combine(copiedDirPath, "nested", "deep.txt")));
        }
        [Fact]
        public async Task CopyAsync_OnFile_LeavesSourceIntact()
        {
            var filePath = Path.Combine(_tempDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "hello", TestContext.Current.CancellationToken);
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);

            await _provider.CopyAsync(PathFor(filePath), PathFor(destFolder), ct:TestContext.Current.CancellationToken);

            Assert.True(File.Exists(filePath));
        }

        [Fact]
        public async Task CopyAsync_OnDirectory_LeavesSourceIntact()
        {
            var dirPath = Path.Combine(_tempDir, "subfolder");
            Directory.CreateDirectory(dirPath);
            await File.WriteAllTextAsync(Path.Combine(dirPath, "file.txt"), "hello", TestContext.Current.CancellationToken);
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);

            await _provider.CopyAsync(PathFor(dirPath), PathFor(destFolder), ct:TestContext.Current.CancellationToken);

            Assert.True(Directory.Exists(dirPath)); // this line alone would have failed against Directory.Move
            Assert.True(File.Exists(Path.Combine(dirPath, "file.txt")));
        }

        [Fact]
        public async Task CopyAsync_OnDirectoryNameWithDot_DoesNotMangleExtension()
        {
            var dirPath = Path.Combine(_tempDir, "Client Files v2.1");
            Directory.CreateDirectory(dirPath);
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);
            Directory.CreateDirectory(Path.Combine(destFolder, "Client Files v2.1"));

            var item = await _provider.CopyAsync(PathFor(dirPath), PathFor(destFolder), ct:TestContext.Current.CancellationToken);

            Assert.Equal("Client Files v2.1 (2)", item!.Name);
        }

        [Fact]
        public async Task CopyAsync_OnReplaceRequest_ReplacesTheRequiredFile()
        {
            var filePath = Path.Combine(_tempDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "hello", TestContext.Current.CancellationToken);
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);
            var destFilePath = Path.Combine(destFolder, "test.txt");
            await File.WriteAllTextAsync(destFilePath, "world", TestContext.Current.CancellationToken);
            var item = await _provider.CopyAsync(PathFor(filePath), PathFor(destFolder), NameCollisionPolicy.Replace, ct:TestContext.Current.CancellationToken);
            Assert.Equal("test.txt", item!.Name);
            Assert.True(File.Exists(destFilePath));
            var content = await File.ReadAllTextAsync(destFilePath, TestContext.Current.CancellationToken);
            Assert.Equal("hello", content); // the destination file should now contain the source file's content
        }

        [Fact]
        public async Task CopyAsync_OnReplaceRequestForDirectory_ReplacesTheRequiredDirectory()
        {
            var dirPath = Path.Combine(_tempDir, "subfolder");
            Directory.CreateDirectory(dirPath);
            await File.WriteAllTextAsync(Path.Combine(dirPath, "file.txt"), "hello", TestContext.Current.CancellationToken);
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);
            var destDirPath = Path.Combine(destFolder, "subfolder");
            Directory.CreateDirectory(destDirPath);
            await File.WriteAllTextAsync(Path.Combine(destDirPath, "oldfile.txt"), "world", TestContext.Current.CancellationToken);
            var item = await _provider.CopyAsync(PathFor(dirPath), PathFor(destFolder), NameCollisionPolicy.Replace, ct:TestContext.Current.CancellationToken);
            Assert.Equal("subfolder", item!.Name);
            Assert.True(Directory.Exists(destDirPath));
            Assert.True(File.Exists(Path.Combine(destDirPath, "file.txt")));
            Assert.False(File.Exists(Path.Combine(destDirPath, "oldfile.txt"))); // the old file should be gone
        }

        [Fact]
        public async Task CopyAsync_OnConflict_InvokesResolverWithCorrectDestinationAndKind()
        {
            var filePath = Path.Combine(_tempDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "hello", TestContext.Current.CancellationToken);
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);
            await File.WriteAllTextAsync(Path.Combine(destFolder, "test.txt"), "world", TestContext.Current.CancellationToken);

            StoragePath? seenPath = null;
            StorageItemKind? seenKind = null;

            await _provider.CopyAsync(PathFor(filePath), PathFor(destFolder), (path, kind, _) =>
            {
                seenPath = path;
                seenKind = kind;
                return Task.FromResult(NameCollisionPolicy.Skip);
            }, ct:TestContext.Current.CancellationToken);

            Assert.Equal("test.txt", seenPath!.Name);
            Assert.Equal(StorageItemKind.File, seenKind);
        }

        [Fact]
        public async Task CopyAsync_Folder_CopiesNestedContents_AndLeavesSourceIntact()
        {
            var ct = TestContext.Current.CancellationToken;
            var src = await Provider.CreateDirectoryAsync(TestRoot, "src", ct: ct);
            var sub = await Provider.CreateDirectoryAsync(src.Path, "sub", ct: ct);
            var bytes = new byte[] { 1, 2, 3 };
            await using (var s = await Provider.OpenWriteAsync(sub.Path.Combine("a.bin"), ct))
                await s.WriteAsync(bytes, ct);
            var dest = await Provider.CreateDirectoryAsync(TestRoot, "dest", ct: ct);

            ConflictResolver resolver = (_, _, _) => throw new InvalidOperationException("No conflict expected");

            await Provider.CopyAsync(src.Path, dest.Path, resolver, ct: ct);

            var copied = dest.Path.Combine("src").Combine("sub").Combine("a.bin");
            Assert.Equal(bytes, await ReadAllAsync(copied, ct));                     // nested file arrived intact
            Assert.Equal(bytes, await ReadAllAsync(sub.Path.Combine("a.bin"), ct));  // source untouched
        }

        private async Task<byte[]> ReadAllAsync(StoragePath path, CancellationToken ct)
        {
            await using var stream = await Provider.OpenReadAsync(path, ct);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, ct);
            return memory.ToArray();
        }

    }
}
