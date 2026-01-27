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
        private string? _currentSegmentPath; 
        
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
            _bufferFolder = Path.Combine(_settings.Paths.Cache, "video");
            Directory.CreateDirectory(_bufferFolder);
        }



        public void StartRecording(string outputFilePath)
        {
            if (_isRecording) return;

            if (!Directory.Exists(_bufferFolder))
            {
                Directory.CreateDirectory(_bufferFolder);
            }

            _isRecording = true;
            _currentSegmentPath = outputFilePath;

            string ffmpegPath = ClipStudioDesktop.Helpers.FFmpegHelper.GetFFmpegPath();
            if (string.IsNullOrEmpty(ffmpegPath)) throw new Exception("FFmpeg no encontrado");

            // Screen capture setup for primary monitor
            var primaryScreen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            int w = primaryScreen.Bounds.Width;
            int h = primaryScreen.Bounds.Height;
            int x = primaryScreen.Bounds.X;
            int y = primaryScreen.Bounds.Y;

            // Scale to max 1080p height
            int outW = w;
            int outH = h;
            if (outH > 1080) { double s = 1080.0 / outH; outH = 1080; outW = (int)(outW * s); }
            if (outW % 2 != 0) outW--; if (outH % 2 != 0) outH--;

            int fps = _settings.Video.Framerate > 0 ? _settings.Video.Framerate : 30;
            int br = _settings.Video.Bitrate > 0 ? _settings.Video.Bitrate : 8000;

            string arguments = $"-f gdigrab -framerate {fps} " +
                             $"-offset_x {x} -offset_y {y} " +
                             $"-video_size {w}x{h} " +
                             $"-i desktop " +
                             $"-c:v libx264 -preset ultrafast -b:v {br}k -pix_fmt yuv420p " +
                             $"-s {outW}x{outH} " +
                             $"\"{outputFilePath}\"";

            Log($"Starting DIRECT recording to: {outputFilePath}");
            Log($"CMD: {ffmpegPath} {arguments}");

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
        }

        public async Task Stop()
        {
            _isRecording = false;
            
            if (_recordingProcess != null && !_recordingProcess.HasExited)
            {
                try
                {
                    _recordingProcess.StandardInput.WriteLine("q");
                    
                    // Wait nicely
                    var cts = new CancellationTokenSource(2000);
                    try { await _recordingProcess.WaitForExitAsync(cts.Token); } catch { }
                    
                    if (!_recordingProcess.HasExited)
                    {
                        _recordingProcess.Kill();
                    }
                }
                catch { }
                _recordingProcess?.Dispose();
                _recordingProcess = null;
            }
        }

        public void Dispose()
        {
             Stop().Wait();
            _recordingProcess?.Dispose();
        }
    }
}
