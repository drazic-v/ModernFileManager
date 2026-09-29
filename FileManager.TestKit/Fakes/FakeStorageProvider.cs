using FileManager.Core.Models;
using FileManager.Core.Providers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace FileManager.TestKit
{
    public sealed class FakeStorageProvider : IStorageProvider
    {
        private readonly Dictionary<string, List<StorageItem>> _children = new();
        private readonly Dictionary<string, StorageItem> _itemsByPath = new();
        private readonly Dictionary<string, byte[]> _content = new();
        private readonly Dictionary<string, Exception> _listFailures = new();
        private readonly Dictionary<(string Root, string Query), List<StorageItem>> _searchResults = new();
        private readonly Dictionary<(string Root, string Query), Exception> _searchFailures = new();

        private int _copyFailuresRemaining;
        private Exception? _scriptedCopyException;

        public FakeStorageProvider(string providerId = "fake") => ProviderId = providerId;

        public string ProviderId { get; }

        public void FailNextCopyAttempts(int count, Exception? exception = null)
        {
            _copyFailuresRemaining = count;
            _scriptedCopyException = exception ?? new IOException("Simulated copy failure.");
        }

        private void MaybeThrowScriptedCopyFailure()
        {
            if (_copyFailuresRemaining <= 0) return;
            _copyFailuresRemaining--;
            throw _scriptedCopyException!;
        }

        public void AddChildren(string folderValue, params StorageItem[] items)
        {
            _children[folderValue] = items.ToList();
            foreach (var item in items)
                _itemsByPath[item.Path.Value] = item;
        }

        public void SetContent(StoragePath path, byte[] content) => _content[path.Value] = content;

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

        public Task<bool> ExistsAsync(StoragePath path, CancellationToken ct = default)
        {
            if (path.Parent() is not { } parent || !_children.TryGetValue(parent.Value, out var siblings))
                return Task.FromResult(false);

            return Task.FromResult(siblings.Any(item => StoragePath.PathsEqual(item.Path, path)));
        }

        public Task<StorageItem> GetInfoAsync(StoragePath path, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (_itemsByPath.TryGetValue(path.Value, out var item))
                return Task.FromResult(item);

            throw new FileNotFoundException($"No such item: {path.Value}");
        }

        public Task<StorageItem> CreateDirectoryAsync(StoragePath parent, string name, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var path = parent.Combine(name);
            var item = new StorageItem
            {
                Path = path,
                Name = name,
                Kind = StorageItemKind.Directory
            };

            _itemsByPath[path.Value] = item;

            if (!_children.TryGetValue(parent.Value, out var siblings))
                _children[parent.Value] = siblings = new List<StorageItem>();
            siblings.RemoveAll(i => StoragePath.PathsEqual(i.Path, path));
            siblings.Add(item);

            if (!_children.ContainsKey(path.Value))
                _children[path.Value] = new List<StorageItem>();

            return Task.FromResult(item);
        }

        public Task DeleteAsync(StoragePath path, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _itemsByPath.Remove(path.Value);
            _content.Remove(path.Value);
            _children.Remove(path.Value);

            if (path.Parent() is { } parent && _children.TryGetValue(parent.Value, out var siblings))
                siblings.RemoveAll(i => StoragePath.PathsEqual(i.Path, path));

            return Task.CompletedTask;
        }

        public Task<StorageItem> RenameAsync(StoragePath path, string newName, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task OpenFileAsync(StoragePath path, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public async Task<StorageItem> CopyAsync(StoragePath source, StoragePath destinationFolder, ConflictResolver resolver, IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
        {
            var (item, _) = await CopyOrSkipAsync(source, destinationFolder, resolver, progress, ct);
            return item;
        }

        public async Task<StorageItem> MoveAsync(StoragePath source, StoragePath destinationFolder, ConflictResolver resolver, IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
        {
            var (item, skipped) = await CopyOrSkipAsync(source, destinationFolder, resolver, progress, ct);
            if (!skipped)
                await DeleteAsync(source, ct);
            return item;
        }

        private async Task<(StorageItem Item, bool Skipped)> CopyOrSkipAsync(
            StoragePath source, StoragePath destinationFolder, ConflictResolver resolver,
            IProgress<TransferProgress>? progress, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            MaybeThrowScriptedCopyFailure();

            if (!_itemsByPath.TryGetValue(source.Value, out var sourceItem))
                throw new FileNotFoundException($"No such item: {source.Value}");

            if (sourceItem.Kind != StorageItemKind.File)
                throw new NotImplementedException("FakeStorageProvider.CopyAsync only supports files.");

            var destinationPath = destinationFolder.Combine(source.Name);

            if (_itemsByPath.TryGetValue(destinationPath.Value, out var conflicting))
            {
                var policy = await resolver(destinationPath, conflicting.Kind, ct);
                switch (policy)
                {
                    case NameCollisionPolicy.GenerateUnique:
                        var newName = await UniqueNameGenerator.GenerateAsync(
                            this, destinationFolder, source.Name, StorageItemKind.File, excludeName: null, ct: ct);
                        destinationPath = destinationFolder.Combine(newName);
                        break;
                    case NameCollisionPolicy.Replace:
                        await DeleteAsync(destinationPath, ct);
                        break;
                    case NameCollisionPolicy.Skip:
                        return (conflicting, true);
                    case NameCollisionPolicy.Fail:
                        throw new IOException($"Item already exists at destination: {destinationPath.Value}");
                    case NameCollisionPolicy.Merge:
                        throw new InvalidOperationException("Merge is not applicable to a file-vs-file conflict.");
                }
            }

            var bytes = _content.TryGetValue(source.Value, out var sourceBytes) ? sourceBytes : Array.Empty<byte>();
            var newItem = new StorageItem
            {
                Path = destinationPath,
                Name = destinationPath.Name,
                Kind = StorageItemKind.File,
                SizeInBytes = sourceItem.SizeInBytes,
                ContentType = sourceItem.ContentType,
                Attributes = sourceItem.Attributes
            };

            _content[destinationPath.Value] = bytes;
            _itemsByPath[destinationPath.Value] = newItem;

            if (!_children.TryGetValue(destinationFolder.Value, out var siblings))
                _children[destinationFolder.Value] = siblings = new List<StorageItem>();
            siblings.RemoveAll(i => StoragePath.PathsEqual(i.Path, destinationPath));
            siblings.Add(newItem);

            progress?.Report(new TransferProgress(sourceItem.SizeInBytes ?? 0, 1));

            return (newItem, false);
        }

        public Task<Stream> OpenReadAsync(StoragePath path, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!_itemsByPath.ContainsKey(path.Value))
                throw new FileNotFoundException($"No such item: {path.Value}");

            var bytes = _content.TryGetValue(path.Value, out var content) ? content : Array.Empty<byte>();
            return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        }

        public Task<Stream> OpenWriteAsync(StoragePath destination, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<Stream>(new CommittingWriteStream(this, destination));
        }

        private void CommitWrittenFile(StoragePath destination, byte[] bytes)
        {
            var item = new StorageItem
            {
                Path = destination,
                Name = destination.Name,
                Kind = StorageItemKind.File,
                SizeInBytes = bytes.LongLength
            };

            _content[destination.Value] = bytes;
            _itemsByPath[destination.Value] = item;

            if (destination.Parent() is { } parent)
            {
                if (!_children.TryGetValue(parent.Value, out var siblings))
                    _children[parent.Value] = siblings = new List<StorageItem>();
                siblings.RemoveAll(i => StoragePath.PathsEqual(i.Path, destination));
                siblings.Add(item);
            }
        }

        private sealed class CommittingWriteStream : MemoryStream
        {
            private readonly FakeStorageProvider _owner;
            private readonly StoragePath _destination;
            private bool _committed;

            public CommittingWriteStream(FakeStorageProvider owner, StoragePath destination)
            {
                _owner = owner;
                _destination = destination;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && !_committed)
                {
                    _committed = true;
                    _owner.CommitWrittenFile(_destination, ToArray());
                }
                base.Dispose(disposing);
            }
        }
    }
}