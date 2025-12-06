using ClipStudioDesktop.Helpers;
using ClipStudioDesktop.Models;
using ClipStudioDesktop.Services.Video;
using NAudio.Wave;
using System;
using System.Diagnostics;
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

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string tempWavFile = Path.Combine(outputFolder, $"temp_audio_{timestamp}.wav");

                using (var writer = new WaveFileWriter(tempWavFile, _waveFormat))
                {
                    writer.Write(audioData, 0, audioData.Length);
                }

                string format = _settings.Audio.Format.ToLower();
                if (format == "mp3")
                {
                    string mp3File = Path.Combine(outputFolder, $"clip_{timestamp}.mp3");
                    ConvertToMp3(tempWavFile, mp3File);
                    try { File.Delete(tempWavFile); } catch { }
                    return mp3File;
                }
                else
                {
                    string wavFile = Path.Combine(outputFolder, $"clip_{timestamp}.wav");
                    File.Move(tempWavFile, wavFile);
                    return wavFile;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving audio clip: {ex.Message}");
                return null;
            }
        }

        private void ConvertToMp3(string inputFile, string outputFile)
        {
             string ffmpegPath = FFmpegHelper.GetFFmpegPath();
             string bitrate = $"{_settings.Audio.Bitrate}k";
             string args = $"-y -i \"{inputFile}\" -c:a libmp3lame -b:a {bitrate} \"{outputFile}\"";
             
             var p = Process.Start(new ProcessStartInfo
             {
                 FileName = ffmpegPath,
                 Arguments = args,
                 UseShellExecute = false,
                 CreateNoWindow = true
             });
             p.WaitForExit();
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
