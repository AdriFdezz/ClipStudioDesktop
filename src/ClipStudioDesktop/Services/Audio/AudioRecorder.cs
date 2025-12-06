using ClipStudioDesktop.Helpers;
using ClipStudioDesktop.Models;
using NAudio.Wave;
using System;
using System.IO;

namespace ClipStudioDesktop.Services.Audio
{
    public class AudioRecorder : IDisposable
    {
        private readonly AppSettings _settings;
        private WasapiLoopbackCapture? _capture;
        private CircularBuffer? _buffer;
        private WaveFormat? _waveFormat;
        private bool _isRecording;

        public AudioRecorder(AppSettings settings)
        {
            _settings = settings;
        }

        public void Start()
        {
            if (_isRecording) return;

            try 
            {
                // Capture system audio (loopback)
                _capture = new WasapiLoopbackCapture();
                _waveFormat = _capture.WaveFormat;

                // Calculate buffer size for max duration
                // Bytes per second = SampleRate * Channels * (BitsPerSample / 8)
                int bytesPerSecond = _waveFormat.AverageBytesPerSecond;
                int bufferSize = bytesPerSecond * _settings.Buffer.MaxDurationSeconds;

                _buffer = new CircularBuffer(bufferSize);

                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;

                _capture.StartRecording();
                _isRecording = true;
            }
            catch (Exception ex)
            {
                // Handle initialization error (e.g. no audio device)
                System.Diagnostics.Debug.WriteLine($"Error starting audio recording: {ex.Message}");
            }
        }

        public void Stop()
        {
            if (!_isRecording) return;

            _capture?.StopRecording();
            _isRecording = false;
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            _buffer?.Write(e.Buffer, 0, e.BytesRecorded);
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            _capture?.Dispose();
            _capture = null;
        }

        public string? SaveClip(int durationSeconds, string outputFolder)
        {
            if (!_isRecording || _buffer == null || _waveFormat == null) return null;

            try
            {
                int bytesPerSecond = _waveFormat.AverageBytesPerSecond;
                int bytesToRead = bytesPerSecond * durationSeconds;

                byte[] audioData = _buffer.ReadLatest(bytesToRead);

                if (audioData.Length == 0) return null;

                string fileName = $"clip_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.wav";
                string filePath = Path.Combine(outputFolder, fileName);

                using (var writer = new WaveFileWriter(filePath, _waveFormat))
                {
                    writer.Write(audioData, 0, audioData.Length);
                }

                return filePath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving audio clip: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
