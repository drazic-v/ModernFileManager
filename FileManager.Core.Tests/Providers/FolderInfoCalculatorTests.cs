using FileManager.Core.Models;
using FileManager.Core.Providers;
using FileManager.Core.Tests.Fakes;
using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.Core.Tests.Providers
{
    public class FolderInfoCalculatorTests
    {
        private static StorageItem MakeFile(StoragePath parent, string name, long size) =>
        new() { Path = parent.Combine(name), Name = name, Kind = StorageItemKind.File, SizeInBytes = size };

        private static StorageItem MakeFolder(StoragePath parent, string name) =>
            new() { Path = parent.Combine(name), Name = name, Kind = StorageItemKind.Directory };

        [Fact]
        public async Task GetFolderInfo_OnEmptyFolder_ReturnsAllZeros()
        {
            var provider = new FakeStorageProvider();
            var folder = new StoragePath { ProviderId = "fake", Value = "/root" };

            var result = await FolderInfoCalculator.GetFolderInfo(provider, folder);

            Assert.Equal(0, result.Size);
            Assert.Equal(0, result.Files);
            Assert.Equal(0, result.Folders);
        }

        [Fact]
        public async Task GetFolderInfo_WithFilesOnly_SumsSizesAndCountsFiles()
        {
            var provider = new FakeStorageProvider();
            var folder = new StoragePath { ProviderId = "fake", Value = "/root" };
            provider.AddChildren("/root",
                MakeFile(folder, "a.txt", 10),
                MakeFile(folder, "b.txt", 20));

            var result = await FolderInfoCalculator.GetFolderInfo(provider, folder);

            Assert.Equal(30, result.Size);
            Assert.Equal(2, result.Files);
            Assert.Equal(0, result.Folders);
        }

        [Fact]
        public async Task GetFolderInfo_WithNestedSubfolder_RecursesAndAccumulatesAcrossLevels()
        {
            var provider = new FakeStorageProvider();
            var root = new StoragePath { ProviderId = "fake", Value = "/root" };
            var sub = root.Combine("sub");

            provider.AddChildren("/root",
                MakeFile(root, "a.txt", 10),
                MakeFolder(root, "sub"));
            provider.AddChildren(sub.Value,
                MakeFile(sub, "b.txt", 20));

            var result = await FolderInfoCalculator.GetFolderInfo(provider, root);

            Assert.Equal(30, result.Size);
            Assert.Equal(2, result.Files);
            Assert.Equal(1, result.Folders); // counts "sub" itself, found one level down
        }

        [Fact]
        public async Task GetFolderInfo_WhenFileSizeIsNull_TreatsItAsZeroWithoutThrowing()
        {
            var provider = new FakeStorageProvider();
            var folder = new StoragePath { ProviderId = "fake", Value = "/root" };
            provider.AddChildren("/root",
                new StorageItem { Path = folder.Combine("mystery"), Name = "mystery", Kind = StorageItemKind.File, SizeInBytes = null });

            var result = await FolderInfoCalculator.GetFolderInfo(provider, folder);

            Assert.Equal(0, result.Size);
            Assert.Equal(1, result.Files);
        }

        [Fact]
        public async Task GetFolderInfo_WithCancelledToken_ThrowsOperationCanceledException()
        {
            var provider = new FakeStorageProvider();
            var folder = new StoragePath { ProviderId = "fake", Value = "/root" };
            provider.AddChildren("/root", MakeFile(folder, "a.txt", 10)); // needs >=1 item, or the loop body - where the check lives - never runs
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                FolderInfoCalculator.GetFolderInfo(provider, folder, null, cts.Token));
        }
    }
}
