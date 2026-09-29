using FileManager.Core.Models;
using FileManager.Core.Providers;
using FileManager.Core.Transfers;
using FileManager.TestKit;
using System.Text;
using Xunit;

namespace FileManager.Core.Tests.Transfers;

public class TransferFileCoreAsyncTests
{
    private static StoragePath P(string providerId, string value) => new() { ProviderId = providerId, Value = value };

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

    private sealed class RecordingProgress : IProgress<TransferUpdate>
    {
        public List<TransferUpdate> Updates { get; } = new();
        public void Report(TransferUpdate value) => Updates.Add(value);
    }

    // --- Same provider ---

    [Fact]
    public async Task SameProvider_Copy_Succeeds_OnFirstAttempt()
    {
        var provider = new FakeStorageProvider();
        var file = P("fake", "/root/a.txt");
        var destinationFolder = P("fake", "/dest");

        provider.AddChildren("/root", MakeFile(file, 10));
        provider.AddChildren("/dest");

        await using var manager = new TransferManager();
        var progress = new RecordingProgress();

        var result = await manager.TransferFileCoreAsync(
            provider, provider, TransferOperation.Copy,
            file, destinationFolder, AlwaysFail, RetryPolicy.None,
            CancellationToken.None, progress);

        Assert.Equal(TransferStatus.Succeeded, result.Status);
        Assert.Null(result.Error);
        Assert.True(await provider.ExistsAsync(destinationFolder.Combine("a.txt")));
    }

    [Fact]
    public async Task SameProvider_Copy_RetriesAfterFailure_ThenSucceeds()
    {
        var provider = new FakeStorageProvider();
        var file = P("fake", "/root/a.txt");
        var destinationFolder = P("fake", "/dest");

        provider.AddChildren("/root", MakeFile(file, 10));
        provider.AddChildren("/dest");
        provider.FailNextCopyAttempts(1);

        await using var manager = new TransferManager();
        var progress = new RecordingProgress();

        var result = await manager.TransferFileCoreAsync(
            provider, provider, TransferOperation.Copy,
            file, destinationFolder, AlwaysFail, RetryPolicy.Automatic(2),
            CancellationToken.None, progress);

        Assert.Equal(TransferStatus.Succeeded, result.Status);
        // Guards the earlier bug where a failed attempt never actually retried.
        Assert.Contains(progress.Updates, u => u.AttemptNumber == 1);
        Assert.Contains(progress.Updates, u => u.AttemptNumber == 2);
    }

    [Fact]
    public async Task SameProvider_Copy_FailsAfterExhaustingRetries()
    {
        var provider = new FakeStorageProvider();
        var file = P("fake", "/root/a.txt");
        var destinationFolder = P("fake", "/dest");

        provider.AddChildren("/root", MakeFile(file, 10));
        provider.AddChildren("/dest");
        provider.FailNextCopyAttempts(3);

        await using var manager = new TransferManager();
        var progress = new RecordingProgress();

        var result = await manager.TransferFileCoreAsync(
            provider, provider, TransferOperation.Copy,
            file, destinationFolder, AlwaysFail, RetryPolicy.Automatic(3),
            CancellationToken.None, progress);

        Assert.Equal(TransferStatus.Failed, result.Status);
        Assert.NotNull(result.Error);
        Assert.Equal(3, progress.Updates.Count(u => u.Status == TransferStatus.Running));
    }

    [Fact]
    public async Task SameProvider_Move_CancellationPropagates_DoesNotDeleteSource()
    {
        var provider = new FakeStorageProvider();
        var file = P("fake", "/root/a.txt");
        var destinationFolder = P("fake", "/dest");

        provider.AddChildren("/root", MakeFile(file, 10));
        provider.AddChildren("/dest");

        await using var manager = new TransferManager();
        var progress = new RecordingProgress();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Guards the earlier bug where a cancelled Move silently reported Succeeded
        // and deleted the source over a truncated destination.
        await Assert.ThrowsAsync<OperationCanceledException>(() => manager.TransferFileCoreAsync(
            provider, provider, TransferOperation.Move,
            file, destinationFolder, AlwaysFail, RetryPolicy.None,
            cts.Token, progress));

        Assert.True(await provider.ExistsAsync(file));
        Assert.DoesNotContain(progress.Updates, u => u.Status == TransferStatus.Succeeded);
    }

    // --- Cross provider ---

    [Fact]
    public async Task CrossProvider_Copy_Succeeds_CopiesContentByteForByte()
    {
        var source = new FakeStorageProvider("provider-a");
        var destination = new FakeStorageProvider("provider-b");
        var sourceFile = P("provider-a", "/root/a.txt");
        var destinationFolder = P("provider-b", "/dest");
        var content = Encoding.UTF8.GetBytes("hello world");

        source.AddChildren("/root", MakeFile(sourceFile, content.Length));
        source.SetContent(sourceFile, content);
        destination.AddChildren("/dest");

        await using var manager = new TransferManager();
        var progress = new RecordingProgress();

        var result = await manager.TransferFileCoreAsync(
            source, destination, TransferOperation.Copy,
            sourceFile, destinationFolder, AlwaysFail, RetryPolicy.None,
            CancellationToken.None, progress);

        Assert.Equal(TransferStatus.Succeeded, result.Status);

        await using var stream = await destination.OpenReadAsync(destinationFolder.Combine("a.txt"));
        using var reader = new StreamReader(stream);
        Assert.Equal("hello world", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task CrossProvider_Copy_DestinationExists_Skip_DoesNotOverwrite()
    {
        var source = new FakeStorageProvider("provider-a");
        var destination = new FakeStorageProvider("provider-b");
        var sourceFile = P("provider-a", "/root/a.txt");
        var destinationFolder = P("provider-b", "/dest");
        var existingFile = destinationFolder.Combine("a.txt");

        source.AddChildren("/root", MakeFile(sourceFile, 5));
        source.SetContent(sourceFile, Encoding.UTF8.GetBytes("hello"));

        destination.AddChildren("/dest", MakeFile(existingFile, 3));
        destination.SetContent(existingFile, Encoding.UTF8.GetBytes("old"));

        await using var manager = new TransferManager();
        var progress = new RecordingProgress();

        var result = await manager.TransferFileCoreAsync(
            source, destination, TransferOperation.Copy,
            sourceFile, destinationFolder, AlwaysSkip, RetryPolicy.None,
            CancellationToken.None, progress);

        Assert.Equal(TransferStatus.Skipped, result.Status);

        await using var stream = await destination.OpenReadAsync(existingFile);
        using var reader = new StreamReader(stream);
        Assert.Equal("old", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task CrossProvider_Move_DeletesSourceAfterSuccess()
    {
        var source = new FakeStorageProvider("provider-a");
        var destination = new FakeStorageProvider("provider-b");
        var sourceFile = P("provider-a", "/root/a.txt");
        var destinationFolder = P("provider-b", "/dest");

        source.AddChildren("/root", MakeFile(sourceFile, 5));
        source.SetContent(sourceFile, Encoding.UTF8.GetBytes("hello"));
        destination.AddChildren("/dest");

        await using var manager = new TransferManager();
        var progress = new RecordingProgress();

        var result = await manager.TransferFileCoreAsync(
            source, destination, TransferOperation.Move,
            sourceFile, destinationFolder, AlwaysFail, RetryPolicy.None,
            CancellationToken.None, progress);

        Assert.Equal(TransferStatus.Succeeded, result.Status);
        Assert.False(await source.ExistsAsync(sourceFile));
    }
}