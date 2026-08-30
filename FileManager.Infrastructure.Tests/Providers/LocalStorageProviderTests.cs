using FileManager.Core.Models;
using FileManager.Core.Providers;
using FileManager.Infrastructure.Providers;
using Newtonsoft.Json.Linq;

namespace FileManager.Infrastructure.Tests.Providers
{
    /// <summary>
    /// Used to be the main file for testing LocalStorageProvider
    /// Split up into different files as a partial class to ensure
    /// ease of use and access
    /// </summary>
    public partial class LocalStorageProviderTests : IAsyncLifetime
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
    }
}