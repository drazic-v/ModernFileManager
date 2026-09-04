using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Metadata;
using FileManager.App.ViewModels;

namespace FileManager.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is WorkspaceViewModel vm && vm.SelectedTab.SelectedItem is { } item)
            await vm.SelectedTab.NavigateIntoAsync(item);
    }

    private async void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is WorkspaceViewModel vm)
            await vm.SelectedTab.SearchCurrentFolderAsync(vm.SelectedTab.SearchText);
    }
}