using System.Globalization;
using Avalonia.Data.Converters;
using FileManager.App.Converters;
using Xunit;

namespace FileManager.App.Tests.Converters;

public class BoolToWidthConverterTests
{
    [Fact]
    public void Convert_True_ReturnsOpenWidth()
    {
        var converter = new BoolToWidthConverter();

        var result = converter.Convert(true, typeof(double), null, CultureInfo.InvariantCulture);

        Assert.Equal(260d, result);
    }

    [Fact]
    public void Convert_True_UsesCustomOpenWidth_WhenSet()
    {
        var converter = new BoolToWidthConverter { OpenWidth = 300 };

        var result = converter.Convert(true, typeof(double), null, CultureInfo.InvariantCulture);

        Assert.Equal(300d, result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void Convert_FalseOrNull_ReturnsZero(object? value)
    {
        var converter = new BoolToWidthConverter();

        var result = converter.Convert(value, typeof(double), null, CultureInfo.InvariantCulture);

        Assert.Equal(0d, result);
    }

    [Fact]
    public void Convert_NonBoolValue_ReturnsZero()
    {
        // "is true" only matches a boxed bool, so a wrong-typed value should fall through
        // to the same default as false, not throw.
        var converter = new BoolToWidthConverter();

        var result = converter.Convert("true", typeof(double), null, CultureInfo.InvariantCulture);

        Assert.Equal(0d, result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        var converter = new BoolToWidthConverter();

        Assert.Throws<NotSupportedException>(() =>
            converter.ConvertBack(260d, typeof(bool), null, CultureInfo.InvariantCulture));
    }
}