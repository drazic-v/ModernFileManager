using System.Globalization;
using System.Threading;
using FileManager.App.Converters;
using Xunit;

namespace FileManager.App.Tests.Converters;

public class BytesToSizeConverterTests : IDisposable
{
    private readonly CultureInfo _originalCulture;
    private readonly BytesToSizeConverter _converter = new();

    public BytesToSizeConverterTests()
    {
        _originalCulture = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
    }

    public void Dispose() => Thread.CurrentThread.CurrentCulture = _originalCulture;

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(500L, "500 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(1048576L, "1 MB")]
    [InlineData(1073741824L, "1 GB")]
    [InlineData(1099511627776L, "1 TB")]
    public void Convert_LongByteCount_FormatsToExpectedUnit(long bytes, string expected)
    {
        var result = _converter.Convert(bytes, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Convert_BeyondTerabyteScale_DoesNotRollOverPastTB()
    {
        // units[] tops out at TB (index 4), so anything larger just keeps a bigger
        // TB number rather than inventing a PB unit. Documenting current behavior.
        long bytes = 1024L * 1024 * 1024 * 1024 * 1024; // 1 PB

        var result = _converter.Convert(bytes, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("1024 TB", result);
    }

    [Fact]
    public void Convert_NullValue_ReturnsPlaceholder()
    {
        var result = _converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("--", result);
    }

    [Fact]
    public void Convert_NonLongValue_ReturnsPlaceholder()
    {
        // The pattern match is exactly `long` - an int wouldn't satisfy it.
        var result = _converter.Convert(42, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("--", result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() =>
            _converter.ConvertBack("1 KB", typeof(long), null, CultureInfo.InvariantCulture));
    }
}