using System;
using System.Globalization;
using System.Windows.Data;

namespace ClipStudioDesktop.Converters
{
    /// <summary>
    /// Formats a number with a negative sign, always showing the sign even for 0
    /// </summary>
    public class NegativeFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
            {
                // Always show negative sign, even for 0
                return $"-{Math.Abs(d):F0} dB";
            }
            return "-0 dB";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
