using FileManager.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;
using FileManager.Core.Providers;

namespace FileManager.App.ViewModels
{
    public class ProviderViewModel
    {
        public ProviderViewModel(string displayName, IStorageProvider provider, StoragePath startingFolder)
        {
            DisplayName = displayName;
            Provider = provider;
            StartingFolder = startingFolder;
        }

        public string DisplayName { get; }
        public IStorageProvider Provider { get; }
        public StoragePath StartingFolder { get; }
    }
}
