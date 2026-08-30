using FileManager.Core.Models;
using FileManager.Infrastructure.Providers;
using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.Infrastructure.Tests.Providers
{
    public partial class LocalStorageProviderTests
    {
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
    }
}
