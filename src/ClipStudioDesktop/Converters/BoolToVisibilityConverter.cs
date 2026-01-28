using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ClipStudioDesktop.Converters
{
    /// <summary>
    /// Convierte un valor booleano en un valor de Visibilidad de WPF.
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        /// <summary>
        /// Convierte un valor booleano a Visibility.
        /// </summary>
        /// <param name="value">El valor booleano a convertir.</param>
        /// <param name="targetType">El tipo del objetivo de enlace (ignorado).</param>
        /// <param name="parameter">Parámetro opcional (ignorado).</param>
        /// <param name="culture">La cultura a usar en la conversión (ignorado).</param>
        /// <returns>Visibility.Visible si value es true; de lo contrario, Visibility.Collapsed.</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Verificamos si el valor es un booleano y si es verdadero
            if (value is bool b && b)
                return Visibility.Visible;
            
            // En cualquier otro caso (false o null), colapsamos el elemento
            return Visibility.Collapsed;
        }

        /// <summary>
        /// Convierte un valor de Visibility a booleano.
        /// </summary>
        /// <param name="value">El valor de Visibility a convertir.</param>
        /// <param name="targetType">El tipo del objetivo de enlace (ignorado).</param>
        /// <param name="parameter">Parámetro opcional (ignorado).</param>
        /// <param name="culture">La cultura a usar en la conversión (ignorado).</param>
        /// <returns>true si la visibilidad es Visible; de lo contrario, false.</returns>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility v)
                return v == Visibility.Visible;
            return false;
        }
    }
}
