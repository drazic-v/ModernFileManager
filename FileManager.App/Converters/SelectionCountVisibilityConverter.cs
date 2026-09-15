using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FileManager.App.Converters
{
    public class SelectionCountVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not int count) return false;
            return (parameter as string) switch
            {
                "None" => count == 0,
                "Single" => count == 1,
                "Multiple" => count > 1,
                _ => false
            };
        }
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
