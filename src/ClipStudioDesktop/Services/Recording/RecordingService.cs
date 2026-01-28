using ClipStudioDesktop.Services.Audio;
using ClipStudioDesktop.Services.Video;
using ClipStudioDesktop.Services.Settings;
using ClipStudioDesktop.Services.Storage;
using NAudio.Wave;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace ClipStudioDesktop.Services.Recording
{
    /// <summary>
    /// Servicio principal de grabación que coordina la captura de video, audio del sistema y micrófono.
    /// <para>
    /// Orquesta el uso de <see cref="SharpAviRecorder"/> para video, <see cref="AudioRecorder"/> para audio puro,
    /// y <see cref="MicrophoneRecorder"/> para el micrófono.
    /// Gestiona los archivos temporales y su posterior conversión/fusión con FFmpeg.
    /// </para>
    /// </summary>
    public class RecordingService : IRecordingService, IDisposable
    {
        private readonly ISettingsService _settingsService;
        private readonly IStorageService _storageService;
        
        // Grabadores especializados
        private SharpAviRecorder? _nativeRecorder;
        private AudioRecorder? _audioRecorder; // Usado solo en modo "Solo Audio"
        private MicrophoneRecorder? _micRecorder;
        
        // Timer de seguridad para verificar límites de tamaño
        private System.Timers.Timer? _checkTimer;
        
        // Rastreo de archivos temporales actuales
        private string? _currentVideoFile; // AVI (Raw)
        private string? _currentAudioFile; // WAV/MP3 (Solo Audio)
        private string? _currentMicFile;   // WAV (Microfono individual)
        
        private long _maxSizeBytes = 0;

        public RecordingService(ISettingsService settingsService, IStorageService storageService)
        {
            _settingsService = settingsService;
            _storageService = storageService;
            
            // Inicializar grabadores (SharpAvi y AudioRecorder)
            _nativeRecorder = new SharpAviRecorder(); 
            _audioRecorder = new AudioRecorder(_settingsService.CurrentSettings);
            
            // Timer para chequear cada 10s si se ha superado el límite de almacenamiento
            _checkTimer = new System.Timers.Timer(10000);
            _checkTimer.Elapsed += CheckRecordingLimit;
        }



        public bool IsRecording { get; private set; }
        public DateTime? CurrentRecordingStartTime { get; private set; }
        public event EventHandler<bool>? RecordingStateChanged;
        public event EventHandler<string>? ClipSaved;
        public event EventHandler<(long Estimated, long Physical)>? BufferSizeChanged; 

        public bool IsVideoMode { get; private set; } = true;

        /// <summary>
        /// Alterna el estado de grabación. Si ya está grabando, detiene. Si no, inicia.
        /// Si se cambia el modo (Video vs Audio) mientras se graba, reinicia la grabación.
        /// </summary>
        public async Task ToggleRecordingAsync(bool videoEnabled = true)
        {
            if (IsRecording)
            {
                if (IsVideoMode == videoEnabled)
                {
                    // Mismo modo, simplemente detener
                    await StopRecordingAsync();
                }
                else
                {
                    // Cambio de modo: Detener y reiniciar en el nuevo modo
                    await StopRecordingAsync();
                    await Task.Delay(500); 
                    await StartRecordingAsync(videoEnabled);
                }
            }
            else
            {
                await StartRecordingAsync(videoEnabled);
            }
        }

        /// <summary>
        /// Inicia el proceso de grabación, configurando directorios temporales e inicializando los grabadores necesarios.
        /// </summary>
        /// <param name="videoEnabled">Determina si grabar video (AVI) o solo audio.</param>
        public async Task StartRecordingAsync(bool videoEnabled = true)
        {
            if (IsRecording) return;
            
            try 
            {
                IsVideoMode = videoEnabled;
                _storageService.EnsureDirectoriesExist();
                
                // Crear carpeta temporal si no existe
                string tempFolder = Path.Combine(Path.GetTempPath(), "ClipStudio_Rec");
                if (!Directory.Exists(tempFolder)) Directory.CreateDirectory(tempFolder);
                
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                
                if (IsVideoMode)
                {
                    _currentAudioFile = null;
                    
                    // GRABACIÓN NATIVA (SHARP AVI)
                    // Graba Video + Audio del Sistema en un solo AVI (MJPEG).
                    string tempAvi = Path.Combine(tempFolder, $"temp_raw_{timestamp}.avi");
                    _currentVideoFile = tempAvi;
                    
                    // Obtener FPS
                    int fps = _settingsService.CurrentSettings.Video.Framerate;
                    if (fps <= 0) fps = 30;
                    
                    // Calcular Calidad Escalada basada en Bitrate deseado
                    // Rango referencia: Bitrate 4000 -> Calidad 50 (Eficiente)
                    //                   Bitrate 15000 -> Calidad 80 (Alta)
                    int targetBitrate = _settingsService.CurrentSettings.Video.Bitrate;
                    if (targetBitrate <= 0) targetBitrate = 8000;
                    
                    int quality = 50; // Base inicial
                    if (targetBitrate > 4000)
                    {
                        // Interpolación lineal
                        double ratio = (double)(targetBitrate - 4000) / 11000.0;
                        if (ratio > 1.0) ratio = 1.0;
                        quality += (int)(ratio * 30); // Max 80
                    }
                    
                    // Iniciar grabador nativo (Video + Audio Desktop)
                    _nativeRecorder?.StartRecording(tempAvi, fps, quality, recordAudio: true);
                }
                else
                {
                    // MODO SOLO AUDIO
                    _currentVideoFile = null;
                    
                    string ext = _settingsService.CurrentSettings.Audio.Format.ToLower(); 
                    if (string.IsNullOrEmpty(ext)) ext = "wav";

                    _currentAudioFile = Path.Combine(tempFolder, $"rec_audio_{timestamp}.{ext}");
                    if (_audioRecorder != null)
                    {
                        var success = _audioRecorder.Start(_currentAudioFile);
                        if (!success) throw new Exception("Error al iniciar grabador de audio");
                    }
                }

                // 3. Iniciar Micrófono (si está habilitado) en paralelo
                if (_settingsService.CurrentSettings.Audio.EnableMicrophone)
                {
                    _micRecorder = new MicrophoneRecorder(_settingsService.CurrentSettings);
                     _currentMicFile = Path.Combine(tempFolder, $"rec_mic_{timestamp}.wav");
                    if (!_micRecorder.Start(_currentMicFile))
                    {
                        _currentMicFile = null;
                    }
                }
                else
                {
                    _micRecorder = null;
                    _currentMicFile = null;
                }
                
                _maxSizeBytes = _settingsService.CurrentSettings.Buffer.MaxBufferBytes;

                IsRecording = true;
                CurrentRecordingStartTime = DateTime.Now;
                RecordingStateChanged?.Invoke(this, IsRecording);
                _checkTimer?.Start();
                
                System.Diagnostics.Debug.WriteLine($"Grabación Iniciada (Video: {IsVideoMode})");
            }
            catch (Exception ex)
            {
                 System.Windows.MessageBox.Show($"Error al iniciar grabación: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                 await StopRecordingAsync();
            }
        }

        /// <summary>
        /// Detiene la grabación actual.
        /// Detiene todos los grabadores activos (Video, Audio, Micrófono) y desencadena el proceso de finalización (conversión).
        /// </summary>
        public async Task StopRecordingAsync()
        {
            if (!IsRecording) return;
            
            try
            {
                _checkTimer?.Stop();
                
                // Detener grabador principal
                if (IsVideoMode)
                {
                    _nativeRecorder?.Stop();
                }
                else
                {
                    _audioRecorder?.Stop();
                }

                // Detener micrófono y limpiar recursos
                _micRecorder?.Stop();
                if (_micRecorder != null) await _micRecorder.FinalizeRecordingAsync();
                
                IsRecording = false;
                CurrentRecordingStartTime = null;
                RecordingStateChanged?.Invoke(this, IsRecording);

                // Finalizar: Convertir archivos temporales (AVI -> MP4) y guardar
                await FinalizeAndSaveRecording();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping recording: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Verifica periódicamente si la grabación ha excedido el límite de tamaño configurado.
        /// Se ejecuta mediante un timer.
        /// </summary>
        private async void CheckRecordingLimit(object? sender, System.Timers.ElapsedEventArgs e)
        {
             if (!IsRecording) return;
             
             long physicalSize = 0;
             long displaySize = 0;
             try
             {
                 // Sumar tamaño de video temporal
                 if (_currentVideoFile != null && File.Exists(_currentVideoFile))
                 {
                     long vSize = new FileInfo(_currentVideoFile).Length;
                     physicalSize += vSize;
                     displaySize += vSize; // Video RAW/MJPEG es lo que ocupa espacio real
                 }
                 
                 // Sumar tamaño de audio
                 if (_currentAudioFile != null && File.Exists(_currentAudioFile))
                 {
                     long aSize = new FileInfo(_currentAudioFile).Length;
                     physicalSize += aSize;
                     displaySize += (aSize / 10); // Estimación para MP3/AAC (aprox 10:1)
                 }
                 
                 // Sumar tamaño de micrófono
                 if (_currentMicFile != null && File.Exists(_currentMicFile))
                 {
                     long mSize = new FileInfo(_currentMicFile).Length;
                     physicalSize += mSize;
                     displaySize += (mSize / 10);
                 }
                 
                 // Actualizar UI con tamaño ESTIMADO final (displaySize) y FÍSICO real (physicalSize)
                 BufferSizeChanged?.Invoke(this, (displaySize, physicalSize));
                 
                 // Chequeo de seguridad con tamaño FÍSICO (Uso de disco real)
                 if (_maxSizeBytes > 0 && physicalSize > _maxSizeBytes)
                 {
                     System.Diagnostics.Debug.WriteLine($"Límite de seguridad alcanzado ({physicalSize} > {_maxSizeBytes}). Deteniendo grabación.");
                     await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () => await StopRecordingAsync());
                 }
             }
             catch { }
        }

        /// <summary>
        /// Realiza la finalización de la grabación: convierte archivos temporales a formatos finales (MP4/MP3),
        /// mezcla canales de audio (Micro + Sistema) y limpia los temporales.
        /// </summary>
        private async Task FinalizeAndSaveRecording()
        {
            // Verificar si hay algo que guardar
            bool hasVideo = _currentVideoFile != null && File.Exists(_currentVideoFile);
            bool hasAudioRec = _currentAudioFile != null && File.Exists(_currentAudioFile);
            
            if (IsVideoMode && !hasVideo)
            {
                 System.Diagnostics.Debug.WriteLine("No se encontró archivo de video en modo video.");
                 return;
            }

            try
            {
                 string timestamp = DateTime.Now.ToString("dd_MM_yyyy_HH_mm_ss");
                 string outputFile;
                 
                 // Procesar Audio (Convertir raw a wav/mp3 para mezcla)
                 string? finalAudio = null;
                 if (_currentAudioFile != null && _audioRecorder != null)
                 {
                     finalAudio = await _audioRecorder.FinalizeRecordingAsync(Path.GetDirectoryName(_currentAudioFile)!, "wav");
                 }
                 
                 // Procesar Micrófono
                 string? finalMic = _currentMicFile;
                 if (finalMic != null && File.Exists(finalMic))
                 {
                     if (new FileInfo(finalMic).Length < 1024) 
                     {
                         // Si es menos de 1KB, asumimos inválido/vacío
                         finalMic = null; 
                     }
                 }
                 else
                 {
                     finalMic = null;
                 }

                 bool hasAudio = finalAudio != null && File.Exists(finalAudio);
                 bool hasMic = finalMic != null && File.Exists(finalMic);

                 // Lógica de Finalización
                
                 if (IsVideoMode)
                 {
                     string finalFolder = _storageService.GetVideoFolder();
                     string ext = _settingsService.CurrentSettings.Video.Format.ToLower();
                     if (string.IsNullOrEmpty(ext)) ext = "mp4";
                     
                     outputFile = Path.Combine(finalFolder, $"Grabacion_de_Video_{timestamp}.{ext}");
                     
                     // _currentVideoFile es el archivo temporal AVI (Raw + Audio Sistema)
                     // Se usa FFmpeg para convertir AVI -> MP4 (H264/AAC)
                     
                     if (hasMic)
                     {
                         // Mezclar AVI + Micrófono -> MP4 Final
                         await MergeMicToVideo(outputFile, _currentVideoFile!, finalMic!);
                     }
                     else
                     {
                         // Transcodificar AVI -> MP4 (Sin mic extra)
                         int bitrate = _settingsService.CurrentSettings.Video.Bitrate;
                         if (bitrate <= 0) bitrate = 8000;
                         
                         await ConvertAviToFinal(outputFile, _currentVideoFile!, bitrate);
                     }
                 }
                 else
                 {
                     // Lógica Solo Audio
                     string finalFolder = _storageService.GetAudioFolder();
                     string ext = _settingsService.CurrentSettings.Audio.Format.ToLower();
                     if (string.IsNullOrEmpty(ext)) ext = "mp3";
                     
                     outputFile = Path.Combine(finalFolder, $"Grabacion_de_Audio_{timestamp}.{ext}");
                     
                     if (hasAudio && hasMic) await MergeAudioOnly(outputFile, finalAudio!, finalMic!);
                     else if (hasAudio) await ConvertAudio(outputFile, finalAudio!);
                     else if (hasMic) await ConvertAudio(outputFile, finalMic!);
                     else return;
                 }
                 
                 // Limpieza de archivos temporales
                 try 
                 {
                     if (_currentVideoFile != null && File.Exists(_currentVideoFile) && _currentVideoFile != outputFile) File.Delete(_currentVideoFile);
                     // Borrar raw audio si existe (Modo Audio Only)
                     if (!IsVideoMode && _currentAudioFile != null && File.Exists(_currentAudioFile)) File.Delete(_currentAudioFile);
                     
                     if (finalAudio != null && File.Exists(finalAudio)) File.Delete(finalAudio);
                     if (finalMic != null && File.Exists(finalMic)) File.Delete(finalMic);
                 }
                 catch (Exception cleanupEx) 
                 { 
                     System.Diagnostics.Debug.WriteLine($"Error de limpieza: {cleanupEx.Message}"); 
                 }

                 ClipSaved?.Invoke(this, outputFile);
                 
            }
            catch (OperationCanceledException)
            {
                // Usuario canceló la conversión - limpiar sigilosamente
                System.Diagnostics.Debug.WriteLine("[Recording] Conversión cancelada por el usuario");
                try 
                {
                    if (_currentVideoFile != null && File.Exists(_currentVideoFile)) File.Delete(_currentVideoFile);
                    if (_currentAudioFile != null && File.Exists(_currentAudioFile)) File.Delete(_currentAudioFile);
                    if (_currentMicFile != null && File.Exists(_currentMicFile)) File.Delete(_currentMicFile);
                }
                catch { }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error al guardar grabación: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Método auxiliar para mezclar video, audio y micrófono en un solo archivo final.
        /// <para>Nota: Este método puede ser redundante con <see cref="MergeMicToVideo"/> pero se mantiene por compatibilidad.</para>
        /// </summary>
        private async Task MergeFiles(string output, string video, string? audio, string? mic)
        {
            string ffmpeg = ClipStudioDesktop.Helpers.FFmpegHelper.GetFFmpegPath();
            if (string.IsNullOrEmpty(ffmpeg)) return;
            
            string args;
             if (audio != null && mic != null)
            {
                 // Mezclar Audio + Mic
                 args = $"-i \"{video}\" -i \"{audio}\" -i \"{mic}\" " +
                           $"-filter_complex \"[1:a][2:a]amix=inputs=2:duration=longest[a]\" " +
                           $"-map 0:v -map \"[a]\" " +
                           $"-c:v copy -c:a aac -b:a 192k " +
                           $"-shortest \"{output}\"";
            }
            else if (audio != null)
            {
                 args = $"-i \"{video}\" -i \"{audio}\" " +
                           $"-map 0:v -map 1:a " +
                           $"-c:v copy -c:a aac -b:a 192k " +
                           $"-shortest \"{output}\"";
            }
             else if (mic != null)
            {
                 args = $"-i \"{video}\" -i \"{mic}\" " +
                           $"-map 0:v -map 1:a " +
                           $"-c:v copy -c:a aac -b:a 192k " +
                           $"-shortest \"{output}\"";
            }
            else 
            {
                return;
            }
            
            await RunFFmpeg(ffmpeg, args);
        }

        /// <summary>
        /// Mezcla dos archivos de audio (Sistema + Micrófono) en uno solo.
        /// </summary>
        private async Task MergeAudioOnly(string output, string audio1, string audio2)
        {
            string ffmpeg = ClipStudioDesktop.Helpers.FFmpegHelper.GetFFmpegPath();
            if (string.IsNullOrEmpty(ffmpeg)) return;

            string codecArgs = GetAudioCodecArgs(output);
            string args = $"-i \"{audio1}\" -i \"{audio2}\" " +
                          $"-filter_complex \"amix=inputs=2:duration=longest\" " +
                          $"{codecArgs} \"{output}\"";

            await RunFFmpeg(ffmpeg, args);
        }

        /// <summary>
        /// Convierte un archivo de audio a otro formato (ej. RAW a MP3).
        /// </summary>
        private async Task ConvertAudio(string output, string input)
        {
             string ffmpeg = ClipStudioDesktop.Helpers.FFmpegHelper.GetFFmpegPath();
            if (string.IsNullOrEmpty(ffmpeg)) return;

            string codecArgs = GetAudioCodecArgs(output);
            string args = $"-i \"{input}\" {codecArgs} \"{output}\"";
            await RunFFmpeg(ffmpeg, args);
        }

        /// <summary>
        /// Determina los argumentos de códec de audio para FFmpeg según la extensión del archivo de salida.
        /// </summary>
        private string GetAudioCodecArgs(string outputFile)
        {
            string ext = Path.GetExtension(outputFile).ToLower();
            if (ext == ".flac") return "-c:a flac";
            if (ext == ".wav") return "-c:a pcm_s16le";
            if (ext == ".ogg") return "-c:a libvorbis -q:a 6";
            return "-c:a libmp3lame -q:a 2"; // por defecto mp3
        }

        /// <summary>
        /// Convierte el archivo AVI temporal (Video Raw + Audio Sistema) al formato final (MP4/WebM).
        /// Aplica re-codificación de video (H264/VP9) y audio (AAC/Opus).
        /// </summary>
        private async Task ConvertAviToFinal(string output, string inputAvi, int vBitrate)
        {
             string ffmpeg = ClipStudioDesktop.Helpers.FFmpegHelper.GetFFmpegPath();
            if (string.IsNullOrEmpty(ffmpeg)) return;

             int aBitrate = _settingsService.CurrentSettings.Audio.Bitrate;
             if (aBitrate <= 0) aBitrate = 192;
             
             string resolution = _settingsService.CurrentSettings.Video.Resolution;
             string scaleFilter = "";
             if (!string.IsNullOrEmpty(resolution) && resolution.Contains("x") && resolution != "Native")
             {
                 scaleFilter = $"-s {resolution}";
             }

            string ext = Path.GetExtension(output).ToLower();
            string args;
            
            if (ext == ".webm")
            {
                // WebM: Video VP9 + Audio Opus
                args = $"-i \"{inputAvi}\" -c:v libvpx-vp9 -b:v {vBitrate}k {scaleFilter} " +
                       $"-c:a libopus -b:a {aBitrate}k " +
                       $"\"{output}\"";
            }
            else
            {
                // MP4/MKV: Video H264 + Audio AAC
                // preset ultrafast y faststart para optimizar velocidad
                args = $"-i \"{inputAvi}\" -c:v libx264 -preset ultrafast -pix_fmt yuv420p " +
                       $"-b:v {vBitrate}k -maxrate {vBitrate}k -bufsize {vBitrate * 2}k {scaleFilter} " +
                       $"-c:a aac -b:a {aBitrate}k " +
                       $"-movflags +faststart \"{output}\"";
            }
            
            await RunFFmpegWithProgress(ffmpeg, args, inputAvi, output);
        }

        /// <summary>
        /// Ejecuta FFmpeg sin mostrar progreso en UI (consola solamente).
        /// </summary>
        private async Task RunFFmpeg(string exe, string args)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[FFmpeg] Starting: {args}");
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"-y {args}", // -y para sobrescribir
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                });
                
                if (p != null) 
                {
                    string stderr = await p.StandardError.ReadToEndAsync();
                    await p.WaitForExitAsync();
                    
                    if (p.ExitCode != 0)
                    {
                         System.Diagnostics.Debug.WriteLine($"[FFmpeg] ERROR (Exit {p.ExitCode}): {stderr}");
                         throw new Exception($"FFmpeg falló con código {p.ExitCode}. Log: {stderr}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[FFmpeg] Completado. Log: {stderr}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FFmpeg] Excepción: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Ejecuta FFmpeg mostrando una ventana de progreso (ProcessingWindow).
        /// Parsea la salida stderr de FFmpeg para calcular el porcentaje de avance.
        /// </summary>
        private async Task RunFFmpegWithProgress(string exe, string args, string inputFile, string outputFile)
        {
            Views.ProcessingWindow? progressWindow = null;
            Process? p = null;
            bool wasCancelled = false;
            
            try
            {
                // Obtener duración total para calcular porcentaje
                TimeSpan totalDuration = await GetMediaDuration(exe, inputFile);
                
                // Mostrar ventana de progreso en thread UI
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    progressWindow = new Views.ProcessingWindow();
                    progressWindow.CancellationRequested += (s, e) =>
                    {
                        wasCancelled = true;
                        try
                        {
                            if (p != null && !p.HasExited)
                            {
                                p.Kill();
                            }
                        }
                        catch { }
                    };
                    progressWindow.Show();
                });

                System.Diagnostics.Debug.WriteLine($"[FFmpeg] Iniciando con progreso: {args}");
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"-y {args}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                p = Process.Start(psi);
                if (p == null) return;

                DateTime startTime = DateTime.Now;
                
                // Leer stderr línea a línea para progreso
                var stderrTask = Task.Run(async () =>
                {
                    var reader = p.StandardError;
                    char[] buffer = new char[256];
                    string accumulated = "";
                    
                    while (!p.HasExited || reader.Peek() >= 0)
                    {
                        int read = await reader.ReadAsync(buffer, 0, buffer.Length);
                        if (read > 0)
                        {
                            accumulated += new string(buffer, 0, read);
                            
                            // Parsear progreso de salida FFmpeg
                            // Formato típico: frame=  120 fps=30 time=00:00:04.00 bitrate=8000kbps speed=1.5x
                            var timeMatch = System.Text.RegularExpressions.Regex.Match(accumulated, @"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})");
                            var speedMatch = System.Text.RegularExpressions.Regex.Match(accumulated, @"speed=\s*([\d.]+)x");
                            
                            if (timeMatch.Success && totalDuration.TotalSeconds > 0)
                            {
                                int hours = int.Parse(timeMatch.Groups[1].Value);
                                int mins = int.Parse(timeMatch.Groups[2].Value);
                                int secs = int.Parse(timeMatch.Groups[3].Value);
                                int centis = int.Parse(timeMatch.Groups[4].Value);
                                
                                TimeSpan currentTime = new TimeSpan(0, hours, mins, secs, centis * 10);
                                double percent = (currentTime.TotalSeconds / totalDuration.TotalSeconds) * 100;
                                
                                TimeSpan? remaining = null;
                                if (speedMatch.Success && double.TryParse(speedMatch.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double speed) && speed > 0)
                                {
                                    double remainingSeconds = (totalDuration.TotalSeconds - currentTime.TotalSeconds) / speed;
                                    remaining = TimeSpan.FromSeconds(remainingSeconds);
                                }
                                
                                progressWindow?.UpdateProgress(percent, remaining);
                            }
                            
                            // Mantener solo los últimos 500 chars para no saturar memoria con logs largos
                            if (accumulated.Length > 500)
                                accumulated = accumulated.Substring(accumulated.Length - 500);
                        }
                        else
                        {
                            await Task.Delay(50);
                        }
                    }
                });

                await p.WaitForExitAsync();
                await stderrTask;

                if (wasCancelled)
                {
                    // Borrar salida parcial
                    try
                    {
                        if (File.Exists(outputFile))
                        {
                            File.Delete(outputFile);
                            System.Diagnostics.Debug.WriteLine($"[FFmpeg] Borrado archivo parcial: {outputFile}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FFmpeg] Error borrando salida: {ex.Message}");
                    }
                    
                    // Lanzar excepción para manejo arriba
                    throw new OperationCanceledException("Conversión cancelada por el usuario");
                }

                if (p.ExitCode != 0)
                {
                    throw new Exception($"FFmpeg falló con código {p.ExitCode}");
                }
            }
            finally
            {
                // Cerrar ventana de progreso
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    progressWindow?.CloseWithoutConfirmation();
                });
            }
        }

        /// <summary>
        /// Obtiene la duración de un archivo multimedia usando FFmpeg (analizando stderr).
        /// </summary>
        private async Task<TimeSpan> GetMediaDuration(string ffmpegPath, string inputFile)
        {
            try
            {
                // Usar input como argumento para obtener metadatos
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-i \"{inputFile}\" -hide_banner",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                var p = Process.Start(psi);
                if (p == null) return TimeSpan.Zero;

                string stderr = await p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();

                // Parsear duración: Duration: 00:01:30.50
                var match = System.Text.RegularExpressions.Regex.Match(stderr, @"Duration:\s*(\d{2}):(\d{2}):(\d{2})\.(\d{2})");
                if (match.Success)
                {
                    int hours = int.Parse(match.Groups[1].Value);
                    int mins = int.Parse(match.Groups[2].Value);
                    int secs = int.Parse(match.Groups[3].Value);
                    int centis = int.Parse(match.Groups[4].Value);
                    return new TimeSpan(0, hours, mins, secs, centis * 10);
                }
            }
            catch { }
            
            return TimeSpan.Zero;
        }

        public void ClearBuffer() { } // No-op en implementación actual
        public void UpdateBufferReservation() { } // No-op en implementación actual
        
        /// <summary>
        /// Método heredado para guardar clips de buffer (Instant Replay).
        /// Actualmente desactivado en favor de grabación directa.
        /// </summary>
        public Task SaveClipAsync(int durationSeconds, bool isVideo) 
        {
             // Esta función era para "Instant Replay". 
             // Con el modo de grabación simplificado, el buffer continuo se ha eliminado.
             System.Windows.MessageBox.Show("La grabación en buffer está desactivada en este modo simplificado.");
             return Task.CompletedTask;
        }

        public void Dispose()
        {
            _checkTimer?.Stop();
             _nativeRecorder?.Dispose();
            _audioRecorder?.Dispose();
             _micRecorder?.Dispose();
        }

        private void PlayNotificationSound(bool start)
        {
            if (_settingsService.CurrentSettings.General.PlaySoundOnClip)
            {
                Task.Run(() =>
                {
                    try
                    {
                        var soundPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "Notification_sound.wav");
                        if (System.IO.File.Exists(soundPath))
                        {
                            using (var audioFile = new AudioFileReader(soundPath))
                            using (var outputDevice = new WaveOutEvent())
                            {
                                outputDevice.Init(audioFile);
                                outputDevice.Play();
                                while (outputDevice.PlaybackState == PlaybackState.Playing)
                                {
                                    System.Threading.Thread.Sleep(100);
                                }
                            }
                        }
                    }
                    catch { }
                });
            }
        }
        private void OnAudioDataAvailable(byte[] buffer, int count)
        {
             // No-op
        }
        
        /// <summary>
        /// Mezcla un video (con audio de sistema integrado) con una pista de micrófono externa.
        /// Genera el archivo final MP4/WebM.
        /// </summary>
        private async Task MergeMicToVideo(string output, string videoInput, string micInput)
        {
             string ffmpeg = ClipStudioDesktop.Helpers.FFmpegHelper.GetFFmpegPath();
             if (string.IsNullOrEmpty(ffmpeg)) return;
             
             // Settings
             int vBitrate = _settingsService.CurrentSettings.Video.Bitrate;
             if (vBitrate <= 0) vBitrate = 8000;
             
             int aBitrate = _settingsService.CurrentSettings.Audio.Bitrate;
             if (aBitrate <= 0) aBitrate = 192;
             
             string resolution = _settingsService.CurrentSettings.Video.Resolution;
             string scaleFilter = "";
             if (!string.IsNullOrEmpty(resolution) && resolution.Contains("x") && resolution != "Native")
             {
                 scaleFilter = $"-s {resolution}";
             }

             string ext = Path.GetExtension(output).ToLower();
             string args;
             
             if (ext == ".webm")
             {
                 // WebM: VP9 video + Opus audio (mix system + mic)
                 args = $"-i \"{videoInput}\" -i \"{micInput}\" " +
                        $"-filter_complex \"[0:a][1:a]amix=inputs=2:duration=longest[a]\" " +
                        $"-map 0:v -map \"[a]\" " +
                        $"-c:v libvpx-vp9 -b:v {vBitrate}k {scaleFilter} " +
                        $"-c:a libopus -b:a {aBitrate}k " +
                        $"-shortest \"{output}\"";
             }
             else
             {
                 // MP4/MKV: H264 video + AAC audio (mix system + mic)
                 args = $"-i \"{videoInput}\" -i \"{micInput}\" " +
                        $"-filter_complex \"[0:a][1:a]amix=inputs=2:duration=longest[a]\" " +
                        $"-map 0:v -map \"[a]\" " +
                        $"-c:v libx264 -preset ultrafast -pix_fmt yuv420p " +
                        $"-b:v {vBitrate}k -maxrate {vBitrate}k -bufsize {vBitrate * 2}k {scaleFilter} " +
                        $"-c:a aac -b:a {aBitrate}k " +
                        $"-movflags +faststart " +
                        $"-shortest \"{output}\"";
             }
                           
             await RunFFmpegWithProgress(ffmpeg, args, videoInput, output);
        }
    }
}
