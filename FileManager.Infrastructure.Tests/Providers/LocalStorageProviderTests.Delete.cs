using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.Infrastructure.Tests.Providers
{
    public partial class LocalStorageProviderTests
    {
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
