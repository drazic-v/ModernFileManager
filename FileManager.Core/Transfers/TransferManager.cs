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

    public TransferManager()
    {
        _workerLoop = Task.Run(ProcessQueueAsync);
    }

    public Guid Submit(TransferRequest request, IProgress<TransferUpdate> progress)
    {
        var job = new TransferJob(Guid.NewGuid(), request, progress);
        lock (_lock) _jobs[job.Id] = job;
        progress.Report(new TransferUpdate { Status = TransferStatus.Queued });
        _queue.Writer.TryWrite(job);
        return job.Id;
    }

    public void Cancel(Guid transferId)
    {
        lock (_lock)
            if (_jobs.TryGetValue(transferId, out var job))
                job.Cts.Cancel();
    }

    public void CancelAll()
    {
        lock (_lock)
            foreach (var job in _jobs.Values)
                job.Cts.Cancel();
    }

    public void Retry(Guid transferId)
    {
        TransferJob? original;
        lock (_lock) _jobs.TryGetValue(transferId, out original);
        if (original is null) return;

        var retryJob = new TransferJob(transferId, original.Request, original.Progress);
        lock (_lock) _jobs[transferId] = retryJob;
        retryJob.Progress.Report(new TransferUpdate { Status = TransferStatus.Queued });
        _queue.Writer.TryWrite(retryJob);
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var job in _queue.Reader.ReadAllAsync())
            await RunJobAsync(job);
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

    private readonly record struct FileTransferResult(TransferStatus Status, Exception? Error = null);
    private async Task<FileTransferResult> TransferFileCoreAsync(
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
                    if (operation == TransferOperation.Copy)
                        await sourceProvider.CopyAsync(sourcePath, destinationFolder, conflictResolver, nativeProgress, token);
                    else
                        await sourceProvider.MoveAsync(sourcePath, destinationFolder, conflictResolver, nativeProgress, token);

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

    private async Task<bool> DeleteEmptyDirectoriesAsync(IStorageProvider provider, StoragePath path, CancellationToken token)
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

    private sealed class ByteAccumulator { public long Completed; }
    private sealed class FolderTransferSummary
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

    private async Task RunFolderJobRecursivelyAsync(
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
        _queue.Writer.Complete();
        await _workerLoop;
    }
}

internal sealed class TransferJob
{
    public TransferJob(Guid id, TransferRequest request, IProgress<TransferUpdate> progress)
    {
        Id = id;
        Request = request;
        Progress = progress;
    }

    public Guid Id { get; }
    public TransferRequest Request { get; }
    public IProgress<TransferUpdate> Progress { get; }
    public CancellationTokenSource Cts { get; } = new();
}
