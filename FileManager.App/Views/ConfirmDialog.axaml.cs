using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.Generic;

namespace FileManager.App.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public ConfirmDialog(string message) : this()
    {
        MessageText.Text = message;
    }

    public ConfirmDialog(string message, IReadOnlyList<string> itemNames) : this()
    {
        MessageText.Text = message;
        if (itemNames.Count > 1)
        {
            ItemListControl.ItemsSource = itemNames;
            ItemListScroll.IsVisible = true;
        }
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}