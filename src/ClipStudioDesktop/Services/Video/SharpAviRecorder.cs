using System;
using System.IO;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.Wave;
using SharpAvi;
using SharpAvi.Codecs;
using SharpAvi.Output;

namespace ClipStudioDesktop.Services.Video
{
    /// <summary>
    /// Grabador de video que utiliza SharpAvi para generar archivos AVI (Motion JPEG) y captura de audio WASAPI.
    /// <para>Esta clase implementa un bucle de captura de video dedicado en un hilo separado para mantener un frame rate constante.</para>
    /// </summary>
    public class SharpAviRecorder : IDisposable
    {
        private AviWriter? _writer;
        private IAviVideoStream? _videoStream;
        private IAviAudioStream? _audioStream;
        private WasapiLoopbackCapture? _audioCapture;
        
        private Thread? _videoThread;
        private readonly ManualResetEvent _stopEvent = new ManualResetEvent(false);
        private readonly object _syncLock = new object();
        
        private int _width;
        private int _height;
        private string? _outputPath;
        private bool _isRecording;

        // Configuración
        private int _frameRate = 30; // Por defecto
        private int _quality = 85; // Calidad JPEG por defecto
        
        /// <summary>
        /// Inicia la grabación de video AVI.
        /// </summary>
        /// <param name="outputPath">Ruta completa del archivo .avi de salida.</param>
        /// <param name="fps">Frames por segundo deseados.</param>
        /// <param name="bounds">Rectángulo de la pantalla a capturar.</param>
        /// <param name="quality">Calidad de compresión JPEG (10-100).</param>
        /// <param name="recordAudio">Si es true, inicializa la captura de audio del sistema (WASAPI).</param>
        public void StartRecording(string outputPath, int fps, Rectangle bounds, int quality = 85, bool recordAudio = true)
        {
            if (_isRecording) return;
            
            _outputPath = outputPath;
            _frameRate = fps;
            _quality = quality;
            
            // Validar límites de calidad
            if (_quality < 10) _quality = 10;
            if (_quality > 100) _quality = 100;
            
            _width = bounds.Width;
            _height = bounds.Height;
            
            // Ajustar resoluciones impares
            if (_width % 2 != 0) _width--;
            if (_height % 2 != 0) _height--;
            
            // Validar dimensiones mínimas
            if (_width <= 0 || _height <= 0) throw new Exception("Dimensiones de captura inválidas.");

            try
            {
                // Crear escritor AVI
                _writer = new AviWriter(_outputPath)
                {
                    FramesPerSecond = _frameRate,
                    EmitIndex1 = true
                };

                // Agregar Stream de Video
                _videoStream = _writer.AddVideoStream();
                _videoStream.Width = _width;
                _videoStream.Height = _height;
                _videoStream.Codec = new SharpAvi.FourCC("MJPG"); 
                _videoStream.BitsPerPixel = BitsPerPixel.Bpp24;

                // Configurar Audio
                if (recordAudio)
                {
                    InitializeAudioCapture();
                }

                _isRecording = true;
                _stopEvent.Reset();

                // Iniciar Audio Capture
                _audioCapture?.StartRecording();

                // Iniciar Hilo de Captura de Video con los bounds específicos
                _videoThread = new Thread(() => VideoLoop(bounds))
                {
                    IsBackground = true,
                    Priority = ThreadPriority.Highest,
                    Name = "SharpAviVideoCaptureThread"
                };
                _videoThread.Start();
                
                Debug.WriteLine($"[SharpAviRecorder] Started recording to {_outputPath} at {_frameRate}fps. Bounds: {bounds}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SharpAviRecorder] Error starting: {ex.Message}");
                Cleanup();
                throw; // Propagar error al servicio
            }
        }
        public void Stop()
        {
            if (!_isRecording) return;
            
            Debug.WriteLine("[SharpAviRecorder] Stopping...");
            _isRecording = false;
            
            // Señalizar al hilo de video para que termine
            _stopEvent.Set();
            _videoThread?.Join(1000); // Esperar máximo 1s
            
            // Detener Audio
            _audioCapture?.StopRecording();
            
            // Cerrar Writer y liberar recursos
            Cleanup();
            Debug.WriteLine("[SharpAviRecorder] Stopped.");
        }

