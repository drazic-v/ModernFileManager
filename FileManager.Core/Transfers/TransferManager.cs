using FileManager.Core.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Threading.Channels;
using FileManager.Core.Providers;

namespace FileManager.Core.Transfers;

public sealed class TransferManager : ITransferManager, IAsyncDisposable
{
    private readonly Channel<TransferJob> _queue = Channel.CreateUnbounded<TransferJob>();
    private readonly Dictionary<Guid, TransferJob> _jobs = new();
    private readonly object _lock = new();
    private readonly Task _workerLoop;
    private readonly TimeSpan _disposeTimeout;


    public TransferManager(TimeSpan? disposeTimeout = null)   // `new TransferManager()` still compiles
    {
        _disposeTimeout = disposeTimeout ?? TimeSpan.FromSeconds(3);
        _workerLoop = Task.Run(ProcessQueueAsync);
    }

    private int _disposed;

    public Guid Submit(TransferRequest request, IProgress<TransferUpdate> progress)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var job = new TransferJob(Guid.NewGuid(), request, progress);
        lock (_lock) _jobs[job.Id] = job;
        progress.Report(new TransferUpdate { Status = TransferStatus.Queued });

        if (!_queue.Writer.TryWrite(job))   // lost a race with DisposeAsync between the check and here
        {
            lock (_lock) _jobs.Remove(job.Id);
            job.DisposeCts();
            throw new ObjectDisposedException(nameof(TransferManager));
        }
        return job.Id;
    }

    public void Cancel(Guid transferId)
    {
        TransferJob? job;
        lock (_lock) _jobs.TryGetValue(transferId, out job);
        job?.TryCancel();
    }

    public void CancelAll()
    {
        List<TransferJob> snapshot;
        lock (_lock) snapshot = _jobs.Values.ToList();
        foreach (var job in snapshot) job.TryCancel();
    }

    public void Retry(Guid transferId)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        TransferJob retryJob;
        lock (_lock)
        {
            if (!_jobs.TryGetValue(transferId, out var original)
                || original.TerminalStatus != TransferStatus.Failed) return;
            retryJob = new TransferJob(transferId, original.Request, original.OriginalProgress);
            _jobs[transferId] = retryJob;
        }
        retryJob.Progress.Report(new TransferUpdate { Status = TransferStatus.Queued });
        if (!_queue.Writer.TryWrite(retryJob))
            throw new ObjectDisposedException(nameof(TransferManager));
    }

    /// <summary>Drops a finished (i.e. retained Failed) job. Live jobs are left alone.</summary>
    public void Forget(Guid transferId)
    {
        lock (_lock)
            if (_jobs.TryGetValue(transferId, out var job) && job.IsFinished)
                _jobs.Remove(transferId);
    }

    internal int TrackedJobCount { get { lock (_lock) return _jobs.Count; } }   // test seam


    // Only Failed jobs are worth keeping: Retry needs their Request/OriginalProgress.
    private void ReleaseJob(TransferJob job)
    {
        lock (_lock)
        {
            if (job.TerminalStatus != TransferStatus.Failed
                && _jobs.TryGetValue(job.Id, out var current) && ReferenceEquals(current, job))
                _jobs.Remove(job.Id);   // identity check: Retry may have replaced this entry
        }
        job.DisposeCts();
    }
    private async Task ProcessQueueAsync()
    {
        await foreach (var job in _queue.Reader.ReadAllAsync())
        {
            try { await RunJobAsync(job); }
            finally { ReleaseJob(job); }
        }
    }

    private async Task RunJobAsync(TransferJob job)
    {
        if (job.Cts.IsCancellationRequested)
        {
            job.Progress.Report(new TransferUpdate { Status = TransferStatus.Cancelled });
            return;
        }

        try
        {
            var info = await job.Request.SourceProvider.GetInfoAsync(job.Request.SourcePath, job.Cts.Token);
            if (info.Kind == StorageItemKind.Directory)
                await RunFolderJobAsync(job);
            else
                await RunFileJobAsync(job);
        }
        catch (OperationCanceledException)
        {
            job.Progress.Report(new TransferUpdate { Status = TransferStatus.Cancelled });
        }
        catch (Exception ex)
        {
            job.Progress.Report(new TransferUpdate { Status = TransferStatus.Failed, Error = ex });
        }
    }

    internal readonly record struct FileTransferResult(TransferStatus Status, Exception? Error = null);
    internal async Task<FileTransferResult> TransferFileCoreAsync(
        IStorageProvider sourceProvider, IStorageProvider destinationProvider, TransferOperation operation,
        StoragePath sourcePath, StoragePath destinationFolder, ConflictResolver conflictResolver,
        RetryPolicy retryPolicy, CancellationToken token, IProgress<TransferUpdate> progress)
    {
        var totalBytes = (await sourceProvider.GetInfoAsync(sourcePath, token)).SizeInBytes ?? 0;

        if (sourceProvider.ProviderId == destinationProvider.ProviderId)
        {
            var nativeProgress = new Progress<TransferProgress>(p =>
                progress.Report(new TransferUpdate { Status = TransferStatus.Running, BytesCopied = p.BytesCopied, TotalBytes = totalBytes }));

            for (var attempt = 1; attempt <= retryPolicy.MaxAttempts; attempt++)
            {
                progress.Report(new TransferUpdate { Status = TransferStatus.Running, AttemptNumber = attempt });
                try
                {
                    var copiedItem = operation == TransferOperation.Copy
                        ? await sourceProvider.CopyAsync(sourcePath, destinationFolder, conflictResolver, nativeProgress, token)
                        : await sourceProvider.MoveAsync(sourcePath, destinationFolder, conflictResolver, nativeProgress, token);

                    return copiedItem is null
                        ? new FileTransferResult(TransferStatus.Skipped)
                        : new FileTransferResult(TransferStatus.Succeeded);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (attempt == retryPolicy.MaxAttempts)
                        return new FileTransferResult(TransferStatus.Failed, ex);
                }
            }

            throw new InvalidOperationException("RetryPolicy.MaxAttempts must be at least 1.");
        }

        // Cross-provider.
        var destinationPath = destinationFolder.Combine(sourcePath.Name);

        if (await destinationProvider.ExistsAsync(destinationPath, token))
        {
            var conflicting = await destinationProvider.GetInfoAsync(destinationPath, token);
            var policy = await conflictResolver(destinationPath, conflicting.Kind, token);
            switch (policy)
            {
                case NameCollisionPolicy.GenerateUnique:
                    var newName = await UniqueNameGenerator.GenerateAsync(
                        destinationProvider, destinationFolder, sourcePath.Name, StorageItemKind.File, excludeName: null, ct: token);
                    destinationPath = destinationFolder.Combine(newName);
                    break;
                case NameCollisionPolicy.Replace:
                    await destinationProvider.DeleteAsync(destinationPath, token);
                    break;
                case NameCollisionPolicy.Skip:
                    return new FileTransferResult(TransferStatus.Skipped);
                case NameCollisionPolicy.Fail:
                    return new FileTransferResult(TransferStatus.Failed, new IOException($"File already exists at destination: {destinationPath}"));
            }
        }

        for (var attempt = 1; attempt <= retryPolicy.MaxAttempts; attempt++)
        {
            progress.Report(new TransferUpdate { Status = TransferStatus.Running, AttemptNumber = attempt });
            try
            {
                await using (var inStream = await sourceProvider.OpenReadAsync(sourcePath, token))
                await using (var outStream = await destinationProvider.OpenWriteAsync(destinationPath, token))
                {
                    var buffer = new byte[81920];
                    int bytesRead;
                    while ((bytesRead = await inStream.ReadAsync(buffer, token)) > 0)
                    {
                        await outStream.WriteAsync(buffer.AsMemory(0, bytesRead), token);
                        progress.Report(new TransferUpdate
                        {
                            Status = TransferStatus.Running,
                            AttemptNumber = attempt,
                            BytesCopied = inStream.Position,
                            TotalBytes = totalBytes
                        });
                    }
                }

                if (operation == TransferOperation.Move)
                    await sourceProvider.DeleteAsync(sourcePath, token);

                return new FileTransferResult(TransferStatus.Succeeded);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (attempt == retryPolicy.MaxAttempts)
                    return new FileTransferResult(TransferStatus.Failed, ex);
            }
        }

        throw new InvalidOperationException("RetryPolicy.MaxAttempts must be at least 1.");
    }

    internal async Task<bool> DeleteEmptyDirectoriesAsync(IStorageProvider provider, StoragePath path, CancellationToken token)
    {
        var isEmpty = true;
        await foreach (var item in provider.ListAsync(path, token))
        {
            if (item.Kind == StorageItemKind.Directory && await DeleteEmptyDirectoriesAsync(provider, item.Path, token))
                continue; // this subdirectory emptied out and got deleted - doesn't count against path

            isEmpty = false; // a file, or a subdirectory that's still non-empty, is here
        }

        if (isEmpty)
            await provider.DeleteAsync(path, token);

        return isEmpty;
    }

    internal sealed class ByteAccumulator { public long Completed; }
    internal sealed class FolderTransferSummary
    {
        public int Succeeded;
        public int Skipped;
        public int Failed;
        public Exception? FirstError;
    }
    private async Task RunFolderJobAsync(TransferJob job)
    {
        var sourceProvider = job.Request.SourceProvider;
        var destinationProvider = job.Request.DestinationProvider;
        var operation = job.Request.Operation;
        var sourcePath = job.Request.SourcePath;
        var destinationFolder = job.Request.DestinationFolder;
        var conflictResolver = job.Request.ConflictResolver;
        var retryPolicy = job.Request.RetryPolicy;
        var token = job.Cts.Token;

        var folderInfo = await FolderInfoCalculator.GetFolderInfo(sourceProvider, sourcePath, null, token);
        job.Progress.Report(new TransferUpdate { Status = TransferStatus.Running, BytesCopied = 0, TotalBytes = folderInfo.Size });

        var destinationPath = destinationFolder.Combine(sourcePath.Name);
        var shouldCreate = true;

        if (await destinationProvider.ExistsAsync(destinationPath, token))
        {
            var conflicting = await destinationProvider.GetInfoAsync(destinationPath, token);
            var policy = await conflictResolver(destinationPath, conflicting.Kind, token);
            switch (policy)
            {
                case NameCollisionPolicy.GenerateUnique:
                    var newName = await UniqueNameGenerator.GenerateAsync(
                        destinationProvider, destinationFolder, sourcePath.Name, StorageItemKind.Directory, excludeName: null, ct: token);
                    destinationPath = destinationFolder.Combine(newName);
                    break;
                case NameCollisionPolicy.Replace:
                    await destinationProvider.DeleteAsync(destinationPath, token);
                    break;
                case NameCollisionPolicy.Skip:
                    job.Progress.Report(new TransferUpdate { Status = TransferStatus.Skipped });
                    return;
                case NameCollisionPolicy.Merge:
                    shouldCreate = false; // reuse the existing directory
                    break;
                case NameCollisionPolicy.Fail:
                    throw new IOException($"Folder already exists at destination: {destinationPath}");
            }
        }

        if (shouldCreate)
            await destinationProvider.CreateDirectoryAsync(destinationFolder, destinationPath.Name, token);

        var accumulator = new ByteAccumulator();
        var summary = new FolderTransferSummary();

        await RunFolderJobRecursivelyAsync(sourceProvider, destinationProvider, operation, sourcePath, destinationPath,
            conflictResolver, retryPolicy, token, job.Progress, accumulator, folderInfo.Size, summary);

        if (operation == TransferOperation.Move)
            await DeleteEmptyDirectoriesAsync(sourceProvider, sourcePath, token);

        job.Progress.Report(summary.Failed > 0
            ? new TransferUpdate { Status = TransferStatus.Failed, Error = summary.FirstError, BytesCopied = accumulator.Completed, TotalBytes = folderInfo.Size }
            : new TransferUpdate { Status = TransferStatus.Succeeded, BytesCopied = folderInfo.Size, TotalBytes = folderInfo.Size });
    }

    internal async Task RunFolderJobRecursivelyAsync(
        IStorageProvider sourceProvider, IStorageProvider destinationProvider, TransferOperation operation,
        StoragePath sourcePath, StoragePath destinationFolder, ConflictResolver conflictResolver,
        RetryPolicy retryPolicy, CancellationToken token, IProgress<TransferUpdate> jobProgress,
        ByteAccumulator accumulator, long totalBytes, FolderTransferSummary summary)
    {
        await foreach (var item in sourceProvider.ListAsync(sourcePath, token))
        {
            if (item.Kind == StorageItemKind.File)
            {
                var fileProgress = new Progress<TransferUpdate>(update =>
                {
                    if (update.Status == TransferStatus.Running)
                        jobProgress.Report(new TransferUpdate
                        {
                            Status = TransferStatus.Running,
                            BytesCopied = accumulator.Completed + update.BytesCopied,
                            TotalBytes = totalBytes
                        });
                });

                // OperationCanceledException isn't caught here - it propagates and stops the
                // whole folder, which is correct. Everything else is recorded, not fatal.
                var result = await TransferFileCoreAsync(sourceProvider, destinationProvider, operation,
                    item.Path, destinationFolder, conflictResolver, retryPolicy, token, fileProgress);

                switch (result.Status)
                {
                    case TransferStatus.Succeeded: summary.Succeeded++; break;
                    case TransferStatus.Skipped: summary.Skipped++; break;
                    case TransferStatus.Failed:
                        summary.Failed++;
                        summary.FirstError ??= result.Error;
                        break;
                }

                accumulator.Completed += item.SizeInBytes ?? 0;
            }
            else
            {
                await destinationProvider.CreateDirectoryAsync(destinationFolder, item.Name, token);
                await RunFolderJobRecursivelyAsync(sourceProvider, destinationProvider, operation,
                    item.Path, destinationFolder.Combine(item.Name), conflictResolver, retryPolicy, token,
                    jobProgress, accumulator, totalBytes, summary);
            }
        }
    }
    private async Task RunFileJobAsync(TransferJob job)
    {
        var result = await TransferFileCoreAsync(
            job.Request.SourceProvider, job.Request.DestinationProvider, job.Request.Operation,
            job.Request.SourcePath, job.Request.DestinationFolder, job.Request.ConflictResolver,
            job.Request.RetryPolicy, job.Cts.Token, job.Progress);

        job.Progress.Report(new TransferUpdate { Status = result.Status, Error = result.Error });
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;   // idempotent
        CancelAll();
        _queue.Writer.TryComplete();
        try { await _workerLoop.WaitAsync(_disposeTimeout); }
        catch (TimeoutException) { }
    }
}

