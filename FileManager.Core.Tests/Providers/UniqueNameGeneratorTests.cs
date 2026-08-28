using FileManager.Core.Models;
using FileManager.Core.Providers;
using FileManager.Core.Tests.Fakes;
using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.Core.Tests.Providers
{
    public class UniqueNameGeneratorTests
    {
        private static StorageItem MakeItem(StoragePath parent, string name, StorageItemKind kind) =>
            new() { Path = parent.Combine(name), Name = name, Kind = kind };

        [Fact]
        public async Task GenerateAsync_WhenNameFree_ReturnsNameUnchanged()
        {
            var provider = new FakeStorageProvider();
            var parent = new StoragePath { ProviderId = "fake", Value = "/root" };

            var result = await UniqueNameGenerator.GenerateAsync(provider, parent, "newfolder", StorageItemKind.Directory);

            Assert.Equal("newfolder", result);
        }

        [Fact]
        public async Task GenerateAsync_WhenFirstAlternativeAlsoTaken_SkipsToNextNumber()
        {
            var provider = new FakeStorageProvider();
            var parent = new StoragePath { ProviderId = "fake", Value = "/root" };
            provider.AddChildren("/root",
                MakeItem(parent, "newfolder", StorageItemKind.Directory),
                MakeItem(parent, "newfolder (2)", StorageItemKind.Directory));

            var result = await UniqueNameGenerator.GenerateAsync(provider, parent, "newfolder", StorageItemKind.Directory);

            Assert.Equal("newfolder (3)", result);
        }

        [Fact]
        public async Task GenerateAsync_OnFileWithExtension_KeepsExtensionOnce()
        {
            var provider = new FakeStorageProvider();
            var parent = new StoragePath { ProviderId = "fake", Value = "/root" };
            provider.AddChildren("/root", MakeItem(parent, "report.pdf", StorageItemKind.File));

            var result = await UniqueNameGenerator.GenerateAsync(provider, parent, "report.pdf", StorageItemKind.File);

            Assert.Equal("report (2).pdf", result);
        }

        [Fact]
        public async Task GenerateAsync_WhenDifferentKindItemHasSameName_StillDetectsCollision()
        {
            var provider = new FakeStorageProvider();
            var parent = new StoragePath { ProviderId = "fake", Value = "/root" };
            provider.AddChildren("/root", MakeItem(parent, "notes", StorageItemKind.Directory));

            var result = await UniqueNameGenerator.GenerateAsync(provider, parent, "notes", StorageItemKind.File);

            Assert.Equal("notes (2)", result);
        }
    }
}
