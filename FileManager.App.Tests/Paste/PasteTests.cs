using Avalonia.Headless.XUnit;
using FileManager.App.Tests.Fakes;
using FileManager.App.ViewModels;
using FileManager.Core.Models;
using FileManager.Core.Providers;
using FileManager.Core.Transfers;
using FileManager.TestKit;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using static FileManager.App.Tests.Fakes.TestItems;

namespace FileManager.App.Tests.Paste
{
    public class PasteTests
    {
        private sealed record Harness(
    WorkspaceViewModel Workspace,
    FakeNotificationService Notifications,
    FakeConflictResolutionService Conflicts);

        /// Opens a workspace on "/root" and navigates its tab to <paramref name="openAt"/>.
        private static async Task<Harness> OpenWorkspaceAsync(
            FakeStorageProvider provider,
            TransferManager manager,
            StorageItem openAt,
            FakeConflictResolutionService? conflicts = null)
        {
            var notifs = new FakeNotificationService();
            var conflictService = conflicts ?? new FakeConflictResolutionService();
            var workspace = new WorkspaceViewModel(provider, Path("/root"), "root", notifs, conflictService, manager);
            await workspace.AddTabCommand.Execute();
            await workspace.SelectedTab!.RefreshAsync();
            await workspace.SelectedTab!.NavigateIntoAsync(openAt);

            return new Harness(workspace, notifs, conflictService);
        }

        private sealed record Tree(FakeStorageProvider Provider, StorageItem Src, StorageItem Dest, StorageItem A, StorageItem B);

        /// /root
        ///   src/  a.txt (10 B), b.txt (20 B)
        ///   dest/ (empty)
        private static Tree StandardTree(string providerId="fake")
        {
            var provider = new FakeStorageProvider(providerId);
            var root = Path("/root");
            var src = Folder(root.Combine("src"));
            var dest = Folder(root.Combine("dest"));
            var a = File(src.Path.Combine("a.txt"), 10);
            var b = File(src.Path.Combine("b.txt"), 20);

            provider.AddChildren(root.Value, src, dest);
            provider.AddChildren(src.Path.Value, a, b);
            provider.AddChildren(dest.Path.Value);

            return new Tree(provider, src, dest, a, b);
        }


        [AvaloniaFact]
        public async Task Paste_Cut_MovesItemsAndClearsClipboard()
        {
            var ct = TestContext.Current.CancellationToken;
            var t = StandardTree();
            await using var manager = new TransferManager();
            var h = await OpenWorkspaceAsync(t.Provider, manager, t.Dest);
            h.Workspace.SetClipboard([t.A, t.B], t.Provider, isCut: true);
            Assert.True(await t.Provider.ExistsAsync(t.A.Path, ct));      // precondition for the "gone" checks

            await h.Workspace.PasteCommand.Execute();

            Assert.True(await t.Provider.ExistsAsync(t.Dest.Path.Combine("a.txt"), ct));
            Assert.True(await t.Provider.ExistsAsync(t.Dest.Path.Combine("b.txt"), ct));
            Assert.False(await t.Provider.ExistsAsync(t.A.Path, ct));
            Assert.False(await t.Provider.ExistsAsync(t.B.Path, ct));
            Assert.Null(h.Workspace.Clipboard);
            Assert.Empty(h.Notifications.Errors);
        }

        [AvaloniaFact]
        public async Task Paste_Copy_KeepsClipboard()
        {
            var ct = TestContext.Current.CancellationToken;
            var t = StandardTree();
            await using var manager = new TransferManager();
            var h = await OpenWorkspaceAsync(t.Provider, manager, t.Dest);
            h.Workspace.SetClipboard([t.A, t.B], t.Provider, isCut: false);
            var before = h.Workspace.Clipboard;

            Assert.True(await t.Provider.ExistsAsync(t.A.Path, ct));

            await h.Workspace.PasteCommand.Execute();
            Assert.True(await t.Provider.ExistsAsync(t.Dest.Path.Combine("a.txt"), ct));
            Assert.True(await t.Provider.ExistsAsync(t.Dest.Path.Combine("b.txt"), ct));
            Assert.True(await t.Provider.ExistsAsync(t.A.Path, ct));
            Assert.True(await t.Provider.ExistsAsync(t.B.Path, ct));
            Assert.Same(before, h.Workspace.Clipboard);
            Assert.Empty(h.Notifications.Errors);
        }

