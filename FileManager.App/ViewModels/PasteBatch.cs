using FileManager.Core.Models;
using FileManager.Core.Transfers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FileManager.App.ViewModels;

/// <summary>Tracks one paste: per-item results plus an aggregate percent across all items.</summary>
public sealed class PasteBatch
{
    public sealed class Entry
    {
        internal Entry(StorageItem item, long size) { Item = item; Size = size; }
        public StorageItem Item { get; }
        public long Size { get; }
        public Guid TransferId { get; set; }
        public TransferStatus Status { get; internal set; } = TransferStatus.Queued; // meaningful once finished
        public Exception? Error { get; internal set; }
        internal long Bytes;
        internal TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly object _lock = new();
    private readonly Action<double> _onPercent;
    private readonly long _totalBytes;
    private int _finished;

    public PasteBatch(IReadOnlyList<StorageItem> items, IReadOnlyList<long> sizes, Action<double> onPercent)
    {
        Entries = items.Select((item, i) => new Entry(item, sizes[i])).ToList();
        _totalBytes = sizes.Sum();
        _onPercent = onPercent;
    }

    public IReadOnlyList<Entry> Entries { get; }

    public Task WhenAllFinished => Task.WhenAll(Entries.Select(e => e.Done.Task));

    /// <summary>Call on the UI thread: Progress<T>; captures the current SynchronizationContext,
    /// so updates are delivered (in order) on the UI thread.</summary>
    public IProgress<TransferUpdate> CreateProgress(int index) =>
        new Progress<TransferUpdate>(update => Apply(index, update));

    /// <summary>Tolerates late, out-of-order, or concurrent updates (e.g. no SynchronizationContext in tests).</summary>
    public void Apply(int index, TransferUpdate update)
    {
        lock (_lock)
        {
            var entry = Entries[index];
            if (IsTerminal(entry.Status)) return;                      // anything after the terminal update is stale

            entry.Bytes = Math.Max(entry.Bytes, update.BytesCopied);   // never move backwards (retry attempts)
            if (IsTerminal(update.Status))
            {
                entry.Status = update.Status;
                entry.Error = update.Error;
                _finished++;
                entry.Done.TrySetResult();
            }

            // Invoked under the lock so concurrent callers can't deliver percents out of order.
            _onPercent(ComputePercent());
        }
    }

    private double ComputePercent()
    {
        if (_totalBytes <= 0)
            return (double)_finished / Entries.Count * 100;   // e.g. only empty files

        long done = 0;
        foreach (var e in Entries)
            done += IsTerminal(e.Status) ? e.Size : Math.Min(e.Bytes, e.Size);
        return Math.Min(100, (double)done / _totalBytes * 100);
    }

    private static bool IsTerminal(TransferStatus s) =>
        s is TransferStatus.Succeeded or TransferStatus.Failed or TransferStatus.Skipped or TransferStatus.Cancelled;
}