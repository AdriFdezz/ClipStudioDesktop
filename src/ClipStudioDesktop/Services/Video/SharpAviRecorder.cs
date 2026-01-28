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

        // Configuration
        private int _frameRate = 30; // Default
        private int _quality = 85; // Default JPEG Quality
        
        public void StartRecording(string outputPath, int fps, int quality = 85, bool recordAudio = true)
        {
            if (_isRecording) return;
            
            _outputPath = outputPath;
            _frameRate = fps;
            _quality = quality;
            
            // Clamp quality
            if (_quality < 10) _quality = 10;
            if (_quality > 100) _quality = 100;
            
            var screen = Screen.PrimaryScreen;
            if (screen == null) throw new Exception("No display detected.");
            _width = screen.Bounds.Width;
            _height = screen.Bounds.Height;
            
            // Adjust to even numbers (SharpAvi/Codecs often prefer even dimensions)
            if (_width % 2 != 0) _width--;
            if (_height % 2 != 0) _height--;

            try
            {
                // Create AVI Writer
                _writer = new AviWriter(_outputPath)
                {
                    FramesPerSecond = _frameRate,
                    EmitIndex1 = true
                };

                // Add Video Stream (Motion JPEG is fast and good quality for accumulation)
                // Using 100 quality for "RAW-like" capture (we convert later)
                _videoStream = _writer.AddVideoStream();
                _videoStream.Width = _width;
                _videoStream.Height = _height;
                _videoStream.Codec = new SharpAvi.FourCC("MJPG"); 
                _videoStream.BitsPerPixel = BitsPerPixel.Bpp24;

                // Configure Audio if requested
                if (recordAudio)
                {
                    InitializeAudioCapture();
                }

                _isRecording = true;
                _stopEvent.Reset();

                // Start Audio Capture
                _audioCapture?.StartRecording();

                // Start Video Capture Thread
                _videoThread = new Thread(VideoLoop)
                {
                    IsBackground = true,
                    Priority = ThreadPriority.Highest, // Critical for timing
                    Name = "SharpAviVideoCaptureThread"
                };
                _videoThread.Start();
                
                Debug.WriteLine($"[SharpAviRecorder] Started recording to {_outputPath} at {_frameRate}fps");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SharpAviRecorder] Error starting: {ex.Message}");
                Cleanup();
                throw; // Propagate to Service
            }
        }
        public void Stop()
        {
            if (!_isRecording) return;
            
            Debug.WriteLine("[SharpAviRecorder] Stopping...");
            _isRecording = false;
            
            // Signal video thread to stop
            _stopEvent.Set();
            _videoThread?.Join(1000); // Wait max 1s
            
            // Stop Audio
            _audioCapture?.StopRecording();
            
            // Close Writer
            Cleanup();
            Debug.WriteLine("[SharpAviRecorder] Stopped.");
        }

        private void InitializeAudioCapture()
        {
            try
            {
                _audioCapture = new WasapiLoopbackCapture();
                
                // WASAPI is usually 32-bit Float. SharpAvi works best with standard 16-bit PCM.
                // We will convert Float -> 16-bit PCM on the fly to fix "oversaturation" noise.
                
                int sourceChannels = _audioCapture.WaveFormat.Channels;
                int sourceSampleRate = _audioCapture.WaveFormat.SampleRate;
                
                // Define the Target Format (16-bit PCM)
                _audioStream = _writer!.AddAudioStream(sourceChannels, sourceSampleRate, 16); // 16 bits per sample
                _audioStream.Name = "System Audio";
                
                _audioCapture.DataAvailable += (s, e) =>
                {
                    if (_isRecording && _audioStream != null && e.BytesRecorded > 0)
                    {
                        // CONVERSION: Float (4 bytes) -> PCM 16 (2 bytes)
                        // Input: e.Buffer (byte[]) containing Floats
                        // Output: New byte[] containing Shorts
                        
                        byte[] buffer = e.Buffer;
                        int bytesRecorded = e.BytesRecorded;
                        
                        // Calculate sample count (Float = 4 bytes)
                        int sampleCount = bytesRecorded / 4;
                        
                        // Output buffer size (Short = 2 bytes) -> Half the size
                        byte[] pcmBuffer = new byte[sampleCount * 2];
                        
                        // Unsafe optimization not needed for audio rates, simple loop is fast enough
                        // But using float array is cleaner
                        
                        for (int i = 0; i < sampleCount; i++)
                        {
                            // Read Float
                            float sample = BitConverter.ToSingle(buffer, i * 4);
                            
                            // Clamp -1.0 to 1.0 (prevent wrapping clipping)
                            if (sample > 1.0f) sample = 1.0f;
                            if (sample < -1.0f) sample = -1.0f;
                            
                            // Scale to Short Range
                            short pcm = (short)(sample * 32767);
                            
                            // Write Short
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

        private void VideoLoop()
        {
            using var bitmap = new Bitmap(_width, _height);
            using var graphics = Graphics.FromImage(bitmap);
            
            // Encoder setup
            var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)_quality); 
            var jpegCodec = ImageCodecInfo.GetImageDecoders().FirstOrDefault(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
            if (jpegCodec == null) return;
            
            // Reusable memory stream
            using var ms = new MemoryStream();

            // TIMING LOGIC (Constant Frame Rate)
            // We must write exactly 'fps' frames for every 1 second of wall-clock time.
            
            double msPerFrame = 1000.0 / _frameRate;
            long startTime = Stopwatch.GetTimestamp();
            long framesWritten = 0;
            
            try
            {
                while (!_stopEvent.WaitOne(0))
                {
                    // 1. Calculate how many frames SHOULD exist by now
                    long now = Stopwatch.GetTimestamp();
                    double elapsedSeconds = (double)(now - startTime) / Stopwatch.Frequency;
                    long targetFrameCount = (long)(elapsedSeconds * _frameRate);
                    
                    // 2. If we are ahead (have written enough), wait
                    if (framesWritten > targetFrameCount)
                    {
                        // Calculate wait time
                        int waitMs = (int)(msPerFrame * (framesWritten - targetFrameCount));
                        if (waitMs > 1) 
                        {
                            Thread.Sleep(Math.Min(waitMs, 10)); // Sleep in small chunks to stay responsive
                            continue;
                        }
                    }
                    
                    // 3. Capture ONE Frame
                    // (Even if we are WAY behind, we capture once and write N times to catch up. 
                    // This avoids GDI+ bottlenecking us further)
                    
                    graphics.CopyFromScreen(0, 0, 0, 0, new Size(_width, _height), CopyPixelOperation.SourceCopy);
                    
                    // Compress
                    ms.SetLength(0);
                    bitmap.Save(ms, jpegCodec, encoderParams);
                    byte[] jpegData = ms.ToArray();

                    // 4. Determine Write Count
                    // If we are behind, we write multiple times to catch up to wall-clock of NEXT frame
                    long framesNeeded = (targetFrameCount + 1) - framesWritten;
                    if (framesNeeded < 1) framesNeeded = 1; // Always write at least 1 if we did the work
                    
                    // Cap duplicate frames to avoid massive files if we freeze (e.g. max 5 dupes per loop)
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