        [AvaloniaFact]
        public async Task Paste_AllSucceed_ShowsSuccessSummary()
        {
            var t = StandardTree();
            await using var manager = new TransferManager();
            var h = await OpenWorkspaceAsync(t.Provider, manager, t.Dest);
            h.Workspace.SetClipboard([t.A, t.B], t.Provider, isCut: false);
            await h.Workspace.PasteCommand.Execute();

            Assert.Empty(h.Notifications.Errors);
            Assert.Equal("Copied 2 items.", Assert.Single(h.Notifications.Successes));
        }


        [AvaloniaFact]
        public async Task Paste_OneItemFails_ShowsErrorWithFirstFailure()
        {
            var ct = TestContext.Current.CancellationToken;
            var t = StandardTree();
            await using var manager = new TransferManager();
            var h = await OpenWorkspaceAsync(t.Provider, manager, t.Dest);
            h.Workspace.SetClipboard([t.A, t.B], t.Provider, isCut: false);
            t.Provider.FailNextCopyAttempts(1);
            await h.Workspace.PasteCommand.Execute();

            Assert.Empty(h.Notifications.Successes);
            Assert.True(await t.Provider.ExistsAsync(t.Dest.Path.Combine("b.txt"), ct));
            Assert.False(await t.Provider.ExistsAsync(t.Dest.Path.Combine("a.txt"), ct));
            Assert.Equal("Copied \"b.txt\", 1 failed. First error: a.txt: Simulated copy failure.", Assert.Single(h.Notifications.Errors));
        }

        [AvaloniaFact]
        public async Task Paste_ConflictResolvedAsSkip_ShowsSkippedInSummary()
        {
            var ct = TestContext.Current.CancellationToken;
            var t = StandardTree();
            var existing = File(t.Dest.Path.Combine("a.txt"), 5);
            t.Provider.AddChildren(t.Dest.Path.Value, existing);        // dest now contains a.txt (replaces the empty list)

            var conflicts = new FakeConflictResolutionService((NameCollisionPolicy.Skip, false));
            await using var manager = new TransferManager();
            var h = await OpenWorkspaceAsync(t.Provider, manager, t.Dest, conflicts);
            h.Workspace.SetClipboard([t.A, t.B], t.Provider, isCut: false);
            await h.Workspace.PasteCommand.Execute();

            Assert.Empty(h.Notifications.Errors);
            Assert.Equal("Copied \"b.txt\", skipped \"a.txt\" (already exists).", Assert.Single(h.Notifications.Successes));
            Assert.True(await t.Provider.ExistsAsync(t.Dest.Path.Combine("b.txt"), ct));
            Assert.Equal(5, (await t.Provider.GetInfoAsync(t.Dest.Path.Combine("a.txt"))).SizeInBytes);
            Assert.Equal("a.txt", Assert.Single(conflicts.Requests).ItemName);
        }

        [AvaloniaFact]
        public async Task Paste_FolderIntoItself_ShowsErrorAndSkipsIt()
        {
            var ct = TestContext.Current.CancellationToken;
            var t = StandardTree();
            await using var manager = new TransferManager();
            var h = await OpenWorkspaceAsync(t.Provider, manager, t.Src);
            h.Workspace.SetClipboard([t.Src], t.Provider, isCut: false);
            await h.Workspace.PasteCommand.Execute();

            Assert.Empty(h.Notifications.Successes);
            Assert.True(await t.Provider.ExistsAsync(t.A.Path, ct));
            Assert.True(await t.Provider.ExistsAsync(t.A.Path, ct));

            Assert.Equal("Can't paste \"src\" into itself.", Assert.Single(h.Notifications.Errors));
        }

        [AvaloniaFact]
        public async Task Paste_FolderIntoDescendant_ShowsErrorAndSkipsIt()
        {
            var ct = TestContext.Current.CancellationToken;
            var t = StandardTree();
            var sub = Folder(t.Src.Path.Combine("sub"));
            t.Provider.AddChildren(t.Src.Path.Value, t.A, t.B, sub);
            await using var manager = new TransferManager();
            var h = await OpenWorkspaceAsync(t.Provider, manager, sub);
            h.Workspace.SetClipboard([t.Src], t.Provider, isCut: false);
            await h.Workspace.PasteCommand.Execute();

            Assert.Empty(h.Notifications.Successes);
            Assert.Equal(0, (await FolderInfoCalculator.GetFolderInfo(t.Provider, sub.Path)).Size);
            Assert.Equal("Can't paste \"src\" into itself.", Assert.Single(h.Notifications.Errors));
        }

