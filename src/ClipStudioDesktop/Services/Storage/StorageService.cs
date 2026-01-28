using ClipStudioDesktop.Models;
using ClipStudioDesktop.Services.Settings;
using System.IO;

namespace ClipStudioDesktop.Services.Storage
{
    /// <summary>
    /// Implementación del servicio de almacenamiento.
    /// Recupera las rutas de los directorios desde la configuración global (<see cref="ISettingsService"/>).
    /// </summary>
    public class StorageService : IStorageService
    {
        private readonly ISettingsService _settingsService;

        public StorageService(ISettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        /// <inheritdoc />
        public string GetAudioFolder() => _settingsService.CurrentSettings.Paths.AudioClips;

        /// <inheritdoc />
        public string GetVideoFolder() => _settingsService.CurrentSettings.Paths.VideoClips;

        /// <inheritdoc />
        public string GetImageFolder() => _settingsService.CurrentSettings.Paths.Screenshots;

        /// <summary>
        /// Crea los directorios de Audio, Video, Capturas y Caché si no existen en el sistema de archivos.
        /// Utiliza las rutas definidas en <see cref="AppSettings.PathConfig"/>.
        /// </summary>
        public void EnsureDirectoriesExist()
        {
            var paths = _settingsService.CurrentSettings.Paths;
            Directory.CreateDirectory(paths.AudioClips);
            Directory.CreateDirectory(paths.VideoClips);
            Directory.CreateDirectory(paths.Screenshots);
            Directory.CreateDirectory(paths.Cache);
        }
    }
}
