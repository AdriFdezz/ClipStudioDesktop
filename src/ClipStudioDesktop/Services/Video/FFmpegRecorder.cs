using ClipStudioDesktop.Models;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClipStudioDesktop.Services.Video
{
    /// <summary>
    /// Graba video+audio simultáneamente usando FFmpeg con gdigrab y audio loopback
    /// Esto elimina problemas de sincronización al grabar ambos streams juntos
    /// </summary>
    public class FFmpegRecorder : IDisposable
    {
        private readonly AppSettings _settings;
        private Process? _recordingProcess;
        private volatile bool _isRecording;
        private readonly string _bufferFolder;
        private readonly ConcurrentQueue<string> _videoSegments = new();
        private int _segmentIndex = 0;
        private System.Threading.Timer? _segmentTimer;
        private readonly int _segmentDurationSeconds = 30;
        private long _lastReservationUpdateSize = 0;
        private const long RESERVATION_UPDATE_THRESHOLD = 100 * 1024 * 1024; // 100MB
        private string? _currentSegmentPath; // Segment currently being recorded
        private readonly object _segmentLock = new object();

        public bool IsRunning => _isRecording;

        // Log file
        private static readonly string _logFile = Path.Combine(Path.GetTempPath(), "ClipStudio_FFmpegRecorder.log");

        private static void Log(string message)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                File.AppendAllText(_logFile, $"[{timestamp}] {message}\n");
            }
            catch { }
        }

        public FFmpegRecorder(AppSettings settings)
        {
            Log("=== FFmpegRecorder initialized ===");
            Log($"Log file: {_logFile}");
            _settings = settings;
            _bufferFolder = Path.Combine(_settings.Paths.TempBuffer, "video");
            Directory.CreateDirectory(_bufferFolder);
        }

        public async Task<bool> StartAsync()
        {
            if (_isRecording) return true;

            try
            {
                Log("Starting FFmpeg recording...");
                StartNewSegment();
                _isRecording = true;

                // Start timer to create new segments every 30 seconds
                _segmentTimer = new System.Threading.Timer(OnSegmentTimer, null, _segmentDurationSeconds * 1000, _segmentDurationSeconds * 1000);

                Log("FFmpeg recording started successfully");
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Log($"ERROR starting recording: {ex.Message}");
                return false;
            }
        }

        private void StartNewSegment()
        {
            // Stop current recording if any
            if (_recordingProcess != null && !_recordingProcess.HasExited)
            {
                try
                {
                    _recordingProcess.StandardInput.WriteLine("q"); // Graceful quit
                    _recordingProcess.WaitForExit(2000);
                    if (!_recordingProcess.HasExited)
                    {
                        _recordingProcess.Kill();
                    }
                }
                catch { }
                _recordingProcess?.Dispose();
                _recordingProcess = null;
            }

            string segmentPath = Path.Combine(_bufferFolder, $"segment_{_segmentIndex:D6}.mp4");
            Log($"Starting new segment: {segmentPath}");
            _segmentIndex++;

            string ffmpegPath = ClipStudioDesktop.Helpers.FFmpegHelper.GetFFmpegPath();
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                Log("ERROR: FFmpeg not found");
                throw new Exception("FFmpeg no encontrado");
            }

            // Get primary screen info for capture region
            var primaryScreen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            int captureWidth = primaryScreen.Bounds.Width;
            int captureHeight = primaryScreen.Bounds.Height;
            int offsetX = primaryScreen.Bounds.X;
            int offsetY = primaryScreen.Bounds.Y;
            
            // Calculate output dimensions (scale to 1080p max if needed)
            int outputWidth = captureWidth;
            int outputHeight = captureHeight;
            if (outputHeight > 1080)
            {
                double scale = 1080.0 / outputHeight;
                outputHeight = 1080;
                outputWidth = (int)(outputWidth * scale);
            }
            // Ensure dimensions are even (required by libx264)
            if (outputWidth % 2 != 0) outputWidth--;
            if (outputHeight % 2 != 0) outputHeight--;

            // FFmpeg arguments for screen capture (VIDEO ONLY)
            // -f gdigrab: Windows screen capture
            // -offset_x/-offset_y: Top-left corner of capture region (primary monitor position)
            // -video_size: Size of capture region (primary monitor dimensions)
            // -framerate: Configured FPS from settings
            // -i desktop: Capture desktop
            // Audio is captured separately by NAudio using WASAPI loopback
            
            // Get configured video settings
            int framerate = _settings.Video.Framerate > 0 ? _settings.Video.Framerate : 30;
            int bitrate = _settings.Video.Bitrate > 0 ? _settings.Video.Bitrate : 8000;
            
            Log($"Recording PRIMARY MONITOR ONLY - Region: {captureWidth}x{captureHeight} at offset ({offsetX},{offsetY})");
            Log($"Output: {outputWidth}x{outputHeight}, FPS: {framerate}, Bitrate: {bitrate}kbps");
            
            string arguments = $"-f gdigrab -framerate {framerate} " +
                             $"-offset_x {offsetX} -offset_y {offsetY} " +
                             $"-video_size {captureWidth}x{captureHeight} " +
                             $"-i desktop " +
                             $"-c:v libx264 -preset ultrafast -b:v {bitrate}k -pix_fmt yuv420p " +
                             $"-s {outputWidth}x{outputHeight} " +
                             $"-t {_segmentDurationSeconds} " +
                             $"-y \"{segmentPath}\"";

            Log($"FFmpeg command: {ffmpegPath} {arguments}");

            _recordingProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                }
            };

            _recordingProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Log($"FFmpeg: {e.Data}");
                }
            };

            _recordingProcess.Exited += (s, e) =>
            {
                int exitCode = _recordingProcess?.ExitCode ?? -1;
                Log($"FFmpeg process exited with code {exitCode}");
                
                // Only add to queue if FFmpeg completed successfully and file exists with content
                lock (_segmentLock)
                {
                    if (_currentSegmentPath != null && File.Exists(_currentSegmentPath))
                    {
                        var fileInfo = new FileInfo(_currentSegmentPath);
                        if (fileInfo.Length > 100000) // At least 100KB to be considered valid
                        {
                            Log($"Segment completed and added to queue: {Path.GetFileName(_currentSegmentPath)} ({fileInfo.Length / 1024}KB)");
                            _videoSegments.Enqueue(_currentSegmentPath);
                        }
                        else
                        {
                            Log($"Segment too small, discarding: {Path.GetFileName(_currentSegmentPath)} ({fileInfo.Length / 1024}KB)");
                            try { File.Delete(_currentSegmentPath); } catch { }
                        }
                    }
                    _currentSegmentPath = null;
                }
                
                // Cleanup old segments
                Task.Run(CleanupOldSegments);
            };
            _recordingProcess.EnableRaisingEvents = true;

            // Track current segment being recorded
            lock (_segmentLock)
            {
                _currentSegmentPath = segmentPath;
            }

            _recordingProcess.Start();
            _recordingProcess.BeginErrorReadLine();
        }

        private void OnSegmentTimer(object? state)
        {
            if (!_isRecording) return;

            try
            {
                Log("Segment timer triggered - starting new segment");
                StartNewSegment();
            }
            catch (Exception ex)
            {
                Log($"ERROR in segment timer: {ex.Message}");
            }
        }

        private void CleanupOldSegments()
        {
            try
            {
                int maxSegmentsToKeep = 7; // 7 x 30s = 3:30 buffer
                int currentSegmentCount = _videoSegments.Count;

                while (currentSegmentCount >= maxSegmentsToKeep)
                {
                    if (_videoSegments.TryDequeue(out string? oldSegment))
                    {
                        if (File.Exists(oldSegment))
                        {
                            try
                            {
                                File.Delete(oldSegment);
                                Log($"Deleted old segment: {Path.GetFileName(oldSegment)}");
                            }
                            catch (Exception ex)
                            {
                                Log($"ERROR deleting segment: {ex.Message}");
                            }
                        }
                    }
                    currentSegmentCount = _videoSegments.Count;
                }

                // Update disk space reservation
                UpdateDiskReservation();
            }
            catch (Exception ex)
            {
                Log($"ERROR in cleanup: {ex.Message}");
            }
        }

        private void UpdateDiskReservation()
        {
            try
            {
                long currentSize = Storage.DiskSpaceReservation.CalculateBufferSize(_bufferFolder);
                long diff = Math.Abs(currentSize - _lastReservationUpdateSize);

                if (diff >= RESERVATION_UPDATE_THRESHOLD)
                {
                    long totalLimit = _settings.Buffer.MaxBufferBytes;
                    Storage.DiskSpaceReservation.UpdateReservation(_bufferFolder, totalLimit);
                    _lastReservationUpdateSize = currentSize;
                }
            }
            catch { }
        }

        /// <summary>
        /// Finalizes the current segment being recorded by sending 'q' to FFmpeg.
        /// This allows including partial recordings in clips.
        /// Returns the path of the finalized segment, or null if no segment was in progress.
        /// </summary>
        private async Task<string?> FinalizeCurrentSegmentAsync()
        {
            string? segmentPath = null;
            
            lock (_segmentLock)
            {
                if (_currentSegmentPath == null || _recordingProcess == null)
                {
                    return null;
                }
                segmentPath = _currentSegmentPath;
            }
            
            Log($"Finalizing current segment: {Path.GetFileName(segmentPath)}");
            
            try
            {
                // Send 'q' to FFmpeg to gracefully stop and finalize the file
                if (_recordingProcess != null && !_recordingProcess.HasExited)
                {
                    _recordingProcess.StandardInput.Write("q");
                    _recordingProcess.StandardInput.Flush();
                    
                    // Wait for FFmpeg to exit (max 3 seconds)
                    var exitTask = Task.Run(() => _recordingProcess.WaitForExit(3000));
                    await exitTask;
                    
                    if (!_recordingProcess.HasExited)
                    {
                        Log("FFmpeg did not exit gracefully, killing process");
                        _recordingProcess.Kill();
                        await Task.Delay(100);
                    }
                }
                
                // The Exited event handler will add the segment to the queue
                // Wait a bit for that to happen
                await Task.Delay(200);
                
                // Start a new segment immediately to continue recording
                if (_isRecording)
                {
                    Log("Starting new segment after finalization");
                    StartNewSegment();
                }
                
                return segmentPath;
            }
            catch (Exception ex)
            {
                Log($"Error finalizing segment: {ex.Message}");
                return null;
            }
        }

        public async Task<string?> SaveClipAsync(int durationSeconds, string outputFolder, string? audioPathToMerge = null, string? micAudioPathToMerge = null)
        {
            try
            {
                Log($"SaveClipAsync started - Duration: {durationSeconds}s");
                
                // First, finalize the current segment if one is being recorded
                // This allows us to include whatever has been recorded so far
                string? finalizedSegment = await FinalizeCurrentSegmentAsync();
                if (finalizedSegment != null)
                {
                    Log($"Finalized current segment: {Path.GetFileName(finalizedSegment)}");
                }
                
                int segmentsNeeded = (int)Math.Ceiling(durationSeconds / (double)_segmentDurationSeconds) + 1; // Add 1 extra for safety
                
                // Get all completed segments (including the just-finalized one)
                var allSegments = _videoSegments.ToArray();
                Log($"Total completed segments in queue: {allSegments.Length}");
                
                // Filter to only segments that exist and are valid
                // Take MORE segments than strictly needed to ensure we have enough content
                var validSegments = allSegments
                    .Where(s => File.Exists(s))
                    .Where(s => new FileInfo(s).Length > 50000) // At least 50KB (lower threshold for partial segments)
                    .TakeLast(segmentsNeeded)
                    .ToArray();
                
                Log($"Valid segments for clip: {validSegments.Length}");

                if (validSegments.Length == 0)
                {
                    Log("ERROR: No valid segments available. Buffer may not have enough recorded content yet.");
                    return null;
                }

                foreach (var seg in validSegments)
                {
                    var fi = new FileInfo(seg);
                    Log($"  Segment: {fi.Name} ({fi.Length / 1024}KB)");
                }

                string tempFolder = Path.Combine(Path.GetTempPath(), $"ClipStudio_{Guid.NewGuid()}");
                Directory.CreateDirectory(tempFolder);

                try
                {
                    // Use configured video format (mp4 or mkv)
                    string videoFormat = _settings.Video.Format.ToLower();
                    if (string.IsNullOrEmpty(videoFormat)) videoFormat = "mp4";
                    
                    string outputFileName = $"clip_{DateTime.Now:yyyyMMdd_HHmmss}.{videoFormat}";
                    string finalOutput = Path.Combine(outputFolder, outputFileName);
                    string videoOnlyOutput = finalOutput;
                    
                    // If we need to merge audio, save video to a temp file first
                    bool hasAudioToMerge = (!string.IsNullOrEmpty(audioPathToMerge) && File.Exists(audioPathToMerge)) ||
                                           (!string.IsNullOrEmpty(micAudioPathToMerge) && File.Exists(micAudioPathToMerge));
                    if (hasAudioToMerge)
                    {
                        videoOnlyOutput = Path.Combine(tempFolder, $"video_only.{videoFormat}");
                    }

                    string videoToTrim;
                    if (validSegments.Length == 1)
                    {
                        // Single segment - use it directly for trimming
                        videoToTrim = validSegments[0];
                    }
                    else
                    {
                        // Concatenate segments first
                        videoToTrim = Path.Combine(tempFolder, $"concatenated.{videoFormat}");
                        await ConcatenateSegmentsAsync(validSegments, videoToTrim);
                    }
                    
                    // ALWAYS trim to exact duration (even for single segments)
                    Log($"Trimming to {durationSeconds}s");
                    await TrimVideoAsync(videoToTrim, videoOnlyOutput, durationSeconds);
                    
                    // Merge audio if provided (Desktop or Mic or Both)
                    bool hasDesktopAudio = !string.IsNullOrEmpty(audioPathToMerge) && File.Exists(audioPathToMerge);
                    bool hasMicAudio = !string.IsNullOrEmpty(micAudioPathToMerge) && File.Exists(micAudioPathToMerge);

                    if (hasDesktopAudio || hasMicAudio)
                    {
                        Log($"Merging audio from sources: Desktop={hasDesktopAudio}, Mic={hasMicAudio}");
                        await MergeVideoWithAudioAsync(videoOnlyOutput, audioPathToMerge, micAudioPathToMerge, finalOutput);
                    }
                    else
                    {
                        // No audio to merge, just rename video only -> final
                        if (File.Exists(finalOutput)) File.Delete(finalOutput);
                        File.Move(videoOnlyOutput, finalOutput);
                    }

                    Log($"SUCCESS: Clip saved: {finalOutput}");
                    return finalOutput;
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(tempFolder))
                        {
                            Directory.Delete(tempFolder, true);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Log($"ERROR in SaveClipAsync: {ex.Message}");
                Log($"StackTrace: {ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Saves an audio-only clip by extracting audio from video segments.
        /// Uses FFmpeg to extract audio track and convert to configured format (MP3/WAV).
        /// </summary>
        /// <param name="durationSeconds">Duration of the clip in seconds</param>
        /// <param name="outputFolder">Folder to save the audio file</param>
        /// <param name="audioFormat">Audio format: "mp3" or "wav"</param>
        /// <param name="audioBitrate">Bitrate for MP3 encoding (e.g., 192)</param>
        /// <returns>Path to the saved audio file, or null on failure</returns>
        public async Task<string?> SaveAudioOnlyClipAsync(int durationSeconds, string outputFolder, string audioFormat, int audioBitrate)
        {
            try
            {
                Log($"SaveAudioOnlyClipAsync started - Duration: {durationSeconds}s, Format: {audioFormat}");
                
                // First, finalize the current segment if one is being recorded
                string? finalizedSegment = await FinalizeCurrentSegmentAsync();
                if (finalizedSegment != null)
                {
                    Log($"Finalized current segment for audio: {Path.GetFileName(finalizedSegment)}");
                }
                
                int segmentsNeeded = (int)Math.Ceiling(durationSeconds / (double)_segmentDurationSeconds);
                
                // Get all completed segments and filter valid ones
                var validSegments = _videoSegments.ToArray()
                    .Where(s => File.Exists(s))
                    .Where(s => new FileInfo(s).Length > 50000)
                    .TakeLast(segmentsNeeded)
                    .ToArray();

                if (validSegments.Length == 0)
                {
                    Log("ERROR: No valid segments available for audio extraction");
                    return null;
                }

                string tempFolder = Path.Combine(Path.GetTempPath(), $"ClipStudio_Audio_{Guid.NewGuid()}");
                Directory.CreateDirectory(tempFolder);

                try
                {
                    string extension = audioFormat.ToLower() == "wav" ? "wav" : "mp3";
                    string outputFileName = $"clip_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}";
                    string finalOutput = Path.Combine(outputFolder, outputFileName);

                    string ffmpegPath = ClipStudioDesktop.Helpers.FFmpegHelper.GetFFmpegPath();
                    if (string.IsNullOrEmpty(ffmpegPath))
                    {
                        Log("ERROR: FFmpeg not found");
                        return null;
                    }

                    string inputFile;
                    if (validSegments.Length == 1)
                    {
                        inputFile = validSegments[0];
                    }
                    else
                    {
                        // Concatenate segments first
                        inputFile = Path.Combine(tempFolder, "concatenated.mp4");
                        await ConcatenateSegmentsAsync(validSegments, inputFile);
                    }

                    // Build FFmpeg arguments for audio extraction
                    // -vn: No video
                    // -sseof: Seek from end of file for last N seconds
                    string codecArgs = audioFormat.ToLower() == "wav"
                        ? "-c:a pcm_s16le"
                        : $"-c:a libmp3lame -b:a {audioBitrate}k";

                    string arguments = $"-y -sseof -{durationSeconds} -i \"{inputFile}\" -vn {codecArgs} \"{finalOutput}\"";

                    Log($"FFmpeg audio extraction: {arguments}");

                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = ffmpegPath,
                            Arguments = arguments,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardError = true
                        }
                    };

                    process.Start();
                    string stderr = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    if (process.ExitCode != 0)
                    {
                        Log($"ERROR: Audio extraction failed: {stderr}");
                        return null;
                    }

                    if (File.Exists(finalOutput))
                    {
                        Log($"SUCCESS: Audio clip saved: {finalOutput}");
                        return finalOutput;
                    }

                    Log("ERROR: Output file was not created");
                    return null;
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(tempFolder))
                        {
                            Directory.Delete(tempFolder, true);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Log($"ERROR in SaveAudioOnlyClipAsync: {ex.Message}");
                Log($"StackTrace: {ex.StackTrace}");
                return null;
            }
        }


        private async Task ConcatenateSegmentsAsync(string[] segments, string outputPath)
        {
            Log($"Concatenating {segments.Length} segments");

            string ffmpegPath = ClipStudioDesktop.Helpers.FFmpegHelper.GetFFmpegPath();
            string listFile = outputPath + ".txt";
            await File.WriteAllLinesAsync(listFile, segments.Select(s => $"file '{s}'"));

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-y -f concat -safe 0 -i \"{listFile}\" -c copy \"{outputPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                }
            };

            process.Start();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                Log($"ERROR: Concatenation failed: {stderr}");
                throw new Exception("Concatenation failed");
            }

            Log("Concatenation successful");
            try { File.Delete(listFile); } catch { }
        }

        private async Task TrimVideoAsync(string inputPath, string outputPath, int durationSeconds)
        {
            Log($"Trimming video to {durationSeconds}s");

            string ffmpegPath = ClipStudioDesktop.Helpers.FFmpegHelper.GetFFmpegPath();
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-y -sseof -{durationSeconds} -i \"{inputPath}\" -c copy \"{outputPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            Log("Trimming complete");
        }

        /// <summary>
        /// Merges a video file with an audio file using FFmpeg.
        /// Uses -shortest to ensure output matches the shorter of the two inputs.
        /// </summary>
        /// <summary>
        /// Merges a video file with audio file(s) using FFmpeg.
        /// Handles scenarios: Desktop only, Mic only, or Both (Mixed).
        /// </summary>
        private async Task MergeVideoWithAudioAsync(string videoPath, string? audioPath, string? micPath, string outputPath)
        {
            Log($"Merging video with audio...");
            bool hasDesktop = !string.IsNullOrEmpty(audioPath) && File.Exists(audioPath);
            bool hasMic = !string.IsNullOrEmpty(micPath) && File.Exists(micPath);

            string ffmpegPath = ClipStudioDesktop.Helpers.FFmpegHelper.GetFFmpegPath();
            string arguments;

            if (hasDesktop && hasMic)
            {
                // Mix both audio sources
                // [1:a][2:a]amix=inputs=2:duration=longest[a]
                arguments = $"-y -i \"{videoPath}\" -i \"{audioPath}\" -i \"{micPath}\" " +
                           $"-filter_complex \"[1:a][2:a]amix=inputs=2:duration=longest[a]\" " +
                           $"-map 0:v -map \"[a]\" " +
                           $"-c:v copy -c:a aac -b:a 192k " +
                           $"-movflags +faststart " +
                           $"-shortest \"{outputPath}\"";
            }
            else if (hasDesktop)
            {
                // Desktop audio only
                arguments = $"-y -i \"{videoPath}\" -i \"{audioPath}\" " +
                           $"-map 0:v -map 1:a " +
                           $"-c:v copy -c:a aac -b:a 192k " +
                           $"-movflags +faststart " +
                           $"-shortest \"{outputPath}\"";
            }
            else if (hasMic)
            {
                // Mic audio only
                arguments = $"-y -i \"{videoPath}\" -i \"{micPath}\" " +
                           $"-map 0:v -map 1:a " +
                           $"-c:v copy -c:a aac -b:a 192k " +
                           $"-movflags +faststart " +
                           $"-shortest \"{outputPath}\"";
            }
            else
            {
                // Should not happen based on caller check, but fallback to copy
                File.Copy(videoPath, outputPath, true);
                return;
            }

            Log($"FFmpeg merge command: {arguments}");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                }
            };

            process.Start();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                Log($"ERROR: Merge failed: {stderr}");
                Log("Falling back to video only...");
                try { File.Copy(videoPath, outputPath, true); } catch { }
            }
            else
            {
                Log("Merge successful");
            }
        }

        public void Stop()
        {
            _isRecording = false;
            _segmentTimer?.Dispose();
            _segmentTimer = null;

            if (_recordingProcess != null && !_recordingProcess.HasExited)
            {
                try
                {
                    _recordingProcess.StandardInput.WriteLine("q");
                    _recordingProcess.WaitForExit(2000);
                    if (!_recordingProcess.HasExited)
                    {
                        _recordingProcess.Kill();
                    }
                }
                catch { }
            }
        }

        public void Dispose()
        {
            Stop();
            _recordingProcess?.Dispose();

            // Clean all segments
            foreach (var segment in _videoSegments)
            {
                try
                {
                    if (File.Exists(segment))
                    {
                        File.Delete(segment);
                    }
                }
                catch { }
            }
        }
    }
}