        [AvaloniaFact]
        public async Task Paste_CutIntoOriginalFolder_ShowsErrorAndSkipsIt()
        {
            var t = StandardTree();
            await using var manager = new TransferManager();
            var h = await OpenWorkspaceAsync(t.Provider, manager, t.Src);
            h.Workspace.SetClipboard([t.A], t.Provider, isCut: true);
            await h.Workspace.PasteCommand.Execute();

            Assert.Empty(h.Notifications.Successes);
            Assert.True(await t.Provider.ExistsAsync(t.A.Path, TestContext.Current.CancellationToken));
            Assert.Equal("Can't move \"a.txt\" to its original location.", Assert.Single(h.Notifications.Errors));
        }

        [AvaloniaFact]
        public async Task Paste_CutWithEveryItemRejected_ClearsClipboardAndChangesNothing()
        {
            var ct = TestContext.Current.CancellationToken;
            var t = StandardTree();
            await using var manager = new TransferManager();
            var h = await OpenWorkspaceAsync(t.Provider, manager, t.Src);
            h.Workspace.SetClipboard([t.A, t.Src], t.Provider, isCut: true);
            await h.Workspace.PasteCommand.Execute();

            Assert.Empty(h.Notifications.Successes);
            Assert.Null(h.Workspace.Clipboard);
            Assert.Equal(2, h.Notifications.Errors.Count);
            Assert.Equal("Can't move \"a.txt\" to its original location.", h.Notifications.Errors[0]);
            Assert.Equal("Can't paste \"src\" into itself.", h.Notifications.Errors[1]);
            Assert.True(await t.Provider.ExistsAsync(t.A.Path, ct));
            Assert.True(await t.Provider.ExistsAsync(t.Src.Path, ct));
        }

        [AvaloniaTheory]
        [InlineData(NameCollisionPolicy.Skip)]
        [InlineData(NameCollisionPolicy.Replace)]
        [InlineData(NameCollisionPolicy.GenerateUnique)]
        public async Task Paste_FileConflict_AppliesChosenPolicy(NameCollisionPolicy policy)
        {
            var ct = TestContext.Current.CancellationToken;
            var t = StandardTree();
            await using var manager = new TransferManager();
            var conflicts = new FakeConflictResolutionService((policy, false));
            t.Provider.AddChildren(t.Dest.Path.Value, File(t.Dest.Path.Combine("a.txt"), 5));
            var h = await OpenWorkspaceAsync(t.Provider, manager, t.Dest, conflicts);
            h.Workspace.SetClipboard([t.A, t.B], t.Provider, isCut: true);
            await h.Workspace.PasteCommand.Execute();

            switch (policy)
            {
                case NameCollisionPolicy.Skip:
                    Assert.True(await t.Provider.ExistsAsync(t.Dest.Path.Combine("a.txt"), ct));
                    Assert.Equal(5, (await t.Provider.GetInfoAsync(t.Dest.Path.Combine("a.txt"))).SizeInBytes);
                    Assert.True(await t.Provider.ExistsAsync(t.Dest.Path.Combine("b.txt"), ct));
                    break;
                case NameCollisionPolicy.Replace:
                    Assert.True(await t.Provider.ExistsAsync(t.Dest.Path.Combine("a.txt"), ct));
                    Assert.Equal(10, (await t.Provider.GetInfoAsync(t.Dest.Path.Combine("a.txt"))).SizeInBytes);
                    Assert.True(await t.Provider.ExistsAsync(t.Dest.Path.Combine("b.txt"), ct));
                    break;
                case NameCollisionPolicy.GenerateUnique:
                    Assert.True(await t.Provider.ExistsAsync(t.Dest.Path.Combine("a.txt"), ct));
                    Assert.Equal(5, (await t.Provider.GetInfoAsync(t.Dest.Path.Combine("a.txt"))).SizeInBytes);
                    Assert.Equal(10, (await t.Provider.GetInfoAsync(t.Dest.Path.Combine("a (2).txt"))).SizeInBytes);
                    Assert.True(await t.Provider.ExistsAsync(t.Dest.Path.Combine("b.txt"), ct));
                    break;
                default:
                    throw new NotImplementedException($"Test not implemented for policy {policy}.");
            }
        }

