using FileManager.Core.Models;
using FileManager.Core.Providers;
using FileManager.Core.Transfers;
using FileManager.TestKit;
using System.Diagnostics;
using System.Text;
using Xunit;

namespace FileManager.Core.Tests.Transfers;

public class JobTrackingTests
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

    private static Task<NameCollisionPolicy> AlwaysSkip(StoragePath _, StorageItemKind __, CancellationToken ___) =>
        Task.FromResult(NameCollisionPolicy.Skip);

    private static (FakeStorageProvider Provider, StoragePath File, StoragePath Dest) Seed(params StorageItem[] destChildren)
    {
        var provider = new FakeStorageProvider();
        var file = P("/root/a.txt");
        var dest = P("/dest");
        provider.AddChildren("/root", MakeFile(file, 5));
        provider.SetContent(file, Encoding.UTF8.GetBytes("hello"));
        provider.AddChildren(dest.Value, destChildren);
        return (provider, file, dest);
    }

    private static TransferRequest CopyRequest(FakeStorageProvider p, StoragePath src, StoragePath dest, ConflictResolver? resolver = null) => new()
    {
        SourceProvider = p,
        SourcePath = src,
        DestinationProvider = p,
        DestinationFolder = dest,
        Operation = TransferOperation.Copy,
        ConflictResolver = resolver ?? AlwaysFail
    };

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task SucceededJob_IsNoLongerTracked()
    {
        var (provider, file, dest) = Seed();
        await using var manager = new TransferManager();
        var progress = new TerminalAwaitingProgress();

        manager.Submit(CopyRequest(provider, file, dest), progress);
        Assert.Equal(TransferStatus.Succeeded, (await progress.Completion.WaitAsync(Wait, TestContext.Current.CancellationToken)).Status);

        await manager.DisposeAsync(); // awaits the worker loop => ReleaseJob has run
        Assert.Equal(0, manager.TrackedJobCount);
    }

    [Fact]
    public async Task SkippedJob_IsNoLongerTracked()
    {
        var (provider, file, dest) = Seed(MakeFile(P("/dest/a.txt"), 3)); // collision
        await using var manager = new TransferManager();
        var progress = new TerminalAwaitingProgress();

        manager.Submit(CopyRequest(provider, file, dest, AlwaysSkip), progress);
        Assert.Equal(TransferStatus.Skipped, (await progress.Completion.WaitAsync(Wait, TestContext.Current.CancellationToken)).Status);

        await manager.DisposeAsync();
        Assert.Equal(0, manager.TrackedJobCount);
    }

    [Fact]
    public async Task CancelledJob_IsNoLongerTracked()
    {
        var (provider, file, dest) = Seed();
        await using var manager = new TransferManager();
        var progress = new TerminalAwaitingProgress();

        var reachedGate = provider.HoldNextCopy();
        var id = manager.Submit(CopyRequest(provider, file, dest), progress);
        await reachedGate.WaitAsync(Wait, TestContext.Current.CancellationToken);

        manager.Cancel(id);
        Assert.Equal(TransferStatus.Cancelled, (await progress.Completion.WaitAsync(Wait, TestContext.Current.CancellationToken)).Status);

        await manager.DisposeAsync();
        Assert.Equal(0, manager.TrackedJobCount);
    }

    [Fact]
    public async Task FailedJob_StaysTracked_SoItCanBeRetried()
    {
        var (provider, file, dest) = Seed();
        provider.FailNextCopyAttempts(1);
        await using var manager = new TransferManager();
        var progress = new TerminalAwaitingProgress();

        manager.Submit(CopyRequest(provider, file, dest), progress);
        Assert.Equal(TransferStatus.Failed, (await progress.Completion.WaitAsync(Wait, TestContext.Current.CancellationToken)).Status);

        // Dispose also proves CancelAll/TryCancel tolerate an already-released Cts.
        await manager.DisposeAsync();
        Assert.Equal(1, manager.TrackedJobCount);
    }

    [Fact]
    public async Task Forget_RemovesFailedJob_SoRetryBecomesANoOp()
    {
        var (provider, file, dest) = Seed();
        provider.FailNextCopyAttempts(1);
        await using var manager = new TransferManager();
        var progress = new TerminalAwaitingProgress();

        var id = manager.Submit(CopyRequest(provider, file, dest), progress);
        await progress.Completion.WaitAsync(Wait, TestContext.Current.CancellationToken);

        manager.Forget(id);
        Assert.Equal(0, manager.TrackedJobCount);

        manager.Retry(id); // a real retry would put the job back in _jobs synchronously
        Assert.Equal(0, manager.TrackedJobCount);
    }

    [Fact]
    public async Task Forget_IgnoresStillRunningJobs()
    {
        var (provider, file, dest) = Seed();
        await using var manager = new TransferManager();
        var progress = new TerminalAwaitingProgress();

        var reachedGate = provider.HoldNextCopy();
        var id = manager.Submit(CopyRequest(provider, file, dest), progress);
        await reachedGate.WaitAsync(Wait, TestContext.Current.CancellationToken);

        manager.Forget(id);
        Assert.Equal(1, manager.TrackedJobCount);   // still live

        manager.Cancel(id);                          // and still cancellable
        Assert.Equal(TransferStatus.Cancelled, (await progress.Completion.WaitAsync(Wait, TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task Retry_KeepsJobTrackedWhileTheRetryRuns()
    {
        var (provider, file, dest) = Seed();
        provider.FailNextCopyAttempts(1);
        await using var manager = new TransferManager();
        var progress = new TerminalAwaitingProgress();

        var id = manager.Submit(CopyRequest(provider, file, dest), progress);
        await progress.Completion.WaitAsync(Wait, TestContext.Current.CancellationToken);

        // Retry may land before or after the worker's cleanup of the ORIGINAL job.
        // Either way the retry's entry must survive while it's running.
        progress.Reset();
        var reachedGate = provider.HoldNextCopy();
        manager.Retry(id);
        await reachedGate.WaitAsync(Wait, TestContext.Current.CancellationToken);   // retry job is now running, so the original was already released

        Assert.Equal(1, manager.TrackedJobCount);

        manager.Cancel(id);
        await progress.Completion.WaitAsync(Wait, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Dispose_CancelsRunningAndQueuedWork()
    {
        var provider = new FakeStorageProvider();
        var file1 = P("/root/a.txt"); var file2 = P("/root/b.txt"); var dest = P("/dest");
        provider.AddChildren("/root", MakeFile(file1, 5), MakeFile(file2, 5));
        provider.SetContent(file1, Encoding.UTF8.GetBytes("hello"));
        provider.SetContent(file2, Encoding.UTF8.GetBytes("world"));
        provider.AddChildren(dest.Value);

        var manager = new TransferManager();
        var progress1 = new TerminalAwaitingProgress();
        var progress2 = new TerminalAwaitingProgress();

        var reachedGate = provider.HoldNextCopy();
        manager.Submit(CopyRequest(provider, file1, dest), progress1);
        manager.Submit(CopyRequest(provider, file2, dest), progress2);
        await reachedGate.WaitAsync(Wait, TestContext.Current.CancellationToken);

        await manager.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);

        Assert.Equal(TransferStatus.Cancelled, (await progress1.Completion.WaitAsync(Wait, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(TransferStatus.Cancelled, (await progress2.Completion.WaitAsync(Wait, TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task Dispose_WithWorkerThatIgnoresCancellation_ReturnsWithinTimeout()
    {
        var (provider, file, dest) = Seed();
        var manager = new TransferManager(disposeTimeout: TimeSpan.FromMilliseconds(200));
        var progress = new TerminalAwaitingProgress();

        var reachedGate = provider.HoldNextCopy(ignoreCancellation: true);
        manager.Submit(CopyRequest(provider, file, dest), progress);
        await reachedGate.WaitAsync(Wait, TestContext.Current.CancellationToken);

        var sw = Stopwatch.StartNew();
        await manager.DisposeAsync().AsTask().WaitAsync(Wait, TestContext.Current.CancellationToken); // would hang without the bound
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1.5), $"Dispose took {sw.Elapsed}");

        provider.ReleaseHeldCopy(); // let the abandoned worker finish so nothing leaks past the test
    }

    [Fact]
    public async Task Retry_OnAJobThatJustSucceeded_IsANoOp()
    {
        var (provider, file, dest) = Seed();
        await using var manager = new TransferManager();

        var statuses = new System.Collections.Concurrent.ConcurrentQueue<TransferStatus>();
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Guid id = default;

        var progress = new CallbackProgress(u =>
        {
            statuses.Enqueue(u.Status);
            if (u.Status == TransferStatus.Succeeded)
            {
                // Runs on the worker, after the terminal report but BEFORE ReleaseJob:
                // the job is finished yet still tracked. Retry must refuse it.
                manager.Retry(id);
                done.TrySetResult(true);
            }
        });

        var reachedGate = provider.HoldNextCopy();
        id = manager.Submit(CopyRequest(provider, file, dest), progress);
        await reachedGate.WaitAsync(Wait, TestContext.Current.CancellationToken);   // id is assigned before the job can finish

        provider.ReleaseHeldCopy();
        await done.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);
        await manager.DisposeAsync();

        Assert.Single(statuses, s => s == TransferStatus.Queued);   // no second Queued => no retry
        Assert.Equal(0, manager.TrackedJobCount);
    }

    private sealed class CallbackProgress : IProgress<TransferUpdate>
    {
        private readonly Action<TransferUpdate> _onReport;
        public CallbackProgress(Action<TransferUpdate> onReport) => _onReport = onReport;
        public void Report(TransferUpdate value) => _onReport(value);
    }

    [Fact]
    public async Task Submit_AfterDispose_Throws_AndLeavesNothingBehind()
    {
        var (provider, file, dest) = Seed();
        var manager = new TransferManager();
        await manager.DisposeAsync();

        var progress = new RecordingProgress<TransferUpdate>();
        Assert.Throws<ObjectDisposedException>(() =>
            manager.Submit(CopyRequest(provider, file, dest), progress));

        Assert.Empty(progress.Updates);          // no stray "Queued"
        Assert.Equal(0, manager.TrackedJobCount);
    }

    [Fact]
    public async Task Retry_AfterDispose_Throws()
    {
        var (provider, file, dest) = Seed();
        provider.FailNextCopyAttempts(1);
        var manager = new TransferManager();
        var progress = new TerminalAwaitingProgress();

        var id = manager.Submit(CopyRequest(provider, file, dest), progress);
        await progress.Completion.WaitAsync(Wait, TestContext.Current.CancellationToken);
        await manager.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => manager.Retry(id));
    }

    [Fact]
    public async Task CancelCancelAllAndForget_AfterDispose_AreHarmless()
    {
        var manager = new TransferManager();
        await manager.DisposeAsync();

        manager.Cancel(Guid.NewGuid());
        manager.CancelAll();
        manager.Forget(Guid.NewGuid());
        await manager.DisposeAsync();   // second dispose is a no-op
    }
}