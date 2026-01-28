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
        public WaveFormat? WaveFormat => _waveFormat;
        private WaveFormat? _waveFormat;
        private bool _isRecording;
        
        // Disk Buffer
        private readonly string _bufferFolder;
        private readonly string _bufferRootPath; // Ruta raíz del buffer para actualizar la reserva
        private FileStream? _currentChunkStream;
        private string? _currentChunkPath;
        private readonly List<string> _chunks = new List<string>();
        private readonly object _lock = new object();


        private long _currentTotalBytes;
        
        public event Action<byte[], int>? AudioDataAvailable;


        public AudioRecorder(AppSettings settings)
        {
            _settings = settings;
            _bufferRootPath = _settings.Paths.Cache;
            _bufferFolder = Path.Combine(_bufferRootPath, "audio");
        }

        public bool Start(string? outputFilePath)
        {
            if (_isRecording) return true;


            try 
            {
                Directory.CreateDirectory(_bufferFolder);
                _currentTotalBytes = 0;
                _currentChunkPath = outputFilePath;


                try 
                {
                    _capture = new WasapiLoopbackCapture();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to initialize WasapiLoopbackCapture: {ex.Message}");
                    return false;
                }

                if (_capture == null) return false;

                _waveFormat = _capture.WaveFormat;
                
                // Direct stream to the output file
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

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0) return;

                if (_currentChunkStream != null)
                {
                    _currentChunkStream.Write(e.Buffer, 0, e.BytesRecorded);
                    _currentTotalBytes += e.BytesRecorded;
                }
                
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
        
        // Helper to convert the raw PCM/WAV we just recorded to final format if needed
        // Assuming we record RAW PCM or WAV headerless, we might need to finalize it.
        // NAudio WasapiLoopbackCapture gives raw PCM in DataAvailable.
        // If we write directly to .wav, we need a header. 
        // For now, let's stick to writing raw samples and then converting with FFmpeg as before, 
        // OR better: write a proper WAV file using WaveFileWriter if possible, but we are manually writing stream.
        // Let's keep the raw writing and reuse ConvertRawToOutput logic which is robust.
        
        public string? FinalizeRecording(string finalOutputFolder, string format)
        {
             if (_currentChunkPath == null || !File.Exists(_currentChunkPath)) return null;

             try
             {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string extension = format.ToLower() == "wav" ? "wav" : "mp3";
                string outputFile = Path.Combine(finalOutputFolder, $"recording_audio_{timestamp}.{extension}");

                ConvertRawToOutput(_currentChunkPath, outputFile, format);
                
                // Cleanup temp raw file
                try { File.Delete(_currentChunkPath); } catch { }
                
                return outputFile;
             }
             catch (Exception ex)
             {
                 Debug.WriteLine($"Error finalizing audio: {ex.Message}");
                 return null;
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
