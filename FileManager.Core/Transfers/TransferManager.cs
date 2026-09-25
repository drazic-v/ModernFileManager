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

    private async Task RunJobAsync(TransferJob job) => throw new NotImplementedException("Transfer logic is not implemented yet.");

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