        [AvaloniaFact]
        public async Task Paste_FolderConflictMerge_MergesContents()
        {
            var ct = TestContext.Current.CancellationToken;
            var t = StandardTree();
            await using var manager = new TransferManager();
            var conflicts = new FakeConflictResolutionService((NameCollisionPolicy.Merge, false));
            var existingFolder = Folder(t.Dest.Path.Combine("src"));
            var existingFile = File(existingFolder.Path.Combine("existing.txt"), 15);
            t.Provider.AddChildren(t.Dest.Path.Value, existingFolder);
            t.Provider.AddChildren(existingFolder.Path.Value, existingFile);
            var h = await OpenWorkspaceAsync(t.Provider, manager, t.Dest, conflicts);
            h.Workspace.SetClipboard([t.Src], t.Provider, isCut: true);
            await h.Workspace.PasteCommand.Execute();
            Assert.True(await t.Provider.ExistsAsync(t.Dest.Path.Combine("src"), ct));
            Assert.True(await t.Provider.ExistsAsync(t.Dest.Path.Combine("src").Combine("a.txt"), ct));
            Assert.True(await t.Provider.ExistsAsync(t.Dest.Path.Combine("src").Combine("b.txt"), ct));
            Assert.True(await t.Provider.ExistsAsync(t.Dest.Path.Combine("src").Combine("existing.txt"), ct));
        }

        [AvaloniaFact]
        public async Task Paste_ApplyToAllChosen_AsksOnlyOnce()
        {
            var ct = TestContext.Current.CancellationToken;
            var t = StandardTree();
            await using var manager = new TransferManager();
            var conflicts = new FakeConflictResolutionService((NameCollisionPolicy.Skip, true));
            t.Provider.AddChildren(t.Dest.Path.Value, File(t.Dest.Path.Combine("a.txt"), 5), File(t.Dest.Path.Combine("b.txt"), 5));
            var h = await OpenWorkspaceAsync(t.Provider, manager, t.Dest, conflicts);
            h.Workspace.SetClipboard([t.A, t.B], t.Provider, isCut: true);
            await h.Workspace.PasteCommand.Execute();
            Assert.Single(conflicts.Requests);
            Assert.Equal("a.txt", conflicts.Requests[0].ItemName);
            Assert.Equal(5, (await t.Provider.GetInfoAsync(t.Dest.Path.Combine("b.txt"))).SizeInBytes);
            Assert.Equal(5, (await t.Provider.GetInfoAsync(t.Dest.Path.Combine("a.txt"))).SizeInBytes);
        }


        [AvaloniaFact]
        public async Task Paste_ConflictDialogDismissed_CancelsRemainingItemsSilently()
        {
            var ct = TestContext.Current.CancellationToken;
            var t = StandardTree();
            await using var manager = new TransferManager();
            var conflicts = new FakeConflictResolutionService((NameCollisionPolicy.Replace, true));
            conflicts.DismissNext();
            t.Provider.AddChildren(t.Dest.Path.Value, File(t.Dest.Path.Combine("a.txt"), 5), File(t.Dest.Path.Combine("b.txt"), 5));
            var h = await OpenWorkspaceAsync(t.Provider, manager, t.Dest, conflicts);
            h.Workspace.SetClipboard([t.A, t.B], t.Provider, isCut: true);
            await h.Workspace.PasteCommand.Execute();
            Assert.Single(conflicts.Requests);
            Assert.Equal("a.txt", conflicts.Requests[0].ItemName);
            Assert.Equal(5, (await t.Provider.GetInfoAsync(t.Dest.Path.Combine("b.txt"))).SizeInBytes);
            Assert.Equal(5, (await t.Provider.GetInfoAsync(t.Dest.Path.Combine("a.txt"))).SizeInBytes);
        }

