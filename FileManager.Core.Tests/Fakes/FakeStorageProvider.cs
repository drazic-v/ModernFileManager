using FileManager.Core.Models;
using FileManager.Core.Providers;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace FileManager.Core.Tests.Fakes
{
    internal sealed class FakeStorageProvider : IStorageProvider
    {
        private readonly Dictionary<string, List<StorageItem>> _children = new();

        public string ProviderId => "fake";

        public void AddChildren(string folderValue, params StorageItem[] items) =>
            _children[folderValue] = items.ToList();

        public async IAsyncEnumerable<StorageItem> ListAsync(StoragePath folder, [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (_children.TryGetValue(folder.Value, out var items))
                foreach (var item in items)
                    yield return item;
        }

        public Task<StorageItem> GetInfoAsync(StoragePath path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StorageItem> CreateDirectoryAsync(StoragePath parent, string name, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(StoragePath path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StorageItem> RenameAsync(StoragePath path, string newName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StorageItem> CopyAsync(StoragePath source, StoragePath destinationFolder, ConflictResolver resolver, IProgress<TransferProgress>? progress = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StorageItem> MoveAsync(StoragePath source, StoragePath destinationFolder, ConflictResolver resolver, IProgress<TransferProgress>? progress = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task OpenAsync(StoragePath path, CancellationToken ct = default) => throw new NotImplementedException();

        public IAsyncEnumerable<StorageItem> SearchAsync(StoragePath root, string query, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Stream> OpenReadAsync(StoragePath path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Stream> OpenWriteAsync(StoragePath destination, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
