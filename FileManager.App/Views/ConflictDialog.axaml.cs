using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FileManager.Core.Providers;

namespace FileManager.App.Views;

public readonly record struct ConflictDialogResult(NameCollisionPolicy Policy, bool ApplyToAll);

public partial class ConflictDialog : Window
{
    public ConflictDialogResult Result { get; private set; } = new(NameCollisionPolicy.Skip, false);

    public ConflictDialog() => InitializeComponent();

    public ConflictDialog(string itemName, bool canMerge) : this()
    {
        MessageText.Text = $"\"{itemName}\" already exists at the destination. What would you like to do?";
        MergeButton.IsVisible = canMerge;
    }

    private void OnChoiceClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<NameCollisionPolicy>(tag, out var policy))
            Result = new ConflictDialogResult(policy, ApplyToAllCheckBox.IsChecked == true);

        Close();
    }
}