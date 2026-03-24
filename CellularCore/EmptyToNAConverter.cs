using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace CellularCore
{
    public class EmptyToNAConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return "N/A";

            // if it's a string, check for empty/whitespace
            if (value is string s)
            {
                return string.IsNullOrWhiteSpace(s) ? "N/A" : s;
            }

            // otherwise return .ToString()
            return value.ToString() ?? "N/A";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
