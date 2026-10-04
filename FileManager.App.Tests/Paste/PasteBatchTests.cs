using FileManager.App.ViewModels;
using FileManager.Core.Transfers;
using static FileManager.App.Tests.Fakes.TestItems;

namespace FileManager.App.Tests.Paste;

public class PasteBatchTests
{
    // Builds a batch over files of the given sizes and records every percent it reports.
    private static (PasteBatch Batch, List<double> Percents) Create(params long[] sizes)
    {
        var items = sizes.Select((size, i) => File(Path($"/src/f{i}.bin"), size)).ToList();
        var percents = new List<double>();
        var batch = new PasteBatch(items, sizes, percents.Add);
        return (batch, percents);
    }

    [Fact]
    public void Apply_UpdateAfterTerminal_IsIgnored()
    {
        // item 0 makes progress, then finishes.
        var (batch, percents) = Create(100, 300);
        batch.Apply(0, new TransferUpdate { Status = TransferStatus.Running, BytesCopied = 50 });
        batch.Apply(0, new TransferUpdate { Status = TransferStatus.Succeeded });
        var reportsBefore = percents.Count;

        // the stale Running update that lost the ThreadPool race.
        batch.Apply(0, new TransferUpdate { Status = TransferStatus.Running, BytesCopied = 80 });

        var entry = batch.Entries[0];
        Assert.Equal(TransferStatus.Succeeded, entry.Status);
        Assert.Equal(50, entry.Bytes);
        Assert.Equal(reportsBefore, percents.Count);
    }

    [Fact]
    public void Apply_LowerBytesAfterRetry_DoesNotMoveBackwards()
    {
        var (batch, percents) = Create(100, 300);
        batch.Apply(0, new TransferUpdate { Status = TransferStatus.Running, BytesCopied = 50 });
        batch.Apply(0, new TransferUpdate { Status = TransferStatus.Running, BytesCopied = 80 });
        var reportsBefore = percents[0];
        batch.Apply(0, new TransferUpdate { Status = TransferStatus.Running, BytesCopied = 60 }); // retry attempt
        var entry = batch.Entries[0];
        Assert.Equal(80, entry.Bytes);
        Assert.Equal(reportsBefore, percents[0]);
    }

    [Fact]
    public void Apply_AllItemsZeroSize_PercentCountsFinishedItems()
    {
        var (batch, percents) = Create(0, 0);
        batch.Apply(0, new TransferUpdate { Status = TransferStatus.Succeeded });
        batch.Apply(1, new TransferUpdate { Status = TransferStatus.Succeeded });
        Assert.Equal(new[] { 50.0, 100.0 }, percents);
    }

    [Fact]
    public void Apply_BytesExceedMeasuredSize_ItemIsCappedAtItsSize()
    {
        var (batch, percents) = Create(100, 300);
        batch.Apply(0, new TransferUpdate { Status = TransferStatus.Running, BytesCopied = 150 });
        batch.Apply(1, new TransferUpdate { Status = TransferStatus.Running, BytesCopied = 400 });
        Assert.Equal(new[] { 25.0, 100.0 }, percents);
    }

    [Fact]
    public void WhenAllFinished_SomeItemsStillRunning_IsNotCompleted()
    {
        var (batch, percents) = Create(100, 300);
        batch.Apply(0, new TransferUpdate { Status = TransferStatus.Running, BytesCopied = 50 });
        batch.Apply(1, new TransferUpdate { Status = TransferStatus.Succeeded });
        Assert.False(batch.WhenAllFinished.IsCompleted);
    }

    [Fact]
    public async Task WhenAllFinished_AllItemsTerminal_Completes()
    {
        var (batch, percents) = Create(100, 300);
        var finished = batch.WhenAllFinished;  
        batch.Apply(0, new TransferUpdate { Status = TransferStatus.Succeeded });
        batch.Apply(1, new TransferUpdate { Status = TransferStatus.Failed });
        await finished.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.True(finished.IsCompleted);
    }

    [Fact]
    public void Apply_TerminalUpdate_RecordsStatusAndErrorOnTheRightEntry()
    {
        var (batch, percents) = Create(100, 300);
        batch.Apply(1, new TransferUpdate { Status = TransferStatus.Failed, Error = new Exception("boom") });
        batch.Apply(0, new TransferUpdate { Status = TransferStatus.Succeeded });
        var failed = batch.Entries[1];
        var succeeded = batch.Entries[0];
        Assert.Equal(TransferStatus.Succeeded, succeeded.Status);
        Assert.Equal(TransferStatus.Failed, failed.Status);
        Assert.Equal("boom", failed.Error?.Message);
    }

    [Theory]    
    [InlineData(TransferStatus.Skipped)]
    [InlineData(TransferStatus.Succeeded)]
    [InlineData(TransferStatus.Failed)]
    public void Apply_TerminalUpdate_CountsItemsFullSize(TransferStatus status)
    {
        var (batch, percents) = Create(100, 300);
        batch.Apply(0, new TransferUpdate { Status = TransferStatus.Running, BytesCopied = 30 });
        batch.Apply(0, new TransferUpdate { Status = status });
        Assert.Equal(new[] { 7.5, 25.0 }, percents);
    }
}