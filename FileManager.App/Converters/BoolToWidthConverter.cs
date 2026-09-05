using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FileManager.App.Converters
{
    public class BoolToWidthConverter : IValueConverter
    {
        public double OpenWidth { get; set; } = 260;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is true ? OpenWidth : 0d;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
