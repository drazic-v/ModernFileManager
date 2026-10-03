using FileManager.App.Services;
using FileManager.Core.Providers;

namespace FileManager.App.Tests.Fakes;

/// <summary>
/// Scripts ConflictResolutionService's responses instead of showing a real dialog.
/// Responses are consumed in order; if the tracker asks more times than the test scripted
/// for, this throws immediately - which is exactly what should happen when "apply to all"
/// silently fails to remember a policy it should have.
/// </summary>
public sealed class FakeConflictResolutionService : IConflictResolutionService
{
    private readonly Queue<(NameCollisionPolicy Policy, bool ApplyToAll)> _responses;

    public List<(string ItemName, bool CanMerge, bool IsSelfReferential)> Requests { get; } = new();

    public FakeConflictResolutionService(params (NameCollisionPolicy Policy, bool ApplyToAll)[] responses)
    {
        _responses = new Queue<(NameCollisionPolicy, bool)>(responses);
    }

    private int _dismissalsRemaining;

    /// <summary>The next N requests behave like the user closing the dialog without choosing.</summary>
    public void DismissNext(int count = 1) => _dismissalsRemaining = count;

    public Task<(NameCollisionPolicy Policy, bool ApplyToAll)> ResolveAsync(string itemName, bool canMerge, bool isSelfReferential, CancellationToken ct)
    {
        Requests.Add((itemName, canMerge, isSelfReferential));

        if (_dismissalsRemaining > 0)
        {
            _dismissalsRemaining--;
            return Task.FromException<(NameCollisionPolicy, bool)>(
                new OperationCanceledException("Conflict dialog closed without a choice."));
        }

        if (_responses.Count == 0)
            throw new InvalidOperationException("Ran out of scripted responses - the tracker asked more times than expected.");

        return Task.FromResult(_responses.Dequeue());
    }
}