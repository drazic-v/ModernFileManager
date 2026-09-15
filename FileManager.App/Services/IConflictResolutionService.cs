using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Providers;

namespace FileManager.App.Services;

public interface IConflictResolutionService
{
    Task<(NameCollisionPolicy Policy, bool ApplyToAll)> ResolveAsync(string itemName, bool canMerge, bool isSelfReferential, CancellationToken ct);
}