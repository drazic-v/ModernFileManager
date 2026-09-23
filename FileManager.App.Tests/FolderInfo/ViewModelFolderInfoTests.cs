using Avalonia.Headless.XUnit;
using FileManager.App.Tests.Fakes;
using FileManager.TestKit;
using Xunit;
using static FileManager.App.Tests.Fakes.TestItems;

namespace FileManager.App.Tests.FolderInfo;

public class ViewModelFolderInfoTests
{
    [AvaloniaFact]
    public async Task SingleFolderSelected_WithDetailsPanelOpen_ComputesSizeAndCounts()
    {
        var provider = new FakeStorageProvider();
        var root = Path("/root");
        var docs = Folder(Path("/root/docs"));
        var sub = Folder(Path("/root/docs/sub"));
        provider.AddChildren(root.Value, docs);
        provider.AddChildren(docs.Path.Value,
            File(Path("/root/docs/a.txt"), 100),
            File(Path("/root/docs/b.txt"), 200),
            sub);
        provider.AddChildren(sub.Path.Value, File(Path("/root/docs/sub/c.txt"), 50));

        var vm = new TestableViewModel(provider, root, new FakeNotificationService());
        await vm.RefreshAsync();
        vm.IsDetailsPanelOpen = true;
        vm.SelectedItems.Add(docs); // also fires OnSelectedItemsChanged's own fire-and-forget call;
                                    // harmless, since UpdateFolderInfoAsync cancels-and-replaces its
                                    // own token at the top, and we await the call that runs last.

        await vm.UpdateFolderInfoAsync();

        Assert.Equal(350, vm.FolderSizeInBytes);
        Assert.Equal(3, vm.FolderFileCount);
        Assert.Equal(1, vm.FolderFolderCount);
        Assert.False(vm.IsFolderInfoLoading);
    }

    [AvaloniaFact]
    public async Task DetailsPanelClosed_DoesNotComputeFolderInfo()
    {
        var provider = new FakeStorageProvider();
        var root = Path("/root");
        var docs = Folder(Path("/root/docs"));
        provider.AddChildren(root.Value, docs);
        provider.AddChildren(docs.Path.Value, File(Path("/root/docs/a.txt"), 100));

        var vm = new TestableViewModel(provider, root, new FakeNotificationService());
        await vm.RefreshAsync();
        vm.SelectedItems.Add(docs); // IsDetailsPanelOpen left false

        await vm.UpdateFolderInfoAsync();

        Assert.Null(vm.FolderSizeInBytes);
    }

    [AvaloniaFact]
    public async Task MultipleFoldersSelected_DoesNotComputeSingleFolderInfo()
    {
        // FolderInfo is specifically the "exactly one item, and it's a folder" panel -
        // aggregate totals for a multi-selection are MultiSelectionSizeBytes's job.
        var provider = new FakeStorageProvider();
        var root = Path("/root");
        var folderA = Folder(Path("/root/a"));
        var folderB = Folder(Path("/root/b"));
        provider.AddChildren(root.Value, folderA, folderB);
        provider.AddChildren(folderA.Path.Value);
        provider.AddChildren(folderB.Path.Value);

        var vm = new TestableViewModel(provider, root, new FakeNotificationService());
        await vm.RefreshAsync();
        vm.IsDetailsPanelOpen = true;
        vm.SelectedItems.Add(folderA);
        vm.SelectedItems.Add(folderB);

        await vm.UpdateFolderInfoAsync();

        Assert.Null(vm.FolderSizeInBytes);
    }

    [AvaloniaFact]
    public async Task SelectingAFile_DoesNotComputeFolderInfo()
    {
        var provider = new FakeStorageProvider();
        var root = Path("/root");
        var file = File(Path("/root/a.txt"), 100);
        provider.AddChildren(root.Value, file);

        var vm = new TestableViewModel(provider, root, new FakeNotificationService());
        await vm.RefreshAsync();
        vm.IsDetailsPanelOpen = true;
        vm.SelectedItems.Add(file);

        await vm.UpdateFolderInfoAsync();

        Assert.Null(vm.FolderSizeInBytes);
    }

    [AvaloniaFact]
    public async Task SwitchingSelection_RecomputesForTheNewFolder()
    {
        var provider = new FakeStorageProvider();
        var root = Path("/root");
        var folderA = Folder(Path("/root/a"));
        var folderB = Folder(Path("/root/b"));
        provider.AddChildren(root.Value, folderA, folderB);
        provider.AddChildren(folderA.Path.Value, File(Path("/root/a/x.txt"), 500));
        provider.AddChildren(folderB.Path.Value);

        var vm = new TestableViewModel(provider, root, new FakeNotificationService());
        await vm.RefreshAsync();
        vm.IsDetailsPanelOpen = true;
        vm.SelectedItems.Add(folderA);
        await vm.UpdateFolderInfoAsync();
        Assert.Equal(500, vm.FolderSizeInBytes);

        vm.SelectedItems.Clear();
        vm.SelectedItems.Add(folderB);
        await vm.UpdateFolderInfoAsync();

        Assert.Equal(0, vm.FolderSizeInBytes);
        Assert.Equal(0, vm.FolderFileCount);
    }
}