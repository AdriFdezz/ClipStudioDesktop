using System;
using System.Globalization;
using System.Windows.Data;

namespace ClipStudioDesktop.Converters
{
    /// <summary>
    /// Converts audio level (0.0 to 1.0) to pixel width for VU meter display
    /// </summary>
    public class LevelToWidthConverter : IValueConverter
    {
        // Maximum width of the VU meter bar in pixels
        private const double MaxWidth = 550;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double level)
            {
                // Clamp to 0-1 range and convert to width
                double clampedLevel = Math.Max(0, Math.Min(1, level));
                return clampedLevel * MaxWidth;
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
