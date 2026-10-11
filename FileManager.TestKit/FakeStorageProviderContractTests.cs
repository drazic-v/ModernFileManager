using FileManager.Core.Models;
using FileManager.Core.Providers;
using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.TestKit
{
    public class FakeStorageProviderContractTests : StorageProviderContractTests
    {
        protected override Task<(IStorageProvider, StoragePath)> CreateProviderAsync()
        {
            var root = new StoragePath { ProviderId = "fake", Value = "/root" };
            return Task.FromResult(((IStorageProvider)new FakeStorageProvider(), root));
        }

        protected override Task CleanupAsync(StoragePath testRoot) => Task.CompletedTask;
    }
}
