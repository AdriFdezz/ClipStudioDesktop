using ClipStudioDesktop.Models;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Pipes;
using System.Windows.Forms;

namespace ClipStudioDesktop.Services.Video
{
    /// <summary>
    /// Grabador de video y audio utilizando FFmpeg directamente (vía CLI).
    /// Utiliza `gdigrab` para captura de pantalla y `NamedPipe` para recibir audio PCM en tiempo real.
    /// <para>Esta implementación busca evitar problemas de desincronización procesando ambos flujos en un solo comando FFmpeg.</para>
    /// </summary>
    public class FFmpegRecorder : IDisposable
    {
        private readonly AppSettings _settings;
        private Process? _recordingProcess;
        private volatile bool _isRecording;
        private readonly string _bufferFolder;

        // Variables para manejo de segmentos y tubería de audio
        private string? _currentSegmentPath;
        private NamedPipeServerStream? _audioPipe;
        private Task? _pipeTask;
 
        
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

        /// <summary>
        /// Inicia la grabación directa usando `ffmpeg.exe`.
        /// </summary>
        /// <param name="outputFilePath">Ruta del archivo de salida.</param>
        /// <param name="recordAudio">Si es true, configura un Named Pipe para recibir audio raw.</param>
        /// <param name="sampleRate">Frecuencia de muestreo del audio.</param>
        /// <param name="channels">Canales de audio.</param>
        /// <param name="pcmFormat">Formato PCM (ej. f32le) que se enviará por el pipe.</param>
        public void StartRecording(string outputFilePath, bool recordAudio = false, int sampleRate = 48000, int channels = 2, string pcmFormat = "f32le")
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

            string arguments;
            
            if (recordAudio)
            {
                // Configurar Named Pipe para audio
                string pipeName = $"clipstudio_audio_{Process.GetCurrentProcess().Id}";
                _audioPipe = new NamedPipeServerStream(pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536);
                
                // Esperar conexión de forma asíncrona
                _pipeTask = Task.Run(() => 
                {
                    try { _audioPipe.WaitForConnection(); } catch { }
                });

                // Argumentos FFmpeg con entrada de Audio Pipe
                // Optimización V7 (Manual Delay): 
                // - thread_queue_size 1024: por seguridad
                // - filter_complex ELIMINADO: Se confía en el delay manual de RecordingService
                // - zerolatency y vsync 1: Mantenidos para fluidez
                 arguments = $"-thread_queue_size 1024 -f gdigrab -framerate {fps} -offset_x {x} -offset_y {y} -video_size {w}x{h} -i desktop " +
                             $"-thread_queue_size 1024 -f {pcmFormat} -ar {sampleRate} -ac {channels} -i \\\\.\\pipe\\{pipeName} " +
                             $"-map 0:v -map 1:a " +
                             $"-c:v libx264 -preset ultrafast -tune zerolatency -b:v {br}k -pix_fmt yuv420p -vsync 1 " +
                             $"-c:a aac -b:a 192k " +
                             $"-fflags nobuffer " +
                             $"-s {outW}x{outH} " +
                             $"\"{outputFilePath}\"";
            }
            else
            {
                // Solo Video
                arguments = $"-f gdigrab -framerate {fps} " +
                                 $"-offset_x {x} -offset_y {y} " +
                                 $"-video_size {w}x{h} " +
                                 $"-i desktop " +
                                 $"-c:v libx264 -preset ultrafast -b:v {br}k -pix_fmt yuv420p " +
                                 $"-s {outW}x{outH} " +
                                 $"\"{outputFilePath}\"";
            }

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

        /// <summary>
        /// Detiene la grabación enviando el comando 'q' a la entrada estándar de FFmpeg.
        /// Espera a que el proceso termine ordenadamente.
        /// </summary>
        public async Task Stop()
        {
            _isRecording = false;
            
            if (_recordingProcess != null && !_recordingProcess.HasExited)
            {
                try
                {
                    _recordingProcess.StandardInput.WriteLine("q");
                    
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

            
            _audioPipe?.Dispose();
            _audioPipe = null;
        }

        /// <summary>
        /// Escribe datos de audio (PCM) en el Named Pipe para que FFmpeg los integre en el video.
        /// </summary>
        public void WriteAudio(byte[] buffer, int count)
        {
            if (_audioPipe != null && _audioPipe.IsConnected)
            {
                try
                {
                    _audioPipe.Write(buffer, 0, count);
                }
                catch { }
            }
        }

        public void Dispose()
        {
             Stop().Wait();
            _recordingProcess?.Dispose();
        }
    }
}
