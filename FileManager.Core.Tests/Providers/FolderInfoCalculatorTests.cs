using FileManager.Core.Models;
using FileManager.Core.Providers;
using FileManager.TestKit;
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

            var result = await FolderInfoCalculator.GetFolderInfo(provider, folder, ct: TestContext.Current.CancellationToken);

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

            var result = await FolderInfoCalculator.GetFolderInfo(provider, folder, ct: TestContext.Current.CancellationToken);

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

            var result = await FolderInfoCalculator.GetFolderInfo(provider, root, ct: TestContext.Current.CancellationToken);

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

            var result = await FolderInfoCalculator.GetFolderInfo(provider, folder, ct:TestContext.Current.CancellationToken);

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

        private static readonly StoragePath Root = new() { ProviderId = "fake", Value = "/root" };

        [Fact]
        public async Task GetFolderInfo_UnreadableSubfolder_IsSkippedAndCounted_RestStillMeasured()
        {
            var ct = TestContext.Current.CancellationToken;
            var provider = new FakeStorageProvider();

            provider.AddChildren(Root.Value, MakeFile(Root, "a.bin", 3));
            var ok = await provider.CreateDirectoryAsync(Root, "ok", ct: ct);
            provider.AddChildren(ok.Path.Value, MakeFile(ok.Path, "b.bin", 5));
            var denied = await provider.CreateDirectoryAsync(Root, "denied", ct: ct);
            provider.AddChildren(denied.Path.Value, MakeFile(denied.Path, "hidden.bin", 100));
            provider.ThrowOnList(denied.Path.Value, new UnauthorizedAccessException("Simulated exception"));

            var info = await FolderInfoCalculator.GetFolderInfo(provider, Root, ct: ct);

            Assert.Equal(1, info.Skipped);
            Assert.Equal(2, info.Folders);   // "ok" + "denied": skipped folders still count as folders
            Assert.Equal(2, info.Files);     // a.bin + b.bin, NOT hidden.bin
            Assert.Equal(8, info.Size);      
        }

        [Fact]
        public async Task GetFolderInfo_UnreadableTopLevelFolder_Throws()
        {
            var ct = TestContext.Current.CancellationToken;
            var provider = new FakeStorageProvider();
            var denied = await provider.CreateDirectoryAsync(Root, "denied", ct: ct);
            provider.ThrowOnList(denied.Path.Value, new UnauthorizedAccessException("Simulated exception"));

            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => FolderInfoCalculator.GetFolderInfo(provider, denied.Path, ct: ct));
        }

        [Fact]
        public async Task GetFolderInfo_Cancellation_IsNotSwallowedBySkipLogic()
        {
            var provider = new FakeStorageProvider();
            var sub = await provider.CreateDirectoryAsync(Root, "sub", ct: TestContext.Current.CancellationToken);
            provider.AddChildren(sub.Path.Value, MakeFile(sub.Path, "b.bin", 5));

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => FolderInfoCalculator.GetFolderInfo(provider, Root, ct: cts.Token));
        }
    }
}
