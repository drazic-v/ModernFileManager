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
        public async Task MoveAsync_OnExistingDestination_GeneratesUniqueName()
        {
            var filePath = Path.Combine(_tempDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "hello");
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);
            await File.WriteAllTextAsync(Path.Combine(destFolder, "test.txt"), "world");
            var item = await _provider.MoveAsync(PathFor(filePath), PathFor(destFolder));
            Assert.Equal(StorageItemKind.File, item.Kind);
            Assert.Equal("test (2).txt", item.Name);
            Assert.True(File.Exists(LocalStorageProvider.ToNativePath(item.Path.Value)));
            Assert.False(File.Exists(filePath)); // source should be gone
        }

        [Fact]
        public async Task MoveAsync_OnNonExistingSource_ThrowsFileNotFoundException()
        {
            var nonExistingPath = Path.Combine(_tempDir, "nonexistent.txt");
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);
            await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            {
                await _provider.MoveAsync(PathFor(nonExistingPath), PathFor(destFolder));
            });
        }

        [Fact]
        public async Task MoveAsync_OnNonExistingDestinationFolder_ThrowsDirectoryNotFoundException()
        {
            var filePath = Path.Combine(_tempDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "hello");
            var nonExistingDestFolder = Path.Combine(_tempDir, "nonexistent");
            await Assert.ThrowsAsync<DirectoryNotFoundException>(async () =>
            {
                await _provider.MoveAsync(PathFor(filePath), PathFor(nonExistingDestFolder));
            });
        }

        [Fact]
        public async Task MoveAsync_OnDirectoryWithExistingDestination_GeneratesUniqueName()
        {
            var dirPath = Path.Combine(_tempDir, "subfolder");
            Directory.CreateDirectory(dirPath);
            await File.WriteAllTextAsync(Path.Combine(dirPath, "file.txt"), "hello");
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);
            Directory.CreateDirectory(Path.Combine(destFolder, "subfolder")); // existing destination
            var item = await _provider.MoveAsync(PathFor(dirPath), PathFor(destFolder));
            Assert.Equal(StorageItemKind.Directory, item.Kind);
            Assert.Equal("subfolder (2)", item.Name);
            var movedDirPath = LocalStorageProvider.ToNativePath(item.Path.Value);
            Assert.True(Directory.Exists(movedDirPath));
            Assert.True(File.Exists(Path.Combine(movedDirPath, "file.txt")));
            Assert.False(File.Exists(dirPath)); // source should be gone
        }

        [Fact]
        public async Task MoveAsync_OnDirectoryWithNestedContents_CopiesAllContents()
        {
            var dirPath = Path.Combine(_tempDir, "subfolder");
            Directory.CreateDirectory(dirPath);
            var nestedDirPath = Path.Combine(dirPath, "nested");
            Directory.CreateDirectory(nestedDirPath);
            await File.WriteAllTextAsync(Path.Combine(nestedDirPath, "deep.txt"), "world");
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);
            var item = await _provider.MoveAsync(PathFor(dirPath), PathFor(destFolder));
            Assert.Equal(StorageItemKind.Directory, item.Kind);
            Assert.Equal("subfolder", item.Name);
            var movedDirPath = LocalStorageProvider.ToNativePath(item.Path.Value);
            Assert.True(Directory.Exists(movedDirPath));
            Assert.True(Directory.Exists(Path.Combine(movedDirPath, "nested")));
            Assert.True(File.Exists(Path.Combine(movedDirPath, "nested", "deep.txt")));
            Assert.False(File.Exists(dirPath)); // source should be gone
        }

        [Fact]
        public async Task MoveAsync_OnDirectoryNameWithDot_DoesNotMangleExtension()
        {
            var dirPath = Path.Combine(_tempDir, "Client Files v2.1");
            Directory.CreateDirectory(dirPath);
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);
            Directory.CreateDirectory(Path.Combine(destFolder, "Client Files v2.1"));

            var item = await _provider.MoveAsync(PathFor(dirPath), PathFor(destFolder));

            Assert.Equal("Client Files v2.1 (2)", item.Name);
            Assert.False(File.Exists(dirPath)); // source should be gone
        }

        [Fact]
        public async Task MoveAsync_OnReplaceRequest_ReplacesTheRequiredFile()
        {
            var filePath = Path.Combine(_tempDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "hello");
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);
            var destFilePath = Path.Combine(destFolder, "test.txt");
            await File.WriteAllTextAsync(destFilePath, "world");
            var item = await _provider.MoveAsync(PathFor(filePath), PathFor(destFolder), NameCollisionPolicy.Replace);
            Assert.Equal("test.txt", item.Name);
            Assert.True(File.Exists(destFilePath));
            var content = await File.ReadAllTextAsync(destFilePath);
            Assert.Equal("hello", content); // the destination file should now contain the source file's content
            Assert.False(File.Exists(filePath)); // source should be gone

        }

        [Fact]
        public async Task MoveAsync_OnReplaceRequestForDirectory_ReplacesTheRequiredDirectory()
        {
            var dirPath = Path.Combine(_tempDir, "subfolder");
            Directory.CreateDirectory(dirPath);
            await File.WriteAllTextAsync(Path.Combine(dirPath, "file.txt"), "hello");
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);
            var destDirPath = Path.Combine(destFolder, "subfolder");
            Directory.CreateDirectory(destDirPath);
            await File.WriteAllTextAsync(Path.Combine(destDirPath, "oldfile.txt"), "world");
            var item = await _provider.MoveAsync(PathFor(dirPath), PathFor(destFolder), NameCollisionPolicy.Replace);
            Assert.Equal("subfolder", item.Name);
            Assert.True(Directory.Exists(destDirPath));
            Assert.True(File.Exists(Path.Combine(destDirPath, "file.txt")));
            Assert.False(File.Exists(Path.Combine(destDirPath, "oldfile.txt"))); // the old file should be gone
            Assert.False(File.Exists(dirPath)); // source should be gone
        }

        [Fact]
        public async Task MoveAsync_OnConflict_InvokesResolverWithCorrectDestinationAndKind()
        {
            var filePath = Path.Combine(_tempDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "hello");
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);
            await File.WriteAllTextAsync(Path.Combine(destFolder, "test.txt"), "world");

            StoragePath? seenPath = null;
            StorageItemKind? seenKind = null;

            await _provider.MoveAsync(PathFor(filePath), PathFor(destFolder), (path, kind, _) =>
            {
                seenPath = path;
                seenKind = kind;
                return Task.FromResult(NameCollisionPolicy.Skip);
            });

            Assert.Equal("test.txt", seenPath!.Name);
            Assert.Equal(StorageItemKind.File, seenKind);
        }

        [Fact]
        public async Task MoveAsync_OnMergeWithSkippedFile_PreservesSkippedFileAndMovesTheRest()
        {
            var dirPath = Path.Combine(_tempDir, "subfolder");
            Directory.CreateDirectory(dirPath);
            await File.WriteAllTextAsync(Path.Combine(dirPath, "keep.txt"), "mine");
            await File.WriteAllTextAsync(Path.Combine(dirPath, "moveme.txt"), "move this");

            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);
            var destDirPath = Path.Combine(destFolder, "subfolder");
            Directory.CreateDirectory(destDirPath);
            await File.WriteAllTextAsync(Path.Combine(destDirPath, "keep.txt"), "theirs");

            await _provider.MoveAsync(PathFor(dirPath), PathFor(destFolder), (path, kind, _) =>
                Task.FromResult(kind == StorageItemKind.Directory
                    ? NameCollisionPolicy.Merge
                    : path.Name == "keep.txt" ? NameCollisionPolicy.Skip : NameCollisionPolicy.GenerateUnique));

            Assert.True(File.Exists(Path.Combine(dirPath, "keep.txt")), "skipped file must survive in source, not be deleted");
            Assert.Equal("theirs", await File.ReadAllTextAsync(Path.Combine(destDirPath, "keep.txt")));
            Assert.True(File.Exists(Path.Combine(destDirPath, "moveme.txt")));
        }
    }
}
