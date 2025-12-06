using ClipStudioDesktop.Services.Audio;
using System.Threading.Tasks;
using System.Windows;

namespace ClipStudioDesktop.Services
{
    public class RecordingService : IRecordingService
    {
        private readonly ISettingsService _settingsService;
        private readonly IStorageService _storageService;
        private AudioRecorder? _audioRecorder;

        public RecordingService(ISettingsService settingsService, IStorageService storageService)
        {
            _settingsService = settingsService;
            _storageService = storageService;
        }

        public Task StartRecordingAsync()
        {
            // Initialize Audio Recorder
            _audioRecorder = new AudioRecorder(_settingsService.CurrentSettings);
            _audioRecorder.Start();

            // TODO: Initialize Video Recorder

            return Task.CompletedTask;
        }

        public Task StopRecordingAsync()
        {
            _audioRecorder?.Stop();
            return Task.CompletedTask;
        }

        public Task SaveClipAsync(int durationSeconds, bool isVideo)
        {
            if (isVideo)
            {
                // TODO: Implement video saving
                MessageBox.Show($"Video clip saving not implemented yet ({durationSeconds}s)");
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
                        // TODO: Show proper notification
                        MessageBox.Show($"Audio guardado: {file}");
                    }
                }
            }

            return Task.CompletedTask;
        }
    }
}
