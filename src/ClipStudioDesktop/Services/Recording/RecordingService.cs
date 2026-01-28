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
        private SharpAviRecorder? _nativeRecorder;
        private AudioRecorder? _audioRecorder; // Kept for Audio Only Mode
        private MicrophoneRecorder? _micRecorder;
        private System.Timers.Timer? _checkTimer;
        
        // Tracking current recording files
        private string? _currentVideoFile; // AVI or MP4
        private string? _currentAudioFile;
        private string? _currentMicFile;
        
        private long _maxSizeBytes = 0;

        public RecordingService(ISettingsService settingsService, IStorageService storageService)
        {
            _settingsService = settingsService;
            _storageService = storageService;
            
            // Initialize recorders
            _nativeRecorder = new SharpAviRecorder(); // Native
            _audioRecorder = new AudioRecorder(_settingsService.CurrentSettings);
            
            // Safety Check Timer (runs every 10s to check size limit)
            _checkTimer = new System.Timers.Timer(10000);
            _checkTimer.Elapsed += CheckRecordingLimit;
        }



        public bool IsRecording { get; private set; }
        public DateTime? CurrentRecordingStartTime { get; private set; }
        public event EventHandler<bool>? RecordingStateChanged;
        public event EventHandler<string>? ClipSaved;
        public event EventHandler<(long Estimated, long Physical)>? BufferSizeChanged; 

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
                    await StopRecordingAsync();
                    await Task.Delay(500); 
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
                
                if (IsVideoMode)
                {
                    _currentAudioFile = null;
                    
                    // NATIVE RECORDING (SHARP AVI)
                    // Records Video + Desktop Audio to single AVI
                    // We record AVI Raw/MJPEG to temp folder first
                    string tempAvi = Path.Combine(tempFolder, $"temp_raw_{timestamp}.avi");
                    _currentVideoFile = tempAvi;
                    
                    // Get FPS from settings
                    int fps = _settingsService.CurrentSettings.Video.Framerate;
                    if (fps <= 0) fps = 30; // Safety default
                    
                    // Calculate Scaled Quality based on Bitrate
                    // Range: Bitrate 4000 -> Quality 50 (Efficient)
                    //        Bitrate 15000 -> Quality 80 (High, but avoids exponential size of 90+)
                    // MJPEG size at Q90 is double Q80. Q80 is visually sufficient for temp.
                    int targetBitrate = _settingsService.CurrentSettings.Video.Bitrate;
                    if (targetBitrate <= 0) targetBitrate = 8000;
                    
                    int quality = 50; // Base (Start lower for space saving)
                    if (targetBitrate > 4000)
                    {
                        // Linear interpolation: +30 quality for +11000 bitrate
                        double ratio = (double)(targetBitrate - 4000) / 11000.0;
                        if (ratio > 1.0) ratio = 1.0;
                        quality += (int)(ratio * 30); // Max 50+30=80
                    }
                    
                    // Note: SharpAviRecorder Start is synchronous but fast
                    _nativeRecorder?.StartRecording(tempAvi, fps, quality, recordAudio: true);
                }
                else
                {
                    // Audio Only Mode - Classic behavior logic preserved or updated?
                    // Task says "AudioRecorder refactoring" was done for direct recording.
                    // We prefer keeping AudioRecorder separate for pure audio to avoid video overhead.
                    _currentVideoFile = null;
                    
                    string ext = _settingsService.CurrentSettings.Audio.Format.ToLower(); 
                    if (string.IsNullOrEmpty(ext)) ext = "wav"; // Raw capture is usually WAV

                    _currentAudioFile = Path.Combine(tempFolder, $"rec_audio_{timestamp}.{ext}");
                    if (_audioRecorder != null)
                    {
                        var success = _audioRecorder.Start(_currentAudioFile);
                        if (!success) throw new Exception("Failed to start audio recorder");
                    }
                }

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
                
                _maxSizeBytes = _settingsService.CurrentSettings.Buffer.MaxBufferBytes;
                // If 0, it means unlimited. logic handled in CheckRecordingLimit.
                // if (_maxSizeBytes <= 0) _maxSizeBytes = 10L * 1024 * 1024 * 1024; // REMOVED arbitrary 10GB default override 

                IsRecording = true;
                CurrentRecordingStartTime = DateTime.Now;
                RecordingStateChanged?.Invoke(this, IsRecording);
                _checkTimer?.Start();
                
                System.Diagnostics.Debug.WriteLine($"Native Recording Started (Video: {IsVideoMode})");
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
                
                // Stop Native Recorder
                if (IsVideoMode)
                {
                    _nativeRecorder?.Stop();
                }
                else
                {
                    _audioRecorder?.Stop();
                }

                _micRecorder?.Stop();
                _micRecorder?.FinalizeRecording();
                
                IsRecording = false;
                CurrentRecordingStartTime = null;
                RecordingStateChanged?.Invoke(this, IsRecording);

                // Finalize (AVI -> MP4)
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
                 if (_maxSizeBytes > 0 && physicalSize > _maxSizeBytes)
                 {
                     System.Diagnostics.Debug.WriteLine($"Safety limit reached ({physicalSize} > {_maxSizeBytes}). Stopping recording.");
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
                 if (finalMic != null && File.Exists(finalMic))
                 {
                     if (new FileInfo(finalMic).Length < 1024) 
                     {
                         // If less than 1KB, assumes empty/invalid
                         finalMic = null; 
                     }
                 }
                 else
                 {
                     finalMic = null;
                 }

                 bool hasAudio = finalAudio != null && File.Exists(finalAudio);
                 bool hasMic = finalMic != null && File.Exists(finalMic);

                // FinalizeAndSaveRecording Logic Update for SharpAvi
                
                 if (IsVideoMode)
                 {
                     string finalFolder = _storageService.GetVideoFolder();
                     string ext = _settingsService.CurrentSettings.Video.Format.ToLower();
                     if (string.IsNullOrEmpty(ext)) ext = "mp4";
                     
                     outputFile = Path.Combine(finalFolder, $"Grabacion_de_Video_{timestamp}.{ext}");
                     
                     // _currentVideoFile is now the temp AVI (Raw + Audio)
                     // Usage: FFmpeg to convert AVI -> MP4 (H264/AAC)
                     
                     // Check if mic exists to merge
                     if (hasMic)
                     {
                         // Merge AVI + Mic -> MP4
                         await MergeMicToVideo(outputFile, _currentVideoFile!, finalMic!);
                     }
                     else
                     {
                         // Transcode AVI -> MP4
                         int bitrate = _settingsService.CurrentSettings.Video.Bitrate;
                         if (bitrate <= 0) bitrate = 8000;
                         
                         await ConvertAviToFinal(outputFile, _currentVideoFile!, bitrate);
                     }
                 }
                 else
                 {
                     // Audio logic remains same
                     string finalFolder = _storageService.GetAudioFolder();
                     string ext = _settingsService.CurrentSettings.Audio.Format.ToLower();
                     if (string.IsNullOrEmpty(ext)) ext = "mp3";
                     
                     outputFile = Path.Combine(finalFolder, $"Grabacion_de_Audio_{timestamp}.{ext}");
                     
                     if (hasAudio && hasMic) await MergeAudioOnly(outputFile, finalAudio!, finalMic!);
                     else if (hasAudio) await ConvertAudio(outputFile, finalAudio!);
                     else if (hasMic) await ConvertAudio(outputFile, finalMic!);
                     else return;
                 }
                 
                 // Cleanup
                 try 
                 {
                     if (_currentVideoFile != null && File.Exists(_currentVideoFile) && _currentVideoFile != outputFile) File.Delete(_currentVideoFile);
                     // Delete audio raw files if they exist (Audio Only mode)
                     if (!IsVideoMode && _currentAudioFile != null && File.Exists(_currentAudioFile)) File.Delete(_currentAudioFile);
                     
                     if (finalAudio != null && File.Exists(finalAudio)) File.Delete(finalAudio);
                     if (finalMic != null && File.Exists(finalMic)) File.Delete(finalMic);
                 }
                 catch (Exception cleanupEx) 
                 { 
                     System.Diagnostics.Debug.WriteLine($"Cleanup error: {cleanupEx.Message}"); 
                 }

                 ClipSaved?.Invoke(this, outputFile);
                 
            }
            catch (OperationCanceledException)
            {
                // User cancelled conversion - cleanup temp files silently
                System.Diagnostics.Debug.WriteLine("[Recording] Conversion cancelled by user");
                try 
                {
                    if (_currentVideoFile != null && File.Exists(_currentVideoFile)) File.Delete(_currentVideoFile);
                    if (_currentAudioFile != null && File.Exists(_currentAudioFile)) File.Delete(_currentAudioFile);
                    if (_currentMicFile != null && File.Exists(_currentMicFile)) File.Delete(_currentMicFile);
                }
                catch { }
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
            if (ext == ".ogg") return "-c:a libvorbis -q:a 6";
            return "-c:a libmp3lame -q:a 2"; // default to mp3
        }

        private async Task ConvertAviToFinal(string output, string inputAvi, int vBitrate)
        {
             string ffmpeg = ClipStudioDesktop.Helpers.FFmpegHelper.GetFFmpegPath();
            if (string.IsNullOrEmpty(ffmpeg)) return;

             int aBitrate = _settingsService.CurrentSettings.Audio.Bitrate;
             if (aBitrate <= 0) aBitrate = 192;
             
             string resolution = _settingsService.CurrentSettings.Video.Resolution;
             string scaleFilter = "";
             if (!string.IsNullOrEmpty(resolution) && resolution.Contains("x") && resolution != "Native")
             {
                 scaleFilter = $"-s {resolution}";
             }

            string ext = Path.GetExtension(output).ToLower();
            string args;
            
            if (ext == ".webm")
            {
                // WebM: VP9 video + Opus audio
                args = $"-i \"{inputAvi}\" -c:v libvpx-vp9 -b:v {vBitrate}k {scaleFilter} " +
                       $"-c:a libopus -b:a {aBitrate}k " +
                       $"\"{output}\"";
            }
            else
            {
                // MP4/MKV: H264 video + AAC audio
                args = $"-i \"{inputAvi}\" -c:v libx264 -preset ultrafast -pix_fmt yuv420p " +
                       $"-b:v {vBitrate}k -maxrate {vBitrate}k -bufsize {vBitrate * 2}k {scaleFilter} " +
                       $"-c:a aac -b:a {aBitrate}k " +
                       $"-movflags +faststart \"{output}\"";
            }
            
            await RunFFmpegWithProgress(ffmpeg, args, inputAvi, output);
        }

        private async Task RunFFmpeg(string exe, string args)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[FFmpeg] Starting: {args}");
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"-y {args}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                });
                
                if (p != null) 
                {
                    string stderr = await p.StandardError.ReadToEndAsync();
                    await p.WaitForExitAsync();
                    
                    if (p.ExitCode != 0)
                    {
                         System.Diagnostics.Debug.WriteLine($"[FFmpeg] ERROR (Exit {p.ExitCode}): {stderr}");
                         throw new Exception($"FFmpeg failed with code {p.ExitCode}. Log: {stderr}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[FFmpeg] Success. Log: {stderr}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FFmpeg] Exception: {ex.Message}");
                throw;
            }
        }

        private async Task RunFFmpegWithProgress(string exe, string args, string inputFile, string outputFile)
        {
            Views.ProcessingWindow? progressWindow = null;
            Process? p = null;
            bool wasCancelled = false;
            
            try
            {
                // Get input duration for progress calculation
                TimeSpan totalDuration = await GetMediaDuration(exe, inputFile);
                
                // Show progress window on UI thread
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    progressWindow = new Views.ProcessingWindow();
                    progressWindow.CancellationRequested += (s, e) =>
                    {
                        wasCancelled = true;
                        try
                        {
                            if (p != null && !p.HasExited)
                            {
                                p.Kill();
                            }
                        }
                        catch { }
                    };
                    progressWindow.Show();
                });

                System.Diagnostics.Debug.WriteLine($"[FFmpeg] Starting with progress: {args}");
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"-y {args}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                p = Process.Start(psi);
                if (p == null) return;

                DateTime startTime = DateTime.Now;
                
                // Read stderr line by line for progress
                var stderrTask = Task.Run(async () =>
                {
                    var reader = p.StandardError;
                    char[] buffer = new char[256];
                    string accumulated = "";
                    
                    while (!p.HasExited || reader.Peek() >= 0)
                    {
                        int read = await reader.ReadAsync(buffer, 0, buffer.Length);
                        if (read > 0)
                        {
                            accumulated += new string(buffer, 0, read);
                            
                            // Parse progress from FFmpeg output
                            // Format: frame=  120 fps=30 time=00:00:04.00 bitrate=8000kbps speed=1.5x
                            var timeMatch = System.Text.RegularExpressions.Regex.Match(accumulated, @"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})");
                            var speedMatch = System.Text.RegularExpressions.Regex.Match(accumulated, @"speed=\s*([\d.]+)x");
                            
                            if (timeMatch.Success && totalDuration.TotalSeconds > 0)
                            {
                                int hours = int.Parse(timeMatch.Groups[1].Value);
                                int mins = int.Parse(timeMatch.Groups[2].Value);
                                int secs = int.Parse(timeMatch.Groups[3].Value);
                                int centis = int.Parse(timeMatch.Groups[4].Value);
                                
                                TimeSpan currentTime = new TimeSpan(0, hours, mins, secs, centis * 10);
                                double percent = (currentTime.TotalSeconds / totalDuration.TotalSeconds) * 100;
                                
                                TimeSpan? remaining = null;
                                if (speedMatch.Success && double.TryParse(speedMatch.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double speed) && speed > 0)
                                {
                                    double remainingSeconds = (totalDuration.TotalSeconds - currentTime.TotalSeconds) / speed;
                                    remaining = TimeSpan.FromSeconds(remainingSeconds);
                                }
                                
                                progressWindow?.UpdateProgress(percent, remaining);
                            }
                            
                            // Keep last 500 chars to avoid memory growth
                            if (accumulated.Length > 500)
                                accumulated = accumulated.Substring(accumulated.Length - 500);
                        }
                        else
                        {
                            await Task.Delay(50);
                        }
                    }
                });

                await p.WaitForExitAsync();
                await stderrTask;

                if (wasCancelled)
                {
                    // Delete partial output file created by FFmpeg
                    try
                    {
                        if (File.Exists(outputFile))
                        {
                            File.Delete(outputFile);
                            System.Diagnostics.Debug.WriteLine($"[FFmpeg] Deleted partial output file: {outputFile}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FFmpeg] Failed to delete output file: {ex.Message}");
                    }
                    
                    // User cancelled - throw to signal cancellation
                    throw new OperationCanceledException("Conversion cancelled by user");
                }

                if (p.ExitCode != 0)
                {
                    throw new Exception($"FFmpeg failed with code {p.ExitCode}");
                }
            }
            finally
            {
                // Close progress window on UI thread
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    progressWindow?.CloseWithoutConfirmation();
                });
            }
        }

        private async Task<TimeSpan> GetMediaDuration(string ffmpegPath, string inputFile)
        {
            try
            {
                // Use ffprobe-like query with ffmpeg
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-i \"{inputFile}\" -hide_banner",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                var p = Process.Start(psi);
                if (p == null) return TimeSpan.Zero;

                string stderr = await p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();

                // Parse duration from: Duration: 00:01:30.50, start: 0.000000
                var match = System.Text.RegularExpressions.Regex.Match(stderr, @"Duration:\s*(\d{2}):(\d{2}):(\d{2})\.(\d{2})");
                if (match.Success)
                {
                    int hours = int.Parse(match.Groups[1].Value);
                    int mins = int.Parse(match.Groups[2].Value);
                    int secs = int.Parse(match.Groups[3].Value);
                    int centis = int.Parse(match.Groups[4].Value);
                    return new TimeSpan(0, hours, mins, secs, centis * 10);
                }
            }
            catch { }
            
            return TimeSpan.Zero;
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
             _nativeRecorder?.Dispose();
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
        private void OnAudioDataAvailable(byte[] buffer, int count)
        {
             // No-op
        }
        
        private async Task MergeMicToVideo(string output, string videoInput, string micInput)
        {
             string ffmpeg = ClipStudioDesktop.Helpers.FFmpegHelper.GetFFmpegPath();
             if (string.IsNullOrEmpty(ffmpeg)) return;
             
             // Settings
             int vBitrate = _settingsService.CurrentSettings.Video.Bitrate;
             if (vBitrate <= 0) vBitrate = 8000;
             
             int aBitrate = _settingsService.CurrentSettings.Audio.Bitrate;
             if (aBitrate <= 0) aBitrate = 192;
             
             string resolution = _settingsService.CurrentSettings.Video.Resolution;
             string scaleFilter = "";
             if (!string.IsNullOrEmpty(resolution) && resolution.Contains("x") && resolution != "Native")
             {
                 scaleFilter = $"-s {resolution}";
             }

             string ext = Path.GetExtension(output).ToLower();
             string args;
             
             if (ext == ".webm")
             {
                 // WebM: VP9 video + Opus audio (mix system + mic)
                 args = $"-i \"{videoInput}\" -i \"{micInput}\" " +
                        $"-filter_complex \"[0:a][1:a]amix=inputs=2:duration=longest[a]\" " +
                        $"-map 0:v -map \"[a]\" " +
                        $"-c:v libvpx-vp9 -b:v {vBitrate}k {scaleFilter} " +
                        $"-c:a libopus -b:a {aBitrate}k " +
                        $"-shortest \"{output}\"";
             }
             else
             {
                 // MP4/MKV: H264 video + AAC audio (mix system + mic)
                 args = $"-i \"{videoInput}\" -i \"{micInput}\" " +
                        $"-filter_complex \"[0:a][1:a]amix=inputs=2:duration=longest[a]\" " +
                        $"-map 0:v -map \"[a]\" " +
                        $"-c:v libx264 -preset ultrafast -pix_fmt yuv420p " +
                        $"-b:v {vBitrate}k -maxrate {vBitrate}k -bufsize {vBitrate * 2}k {scaleFilter} " +
                        $"-c:a aac -b:a {aBitrate}k " +
                        $"-movflags +faststart " +
                        $"-shortest \"{output}\"";
             }
                           
             await RunFFmpegWithProgress(ffmpeg, args, videoInput, output);
        }
    }
}
