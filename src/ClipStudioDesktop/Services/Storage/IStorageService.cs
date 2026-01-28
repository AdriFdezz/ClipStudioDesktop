namespace ClipStudioDesktop.Services.Storage
{
    /// <summary>
    /// Interfaz que define las operaciones para la gestión de directorios y rutas de almacenamiento.
    /// Abstrae la lógica de ubicación de archivos para audio, video e imágenes.
    /// </summary>
    public interface IStorageService
    {
        /// <summary>
        /// Obtiene la ruta absoluta del directorio configurado para guardar clips de audio.
        /// </summary>
        string GetAudioFolder();

        /// <summary>
        /// Obtiene la ruta absoluta del directorio configurado para guardar grabaciones de video.
        /// </summary>
        string GetVideoFolder();

        /// <summary>
        /// Obtiene la ruta absoluta del directorio configurado para guardar capturas de pantalla.
        /// </summary>
        string GetImageFolder();

        /// <summary>
        /// Verifica que todos los directorios de salida (audio, video, imágenes, caché) existan,
        /// creándolos si es necesario.
        /// </summary>
        void EnsureDirectoriesExist();
    }
}
