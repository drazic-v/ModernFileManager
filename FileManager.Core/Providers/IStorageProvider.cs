using System;
using System.Collections.Generic;
using System.Text;
using FileManager.Core.Models;

namespace FileManager.Core.Providers
{
    /// <summary>
    /// The seam between the UI and any storage technology. A Windows local
    /// disk, a Linux filesystem, and an Azure Blob container all implement
    /// this the same way, so nothing above this interface ever needs an
    /// "if (isCloud)" branch.
    ///
    /// Every method is async and takes a CancellationToken - even ones that
    /// feel instant for a local disk - because the same interface also has to
    /// work for a network call over a slow connection.
    /// </summary>
    internal interface IStorageProvider
    {
        /// <summary>
        /// Identifies this provider instance - see StoragePath.ProviderId.
        /// </summary>
        string ProviderId { get; }

        IAsyncEnumerable<StorageItem> ListAsync(StoragePath folder, CancellationToken ct = default);

        Task<StorageItem> CreateDirectoryAsync(StoragePath parent, string name, CancellationToken ct = default);

        Task<StorageItem> GetInfoAsync(StoragePath path,  CancellationToken ct = default);

        Task DeleteAsync(StoragePath path, CancellationToken ct = default);

        Task<StorageItem> RenameAsync(StoragePath path, string newName, CancellationToken ct = default);

        /// <summary>
        /// Same-provider copy only, on purpose => this lets a
        /// provider take a shortcut when one exists (an Azure server-side
        /// copy, an NTFS same-volume move) instead of always reading and
        /// rewriting every byte through the app.
        /// </summary>
        Task<StorageItem> CopyAsync(StoragePath source, StoragePath destination, CancellationToken ct = default);
        Task<StorageItem> MoveAsync(StoragePath source, StoragePath destination, CancellationToken ct = default);

        Task OpenAsync(StoragePath path, CancellationToken ct = default);

        IAsyncEnumerable<StorageItem> SearchAsync(StoragePath root, string query, CancellationToken ct = default);
        
        /// <summary>
        /// The building blocks a future TransferManager uses for cross-provider
        /// work (local disk to Azure, Azure to OneDrive, etc). It opens a read
        /// stream from the source provider and a write stream on the
        /// destination provider and pumps bytes between them itself, reporting
        /// its own progress.
        /// </summary>
        Task<Stream> OpenReadAsync(StoragePath path, CancellationToken ct = default);
        Task<Stream> OpenWriteAsync(StoragePath destination, CancellationToken ct = default);
    }
}
