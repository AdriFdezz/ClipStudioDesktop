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
    public class MicrophoneRecorder : IDisposable
    {
        private readonly AppSettings _settings;
        private WasapiCapture? _capture;
        private WaveFormat? _waveFormat;
        private bool _isRecording;
        private string? _selectedDeviceId;
        
        // Disk Buffer
        private readonly string _bufferFolder;
        private FileStream? _currentChunkStream;
        private string? _currentChunkPath;
        private readonly List<string> _chunks = new List<string>();
        private readonly object _lock = new object();
        private int _bytesPerChunk;
        
        public MicrophoneRecorder(AppSettings settings)
        {
            _settings = settings;
            // Use a separate folder for microphone buffer
            _bufferFolder = Path.Combine(_settings.Paths.TempBuffer, "mic");
        }

        public bool Start()
        {
            if (_isRecording) return true;
            if (!_settings.Audio.EnableMicrophone) return false;

            _selectedDeviceId = _settings.Audio.SelectedMicrophone;

            try 
            {
                Directory.CreateDirectory(_bufferFolder);
                // Clean old buffer files
                foreach (var file in Directory.GetFiles(_bufferFolder, "*.raw"))
                {
                    try { File.Delete(file); } catch { }
                }
                _chunks.Clear();

                // Initialize Capture
                if (string.IsNullOrEmpty(_selectedDeviceId))
                {
                    // Default device
                    _capture = new WasapiCapture();
                }
                else
                {
                    // Specific device by ID
                    var enumerator = new MMDeviceEnumerator();
                    var device = enumerator.GetDevice(_selectedDeviceId);
                    _capture = new WasapiCapture(device);
                }

                if (_capture == null) return false;

                _waveFormat = _capture.WaveFormat;

                // Calculate buffer settings
                int bytesPerSecond = _waveFormat.AverageBytesPerSecond;
                _bytesPerChunk = bytesPerSecond * 30; // 30 seconds per chunk

                StartNewChunk();

                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;

                _capture.StartRecording();
                _isRecording = true;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting mic recording: {ex.Message}");
                // Fallback to default if specific fails?
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
                }
            }

            // Keep max 7 chunks (similar to AudioRecorder)
            int maxChunksToKeep = 7;
            while (_chunks.Count >= maxChunksToKeep)
            {
                var oldChunk = _chunks[0];
                _chunks.RemoveAt(0);
                try { File.Delete(oldChunk); } catch { }
            }

            _currentChunkPath = Path.Combine(_bufferFolder, $"mic_{DateTime.Now.Ticks}.raw");
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
                return null; // Silent fail if mic not recording (optional feature)
            }

            string? tempRawFile = null;
            string? trimmedRawFile = null;
            try
            {
                List<string> filesToProcess;
                lock (_lock)
                {
                    if (_currentChunkStream != null) _currentChunkStream.Flush();
                    
                    int chunksNeeded = (int)Math.Ceiling(durationSeconds / 30.0);
                    filesToProcess = _chunks.TakeLast(chunksNeeded).ToList();
                    if (_currentChunkPath != null) filesToProcess.Add(_currentChunkPath);
                }

                if (filesToProcess.Count == 0) return null;

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
                tempRawFile = Path.Combine(outputFolder, $"temp_mic_full_{timestamp}.raw");
                trimmedRawFile = Path.Combine(outputFolder, $"temp_mic_trimmed_{timestamp}.raw");

                // Concatenate
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
                        catch { }
                    }
                }

                long fullFileSize = new FileInfo(tempRawFile).Length;
                if (fullFileSize == 0) return null;

                // Extract logic
                int bytesPerSample = _waveFormat.BitsPerSample / 8;
                int bytesPerSecond = _waveFormat.SampleRate * _waveFormat.Channels * bytesPerSample;
                long bytesToExtract = (long)durationSeconds * bytesPerSecond;
                long startPosition = Math.Max(0, fullFileSize - bytesToExtract);

                using (var inputStream = new FileStream(tempRawFile, FileMode.Open, FileAccess.Read))
                using (var outputStream = new FileStream(trimmedRawFile, FileMode.Create))
                {
                    inputStream.Seek(startPosition, SeekOrigin.Begin);
                    inputStream.CopyTo(outputStream);
                }

                // Output as WAV always for intermediate merging
                string outputFile = Path.Combine(outputFolder, $"mic_clip_{timestamp}.wav");
                
                ConvertRawToWav(trimmedRawFile, outputFile);
                
                return File.Exists(outputFile) ? outputFile : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving mic clip: {ex.Message}");
                return null;
            }
            finally
            {
                if (tempRawFile != null) try { File.Delete(tempRawFile); } catch { }
                if (trimmedRawFile != null) try { File.Delete(trimmedRawFile); } catch { }
            }
        }

        private void ConvertRawToWav(string inputFile, string outputFile)
        {
             try
             {
                 string ffmpegPath = FFmpegHelper.GetFFmpegPath();
                 string pcmFormat = GetFFmpegPcmFormat(_waveFormat!);
                 string sampleRate = _waveFormat!.SampleRate.ToString();
                 string channels = _waveFormat!.Channels.ToString();
                 
                 // Always export as WAV PCM for merging
                 string args = $"-y -f {pcmFormat} -ar {sampleRate} -ac {channels} -i \"{inputFile}\" -c:a pcm_s16le \"{outputFile}\"";
                 
                 var p = Process.Start(new ProcessStartInfo
                 {
                     FileName = ffmpegPath,
                     Arguments = args,
                     UseShellExecute = false,
                     CreateNoWindow = true
                 });
                 p?.WaitForExit();
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
                lock (_lock)
                {
                    foreach (var chunk in _chunks) try { File.Delete(chunk); } catch { }
                    _chunks.Clear();
                    if (_currentChunkPath != null) try { File.Delete(_currentChunkPath); } catch { }
                }
            }
            catch { }
        }
    }
}
