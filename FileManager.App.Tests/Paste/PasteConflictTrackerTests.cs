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
    public async Task ResolveAsync_NoRememberedPolicy_AsksTheResolutionService()
    {
        var fake = new FakeConflictResolutionService((NameCollisionPolicy.Replace, false));
        var tracker = new PasteConflictTracker(fake) { CurrentItem = File(Path("/root/a.txt")) };

        var result = await tracker.ResolveAsync(Path("/dest/a.txt"), StorageItemKind.File, CancellationToken.None);

        Assert.Equal(NameCollisionPolicy.Replace, result);
        Assert.Single(fake.Requests);
        Assert.Equal("a.txt", fake.Requests[0].ItemName);
    }

    [Fact]
    public async Task ResolveAsync_BothDirectories_ForwardsCanMergeTrue()
    {
        var fake = new FakeConflictResolutionService((NameCollisionPolicy.Merge, false));
        var tracker = new PasteConflictTracker(fake) { CurrentItem = Folder(Path("/root/docs")) };

        await tracker.ResolveAsync(Path("/dest/docs"), StorageItemKind.Directory, CancellationToken.None);

        Assert.True(fake.Requests[0].CanMerge);
    }

    [Fact]
    public async Task ResolveAsync_ConflictingItemIsAFile_ForwardsCanMergeFalse_EvenIfCurrentItemIsADirectory()
    {
        var fake = new FakeConflictResolutionService((NameCollisionPolicy.Replace, false));
        var tracker = new PasteConflictTracker(fake) { CurrentItem = Folder(Path("/root/docs")) };

        // A file named "docs" already exists at the destination, even though we're pasting a folder.
        await tracker.ResolveAsync(Path("/dest/docs"), StorageItemKind.File, CancellationToken.None);

        Assert.False(fake.Requests[0].CanMerge);
    }

    [Fact]
    public async Task ResolveAsync_DestinationIsCurrentItemsOwnPath_ForwardsIsSelfReferentialTrue()
    {
        var fake = new FakeConflictResolutionService((NameCollisionPolicy.Skip, false));
        var item = Folder(Path("/root/docs"));
        var tracker = new PasteConflictTracker(fake) { CurrentItem = item };

        await tracker.ResolveAsync(item.Path, StorageItemKind.Directory, CancellationToken.None);

        Assert.True(fake.Requests[0].IsSelfReferential);
    }

    [Fact]
    public async Task ResolveAsync_DestinationIsADifferentPath_ForwardsIsSelfReferentialFalse()
    {
        var fake = new FakeConflictResolutionService((NameCollisionPolicy.Skip, false));
        var tracker = new PasteConflictTracker(fake) { CurrentItem = Folder(Path("/root/docs")) };

        await tracker.ResolveAsync(Path("/dest/docs"), StorageItemKind.Directory, CancellationToken.None);

        Assert.False(fake.Requests[0].IsSelfReferential);
    }

    [Fact]
    public async Task ResolveAsync_ApplyToAllTrue_ReusesThePolicyForTheNextApplicableConflict_WithoutAskingAgain()
    {
        // Only one scripted response - if the tracker asked a second time, the fake would throw.
        var fake = new FakeConflictResolutionService((NameCollisionPolicy.Replace, true));
        var tracker = new PasteConflictTracker(fake);

        tracker.CurrentItem = File(Path("/root/a.txt"));
        var first = await tracker.ResolveAsync(Path("/dest/a.txt"), StorageItemKind.File, CancellationToken.None);

        tracker.CurrentItem = File(Path("/root/b.txt"));
        var second = await tracker.ResolveAsync(Path("/dest/b.txt"), StorageItemKind.File, CancellationToken.None);

        Assert.Equal(NameCollisionPolicy.Replace, first);
        Assert.Equal(NameCollisionPolicy.Replace, second);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task ResolveAsync_ApplyToAllFalse_AsksAgainForTheNextConflict()
    {
        var fake = new FakeConflictResolutionService(
            (NameCollisionPolicy.Replace, false),
            (NameCollisionPolicy.Skip, false));
        var tracker = new PasteConflictTracker(fake);

        tracker.CurrentItem = File(Path("/root/a.txt"));
        await tracker.ResolveAsync(Path("/dest/a.txt"), StorageItemKind.File, CancellationToken.None);

        tracker.CurrentItem = File(Path("/root/b.txt"));
        var second = await tracker.ResolveAsync(Path("/dest/b.txt"), StorageItemKind.File, CancellationToken.None);

        Assert.Equal(NameCollisionPolicy.Skip, second);
        Assert.Equal(2, fake.Requests.Count);
    }

    [Fact]
    public async Task ResolveAsync_RememberedMergePolicy_IsNotReusedForALaterFileConflict()
    {
        // The exact bug the IsApplicable guard exists for: Merge chosen "apply to all" for a
        // folder conflict must not get silently reapplied to a file conflict later in the
        // same mixed paste - Merge only makes sense directory-to-directory.
        var fake = new FakeConflictResolutionService(
            (NameCollisionPolicy.Merge, true),      // folder vs folder - remembered
            (NameCollisionPolicy.Replace, false));  // file vs file - must ask again, not reuse Merge

        var tracker = new PasteConflictTracker(fake);

        tracker.CurrentItem = Folder(Path("/root/docs"));
        var first = await tracker.ResolveAsync(Path("/dest/docs"), StorageItemKind.Directory, CancellationToken.None);

        tracker.CurrentItem = File(Path("/root/notes.txt"));
        var second = await tracker.ResolveAsync(Path("/dest/notes.txt"), StorageItemKind.File, CancellationToken.None);

        Assert.Equal(NameCollisionPolicy.Merge, first);
        Assert.Equal(NameCollisionPolicy.Replace, second);
        Assert.Equal(2, fake.Requests.Count); // proves it asked again instead of silently reusing Merge
    }

    [Fact]
    public async Task ResolveAsync_RememberedNonMergePolicy_IsReusedRegardlessOfConflictingKind()
    {
        // Replace/Skip/GenerateUnique/Fail don't have Merge's directory-only restriction.
        var fake = new FakeConflictResolutionService((NameCollisionPolicy.Skip, true));
        var tracker = new PasteConflictTracker(fake);

        tracker.CurrentItem = File(Path("/root/a.txt"));
        await tracker.ResolveAsync(Path("/dest/a.txt"), StorageItemKind.File, CancellationToken.None);

        tracker.CurrentItem = Folder(Path("/root/docs"));
        var second = await tracker.ResolveAsync(Path("/dest/docs"), StorageItemKind.Directory, CancellationToken.None);

        Assert.Equal(NameCollisionPolicy.Skip, second);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task ItemResolutions_RecordsThePolicyForCurrentItem_WhenDestinationNameMatchesIt()
    {
        var fake = new FakeConflictResolutionService((NameCollisionPolicy.Skip, false));
        var item = File(Path("/root/a.txt"));
        var tracker = new PasteConflictTracker(fake) { CurrentItem = item };

        await tracker.ResolveAsync(Path("/dest/a.txt"), StorageItemKind.File, CancellationToken.None);

        Assert.True(tracker.ItemResolutions.TryGetValue(item, out var resolution));
        Assert.Equal(NameCollisionPolicy.Skip, resolution);
    }

    [Fact]
    public async Task ItemResolutions_RecordsForARememberedPolicyToo_NotJustAFreshAsk()
    {
        var fake = new FakeConflictResolutionService((NameCollisionPolicy.Replace, true));
        var tracker = new PasteConflictTracker(fake);

        var first = File(Path("/root/a.txt"));
        tracker.CurrentItem = first;
        await tracker.ResolveAsync(Path("/dest/a.txt"), StorageItemKind.File, CancellationToken.None);

        var second = File(Path("/root/b.txt"));
        tracker.CurrentItem = second;
        await tracker.ResolveAsync(Path("/dest/b.txt"), StorageItemKind.File, CancellationToken.None); // reused, not asked

        Assert.Equal(NameCollisionPolicy.Replace, tracker.ItemResolutions[first]);
        Assert.Equal(NameCollisionPolicy.Replace, tracker.ItemResolutions[second]);
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