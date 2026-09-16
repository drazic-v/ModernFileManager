using FileManager.Core.Models;
using FileManager.Core.Providers;
using System.Collections.Generic;

namespace FileManager.App.ViewModels;

// What's currently "copied" or "cut" - remembers which provider it came from
// so Paste can tell whether a same-provider CopyAsync/MoveAsync is even valid yet.
public class ClipboardEntry
{
    public ClipboardEntry(IReadOnlyList<StorageItem> items, IStorageProvider sourceProvider, bool isCut)
    {
        Items = items;
        SourceProvider = sourceProvider;
        IsCut = isCut;
    }

    public IReadOnlyList<StorageItem> Items { get; }
    public IStorageProvider SourceProvider { get; }
    public bool IsCut { get; }
}