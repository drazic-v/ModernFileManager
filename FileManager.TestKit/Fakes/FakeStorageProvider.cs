using FileManager.Core.Models;
using FileManager.Core.Providers;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace FileManager.TestKit
{
    public sealed class FakeStorageProvider : IStorageProvider
    {
        private readonly Dictionary<string, List<StorageItem>> _children = new();
        private readonly Dictionary<string, Exception> _listFailures = new();
        private readonly Dictionary<(string Root, string Query), List<StorageItem>> _searchResults = new();
        private readonly Dictionary<(string Root, string Query), Exception> _searchFailures = new();

        public string ProviderId => "fake";

        public void AddChildren(string folderValue, params StorageItem[] items) =>
            _children[folderValue] = items.ToList();

        public void ThrowOnList(string folderValue, Exception exception) =>
            _listFailures[folderValue] = exception;

        public void AddSearchResults(string rootValue, string query, params StorageItem[] items) =>
            _searchResults[(rootValue, query)] = items.ToList();

        public void ThrowOnSearch(string rootValue, string query, Exception exception) =>
            _searchFailures[(rootValue, query)] = exception;

        public async IAsyncEnumerable<StorageItem> ListAsync(StoragePath folder, [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (_listFailures.TryGetValue(folder.Value, out var exception))
                throw exception;

            if (_children.TryGetValue(folder.Value, out var items))
                foreach (var item in items)
                {
                    ct.ThrowIfCancellationRequested();
                    yield return item;
                }
        }

        public async IAsyncEnumerable<StorageItem> SearchAsync(StoragePath root, string query, [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (_searchFailures.TryGetValue((root.Value, query), out var exception))
                throw exception;

            if (_searchResults.TryGetValue((root.Value, query), out var items))
                foreach (var item in items)
                {
                    ct.ThrowIfCancellationRequested();
                    yield return item;
                }
        }

        public Task<StorageItem> GetInfoAsync(StoragePath path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StorageItem> CreateDirectoryAsync(StoragePath parent, string name, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(StoragePath path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StorageItem> RenameAsync(StoragePath path, string newName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StorageItem> CopyAsync(StoragePath source, StoragePath destinationFolder, ConflictResolver resolver, IProgress<TransferProgress>? progress = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StorageItem> MoveAsync(StoragePath source, StoragePath destinationFolder, ConflictResolver resolver, IProgress<TransferProgress>? progress = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task OpenFileAsync(StoragePath path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Stream> OpenReadAsync(StoragePath path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Stream> OpenWriteAsync(StoragePath destination, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
