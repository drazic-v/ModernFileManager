using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.Core.Models
{
    public static class StorageItemFilters
    {
        public static bool IsHidden(StorageItem item) =>
        item.Attributes.HasFlag(FileAttributes.Hidden) ||
        item.Attributes.HasFlag(FileAttributes.System);
    }
}
