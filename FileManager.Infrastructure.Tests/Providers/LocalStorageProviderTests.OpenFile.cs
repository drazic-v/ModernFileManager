using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.Infrastructure.Tests.Providers
{
    public partial class LocalStorageProviderTests
    {
        [Fact]
        public async Task OpenFileAsync_WithDirectory_ThrowsArgumentException()
        {
            var dirPath = Path.Combine(_tempDir, "subfolder");
            Directory.CreateDirectory(dirPath);
            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await _provider.OpenFileAsync(PathFor(dirPath));
            });
        }

        [Fact]
        public async Task OpenFileAsync_OnNonExistingSource_ThrowsFileNotFoundException()
        {
            var nonExistingPath = Path.Combine(_tempDir, "nonexistent.txt");
            await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            {
                await _provider.OpenFileAsync(PathFor(nonExistingPath));
            });
        }
    }
}
