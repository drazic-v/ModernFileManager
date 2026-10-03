using FileManager.App.Services;
using FileManager.Core.Models;
using FileManager.Core.Providers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FileManager.App.ViewModels;

/// <summary>
/// Shared "apply to all" state for one multi-item paste. Hands out one ConflictResolver per
/// item (ForItem), because queued jobs run later - a mutable "current item" would be wrong
/// by the time a job actually hits a conflict.
/// </summary>
public sealed class PasteConflictTracker
{
    private readonly IConflictResolutionService _conflictResolution;
    private readonly Action? _onAbort;
    private NameCollisionPolicy? _remembered;   // touched only by the worker thread (single consumer)

    /// <param name="onAbort">Called when the user dismisses the dialog without choosing,
    /// so the caller can abandon the whole paste.</param>
    public PasteConflictTracker(IConflictResolutionService conflictResolution, Action? onAbort = null)
    {
        _conflictResolution = conflictResolution;
        _onAbort = onAbort;
    }

    /// <summary>
    /// Merge only makes sense directory-to-directory - a remembered Merge policy must not
    /// get silently reapplied to a file conflict later in the same mixed-selection paste.
    /// </summary>
    public static bool IsApplicable(NameCollisionPolicy policy, StorageItemKind conflictingKind) =>
        policy != NameCollisionPolicy.Merge || conflictingKind == StorageItemKind.Directory;

    /// <summary>A resolver bound to one item; "apply to all" state is shared across all of them.</summary>
    public ConflictResolver ForItem(StorageItem item) =>
        (destinationPath, conflictingKind, ct) => ResolveAsync(item, destinationPath, conflictingKind, ct);

    private async Task<NameCollisionPolicy> ResolveAsync(
        StorageItem item, StoragePath destinationPath, StorageItemKind conflictingKind, CancellationToken ct)
    {
        if (_remembered is { } remembered && IsApplicable(remembered, conflictingKind))
            return remembered;

        var isSelfReferential = StoragePath.PathsEqual(destinationPath, item.Path);
        var canMerge = item.Kind == StorageItemKind.Directory && conflictingKind == StorageItemKind.Directory;

        try
        {
            var (policy, applyToAll) = await _conflictResolution.ResolveAsync(destinationPath.Name, canMerge, isSelfReferential, ct);
            if (applyToAll) _remembered = policy;
            return policy;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The job's token wasn't cancelled, so the user closed the dialog without choosing.
            _onAbort?.Invoke();
            throw;
        }
    }
}