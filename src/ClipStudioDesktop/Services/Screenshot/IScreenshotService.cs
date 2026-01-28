using System.Threading.Tasks;

namespace ClipStudioDesktop.Services.Screenshot
{
    /// <summary>
    /// Interfaz que define las operaciones del servicio de capturas de pantalla.
    /// Permite capturar la pantalla completa, regiones seleccionadas y guardar en archivo o portapapeles.
    /// </summary>
    public interface IScreenshotService
    {
        /// <summary>
        /// Realiza una captura de toda la pantalla (o monitor configurado) y la guarda automáticamente.
        /// </summary>
        Task CaptureFullScreenAsync();

        /// <summary>
        /// Inicia el proceso interactivo de selección de una región y guarda la captura en un archivo.
        /// </summary>
        /// <returns>True si la captura fue exitosa, False si el usuario canceló.</returns>
        Task<bool> CaptureSelectionAsync();

        /// <summary>
        /// Inicia el proceso interactivo de selección de una región y copia la imagen al portapapeles.
        /// </summary>
        /// <returns>True si la captura fue exitosa, False si el usuario canceló.</returns>
        Task<bool> CaptureSelectionToClipboardAsync();

        /// <summary>
        /// Evento que se dispara cuando una captura de pantalla se ha guardado exitosamente en disco.
        /// Provee la ruta del archivo generado.
        /// </summary>
        event System.EventHandler<string>? ScreenshotSaved;
    }
}
