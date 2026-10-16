using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.Core.Models
{
    public sealed class StoragePathComparer : IEqualityComparer<StoragePath>
    {
        public static readonly StoragePathComparer Instance = new ();

        public bool Equals(StoragePath? x, StoragePath? y) =>
            ReferenceEquals(x, y) || (x is not null && y is not null && StoragePath.PathsEqual(x, y));
        public int GetHashCode(StoragePath obj) => HashCode.Combine(obj.ProviderId, obj.Value);
    }
}
