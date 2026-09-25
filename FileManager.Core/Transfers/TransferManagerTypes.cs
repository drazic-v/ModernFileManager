using FileManager.Core.Models;
using FileManager.Core.Providers;

namespace FileManager.Core.Transfers;

public enum TransferOperation { Copy, Move }

public enum TransferStatus { Queued, Running, Succeeded, Skipped, Failed, Cancelled }

public sealed record RetryPolicy
{
    public required int MaxAttempts { get; init; } // 1 = no automatic retry

    public static RetryPolicy None => new() { MaxAttempts = 1 };
    public static RetryPolicy Automatic(int maxAttempts) => new() { MaxAttempts = maxAttempts };
}

public sealed record TransferRequest
{
    public required IStorageProvider SourceProvider { get; init; }
    public required StoragePath SourcePath { get; init; }
    public required IStorageProvider DestinationProvider { get; init; }
    public required StoragePath DestinationFolder { get; init; }
    public required TransferOperation Operation { get; init; }
    public required ConflictResolver ConflictResolver { get; init; }
    public RetryPolicy RetryPolicy { get; init; } = RetryPolicy.None;
}

public sealed record TransferUpdate
{
    public required TransferStatus Status { get; init; }
    public int AttemptNumber { get; init; } = 1;
    public long BytesCopied { get; init; }
    public long TotalBytes { get; init; }
    public Exception? Error { get; init; }
}