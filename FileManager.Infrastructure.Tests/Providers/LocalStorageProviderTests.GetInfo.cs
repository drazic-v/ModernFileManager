using FileManager.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.Infrastructure.Tests.Providers
{
    public partial class LocalStorageProviderTests
    {
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
    }
}
