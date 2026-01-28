using ClipStudioDesktop.Helpers;
using ClipStudioDesktop.Models;
using ClipStudioDesktop.Services.Video;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ClipStudioDesktop.Services.Audio
{
    /// <summary>
    /// Gestiona la grabación de audio del sistema (Escritorio) utilizando WASAPI Loopback.
    /// <para>
    /// Funciona capturando el flujo de audio directamente desde la tarjeta de sonido, escribiendo los datos crudos (PCM)
    /// a un archivo temporal y luego convirtiéndolo al formato deseado (MP3/FLAC) usando FFmpeg.
    /// </para>
    /// </summary>
    public class AudioRecorder : IDisposable
    {
        private readonly AppSettings _settings;
        private WasapiLoopbackCapture? _capture;
        
        /// <summary>
        /// Formato de onda detectado del dispositivo de audio del sistema (frecuencia, canales, bits).
        /// </summary>
        public WaveFormat? WaveFormat => _waveFormat;
        private WaveFormat? _waveFormat;
        private bool _isRecording;
        
        // Gestión de Búfer en Disco
        private readonly string _bufferFolder;
        private readonly string _bufferRootPath;
        private FileStream? _currentChunkStream;
        private string? _currentChunkPath;
        private readonly List<string> _chunks = new List<string>();
        private readonly object _lock = new object();

        private long _currentTotalBytes;
        
        /// <summary>
        /// Evento que se dispara cuando hay nuevos datos de audio disponibles.
        /// Útil para visualizar medidores de volumen (VU Meter).
        /// </summary>
        public event Action<byte[], int>? AudioDataAvailable;

        public AudioRecorder(AppSettings settings)
        {
            _settings = settings;
            _bufferRootPath = _settings.Paths.Cache;
            _bufferFolder = Path.Combine(_bufferRootPath, "audio");
        }

        /// <summary>
        /// Inicia la grabación de audio del sistema.
        /// </summary>
        /// <param name="outputFilePath">Ruta completa donde se guardará el archivo temporal RAW.</param>
        /// <returns><c>true</c> si inició correctamente; <c>false</c> si falló.</returns>
        public bool Start(string? outputFilePath)
        {
            if (_isRecording) return true;

            try 
            {
                // Asegurar que existe el directorio de caché
                Directory.CreateDirectory(_bufferFolder);
                _currentTotalBytes = 0;
                _currentChunkPath = outputFilePath;

                try 
                {
                    // Inicializar captura de loopback (audio del sistema)
                    _capture = new WasapiLoopbackCapture();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to initialize WasapiLoopbackCapture: {ex.Message}");
                    return false;
                }

                if (_capture == null) return false;

                _waveFormat = _capture.WaveFormat;
                
                // Abrir stream para escribir directamente los datos crudos
                if (!string.IsNullOrEmpty(_currentChunkPath))
                {
                    _currentChunkStream = new FileStream(_currentChunkPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                }

                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;

                _capture.StartRecording();
                _isRecording = true;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting audio recording: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Detiene la captura de audio y cierra el stream de escritura.
        /// </summary>
        public void Stop()
        {
            if (!_isRecording) return;

            _capture?.StopRecording();
            _isRecording = false;
            
            lock (_lock)
            {
                _currentChunkStream?.Flush();
                _currentChunkStream?.Dispose();
                _currentChunkStream = null;
            }
        }

        /// <summary>
        /// Callback invocado por NAudio cuando hay un fragmento de audio disponible.
        /// </summary>
        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0) return;

            // Escribir al archivo temporal
            if (_currentChunkStream != null)
            {
                _currentChunkStream.Write(e.Buffer, 0, e.BytesRecorded);
                _currentTotalBytes += e.BytesRecorded;
            }
            
            // Notificar a la UI (VU Meter)
            AudioDataAvailable?.Invoke(e.Buffer, e.BytesRecorded);
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            _isRecording = false;
            if (e.Exception != null)
            {
                System.Diagnostics.Debug.WriteLine($"Audio recording stopped with error: {e.Exception.Message}");
            }
        }
        
        /// <summary>
        /// Finaliza la grabación convirtiendo el archivo temporal RAW al formato de salida final.
        /// </summary>
        /// <param name="finalOutputFolder">Carpeta de destino para el archivo final.</param>
        /// <param name="format">Formato deseado (ej. "mp3", "wav").</param>
        /// <returns>La ruta absoluta del archivo final generado, o null si falló.</returns>
        public async Task<string?> FinalizeRecordingAsync(string finalOutputFolder, string format)
        {
             if (_currentChunkPath == null || !File.Exists(_currentChunkPath)) return null;

             try
             {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string extension = format.ToLower() == "wav" ? "wav" : "mp3";
                string outputFile = Path.Combine(finalOutputFolder, $"recording_audio_{timestamp}.{extension}");

                // Conversión asíncrona usando FFmpeg
                await ConvertRawToOutputAsync(_currentChunkPath, outputFile, format);
                
                // Limpiar archivo temporal crudo
                try { File.Delete(_currentChunkPath); } catch { }
                
                return outputFile;
             }
             catch (Exception ex)
             {
                 Debug.WriteLine($"Error finalizing audio: {ex.Message}");
                 return null;
             }
        }

        /// <summary>
        /// Ejecuta FFmpeg para convertir el audio raw PCM al formato destino.
        /// </summary>
        private async Task ConvertRawToOutputAsync(string inputFile, string outputFile, string format)
        {
             try
             {
                 string ffmpegPath = FFmpegHelper.GetFFmpegPath();
                 string pcmFormat = GetFFmpegPcmFormat(_waveFormat!); // Detectar formato de bits
                 string sampleRate = _waveFormat!.SampleRate.ToString();
                 string channels = _waveFormat!.Channels.ToString();
                 
                 // Selección de códec según formato
                 string codecArgs;
                 switch (format.ToLower())
                 {
                     case "mp3":
                         codecArgs = $"-c:a libmp3lame -b:a {_settings.Audio.Bitrate}k";
                         break;
                     case "flac":
                         codecArgs = "-c:a flac -compression_level 5"; // Compresión sin pérdida
                         break;
                     default:
                         codecArgs = "-c:a libmp3lame -b:a 192k"; // Fallback MP3 básico
                         break;
                 }

                 // Construir comando FFmpeg: Input RAW -> Output codificado
                 string args = $"-y -f {pcmFormat} -ar {sampleRate} -ac {channels} -i \"{inputFile}\" {codecArgs} \"{outputFile}\"";
                 
                 System.Diagnostics.Debug.WriteLine($"FFmpeg command: {args}");
                 
                 var p = Process.Start(new ProcessStartInfo
                 {
                     FileName = ffmpegPath,
                     Arguments = args,
                     UseShellExecute = false,
                     CreateNoWindow = true,
                     RedirectStandardError = true,
                     RedirectStandardOutput = true
                 });
                 
                 if (p != null)
                 {
                     // Espera asíncrona para no bloquear UI
                     string errors = await p.StandardError.ReadToEndAsync();
                     await p.WaitForExitAsync();
                     
                     if (p.ExitCode != 0)
                     {
                         System.Diagnostics.Debug.WriteLine($"FFmpeg stderr: {errors}");
                         throw new Exception($"FFmpeg falló al convertir audio. Código: {p.ExitCode}");
                     }
                 }
             }
             catch (Exception ex)
             {
                 System.Diagnostics.Debug.WriteLine($"Error converting audio: {ex.Message}");
                 throw;
             }
        }

        /// <summary>
        /// Traduce el formato de NAudio a formato de entrada de FFmpeg.
        /// </summary>
        /// <param name="format">Formato de onda NAudio.</param>
        /// <returns>String de formato PCM para FFmpeg (ej. "f32le", "s16le").</returns>
        private string GetFFmpegPcmFormat(WaveFormat format)
        {
            if (format.Encoding == WaveFormatEncoding.IeeeFloat) return "f32le"; // Float 32-bit (común en WASAPI)
            if (format.Encoding == WaveFormatEncoding.Pcm)
            {
                switch (format.BitsPerSample)
                {
                    case 16: return "s16le";
                    case 24: return "s24le";
                    case 32: return "s32le";
                }
            }
            return "s16le"; // Default seguro
        }

        /// <summary>
        /// Libera recursos y limpia archivos temporales.
        /// </summary>
        public void Dispose()
        {
            Stop();
            
            // Limpieza agresiva de temporales al cerrar
            try
            {
                lock (_lock)
                {
                    foreach (var chunk in _chunks.ToArray())
                    {
                        try { File.Delete(chunk); } catch { }
                    }
                    _chunks.Clear();
                    
                    if (_currentChunkPath != null && File.Exists(_currentChunkPath))
                    {
                        try { File.Delete(_currentChunkPath); } catch { }
                    }
                }
            }
            catch { }
        }
    }
}
