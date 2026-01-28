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
        public MicrophoneRecorder(AppSettings settings)
        {
            _settings = settings;
            // Use a separate folder for microphone buffer
            _bufferFolder = Path.Combine(_settings.Paths.Cache, "mic");
        }

        public bool Start(string outputFilePath)
        {
            if (_isRecording) return true;
            if (!_settings.Audio.EnableMicrophone) return false;

            _selectedDeviceId = _settings.Audio.SelectedMicrophone;
            _currentChunkPath = outputFilePath;

            try 
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);
                
                // Initialize Capture
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
                        // Fallback to default
                        _capture = new WasapiCapture();
                     }
                }

                if (_capture == null) return false;

                _waveFormat = _capture.WaveFormat;
                
                // Direct stream
                _currentChunkStream = new FileStream(_currentChunkPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                
                // Write WAV Header placeholder (44 bytes) for Mic if we want standard WAV, 
                // BUT WasapiCapture gives RAW data. 
                // Since our merge logic expects inputs, maybe simpler to just write raw and let merge handle it if we know format?
                // Actually, WasapiCapture is float or PCM.
                // Let's stick to RAW and we already handle raw->wav conversion in Finalize or Merge?
                // The RecordingService expects this to produce a File. 
                // Since this class is simple, let's just write raw data 
                // AND since RecordingService.MergeFiles expects inputs, raw files work if we specify parameters.
                // However, MergeFiles in RecordingService uses simple "-i file". Inputting raw data with just "-i" often fails without -f s16le etc.
                // So we SHOULD probably produce a WAV header or convert it later.
                // Let's write RAW and let the caller or finalizer handle it. 
                // BUT wait, RecordingService merges "finalAudio" (converted to WAV) for Desktop Audio,
                // but for Mic it takes `_currentMicFile` directly. 
                // So `_currentMicFile` MUST be a valid container OR we need to convert it.
                // I will update this class to just Write RAW, and I will manually add a method "StopAndConvert" or similar?
                // Actually, `RecordingService` does NOT convert Mic file currently. It passes it to MergeFiles.
                // MergeFiles uses "-i {mic}". If it's raw, it might fail.
                // I should replicate the "FinalizeRecording" pattern from AudioRecorder here.
                
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

            byte[] processedBuffer = e.Buffer;
            int bytesRecorded = e.BytesRecorded;

            // Apply gain and noise gate if configured
            double gainDB = _settings.Audio.MicrophoneGainDB;
            double noiseGateDB = _settings.Audio.NoiseGateDB;

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

        private byte[] ProcessAudioBuffer(byte[] buffer, int bytesRecorded, double gainDB, double noiseGateDB)
        {
            // Clone buffer to avoid modifying original
            byte[] result = new byte[bytesRecorded];
            Array.Copy(buffer, result, bytesRecorded);

            if (_waveFormat == null) return result;

            // Calculate RMS (root mean square) level of the RAW buffer (before gain)
            // This ensures we are gating based on the actual input level, so increasing gain
            // doesn't "break" the noise gate by amplifying noise above the threshold.
            double rawRmsLevel = CalculateRmsLevel(result, bytesRecorded);

            // Noise gate threshold calculation (INVERTED for intuitive behavior):
            // Slider low = little filtering, slider high = much filtering
            double noiseGateThreshold = 0.0;
            if (noiseGateDB != 0)
            {
                double effectiveDB = -60.0 - noiseGateDB;
                noiseGateThreshold = Math.Pow(10, effectiveDB / 20.0);
            }
            
            // If RAW RMS level is below threshold, silence the entire buffer (gate is closed)
            bool gateOpen = (noiseGateThreshold == 0) || (rawRmsLevel >= noiseGateThreshold);

            // Calculate gain multiplier: 10^(dB/20)
            double gainMultiplier = gainDB != 0 ? Math.Pow(10, gainDB / 20.0) : 1.0;

            if (_waveFormat.Encoding == WaveFormatEncoding.IeeeFloat && _waveFormat.BitsPerSample == 32)
            {
                for (int i = 0; i < bytesRecorded; i += 4)
                {
                    float sample;
                    
                    if (!gateOpen)
                    {
                        // Gate closed: silence
                        sample = 0f;
                    }
                    else
                    {
                        // Gate open: pass audio through with gain applied
                        sample = BitConverter.ToSingle(result, i);
                        if (gainMultiplier != 1.0)
                        {
                            sample = (float)(sample * gainMultiplier);
                        }
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
                        // Gate closed: silence
                        sample = 0;
                    }
                    else
                    {
                        // Gate open: pass audio through with gain applied
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
        
        // This is necessary because the recorded file is RAW. We need valid WAV for FFmpeg auto-detection to work best in MergeFiles.
        // Or we convert it explicitly.
        public void FinalizeRecording()
        {
             // This converts the current RAW file to WAV in-place (or replaces it)
             if (_currentChunkPath == null || !File.Exists(_currentChunkPath)) return;
             
             try
             {
                 string rawPath = _currentChunkPath;
                 string wavPath = Path.ChangeExtension(rawPath, ".wav");
                 
                 // If the file extension was already wav, we still need to fix headers if it was raw.
                 // In Start(), we used whatever path was given. RecordingService gives "rec_mic_... .wav".
                 // But we wrote RAW data to it. So it has .wav extension but no header.
                 // We should rename it to .raw and then convert to .wav
                 
                 string tempRaw = rawPath + ".tmp.raw";
                 File.Move(rawPath, tempRaw);
                 
                 ConvertRawToWav(tempRaw, wavPath);
                 
                 if (File.Exists(wavPath))
                 {
                     try { File.Delete(tempRaw); } catch { }
                 }
                 else
                 {
                     // Failed? Restore
                     File.Move(tempRaw, rawPath);
                 }
             }
             catch { }
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

        private void ConvertRawToWav(string inputFile, string outputFile)
        {
             try
             {
                 string ffmpegPath = FFmpegHelper.GetFFmpegPath();
                 string pcmFormat = GetFFmpegPcmFormat(_waveFormat!);
                 string sampleRate = _waveFormat!.SampleRate.ToString();
                 string channels = _waveFormat!.Channels.ToString();
                 
                 // Raw to proper WAV
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
                if (_currentChunkPath != null && File.Exists(_currentChunkPath))
                {
                    // Clean up if needed? 
                    // Usually RecordingService manages lifecycle of this file now.
                }
            }
            catch { }
        }
    }
}