        [AvaloniaFact]
        public async Task Paste_CancelledMidTransfer_IsSilentAndKeepsClipboard()
        {
            var ct = TestContext.Current.CancellationToken;
            var t = StandardTree();
            await using var manager = new TransferManager();
            var h = await OpenWorkspaceAsync(t.Provider, manager, t.Dest);
            h.Workspace.SetClipboard([t.A, t.B], t.Provider, isCut: false);
            var clipboard = h.Workspace.Clipboard;
            var reached = t.Provider.HoldNextCopy();                                  
            var paste = h.Workspace.PasteCommand.Execute().ToTask();                   
            await reached.WaitAsync(TimeSpan.FromSeconds(5), ct);                      
            h.Workspace.ActiveTransfers.Single().Cancel();                             
            await paste.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.Empty(h.Notifications.Successes);
            Assert.Empty(h.Notifications.Errors);
            Assert.Equal(clipboard, h.Workspace.Clipboard);
            Assert.Null(h.Notifications.Errors.SingleOrDefault(e => e.Contains("cancelled")));
            Assert.True(await t.Provider.ExistsAsync(t.A.Path, ct));
            Assert.True(await t.Provider.ExistsAsync(t.B.Path, ct));
            Assert.False(await t.Provider.ExistsAsync(t.Dest.Path.Combine("a.txt"), ct));
            Assert.False(await t.Provider.ExistsAsync(t.Dest.Path.Combine("b.txt"), ct));
            Assert.NotNull(h.Workspace.Clipboard);
        }

        [AvaloniaFact]
        public async Task Paste_EmptyFolder_CreatesFolderAtDestination()
        {
            var ct = TestContext.Current.CancellationToken;
            var t = StandardTree();
            var emptyFolder = Folder(t.Src.Path.Combine("empty"));
            t.Provider.AddChildren(t.Src.Path.Value, emptyFolder);
            await using var manager = new TransferManager();
            var h = await OpenWorkspaceAsync(t.Provider, manager, t.Dest);
            h.Workspace.SetClipboard([emptyFolder], t.Provider, isCut: false);
            await h.Workspace.PasteCommand.Execute();
            Assert.True(await t.Provider.ExistsAsync(t.Dest.Path.Combine("empty"), ct));
        }

        [AvaloniaFact]
        public async Task Paste_NestedFolder_RecreatesStructureAndContent()
        {
            var ct = TestContext.Current.CancellationToken;
            var t = StandardTree();
            var nestedFolder = Folder(t.Src.Path.Combine("nested"));
            var nestedFile = File(nestedFolder.Path.Combine("nested.txt"), 30);
            t.Provider.AddChildren(t.Src.Path.Value, nestedFolder);
            t.Provider.AddChildren(nestedFolder.Path.Value, nestedFile);
            await using var manager = new TransferManager();
            var h = await OpenWorkspaceAsync(t.Provider, manager, t.Dest);
            h.Workspace.SetClipboard([nestedFolder], t.Provider, isCut: false);
            await h.Workspace.PasteCommand.Execute();
            Assert.True(await t.Provider.ExistsAsync(t.Dest.Path.Combine("nested"), ct));
            Assert.True(await t.Provider.ExistsAsync(t.Dest.Path.Combine("nested").Combine("nested.txt"), ct));
        }

        [AvaloniaFact]
        public async Task Paste_AcrossProviders_CopiesContent()
        {
            var ct = TestContext.Current.CancellationToken;
            var t1 = StandardTree("provider1");
            var t2 = StandardTree("provider2");
            await using var manager = new TransferManager();
            var h = await OpenWorkspaceAsync(t2.Provider, manager, t2.Dest);
            h.Workspace.SetClipboard([t1.A, t1.B], t1.Provider, isCut: false);
            await h.Workspace.PasteCommand.Execute();
            Assert.True(await t2.Provider.ExistsAsync(t2.Dest.Path.Combine("a.txt"), ct));
            Assert.True(await t2.Provider.ExistsAsync(t2.Dest.Path.Combine("b.txt"), ct));
        }

        [AvaloniaFact]
        public async Task Paste_AfterCompletion_RemovesTransferAndRefreshesTab()
        {
            var ct = TestContext.Current.CancellationToken;
            var t = StandardTree();
            await using var manager = new TransferManager();
            var h = await OpenWorkspaceAsync(t.Provider, manager, t.Dest);
            Assert.Empty(h.Workspace.SelectedTab!.Items);
            h.Workspace.SetClipboard([t.A, t.B], t.Provider, isCut: false);
            await h.Workspace.PasteCommand.Execute();
            Assert.Empty(h.Workspace.ActiveTransfers);
            Assert.Equal(2, h.Workspace.SelectedTab!.Items.Count);
        }
    }
}
