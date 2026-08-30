using FileManager.Core.Models;
using FileManager.Core.Providers;
using FileManager.Infrastructure.Providers;
using Newtonsoft.Json.Linq;

namespace FileManager.Infrastructure.Tests.Providers
{
    public class LocalStorageProviderTests : IAsyncLifetime
    {
        private string _tempDir = null!;
        private LocalStorageProvider _provider = null!;

        public Task InitializeAsync()
        {
            _tempDir = Directory.CreateTempSubdirectory().FullName;
            _provider = new LocalStorageProvider();
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            Directory.Delete(_tempDir, true);
            return Task.CompletedTask;
        }

        private static StoragePath PathFor(string nativePath) =>
        new StoragePath { ProviderId = "local", Value = nativePath.Replace('\\', '/') };

        [Fact]
        public async Task GetInfoAsync_OnExistingFile_ReturnsFileKindAndCorrectSize()
        {
            var filePath = Path.Combine(_tempDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "hello");

            var item = await _provider.GetInfoAsync(PathFor(filePath));

            Assert.Equal(StorageItemKind.File, item.Kind);
            Assert.Equal(5, item.SizeInBytes);
            Assert.Equal("test.txt", item.Name);
        }

        [Fact]
        public async Task GetInfoAsync_OnExistingDirectory_ReturnsDirectoryKindWithTimestamps()
        {
            var dirPath = Path.Combine(_tempDir, "subfolder");
            Directory.CreateDirectory(dirPath);

            var item = await _provider.GetInfoAsync(PathFor(dirPath));

            Assert.Equal(StorageItemKind.Directory, item.Kind);
            Assert.Null(item.SizeInBytes);
            Assert.NotNull(item.LastModifiedUtc); // this is the assertion that would have caught the regression
        }


        [Fact]
        public async Task GetInfoAsync_PreservesTheOriginalPath()
        {
            var filePath = Path.Combine(_tempDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "hello");
            var path = PathFor(filePath);

            var item = await _provider.GetInfoAsync(path);

            Assert.Equal(path, item.Path); // StoragePath is a record - value equality, not reference equality
        }

        [Fact]
        public async Task GetInfoAsync_OnKnownExtension_ReturnsExpectedContentType()
        {
            var filePath = Path.Combine(_tempDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "hello");

            var item = await _provider.GetInfoAsync(PathFor(filePath));

            Assert.Equal("text/plain", item.ContentType);
        }

        [Fact]
        public async Task ListAsync_OnDirectoryWithFilesAndSubdirectories_ReturnsAllItems()
        {
            var dirPath = Path.Combine(_tempDir, "subfolder");
            Directory.CreateDirectory(dirPath);
            var file1Path = Path.Combine(dirPath, "file1.txt");
            var file2Path = Path.Combine(dirPath, "file2.txt");
            await File.WriteAllTextAsync(file1Path, "hello");
            await File.WriteAllTextAsync(file2Path, "world");
            var items = new List<StorageItem>();
            await foreach (var item in _provider.ListAsync(PathFor(dirPath)))
            {
                items.Add(item);
            }
            Assert.Equal(2, items.Count);
            Assert.Contains(items, i => i.Name == "file1.txt" && i.Kind == StorageItemKind.File);
            Assert.Contains(items, i => i.Name == "file2.txt" && i.Kind == StorageItemKind.File);
        }

        [Fact]
        public async Task ListAsync_OnEmptyDirectory_ReturnsNoItems()
        {
            var dirPath = Path.Combine(_tempDir, "emptyfolder");
            Directory.CreateDirectory(dirPath);
            var items = new List<StorageItem>();
            await foreach (var item in _provider.ListAsync(PathFor(dirPath)))
            {
                items.Add(item);
            }
            Assert.Empty(items);
        }

        [Fact]
        public async Task CreateDirectoryAsync_OnValidParent_CreatesDirectory()
        {
            var parentPath = Path.Combine(_tempDir, "parent");
            Directory.CreateDirectory(parentPath);

            var item = await _provider.CreateDirectoryAsync(PathFor(parentPath), "newfolder");

            var newPath = item.Path;
            Assert.Equal(StorageItemKind.Directory, item.Kind);
            Assert.Equal(PathFor(Path.Combine(parentPath, "newfolder")), newPath);
            Assert.True(Directory.Exists(LocalStorageProvider.ToNativePath(newPath.Value)));
        }

        [Fact]
        public async Task CreateDirectoryAsync_OnExistingName_GeneratesUniqueName()
        {
            var parentPath = Path.Combine(_tempDir, "parent");
            Directory.CreateDirectory(parentPath);
            Directory.CreateDirectory(Path.Combine(parentPath, "newfolder"));

            var item = await _provider.CreateDirectoryAsync(PathFor(parentPath), "newfolder");

            var newPath = item.Path;
            Assert.Equal(StorageItemKind.Directory, item.Kind);
            Assert.Equal(PathFor(Path.Combine(parentPath, "newfolder (2)")), newPath);
            Assert.True(Directory.Exists(LocalStorageProvider.ToNativePath(newPath.Value)));
        }

