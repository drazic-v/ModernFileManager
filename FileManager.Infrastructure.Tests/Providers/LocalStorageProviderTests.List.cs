using FileManager.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.Infrastructure.Tests.Providers
{
    public partial class LocalStorageProviderTests
    {
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
    }
}
