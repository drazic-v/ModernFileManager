using System.Globalization;
using FileManager.App.Converters;
using Xunit;

namespace FileManager.App.Tests.Converters;

public class GreaterThanZeroConverterTests
{
    private readonly GreaterThanZeroConverter _converter = new();

    [Theory]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void Convert_IntValue_ReturnsExpected(int value, bool expected)
    {
        var result = _converter.Convert(value, typeof(bool), null, CultureInfo.InvariantCulture);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Convert_NonIntValue_ReturnsFalse()
    {
        // A boxed long shouldn't satisfy an `is int` pattern, even though a long can
        // hold the same value - the type check is exact.
        var result = _converter.Convert(5L, typeof(bool), null, CultureInfo.InvariantCulture);

        Assert.Equal(false, result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() =>
            _converter.ConvertBack(true, typeof(int), null, CultureInfo.InvariantCulture));
    }
}