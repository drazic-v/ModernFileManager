using FileManager.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace FileManager.App.ViewModels
{
    public partial class ViewModelBase
    {
        public async Task OpenItemAsync(StorageItem item)
        {
            if (item.Kind == StorageItemKind.Directory)
            {
                await NavigateIntoAsync(item);
                return;
            }
            try
            {
                await _provider.OpenFileAsync(item.Path);
            }
            catch (Exception ex)
            {
                _notifications.ShowError($"Couldn't open \"{item.Name}\": {ex.Message}");
                return;
            }
        }
        
        public async Task DeleteItemsAsync(IReadOnlyList<StorageItem> items)
        {
            var succeeded = new List<StorageItem>();
            var failed = new List<string>();

            foreach (var item in items)
            {
                try
                {
                    await _provider.DeleteAsync(item.Path);
                    succeeded.Add(item);
                }
                catch (Exception ex)
                {
                    failed.Add($"{item.Name}: {ex.Message}");
                }
            }

            foreach (var item in succeeded)
            {
                Items.Remove(item);
                SelectedItems.Remove(item);
                if (SelectedItem == item) SelectedItem = null;
            }

            if (failed.Count > 0)
            {
                var message = succeeded.Count == 0
                    ? $"Nothing could be deleted. First error: {failed[0]}"
                    : $"Deleted {succeeded.Count} item(s), {failed.Count} failed. First error: {failed[0]}";
                _notifications.ShowError(message);
            }
            else
            {
                _notifications.ShowSuccess(succeeded.Count == 1 ? $"Deleted \"{succeeded[0].Name}\"." : $"Deleted {succeeded.Count} items.");
            }
        }

        public async Task<StorageItem?> CreateFolderAsync()
        {
            try
            {
                var created = await _provider.CreateDirectoryAsync(CurrentFolder, "New Folder");
                Items.Add(created);
                SelectedItem = created;
                return created;
            }
            catch (Exception ex)
            {
                _notifications.ShowError($"Couldn't create folder in \"{CurrentFolder.Name}\": {ex.Message}");
                return null;
            }
        }

        public async Task RenameItemAsync(StorageItem item, string newName)
        {
            if (!Items.Contains(item))
                return; // already handled by an earlier call - nothing left to do

            try
            {
                var renamed = await _provider.RenameAsync(item.Path, newName);
                var index = Items.IndexOf(item);
                if (index >= 0) Items[index] = renamed;
                if (SelectedItem == item) SelectedItem = renamed;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _notifications.ShowError($"Couldn't rename \"{item.Name}\": {ex.Message}");
                return;
            }
        }
    }
}
