using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ClipStudioDesktop.Converters
{
    /// <summary>
    /// Convierte un nivel de audio (0.0 a 1.0) en un color sólido (Brush) indicativo de intensidad.
    /// <para>
    /// Escala de colores:
    /// <list type="bullet">
    /// <item>0-25%: Verde (Señal baja/normal)</item>
    /// <item>26-55%: Amarillo (Señal media)</item>
    /// <item>56-70%: Naranja (Señal fuerte)</item>
    /// <item>71-100%: Rojo (Posible saturación)</item>
    /// </list>
    /// </para>
    /// </summary>
    public class LevelToColorConverter : IValueConverter
    {
        /// <summary>
        /// Convierte un valor de nivel (double) a un Brush de color.
        /// </summary>
        /// <param name="value">El nivel de audio normalizado (0.0 a 1.0).</param>
        /// <param name="targetType">Tipo objetivo (ignorado).</param>
        /// <param name="parameter">Parámetro opcional (ignorado).</param>
        /// <param name="culture">Cultura (ignorado).</param>
        /// <returns>Un <see cref="SolidColorBrush"/> con el color correspondiente al nivel.</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double level)
            {
                // Aseguramos que el nivel esté entre 0 y 1, y lo convertimos a porcentaje (0-100)
                double percentage = Math.Max(0, Math.Min(1, level)) * 100;

                if (percentage <= 25)
                    return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 200, 0));      // Verde: Niveles seguros
                else if (percentage <= 55)
                    return new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 200, 0));    // Amarillo: Precaución leve  
                else if (percentage <= 70)
                    return new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 140, 0));    // Naranja: Nivel alto
                else
                    return new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 0, 0));      // Rojo: Peligro de clipping
            }
            
            // Color por defecto (verde) si el valor no es válido
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 200, 0));
        }

        /// <summary>
        /// No implementado.
        /// </summary>
        /// <param name="value">Valor del objetivo de enlace.</param>
        /// <param name="targetType">El tipo al que convertir.</param>
        /// <param name="parameter">Parámetro opcional del convertidor.</param>
        /// <param name="culture">La cultura a usar en la conversión.</param>
        /// <returns>Lanza <see cref="NotImplementedException"/> siempre.</returns>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