        [Fact]
        public async Task DeleteAsync_OnExistingFile_DeletesFile()
        {
            var filePath = Path.Combine(_tempDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "hello");
            await _provider.DeleteAsync(PathFor(filePath));
            Assert.False(File.Exists(filePath));
        }

        [Fact]
        public async Task DeleteAsync_OnDirectoryWithContents_DeletesRecursively()
        {
            var dirPath = Path.Combine(_tempDir, "subfolder");
            Directory.CreateDirectory(dirPath);
            await File.WriteAllTextAsync(Path.Combine(dirPath, "file.txt"), "hello");
            Directory.CreateDirectory(Path.Combine(dirPath, "nested"));
            await File.WriteAllTextAsync(Path.Combine(dirPath, "nested", "deep.txt"), "world");

            await _provider.DeleteAsync(PathFor(dirPath));

            Assert.False(Directory.Exists(dirPath));
        }

        [Fact]
        public async Task DeleteAsync_OnNonExistingPath_DoesNotThrow()
        {
            var nonExistingPath = Path.Combine(_tempDir, "nonexistent");
            await _provider.DeleteAsync(PathFor(nonExistingPath));
            // No exception should be thrown
        }

        [Fact]
        public async Task RenameAsync_OnExistingFile_RenamesFile()
        {
            var filePath = Path.Combine(_tempDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "hello");
            var item = await _provider.RenameAsync(PathFor(filePath), "renamed.txt");
            Assert.Equal(StorageItemKind.File, item.Kind);
            Assert.Equal("renamed.txt", item.Name);
            Assert.True(File.Exists(LocalStorageProvider.ToNativePath(item.Path.Value)));
            Assert.False(File.Exists(filePath));
        }

        [Fact]
        public async Task RenameAsync_OnExistingDirectory_RenamesDirectory()
        {
            var dirPath = Path.Combine(_tempDir, "subfolder");
            Directory.CreateDirectory(dirPath);
            var item = await _provider.RenameAsync(PathFor(dirPath), "renamedfolder");
            Assert.Equal(StorageItemKind.Directory, item.Kind);
            Assert.Equal("renamedfolder", item.Name);
            Assert.True(Directory.Exists(LocalStorageProvider.ToNativePath(item.Path.Value)));
            Assert.False(Directory.Exists(dirPath));
        }

        [Fact]
        public async Task RenameAsync_OnExistingName_GeneratesUniqueName()
        {
            var dirPath = Path.Combine(_tempDir, "subfolder");
            Directory.CreateDirectory(dirPath);
            Directory.CreateDirectory(Path.Combine(_tempDir, "existingfolder"));
            var item = await _provider.RenameAsync(PathFor(dirPath), "existingfolder");
            Assert.Equal(StorageItemKind.Directory, item.Kind);
            Assert.Equal("existingfolder (2)", item.Name);
            Assert.True(Directory.Exists(LocalStorageProvider.ToNativePath(item.Path.Value)));
            Assert.False(Directory.Exists(dirPath));
        }

        [Fact]
        public async Task RenameAsync_OnNonExistingPath_ThrowsFileNotFoundException()
        {
            var nonExistingPath = Path.Combine(_tempDir, "nonexistent");
            await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            {
                await _provider.RenameAsync(PathFor(nonExistingPath), "newname");
            });
        }

        [Fact]
        public async Task RenameAsync_OnNonExistingParent_ThrowsDirectoryNotFoundException()
        {
            var nonExistingPath = Path.Combine(_tempDir, "nonexistent", "file.txt");
            await Assert.ThrowsAsync<DirectoryNotFoundException>(async () =>
            {
                await _provider.RenameAsync(PathFor(nonExistingPath), "newname");
            });
        }

