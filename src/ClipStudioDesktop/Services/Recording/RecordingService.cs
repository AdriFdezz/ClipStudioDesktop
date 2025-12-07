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

        public RecordingService(ISettingsService settingsService, IStorageService storageService)
        {
            _settingsService = settingsService;
            _storageService = storageService;
        }

        public bool IsRecording { get; private set; }

        public async Task StartRecordingAsync()
        {
            if (IsRecording) return;

            // Initialize Audio Recorder
            _audioRecorder = new AudioRecorder(_settingsService.CurrentSettings);
            _audioRecorder.Start();

            // Initialize Video Recorder
            _videoRecorder = new VideoRecorder(_settingsService.CurrentSettings);
            await _videoRecorder.StartAsync();

            IsRecording = true;
        }

        public Task StopRecordingAsync()
        {
            _audioRecorder?.Stop();
            _videoRecorder?.Stop();
            IsRecording = false;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _audioRecorder?.Dispose();
            _videoRecorder?.Dispose();
        }

        public async Task SaveClipAsync(int durationSeconds, bool isVideo)
        {
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
