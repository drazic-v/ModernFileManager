using Avalonia.Threading;
using FileManager.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace FileManager.App.ViewModels
{
    public partial class ViewModelBase
    {
        public async Task SearchCurrentFolderAsync(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                await LoadAsync(CurrentFolder);
                return;
            }

            var token = BeginNewOperation();
            Items.Clear();
            SelectedItems.Clear();
            IsSearchActive = true;
            await Dispatcher.Yield(DispatcherPriority.Background); // let the Cancel button actually paint before the heavy work starts

            var count = 0;
            try
            {
                await foreach (var item in _provider.SearchAsync(CurrentFolder, query, token))
                {
                    if (_showHiddenItems || !StorageItemFilters.IsHidden(item))
                        Items.Add(item);

                    if (++count % 25 == 0)
                        await Dispatcher.Yield(DispatcherPriority.Background);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _notifications.ShowError($"Couldn't search \"{CurrentFolder.Name}\": {ex.Message}");
                return;
            }
        }

        public async Task ClearSearchAsync()
        {
            SearchText = string.Empty;
            await LoadAsync(CurrentFolder);
        }
    }
}
