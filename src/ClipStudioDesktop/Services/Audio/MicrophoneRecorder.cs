using ClipStudioDesktop.Helpers;
using ClipStudioDesktop.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ClipStudioDesktop.Services.Audio
{
    /// <summary>
    /// Gestiona la grabación del micrófono utilizando WASAPI.
    /// <para>
    /// Incluye procesamiento de audio en tiempo real para:
    /// <list type="bullet">
    /// <item><description>Selección de dispositivo de entrada.</description></item>
    /// <item><description>Aplicación de Ganancia (+dB).</description></item>
    /// <item><description>Puerta de Ruido (Noise Gate) para silenciar el fondo.</description></item>
    /// </list>
    /// Los datos se guardan inicialmente en formato RAW y se convierten a WAV asincrónicamente al finalizar.
    /// </para>
    /// </summary>
    public class MicrophoneRecorder : IDisposable
    {
        private readonly AppSettings _settings;
        private WasapiCapture? _capture;
        private WaveFormat? _waveFormat;
        private bool _isRecording;
        private string? _selectedDeviceId;
        
        // Gestión de Búfer en Disco
        private readonly string _bufferFolder;
        private FileStream? _currentChunkStream;
        private string? _currentChunkPath;
        private readonly List<string> _chunks = new List<string>();
        private readonly object _lock = new object();        

        public MicrophoneRecorder(AppSettings settings)
        {
            _settings = settings;
            // Usar una carpeta separada para el buffer del micrófono
            _bufferFolder = Path.Combine(_settings.Paths.Cache, "mic");
        }

        /// <summary>
        /// Inicia la grabación del micrófono.
        /// </summary>
        /// <param name="outputFilePath">Ruta del archivo de salida (inicialmente contendrá datos RAW).</param>
        /// <returns><c>true</c> si inició correctamente; <c>false</c> si falló.</returns>
        public bool Start(string outputFilePath)
        {
            if (_isRecording) return true;
            if (!_settings.Audio.EnableMicrophone) return false;

            _selectedDeviceId = _settings.Audio.SelectedMicrophone;
            _currentChunkPath = outputFilePath;

            try 
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);
                
                // Inicializar Captura
                if (string.IsNullOrEmpty(_selectedDeviceId))
                {
                    _capture = new WasapiCapture();
                }
                else
                {
                     try
                     {
                        var enumerator = new MMDeviceEnumerator();
                        var device = enumerator.GetDevice(_selectedDeviceId);
                        _capture = new WasapiCapture(device);
                     }
                     catch
                     {
                        // Fallback al dispositivo por defecto
                        _capture = new WasapiCapture();
                     }
                }

                if (_capture == null) return false;

                _waveFormat = _capture.WaveFormat;
                
                // Abrir stream directo al archivo
                _currentChunkStream = new FileStream(_currentChunkPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                
                // NOTA SOBRE FORMATO:
                // WasapiCapture entrega datos RAW (Float o PCM) sin encabezado WAV.
                // Escribimos estos datos crudos directamente al archivo. 
                // Posteriormente, en FinalizeRecordingAsync, convertimos este RAW a un WAV válido con encabezados
                // utilizando FFmpeg, para que pueda ser procesado correctamente en la mezcla final.
                
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;

                _capture.StartRecording();
                _isRecording = true;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting mic recording: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Detiene la grabación y cierra el flujo de archivo.
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
        /// Callback ejecutado cuando hay datos de audio disponibles.
        /// Aplica los efectos de audio (Ganancia, Noise Gate) antes de escribir al disco.
        /// </summary>
        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0) return;

            byte[] processedBuffer = e.Buffer;
            int bytesRecorded = e.BytesRecorded;

            // Obtener configuración de efectos
            double gainDB = _settings.Audio.MicrophoneGainDB;
            double noiseGateDB = _settings.Audio.NoiseGateDB;

            // Aplicar procesamiento si es necesario
            if (gainDB != 0 || noiseGateDB != 0)
            {
                processedBuffer = ProcessAudioBuffer(e.Buffer, e.BytesRecorded, gainDB, noiseGateDB);
            }

            lock (_lock)
            {
                if (_currentChunkStream != null)
                {
                    _currentChunkStream.Write(processedBuffer, 0, bytesRecorded);
                }
            }
        }

        /// <summary>
        /// Procesa el búfer de audio aplicando Noise Gate y Ganancia.
        /// </summary>
        /// <param name="buffer">Datos de audio crudos.</param>
        /// <param name="bytesRecorded">Cantidad de bytes grabados.</param>
        /// <param name="gainDB">Ganancia en decibelios.</param>
        /// <param name="noiseGateDB">Umbral de Noise Gate en decibelios.</param>
        /// <returns>Búfer de audio procesado.</returns>
        private byte[] ProcessAudioBuffer(byte[] buffer, int bytesRecorded, double gainDB, double noiseGateDB)
        {
            // Clonar buffer para no modificar el original del evento (buena práctica)
            byte[] result = new byte[bytesRecorded];
            Array.Copy(buffer, result, bytesRecorded);

            if (_waveFormat == null) return result;

            // Calcular nivel RMS (root mean square) del búfer RAW (antes de ganancia)
            // Esto asegura que la Noise Gate actúe sobre la señal real de entrada.
            double rawRmsLevel = CalculateRmsLevel(result, bytesRecorded);

            // Cálculo del umbral de la Noise Gate (INVERTIDO para comportamiento intuitivo):
            // Slider bajo = poco filtrado (umbral bajo), Slider alto = mucho filtrado (umbral alto)
            double noiseGateThreshold = 0.0;
            if (noiseGateDB != 0)
            {
                double effectiveDB = -60.0 - noiseGateDB;
                noiseGateThreshold = Math.Pow(10, effectiveDB / 20.0);
            }
            
            // Si el nivel RMS está por debajo del umbral, se cierra la Noise Gate (silencio)
            bool gateOpen = (noiseGateThreshold == 0) || (rawRmsLevel >= noiseGateThreshold);

            // Calcular multiplicador de ganancia: 10^(dB/20)
            double gainMultiplier = gainDB != 0 ? Math.Pow(10, gainDB / 20.0) : 1.0;

            if (_waveFormat.Encoding == WaveFormatEncoding.IeeeFloat && _waveFormat.BitsPerSample == 32)
            {
                for (int i = 0; i < bytesRecorded; i += 4)
                {
                    float sample;
                    
                    if (!gateOpen)
                    {
                        // Noise gate cerrada: Silencio total
                        sample = 0f;
                    }
                    else
                    {
                        // Noise gate abierta: Pasar audio aplicando ganancia
                        sample = BitConverter.ToSingle(result, i);
                        if (gainMultiplier != 1.0)
                        {
                            sample = (float)(sample * gainMultiplier);
                        }
                        // Limitar (Clamp) para evitar desbordamiento
                        sample = Math.Max(-1f, Math.Min(1f, sample));
                    }
                    
                    byte[] bytes = BitConverter.GetBytes(sample);
                    Array.Copy(bytes, 0, result, i, 4);
                }
            }
            else if (_waveFormat.Encoding == WaveFormatEncoding.Pcm && _waveFormat.BitsPerSample == 16)
            {
                for (int i = 0; i < bytesRecorded; i += 2)
                {
                    short sample;
                    
                    if (!gateOpen)
                    {
                        // Noise gate cerrada: Silencio
                        sample = 0;
                    }
                    else
                    {
                        // Noise gate abierta: Pasar audio con ganancia
                        sample = BitConverter.ToInt16(result, i);
                        if (gainMultiplier != 1.0)
                        {
                            double amplified = sample * gainMultiplier;
                            sample = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, amplified));
                        }
                    }
                    
                    byte[] bytes = BitConverter.GetBytes(sample);
                    Array.Copy(bytes, 0, result, i, 2);
                }
            }

            return result;
        }

        private double CalculateRmsLevel(byte[] buffer, int bytesRecorded)
        {
            if (_waveFormat == null) return 0;

            double sumSquares = 0;
            int sampleCount = 0;

            if (_waveFormat.Encoding == WaveFormatEncoding.IeeeFloat && _waveFormat.BitsPerSample == 32)
            {
                for (int i = 0; i < bytesRecorded; i += 4)
                {
                    float sample = BitConverter.ToSingle(buffer, i);
                    sumSquares += sample * sample;
                    sampleCount++;
                }
            }
            else if (_waveFormat.Encoding == WaveFormatEncoding.Pcm && _waveFormat.BitsPerSample == 16)
            {
                for (int i = 0; i < bytesRecorded; i += 2)
                {
                    short sample = BitConverter.ToInt16(buffer, i);
                    double normalized = sample / 32768.0;
                    sumSquares += normalized * normalized;
                    sampleCount++;
                }
            }

            if (sampleCount == 0) return 0;
            return Math.Sqrt(sumSquares / sampleCount);
        }
        
        /// <summary>
        /// Finaliza la grabación convirtiendo el archivo RAW temporal a un archivo WAV válido.
        /// <para>
        /// Es necesario porque FFmpeg detecta mejor el formato si el archivo tiene encabezados WAV correctos,
        /// especialmente para la etapa posterior de mezcla (Merge).
        /// </para>
        /// </summary>
        public async Task FinalizeRecordingAsync()
        {
             // Convierte el archivo RAW actual a WAV in-place (o lo reemplaza)
             if (_currentChunkPath == null || !File.Exists(_currentChunkPath)) return;
             
             try
             {
                 string rawPath = _currentChunkPath;
                 string wavPath = Path.ChangeExtension(rawPath, ".wav");
                 
                 // Renombrar el archivo actual (que contiene datos RAW pero extensión .wav o similar) a .tmp.raw
                 // para procesarlo y generar el verdadero WAV.
                 string tempRaw = rawPath + ".tmp.raw";
                 if (File.Exists(tempRaw)) File.Delete(tempRaw); // Prevenir colisiones
                 
                 File.Move(rawPath, tempRaw);
                 
                 // Convertir RAW -> WAV
                 await ConvertRawToWavAsync(tempRaw, wavPath);
                 
                 if (File.Exists(wavPath))
                 {
                     try { File.Delete(tempRaw); } catch { }
                 }
                 else
                 {
                     // Si falló, restaurar el archivo original
                     File.Move(tempRaw, rawPath);
                 }
             }
             catch (Exception ex)
             {
                 System.Diagnostics.Debug.WriteLine($"Error finalizing mic recording: {ex.Message}");
             }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            _capture?.Dispose();
            _capture = null;
            lock (_lock)
            {
                _currentChunkStream?.Dispose();
                _currentChunkStream = null;
            }
        }

        /// <summary>
        /// Utiliza FFmpeg para encapsular los datos PCM crudos en un contenedor WAV.
        /// </summary>
        private async Task ConvertRawToWavAsync(string inputFile, string outputFile)
        {
             try
             {
                 string ffmpegPath = FFmpegHelper.GetFFmpegPath();
                 string pcmFormat = GetFFmpegPcmFormat(_waveFormat!);
                 string sampleRate = _waveFormat!.SampleRate.ToString();
                 string channels = _waveFormat!.Channels.ToString();
                 
                 // Comando: Input RAW -> Output WAV (pcm_s16le)
                 string args = $"-y -f {pcmFormat} -ar {sampleRate} -ac {channels} -i \"{inputFile}\" -c:a pcm_s16le \"{outputFile}\"";
                 
                 var p = Process.Start(new ProcessStartInfo
                 {
                     FileName = ffmpegPath,
                     Arguments = args,
                     UseShellExecute = false,
                     CreateNoWindow = true
                 });
                 
                 if (p != null)
                 {
                     await p.WaitForExitAsync();
                 }
             }
             catch { }
        }

        private string GetFFmpegPcmFormat(WaveFormat format)
        {
            if (format.Encoding == WaveFormatEncoding.IeeeFloat) return "f32le";
            if (format.Encoding == WaveFormatEncoding.Pcm)
            {
                switch (format.BitsPerSample)
                {
                    case 16: return "s16le";
                    case 24: return "s24le";
                    case 32: return "s32le";
                }
            }
            return "s16le";
        }

        public void Dispose()
        {
            Stop();
            try
            {
                if (_currentChunkPath != null && File.Exists(_currentChunkPath))
                {
                    // La limpieza puede ser manejada por el RecordingService, pero aquí aseguramos recursos libres.
                }
            }
            catch { }
        }
    }
}
