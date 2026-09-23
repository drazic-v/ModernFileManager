using Avalonia.Headless.XUnit;
using FileManager.App.Tests.Fakes;
using FileManager.TestKit;
using Xunit;
using static FileManager.App.Tests.Fakes.TestItems;

namespace FileManager.App.Tests.Search;

public class ViewModelSearchTests
{
    [AvaloniaFact]
    public async Task SearchCurrentFolderAsync_WithMatches_PopulatesItemsAndActivatesSearch()
    {
        var provider = new FakeStorageProvider();
        var root = Path("/root");
        provider.AddChildren(root.Value);
        provider.AddSearchResults(root.Value, "report", File(Path("/root/deep/report.docx")));

        var vm = new TestableViewModel(provider, root, new FakeNotificationService());
        await vm.RefreshAsync();

        await vm.SearchCurrentFolderAsync("report");

        Assert.True(vm.IsSearchActive);
        Assert.Single(vm.Items);
        Assert.Equal("report.docx", vm.Items[0].Name);
    }

    [AvaloniaFact]
    public async Task SearchCurrentFolderAsync_FiltersHiddenItems_UnlessShowHiddenItemsIsOn()
    {
        var provider = new FakeStorageProvider();
        var root = Path("/root");
        provider.AddChildren(root.Value);
        provider.AddSearchResults(root.Value, "x",
            File(Path("/root/x.txt")),
            File(Path("/root/.x-hidden"), attributes: FileAttributes.Hidden));

        var vm = new TestableViewModel(provider, root, new FakeNotificationService());
        await vm.RefreshAsync();

        await vm.SearchCurrentFolderAsync("x");
        Assert.Single(vm.Items);

        vm.ShowHiddenItems = true;
        await vm.SearchCurrentFolderAsync("x");
        Assert.Equal(2, vm.Items.Count);
    }

    [AvaloniaFact]
    public async Task SearchCurrentFolderAsync_EmptyQuery_ReloadsCurrentFolderInstead()
    {
        var provider = new FakeStorageProvider();
        var root = Path("/root");
        provider.AddChildren(root.Value, File(Path("/root/plain.txt")));
        // deliberately no search results registered for "" - if this hit SearchAsync instead
        // of falling back to LoadAsync, Items would end up empty.

        var vm = new TestableViewModel(provider, root, new FakeNotificationService());
        await vm.RefreshAsync();

        await vm.SearchCurrentFolderAsync(string.Empty);

        Assert.False(vm.IsSearchActive);
        Assert.Single(vm.Items);
        Assert.Equal("plain.txt", vm.Items[0].Name);
    }

    [AvaloniaFact]
    public async Task ClearSearchAsync_ResetsSearchTextAndReloadsFolder()
    {
        var provider = new FakeStorageProvider();
        var root = Path("/root");
        provider.AddChildren(root.Value, File(Path("/root/plain.txt")));
        provider.AddSearchResults(root.Value, "plain", File(Path("/root/plain.txt")));

        var vm = new TestableViewModel(provider, root, new FakeNotificationService());
        await vm.RefreshAsync();
        vm.SearchText = "plain";
        await vm.SearchCurrentFolderAsync(vm.SearchText);
        Assert.True(vm.IsSearchActive);

        await vm.ClearSearchAsync();

        Assert.Equal(string.Empty, vm.SearchText);
        Assert.False(vm.IsSearchActive);
        Assert.Single(vm.Items);
    }

    [AvaloniaFact]
    public async Task SearchCurrentFolderAsync_WhenProviderThrows_ReportsErrorAndLeavesItemsEmpty()
    {
        var provider = new FakeStorageProvider();
        var root = Path("/root");
        provider.AddChildren(root.Value);
        provider.ThrowOnSearch(root.Value, "boom", new IOException("disk error"));

        var notifications = new FakeNotificationService();
        var vm = new TestableViewModel(provider, root, notifications);
        await vm.RefreshAsync();

        await vm.SearchCurrentFolderAsync("boom");

        Assert.Empty(vm.Items);
        Assert.Single(notifications.Errors);
    }
}