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
        private Task? _cleanupTask;
        private System.Threading.CancellationTokenSource? _cleanupCts;

        public VideoRecorder(AppSettings settings)
        {
            _settings = settings;
            _bufferFolder = Path.Combine(_settings.Paths.TempBuffer, "video");
        }

        public bool IsRunning => _ffmpegProcess != null && !_ffmpegProcess.HasExited;

        public async Task<bool> StartAsync()
        {
            if (_isRecording) return true;

            await FFmpegHelper.EnsureFFmpegInstalledAsync();

            Directory.CreateDirectory(_bufferFolder);
            
            // Clean up previous buffer files
            foreach (var file in Directory.GetFiles(_bufferFolder, "video_*.ts"))
            {
                try { File.Delete(file); } catch { }
            }
            foreach (var file in Directory.GetFiles(_bufferFolder, "video_*.mp4"))
            {
                try { File.Delete(file); } catch { }
            }

            string ffmpegPath = FFmpegHelper.GetFFmpegPath();
            // Use MPEG-TS for buffer to avoid corruption and allow easy concatenation
            // Use strftime to generate unique timestamps
            string outputFilePattern = Path.Combine(_bufferFolder, "video_%Y-%m-%d_%H-%M-%S.ts");

            int segmentTime = 5; // Smaller segments for better granularity
            int bitrateKbps = _settings.Video.Bitrate;
            
            // Try ddagrab first (GPU accelerated capture)
            bool success = await StartFFmpegProcess(ffmpegPath, "ddagrab", outputFilePattern, segmentTime, bitrateKbps, true);
            
            if (!success)
            {
                System.Diagnostics.Debug.WriteLine("Hardware encoding failed, falling back to software encoding");
                success = await StartFFmpegProcess(ffmpegPath, "ddagrab", outputFilePattern, segmentTime, bitrateKbps, false);
            }

            if (!success)
            {
                System.Diagnostics.Debug.WriteLine("ddagrab failed, falling back to gdigrab");
                success = await StartFFmpegProcess(ffmpegPath, "gdigrab", outputFilePattern, segmentTime, bitrateKbps, false);
            }

            if (success)
            {
                _isRecording = true;
                _cleanupCts = new System.Threading.CancellationTokenSource();
                _cleanupTask = StartBufferCleanup(_cleanupCts.Token);
            }
            
            return success;
        }

        private async Task StartBufferCleanup(System.Threading.CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(5000, token); // Check every 5s
                    CleanupOldBufferFiles();
                }
                catch (TaskCanceledException) { break; }
                catch { }
            }
        }

        private void CleanupOldBufferFiles()
        {
            try
            {
                var dir = new DirectoryInfo(_bufferFolder);
                var files = dir.GetFiles("video_*.ts").OrderByDescending(f => f.CreationTime).ToList();
                
                long maxBytes = (long)_settings.Buffer.VideoBufferSizeMB * 1024 * 1024;
                long currentBytes = 0;
                
                // Keep files within budget
                foreach (var file in files)
                {
                    currentBytes += file.Length;
                    if (currentBytes > maxBytes)
                    {
                        try { file.Delete(); } catch { }
                    }
                }
            }
            catch { }
        }

        private async Task<bool> StartFFmpegProcess(string ffmpegPath, string inputFormat, string outputFilePattern, int segmentTime, int bitrateKbps, bool tryHardwareEncoding)
        {
            string encoder = "libx264";
            string preset = "-preset ultrafast -tune zerolatency -threads 4";
            
            if (tryHardwareEncoding)
            {
                encoder = "h264_nvenc";
                preset = "-preset fast -delay 0"; 
            }

            // Use -strftime 1 to support timestamp in filename
            string arguments = $"-y -f {inputFormat} -framerate {_settings.Video.Framerate} -draw_mouse 1 -i desktop " +
                             $"-c:v {encoder} {preset} " +
                             $"-b:v {bitrateKbps}k -maxrate {bitrateKbps}k -bufsize {bitrateKbps}k " +
                             $"-pix_fmt yuv420p " + 
                             $"-f segment -segment_time {segmentTime} -strftime 1 -reset_timestamps 1 " +
                             $"\"{outputFilePattern}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true
            };

            try
            {
                _ffmpegProcess = Process.Start(startInfo);
                
                if (_ffmpegProcess != null)
                {
                    _ffmpegProcess.EnableRaisingEvents = true;
                    _ffmpegProcess.Exited += (s, e) => 
                    {
                        _isRecording = false;
                        System.Diagnostics.Debug.WriteLine($"FFmpeg exited unexpectedly. Exit code: {_ffmpegProcess.ExitCode}");
                    };
                }
                
                await Task.Delay(2000);
                
                if (_ffmpegProcess == null || _ffmpegProcess.HasExited)
                {
                    return false;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start FFmpeg: {ex.Message}");
                return false;
            }
        }

        public void Stop()
        {
            _cleanupCts?.Cancel();
            
            if (!_isRecording || _ffmpegProcess == null) return;

            try
            {
                _ffmpegProcess.StandardInput.WriteLine("q");
                _ffmpegProcess.WaitForExit(2000);
                
                if (!_ffmpegProcess.HasExited)
                {
                    _ffmpegProcess.Kill();
                }
            }
            catch { }
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
                var directory = new DirectoryInfo(_bufferFolder);
                var files = directory.GetFiles("video_*.ts")
                                   .OrderBy(f => f.CreationTime)
                                   .ToList();

                if (files.Count == 0) return null;

                // Calculate needed files
                // Each file is approx 5s (segmentTime)
                int filesNeeded = (int)Math.Ceiling(durationSeconds / 5.0) + 2; // +2 for safety margin
                
                var clipsToConcat = files.Skip(Math.Max(0, files.Count - filesNeeded)).ToList();

                if (clipsToConcat.Count == 0) return null;

                // Copy to temp files to avoid locking issues
                var tempFiles = new List<string>();
                foreach (var clip in clipsToConcat)
                {
                    string tempPath = Path.Combine(_bufferFolder, $"temp_copy_{Path.GetFileName(clip.Name)}");
                    try 
                    {
                        // Use Copy with FileShare.ReadWrite
                        using (var src = new FileStream(clip.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var dst = new FileStream(tempPath, FileMode.Create))
                        {
                            await src.CopyToAsync(dst);
                        }
                        tempFiles.Add(tempPath);
                    }
                    catch { /* Skip if locked/failed */ }
                }

                if (tempFiles.Count == 0) return null;

                // Create concat list
                string listFile = Path.Combine(_bufferFolder, "concat_list.txt");
                var sb = new StringBuilder();
                foreach (var tempFile in tempFiles)
                {
                    sb.AppendLine($"file '{tempFile.Replace("\\", "/")}'");
                }
                await File.WriteAllTextAsync(listFile, sb.ToString());

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
                string finalOutputFile = Path.Combine(outputFolder, $"clip_{timestamp}.mp4");
                string ffmpegPath = FFmpegHelper.GetFFmpegPath();

                // Concat and convert to MP4
                string tempVideoFull = Path.Combine(_bufferFolder, $"temp_video_full_{timestamp}.mp4");
                // -bsf:a aac_adtstoasc is needed when converting TS to MP4 if audio is AAC, but we have no audio in video stream usually
                string concatArgs = $"-y -f concat -safe 0 -i \"{listFile}\" -c copy \"{tempVideoFull}\"";
                
                var pConcat = Process.Start(new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = concatArgs,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (pConcat != null) await pConcat.WaitForExitAsync();

                // Trim
                string tempVideoTrimmed = Path.Combine(_bufferFolder, $"temp_video_trimmed_{timestamp}.mp4");
                string trimArgs = $"-y -sseof -{durationSeconds} -i \"{tempVideoFull}\" -c copy \"{tempVideoTrimmed}\"";
                
                var pTrim = Process.Start(new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = trimArgs,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (pTrim != null) await pTrim.WaitForExitAsync();

                if (string.IsNullOrEmpty(audioPathToMerge))
                {
                    File.Move(tempVideoTrimmed, finalOutputFile);
                }
                else
                {
                    // Merge with audio
                    // Re-encode audio to AAC, copy video
                    string mergeArgs = $"-y -i \"{tempVideoTrimmed}\" -i \"{audioPathToMerge}\" -c:v copy -c:a aac -shortest \"{finalOutputFile}\"";
                    
                    var pMerge = Process.Start(new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = mergeArgs,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    if (pMerge != null) await pMerge.WaitForExitAsync();
                }

                // Cleanup
                try 
                { 
                    File.Delete(listFile);
                    File.Delete(tempVideoFull);
                    File.Delete(tempVideoTrimmed);
                    foreach(var f in tempFiles) File.Delete(f);
                } 
                catch { }

                return File.Exists(finalOutputFile) ? finalOutputFile : null;
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
