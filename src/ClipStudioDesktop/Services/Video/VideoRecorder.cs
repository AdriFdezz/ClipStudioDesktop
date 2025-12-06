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

            // Command to record desktop in 10s chunks, keeping last 30 chunks (300s)
            // -y: Overwrite output files
            // -f gdigrab: Windows GDI capture
            // -framerate 30: 30 FPS
            // -i desktop: Capture main screen
            // -c:v libx264: H.264 encoding
            // -preset ultrafast: Low CPU usage
            // -f segment: Segment muxer
            // -segment_time 10: 10 seconds per segment
            // -segment_wrap 30: Wrap after 30 segments
            // -reset_timestamps 1: Reset timestamps for each segment
            string arguments = $"-y -f gdigrab -framerate 30 -i desktop -c:v libx264 -preset ultrafast -f segment -segment_time 10 -segment_wrap 30 -reset_timestamps 1 \"{outputFilePattern}\"";

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
