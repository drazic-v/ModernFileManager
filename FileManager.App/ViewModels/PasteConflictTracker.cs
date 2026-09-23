using FileManager.App.Services;
using FileManager.Core.Models;
using FileManager.Core.Providers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FileManager.App.ViewModels;

/// <summary>
/// Tracks "apply to all" conflict-resolution state across a multi-item paste. Wraps
/// IConflictResolutionService (which shows a real dialog) so a remembered choice can be
/// reused for later items without re-prompting, and records which policy actually applied
/// to each item so PasteAsync can tell a Skip apart from a genuine success afterward.
/// </summary>
public sealed class PasteConflictTracker
{
    private readonly IConflictResolutionService _conflictResolution;
    private readonly Dictionary<StorageItem, NameCollisionPolicy> _itemResolutions = new();
    private NameCollisionPolicy? _remembered;

    public PasteConflictTracker(IConflictResolutionService conflictResolution)
    {
        _conflictResolution = conflictResolution;
    }

    /// <summary>Set by the paste loop before each item's copy/move call.</summary>
    public StorageItem? CurrentItem { get; set; }

    public IReadOnlyDictionary<StorageItem, NameCollisionPolicy> ItemResolutions => _itemResolutions;

    /// <summary>
    /// Merge only makes sense directory-to-directory - a remembered Merge policy must not
    /// get silently reapplied to a file conflict later in the same mixed-selection paste.
    /// </summary>
    public static bool IsApplicable(NameCollisionPolicy policy, StorageItemKind conflictingKind) =>
        policy != NameCollisionPolicy.Merge || conflictingKind == StorageItemKind.Directory;

    // Matches the ConflictResolver delegate shape directly: ConflictResolver resolver = tracker.ResolveAsync;
    public async Task<NameCollisionPolicy> ResolveAsync(StoragePath destinationPath, StorageItemKind conflictingKind, CancellationToken ct)
    {
        if (_remembered is { } remembered && IsApplicable(remembered, conflictingKind))
        {
            Record(destinationPath, remembered);
            return remembered;
        }

        var isSelfReferential = CurrentItem is not null && StoragePath.PathsEqual(destinationPath, CurrentItem.Path);
        var canMerge = CurrentItem?.Kind == StorageItemKind.Directory && conflictingKind == StorageItemKind.Directory;
        var (policy, applyToAll) = await _conflictResolution.ResolveAsync(destinationPath.Name, canMerge, isSelfReferential, ct);

        Record(destinationPath, policy);
        if (applyToAll) _remembered = policy;
        return policy;
    }

    private void Record(StoragePath destinationPath, NameCollisionPolicy policy)
    {
        if (CurrentItem is not null && destinationPath.Name == CurrentItem.Name)
            _itemResolutions[CurrentItem] = policy;
    }
}