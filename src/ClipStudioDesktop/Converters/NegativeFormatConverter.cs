using System;
using System.Globalization;
using System.Windows.Data;

namespace ClipStudioDesktop.Converters
{
    /// <summary>
    /// Formatea un número agregando siempre un signo negativo y la unidad "dB".
    /// <para>Se usa para mostrar el valor de la Puerta de Ruido (ej. "30" -> "-30 dB").</para>
    /// </summary>
    public class NegativeFormatConverter : IValueConverter
    {
        /// <summary>
        /// Convierte un valor numérico en un string con formato negativo y sufijo "dB".
        /// </summary>
        /// <param name="value">El valor numérico (generalmente positivo desde un Slider).</param>
        /// <param name="targetType">El tipo al que convertir (ignorado).</param>
        /// <param name="parameter">Parámetro opcional (ignorado).</param>
        /// <param name="culture">La cultura a usar en la conversión (ignorado).</param>
        /// <returns>String formateado (ej. "-25 dB").</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
            {
                // Siempre muestra el número como negativo, incluso si es 0 (para indicar atenuación/umbral)
                // "F0" asegura que no haya decimales
                return $"-{Math.Abs(d):F0} dB";
            }
            return "-0 dB";
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
