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
        if (DataContext is ViewModelBase vm && vm.SelectedItem is { } item)
            await vm.NavigateIntoAsync(item);
    }
}