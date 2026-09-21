using System.Globalization;
using FileManager.App.Converters;
using Xunit;

namespace FileManager.App.Tests.Converters;

public class SelectionCountVisibilityConverterTests
{
    private readonly SelectionCountVisibilityConverter _converter = new();

    [Theory]
    [InlineData(0, "None", true)]
    [InlineData(1, "None", false)]
    [InlineData(2, "None", false)]
    [InlineData(1, "Single", true)]
    [InlineData(0, "Single", false)]
    [InlineData(2, "Single", false)]
    [InlineData(2, "Multiple", true)]
    [InlineData(5, "Multiple", true)]
    [InlineData(1, "Multiple", false)]
    [InlineData(0, "Multiple", false)]
    public void Convert_CountAndBucket_ReturnsExpected(int count, string bucket, bool expected)
    {
        var result = _converter.Convert(count, typeof(bool), bucket, CultureInfo.InvariantCulture);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Convert_UnrecognizedParameter_ReturnsFalse()
    {
        var result = _converter.Convert(3, typeof(bool), "Everything", CultureInfo.InvariantCulture);

        Assert.Equal(false, result);
    }

    [Fact]
    public void Convert_NullParameter_ReturnsFalse()
    {
        var result = _converter.Convert(3, typeof(bool), null, CultureInfo.InvariantCulture);

        Assert.Equal(false, result);
    }

    [Fact]
    public void Convert_NonIntValue_ReturnsFalse()
    {
        var result = _converter.Convert("3", typeof(bool), "Single", CultureInfo.InvariantCulture);

        Assert.Equal(false, result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() =>
            _converter.ConvertBack(true, typeof(int), "Single", CultureInfo.InvariantCulture));
    }
}