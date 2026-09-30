using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace FileManager.TestKit
{
    /// <summary>
    /// IProgress<T> test double. Backed by a ConcurrentQueue because System.Progress<T>
    /// marshals Report() through the captured SynchronizationContext - or through the ThreadPool
    /// when there is none, as in a plain xUnit test - so Report() calls can land on a different
    /// thread than the one that triggered them, and can race each other. A plain List<T>
    /// here is a real "Collection was modified" crash waiting to happen.
    /// </summary>
    public sealed class RecordingProgress<T> : IProgress<T>
    {
        private readonly ConcurrentQueue<T> _updates = new();
        public IReadOnlyList<T> Updates => _updates.ToList();
        public void Report(T value) => _updates.Enqueue(value);
    }
}