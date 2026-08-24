using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.Core.Models
{
    /// <summary>
    /// Represents the kind of a storage item. Drive exists because
    /// "This PC > C:" and a cloud account's root need to render as
    /// tree nodes too, even though they aren't really folders on disk.
    /// </summary>
    public enum StorageItemKind
    {
        File,
        Directory,
        Drive
    }
}