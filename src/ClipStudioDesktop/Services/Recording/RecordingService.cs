using ClipStudioDesktop.Services.Audio;
using ClipStudioDesktop.Services.Video;
using ClipStudioDesktop.Services.Settings;
using ClipStudioDesktop.Services.Storage;
using NAudio.Wave;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace ClipStudioDesktop.Services.Recording
{
    public class RecordingService : IRecordingService, IDisposable
    {
        private readonly ISettingsService _settingsService;
        private readonly IStorageService _storageService;
        private FFmpegRecorder? _videoRecorder;
        private System.Timers.Timer? _checkTimer;

        public RecordingService(ISettingsService settingsService, IStorageService storageService)
        {
            _settingsService = settingsService;
            _storageService = storageService;
            
            // Clean buffer from previous sessions on startup
            CleanBufferFolder();
            
            // Reservar espacio en disco para el buffer
            string bufferPath = _settingsService.CurrentSettings.Paths.TempBuffer;
            long bytesToReserve = _settingsService.CurrentSettings.Buffer.MaxBufferBytes;
            Storage.DiskSpaceReservation.ReserveSpace(bufferPath, bytesToReserve);
        }
        
        private void CleanBufferFolder()
        {
            try
            {
                string bufferPath = _settingsService.CurrentSettings.Paths.TempBuffer;
                if (Directory.Exists(bufferPath))
                {
                    // Clean audio buffer
                    string audioBuffer = Path.Combine(bufferPath, "audio");
                    if (Directory.Exists(audioBuffer))
                    {
                        foreach (var file in Directory.GetFiles(audioBuffer, "*.*", SearchOption.TopDirectoryOnly))
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }
                    
                    // Clean video buffer
                    string videoBuffer = Path.Combine(bufferPath, "video");
                    if (Directory.Exists(videoBuffer))
                    {
                        foreach (var file in Directory.GetFiles(videoBuffer, "*.*", SearchOption.TopDirectoryOnly))
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }
                }
                
                System.Diagnostics.Debug.WriteLine("Buffer cleaned on startup");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error cleaning buffer: {ex.Message}");
            }
        }

        public bool IsRecording { get; private set; }
        public event EventHandler<bool>? RecordingStateChanged;

        public async Task StartRecordingAsync()
        {
            if (IsRecording) return;

            // Initialize and start FFmpeg recorder (captures video + audio together)
            _videoRecorder = new FFmpegRecorder(_settingsService.CurrentSettings);
            bool started = await _videoRecorder.StartAsync();

            if (!started)
            {
                System.Windows.MessageBox.Show("No se pudo iniciar la grabación con FFmpeg.", "Error de Grabación", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            System.Diagnostics.Debug.WriteLine("FFmpeg recording started - audio and video synchronized");
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

        private Task CheckRecordingHealth()
        {
            if (!IsRecording) return Task.CompletedTask;

            // FFmpegRecorder handles its own segmentation, no health check needed
            /*
            if (_videoRecorder != null && !_videoRecorder.IsRunning)
            {
                System.Diagnostics.Debug.WriteLine("Video recorder stopped unexpectedly. Restarting...");
                try { _videoRecorder.Dispose(); } catch { }
                
                _videoRecorder = new FFmpegRecorder(_settingsService.CurrentSettings);
                await _videoRecorder.StartAsync();
            }
            */
            return Task.CompletedTask;
        }

        public Task StopRecordingAsync()
        {
            _checkTimer?.Stop();
            _videoRecorder?.Stop();
            IsRecording = false;
            RecordingStateChanged?.Invoke(this, IsRecording);
            return Task.CompletedTask;
        }

        public void ClearBuffer()
        {
            if (IsRecording)
            {
                throw new InvalidOperationException("No se puede limpiar el buffer mientras la grabación está activa.");
            }
            
            CleanBufferFolder();
        }

        public void UpdateBufferReservation()
        {
            try
            {
                string bufferPath = _settingsService.CurrentSettings.Paths.TempBuffer;
                long bytesToReserve = _settingsService.CurrentSettings.Buffer.MaxBufferBytes;
                Storage.DiskSpaceReservation.UpdateReservation(bufferPath, bytesToReserve);
                System.Diagnostics.Debug.WriteLine($"RecordingService: Reserva de buffer actualizada a {bytesToReserve / 1024 / 1024 / 1024}GB");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RecordingService: Error al actualizar reserva de buffer: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try
            {
                _videoRecorder?.Dispose();
                
                // Limpiar buffer al cerrar la aplicación
                CleanBufferFolder();
                
                // Actualizar el espacio reservado al tamaño configurado (no borrar el archivo)
                // Esto mantiene el .space_reservation pero ajusta su tamaño al valor inicial
                string bufferPath = _settingsService.CurrentSettings.Paths.TempBuffer;
                long bytesToReserve = _settingsService.CurrentSettings.Buffer.MaxBufferBytes;
                Storage.DiskSpaceReservation.ReserveSpace(bufferPath, bytesToReserve);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en Dispose: {ex.Message}");
            }
        }

        public async Task SaveClipAsync(int durationSeconds, bool isVideo)
        {
            if (durationSeconds <= 0) durationSeconds = 30; // Default to 30s if invalid

            if (_videoRecorder != null)
            {
                string folder = _storageService.GetVideoFolder();
                _storageService.EnsureDirectoriesExist();

                // FFmpegRecorder already has audio integrated, no need to merge
                string? file = await _videoRecorder.SaveClipAsync(durationSeconds, folder, null);

                if (file != null)
                {
                    PlayNotificationSound();
                }
                else
                {
                    System.Windows.MessageBox.Show("No se pudo guardar el clip. Es posible que la grabación no esté activa o no haya suficiente buffer.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                System.Windows.MessageBox.Show("El servicio de grabación no está inicializado.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PlayNotificationSound()
        {
            if (_settingsService.CurrentSettings.General.PlaySoundOnClip)
            {
                Task.Run(() =>
                {
                    try
                    {
                        var soundPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "Notification_sound.wav");
                        if (System.IO.File.Exists(soundPath))
                        {
                            using (var audioFile = new AudioFileReader(soundPath))
                            using (var outputDevice = new WaveOutEvent())
                            {
                                outputDevice.Init(audioFile);
                                outputDevice.Play();
                                while (outputDevice.PlaybackState == PlaybackState.Playing)
                                {
                                    System.Threading.Thread.Sleep(100);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error playing sound: {ex.Message}");
                    }
                });
            }
        }
    }
}