        /// <summary>
        /// Inicializa la captura de audio loopback (lo que se oye en los altavoces).
        /// Configura la conversión automática de IEEE Float (WASAPI) a PCM 16-bit (SharpAvi).
        /// </summary>
        private void InitializeAudioCapture()
        {
            try
            {
                _audioCapture = new WasapiLoopbackCapture();
                
                // WASAPI entrega audio en Float 32-bit. SharpAvi trabaja mejor con PCM 16-bit estándar.
                // Convertiremos Float -> 16-bit PCM al vuelo para evitar ruido/saturación.
                
                int sourceChannels = _audioCapture.WaveFormat.Channels;
                int sourceSampleRate = _audioCapture.WaveFormat.SampleRate;
                
                // Definir formato destino (16-bit PCM)
                _audioStream = _writer!.AddAudioStream(sourceChannels, sourceSampleRate, 16); // 16 bits por muestra
                _audioStream.Name = "System Audio";
                
                _audioCapture.DataAvailable += (s, e) =>
                {
                    if (_isRecording && _audioStream != null && e.BytesRecorded > 0)
                    {
                        // CONVERSIÓN: Float (4 bytes/sample) -> PCM 16 (2 bytes/sample)
                        
                        byte[] buffer = e.Buffer;
                        int bytesRecorded = e.BytesRecorded;
                        
                        // Calcular cantidad de muestras (Float = 4 bytes)
                        int sampleCount = bytesRecorded / 4;
                        
                        // Tamaño buffer salida (Short = 2 bytes) -> Mitad de tamaño
                        byte[] pcmBuffer = new byte[sampleCount * 2];
                        
                        for (int i = 0; i < sampleCount; i++)
                        {
                            // Leer Float
                            float sample = BitConverter.ToSingle(buffer, i * 4);
                            
                            // Clamp -1.0 a 1.0 (prevenir distorsión por wrapping)
                            if (sample > 1.0f) sample = 1.0f;
                            if (sample < -1.0f) sample = -1.0f;
                            
                            // Escalar al rango Short (16-bit signed)
                            short pcm = (short)(sample * 32767);
                            
                            // Escribir Short (Little Endian)
                            pcmBuffer[i * 2] = (byte)(pcm & 0xFF);
                            pcmBuffer[i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
                        }

                        lock (_syncLock)
                        {
                            try { _audioStream.WriteBlock(pcmBuffer, 0, pcmBuffer.Length); } catch { }
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SharpAviRecorder] Audio Init Failed: {ex.Message}");
                _audioCapture = null;
                _audioStream = null;
            }
        }

        /// <summary>
        /// Bucle principal de captura de video. Se ejecuta en un hilo de alta prioridad.
        /// Intenta mantener una tasa de frames constante (CFR) ajustando los tiempos de espera
        /// y duplicando frames si el sistema se retrasa.
        /// </summary>
        /// <summary>
        /// Bucle principal de captura de video. Se ejecuta en un hilo de alta prioridad.
        /// </summary>
        private void VideoLoop(Rectangle bounds)
        {
            using var bitmap = new Bitmap(_width, _height);
            using var graphics = Graphics.FromImage(bitmap);
            
            // Configurar encoder JPEG
            var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)_quality); 
            var jpegCodec = ImageCodecInfo.GetImageDecoders().FirstOrDefault(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
            if (jpegCodec == null) return;
            
            // MemoryStream reutilizable
            using var ms = new MemoryStream();

            // LÓGICA DE SINCRONIZACIÓN (Constant Frame Rate)
            
            double msPerFrame = 1000.0 / _frameRate;
            long startTime = Stopwatch.GetTimestamp();
            long framesWritten = 0;
            
            try
            {
                while (!_stopEvent.WaitOne(0))
                {
                    // 1. Calcular cuántos frames DEBERÍAN existir en este momento
                    long now = Stopwatch.GetTimestamp();
                    double elapsedSeconds = (double)(now - startTime) / Stopwatch.Frequency;
                    long targetFrameCount = (long)(elapsedSeconds * _frameRate);
                    
                    // 2. Si vamos adelantados (ya escribimos suficientes), esperar
                    if (framesWritten > targetFrameCount)
                    {
                        // Calcular tiempo de espera
                        int waitMs = (int)(msPerFrame * (framesWritten - targetFrameCount));
                        if (waitMs > 1) 
                        {
                            Thread.Sleep(Math.Min(waitMs, 10)); // Dormir en trozos pequeños para mantener responsividad
                            continue;
                        }
                    }
                    
                    // 3. Capturar UN frame desde las coordenadas del monitor seleccionado
                    graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new Size(_width, _height), CopyPixelOperation.SourceCopy);
                    
                    // Comprimir a MJPEG
                    ms.SetLength(0);
                    bitmap.Save(ms, jpegCodec, encoderParams);
                    byte[] jpegData = ms.ToArray();

                    // 4. Determinar cuenta de escritura
                    // Si vamos atrasados, escribimos múltiples veces para alcanzar el reloj del SIGUIENTE frame
                    long framesNeeded = (targetFrameCount + 1) - framesWritten;
                    if (framesNeeded < 1) framesNeeded = 1; // Siempre escribir al menos 1 si hicimos el trabajo
                    
                    // Limitar duplicados para evitar archivos gigantes si hay congelamiento (ej. max 5 por ciclo)
                    if (framesNeeded > 5) framesNeeded = 5; 

                    lock (_syncLock)
                    {
                        if (_videoStream != null && _isRecording)
                        {
                            for (int i = 0; i < framesNeeded; i++)
                            {
                                _videoStream.WriteFrame(true, jpegData, 0, jpegData.Length);
                                framesWritten++;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SharpAviRecorder] Video Loop Error: {ex.Message}");
            }
        }

        private void Cleanup()
        {
            try { _writer?.Close(); } catch { }
            try { _audioCapture?.Dispose(); } catch { }
            
            _writer = null;
            _audioCapture = null;
            _videoStream = null;
            _audioStream = null;
        }

        public void Dispose()
        {
            Stop();
            _stopEvent.Dispose();
        }
    }
}
