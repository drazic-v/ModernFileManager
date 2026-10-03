using FileManager.App.Tests.Fakes;
using FileManager.App.ViewModels;
using FileManager.Core.Models;
using FileManager.Core.Providers;
using Xunit;
using static FileManager.App.Tests.Fakes.TestItems;

namespace FileManager.App.Tests.Paste;

public class PasteConflictTrackerTests
{
    [Fact]
    public async Task ForItem_NoRememberedPolicy_AsksTheResolutionService()
    {
        var fake = new FakeConflictResolutionService((NameCollisionPolicy.Replace, false));
        var resolver = new PasteConflictTracker(fake).ForItem(File(Path("/root/a.txt")));

        var result = await resolver(Path("/dest/a.txt"), StorageItemKind.File, CancellationToken.None);

        Assert.Equal(NameCollisionPolicy.Replace, result);
        Assert.Single(fake.Requests);
        Assert.Equal("a.txt", fake.Requests[0].ItemName);
    }

    [Fact]
    public async Task ForItem_BothDirectories_ForwardsCanMergeTrue()
    {
        var fake = new FakeConflictResolutionService((NameCollisionPolicy.Merge, false));
        var resolver = new PasteConflictTracker(fake).ForItem(Folder(Path("/root/docs")));

        await resolver(Path("/dest/docs"), StorageItemKind.Directory, CancellationToken.None);

        Assert.True(fake.Requests[0].CanMerge);
    }

    [Fact]
    public async Task ForItem_ConflictingItemIsAFile_ForwardsCanMergeFalse_EvenIfPastedItemIsADirectory()
    {
        var fake = new FakeConflictResolutionService((NameCollisionPolicy.Replace, false));
        var resolver = new PasteConflictTracker(fake).ForItem(Folder(Path("/root/docs")));

        // A file named "docs" already exists at the destination, even though we're pasting a folder.
        await resolver(Path("/dest/docs"), StorageItemKind.File, CancellationToken.None);

        Assert.False(fake.Requests[0].CanMerge);
    }

    [Fact]
    public async Task ForItem_DestinationIsTheItemsOwnPath_ForwardsIsSelfReferentialTrue()
    {
        var fake = new FakeConflictResolutionService((NameCollisionPolicy.Skip, false));
        var item = Folder(Path("/root/docs"));
        var resolver = new PasteConflictTracker(fake).ForItem(item);

        await resolver(item.Path, StorageItemKind.Directory, CancellationToken.None);

        Assert.True(fake.Requests[0].IsSelfReferential);
    }

    [Fact]
    public async Task ForItem_DestinationIsADifferentPath_ForwardsIsSelfReferentialFalse()
    {
        var fake = new FakeConflictResolutionService((NameCollisionPolicy.Skip, false));
        var resolver = new PasteConflictTracker(fake).ForItem(Folder(Path("/root/docs")));

        await resolver(Path("/dest/docs"), StorageItemKind.Directory, CancellationToken.None);

        Assert.False(fake.Requests[0].IsSelfReferential);
    }

