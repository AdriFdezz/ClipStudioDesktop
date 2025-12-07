using ClipStudioDesktop.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ClipStudioDesktop.Services.Video
{
    public class VideoRecorder : IDisposable
    {
        private readonly AppSettings _settings;
        private Process? _ffmpegProcess;
        private bool _isRecording;
        private readonly string _bufferFolder;

        public VideoRecorder(AppSettings settings)
        {
            _settings = settings;
            _bufferFolder = Path.Combine(_settings.Paths.TempBuffer, "video");
        }

        public async Task StartAsync()
        {
            if (_isRecording) return;

            await FFmpegHelper.EnsureFFmpegInstalledAsync();

            Directory.CreateDirectory(_bufferFolder);
            
            // Clean up previous buffer files
            foreach (var file in Directory.GetFiles(_bufferFolder, "video_*.mp4"))
            {
                try { File.Delete(file); } catch { }
            }

            string ffmpegPath = FFmpegHelper.GetFFmpegPath();
            string outputFilePattern = Path.Combine(_bufferFolder, "video_%03d.mp4");

            // Calculate segment wrap based on 1GB reserved space (or configured size)
            // Size (MB) = (Bitrate (kbps) * Duration (s)) / (8 * 1024)
            // Duration = (Size * 8 * 1024) / Bitrate
            // Segments = Duration / SegmentTime
            
            int segmentTime = 10;
            int bitrateKbps = _settings.Video.Bitrate;
            int targetSizeMB = _settings.Buffer.VideoBufferSizeMB;
            
            // Calculate total duration to fill target size
            // Use long to avoid overflow
            long totalDurationSeconds = ((long)targetSizeMB * 8 * 1024) / bitrateKbps;
            
            // Ensure minimum duration (e.g. 300s)
            if (totalDurationSeconds < 300) totalDurationSeconds = 300;

            int segmentWrap = (int)(totalDurationSeconds / segmentTime);

            // Command to record desktop
            // Optimization:
            // 1. Use ddagrab (Desktop Duplication) for GPU capture (much lower CPU usage)
            // 2. -rtbufsize 100M: Limit memory buffer
            // 3. -preset ultrafast: Minimal CPU usage for compression
            // 4. -tune zerolatency: Optimize for real-time
            
            // Using ddagrab instead of gdigrab for performance
            
            string arguments = $"-y -f ddagrab -framerate {_settings.Video.Framerate} -draw_mouse 1 -i desktop " +
                             $"-c:v libx264 -preset ultrafast -tune zerolatency " +
                             $"-b:v {bitrateKbps}k -maxrate {bitrateKbps}k -bufsize {bitrateKbps * 2}k " +
                             $"-pix_fmt yuv420p " + 
                             $"-rtbufsize 100M " + 
                             $"-f segment -segment_time {segmentTime} -segment_wrap {segmentWrap} -reset_timestamps 1 " +
                             $"\"{outputFilePattern}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true // Needed to stop gracefully with 'q'
            };

            try
            {
                _ffmpegProcess = Process.Start(startInfo);
                _isRecording = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start FFmpeg: {ex.Message}");
            }
        }

        public void Stop()
        {
            if (!_isRecording || _ffmpegProcess == null) return;

            try
            {
                // Send 'q' to stop gracefully
                _ffmpegProcess.StandardInput.WriteLine("q");
                _ffmpegProcess.WaitForExit(2000);
                
                if (!_ffmpegProcess.HasExited)
                {
                    _ffmpegProcess.Kill();
                }
            }
            catch
            {
                // Ignore errors during stop
            }
            finally
            {
                _ffmpegProcess?.Dispose();
                _ffmpegProcess = null;
                _isRecording = false;
            }
        }

        public async Task<string?> SaveClipAsync(int durationSeconds, string outputFolder, string? audioPathToMerge = null)
        {
            if (!_isRecording) return null;

            try
            {
                // 1. Identify relevant segments
                var directory = new DirectoryInfo(_bufferFolder);
                var files = directory.GetFiles("video_*.mp4")
                                   .OrderBy(f => f.LastWriteTime)
                                   .ToList();

                if (files.Count == 0) return null;

                // Calculate how many files we need
                // Each file is approx 10s. 
                int filesNeeded = (int)Math.Ceiling(durationSeconds / 10.0);
                
                // Take the last N files
                var clipsToConcat = files.Skip(Math.Max(0, files.Count - filesNeeded)).ToList();

                if (clipsToConcat.Count == 0) return null;

                // 2. Create concat list file
                string listFile = Path.Combine(_bufferFolder, "concat_list.txt");
                var sb = new StringBuilder();
                foreach (var clip in clipsToConcat)
                {
                    // FFmpeg concat requires absolute paths with forward slashes or escaped backslashes
                    sb.AppendLine($"file '{clip.FullName.Replace("\\", "/")}'");
                }
                await File.WriteAllTextAsync(listFile, sb.ToString());

                // 3. Run FFmpeg
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string finalOutputFile = Path.Combine(outputFolder, $"clip_{timestamp}.mp4");
                string ffmpegPath = FFmpegHelper.GetFFmpegPath();

                if (string.IsNullOrEmpty(audioPathToMerge))
                {
                    // Video only
                    string args = $"-y -f concat -safe 0 -i \"{listFile}\" -c copy \"{finalOutputFile}\"";
                    var p = Process.Start(new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    await p.WaitForExitAsync();
                }
                else
                {
                    // Video + Audio Merge
                    // First concat video to temp file
                    string tempVideoFile = Path.Combine(_bufferFolder, $"temp_video_{timestamp}.mp4");
                    string concatArgs = $"-y -f concat -safe 0 -i \"{listFile}\" -c copy \"{tempVideoFile}\"";
                    
                    var pConcat = Process.Start(new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = concatArgs,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    await pConcat.WaitForExitAsync();

                    // Then merge with audio
                    // -c:v copy: Copy video stream (fast)
                    // -c:a aac: Encode audio to AAC
                    // -shortest: Stop when the shortest stream ends (usually audio)
                    string mergeArgs = $"-y -i \"{tempVideoFile}\" -i \"{audioPathToMerge}\" -c:v copy -c:a aac -shortest \"{finalOutputFile}\"";
                    
                    var pMerge = Process.Start(new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = mergeArgs,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    await pMerge.WaitForExitAsync();

                    // Cleanup temp video
                    try { File.Delete(tempVideoFile); } catch { }
                }

                return finalOutputFile;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving video clip: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
