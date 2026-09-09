using FileManager.Core.Models;
using FileManager.Core.Providers;

namespace FileManager.App.ViewModels;

// What's currently "copied" or "cut" - remembers which provider it came from
// so Paste can tell whether a same-provider CopyAsync/MoveAsync is even valid yet.
public class ClipboardEntry
{
    public ClipboardEntry(StorageItem item, IStorageProvider sourceProvider, bool isCut)
    {
        Item = item;
        SourceProvider = sourceProvider;
        IsCut = isCut;
    }

    public StorageItem Item { get; }
    public IStorageProvider SourceProvider { get; }
    public bool IsCut { get; }
}