using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.Core.Models
{
    public class StorageItem
    {
        public required StoragePath Path { get; init; }
        public required string Name { get; init; }
        public required StorageItemKind Kind { get; init; }

        /// <summary>
        /// Null for directories/drives, and for files where the provider
        /// couldn't determine size cheaply (some cloud APIs need a second call
        /// for that) - better to show "-" in the UI than block listing on it.
        /// </summary>
        public long? SizeInBytes { get; init; }

        public DateTimeOffset? LastModifiedUtc { get; init; }
        public DateTimeOffset? CreatedUtc { get; init; }

        /// <summary>
        /// Local providers can derive this from the file extension, Azure can
        /// fill it from blob content-type. Left as a plain string so each
        /// provider fills it however makes sense - the UI just displays it.
        /// </summary>
        public string? ContentType { get; init; }

        /// <summary>
        /// LocalStorageProvider can pass FileInfo.Attributes straight through
        /// with zero conversion. Cloud providers with nothing equivalent just
        /// leave it as Normal, or map whatever comes closest - e.g. an
        /// immutable blob as ReadOnly. Check a flag with
        /// Attributes.HasFlag(FileAttributes.Hidden).
        /// </summary>
        public FileAttributes Attributes { get; init; } = FileAttributes.Normal;

        public bool IsFolder => Kind is StorageItemKind.Directory or StorageItemKind.Drive;
    }
}
