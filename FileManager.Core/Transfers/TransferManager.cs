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
