using FileManager.Core.Models;
using FileManager.Core.Providers;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;


namespace FileManager.Infrastructure.Providers
{
    public class LocalStorageProvider : IStorageProvider
    {
        public string ProviderId { get; } = "local";

        public async Task<StorageItem> GetInfoAsync(StoragePath path, CancellationToken ct = default) 
        {
            throw new NotImplementedException();
        }

        public async IAsyncEnumerable<StorageItem> ListAsync(StoragePath folder, [EnumeratorCancellation] CancellationToken ct = default) 
        {
            throw new NotImplementedException();
        }

        public Task<StorageItem> CreateDirectoryAsync(StoragePath parent, string name, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(StoragePath path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StorageItem> RenameAsync(StoragePath path, string newName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StorageItem> CopyAsync(StoragePath source, StoragePath destinationFolder, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StorageItem> MoveAsync(StoragePath source, StoragePath destinationFolder, CancellationToken ct = default) => throw new NotImplementedException();
        public Task OpenAsync(StoragePath path, CancellationToken ct = default) => throw new NotImplementedException();

        public IAsyncEnumerable<StorageItem> SearchAsync(StoragePath root, string query, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Stream> OpenReadAsync(StoragePath path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Stream> OpenWriteAsync(StoragePath destination, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
