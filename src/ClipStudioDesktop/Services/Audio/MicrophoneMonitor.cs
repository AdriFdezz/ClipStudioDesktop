using ClipStudioDesktop.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;

namespace ClipStudioDesktop.Services.Audio
{
    /// <summary>
    /// Servicio para monitorear la entrada del micrófono en tiempo real.
    /// <para>
    /// Proporciona reproducción de audio (escucharse a sí mismo) y medición de niveles para la visualización del VU Meter en la UI.
    /// </para>
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

        /// <summary>
        /// Evento que se dispara cuando cambia el nivel de volumen detectado.
        /// Útil para actualizar la barra de progreso o medidor en la interfaz gráfica.
        /// </summary>
        public event Action<double>? LevelChanged;

        /// <summary>
        /// Nivel actual del audio monitorizado (valor normalizado entre 0.0 y 1.0).
        /// </summary>
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

        /// <summary>
        /// Inicia el monitoreo del micrófono y la reproducción local (si aplica).
        /// </summary>
        /// <returns><c>true</c> si el inicio fue exitoso; de lo contrario, <c>false</c>.</returns>
        public bool Start()
        {
            if (_isMonitoring) return true;

            try
            {
                // Inicializar captura desde el micrófono seleccionado
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
                        // Fallback al dispositivo por defecto si falla el seleccionado
                        _capture = new WasapiCapture();
                    }
                }

                if (_capture == null) return false;

                // Crear un proveedor con búfer para la reproducción (playback)
                // Se descartan datos si hay desbordamiento para evitar mucha latencia
                _bufferedProvider = new BufferedWaveProvider(_capture.WaveFormat)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromMilliseconds(200)
                };

                // Configurar salida de audio para escucharse a sí mismo
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

        /// <summary>
        /// Detiene el monitoreo y libera los recursos de captura y reproducción.
        /// </summary>
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

        /// <summary>
        /// Callback ejecutado cuando hay nuevos datos de audio disponibles.
        /// Procesa la señal para aplicar ganancia, noise gate y calcular el nivel para la UI.
        /// </summary>
        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0 || !_isMonitoring || _capture?.WaveFormat == null) return;

            // Obtener configuración de audio
            double gainDB = _settings.Audio.MicrophoneGainDB;
            double noiseGateDB = _settings.Audio.NoiseGateDB;
            double gainMultiplier = gainDB != 0 ? Math.Pow(10, gainDB / 20.0) : 1.0;
            
            // Umbral de noise gate (invertido para comportamiento intuitivo: -60 es silencioso)
            double noiseGateThreshold = 0.0;
            if (noiseGateDB != 0)
            {
                double effectiveDB = -60.0 - noiseGateDB;
                noiseGateThreshold = Math.Pow(10, effectiveDB / 20.0);
            }

            // Búfer para procesar el audio (aplicar efectos)
            byte[] processedBuffer = new byte[e.BytesRecorded];
            Array.Copy(e.Buffer, processedBuffer, e.BytesRecorded);

            // Calcular nivel RMS crudo (antes de ganancia) para decidir si la noise gate se abre
            double rawLevel = CalculateRmsLevel(e.Buffer, e.BytesRecorded, _capture.WaveFormat);
            
            // ¿Debe abrirse la noise gate? Basado en si la señal cruda supera el umbral
            bool gateOpen = (noiseGateThreshold == 0) || (rawLevel >= noiseGateThreshold);
            
            // Calcular nivel final para visualización (se aplica ganancia si la noise gate abre)
            double levelAfterGain = rawLevel * gainMultiplier;

            // Procesar muestras individuales según formato (Float o PCM 16-bit)
            if (_capture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat && _capture.WaveFormat.BitsPerSample == 32)
            {
                for (int i = 0; i < e.BytesRecorded; i += 4)
                {
                    float sample;
                    if (!gateOpen)
                    {
                        sample = 0f; // Silencio total si la noise gate está cerrada
                    }
                    else
                    {
                        sample = BitConverter.ToSingle(processedBuffer, i);
                        if (gainMultiplier != 1.0)
                            sample = (float)(sample * gainMultiplier);
                        // Clampear para evitar distorsión digital dura
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

            // Añadir audio procesado al búfer de reproducción (para escucharse a sí mismo)
            _bufferedProvider?.AddSamples(processedBuffer, 0, e.BytesRecorded);

            // Actualizar nivel del VU Meter
            // Nota: Se muestra el nivel DE SALIDA (después de ganancia y noise gate)
            // Multiplicador x30 para hacer la visualización más responsiva y visible
            double displayLevel = gateOpen ? levelAfterGain * 30.0 : 0;
            CurrentLevel = Math.Min(1.0, Math.Max(0.0, displayLevel));
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            // La limpieza principal se maneja en Stop()
        }

        /// <summary>
        /// Calcula el nivel RMS (Root Mean Square) del buffer de audio.
        /// </summary>
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
