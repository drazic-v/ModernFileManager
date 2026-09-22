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
            var sub = await Provider.CreateDirectoryAsync(TestRoot, "sub", TestContext.Current.CancellationToken);
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
            var created = await Provider.CreateDirectoryAsync(TestRoot, "folder", TestContext.Current.CancellationToken);
            var renamed = await Provider.RenameAsync(created.Path, "folder", TestContext.Current.CancellationToken);
            Assert.Equal(created.Path, renamed.Path);
        }

        [Fact]
        public async Task CopyAsync_LeavesSourceIntact()
        {
            var created = await Provider.CreateDirectoryAsync(TestRoot, "folder", TestContext.Current.CancellationToken);
            var dest = await Provider.CreateDirectoryAsync(TestRoot, "dest", TestContext.Current.CancellationToken);

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
            await Provider.CreateDirectoryAsync(TestRoot, "newfolder", TestContext.Current.CancellationToken);

            var names = new List<string>();
            await foreach (var item in Provider.ListAsync(TestRoot, TestContext.Current.CancellationToken))
                names.Add(item.Name);

            Assert.Contains("newfolder", names);
        }

        [Fact]
        public async Task DeleteAsync_RemovesItemFromParentListing()
        {
            var created = await Provider.CreateDirectoryAsync(TestRoot, "temp", TestContext.Current.CancellationToken);
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
            await Provider.CreateDirectoryAsync(TestRoot, "temp", TestContext.Current.CancellationToken); // needs >=1 item - same reason as the FolderSizeCalculator cancellation test
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in Provider.ListAsync(TestRoot, cts.Token)) { }
            });
        }
    }
}
