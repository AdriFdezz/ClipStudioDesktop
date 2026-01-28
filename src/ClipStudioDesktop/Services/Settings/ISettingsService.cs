using ClipStudioDesktop.Models;

namespace ClipStudioDesktop.Services.Settings
{
    /// <summary>
    /// Interfaz para el servicio de gestión de configuración de la aplicación.
    /// Permite cargar, guardar y restablecer las preferencias del usuario.
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>
        /// Obtiene la configuración actual de la aplicación.
        /// </summary>
        AppSettings CurrentSettings { get; }

        /// <summary>
        /// Carga la configuración desde el almacenamiento persistente (ej. archivo JSON).
        /// Si falla o no existe, carga valores por defecto.
        /// </summary>
        void LoadSettings();

        /// <summary>
        /// Guarda la configuración actual en el almacenamiento persistente.
        /// </summary>
        void SaveSettings();

        /// <summary>
        /// Restablece todas las configuraciones a sus valores predeterminados y guarda los cambios.
        /// </summary>
        void ResetToDefaults();
    }
}
