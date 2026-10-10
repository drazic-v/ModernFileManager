using FileManager.Core.Models;
using FileManager.Core.Providers;
using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.TestKit
{
    /// <summary>
    /// Behavioral contract every IStorageProvider must satisfy, regardless of
    /// what's underneath it. A concrete provider's test class only supplies
    /// how to create and tear down a provider and an empty starting folder -
    /// every test here already knows what "correct" looks like.
    /// </summary>
    public abstract class StorageProviderContractTests : IAsyncLifetime
    {
        protected IStorageProvider Provider { get; private set; } = null!;
        protected StoragePath TestRoot { get; private set; } = null!;

        protected abstract Task<(IStorageProvider provider, StoragePath testRoot)> CreateProviderAsync();
        protected abstract Task CleanupAsync(StoragePath testRoot);

        public async ValueTask InitializeAsync() => (Provider, TestRoot) = await CreateProviderAsync();
        public async ValueTask DisposeAsync() => await CleanupAsync(TestRoot);

        [Fact]
        public async Task ListAsync_ReturnsOnlyDirectContents_NeverNestedItems()
        {
            var sub = await Provider.CreateDirectoryAsync(TestRoot, "sub", ct:TestContext.Current.CancellationToken);
            await using (await Provider.OpenWriteAsync(sub.Path.Combine("nested.txt"), TestContext.Current.CancellationToken)) { }

            var names = new List<string>();
            await foreach (var item in Provider.ListAsync(TestRoot, TestContext.Current.CancellationToken))
                names.Add(item.Name);

            Assert.Contains("sub", names);
            Assert.DoesNotContain("nested.txt", names);
        }

        [Fact]
        public async Task RenameAsync_ToSameName_DoesNotChangeTheItem()
        {
            var created = await Provider.CreateDirectoryAsync(TestRoot, "folder", ct:TestContext.Current.CancellationToken);
            var renamed = await Provider.RenameAsync(created.Path, "folder", ct:TestContext.Current.CancellationToken);
            Assert.Equal(created.Path, renamed.Path);
        }

        [Fact]
        public async Task CopyAsync_LeavesSourceIntact()
        {
            var created = await Provider.CreateDirectoryAsync(TestRoot, "folder", ct:TestContext.Current.CancellationToken);
            var dest = await Provider.CreateDirectoryAsync(TestRoot, "dest", ct:TestContext.Current.CancellationToken);

            ConflictResolver resolver = async (destinationPath, conflictingKind, ct) =>
            { return NameCollisionPolicy.Skip; };
            await Provider.CopyAsync(created.Path, dest.Path, resolver, null, TestContext.Current.CancellationToken);

            var names = new List<string>();
            await foreach (var item in Provider.ListAsync(TestRoot, TestContext.Current.CancellationToken))
                names.Add(item.Name);
            Assert.Contains("folder", names);
        }

        [Fact]
        public void ProviderId_IsNotNullOrEmpty() =>
    Assert.False(string.IsNullOrWhiteSpace(Provider.ProviderId));

        [Fact]
        public async Task CreateDirectoryAsync_ThenListAsync_IncludesTheNewDirectory()
        {
            await Provider.CreateDirectoryAsync(TestRoot, "newfolder", ct:TestContext.Current.CancellationToken);

            var names = new List<string>();
            await foreach (var item in Provider.ListAsync(TestRoot, TestContext.Current.CancellationToken))
                names.Add(item.Name);

            Assert.Contains("newfolder", names);
        }

        [Fact]
        public async Task DeleteAsync_RemovesItemFromParentListing()
        {
            var created = await Provider.CreateDirectoryAsync(TestRoot, "temp", ct:TestContext.Current.CancellationToken);
            await Provider.DeleteAsync(created.Path, TestContext.Current.CancellationToken);

            var names = new List<string>();
            await foreach (var item in Provider.ListAsync(TestRoot, TestContext.Current.CancellationToken))
                names.Add(item.Name);

            Assert.DoesNotContain("temp", names);
        }

        [Fact]
        public async Task WriteThenRead_RoundTripsExactBytes()
        {
            var filePath = TestRoot.Combine("data.bin");
            var written = new byte[] { 1, 2, 3, 4, 5 };

            await using (var writeStream = await Provider.OpenWriteAsync(filePath, TestContext.Current.CancellationToken))
                await writeStream.WriteAsync(written, TestContext.Current.CancellationToken);

            await using var readStream = await Provider.OpenReadAsync(filePath, TestContext.Current.CancellationToken);
            using var memory = new MemoryStream();
            await readStream.CopyToAsync(memory, TestContext.Current.CancellationToken);

            Assert.Equal(written, memory.ToArray());
        }

        [Fact]
        public async Task ListAsync_WithAlreadyCancelledToken_ThrowsOperationCanceledException()
        {
            await Provider.CreateDirectoryAsync(TestRoot, "temp", ct:TestContext.Current.CancellationToken); // needs >=1 item - same reason as the FolderSizeCalculator cancellation test
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in Provider.ListAsync(TestRoot, cts.Token)) { }
            });
        }
        private async Task<List<StorageItem>> ListRootAsync()
        {
            var items = new List<StorageItem>();
            await foreach (var item in Provider.ListAsync(TestRoot))
                items.Add(item);
            return items;
        }

        [Fact]
        public async Task CreateDirectoryAsync_Merge_WhenFolderExists_ReturnsSameFolderAndCreatesNoSibling()
        {
            var existing = await Provider.CreateDirectoryAsync(TestRoot, "folder", ct:TestContext.Current.CancellationToken);

            var merged = await Provider.CreateDirectoryAsync(TestRoot, "folder", NameCollisionPolicy.Merge, ct:TestContext.Current.CancellationToken);

            Assert.Equal(existing.Path, merged.Path);
            var dirs = (await ListRootAsync()).Where(i => i.Kind == StorageItemKind.Directory).ToList();
            Assert.Single(dirs); // the bug: an empty "folder (2)" appeared here
        }

        [Fact]
        public async Task CreateDirectoryAsync_Merge_WhenFolderExists_KeepsItsContents()
        {
            var existing = await Provider.CreateDirectoryAsync(TestRoot, "folder", ct:TestContext.Current.CancellationToken);
            await using (await Provider.OpenWriteAsync(existing.Path.Combine("keep.txt"), TestContext.Current.CancellationToken)) { }

            var merged = await Provider.CreateDirectoryAsync(TestRoot, "folder", NameCollisionPolicy.Merge, ct:TestContext.Current.CancellationToken);

            var names = new List<string>();
            await foreach (var item in Provider.ListAsync(merged.Path, TestContext.Current.CancellationToken))
                names.Add(item.Name);
            Assert.Contains("keep.txt", names);
        }

        [Fact]
        public async Task CreateDirectoryAsync_Merge_WhenFolderMissing_CreatesIt()
        {
            var created = await Provider.CreateDirectoryAsync(TestRoot, "fresh", NameCollisionPolicy.Merge, ct:TestContext.Current.CancellationToken);

            Assert.Equal("fresh", created.Name);
            Assert.Contains("fresh", (await ListRootAsync()).Select(i => i.Name));
        }

        [Fact]
        public async Task CreateDirectoryAsync_DefaultPolicy_WhenFolderExists_CreatesSecondFolderWithDifferentName()
        {
            var first = await Provider.CreateDirectoryAsync(TestRoot, "folder", ct:TestContext.Current.CancellationToken);

            var second = await Provider.CreateDirectoryAsync(TestRoot, "folder", ct:TestContext.Current.CancellationToken); // no policy: must stay GenerateUnique

            Assert.NotEqual(first.Path, second.Path);
            var dirs = (await ListRootAsync()).Where(i => i.Kind == StorageItemKind.Directory).ToList();
            Assert.Equal(2, dirs.Count);
        }

        [Theory]
        [InlineData(NameCollisionPolicy.Replace)]
        [InlineData(NameCollisionPolicy.Skip)]
        [InlineData(NameCollisionPolicy.Fail)]
        public async Task CreateDirectoryAsync_UnsupportedPolicy_Throws(NameCollisionPolicy policy)
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => Provider.CreateDirectoryAsync(TestRoot, "folder", policy, ct:TestContext.Current.CancellationToken));

            Assert.Empty(await ListRootAsync()); // and nothing was created on the way out
        }
    }
}
