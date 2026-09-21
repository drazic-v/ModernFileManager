using FileManager.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace FileManager.App.ViewModels
{
    public partial class ViewModelBase
    {
        private async Task LoadAsync(StoragePath folder)
        {
            var token = BeginNewOperation();
            var hasCleared = false;
            try
            {
                await foreach (var item in _provider.ListAsync(folder, token))
                {
                    if (!hasCleared)
                    {
                        Items.Clear();
                        SelectedItem = null;
                        SelectedItems.Clear();
                        hasCleared = true;
                    }
                    if (_showHiddenItems || !StorageItemFilters.IsHidden(item))
                        Items.Add(item);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _notifications.ShowError($"Couldn't open \"{folder.Name}\": {ex.Message}");
                return;
            }

            if (!hasCleared)
            {
                // loop completed with zero items - a genuinely empty folder still needs to *look* empty
                Items.Clear();
                SelectedItem = null;
                SelectedItems.Clear();
            }

            CurrentFolder = folder;
            IsSearchActive = false;
        }

        private void UpdateNavigationState()
        {
            CanGoBack = _backStack.Count > 0;
            CanGoForward = _forwardStack.Count > 0;
        }

        private async Task NavigateToAsync(StoragePath folder)
        {
            var previous = CurrentFolder;
            await LoadAsync(folder);

            if (!StoragePath.PathsEqual(CurrentFolder, folder))
                return; // LoadAsync failed - CurrentFolder never actually changed, nothing to record

            _backStack.Push(previous);
            _forwardStack.Clear();
            UpdateNavigationState();
        }

        public async Task NavigateIntoAsync(StorageItem item)
        {
            if (item.IsFolder)
                await NavigateToAsync(item.Path);
        }

        public async Task NavigateUpAsync()
        {
            if (CurrentFolder.Parent() is { } parent)
                await NavigateToAsync(parent);
        }

        public async Task RefreshAsync()
        {
            await LoadAsync(CurrentFolder);
        }

        public async Task BackAsync()
        {
            if (_backStack.Count == 0) return;
            var target = _backStack.Peek();
            var before = CurrentFolder;
            await LoadAsync(target);

            if (!StoragePath.PathsEqual(CurrentFolder, target))
                return; // leave the stacks untouched - the entry might still be valid later

            _backStack.Pop();
            _forwardStack.Push(before);
            UpdateNavigationState();
        }

        public async Task ForwardAsync()
        {
            if (_forwardStack.Count == 0) return;
            var target = _forwardStack.Peek();
            var before = CurrentFolder;
            await LoadAsync(target);

            if (!StoragePath.PathsEqual(CurrentFolder, target))
                return;

            _forwardStack.Pop();
            _backStack.Push(before);
            UpdateNavigationState();
        }
    }
}
