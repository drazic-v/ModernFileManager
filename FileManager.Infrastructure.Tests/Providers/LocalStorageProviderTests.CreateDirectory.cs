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
    }
}
