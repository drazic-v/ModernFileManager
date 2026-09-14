using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using FileManager.App.Views;
using FileManager.Core.Providers;

namespace FileManager.App.Services;

public class ConflictResolutionService : IConflictResolutionService
{
    private readonly Window _owner;
    public ConflictResolutionService(Window owner) => _owner = owner;

    public Task<(NameCollisionPolicy Policy, bool ApplyToAll)> ResolveAsync(string itemName, bool canMerge, CancellationToken ct) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new ConflictDialog(itemName, canMerge);
            await dialog.ShowDialog(_owner);
            return (dialog.Result.Policy, dialog.Result.ApplyToAll);
        });
}