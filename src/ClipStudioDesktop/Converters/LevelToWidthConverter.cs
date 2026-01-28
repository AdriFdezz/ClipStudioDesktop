using System;
using System.Globalization;
using System.Windows.Data;

namespace ClipStudioDesktop.Converters
{
    /// <summary>
    /// Convierte el nivel actual del audio (0.0 a 1.0) en un valor de ancho en píxeles.
    /// <para>Se utiliza para animar la barra de progreso "VU Meter" del micrófono.</para>
    /// </summary>
    public class LevelToWidthConverter : IValueConverter
    {
        // Ancho máximo visual de la barra en la interfaz (debe coincidir o ser menor al ancho del contenedor)
        private const double MaxWidth = 550;

        /// <summary>
        /// Calcula el ancho de la barra basado en el nivel de audio.
        /// </summary>
        /// <param name="value">Nivel de audio normalizado (0.0 a 1.0).</param>
        /// <param name="targetType">El tipo al que convertir (ignorado).</param>
        /// <param name="parameter">Parámetro opcional (ignorado).</param>
        /// <param name="culture">La cultura a usar en la conversión (ignorado).</param>
        /// <returns>Ancho en píxeles (double) proporcional al nivel.</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double level)
            {
                // Limitamos el nivel entre 0 y 1 para evitar desbordamientos visuales
                double clampedLevel = Math.Max(0, Math.Min(1, level));
                
                // Calculamos el ancho: (Nivel * AnchoMáximo)
                return clampedLevel * MaxWidth;
            }
            return 0.0;
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