    [Fact]
    public async Task ApplyToAllTrue_IsSharedAcrossResolversForDifferentItems_WithoutAskingAgain()
    {
        // Only one scripted response - if the tracker asked a second time, the fake would throw.
        var fake = new FakeConflictResolutionService((NameCollisionPolicy.Replace, true));
        var tracker = new PasteConflictTracker(fake);

        var first = await tracker.ForItem(File(Path("/root/a.txt")))(Path("/dest/a.txt"), StorageItemKind.File, CancellationToken.None);
        var second = await tracker.ForItem(File(Path("/root/b.txt")))(Path("/dest/b.txt"), StorageItemKind.File, CancellationToken.None);

        Assert.Equal(NameCollisionPolicy.Replace, first);
        Assert.Equal(NameCollisionPolicy.Replace, second);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task ApplyToAllFalse_AsksAgainForTheNextConflict()
    {
        var fake = new FakeConflictResolutionService(
            (NameCollisionPolicy.Replace, false),
            (NameCollisionPolicy.Skip, false));
        var tracker = new PasteConflictTracker(fake);

        await tracker.ForItem(File(Path("/root/a.txt")))(Path("/dest/a.txt"), StorageItemKind.File, CancellationToken.None);
        var second = await tracker.ForItem(File(Path("/root/b.txt")))(Path("/dest/b.txt"), StorageItemKind.File, CancellationToken.None);

        Assert.Equal(NameCollisionPolicy.Skip, second);
        Assert.Equal(2, fake.Requests.Count);
    }

    [Fact]
    public async Task RememberedMergePolicy_IsNotReusedForALaterFileConflict()
    {
        // Merge chosen "apply to all" for a folder conflict must not get silently reapplied
        // to a file conflict later in the same mixed paste.
        var fake = new FakeConflictResolutionService(
            (NameCollisionPolicy.Merge, true),
            (NameCollisionPolicy.Replace, false));
        var tracker = new PasteConflictTracker(fake);

        var first = await tracker.ForItem(Folder(Path("/root/docs")))(Path("/dest/docs"), StorageItemKind.Directory, CancellationToken.None);
        var second = await tracker.ForItem(File(Path("/root/notes.txt")))(Path("/dest/notes.txt"), StorageItemKind.File, CancellationToken.None);

        Assert.Equal(NameCollisionPolicy.Merge, first);
        Assert.Equal(NameCollisionPolicy.Replace, second);
        Assert.Equal(2, fake.Requests.Count);
    }

    [Fact]
    public async Task RememberedNonMergePolicy_IsReusedRegardlessOfConflictingKind()
    {
        var fake = new FakeConflictResolutionService((NameCollisionPolicy.Skip, true));
        var tracker = new PasteConflictTracker(fake);

        await tracker.ForItem(File(Path("/root/a.txt")))(Path("/dest/a.txt"), StorageItemKind.File, CancellationToken.None);
        var second = await tracker.ForItem(Folder(Path("/root/docs")))(Path("/dest/docs"), StorageItemKind.Directory, CancellationToken.None);

        Assert.Equal(NameCollisionPolicy.Skip, second);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task DialogDismissed_InvokesOnAbort_AndRethrows()
    {
        var fake = new FakeConflictResolutionService();
        fake.DismissNext();
        var aborted = false;
        var resolver = new PasteConflictTracker(fake, onAbort: () => aborted = true).ForItem(File(Path("/root/a.txt")));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            resolver(Path("/dest/a.txt"), StorageItemKind.File, CancellationToken.None));

        Assert.True(aborted);
    }

    [Fact]
    public async Task JobTokenCancelled_DoesNotInvokeOnAbort()
    {
        // The job itself was cancelled (Cancel button / shutdown) - that's not the user
        // abandoning the paste via the dialog, so onAbort must stay out of it.
        var fake = new FakeConflictResolutionService();
        fake.DismissNext();
        var aborted = false;
        var resolver = new PasteConflictTracker(fake, onAbort: () => aborted = true).ForItem(File(Path("/root/a.txt")));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            resolver(Path("/dest/a.txt"), StorageItemKind.File, cts.Token));

        Assert.False(aborted);
    }

    [Theory]
    [InlineData(NameCollisionPolicy.Merge, StorageItemKind.Directory, true)]
    [InlineData(NameCollisionPolicy.Merge, StorageItemKind.File, false)]
    [InlineData(NameCollisionPolicy.Replace, StorageItemKind.File, true)]
    [InlineData(NameCollisionPolicy.Replace, StorageItemKind.Directory, true)]
    [InlineData(NameCollisionPolicy.Skip, StorageItemKind.File, true)]
    [InlineData(NameCollisionPolicy.GenerateUnique, StorageItemKind.File, true)]
    [InlineData(NameCollisionPolicy.Fail, StorageItemKind.File, true)]
    public void IsApplicable_OnlyMergeIsRestrictedToDirectoryConflicts(NameCollisionPolicy policy, StorageItemKind conflictingKind, bool expected)
    {
        Assert.Equal(expected, PasteConflictTracker.IsApplicable(policy, conflictingKind));
    }
}