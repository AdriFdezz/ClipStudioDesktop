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

            // Get primary screen resolution
            var primaryScreen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            int width = primaryScreen.Bounds.Width;
            int height = primaryScreen.Bounds.Height;

            // Scale to 1080p if needed
            if (height > 1080)
            {
                double scale = 1080.0 / height;
                height = 1080;
                width = (int)(width * scale);
                // Ensure dimensions are even
                if (width % 2 != 0) width--;
                if (height % 2 != 0) height--;
            }

            // FFmpeg arguments for screen capture with audio
            // -f gdigrab: Windows screen capture
            // -framerate 30: 30 fps
            // -i desktop: Capture entire desktop
            // -f wasapi -i audio=loopback: Capture system audio (desktop audio)
            // Optionally mix microphone input if enabled
            // -c:v libx264 -preset ultrafast: H.264 encoding with fast preset
            // -crf 23: Quality (lower = better, 23 is good balance)
            // -c:a aac -b:a 192k: AAC audio at 192kbps
            
            string audioInputs = "-f wasapi -i audio=\"\""; // System audio (loopback)
            string audioFilters = "";
            
            // Add microphone input if enabled
            if (_settings.Audio.EnableMicrophone)
            {
                string micDevice = string.IsNullOrEmpty(_settings.Audio.SelectedMicrophone) 
                    ? "" // Default microphone
                    : _settings.Audio.SelectedMicrophone;
                
                audioInputs += $" -f dshow -i audio=\"{micDevice}\"";
                
                // Mix both audio sources with amerge filter
                // [0:a] = system audio, [1:a] = microphone
                audioFilters = "-filter_complex \"[0:a][1:a]amerge=inputs=2[aout]\" -map 0:v -map \"[aout]\" ";
            }
            else
            {
                audioFilters = "-map 0:v -map 0:a ";
            }
            
            string arguments = $"-f gdigrab -framerate 30 -i desktop " +
                             $"{audioInputs} " +
                             $"{audioFilters}" +
                             $"-c:v libx264 -preset ultrafast -crf 23 -pix_fmt yuv420p " +
                             $"-s {width}x{height} " +
                             $"-c:a aac -b:a 192k " +
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
                Log($"FFmpeg process exited with code {_recordingProcess?.ExitCode}");
            };
            _recordingProcess.EnableRaisingEvents = true;

            _recordingProcess.Start();
            _recordingProcess.BeginErrorReadLine();

            // Add to queue
            _videoSegments.Enqueue(segmentPath);

            // Cleanup old segments
            Task.Run(CleanupOldSegments);
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

        public async Task<string?> SaveClipAsync(int durationSeconds, string outputFolder, string? audioPathToMerge = null)
        {
            try
            {
                Log($"SaveClipAsync started - Duration: {durationSeconds}s");
                int segmentsNeeded = (int)Math.Ceiling(durationSeconds / (double)_segmentDurationSeconds);
                var segments = _videoSegments.ToArray().TakeLast(segmentsNeeded).ToArray();

                if (segments.Length == 0)
                {
                    Log("ERROR: No segments available");
                    return null;
                }

                string tempFolder = Path.Combine(Path.GetTempPath(), $"ClipStudio_{Guid.NewGuid()}");
                Directory.CreateDirectory(tempFolder);

                try
                {
                    string outputFileName = $"clip_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
                    string finalOutput = Path.Combine(outputFolder, outputFileName);

                    if (segments.Length == 1 && durationSeconds == _segmentDurationSeconds)
                    {
                        // Single segment, exact duration - just copy
                        Log("Single segment, exact duration - copying directly");
                        File.Copy(segments[0], finalOutput, true);
                    }
                    else
                    {
                        // Concatenate segments
                        string concatenated = Path.Combine(tempFolder, "concatenated.mp4");
                        await ConcatenateSegmentsAsync(segments, concatenated);

                        // Trim if needed
                        bool needsTrimming = (durationSeconds % _segmentDurationSeconds) != 0;
                        if (needsTrimming)
                        {
                            Log($"Trimming to {durationSeconds}s");
                            await TrimVideoAsync(concatenated, finalOutput, durationSeconds);
                        }
                        else
                        {
                            Log("No trimming needed");
                            File.Copy(concatenated, finalOutput, true);
                        }
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
