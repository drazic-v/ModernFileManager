using FileManager.Core.Providers;
using FileManager.Core.Transfers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FileManager.TestKit
{
    /// <summary>
    /// Like RecordingProgress<T>, but   also completes a Task once a terminal
    /// TransferUpdate arrives - so a test can `await progress.Completion` instead
    /// of polling after calling the real, async Submit().
    /// </summary>
    public sealed class TerminalAwaitingProgress : IProgress<TransferUpdate>
    {
        private readonly object _lock = new();
        private TaskCompletionSource<TransferUpdate> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentQueue<TransferUpdate> _updates = new();

        public IReadOnlyList<TransferUpdate> Updates => _updates.ToList();
        public Task<TransferUpdate> Completion { get { lock (_lock) return _tcs.Task; } }

        public void Report(TransferUpdate value)
        {
            _updates.Enqueue(value);
            if (IsTerminal(value.Status))
                lock (_lock) _tcs.TrySetResult(value);
        }

        /// <summary>Call after observing one terminal status and before triggering another
        /// (e.g. before Retry()), so Completion represents the *next* terminal report.</summary>
        /// <summary>Call only after the current Completion task has already finished
        /// (e.g. right before a Retry()) - never speculatively or concurrently with a
        /// possible in-flight Report(), or a report could complete the wrong cycle's task.</summary>
        public void Reset()
        {
            lock (_lock) _tcs = new TaskCompletionSource<TransferUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static bool IsTerminal(TransferStatus status) =>
            status is TransferStatus.Succeeded or TransferStatus.Failed
                or TransferStatus.Skipped or TransferStatus.Cancelled;
    }
}