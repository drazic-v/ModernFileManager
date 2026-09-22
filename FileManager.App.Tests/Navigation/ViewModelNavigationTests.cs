using FileManager.App.Tests.Fakes;
using FileManager.Core.Models;
using FileManager.TestKit;
using Xunit;
using static FileManager.App.Tests.Fakes.TestItems;

namespace FileManager.App.Tests.Navigation;

public class ViewModelNavigationTests
{
    [Fact]
    public async Task Constructor_LoadsStartingFolder()
    {
        var provider = new FakeStorageProvider();
        var root = Path("/root");
        provider.AddChildren(root.Value, File(Path("/root/a.txt")));

        var vm = new TestableViewModel(provider, root, new FakeNotificationService());
        // the constructor kicks LoadAsync off fire-and-forget; RefreshAsync gives us a
        // deterministic point to await before asserting on state.
        await vm.RefreshAsync();

        Assert.Single(vm.Items);
        Assert.Equal("a.txt", vm.Items[0].Name);
        Assert.Equal(root, vm.CurrentFolder);
    }

    [Fact]
    public async Task NavigateIntoAsync_Directory_LoadsItsContentsAndUpdatesCurrentFolder()
    {
        var provider = new FakeStorageProvider();
        var root = Path("/root");
        var sub = Folder(Path("/root/sub"));
        provider.AddChildren(root.Value, sub);
        provider.AddChildren(sub.Path.Value, File(Path("/root/sub/file.txt")));

        var vm = new TestableViewModel(provider, root, new FakeNotificationService());
        await vm.RefreshAsync();

        await vm.NavigateIntoAsync(sub);

        Assert.Equal(sub.Path, vm.CurrentFolder);
        Assert.Single(vm.Items);
        Assert.Equal("file.txt", vm.Items[0].Name);
    }

    [Fact]
    public async Task NavigateIntoAsync_FileItem_DoesNotChangeCurrentFolder()
    {
        var provider = new FakeStorageProvider();
        var root = Path("/root");
        var file = File(Path("/root/file.txt"));
        provider.AddChildren(root.Value, file);

        var vm = new TestableViewModel(provider, root, new FakeNotificationService());
        await vm.RefreshAsync();

        await vm.NavigateIntoAsync(file);

        Assert.Equal(root, vm.CurrentFolder);
    }

    [Fact]
    public async Task NavigateUpAsync_FromSubfolder_ReturnsToParent()
    {
        var provider = new FakeStorageProvider();
        var root = Path("/root");
        var sub = Folder(Path("/root/sub"));
        provider.AddChildren(root.Value, sub);
        provider.AddChildren(sub.Path.Value);

        var vm = new TestableViewModel(provider, root, new FakeNotificationService());
        await vm.RefreshAsync();
        await vm.NavigateIntoAsync(sub);

        await vm.NavigateUpAsync();

        Assert.Equal(root, vm.CurrentFolder);
    }

    [Fact]
    public async Task BackAsync_AfterNavigatingIntoSubfolder_ReturnsToRootAndUpdatesCanGoState()
    {
        var provider = new FakeStorageProvider();
        var root = Path("/root");
        var sub = Folder(Path("/root/sub"));
        provider.AddChildren(root.Value, sub);
        provider.AddChildren(sub.Path.Value);

        var vm = new TestableViewModel(provider, root, new FakeNotificationService());
        await vm.RefreshAsync();
        await vm.NavigateIntoAsync(sub);
        Assert.True(vm.CanGoBack);

        await vm.BackAsync();

        Assert.Equal(root, vm.CurrentFolder);
        Assert.False(vm.CanGoBack);
        Assert.True(vm.CanGoForward);
    }

