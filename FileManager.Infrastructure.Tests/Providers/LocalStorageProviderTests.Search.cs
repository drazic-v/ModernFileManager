using FileManager.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.Infrastructure.Tests.Providers
{
    public partial class LocalStorageProviderTests
    {
        [Fact]
        public void SearchAsync_OnNonExistingRoot_ThrowsImmediatelyWithoutEnumeration()
        {
            var nonExistingPath = Path.Combine(_tempDir, "nonexistent");

            Assert.Throws<ArgumentException>(() => _provider.SearchAsync(PathFor(nonExistingPath), "query"));
            // deliberately no await, no foreach - this is exactly the eager-validation fix being checked
        }

        [Fact]
        public async Task SearchAsync_FindsMatchAtTopLevel()
        {
            await File.WriteAllTextAsync(Path.Combine(_tempDir, "photo.jpg"), "data");
            await File.WriteAllTextAsync(Path.Combine(_tempDir, "notes.txt"), "data");

            var results = new List<StorageItem>();
            await foreach (var item in _provider.SearchAsync(PathFor(_tempDir), "photo"))
                results.Add(item);

            Assert.Single(results);
            Assert.Equal("photo.jpg", results[0].Name);
        }

        [Fact]
        public async Task SearchAsync_IsCaseInsensitive()
        {
            await File.WriteAllTextAsync(Path.Combine(_tempDir, "photo.jpg"), "data");

            var results = new List<StorageItem>();
            await foreach (var item in _provider.SearchAsync(PathFor(_tempDir), "PHOTO"))
                results.Add(item);

            Assert.Single(results);
        }

        [Fact]
        public async Task SearchAsync_FindsMatchInNestedSubfolder()
        {
            var subDir = Path.Combine(_tempDir, "vacation");
            Directory.CreateDirectory(subDir);
            await File.WriteAllTextAsync(Path.Combine(subDir, "beach-photo.jpg"), "data");

            var results = new List<StorageItem>();
            await foreach (var item in _provider.SearchAsync(PathFor(_tempDir), "photo"))
                results.Add(item);

            Assert.Single(results);
            Assert.Equal("beach-photo.jpg", results[0].Name);
        }

        [Fact]
        public async Task SearchAsync_OnMatchingFolder_ReturnsFolderAndStillSearchesInsideIt()
        {
            var photosDir = Path.Combine(_tempDir, "photos");
            Directory.CreateDirectory(photosDir);
            await File.WriteAllTextAsync(Path.Combine(photosDir, "photo-1.jpg"), "data");

            var results = new List<StorageItem>();
            await foreach (var item in _provider.SearchAsync(PathFor(_tempDir), "photo"))
                results.Add(item);

            Assert.Equal(2, results.Count); // the "photos" folder itself, and "photo-1.jpg" inside it
            Assert.Contains(results, r => r.Name == "photos" && r.Kind == StorageItemKind.Directory);
            Assert.Contains(results, r => r.Name == "photo-1.jpg" && r.Kind == StorageItemKind.File);
        }

        [Fact]
        public async Task SearchAsync_WithNoMatches_ReturnsEmpty()
        {
            await File.WriteAllTextAsync(Path.Combine(_tempDir, "notes.txt"), "data");

            var results = new List<StorageItem>();
            await foreach (var item in _provider.SearchAsync(PathFor(_tempDir), "nonexistentquery"))
                results.Add(item);

            Assert.Empty(results);
        }
    }
}
