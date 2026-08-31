using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.Infrastructure.Tests.Providers
{
    public partial class LocalStorageProviderTests
    {
        [Fact]
        public async Task OpenReadAsync_OnExistingFile_ReturnsReadableStreamWithCorrectContent()
        {
            var filePath = Path.Combine(_tempDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "hello");

            await using var stream = await _provider.OpenReadAsync(PathFor(filePath));
            using var reader = new StreamReader(stream);

            Assert.Equal("hello",await reader.ReadToEndAsync());
        }

        [Fact]
        public async Task OpenReadAsync_OnDirectory_ThrowsArgumentException()
        {
            var dirPath = Path.Combine(_tempDir, "subfolder");
            Directory.CreateDirectory(dirPath);

            await Assert.ThrowsAsync<ArgumentException>(() => _provider.OpenReadAsync(PathFor(dirPath)));
        }

        [Fact]
        public async Task OpenReadAsync_OnNonExistingPath_ThrowsFileNotFoundException()
        {
            var nonExistingPath = Path.Combine(_tempDir, "nonexistent.txt");

            await Assert.ThrowsAsync<FileNotFoundException>(() => _provider.OpenReadAsync(PathFor(nonExistingPath)));
        }
    }
}
