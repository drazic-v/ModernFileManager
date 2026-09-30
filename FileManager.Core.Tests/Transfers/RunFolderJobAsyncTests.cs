using FileManager.Core.Models;
using FileManager.Core.Providers;
using FileManager.Core.Transfers;
using FileManager.TestKit;
using System.IO;
using System.Text;
using Xunit;

namespace FileManager.Core.Tests.Transfers;

public class RunFolderJobAsyncTests
{
    private static StoragePath P(string value) => new() { ProviderId = "fake", Value = value };

    private static StorageItem MakeFolder(StoragePath path) => new()
    {
        Path = path,
        Name = path.Name,
        Kind = StorageItemKind.Directory
    };

    private static StorageItem MakeFile(StoragePath path, long size) => new()
    {
        Path = path,
        Name = path.Name,
        Kind = StorageItemKind.File,
        SizeInBytes = size
    };

    private static Task<NameCollisionPolicy> AlwaysFail(StoragePath _, StorageItemKind __, CancellationToken ___) =>
        Task.FromResult(NameCollisionPolicy.Fail);

    private static Task<NameCollisionPolicy> AlwaysSkip(StoragePath _, StorageItemKind __, CancellationToken ___) =>
        Task.FromResult(NameCollisionPolicy.Skip);

    private static Task<NameCollisionPolicy> AlwaysMerge(StoragePath _, StorageItemKind __, CancellationToken ___) =>
        Task.FromResult(NameCollisionPolicy.Merge);

    private static Task<NameCollisionPolicy> AlwaysGenerateUnique(StoragePath _, StorageItemKind __, CancellationToken ___) =>
        Task.FromResult(NameCollisionPolicy.GenerateUnique);

