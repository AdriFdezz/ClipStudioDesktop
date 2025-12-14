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
        private FileStream? _currentChunkStream;
        private string? _currentChunkPath;
        private readonly List<string> _chunks = new List<string>();
        private readonly object _lock = new object();
        private int _bytesPerChunk;
        private long _maxBytesTotal;
        private long _currentTotalBytes;

        public AudioRecorder(AppSettings settings)
        {
            _settings = settings;
            _bufferFolder = Path.Combine(_settings.Paths.TempBuffer, "audio");
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
                _bytesPerChunk = bytesPerSecond * 10; // 10 seconds per chunk
                _maxBytesTotal = (long)bytesPerSecond * _settings.Buffer.MaxDurationSeconds;

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

            // Prune old chunks
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
                    }
                } 
                catch { /* Ignore if in use */ }
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
            if (!_isRecording || _waveFormat == null) return null;

            string? tempRawFile = null;
            try
            {
                List<string> filesToProcess;
                lock (_lock)
                {
                    if (_currentChunkStream != null) _currentChunkStream.Flush();
                    filesToProcess = new List<string>(_chunks);
                    if (_currentChunkPath != null) filesToProcess.Add(_currentChunkPath);
                }

                if (filesToProcess.Count == 0) return null;

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
                tempRawFile = Path.Combine(outputFolder, $"temp_audio_{timestamp}.raw");

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
                        catch { /* Ignore missing files */ }
                    }
                }

                string format = _settings.Audio.Format.ToLower();
                string extension = format == "mp3" ? "mp3" : "wav";
                string outputFile = Path.Combine(outputFolder, $"clip_{timestamp}.{extension}");

                ConvertRawToOutput(tempRawFile, outputFile, durationSeconds, format);
                
                if (File.Exists(outputFile))
                {
                    return outputFile;
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving audio clip: {ex.Message}");
                return null;
            }
            finally
            {
                if (tempRawFile != null)
                {
                    try { File.Delete(tempRawFile); } catch { }
                }
            }
        }

        private void ConvertRawToOutput(string inputFile, string outputFile, int durationSeconds, string format)
        {
             string ffmpegPath = FFmpegHelper.GetFFmpegPath();
             string pcmFormat = GetFFmpegPcmFormat(_waveFormat!);
             string sampleRate = _waveFormat!.SampleRate.ToString();
             string channels = _waveFormat!.Channels.ToString();
             
             string codecArgs = format == "mp3" 
                ? $"-c:a libmp3lame -b:a {_settings.Audio.Bitrate}k" 
                : "-c:a pcm_s16le"; // WAV

             // Input: Raw PCM
             // Output: Trimmed last N seconds
             string args = $"-y -f {pcmFormat} -ar {sampleRate} -ac {channels} -i \"{inputFile}\" " +
                           $"-sseof -{durationSeconds} {codecArgs} \"{outputFile}\"";
             
             var p = Process.Start(new ProcessStartInfo
             {
                 FileName = ffmpegPath,
                 Arguments = args,
                 UseShellExecute = false,
                 CreateNoWindow = true
             });
             p?.WaitForExit();
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
        }
    }
}
