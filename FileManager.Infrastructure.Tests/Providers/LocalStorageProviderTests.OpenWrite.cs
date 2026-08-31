using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.Infrastructure.Tests.Providers
{
    public partial class LocalStorageProviderTests
    {
        [Fact]
        public async Task OpenWriteAsync_OnNewFile_CreatesFileWithWrittenContent()
        {
            var filePath = Path.Combine(_tempDir, "newfile.txt");
            await using (var stream = await _provider.OpenWriteAsync(PathFor(filePath)))
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync("world");
            }

            Assert.Equal("world", await File.ReadAllTextAsync(filePath));
        }

        [Fact]
        public async Task OpenWriteAsync_OnExistingFile_TruncatesOldContentEntirely()
        {
            var filePath = Path.Combine(_tempDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "old content that is much longer than the new content");
            
            await using (var stream = await _provider.OpenWriteAsync(PathFor(filePath)))
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync("new");
            }

            Assert.Equal("new", await File.ReadAllTextAsync(filePath));
        }

        [Fact]
        public async Task OpenWriteAsync_OnDirectory_ThrowsArgumentException()
        {
            var dirPath = Path.Combine(_tempDir, "subfolder");
            Directory.CreateDirectory(dirPath);

            await Assert.ThrowsAsync<ArgumentException>(() => _provider.OpenWriteAsync(PathFor(dirPath)));
        }
    }
}
