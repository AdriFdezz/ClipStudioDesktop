using ClipStudioDesktop.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;

namespace ClipStudioDesktop.Services.Audio
{
    /// <summary>
    /// Service for monitoring microphone input in real-time.
    /// Provides audio playback (hear yourself) and level metering for VU display.
    /// </summary>
    public class MicrophoneMonitor : IDisposable
    {
        private readonly AppSettings _settings;
        private WasapiCapture? _capture;
        private WaveOutEvent? _waveOut;
        private BufferedWaveProvider? _bufferedProvider;
        private bool _isMonitoring;
        private double _currentLevel;
        private readonly object _levelLock = new object();

        public event Action<double>? LevelChanged;

        public double CurrentLevel
        {
            get { lock (_levelLock) return _currentLevel; }
            private set
            {
                lock (_levelLock) _currentLevel = value;
                LevelChanged?.Invoke(value);
            }
        }

        public bool IsMonitoring => _isMonitoring;

        public MicrophoneMonitor(AppSettings settings)
        {
            _settings = settings;
        }

        public bool Start()
        {
            if (_isMonitoring) return true;

            try
            {
                // Initialize capture from selected microphone
                string? deviceId = _settings.Audio.SelectedMicrophone;
                
                if (string.IsNullOrEmpty(deviceId))
                {
                    _capture = new WasapiCapture();
                }
                else
                {
                    try
                    {
                        var enumerator = new MMDeviceEnumerator();
                        var device = enumerator.GetDevice(deviceId);
                        _capture = new WasapiCapture(device);
                    }
                    catch
                    {
                        _capture = new WasapiCapture();
                    }
                }

                if (_capture == null) return false;

                // Create buffered provider for playback
                _bufferedProvider = new BufferedWaveProvider(_capture.WaveFormat)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromMilliseconds(200)
                };

                // Setup playback
                _waveOut = new WaveOutEvent();
                _waveOut.Init(_bufferedProvider);

                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;

                _capture.StartRecording();
                _waveOut.Play();
                _isMonitoring = true;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting mic monitor: {ex.Message}");
                Stop();
                return false;
            }
        }

        public void Stop()
        {
            _isMonitoring = false;
            CurrentLevel = 0;

            try
            {
                _capture?.StopRecording();
                _waveOut?.Stop();
            }
            catch { }
            finally
            {
                _capture?.Dispose();
                _waveOut?.Dispose();
                _capture = null;
                _waveOut = null;
                _bufferedProvider = null;
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0 || !_isMonitoring || _capture?.WaveFormat == null) return;

            // Get settings
            double gainDB = _settings.Audio.MicrophoneGainDB;
            double noiseGateDB = _settings.Audio.NoiseGateDB;
            double gainMultiplier = gainDB != 0 ? Math.Pow(10, gainDB / 20.0) : 1.0;
            
            // Noise gate threshold (inverted for intuitive behavior, same as recording)
            double noiseGateThreshold = 0.0;
            if (noiseGateDB != 0)
            {
                double effectiveDB = -60.0 - noiseGateDB;
                noiseGateThreshold = Math.Pow(10, effectiveDB / 20.0);
            }

            // Process audio buffer with gain and noise gate
            byte[] processedBuffer = new byte[e.BytesRecorded];
            Array.Copy(e.Buffer, processedBuffer, e.BytesRecorded);

            // Calculate RAW RMS level (prior to gain) to decide if gate should open
            double rawLevel = CalculateRmsLevel(e.Buffer, e.BytesRecorded, _capture.WaveFormat);
            
            // Should the gate open? Based on RAW input signal vs threshold
            bool gateOpen = (noiseGateThreshold == 0) || (rawLevel >= noiseGateThreshold);
            
            // Calculate final level for display (raw * gain, if passed)
            double levelAfterGain = rawLevel * gainMultiplier;

            // Process samples based on format
            if (_capture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat && _capture.WaveFormat.BitsPerSample == 32)
            {
                for (int i = 0; i < e.BytesRecorded; i += 4)
                {
                    float sample;
                    if (!gateOpen)
                    {
                        sample = 0f;
                    }
                    else
                    {
                        sample = BitConverter.ToSingle(processedBuffer, i);
                        if (gainMultiplier != 1.0)
                            sample = (float)(sample * gainMultiplier);
                        sample = Math.Max(-1f, Math.Min(1f, sample));
                    }
                    byte[] bytes = BitConverter.GetBytes(sample);
                    Array.Copy(bytes, 0, processedBuffer, i, 4);
                }
            }
            else if (_capture.WaveFormat.Encoding == WaveFormatEncoding.Pcm && _capture.WaveFormat.BitsPerSample == 16)
            {
                for (int i = 0; i < e.BytesRecorded; i += 2)
                {
                    short sample;
                    if (!gateOpen)
                    {
                        sample = 0;
                    }
                    else
                    {
                        sample = BitConverter.ToInt16(processedBuffer, i);
                        if (gainMultiplier != 1.0)
                        {
                            double amplified = sample * gainMultiplier;
                            sample = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, amplified));
                        }
                    }
                    byte[] bytes = BitConverter.GetBytes(sample);
                    Array.Copy(bytes, 0, processedBuffer, i, 2);
                }
            }

            // Add processed audio to playback buffer
            _bufferedProvider?.AddSamples(processedBuffer, 0, e.BytesRecorded);

            // Update VU meter level (sensitivity x30 for responsive visual)
            // Note: We show the OUTPUT level (after gain and gate)
            double displayLevel = gateOpen ? levelAfterGain * 30.0 : 0;
            CurrentLevel = Math.Min(1.0, Math.Max(0.0, displayLevel));
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            // Cleanup handled in Stop()
        }

        private double CalculateRmsLevel(byte[] buffer, int bytesRecorded, WaveFormat? format)
        {
            if (format == null) return 0;

            double sumSquares = 0;
            int sampleCount = 0;

            if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
            {
                for (int i = 0; i < bytesRecorded; i += 4)
                {
                    float sample = BitConverter.ToSingle(buffer, i);
                    sumSquares += sample * sample;
                    sampleCount++;
                }
            }
            else if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
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

        public void Dispose()
        {
            Stop();
        }
    }
}
