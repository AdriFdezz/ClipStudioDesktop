using ClipStudioDesktop.Services.Audio;
using ClipStudioDesktop.Services.Video;
using ClipStudioDesktop.Services.Settings;
using ClipStudioDesktop.Services.Storage;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace ClipStudioDesktop.Services.Recording
{
    public class RecordingService : IRecordingService, IDisposable
    {
        private readonly ISettingsService _settingsService;
        private readonly IStorageService _storageService;
        private AudioRecorder? _audioRecorder;
        private VideoRecorder? _videoRecorder;
        private System.Timers.Timer? _checkTimer;

        public RecordingService(ISettingsService settingsService, IStorageService storageService)
        {
            _settingsService = settingsService;
            _storageService = storageService;
        }

        public bool IsRecording { get; private set; }
        public event EventHandler<bool>? RecordingStateChanged;

        public async Task StartRecordingAsync()
        {
            if (IsRecording) return;

            // Initialize Audio Recorder
            _audioRecorder = new AudioRecorder(_settingsService.CurrentSettings);
            bool audioStarted = _audioRecorder.Start();
            if (!audioStarted)
            {
                System.Diagnostics.Debug.WriteLine("Audio recorder failed to start (possibly no audio device). Continuing with video only.");
            }

            // Initialize Video Recorder
            _videoRecorder = new VideoRecorder(_settingsService.CurrentSettings);
            bool videoStarted = await _videoRecorder.StartAsync();

            if (!videoStarted)
            {
                System.Windows.MessageBox.Show("No se pudo iniciar la grabación de video. Verifique que FFmpeg esté instalado correctamente y que no haya otros programas usando la cámara/pantalla.", "Error de Grabación", MessageBoxButton.OK, MessageBoxImage.Error);
                // Clean up
                _audioRecorder.Stop();
                return;
            }

            IsRecording = true;
            RecordingStateChanged?.Invoke(this, IsRecording);
            StartHealthCheck();
        }

        private void StartHealthCheck()
        {
            if (_checkTimer == null)
            {
                _checkTimer = new System.Timers.Timer(5000); // Check every 5 seconds
                _checkTimer.Elapsed += async (s, e) => await CheckRecordingHealth();
            }
            _checkTimer.Start();
        }

        private async Task CheckRecordingHealth()
        {
            if (!IsRecording) return;

            if (_videoRecorder != null && !_videoRecorder.IsRunning)
            {
                System.Diagnostics.Debug.WriteLine("Video recorder stopped unexpectedly. Restarting...");
                try { _videoRecorder.Dispose(); } catch { }
                
                _videoRecorder = new VideoRecorder(_settingsService.CurrentSettings);
                await _videoRecorder.StartAsync();
            }
        }

        public Task StopRecordingAsync()
        {
            _checkTimer?.Stop();
            _audioRecorder?.Stop();
            _videoRecorder?.Stop();
            IsRecording = false;
            RecordingStateChanged?.Invoke(this, IsRecording);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _audioRecorder?.Dispose();
            _videoRecorder?.Dispose();
        }

        public async Task SaveClipAsync(int durationSeconds, bool isVideo)
        {
            if (durationSeconds <= 0) durationSeconds = 30; // Default to 30s if invalid

            if (isVideo)
            {
                if (_videoRecorder != null)
                {
                    string folder = _storageService.GetVideoFolder();
                    _storageService.EnsureDirectoriesExist();

                    string? tempAudioPath = null;
                    
                    // Try to get audio to merge
                    if (_audioRecorder != null)
                    {
                        // Save to temp folder
                        string tempFolder = _settingsService.CurrentSettings.Paths.TempBuffer;
                        tempAudioPath = _audioRecorder.SaveClip(durationSeconds, tempFolder);
                    }

                    string? file = await _videoRecorder.SaveClipAsync(durationSeconds, folder, tempAudioPath);

                    // Cleanup temp audio
                    if (tempAudioPath != null)
                    {
                        try { System.IO.File.Delete(tempAudioPath); } catch { }
                    }

                    if (file != null && _settingsService.CurrentSettings.General.ShowNotifications)
                    {
                        System.Windows.MessageBox.Show($"Video guardado: {file}");
                    }
                    else if (file == null)
                    {
                        System.Windows.MessageBox.Show("No se pudo guardar el clip de video. Es posible que la grabación no esté activa o no haya suficiente buffer.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    System.Windows.MessageBox.Show("El servicio de grabación de video no está inicializado.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                        System.Windows.MessageBox.Show($"Audio guardado: {file}");
                    }
                    else if (file == null)
                    {
                        System.Windows.MessageBox.Show("No se pudo guardar el clip de audio.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    System.Windows.MessageBox.Show("El servicio de grabación de audio no está inicializado.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
