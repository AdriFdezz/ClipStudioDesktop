using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ClipStudioDesktop.ViewModels
{
    /// <summary>
    /// Clase base para todos los ViewModels.
    /// Implementa INotifyPropertyChanged para permitir el enlace de datos (Data Binding) en WPF.
    /// </summary>
    public class ViewModelBase : INotifyPropertyChanged
    {
        /// <summary>
        /// Evento que se lanza cuando cambia el valor de una propiedad.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Notifica a la vista que una propiedad ha cambiado.
        /// </summary>
        /// <param name="propertyName">Nombre de la propiedad (opcional, se obtiene automáticamente).</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Establece el valor de una propiedad y notifica el cambio si el valor es diferente.
        /// Método auxiliar para simplificar los setters de propiedades.
        /// </summary>
        /// <typeparam name="T">Tipo de la propiedad.</typeparam>
        /// <param name="field">Referencia al campo de respaldo (backing field).</param>
        /// <param name="value">Nuevo valor a asignar.</param>
        /// <param name="propertyName">Nombre de la propiedad (automático).</param>
        /// <returns>True si el valor cambió, False si era igual.</returns>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
