using Avalonia.Data.Converters;
using FileManager.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FileManager.App.Converters
{
    public class ItemKindVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is StorageItem item && item.Kind.ToString() == parameter as string;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
