using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ClipStudioDesktop.Converters
{
    /// <summary>
    /// Converts audio level (0.0 to 1.0) to a solid color based on percentage:
    /// 0-25% = Green, 26-55% = Yellow, 56-70% = Orange, 71-100% = Red
    /// </summary>
    public class LevelToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double level)
            {
                double percentage = Math.Max(0, Math.Min(1, level)) * 100;

                if (percentage <= 25)
                    return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 200, 0));      // Green
                else if (percentage <= 55)
                    return new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 200, 0));    // Yellow  
                else if (percentage <= 70)
                    return new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 140, 0));    // Orange
                else
                    return new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 0, 0));      // Red
            }
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 200, 0));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
