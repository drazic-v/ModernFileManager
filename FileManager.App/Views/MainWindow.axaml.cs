using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Metadata;
using FileManager.App.ViewModels;
using FileManager.Core.Models;
using System.Linq;
using System.Threading.Tasks;

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
            await vm.OpenItemAsync(item);
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
        if (await dialog.ShowDialog<bool>(this)){
            await tab.DeleteItemAsync(item);
            if (DataContext is WorkspaceViewModel workspace)
                await workspace.RefreshTabsViewingAsync(tab, tab.CurrentFolder);
        }
    }

    private void OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return; // Escape was pressed - nothing to do
        if (e.EditingElement is not TextBox textBox) return;
        if (e.Row.DataContext is not StorageItem item) return;
        if (sender is not DataGrid { DataContext: MainViewModel tab }) return;

        var newName = textBox.Text?.Trim();
        if (string.IsNullOrEmpty(newName) || newName == item.Name) return;

        _ = RenameAndRefreshAsync(tab, item, newName);
    }

    private bool _renameRequestedProgrammatically;

    private void OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (!_renameRequestedProgrammatically)
            e.Cancel = true; // block F2 and double-click - Rename only starts from the context menu now
    }

    private void BeginProgrammaticEdit(DataGrid grid)
    {
        _renameRequestedProgrammatically = true;
        try
        {
            grid.BeginEdit();
        }
        finally
        {
            _renameRequestedProgrammatically = false;
        }
    }

    private void OnRenameMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: DataGrid grid, CommandParameter: StorageItem item }) return;

        grid.SelectedItem = item;
        grid.CurrentColumn = grid.Columns.First(c => c.Header as string == "Name");

        BeginProgrammaticEdit(grid);
    }
    private async Task RenameAndRefreshAsync(MainViewModel tab, StorageItem item, string newName)
    {
        await tab.RenameItemAsync(item, newName);
        if (DataContext is WorkspaceViewModel workspace)
            await workspace.RefreshTabsViewingAsync(tab, tab.CurrentFolder);
    }

    private async void OnNewFolderMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: DataGrid grid } || grid.DataContext is not MainViewModel tab) return;

        var newItem = await tab.CreateFolderAsync();
        if (newItem is null) return;

        var nameColumn = grid.Columns.First(c => c.Header as string == "Name");
        grid.SelectedItem = newItem;
        grid.ScrollIntoView(newItem, nameColumn);
        grid.CurrentColumn = nameColumn;

        BeginProgrammaticEdit(grid);


        if (DataContext is WorkspaceViewModel workspace)
            _ = workspace.RefreshTabsViewingAsync(tab, tab.CurrentFolder);
    }
}