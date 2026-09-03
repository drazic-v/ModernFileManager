using FileManager.Core.Models;
using FileManager.Core.Providers;
using FileManager.Infrastructure.Providers;
using FileManager.TestKit;
using Newtonsoft.Json.Linq;

namespace FileManager.Infrastructure.Tests.Providers
{
    /// <summary>
    /// Used to be the main file for testing LocalStorageProvider
    /// Split up into different files as a partial class to ensure
    /// ease of use and access
    /// </summary>
    public partial class LocalStorageProviderTests : StorageProviderContractTests
    {
        private string _tempDir = null!;
        private LocalStorageProvider _provider => (LocalStorageProvider)Provider;

        protected override Task<(IStorageProvider, StoragePath)> CreateProviderAsync()
        {
            _tempDir = Directory.CreateTempSubdirectory().FullName;
            var root = new StoragePath { ProviderId = "local", Value = _tempDir.Replace('\\', '/') };
            return Task.FromResult(((IStorageProvider)new LocalStorageProvider(), root));
        }

        protected override Task CleanupAsync(StoragePath _)
        {
            Directory.Delete(_tempDir, recursive: true);
            return Task.CompletedTask;
        }

        private static StoragePath PathFor(string nativePath) =>
        new StoragePath { ProviderId = "local", Value = nativePath.Replace('\\', '/') };
    }
}