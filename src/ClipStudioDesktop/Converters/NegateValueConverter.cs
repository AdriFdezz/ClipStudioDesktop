using System;
using System.Globalization;
using System.Windows.Data;

namespace ClipStudioDesktop.Converters
{
    /// <summary>
    /// Converts positive slider values to negative dB values for noise gate
    /// UI: 0 to 30 → Stored: 0 to -30
    /// </summary>
    public class NegateValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
            {
                return -d; // Stored -30 becomes UI 30
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
            {
                return -d; // UI 30 becomes stored -30
            }
            return 0;
        }
    }
}
