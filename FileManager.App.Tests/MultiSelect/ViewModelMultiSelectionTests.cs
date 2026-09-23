using Avalonia.Headless.XUnit;
using FileManager.App.Tests.Fakes;
using FileManager.TestKit;
using Xunit;
using static FileManager.App.Tests.Fakes.TestItems;

namespace FileManager.App.Tests.MultiSelect;

public class ViewModelMultiSelectionTests
{
    [AvaloniaFact]
    public async Task TwoOrMoreItemsSelected_WithDetailsPanelOpen_SumsTheirSizes()
    {
        var provider = new FakeStorageProvider();
        var root = Path("/root");
        var fileA = File(Path("/root/a.txt"), 100);
        var fileB = File(Path("/root/b.txt"), 250);
        var folder = Folder(Path("/root/docs"));
        provider.AddChildren(root.Value, fileA, fileB, folder);
        provider.AddChildren(folder.Path.Value, File(Path("/root/docs/c.txt"), 50));

        var vm = new TestableViewModel(provider, root, new FakeNotificationService());
        await vm.RefreshAsync();
        vm.IsDetailsPanelOpen = true;
        vm.SelectedItems.Add(fileA);
        vm.SelectedItems.Add(fileB);
        vm.SelectedItems.Add(folder);

        await vm.UpdateMultiSelectionInfoAsync();

        Assert.Equal(400, vm.MultiSelectionSizeBytes); // 100 + 250 + 50
        Assert.False(vm.IsMultiSelectionSizeLoading);
    }

    [AvaloniaFact]
    public async Task SingleItemSelected_DoesNotComputeMultiSelectionSize()
    {
        var provider = new FakeStorageProvider();
        var root = Path("/root");
        var file = File(Path("/root/a.txt"), 100);
        provider.AddChildren(root.Value, file);

        var vm = new TestableViewModel(provider, root, new FakeNotificationService());
        await vm.RefreshAsync();
        vm.IsDetailsPanelOpen = true;
        vm.SelectedItems.Add(file);

        await vm.UpdateMultiSelectionInfoAsync();

        Assert.Null(vm.MultiSelectionSizeBytes);
    }

    [AvaloniaFact]
    public async Task DetailsPanelClosed_DoesNotComputeMultiSelectionSize()
    {
        var provider = new FakeStorageProvider();
        var root = Path("/root");
        var fileA = File(Path("/root/a.txt"), 100);
        var fileB = File(Path("/root/b.txt"), 200);
        provider.AddChildren(root.Value, fileA, fileB);

        var vm = new TestableViewModel(provider, root, new FakeNotificationService());
        await vm.RefreshAsync();
        vm.SelectedItems.Add(fileA);
        vm.SelectedItems.Add(fileB); // IsDetailsPanelOpen left false

        await vm.UpdateMultiSelectionInfoAsync();

        Assert.Null(vm.MultiSelectionSizeBytes);
    }

    [AvaloniaFact]
    public async Task ClearingSelection_ResetsMultiSelectionSizeToNull()
    {
        var provider = new FakeStorageProvider();
        var root = Path("/root");
        var fileA = File(Path("/root/a.txt"), 100);
        var fileB = File(Path("/root/b.txt"), 200);
        provider.AddChildren(root.Value, fileA, fileB);

        var vm = new TestableViewModel(provider, root, new FakeNotificationService());
        await vm.RefreshAsync();
        vm.IsDetailsPanelOpen = true;
        vm.SelectedItems.Add(fileA);
        vm.SelectedItems.Add(fileB);
        await vm.UpdateMultiSelectionInfoAsync();
        Assert.NotNull(vm.MultiSelectionSizeBytes);

        vm.SelectedItems.Clear();
        await vm.UpdateMultiSelectionInfoAsync();

        Assert.Null(vm.MultiSelectionSizeBytes);
    }
}