    [Fact]
    public async Task CopyFolder_ToFreshDestination_Succeeds()
    {
        var provider = new FakeStorageProvider();
        var sourceFolder = P("/root/src");
        var destParent = P("/root/dest-parent");
        var file1 = P("/root/src/a.txt");
        var subdir = P("/root/src/sub");
        var nestedFile = P("/root/src/sub/b.txt");

        provider.AddChildren("/root", MakeFolder(sourceFolder), MakeFolder(destParent));
        provider.AddChildren(sourceFolder.Value, MakeFile(file1, 5), MakeFolder(subdir));
        provider.SetContent(file1, Encoding.UTF8.GetBytes("hello"));
        provider.AddChildren(subdir.Value, MakeFile(nestedFile, 3));
        provider.SetContent(nestedFile, Encoding.UTF8.GetBytes("abc"));
        provider.AddChildren(destParent.Value);

        await using var manager = new TransferManager();
        var progress = new TerminalAwaitingProgress();

        manager.Submit(new TransferRequest
        {
            SourceProvider = provider,
            SourcePath = sourceFolder,
            DestinationProvider = provider,
            DestinationFolder = destParent,
            Operation = TransferOperation.Copy,
            ConflictResolver = AlwaysFail
        }, progress);

        var final = await progress.Completion.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(TransferStatus.Succeeded, final.Status);
        var copiedRoot = destParent.Combine("src");
        Assert.True(await provider.ExistsAsync(copiedRoot.Combine("a.txt"), TestContext.Current.CancellationToken));
        Assert.True(await provider.ExistsAsync(copiedRoot.Combine("sub").Combine("b.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CopyFolder_MergePolicy_ReusesExistingDestinationFolder()
    {
        var provider = new FakeStorageProvider();
        var sourceFolder = P("/root/src");
        var destParent = P("/root/dest-parent");
        var existingDestFolder = destParent.Combine("src");
        var existingFile = existingDestFolder.Combine("existing.txt");
        var newFile = P("/root/src/new.txt");

        provider.AddChildren("/root", MakeFolder(sourceFolder), MakeFolder(destParent));
        provider.AddChildren(sourceFolder.Value, MakeFile(newFile, 3));
        provider.SetContent(newFile, Encoding.UTF8.GetBytes("new"));

        provider.AddChildren(destParent.Value, MakeFolder(existingDestFolder));
        provider.AddChildren(existingDestFolder.Value, MakeFile(existingFile, 3));
        provider.SetContent(existingFile, Encoding.UTF8.GetBytes("old"));

        await using var manager = new TransferManager();
        var progress = new TerminalAwaitingProgress();

        manager.Submit(new TransferRequest
        {
            SourceProvider = provider,
            SourcePath = sourceFolder,
            DestinationProvider = provider,
            DestinationFolder = destParent,
            Operation = TransferOperation.Copy,
            ConflictResolver = AlwaysMerge
        }, progress);

        var final = await progress.Completion.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(TransferStatus.Succeeded, final.Status);
        // Merge reuses the existing folder: the pre-existing file survives...
        Assert.True(await provider.ExistsAsync(existingFile, TestContext.Current.CancellationToken));
        // ...and the new file lands inside the same, not a duplicate, folder.
        Assert.True(await provider.ExistsAsync(existingDestFolder.Combine("new.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CopyFolder_GenerateUniquePolicy_CreatesDistinctFolder_LeavesOriginalUntouched()
    {
        var provider = new FakeStorageProvider();
        var sourceFolder = P("/root/src");
        var destParent = P("/root/dest-parent");
        var existingDestFolder = destParent.Combine("src");
        var file = P("/root/src/a.txt");

        provider.AddChildren("/root", MakeFolder(sourceFolder), MakeFolder(destParent));
        provider.AddChildren(sourceFolder.Value, MakeFile(file, 5));
        provider.SetContent(file, Encoding.UTF8.GetBytes("hello"));

        provider.AddChildren(destParent.Value, MakeFolder(existingDestFolder));
        provider.AddChildren(existingDestFolder.Value);

        await using var manager = new TransferManager();
        var progress = new TerminalAwaitingProgress();

        manager.Submit(new TransferRequest
        {
            SourceProvider = provider,
            SourcePath = sourceFolder,
            DestinationProvider = provider,
            DestinationFolder = destParent,
            Operation = TransferOperation.Copy,
            ConflictResolver = AlwaysGenerateUnique
        }, progress);

        var final = await progress.Completion.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(TransferStatus.Succeeded, final.Status);

        var childrenAfter = new List<StorageItem>();
        await foreach (var item in provider.ListAsync(destParent, TestContext.Current.CancellationToken))
            childrenAfter.Add(item);

        Assert.Equal(2, childrenAfter.Count); // original "src" + the newly generated one
        var generated = Assert.Single(childrenAfter, i => !StoragePath.PathsEqual(i.Path, existingDestFolder));
        Assert.True(await provider.ExistsAsync(generated.Path.Combine("a.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CopyFolder_SkipPolicy_CreatesNothing()
    {
        var provider = new FakeStorageProvider();
        var sourceFolder = P("/root/src");
        var destParent = P("/root/dest-parent");
        var existingDestFolder = destParent.Combine("src");
        var file = P("/root/src/a.txt");

        provider.AddChildren("/root", MakeFolder(sourceFolder), MakeFolder(destParent));
        provider.AddChildren(sourceFolder.Value, MakeFile(file, 5));
        provider.SetContent(file, Encoding.UTF8.GetBytes("hello"));

        provider.AddChildren(destParent.Value, MakeFolder(existingDestFolder));
        provider.AddChildren(existingDestFolder.Value);

        await using var manager = new TransferManager();
        var progress = new TerminalAwaitingProgress();

        manager.Submit(new TransferRequest
        {
            SourceProvider = provider,
            SourcePath = sourceFolder,
            DestinationProvider = provider,
            DestinationFolder = destParent,
            Operation = TransferOperation.Copy,
            ConflictResolver = AlwaysSkip
        }, progress);

        var final = await progress.Completion.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(TransferStatus.Skipped, final.Status);

        var childCount = 0;
        await foreach (var _ in provider.ListAsync(existingDestFolder, TestContext.Current.CancellationToken))
            childCount++;
        Assert.Equal(0, childCount);
    }

    [Fact]
    public async Task MoveFolder_DeletesEmptySourceAfterSuccess()
    {
        var provider = new FakeStorageProvider();
        var sourceFolder = P("/root/src");
        var destParent = P("/root/dest-parent");
        var file = P("/root/src/a.txt");

        provider.AddChildren("/root", MakeFolder(sourceFolder), MakeFolder(destParent));
        provider.AddChildren(sourceFolder.Value, MakeFile(file, 5));
        provider.SetContent(file, Encoding.UTF8.GetBytes("hello"));
        provider.AddChildren(destParent.Value);

        await using var manager = new TransferManager();
        var progress = new TerminalAwaitingProgress();

        manager.Submit(new TransferRequest
        {
            SourceProvider = provider,
            SourcePath = sourceFolder,
            DestinationProvider = provider,
            DestinationFolder = destParent,
            Operation = TransferOperation.Move,
            ConflictResolver = AlwaysFail
        }, progress);

        var final = await progress.Completion.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(TransferStatus.Succeeded, final.Status);
        await Assert.ThrowsAsync<FileNotFoundException>(() => provider.GetInfoAsync(sourceFolder, TestContext.Current.CancellationToken));
        Assert.True(await provider.ExistsAsync(destParent.Combine("src").Combine("a.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CopyFolder_OneFileFails_OverallReportsFailed_ButOthersStillTransferred()
    {
        var provider = new FakeStorageProvider();
        var sourceFolder = P("/root/src");
        var destParent = P("/root/dest-parent");
        var file1 = P("/root/src/a.txt");
        var file2 = P("/root/src/b.txt");

        provider.AddChildren("/root", MakeFolder(sourceFolder), MakeFolder(destParent));
        provider.AddChildren(sourceFolder.Value, MakeFile(file1, 5), MakeFile(file2, 7));
        provider.SetContent(file1, Encoding.UTF8.GetBytes("hello"));
        provider.SetContent(file2, Encoding.UTF8.GetBytes("goodbye"));
        provider.AddChildren(destParent.Value);
        provider.FailNextCopyAttempts(1); // a.txt fails, b.txt still succeeds

        await using var manager = new TransferManager();
        var progress = new TerminalAwaitingProgress();

        manager.Submit(new TransferRequest
        {
            SourceProvider = provider,
            SourcePath = sourceFolder,
            DestinationProvider = provider,
            DestinationFolder = destParent,
            Operation = TransferOperation.Copy,
            ConflictResolver = AlwaysFail
        }, progress);

        var final = await progress.Completion.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(TransferStatus.Failed, final.Status);
        Assert.NotNull(final.Error);
        var destRoot = destParent.Combine("src");
        Assert.False(await provider.ExistsAsync(destRoot.Combine("a.txt"), TestContext.Current.CancellationToken));
        Assert.True(await provider.ExistsAsync(destRoot.Combine("b.txt"), TestContext.Current.CancellationToken));
    }
}