    [Fact]
    public async Task ForwardAsync_AfterBack_ReturnsToTheFolderWeCameFrom()
    {
        var provider = new FakeStorageProvider();
        var root = Path("/root");
        var sub = Folder(Path("/root/sub"));
        provider.AddChildren(root.Value, sub);
        provider.AddChildren(sub.Path.Value);

        var vm = new TestableViewModel(provider, root, new FakeNotificationService());
        await vm.RefreshAsync();
        await vm.NavigateIntoAsync(sub);
        await vm.BackAsync();

        await vm.ForwardAsync();

        Assert.Equal(sub.Path, vm.CurrentFolder);
        Assert.False(vm.CanGoForward);
        Assert.True(vm.CanGoBack);
    }

    [Fact]
    public async Task BackAsync_WithEmptyBackStack_DoesNothing()
    {
        var provider = new FakeStorageProvider();
        var root = Path("/root");
        provider.AddChildren(root.Value);

        var vm = new TestableViewModel(provider, root, new FakeNotificationService());
        await vm.RefreshAsync();

        await vm.BackAsync();

        Assert.Equal(root, vm.CurrentFolder);
        Assert.False(vm.CanGoBack);
    }

    [Fact]
    public async Task ForwardAsync_WithEmptyForwardStack_DoesNothing()
    {
        var provider = new FakeStorageProvider();
        var root = Path("/root");
        provider.AddChildren(root.Value);

        var vm = new TestableViewModel(provider, root, new FakeNotificationService());
        await vm.RefreshAsync();

        await vm.ForwardAsync();

        Assert.Equal(root, vm.CurrentFolder);
        Assert.False(vm.CanGoForward);
    }

    [Fact]
    public async Task NavigateIntoAsync_NewNavigationAfterBack_ClearsForwardStack()
    {
        var provider = new FakeStorageProvider();
        var root = Path("/root");
        var subA = Folder(Path("/root/a"));
        var subB = Folder(Path("/root/b"));
        provider.AddChildren(root.Value, subA, subB);
        provider.AddChildren(subA.Path.Value);
        provider.AddChildren(subB.Path.Value);

        var vm = new TestableViewModel(provider, root, new FakeNotificationService());
        await vm.RefreshAsync();
        await vm.NavigateIntoAsync(subA);
        await vm.BackAsync();
        Assert.True(vm.CanGoForward);

        await vm.NavigateIntoAsync(subB);

        Assert.False(vm.CanGoForward); // the old "forward to A" branch is gone once we branched off to B
    }

    [Fact]
    public async Task NavigateIntoAsync_WhenTargetFolderFailsToLoad_LeavesCurrentFolderAndBackStackUnchanged()
    {
        // Exercises the peek-then-verify guard directly: LoadAsync throws, reports the
        // error, and returns before touching CurrentFolder - so NavigateToAsync must not
        // push a stack entry for a navigation that never actually happened.
        var provider = new FakeStorageProvider();
        var root = Path("/root");
        var broken = Folder(Path("/root/broken"));
        provider.AddChildren(root.Value, broken);
        provider.ThrowOnList(broken.Path.Value, new IOException("access denied"));

        var notifications = new FakeNotificationService();
        var vm = new TestableViewModel(provider, root, notifications);
        await vm.RefreshAsync();

        await vm.NavigateIntoAsync(broken);

        Assert.Equal(root, vm.CurrentFolder);
        Assert.False(vm.CanGoBack);
        Assert.Single(notifications.Errors);
    }

    [Fact]
    public async Task Items_ExcludeHiddenByDefault_ButIncludeThemWhenShowHiddenItemsIsOn()
    {
        var provider = new FakeStorageProvider();
        var root = Path("/root");
        provider.AddChildren(root.Value,
            File(Path("/root/visible.txt")),
            File(Path("/root/.hidden"), attributes: FileAttributes.Hidden));

        var vm = new TestableViewModel(provider, root, new FakeNotificationService());
        await vm.RefreshAsync();
        Assert.Single(vm.Items);

        vm.ShowHiddenItems = true;
        await vm.RefreshAsync(); // ShowHiddenItems's setter already refreshes; this just gives a deterministic await point

        Assert.Equal(2, vm.Items.Count);
    }
}