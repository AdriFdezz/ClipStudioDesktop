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
        private AudioRecorder? _audioRecorder;
        private MicrophoneRecorder? _micRecorder;
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
        public event EventHandler<string>? ClipSaved;

        public async Task StartRecordingAsync()
        {
            if (IsRecording) return;

            // Initialize and start FFmpeg recorder (video only)
            _videoRecorder = new FFmpegRecorder(_settingsService.CurrentSettings);
            bool videoStarted = await _videoRecorder.StartAsync();

            if (!videoStarted)
            {
                System.Windows.MessageBox.Show("No se pudo iniciar la grabación de video con FFmpeg.", "Error de Grabación", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Initialize and start Audio recorder (NAudio WASAPI loopback for desktop audio)
            // This will automatically capture all system audio without configuration
            _audioRecorder = new AudioRecorder(_settingsService.CurrentSettings);
            bool audioStarted = _audioRecorder.Start();
            
            if (!audioStarted)
            {
                // Audio failed but video is running - continue with video only
                System.Diagnostics.Debug.WriteLine("WARNING: Audio recording failed to start (no audio device active?). Continuing with video only.");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Audio recording started - capturing desktop audio via WASAPI loopback");
            }

            // Initialize and start Microphone recorder if enabled
            if (_settingsService.CurrentSettings.Audio.EnableMicrophone)
            {
                _micRecorder = new MicrophoneRecorder(_settingsService.CurrentSettings);
                bool micStarted = _micRecorder.Start();
                if (micStarted)
                {
                     System.Diagnostics.Debug.WriteLine($"Microphone recording started: {_settingsService.CurrentSettings.Audio.SelectedMicrophone}");
                }
                else
                {
                     System.Diagnostics.Debug.WriteLine("Microphone recording failed to start or disabled");
                     _micRecorder = null;
                }
            }

            System.Diagnostics.Debug.WriteLine("Recording started - video and audio capturing separately");
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
            _audioRecorder?.Stop();
            _micRecorder?.Stop();
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
                _audioRecorder?.Dispose();
                _micRecorder?.Dispose();
                
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

            if (_videoRecorder == null)
            {
                System.Windows.MessageBox.Show("El servicio de grabación no está inicializado.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _storageService.EnsureDirectoriesExist();
            string? outputFile = null;

            try
            {
                if (isVideo)
                {
                    // Save video clip with audio from NAudio
                    string videoFolder = _storageService.GetVideoFolder();
                    string? audioTempFile = null;
                    
                    // First, save audio to a temp file if AudioRecorder is active
                    if (_audioRecorder != null)
                    {
                        try
                        {
                            string tempFolder = Path.Combine(Path.GetTempPath(), "ClipStudio_AudioMerge");
                            Directory.CreateDirectory(tempFolder);
                            audioTempFile = _audioRecorder.SaveClip(durationSeconds, tempFolder);
                            System.Diagnostics.Debug.WriteLine($"Audio saved to temp: {audioTempFile}");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Could not save audio (continuing without): {ex.Message}");
                            audioTempFile = null;
                        }
                    }
                    
                    // Save microphone audio to a temp file if MicrophoneRecorder is active
                    string? micTempFile = null;
                    if (_micRecorder != null)
                    {
                        try
                        {
                            string tempFolder = Path.Combine(Path.GetTempPath(), "ClipStudio_AudioMerge");
                            Directory.CreateDirectory(tempFolder);
                            micTempFile = _micRecorder.SaveClip(durationSeconds, tempFolder);
                            System.Diagnostics.Debug.WriteLine($"Mic audio saved to temp: {micTempFile}");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Could not save mic audio (continuing without): {ex.Message}");
                            micTempFile = null;
                        }
                    }
                    
                    // Save video clip - pass audio files to merge
                    outputFile = await _videoRecorder.SaveClipAsync(durationSeconds, videoFolder, audioTempFile, micTempFile);
                    
                    // Clean up temp audio files
                    if (micTempFile != null)
                    {
                        try { File.Delete(micTempFile); } catch { }
                    }
                    
                    // Clean up temp audio file
                    if (audioTempFile != null)
                    {
                        try { File.Delete(audioTempFile); } catch { }
                    }
                }
                else
                {
                    // Save audio-only clip using AudioRecorder directly
                    if (_audioRecorder != null)
                    {
                        string audioFolder = _storageService.GetAudioFolder();
                        outputFile = _audioRecorder.SaveClip(durationSeconds, audioFolder);
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("El grabador de audio no está disponible.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                if (outputFile != null)
                {
                    PlayNotificationSound();
                    ClipSaved?.Invoke(this, outputFile);
                }
                else
                {
                    string clipType = isVideo ? "video" : "audio";
                    System.Windows.MessageBox.Show($"No se pudo guardar el clip de {clipType}. Es posible que la grabación no esté activa o no haya suficiente buffer.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving clip: {ex.Message}");
                string clipType = isVideo ? "video" : "audio";
                System.Windows.MessageBox.Show($"Error al guardar el clip de {clipType}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
