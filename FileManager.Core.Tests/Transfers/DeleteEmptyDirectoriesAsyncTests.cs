using FileManager.Core.Models;
using FileManager.Core.Transfers;
using FileManager.TestKit;
using System.IO;
using Xunit;

namespace FileManager.Core.Tests.Transfers;

public class DeleteEmptyDirectoriesAsyncTests
{
    private static StoragePath P(string value) => new() { ProviderId = "fake", Value = value };

    private static StorageItem MakeFolder(StoragePath path) => new()
    {
        Path = path,
        Name = path.Name,
        Kind = StorageItemKind.Directory
    };

    private static StorageItem MakeFile(StoragePath path, long size) => new()
    {
        Path = path,
        Name = path.Name,
        Kind = StorageItemKind.File,
        SizeInBytes = size
    };

    [Fact]
    public async Task EmptyLeafDirectory_IsDeleted_ReturnsTrue()
    {
        var provider = new FakeStorageProvider();
        var folder = P("/root/empty");
        provider.AddChildren("/root", MakeFolder(folder));
        provider.AddChildren(folder.Value);

        await using var manager = new TransferManager();
        var result = await manager.DeleteEmptyDirectoriesAsync(provider, folder, CancellationToken.None);

        Assert.True(result);
        await Assert.ThrowsAsync<FileNotFoundException>(() => provider.GetInfoAsync(folder, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DirectoryContainingAFile_IsNotDeleted_ReturnsFalse()
    {
        var provider = new FakeStorageProvider();
        var folder = P("/root/docs");
        var file = P("/root/docs/a.txt");
        provider.AddChildren("/root", MakeFolder(folder));
        provider.AddChildren(folder.Value, MakeFile(file, 5));

        await using var manager = new TransferManager();
        var result = await manager.DeleteEmptyDirectoriesAsync(provider, folder, CancellationToken.None);

        Assert.False(result);
        Assert.NotNull(await provider.GetInfoAsync(folder, TestContext.Current.CancellationToken));
        Assert.NotNull(await provider.GetInfoAsync(file, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NestedEmptyDirectories_AreDeletedBottomUp()
    {
        var provider = new FakeStorageProvider();
        var top = P("/root/top");
        var sub = P("/root/top/sub");
        provider.AddChildren("/root", MakeFolder(top));
        provider.AddChildren(top.Value, MakeFolder(sub));
        provider.AddChildren(sub.Value);

        await using var manager = new TransferManager();
        var result = await manager.DeleteEmptyDirectoriesAsync(provider, top, CancellationToken.None);

        Assert.True(result);
        await Assert.ThrowsAsync<FileNotFoundException>(() => provider.GetInfoAsync(sub, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<FileNotFoundException>(() => provider.GetInfoAsync(top, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EmptySubdirectory_IsCleanedUp_EvenWhenParentIsNotFullyEmpty()
    {
        var provider = new FakeStorageProvider();
        var top = P("/root/top");
        var emptySub = P("/root/top/emptysub");
        var file = P("/root/top/a.txt");
        provider.AddChildren("/root", MakeFolder(top));
        provider.AddChildren(top.Value, MakeFolder(emptySub), MakeFile(file, 5));
        provider.AddChildren(emptySub.Value);

        await using var manager = new TransferManager();
        var result = await manager.DeleteEmptyDirectoriesAsync(provider, top, CancellationToken.None);

        Assert.False(result);
        await Assert.ThrowsAsync<FileNotFoundException>(() => provider.GetInfoAsync(emptySub, TestContext.Current.CancellationToken));
        Assert.NotNull(await provider.GetInfoAsync(top, TestContext.Current.CancellationToken));
        Assert.NotNull(await provider.GetInfoAsync(file, TestContext.Current.CancellationToken));
    }
}