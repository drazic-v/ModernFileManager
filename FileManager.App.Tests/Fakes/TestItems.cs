using FileManager.Core.Models;

namespace FileManager.App.Tests.Fakes;

internal static class TestItems
{
    public static StoragePath Path(string value) => new() { ProviderId = "fake", Value = value };

    public static StorageItem Folder(StoragePath path) => new()
    {
        Path = path,
        Name = path.Name,
        Kind = StorageItemKind.Directory
    };

    public static StorageItem File(StoragePath path, long sizeInBytes = 0, FileAttributes attributes = FileAttributes.Normal) => new()
    {
        Path = path,
        Name = path.Name,
        Kind = StorageItemKind.File,
        SizeInBytes = sizeInBytes,
        Attributes = attributes
    };
}