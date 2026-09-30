using FileManager.Core.Models;
using FileManager.Core.Providers;
using FileManager.Core.Transfers;
using FileManager.TestKit;
using System.Text;
using Xunit;

namespace FileManager.Core.Tests.Transfers;

public class RunFolderJobRecursivelyAsyncTests
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

    [Fact]
    public async Task FlatFolder_AllFilesSucceed()
    {
        var provider = new FakeStorageProvider();
        var sourceRoot = P("/root/src");
        var destRoot = P("/root/dest");
        var file1 = P("/root/src/a.txt");
        var file2 = P("/root/src/b.txt");

        provider.AddChildren(sourceRoot.Value, MakeFile(file1, 5), MakeFile(file2, 7));
        provider.SetContent(file1, Encoding.UTF8.GetBytes("hello"));
        provider.SetContent(file2, Encoding.UTF8.GetBytes("goodbye"));
        provider.AddChildren(destRoot.Value);

        await using var manager = new TransferManager();
        var jobProgress = new RecordingProgress<TransferUpdate>();
        var accumulator = new TransferManager.ByteAccumulator();
        var summary = new TransferManager.FolderTransferSummary();

        await manager.RunFolderJobRecursivelyAsync(
            provider, provider, TransferOperation.Copy,
            sourceRoot, destRoot, AlwaysFail, RetryPolicy.None,
            CancellationToken.None, jobProgress, accumulator, totalBytes: 12, summary);

        Assert.Equal(2, summary.Succeeded);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(0, summary.Skipped);
        Assert.Equal(12, accumulator.Completed);
        Assert.True(await provider.ExistsAsync(destRoot.Combine("a.txt"), TestContext.Current.CancellationToken));
        Assert.True(await provider.ExistsAsync(destRoot.Combine("b.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OneFileFails_OthersStillTransfer_SummarizesAtEnd()
    {
        var provider = new FakeStorageProvider();
        var sourceRoot = P("/root/src");
        var destRoot = P("/root/dest");
        var file1 = P("/root/src/a.txt");
        var file2 = P("/root/src/b.txt");

        provider.AddChildren(sourceRoot.Value, MakeFile(file1, 5), MakeFile(file2, 7));
        provider.SetContent(file1, Encoding.UTF8.GetBytes("hello"));
        provider.SetContent(file2, Encoding.UTF8.GetBytes("goodbye"));
        provider.AddChildren(destRoot.Value);
        provider.FailNextCopyAttempts(1); // fails on a.txt (processed first), b.txt still succeeds

        await using var manager = new TransferManager();
        var jobProgress = new RecordingProgress<TransferUpdate>();
        var accumulator = new TransferManager.ByteAccumulator();
        var summary = new TransferManager.FolderTransferSummary();

        await manager.RunFolderJobRecursivelyAsync(
            provider, provider, TransferOperation.Copy,
            sourceRoot, destRoot, AlwaysFail, RetryPolicy.None,
            CancellationToken.None, jobProgress, accumulator, totalBytes: 12, summary);

        Assert.Equal(1, summary.Succeeded);
        Assert.Equal(1, summary.Failed);
        Assert.NotNull(summary.FirstError);
        Assert.False(await provider.ExistsAsync(destRoot.Combine("a.txt"), TestContext.Current.CancellationToken));
        Assert.True(await provider.ExistsAsync(destRoot.Combine("b.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NestedSubdirectory_UsesItemNameNotEnclosingFolderName()
    {
        var provider = new FakeStorageProvider();
        var sourceRoot = P("/root/src");
        var destRoot = P("/root/dest");
        var subdir = P("/root/src/sub");
        var nestedFile = P("/root/src/sub/c.txt");

        provider.AddChildren(sourceRoot.Value, MakeFolder(subdir));
        provider.AddChildren(subdir.Value, MakeFile(nestedFile, 3));
        provider.SetContent(nestedFile, Encoding.UTF8.GetBytes("abc"));
        provider.AddChildren(destRoot.Value);

        await using var manager = new TransferManager();
        var jobProgress = new RecordingProgress<TransferUpdate>();
        var accumulator = new TransferManager.ByteAccumulator();
        var summary = new TransferManager.FolderTransferSummary();

        await manager.RunFolderJobRecursivelyAsync(
            provider, provider, TransferOperation.Copy,
            sourceRoot, destRoot, AlwaysFail, RetryPolicy.None,
            CancellationToken.None, jobProgress, accumulator, totalBytes: 3, summary);

        Assert.Equal(1, summary.Succeeded);
        // Guards the earlier bug where sourcePath.Name (the enclosing folder's own name)
        // was used instead of item.Name, causing incorrect double-nesting.
        Assert.True(await provider.ExistsAsync(destRoot.Combine("sub"), TestContext.Current.CancellationToken));
        Assert.True(await provider.ExistsAsync(destRoot.Combine("sub").Combine("c.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CollisionWithSkipPolicy_LeavesExistingDestinationFileUntouched()
    {
        var provider = new FakeStorageProvider();
        var sourceRoot = P("/root/src");
        var destRoot = P("/root/dest");
        var file1 = P("/root/src/a.txt");
        var existingDestFile = destRoot.Combine("a.txt");

        provider.AddChildren(sourceRoot.Value, MakeFile(file1, 5));
        provider.SetContent(file1, Encoding.UTF8.GetBytes("hello"));
        provider.AddChildren(destRoot.Value, MakeFile(existingDestFile, 3));
        provider.SetContent(existingDestFile, Encoding.UTF8.GetBytes("old"));

        await using var manager = new TransferManager();
        var jobProgress = new RecordingProgress<TransferUpdate>();
        var accumulator = new TransferManager.ByteAccumulator();
        var summary = new TransferManager.FolderTransferSummary();

        await manager.RunFolderJobRecursivelyAsync(
            provider, provider, TransferOperation.Copy,
            sourceRoot, destRoot, AlwaysSkip, RetryPolicy.None,
            CancellationToken.None, jobProgress, accumulator, totalBytes: 5, summary);

        Assert.Equal(1, summary.Skipped);
        Assert.Equal(0, summary.Succeeded);

        await using var stream = await provider.OpenReadAsync(existingDestFile, TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stream);
        Assert.Equal("old", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }
}