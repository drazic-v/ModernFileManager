using FileManager.Core.Models;
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
    }
}