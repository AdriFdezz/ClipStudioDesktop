using ClipStudioDesktop.Models;
using System.IO;

namespace ClipStudioDesktop.Services
{
    public class StorageService : IStorageService
    {
        private readonly ISettingsService _settingsService;

        public StorageService(ISettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public string GetAudioFolder() => _settingsService.CurrentSettings.Paths.AudioClips;
        public string GetVideoFolder() => _settingsService.CurrentSettings.Paths.VideoClips;
        public string GetImageFolder() => _settingsService.CurrentSettings.Paths.Screenshots;

        public void EnsureDirectoriesExist()
        {
            var paths = _settingsService.CurrentSettings.Paths;
            Directory.CreateDirectory(paths.AudioClips);
            Directory.CreateDirectory(paths.VideoClips);
            Directory.CreateDirectory(paths.Screenshots);
            Directory.CreateDirectory(paths.TempBuffer);
        }
    }
}