internal sealed class TransferJob
{
    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _terminalStatus = -1; // -1 = not finished


    public TransferJob(Guid id, TransferRequest request, IProgress<TransferUpdate> progress)
    {
        Id = id;
        Request = request;
        OriginalProgress = progress;
        Progress = new CompletionTrackingProgress(this, progress);
    }

    public Guid Id { get; }
    public TransferRequest Request { get; }

    /// <summary>The caller-supplied progress, unwrapped. Use this (not Progress) when
    /// constructing a follow-up job - e.g. in Retry - so the tracking wrapper doesn't
    /// stack a layer deeper on every retry.</summary>
    public IProgress<TransferUpdate> OriginalProgress { get; }

    /// <summary>What RunJobAsync/RunFileJobAsync/RunFolderJobAsync report through.
    /// Marks the job finished *before* forwarding to OriginalProgress, so IsFinished
    /// is never observably false by the time any caller sees a terminal report.</summary>
    public IProgress<TransferUpdate> Progress { get; }

    public CancellationTokenSource Cts { get; } = new();

    public bool IsFinished => _completion.Task.IsCompleted;

    public TransferStatus? TerminalStatus
    {
        get { var v = Volatile.Read(ref _terminalStatus); return v < 0 ? null : (TransferStatus)v; }
    }

    /// <summary>Cancelling a job whose Cts was already released is a harmless no-op.</summary>
    public void TryCancel()
    {
        try { Cts.Cancel(); } catch (ObjectDisposedException) { }
    }

    public void DisposeCts() => Cts.Dispose(); // idempotent

    private void MarkTerminal(TransferStatus status)
    {
        // First terminal report wins; status is visible before IsFinished flips.
        if (Interlocked.CompareExchange(ref _terminalStatus, (int)status, -1) == -1)
            _completion.TrySetResult(true);
    }
    private sealed class CompletionTrackingProgress : IProgress<TransferUpdate>
    {
        private readonly TransferJob _job;
        private readonly IProgress<TransferUpdate> _inner;

        public CompletionTrackingProgress(TransferJob job, IProgress<TransferUpdate> inner)
        {
            _job = job;
            _inner = inner;
        }

        public void Report(TransferUpdate value)
        {
            if (IsTerminal(value.Status)) 
                _job.MarkTerminal(value.Status); // before forwarding
            _inner.Report(value);
        }

        private static bool IsTerminal(TransferStatus status) =>
            status is TransferStatus.Succeeded or TransferStatus.Failed
                or TransferStatus.Skipped or TransferStatus.Cancelled;
    }
}