        [Fact]
        public async Task RenameAsync_OnInvalidNewName_ThrowsArgumentException()
        {
            var filePath = Path.Combine(_tempDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "hello");
            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await _provider.RenameAsync(PathFor(filePath), "invalid/name");
            });
        }

        [Fact]
        public async Task RenameAsync_ToSameName_ReturnsInfoWithoutRenaming()
        {
            var filePath = Path.Combine(_tempDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "hello");

            var item = await _provider.RenameAsync(PathFor(filePath), "test.txt");

            Assert.Equal("test.txt", item.Name);
            Assert.True(File.Exists(filePath)); // still there under the original name, not bumped to "test (2).txt"
        }

        [Fact]
        public async Task CopyAsync_OnExistingDestination_GeneratesUniqueName()
        {
            var filePath = Path.Combine(_tempDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "hello");
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);
            await File.WriteAllTextAsync(Path.Combine(destFolder, "test.txt"), "world");
            var item = await _provider.CopyAsync(PathFor(filePath), PathFor(destFolder));
            Assert.Equal(StorageItemKind.File, item.Kind);
            Assert.Equal("test (2).txt", item.Name);
            Assert.True(File.Exists(LocalStorageProvider.ToNativePath(item.Path.Value)));
        }

        [Fact]
        public async Task CopyAsync_OnNonExistingSource_ThrowsFileNotFoundException()
        {
            var nonExistingPath = Path.Combine(_tempDir, "nonexistent.txt");
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);
            await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            {
                await _provider.CopyAsync(PathFor(nonExistingPath), PathFor(destFolder));
            });
        }

        [Fact]
        public async Task CopyAsync_OnNonExistingDestinationFolder_ThrowsDirectoryNotFoundException()
        {
            var filePath = Path.Combine(_tempDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "hello");
            var nonExistingDestFolder = Path.Combine(_tempDir, "nonexistent");
            await Assert.ThrowsAsync<DirectoryNotFoundException>(async () =>
            {
                await _provider.CopyAsync(PathFor(filePath), PathFor(nonExistingDestFolder));
            });
        }

        [Fact]
        public async Task CopyAsync_OnDirectoryWithExistingDestination_GeneratesUniqueName()
        {
            var dirPath = Path.Combine(_tempDir, "subfolder");
            Directory.CreateDirectory(dirPath);
            await File.WriteAllTextAsync(Path.Combine(dirPath, "file.txt"), "hello");
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);
            Directory.CreateDirectory(Path.Combine(destFolder, "subfolder")); // existing destination
            var item = await _provider.CopyAsync(PathFor(dirPath), PathFor(destFolder));
            Assert.Equal(StorageItemKind.Directory, item.Kind);
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
            await File.WriteAllTextAsync(Path.Combine(nestedDirPath, "deep.txt"), "world");
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);
            var item = await _provider.CopyAsync(PathFor(dirPath), PathFor(destFolder));
            Assert.Equal(StorageItemKind.Directory, item.Kind);
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
            await File.WriteAllTextAsync(filePath, "hello");
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);

            await _provider.CopyAsync(PathFor(filePath), PathFor(destFolder));

            Assert.True(File.Exists(filePath));
        }

        [Fact]
        public async Task CopyAsync_OnDirectory_LeavesSourceIntact()
        {
            var dirPath = Path.Combine(_tempDir, "subfolder");
            Directory.CreateDirectory(dirPath);
            await File.WriteAllTextAsync(Path.Combine(dirPath, "file.txt"), "hello");
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);

            await _provider.CopyAsync(PathFor(dirPath), PathFor(destFolder));

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

            var item = await _provider.CopyAsync(PathFor(dirPath), PathFor(destFolder));

            Assert.Equal("Client Files v2.1 (2)", item.Name);
        }

        [Fact]
        public async Task CopyAsync_OnReplaceRequest_ReplacesTheRequiredFile()
        {
            var filePath = Path.Combine(_tempDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "hello");
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);
            var destFilePath = Path.Combine(destFolder, "test.txt");
            await File.WriteAllTextAsync(destFilePath, "world");
            var item = await _provider.CopyAsync(PathFor(filePath), PathFor(destFolder), NameCollisionPolicy.Replace);
            Assert.Equal("test.txt", item.Name);
            Assert.True(File.Exists(destFilePath));
            var content = await File.ReadAllTextAsync(destFilePath);
            Assert.Equal("hello", content); // the destination file should now contain the source file's content
        }

        [Fact]
        public async Task CopyAsync_OnReplaceRequestForDirectory_ReplacesTheRequiredDirectory()
        {
            var dirPath = Path.Combine(_tempDir, "subfolder");
            Directory.CreateDirectory(dirPath);
            await File.WriteAllTextAsync(Path.Combine(dirPath, "file.txt"), "hello");
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);
            var destDirPath = Path.Combine(destFolder, "subfolder");
            Directory.CreateDirectory(destDirPath);
            await File.WriteAllTextAsync(Path.Combine(destDirPath, "oldfile.txt"), "world");
            var item = await _provider.CopyAsync(PathFor(dirPath), PathFor(destFolder), NameCollisionPolicy.Replace);
            Assert.Equal("subfolder", item.Name);
            Assert.True(Directory.Exists(destDirPath));
            Assert.True(File.Exists(Path.Combine(destDirPath, "file.txt")));
            Assert.False(File.Exists(Path.Combine(destDirPath, "oldfile.txt"))); // the old file should be gone
        }

        [Fact]
        public async Task CopyAsync_OnConflict_InvokesResolverWithCorrectDestinationAndKind()
        {
            var filePath = Path.Combine(_tempDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "hello");
            var destFolder = Path.Combine(_tempDir, "dest");
            Directory.CreateDirectory(destFolder);
            await File.WriteAllTextAsync(Path.Combine(destFolder, "test.txt"), "world");

            StoragePath? seenPath = null;
            StorageItemKind? seenKind = null;

            await _provider.CopyAsync(PathFor(filePath), PathFor(destFolder), (path, kind, _) =>
            {
                seenPath = path;
                seenKind = kind;
                return Task.FromResult(NameCollisionPolicy.Skip);
            });

            Assert.Equal("test.txt", seenPath!.Name);
            Assert.Equal(StorageItemKind.File, seenKind);
        }

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