using System;
using System.Globalization;
using System.Windows.Data;

namespace ClipStudioDesktop.Converters
{
    /// <summary>
    /// Invierte el signo de un valor numérico (double).
    /// <para>Útil para controles UI que manejan valores positivos (0 a 30) pero representan lógicamente valores negativos (0 a -30 dB) como la Puerta de Ruido.</para>
    /// </summary>
    public class NegateValueConverter : IValueConverter
    {
        /// <summary>
        /// Convierte un valor negativo (modelo) a positivo (vista), o viceversa.
        /// </summary>
        /// <param name="value">Valor numérico (double).</param>
        /// <param name="targetType">El tipo al que convertir (ignorado).</param>
        /// <param name="parameter">Parámetro opcional (ignorado).</param>
        /// <param name="culture">La cultura a usar en la conversión (ignorado).</param>
        /// <returns>El valor multiplicado por -1.</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
            {
                // Ejemplo: Si el modelo tiene -30dB, devuelve 30 para el Slider.
                return -d; 
            }
            return 0;
        }

        /// <summary>
        /// Convierte el valor de la vista al modelo invirtiendo el signo nuevamente.
        /// </summary>
        /// <param name="value">Valor numérico (double) desde la UI.</param>
        /// <param name="targetType">El tipo al que convertir (ignorado).</param>
        /// <param name="parameter">Parámetro opcional (ignorado).</param>
        /// <param name="culture">La cultura a usar en la conversión (ignorado).</param>
        /// <returns>El valor multiplicado por -1.</returns>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
            {
                // Ejemplo: Si el usuario selecciona 30 en el Slider, guarda -30dB en la configuración.
                return -d; 
            }
            return 0;
        }
    }
}
