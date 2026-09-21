using Avalonia.Threading;
using DynamicData.Kernel;
using FileManager.Core.Models;
using FileManager.Core.Providers;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FileManager.App.ViewModels
{
    public partial class ViewModelBase
    {
        private void OnSelectedItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            _ = UpdateFolderInfoAsync();
            _ = UpdateMultiSelectionInfoAsync();
        }

        private async Task UpdateMultiSelectionInfoAsync()
        {
            _multiSelectionCts?.Cancel();
            _multiSelectionCts?.Dispose();
            _multiSelectionCts = null;
            MultiSelectionSizeBytes = null;

            var items = SelectedItems.AsList();
            if (!IsDetailsPanelOpen || items.Count <= 1) return;

            _multiSelectionCts = new CancellationTokenSource();
            var token = _multiSelectionCts.Token;
            IsMultiSelectionSizeLoading = true;

            await Dispatcher.Yield(DispatcherPriority.Background);
            try
            {
                long completedTotal = 0;
                foreach (var item in items)
                {
                    token.ThrowIfCancellationRequested();

                    if (item.Kind == StorageItemKind.Directory)
                    {
                        var runningTotal = completedTotal;
                        var progress = new Progress<FolderInfoCalculator.FolderInfo>(info =>
                            MultiSelectionSizeBytes = runningTotal + info.Size);

                        var result = await Task.Run(() => FolderInfoCalculator.GetFolderInfo(_provider, item.Path, progress, token), token);
                        completedTotal += result.Size;
                    }
                    else
                    {
                        completedTotal += item.SizeInBytes ?? 0;
                    }

                    MultiSelectionSizeBytes = completedTotal;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _notifications.ShowError($"Couldn't calculate selection size: {ex.Message}");
                return;
            }
            finally
            {
                if (!token.IsCancellationRequested)
                    IsMultiSelectionSizeLoading = false;
            }
        }

        private async Task UpdateFolderInfoAsync()
        {
            _folderInfoCts?.Cancel();
            _folderInfoCts?.Dispose();
            _folderInfoCts = null;

            FolderSizeInBytes = null;
            FolderFileCount = null;
            FolderFolderCount = null;

            if (!IsDetailsPanelOpen || SelectedItems.Count != 1 || SelectedItems[0].Kind != StorageItemKind.Directory)
                return;
            var folder = SelectedItems[0];

            _folderInfoCts = new CancellationTokenSource();
            var token = _folderInfoCts.Token;
            IsFolderInfoLoading = true;

            var progress = new Progress<FolderInfoCalculator.FolderInfo>(info =>
            {
                FolderSizeInBytes = info.Size;
                FolderFileCount = info.Files;
                FolderFolderCount = info.Folders;
            });

            await Dispatcher.Yield(DispatcherPriority.Background); // let the Cancel button actually paint before the heavy work starts

            try
            {
                await Task.Run(() => FolderInfoCalculator.GetFolderInfo(_provider, folder.Path, progress, token), token);
            }
            catch (OperationCanceledException)
            {
                return; // a newer selection superseded this calculation
            }
            catch (Exception ex)
            {
                _notifications.ShowError($"Couldn't calculate folder size: {ex.Message}");
                return;
            }
            finally
            {
                if (!token.IsCancellationRequested)
                    IsFolderInfoLoading = false;
            }
        }
    }
}
