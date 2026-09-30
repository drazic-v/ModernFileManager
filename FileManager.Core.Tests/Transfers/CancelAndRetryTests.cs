using FileManager.Core.Models;
using FileManager.Core.Providers;
using FileManager.Core.Transfers;
using FileManager.TestKit;
using System.Text;
using Xunit;

namespace FileManager.Core.Tests.Transfers;

public class CancelAndRetryTests
{
    private static StoragePath P(string value) => new() { ProviderId = "fake", Value = value };

    private static StorageItem MakeFile(StoragePath path, long size) => new()
    {
        Path = path,
        Name = path.Name,
        Kind = StorageItemKind.File,
        SizeInBytes = size
    };

    private static Task<NameCollisionPolicy> AlwaysFail(StoragePath _, StorageItemKind __, CancellationToken ___) =>
        Task.FromResult(NameCollisionPolicy.Fail);

    [Fact]
    public async Task Cancel_StopsOnlyTheTargetedTransfer_OthersProceedNormally()
    {
        var provider = new FakeStorageProvider();
        var file1 = P("/root/a.txt");
        var file2 = P("/root/b.txt");
        var dest = P("/dest");

        provider.AddChildren("/root", MakeFile(file1, 5), MakeFile(file2, 5));
        provider.SetContent(file1, Encoding.UTF8.GetBytes("hello"));
        provider.SetContent(file2, Encoding.UTF8.GetBytes("world"));
        provider.AddChildren(dest.Value);

        await using var manager = new TransferManager();
        var progress1 = new TerminalAwaitingProgress();
        var progress2 = new TerminalAwaitingProgress();

        var reachedGate = provider.HoldNextCopy();
        var id1 = manager.Submit(new TransferRequest
        {
            SourceProvider = provider,
            SourcePath = file1,
            DestinationProvider = provider,
            DestinationFolder = dest,
            Operation = TransferOperation.Copy,
            ConflictResolver = AlwaysFail
        }, progress1);

        manager.Submit(new TransferRequest
        {
            SourceProvider = provider,
            SourcePath = file2,
            DestinationProvider = provider,
            DestinationFolder = dest,
            Operation = TransferOperation.Copy,
            ConflictResolver = AlwaysFail
        }, progress2);

        // Don't assume job1 is stuck - confirm it.
        await reachedGate.WaitAsync(TimeSpan.FromSeconds(2));

        // Cancelling should unstick job1 itself - the gate observes the token.
        manager.Cancel(id1);

        var final1 = await progress1.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        var final2 = await progress2.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(TransferStatus.Cancelled, final1.Status);
        Assert.Equal(TransferStatus.Succeeded, final2.Status);
        Assert.False(await provider.ExistsAsync(dest.Combine("a.txt")));
        Assert.True(await provider.ExistsAsync(dest.Combine("b.txt")));
    }

    [Fact]
    public async Task CancelAll_StopsInFlightAndQueuedTransfers()
    {
        var provider = new FakeStorageProvider();
        var file1 = P("/root/a.txt");
        var file2 = P("/root/b.txt");
        var dest = P("/dest");

        provider.AddChildren("/root", MakeFile(file1, 5), MakeFile(file2, 5));
        provider.SetContent(file1, Encoding.UTF8.GetBytes("hello"));
        provider.SetContent(file2, Encoding.UTF8.GetBytes("world"));
        provider.AddChildren(dest.Value);

        await using var manager = new TransferManager();
        var progress1 = new TerminalAwaitingProgress();
        var progress2 = new TerminalAwaitingProgress();

        var reachedGate = provider.HoldNextCopy();
        manager.Submit(new TransferRequest
        {
            SourceProvider = provider,
            SourcePath = file1,
            DestinationProvider = provider,
            DestinationFolder = dest,
            Operation = TransferOperation.Copy,
            ConflictResolver = AlwaysFail
        }, progress1);

        manager.Submit(new TransferRequest
        {
            SourceProvider = provider,
            SourcePath = file2,
            DestinationProvider = provider,
            DestinationFolder = dest,
            Operation = TransferOperation.Copy,
            ConflictResolver = AlwaysFail
        }, progress2);

        // Confirmed stuck, and - because the worker is single-consumer and job1
        // hasn't released the queue yet - job2 is confirmed still un-started too.
        await reachedGate.WaitAsync(TimeSpan.FromSeconds(2));

        manager.CancelAll();

        var final1 = await progress1.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        var final2 = await progress2.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(TransferStatus.Cancelled, final1.Status);
        Assert.Equal(TransferStatus.Cancelled, final2.Status);
        Assert.False(await provider.ExistsAsync(dest.Combine("a.txt")));
        Assert.False(await provider.ExistsAsync(dest.Combine("b.txt")));
    }

    [Fact]
    public async Task Retry_AfterFailure_SucceedsOnNextAttempt()
    {
        var provider = new FakeStorageProvider();
        var file = P("/root/a.txt");
        var dest = P("/dest");

        provider.AddChildren("/root", MakeFile(file, 5));
        provider.SetContent(file, Encoding.UTF8.GetBytes("hello"));
        provider.AddChildren(dest.Value);
        provider.FailNextCopyAttempts(1);

        await using var manager = new TransferManager();
        var progress = new TerminalAwaitingProgress();

        var id = manager.Submit(new TransferRequest
        {
            SourceProvider = provider,
            SourcePath = file,
            DestinationProvider = provider,
            DestinationFolder = dest,
            Operation = TransferOperation.Copy,
            ConflictResolver = AlwaysFail
        }, progress);

        var firstResult = await progress.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(TransferStatus.Failed, firstResult.Status);
        Assert.False(await provider.ExistsAsync(dest.Combine("a.txt")));

        progress.Reset();
        manager.Retry(id);

        var retryResult = await progress.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(TransferStatus.Succeeded, retryResult.Status);
        Assert.True(await provider.ExistsAsync(dest.Combine("a.txt")));
    }

    [Fact]
    public async Task Retry_WhileOriginalStillRunning_IsANoOp()
    {
        var provider = new FakeStorageProvider();
        var file = P("/root/a.txt");
        var dest = P("/dest");

        provider.AddChildren("/root", MakeFile(file, 5));
        provider.SetContent(file, Encoding.UTF8.GetBytes("hello"));
        provider.AddChildren(dest.Value);

        await using var manager = new TransferManager();
        var progress = new TerminalAwaitingProgress();

        var reachedGate = provider.HoldNextCopy();
        var id = manager.Submit(new TransferRequest
        {
            SourceProvider = provider,
            SourcePath = file,
            DestinationProvider = provider,
            DestinationFolder = dest,
            Operation = TransferOperation.Copy,
            ConflictResolver = AlwaysFail
        }, progress);

        await reachedGate.WaitAsync(TimeSpan.FromSeconds(2));

        manager.Retry(id); // must be a no-op - confirmed still running, not finished

        provider.ReleaseHeldCopy();
        var final = await progress.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(TransferStatus.Succeeded, final.Status);
        Assert.Single(progress.Updates, u => u.Status == TransferStatus.Queued);
    }

    [Fact]
    public async Task Retry_UnknownTransferId_IsANoOp()
    {
        await using var manager = new TransferManager();
        var exception = Record.Exception(() => manager.Retry(Guid.NewGuid()));
        Assert.Null(exception);
    }

    [Fact]
    public async Task Cancel_UnknownTransferId_IsANoOp()
    {
        await using var manager = new TransferManager();
        var exception = Record.Exception(() => manager.Cancel(Guid.NewGuid()));
        Assert.Null(exception);
    }
}