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
        
        // Tracking current recording files
        private string? _currentVideoFile;
        private string? _currentAudioFile;
        private string? _currentMicFile;
        
        private long _maxSizeBytes = 0;

        public RecordingService(ISettingsService settingsService, IStorageService storageService)
        {
            _settingsService = settingsService;
            _storageService = storageService;
            
            // Initialize recorders
            _videoRecorder = new FFmpegRecorder(_settingsService.CurrentSettings);
            _audioRecorder = new AudioRecorder(_settingsService.CurrentSettings);
            
            // Safety Check Timer (runs every 10s to check size limit)
            _checkTimer = new System.Timers.Timer(10000);
            _checkTimer.Elapsed += CheckRecordingLimit;
        }

        public bool IsRecording { get; private set; }
        public event EventHandler<bool>? RecordingStateChanged;
        public event EventHandler<string>? ClipSaved;
        public event EventHandler<(long Estimated, long Physical)>? BufferSizeChanged; // Legacy name, repurposed for current size updates

        public bool IsVideoMode { get; private set; } = true;

        public async Task ToggleRecordingAsync(bool videoEnabled = true)
        {
            if (IsRecording)
            {
                if (IsVideoMode == videoEnabled)
                {
                    await StopRecordingAsync();
                }
                else
                {
                    // Switch mode: Stop then Start new mode
                    await StopRecordingAsync();
                    await Task.Delay(500); // Give a moment to cleanup
                    await StartRecordingAsync(videoEnabled);
                }
            }
            else
            {
                await StartRecordingAsync(videoEnabled);
            }
        }

        public async Task StartRecordingAsync(bool videoEnabled = true)
        {
            if (IsRecording) return;
            
            try 
            {
                IsVideoMode = videoEnabled;
                _storageService.EnsureDirectoriesExist();
                string tempFolder = Path.Combine(Path.GetTempPath(), "ClipStudio_Rec");
                if (!Directory.Exists(tempFolder)) Directory.CreateDirectory(tempFolder);
                
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                
                // 1. Start Video (Only if videoEnabled)
                if (IsVideoMode)
                {
                    string ext = _settingsService.CurrentSettings.Video.Format.ToLower();
                    if (string.IsNullOrEmpty(ext)) ext = "mp4";
                    
                    _currentVideoFile = Path.Combine(tempFolder, $"rec_video_{timestamp}.{ext}");
                    _videoRecorder?.StartRecording(_currentVideoFile);
                }
                else
                {
                    _currentVideoFile = null;
                }
                
                // 2. Start Desktop Audio
                _currentAudioFile = Path.Combine(tempFolder, $"rec_audio_{timestamp}.raw");
                bool audioStarted = _audioRecorder?.Start(_currentAudioFile) ?? false;
                if (!audioStarted) _currentAudioFile = null;

                // 3. Start Mic (if enabled)
                if (_settingsService.CurrentSettings.Audio.EnableMicrophone)
                {
                    _micRecorder = new MicrophoneRecorder(_settingsService.CurrentSettings);
                     _currentMicFile = Path.Combine(tempFolder, $"rec_mic_{timestamp}.wav");
                    if (!_micRecorder.Start(_currentMicFile))
                    {
                        _currentMicFile = null;
                    }
                }
                else
                {
                    _micRecorder = null;
                    _currentMicFile = null;
                }
                
                // Set limit
                _maxSizeBytes = _settingsService.CurrentSettings.Buffer.MaxBufferBytes;
                if (_maxSizeBytes <= 0) _maxSizeBytes = 10L * 1024 * 1024 * 1024; 

                IsRecording = true;
                RecordingStateChanged?.Invoke(this, IsRecording);
                _checkTimer?.Start();
                
                System.Diagnostics.Debug.WriteLine($"Direct Recording Started (Video: {IsVideoMode})");
            }
            catch (Exception ex)
            {
                 System.Windows.MessageBox.Show($"Error al iniciar grabación: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                 await StopRecordingAsync();
            }
        }

        public async Task StopRecordingAsync()
        {
            if (!IsRecording) return;
            
            try
            {
                _checkTimer?.Stop();
                
                // Stop all recorders
                await (_videoRecorder?.Stop() ?? Task.CompletedTask);
                _audioRecorder?.Stop();
                 _micRecorder?.Stop();
                
                IsRecording = false;
                RecordingStateChanged?.Invoke(this, IsRecording);

                // Finalize and Merge
                await FinalizeAndSaveRecording();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping recording: {ex.Message}");
            }
        }
        
        private async void CheckRecordingLimit(object? sender, System.Timers.ElapsedEventArgs e)
        {
             if (!IsRecording) return;
             
             long physicalSize = 0;
             long displaySize = 0;
             try
             {
                 if (_currentVideoFile != null && File.Exists(_currentVideoFile))
                 {
                     long vSize = new FileInfo(_currentVideoFile).Length;
                     physicalSize += vSize;
                     displaySize += vSize;
                 }
                 
                 if (_currentAudioFile != null && File.Exists(_currentAudioFile))
                 {
                     long aSize = new FileInfo(_currentAudioFile).Length;
                     physicalSize += aSize;
                     displaySize += (aSize / 10); // Estimate compression (Raw -> MP3/AAC is approx 10:1)
                 }
                 
                 if (_currentMicFile != null && File.Exists(_currentMicFile))
                 {
                     long mSize = new FileInfo(_currentMicFile).Length;
                     physicalSize += mSize;
                     displaySize += (mSize / 10);
                 }
                 
                 // Update UI with ESTIMATED final size AND Physical size
                 BufferSizeChanged?.Invoke(this, (displaySize, physicalSize));
                 
                 // Safety Check with PHYSICAL size (Disk usage)
                 if (physicalSize > _maxSizeBytes)
                 {
                     System.Diagnostics.Debug.WriteLine("Safety limit reached. Stopping recording.");
                     await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () => await StopRecordingAsync());
                 }
             }
             catch { }
        }

        private async Task FinalizeAndSaveRecording()
        {
            // Verify we have something to save
            bool hasVideo = _currentVideoFile != null && File.Exists(_currentVideoFile);
            bool hasAudioRec = _currentAudioFile != null && File.Exists(_currentAudioFile); // Not using file exists check strictly here as audio recorder might not flush yet? No, Stop() called.
            
            if (IsVideoMode && !hasVideo)
            {
                 System.Diagnostics.Debug.WriteLine("No video file recorded in video mode.");
                 return;
            }

            try
            {
                 string timestamp = DateTime.Now.ToString("dd_MM_yyyy_HH_mm_ss");
                 string outputFile;
                 
                 // Process Audio
                 string? finalAudio = null;
                 if (_currentAudioFile != null && _audioRecorder != null)
                 {
                     // Convert raw to wav/mp3 for merging
                     finalAudio = _audioRecorder.FinalizeRecording(Path.GetDirectoryName(_currentAudioFile)!, "wav");
                 }
                 
                 // Mic
                 string? finalMic = _currentMicFile;
                 if (finalMic != null && !File.Exists(finalMic)) finalMic = null;

                 bool hasAudio = finalAudio != null && File.Exists(finalAudio);
                 bool hasMic = finalMic != null && File.Exists(finalMic);

                 if (IsVideoMode)
                 {
                     string finalFolder = _storageService.GetVideoFolder();
                     string ext = _settingsService.CurrentSettings.Video.Format.ToLower();
                     if (string.IsNullOrEmpty(ext)) ext = "mp4";
                     
                     outputFile = Path.Combine(finalFolder, $"Grabacion_de_Video_{timestamp}.{ext}");
                     
                     if (hasAudio || hasMic)
                     {
                         await MergeFiles(outputFile, _currentVideoFile!, finalAudio, finalMic);
                     }
                     else
                     {
                         // Video only
                         if (File.Exists(outputFile)) File.Delete(outputFile);
                         File.Move(_currentVideoFile!, outputFile);
                     }
                 }
                 else
                 {
                     // Audio Only
                     string finalFolder = _storageService.GetAudioFolder();
                     string ext = _settingsService.CurrentSettings.Audio.Format.ToLower(); 
                     if (string.IsNullOrEmpty(ext)) ext = "mp3";
                     
                     outputFile = Path.Combine(finalFolder, $"Grabacion_de_Audio_{timestamp}.{ext}");
                     
                     if (hasAudio && hasMic)
                     {
                         // Merge audio sources
                         await MergeAudioOnly(outputFile, finalAudio!, finalMic!);
                     }
                     else if (hasAudio)
                     {
                         await ConvertAudio(outputFile, finalAudio!);
                     }
                     else if (hasMic)
                     {
                         await ConvertAudio(outputFile, finalMic!);
                     }
                     else
                     {
                         System.Diagnostics.Debug.WriteLine("No audio content to save.");
                         return;
                     }
                 }
                 
                 // Cleanup
                 if (_currentVideoFile != null && File.Exists(_currentVideoFile)) File.Delete(_currentVideoFile);
                 if (finalAudio != null && File.Exists(finalAudio)) File.Delete(finalAudio);
                 if (finalMic != null && File.Exists(finalMic)) File.Delete(finalMic);

                 ClipSaved?.Invoke(this, outputFile);
                 
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error al guardar grabación: {ex.Message}");
            }
        }
        
        private async Task MergeFiles(string output, string video, string? audio, string? mic)
        {
            string ffmpeg = ClipStudioDesktop.Helpers.FFmpegHelper.GetFFmpegPath();
            if (string.IsNullOrEmpty(ffmpeg)) return;
            
            string args;
             if (audio != null && mic != null)
            {
                // Mix
                 args = $"-i \"{video}\" -i \"{audio}\" -i \"{mic}\" " +
                           $"-filter_complex \"[1:a][2:a]amix=inputs=2:duration=longest[a]\" " +
                           $"-map 0:v -map \"[a]\" " +
                           $"-c:v copy -c:a aac -b:a 192k " +
                           $"-shortest \"{output}\"";
            }
            else if (audio != null)
            {
                 args = $"-i \"{video}\" -i \"{audio}\" " +
                           $"-map 0:v -map 1:a " +
                           $"-c:v copy -c:a aac -b:a 192k " +
                           $"-shortest \"{output}\"";
            }
             else if (mic != null)
            {
                 args = $"-i \"{video}\" -i \"{mic}\" " +
                           $"-map 0:v -map 1:a " +
                           $"-c:v copy -c:a aac -b:a 192k " +
                           $"-shortest \"{output}\"";
            }
            else 
            {
                return;
            }
            
            await RunFFmpeg(ffmpeg, args);
        }

        private async Task MergeAudioOnly(string output, string audio1, string audio2)
        {
            string ffmpeg = ClipStudioDesktop.Helpers.FFmpegHelper.GetFFmpegPath();
            if (string.IsNullOrEmpty(ffmpeg)) return;

            string codecArgs = GetAudioCodecArgs(output);
            string args = $"-i \"{audio1}\" -i \"{audio2}\" " +
                          $"-filter_complex \"amix=inputs=2:duration=longest\" " +
                          $"{codecArgs} \"{output}\"";

            await RunFFmpeg(ffmpeg, args);
        }

        private async Task ConvertAudio(string output, string input)
        {
             string ffmpeg = ClipStudioDesktop.Helpers.FFmpegHelper.GetFFmpegPath();
            if (string.IsNullOrEmpty(ffmpeg)) return;

            string codecArgs = GetAudioCodecArgs(output);
            string args = $"-i \"{input}\" {codecArgs} \"{output}\"";
            await RunFFmpeg(ffmpeg, args);
        }

        private string GetAudioCodecArgs(string outputFile)
        {
            string ext = Path.GetExtension(outputFile).ToLower();
            if (ext == ".flac") return "-c:a flac";
            if (ext == ".wav") return "-c:a pcm_s16le";
            return "-c:a libmp3lame -q:a 2"; // default to mp3
        }

        private async Task RunFFmpeg(string exe, string args)
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"-y {args}",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            
            if (p != null) await p.WaitForExitAsync();
        }

        public void ClearBuffer() { } // No-op now
        public void UpdateBufferReservation() { } // No-op now
        
        // Legacy support if interface demands it
        public Task SaveClipAsync(int durationSeconds, bool isVideo) 
        {
             // This was for "Instant Replay". 
             // With direct recording, this might not be relevant unless we keep "Replay" feature.
             // The user asked to "Remove buffer". so SaveClip (Instant Replay) is effectively gone?
             // "Simplifying Recording Logic... removal of buffer"
             // I will leave this as a stub or show a message that Replay is disabled.
             System.Windows.MessageBox.Show("La grabación en buffer está desactivada en este modo simplificado.");
             return Task.CompletedTask;
        }

        public void Dispose()
        {
            _checkTimer?.Stop();
             _videoRecorder?.Dispose();
            _audioRecorder?.Dispose();
             _micRecorder?.Dispose();
        }

        private void PlayNotificationSound(bool start)
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
                    catch { }
                });
            }
        }
    }
}
