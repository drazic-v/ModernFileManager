using System.Globalization;
using FileManager.App.Converters;
using FileManager.Core.Models;
using Xunit;

namespace FileManager.App.Tests.Converters;

public class ItemKindVisibilityConverterTests
{
    private readonly ItemKindVisibilityConverter _converter = new();

    private static StorageItem MakeItem(StorageItemKind kind) => new()
    {
        Path = new StoragePath { ProviderId = "local", Value = "/x" },
        Name = "x",
        Kind = kind
    };

    [Fact]
    public void Convert_MatchingKind_ReturnsTrue()
    {
        var item = MakeItem(StorageItemKind.File);

        var result = _converter.Convert(item, typeof(bool), "File", CultureInfo.InvariantCulture);

        Assert.Equal(true, result);
    }

    [Fact]
    public void Convert_NonMatchingKind_ReturnsFalse()
    {
        var item = MakeItem(StorageItemKind.Directory);

        var result = _converter.Convert(item, typeof(bool), "File", CultureInfo.InvariantCulture);

        Assert.Equal(false, result);
    }

    [Fact]
    public void Convert_ParameterCaseMismatch_ReturnsFalse()
    {
        // String == is ordinal here, so "file" won't match Kind.ToString() == "File".
        var item = MakeItem(StorageItemKind.File);

        var result = _converter.Convert(item, typeof(bool), "file", CultureInfo.InvariantCulture);

        Assert.Equal(false, result);
    }

    [Fact]
    public void Convert_ValueIsNotStorageItem_ReturnsFalse()
    {
        var result = _converter.Convert("not an item", typeof(bool), "File", CultureInfo.InvariantCulture);

        Assert.Equal(false, result);
    }

    [Fact]
    public void Convert_NullParameter_ReturnsFalse()
    {
        var item = MakeItem(StorageItemKind.File);

        var result = _converter.Convert(item, typeof(bool), null, CultureInfo.InvariantCulture);

        Assert.Equal(false, result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() =>
            _converter.ConvertBack(true, typeof(StorageItem), "File", CultureInfo.InvariantCulture));
    }
}