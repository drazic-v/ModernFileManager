using Avalonia.Controls;
using Avalonia.Threading;
using FileManager.App.Views;
using FileManager.Core.Providers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FileManager.App.Services;

public class ConflictResolutionService : IConflictResolutionService
{
    private readonly Window _owner;
    public ConflictResolutionService(Window owner) => _owner = owner;

    public Task<(NameCollisionPolicy Policy, bool ApplyToAll)> ResolveAsync(string itemName, bool canMerge, bool isSelfReferential, CancellationToken ct) =>
    Dispatcher.UIThread.InvokeAsync(async () =>
    {
        ct.ThrowIfCancellationRequested();
        var dialog = new ConflictDialog(itemName, canMerge, isSelfReferential);
        using var registration = ct.Register(() => Dispatcher.UIThread.Post(dialog.Close));

        await dialog.ShowDialog(_owner);

        ct.ThrowIfCancellationRequested();
        if (dialog.WasCancelled)
            throw new OperationCanceledException("Conflict dialog closed without a choice.");

        return (dialog.Result.Policy, dialog.Result.ApplyToAll);
    });
}