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
    public class AudioRecorder : IDisposable
    {
        private readonly AppSettings _settings;
        private WasapiLoopbackCapture? _capture;
        private WaveFormat? _waveFormat;
        private bool _isRecording;
        
        // Disk Buffer
        private readonly string _bufferFolder;
        private readonly string _bufferRootPath; // Ruta raíz del buffer para actualizar la reserva
        private FileStream? _currentChunkStream;
        private string? _currentChunkPath;
        private readonly List<string> _chunks = new List<string>();
        private readonly object _lock = new object();
        private int _bytesPerChunk;
        private long _maxBytesTotal;
        private long _currentTotalBytes;
        private long _lastReservationUpdateSize = 0; // Último tamaño cuando se actualizó la reserva
        private const long RESERVATION_UPDATE_THRESHOLD = 100 * 1024 * 1024; // 100MB

        public AudioRecorder(AppSettings settings)
        {
            _settings = settings;
            _bufferRootPath = _settings.Paths.TempBuffer;
            _bufferFolder = Path.Combine(_bufferRootPath, "audio");
        }

        public bool Start()
        {
            if (_isRecording) return true;

            try 
            {
                Directory.CreateDirectory(_bufferFolder);
                // Clean old buffer files
                foreach (var file in Directory.GetFiles(_bufferFolder, "*.raw"))
                {
                    try { File.Delete(file); } catch { }
                }
                _chunks.Clear();
                _currentTotalBytes = 0;

                // Capture system audio (loopback)
                // NOTE: This requires a valid audio device to be active
                try 
                {
                    _capture = new WasapiLoopbackCapture();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to initialize WasapiLoopbackCapture: {ex.Message}");
                    // If loopback fails (e.g. no audio playing), we can't record audio
                    // But we shouldn't crash the app.
                    return false;
                }

                if (_capture == null) return false;

                _waveFormat = _capture.WaveFormat;

                // Calculate buffer settings
                int bytesPerSecond = _waveFormat.AverageBytesPerSecond;
                _bytesPerChunk = bytesPerSecond * 30; // 30 seconds per chunk (igual que video)
                _maxBytesTotal = _settings.Buffer.MaxBufferBytes / 2; // Mitad del buffer para audio

                StartNewChunk();

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

        public void Stop()
        {
            if (!_isRecording) return;

            _capture?.StopRecording();
            _isRecording = false;
            
            lock (_lock)
            {
                _currentChunkStream?.Dispose();
                _currentChunkStream = null;
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0) return;

            lock (_lock)
            {
                if (_currentChunkStream != null)
                {
                    _currentChunkStream.Write(e.Buffer, 0, e.BytesRecorded);
                    
                    if (_currentChunkStream.Length >= _bytesPerChunk)
                    {
                        StartNewChunk();
                    }
                }
            }
        }

        private void StartNewChunk()
        {
            if (_currentChunkStream != null)
            {
                long length = _currentChunkStream.Length;
                _currentChunkStream.Flush();
                _currentChunkStream.Dispose();
                
                if (_currentChunkPath != null)
                {
                    _chunks.Add(_currentChunkPath);
                    _currentTotalBytes += length;
                }
            }

            // Limpieza: mantener máximo 7 chunks (3 minutos 30 segundos = 210 segundos / 30 segundos por chunk)
            // Una vez alcanzado este límite, eliminar el más antiguo
            int maxChunksToKeep = 7;
            
            while (_chunks.Count >= maxChunksToKeep)
            {
                var oldChunk = _chunks[0];
                _chunks.RemoveAt(0);
                try 
                { 
                    var fi = new FileInfo(oldChunk);
                    if (fi.Exists)
                    {
                        _currentTotalBytes -= fi.Length;
                        fi.Delete();
                        Debug.WriteLine($"AudioRecorder: Chunk eliminado (límite de cantidad): {Path.GetFileName(oldChunk)}");
                    }
                } 
                catch { /* Ignore if in use */ }
            }

            // Limpieza adicional por tamaño total si excede el límite
            while (_currentTotalBytes > _maxBytesTotal && _chunks.Count > 0)
            {
                var oldChunk = _chunks[0];
                _chunks.RemoveAt(0);
                try 
                { 
                    var fi = new FileInfo(oldChunk);
                    if (fi.Exists)
                    {
                        _currentTotalBytes -= fi.Length;
                        fi.Delete();
                        Debug.WriteLine($"AudioRecorder: Chunk eliminado (límite de tamaño): {Path.GetFileName(oldChunk)}");
                    }
                } 
                catch { /* Ignore if in use */ }
            }

            // Actualizar la reserva de espacio en disco dinámicamente (solo cada 100MB)
            try
            {
                long currentBufferSize = Storage.DiskSpaceReservation.CalculateBufferSize(_bufferRootPath);
                if (Math.Abs(currentBufferSize - _lastReservationUpdateSize) >= RESERVATION_UPDATE_THRESHOLD)
                {
                    Storage.DiskSpaceReservation.UpdateReservation(_bufferRootPath, _settings.Buffer.MaxBufferBytes);
                    _lastReservationUpdateSize = currentBufferSize;
                    Debug.WriteLine($"AudioRecorder: Reserva actualizada. Buffer: {currentBufferSize / 1024 / 1024}MB");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AudioRecorder: Error al actualizar reserva: {ex.Message}");
            }

            _currentChunkPath = Path.Combine(_bufferFolder, $"audio_{DateTime.Now.Ticks}.raw");
            _currentChunkStream = new FileStream(_currentChunkPath, FileMode.Create, FileAccess.Write, FileShare.Read);
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

        public string? SaveClip(int durationSeconds, string outputFolder)
        {
            if (!_isRecording || _waveFormat == null) 
            {
                throw new InvalidOperationException("El grabador de audio no está activo o no se ha inicializado correctamente.");
            }

            string? tempRawFile = null;
            string? trimmedRawFile = null;
            try
            {
                List<string> filesToProcess;
                lock (_lock)
                {
                    if (_currentChunkStream != null) _currentChunkStream.Flush();
                    
                    // Calculate how many chunks we need (each chunk is 30 seconds)
                    int chunksNeeded = (int)Math.Ceiling(durationSeconds / 30.0);
                    
                    // Take LAST N chunks (most recent) instead of all chunks
                    filesToProcess = _chunks.TakeLast(chunksNeeded).ToList();
                    if (_currentChunkPath != null) filesToProcess.Add(_currentChunkPath);
                }

                if (filesToProcess.Count == 0) 
                {
                    throw new InvalidOperationException("No hay datos de audio en el buffer. Espera unos segundos después de activar la grabación.");
                }

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
                tempRawFile = Path.Combine(outputFolder, $"temp_audio_full_{timestamp}.raw");
                trimmedRawFile = Path.Combine(outputFolder, $"temp_audio_trimmed_{timestamp}.raw");

                // Concatenate all chunks
                using (var outputStream = new FileStream(tempRawFile, FileMode.Create))
                {
                    foreach (var file in filesToProcess)
                    {
                        try
                        {
                            using (var inputStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            {
                                inputStream.CopyTo(outputStream);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Skipping chunk {file}: {ex.Message}");
                        }
                    }
                }

                long fullFileSize = new FileInfo(tempRawFile).Length;
                if (fullFileSize == 0)
                {
                    throw new InvalidOperationException("El archivo de audio está vacío. No se pudo capturar audio del sistema.");
                }

                // Calculate bytes to extract
                int bytesPerSample = _waveFormat.BitsPerSample / 8;
                int bytesPerSecond = _waveFormat.SampleRate * _waveFormat.Channels * bytesPerSample;
                long bytesToExtract = (long)durationSeconds * bytesPerSecond;
                long startPosition = Math.Max(0, fullFileSize - bytesToExtract);

                System.Diagnostics.Debug.WriteLine($"Audio: fileSize={fullFileSize}, bytesPerSec={bytesPerSecond}, extracting {bytesToExtract} bytes from position {startPosition}");

                // Extract last N seconds by copying bytes directly
                using (var inputStream = new FileStream(tempRawFile, FileMode.Open, FileAccess.Read))
                using (var outputStream = new FileStream(trimmedRawFile, FileMode.Create))
                {
                    inputStream.Seek(startPosition, SeekOrigin.Begin);
                    inputStream.CopyTo(outputStream);
                }

                string format = _settings.Audio.Format.ToLower();
                string extension = format; // mp3 or flac
                string outputFile = Path.Combine(outputFolder, $"clip_{timestamp}.{extension}");

                ConvertRawToOutput(trimmedRawFile, outputFile, format);
                
                if (File.Exists(outputFile))
                {
                    return outputFile;
                }
                
                throw new InvalidOperationException("FFmpeg no pudo crear el archivo de salida.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving audio clip: {ex.Message}");
                throw;
            }
            finally
            {
                if (tempRawFile != null)
                {
                    try { File.Delete(tempRawFile); } catch { }
                }
                if (trimmedRawFile != null)
                {
                    try { File.Delete(trimmedRawFile); } catch { }
                }
            }
        }

        private void ConvertRawToOutput(string inputFile, string outputFile, string format)
        {
             try
             {
                 string ffmpegPath = FFmpegHelper.GetFFmpegPath();
                 string pcmFormat = GetFFmpegPcmFormat(_waveFormat!);
                 string sampleRate = _waveFormat!.SampleRate.ToString();
                 string channels = _waveFormat!.Channels.ToString();
                 
                 // Codec selection based on format
                 string codecArgs;
                 switch (format.ToLower())
                 {
                     case "mp3":
                         codecArgs = $"-c:a libmp3lame -b:a {_settings.Audio.Bitrate}k";
                         break;
                     case "flac":
                         codecArgs = "-c:a flac -compression_level 5"; // FLAC lossless
                         break;
                     default:
                         codecArgs = "-c:a libmp3lame -b:a 192k"; // Fallback to MP3
                         break;
                 }

                 // Simple conversion without seeking
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
                     string errors = p.StandardError.ReadToEnd();
                     p.WaitForExit();
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
            
            // Limpiar todos los chunks al cerrar
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
