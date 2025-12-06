using ClipStudioDesktop.Services.Audio;
using ClipStudioDesktop.Services.Video;
using System.Threading.Tasks;
using System.Windows;

namespace ClipStudioDesktop.Services
{
    public class RecordingService : IRecordingService
    {
        private readonly ISettingsService _settingsService;
        private readonly IStorageService _storageService;
        private AudioRecorder? _audioRecorder;
        private VideoRecorder? _videoRecorder;

        public RecordingService(ISettingsService settingsService, IStorageService storageService)
        {
            _settingsService = settingsService;
            _storageService = storageService;
        }

        public async Task StartRecordingAsync()
        {
            // Initialize Audio Recorder
            _audioRecorder = new AudioRecorder(_settingsService.CurrentSettings);
            _audioRecorder.Start();

            // Initialize Video Recorder
            _videoRecorder = new VideoRecorder(_settingsService.CurrentSettings);
            await _videoRecorder.StartAsync();
        }

        public Task StopRecordingAsync()
        {
            _audioRecorder?.Stop();
            _videoRecorder?.Stop();
            return Task.CompletedTask;
        }

        public async Task SaveClipAsync(int durationSeconds, bool isVideo)
        {
            if (isVideo)
            {
                if (_videoRecorder != null)
                {
                    string folder = _storageService.GetVideoFolder();
                    _storageService.EnsureDirectoriesExist();

                    string? file = await _videoRecorder.SaveClipAsync(durationSeconds, folder);

                    if (file != null && _settingsService.CurrentSettings.General.ShowNotifications)
                    {
                        MessageBox.Show($"Video guardado: {file}");
                    }
                }
            }
            else
            {
                if (_audioRecorder != null)
                {
                    string folder = _storageService.GetAudioFolder();
                    _storageService.EnsureDirectoriesExist();
                    
                    string? file = _audioRecorder.SaveClip(durationSeconds, folder);
                    
                    if (file != null && _settingsService.CurrentSettings.General.ShowNotifications)
                    {
                        MessageBox.Show($"Audio guardado: {file}");
                    }
                }
            }
        }
    }
}
