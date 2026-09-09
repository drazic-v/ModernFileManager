using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Metadata;
using FileManager.App.ViewModels;
using FileManager.Core.Models;

namespace FileManager.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if(sender is DataGrid { DataContext: MainViewModel vm, SelectedItem: StorageItem item })
            await vm.NavigateIntoAsync(item);
    }

    private async void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter &&
            sender is TextBox { DataContext: WorkspaceViewModel workspace } &&
            workspace.SelectedTab is { } tab)
        {
            await tab.SearchCurrentFolderAsync(tab.SearchText);
        }
    }

    private async void OnDeleteMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: MainViewModel tab, CommandParameter: StorageItem item }) return;

        var dialog = new ConfirmDialog($"Delete \"{item.Name}\"? This can't be undone.");
        if (await dialog.ShowDialog<bool>(this))
            await tab.DeleteItemAsync(item);
    }
}