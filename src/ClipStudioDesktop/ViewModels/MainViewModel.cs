using ClipStudioDesktop.Helpers;
using ClipStudioDesktop.Models;
using ClipStudioDesktop.Services.Audio;
using ClipStudioDesktop.Services.Settings;
using ClipStudioDesktop.Services.Storage;
using ClipStudioDesktop.Services.Recording;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace ClipStudioDesktop.ViewModels
{
    /// <summary>
    /// Modelo simple para representar un dispositivo de micrófono en la UI.
    /// </summary>
    public class MicrophoneDevice
    {
        public string DisplayName { get; set; } = "";
        public string DeviceName { get; set; } = "";
    }

    /// <summary>
    /// ViewModel principal de la aplicación.
    /// Gestiona el estado de la UI, la interacción con los servicios de grabación,
    /// la configuración y las estadísticas en tiempo real.
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly IStorageService _storageService;
        private readonly IRecordingService _recordingService;
        private readonly System.Windows.Threading.DispatcherTimer _timer;
        private MicrophoneMonitor? _micMonitor;
        private bool _isMicMonitorEnabled;
        private double _micLevel;
        
        /// <summary>
        /// Acceso directo a la configuración global para el Binding en XAML.
        /// </summary>
        public AppSettings Settings => _settingsService.CurrentSettings;

        public ObservableCollection<string> AudioDevices { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<MicrophoneDevice> AvailableMicrophones { get; set; } = new ObservableCollection<MicrophoneDevice>();
        
        private string _selectedAudioDevice = "";
        /// <summary>
        /// Dispositivo de audio de escritorio seleccionado (Loopback).
        /// Actualiza la configuración al cambiar.
        /// </summary>
        public string SelectedAudioDevice
        {
            get => _selectedAudioDevice;
            set
            {
                _selectedAudioDevice = value;
                _settingsService.CurrentSettings.Audio.SelectedAudioDevice = value;
                OnPropertyChanged(nameof(SelectedAudioDevice));
            }
        }

        public string StatusText => _recordingService.IsRecording ? "Grabando (Activo)" : "Pausado";
        
        public string VideoButtonText => _recordingService.IsRecording && _recordingService.IsVideoMode ? "Detener Video" : "Grabar Video";
        public string AudioButtonText => _recordingService.IsRecording && !_recordingService.IsVideoMode ? "Detener Audio" : "Grabar Audio";
        
        public bool IsRecording => _recordingService.IsRecording;

        public bool IsVideoCaptureEnabled => !_recordingService.IsRecording || (_recordingService.IsRecording && _recordingService.IsVideoMode);
        public bool IsAudioCaptureEnabled => !_recordingService.IsRecording || (_recordingService.IsRecording && !_recordingService.IsVideoMode);

        public string BufferSizeText { get; private set; } = "0 MB";
        
        // Propiedades para estadísticas
        public string AudioClipsText { get; private set; } = "0";
        public string VideoClipsText { get; private set; } = "0";
        public string ImagesText { get; private set; } = "0";
        public string SpaceUsedText { get; private set; } = "Calculando...";

        public ICommand SaveCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand ToggleVideoCommand { get; }
        public ICommand ToggleAudioCommand { get; }
        public ICommand OpenAudioFolderCommand { get; }
        public ICommand OpenVideoFolderCommand { get; }
        public ICommand OpenImagesFolderCommand { get; }
        public ICommand ReloadSettingsCommand { get; }

        // Propiedades de Monitor de Micrófono
        public bool IsMicMonitorEnabled
        {
            get => _isMicMonitorEnabled;
            set
            {
                if (_isMicMonitorEnabled != value)
                {
                    _isMicMonitorEnabled = value;
                    OnPropertyChanged(nameof(IsMicMonitorEnabled));
                    ToggleMicMonitor(value);
                }
            }
        }

        /// <summary>
        /// Nivel actual del micrófono (0.0 a 1.0) para el vúmetro visual.
        /// </summary>
        public double MicLevel
        {
            get => _micLevel;
            set
            {
                _micLevel = value;
                OnPropertyChanged(nameof(MicLevel));
            }
        }


        /// <summary>
        /// Activa o desactiva el monitoreo del micrófono (vúmetro visual).
        /// </summary>
        private void ToggleMicMonitor(bool enable)
        {
            if (enable)
            {
                _micMonitor = new MicrophoneMonitor(_settingsService.CurrentSettings);
                _micMonitor.LevelChanged += level =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => MicLevel = level);
                };
                _micMonitor.Start();
            }
            else
            {
                _micMonitor?.Stop();
                _micMonitor?.Dispose();
                _micMonitor = null;
                MicLevel = 0;
            }
        }

        public MainViewModel(ISettingsService settingsService, IStorageService storageService, IRecordingService recordingService)
        {
            _settingsService = settingsService;
            _storageService = storageService;
            _recordingService = recordingService;

            SaveCommand = new RelayCommand(_ => SaveSettings());
            ResetCommand = new RelayCommand(_ => ResetSettings());
            ToggleVideoCommand = new RelayCommand(async _ => await ToggleVideoRecording());
            ToggleAudioCommand = new RelayCommand(async _ => await ToggleAudioRecording());
            OpenAudioFolderCommand = new RelayCommand(_ => OpenFolder(_storageService.GetAudioFolder()));
            OpenVideoFolderCommand = new RelayCommand(_ => OpenFolder(_storageService.GetVideoFolder()));
            OpenImagesFolderCommand = new RelayCommand(_ => OpenFolder(_storageService.GetImageFolder()));
            ReloadSettingsCommand = new RelayCommand(_ => ReloadSettings());

            LoadAudioDevices();
            LoadAvailableMicrophones();
            
            // Timer para actualizar estadísticas (clips y espacio) cada 100ms
            _timer = new System.Windows.Threading.DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(100);
            _timer.Tick += UpdateStats; 
            _timer.Start();
            
            // Suscribirse a cambios en tiempo real del buffer
            _recordingService.BufferSizeChanged += OnBufferSizeChanged;
            _recordingService.RecordingStateChanged += OnRecordingStateChanged;
            
            UpdateStats(null, EventArgs.Empty);
        }

        /// <summary>
        /// Callback invocado cuando cambia el estado global de grabación (Start/Stop).
        /// Actualiza los textos de los botones y el estado de la UI.
        /// </summary>
        private void OnRecordingStateChanged(object? sender, bool isRecording)
        {
             System.Windows.Application.Current.Dispatcher.Invoke(() =>
             {
                 OnPropertyChanged(nameof(StatusText));
                 OnPropertyChanged(nameof(VideoButtonText));
                 OnPropertyChanged(nameof(AudioButtonText));
                 OnPropertyChanged(nameof(IsVideoCaptureEnabled));
                 OnPropertyChanged(nameof(IsAudioCaptureEnabled));
                 OnPropertyChanged(nameof(IsRecording)); // Notificar cambio

                 if (!isRecording) 
                 {
                     BufferSizeText = "0 MB";
                     RemainingSpace = "Espacio Restante: Esperando a comenzar una grabación...";
                     OnPropertyChanged(nameof(BufferSizeText));
                 }
             });
        }

        /// <summary>
        /// Callback invocado cuando cambia el uso del búfer en memoria.
        /// </summary>
        private void OnBufferSizeChanged(object? sender, (long Estimated, long Physical) sizes)
        {
             System.Windows.Application.Current.Dispatcher.Invoke(() =>
             {
                 UpdateBufferStats(sizes.Estimated, sizes.Physical);
             });
        }

        private string _remainingSpace = "Espacio Restante: Esperando a comenzar una grabación...";
        public string RemainingSpace
        {
            get => _remainingSpace;
            set => SetProperty(ref _remainingSpace, value);
        }

        private string _recordingDuration = "00:00:00";
        public string RecordingDuration
        {
            get => _recordingDuration;
            set => SetProperty(ref _recordingDuration, value);
        }



        /// <summary>
        /// Calcula y actualiza el texto de estadísticas del búfer y el espacio restante.
        /// </summary>
        private void UpdateBufferStats(long estimatedBytes, long physicalBytes)
        {
            try
            {
                // Tamaño RAW Actual del Buffer
                string physStr = FormatBytes(physicalBytes);
                BufferSizeText = physStr;
                
                // Lógica de Espacio Restante
                double maxGB = _settingsService.CurrentSettings.Buffer.MaxBufferSizeGB;
                
                if (maxGB <= 0.001) // 0 = Ilimitado
                {
                    RemainingSpace = "Espacio Restante: Sin Límite";
                }
                else
                {
                    long maxBytes = (long)(maxGB * 1024 * 1024 * 1024);
                    long remaining = maxBytes - physicalBytes;
                    if (remaining < 0) remaining = 0;
                    
                    string remStr = FormatBytes(remaining);
                    RemainingSpace = $"Espacio Restante: {remStr} ({maxGB:F1} GB)";
                }
            }
            catch
            {
                BufferSizeText = "Error";
                RemainingSpace = "Error";
            }
            
            OnPropertyChanged(nameof(BufferSizeText));
        }

        private string FormatBytes(long bytes)
        {
            if (bytes < 1024 * 1024 * 1024) 
            {
                double mb = bytes / 1024.0 / 1024.0;
                return $"{mb:F2} MB";
            }
            else
            {
                double gb = bytes / 1024.0 / 1024.0 / 1024.0;
                return $"{gb:F2} GB";
            }
        }

        /// <summary>
        /// Carga los dispositivos de salida de audio del sistema utilizando FFmpeg (dshow).
        /// Identifica dispositivos de loopback como "Stereo Mix" o "VoiceMeeter" para capturar audio de escritorio.
        /// </summary>
        private void LoadAudioDevices()
        {
            AudioDevices.Clear();
            AudioDevices.Add("Ninguno (sin audio de escritorio)");
            
            try
            {
                string ffmpegPath = ClipStudioDesktop.Helpers.FFmpegHelper.GetFFmpegPath();
                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    Debug.WriteLine("FFmpeg no encontrado para listar dispositivos de audio");
                    return;
                }

                // Usar FFmpeg para listar dispositivos (-list_devices true)
                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = "-list_devices true -f dshow -i dummy",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    string output = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    // Parsear salida para encontrar dispositivos capaces de capturar audio en bucle (loopback)
                    // Ejemplos: VoiceMeeter Output, Mezcla estéreo, Cable Output, etc.
                    var lines = output.Split('\n');
                    string? voiceMeeterDevice = null;
                    string? stereoMixDevice = null;
                    
                    foreach (var line in lines)
                    {
                        string trimmedLine = line.Trim();
                        
                        // Formato típico ffmpeg dshow:  [dshow @ ...]  "Nombre Dispositivo" (audio)
                        if (trimmedLine.Contains("(audio)") && trimmedLine.Contains("\""))
                        {
                            int firstQuote = trimmedLine.IndexOf("\"");
                            if (firstQuote >= 0)
                            {
                                int secondQuote = trimmedLine.IndexOf("\"", firstQuote + 1);
                                if (secondQuote > firstQuote)
                                {
                                    string deviceName = trimmedLine.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                                    
                                    // Filtrar dispositivos virtuales de loopback
                                    string lowerName = deviceName.ToLower();
                                    bool isLoopback = lowerName.Contains("voicemeeter output") ||
                                                     lowerName.Contains("stereo mix") ||
                                                     lowerName.Contains("cable output") ||
                                                     lowerName.Contains("mezcla") ||
                                                     lowerName.Contains("what u hear") ||
                                                     lowerName.Contains("loopback");
                                    
                                    if (isLoopback && !AudioDevices.Contains(deviceName))
                                    {
                                        AudioDevices.Add(deviceName);
                                        
                                        // Rastrear preferencias para autoselección
                                        if (lowerName.Contains("voicemeeter output") && !lowerName.Contains("aux"))
                                        {
                                            voiceMeeterDevice = deviceName;
                                        }
                                        if (lowerName.Contains("stereo mix") || lowerName.Contains("mezcla"))
                                        {
                                            stereoMixDevice = deviceName;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    
                    // Auto-seleccionar el mejor dispositivo disponible
                    string savedDevice = _settingsService.CurrentSettings.Audio.SelectedAudioDevice;
                    if (!string.IsNullOrEmpty(savedDevice) && AudioDevices.Contains(savedDevice))
                    {
                        SelectedAudioDevice = savedDevice;
                    }
                    else if (voiceMeeterDevice != null)
                    {
                        SelectedAudioDevice = voiceMeeterDevice;
                    }
                    else if (stereoMixDevice != null)
                    {
                        SelectedAudioDevice = stereoMixDevice;
                    }
                    else if (AudioDevices.Count > 1)
                    {
                        SelectedAudioDevice = AudioDevices[1]; // Primer dispositivo real encontrado
                    }
                    else
                    {
                        SelectedAudioDevice = AudioDevices[0]; // Ninguno
                    }
                    
                    Debug.WriteLine($"Audio devices loaded: {AudioDevices.Count}, Selected: {SelectedAudioDevice}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading audio devices: {ex.Message}");
                SelectedAudioDevice = "Ninguno (sin audio de escritorio)";
            }
        }

        /// <summary>
        /// Carga los micrófonos disponibles utilizando NAudio (CoreAudioApi).
        /// Selecciona automáticamente el micrófono guardado o el predeterminado.
        /// </summary>
        private void LoadAvailableMicrophones()
        {
            AvailableMicrophones.Clear();
            AvailableMicrophones.Add(new MicrophoneDevice 
            { 
                DisplayName = "Micrófono predeterminado", 
                DeviceName = "" 
            });
            
            try
            {
                // Usar NAudio para enumerar dispositivos de captura activos
                using (var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator())
                {
                    var devices = enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active);
                    
                    foreach (var device in devices)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            AvailableMicrophones.Add(new MicrophoneDevice 
                            { 
                                DisplayName = device.FriendlyName, 
                                DeviceName = device.ID 
                            });
                        });
                    }
                }
                
                Debug.WriteLine($"Total microphones found: {AvailableMicrophones.Count}");
                
                // Seleccionar micrófono por defecto si no hay configuración
                if (string.IsNullOrEmpty(_settingsService.CurrentSettings.Audio.SelectedMicrophone))
                {
                    if (AvailableMicrophones.Count > 0)
                    {
                        _settingsService.CurrentSettings.Audio.SelectedMicrophone = AvailableMicrophones[0].DeviceName;
                        OnPropertyChanged(nameof(Settings));
                    }
                }
                else
                {
                    var savedMic = _settingsService.CurrentSettings.Audio.SelectedMicrophone;
                    var foundMic = AvailableMicrophones.FirstOrDefault(m => m.DeviceName == savedMic);
                    if (foundMic == null && AvailableMicrophones.Count > 0)
                    {
                        // Si el micrófono guardado no existe (desconectado), usar default
                        _settingsService.CurrentSettings.Audio.SelectedMicrophone = AvailableMicrophones[0].DeviceName;
                        OnPropertyChanged(nameof(Settings));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading microphones: {ex.Message}");
            }
        }

        /// <summary>
        /// Actualiza las estadísticas de la sesión (duración) y escanea las carpetas
        /// para calcular clips totales y espacio utilizado.
        /// Se ejecuta periódicamente vía Timer (100ms).
        /// </summary>
        private void UpdateStats(object? sender, EventArgs e)
        {
            // Lógica de Duración
            if (_recordingService.IsRecording && _recordingService.CurrentRecordingStartTime.HasValue)
            {
                var duration = DateTime.Now - _recordingService.CurrentRecordingStartTime.Value;
                RecordingDuration = duration.ToString(@"hh\:mm\:ss");
            }
            else
            {
                RecordingDuration = "00:00:00";
            }

            OnPropertyChanged(nameof(StatusText));

            
            // Actualización de estadísticas para carpetas de clips
            try
            {
                var audioFiles = GetAllFiles(_storageService.GetAudioFolder());
                var videoFiles = GetAllFiles(_storageService.GetVideoFolder());
                var imageFiles = GetAllFiles(_storageService.GetImageFolder());
                
                AudioClipsText = GetFormattedStats(audioFiles);
                VideoClipsText = GetFormattedStats(videoFiles);
                ImagesText = GetFormattedStats(imageFiles);
                
                long totalBytes = audioFiles.Sum(f => f.Length) + videoFiles.Sum(f => f.Length) + imageFiles.Sum(f => f.Length);
                SpaceUsedText = FormatBytes(totalBytes);
            }
            catch 
            { 
                AudioClipsText = "Error";
                VideoClipsText = "Error";
                ImagesText = "Error";
                SpaceUsedText = "Error";
            }
            OnPropertyChanged(nameof(AudioClipsText));
            OnPropertyChanged(nameof(VideoClipsText));
            OnPropertyChanged(nameof(ImagesText));
            OnPropertyChanged(nameof(SpaceUsedText));
        }

        /// <summary>
        /// Formatea el conteo y tamaño total de un conjunto de archivos.
        /// </summary>
        private string GetFormattedStats(FileInfo[] files)
        {
            int count = files.Length;
            long bytes = files.Sum(f => f.Length);
            double mb = bytes / 1024.0 / 1024.0;
            
            string sizeStr;
            if (mb < 1024)
            {
                sizeStr = $"{mb:F2} MB";
            }
            else
            {
                double gb = mb / 1024.0;
                sizeStr = $"{gb:F2} GB";
            }
            
            return $"{count} ({sizeStr})";
        }

        private FileInfo[] GetAllFiles(string path)
        {
            if (!Directory.Exists(path)) return Array.Empty<FileInfo>();
            return new DirectoryInfo(path).GetFiles("*.*", SearchOption.TopDirectoryOnly);
        }
        
        private async System.Threading.Tasks.Task ToggleVideoRecording()
        {
            await _recordingService.ToggleRecordingAsync(videoEnabled: true);
        }

        private async System.Threading.Tasks.Task ToggleAudioRecording()
        {
            await _recordingService.ToggleRecordingAsync(videoEnabled: false);
        }

        /// <summary>
        /// Guarda la configuración actual en disco y solicita reinicio si es necesario.
        /// </summary>
        private void SaveSettings()
        {
            _settingsService.SaveSettings();
            StartupHelper.SetStartup(_settingsService.CurrentSettings.General.StartWithWindows);
            
            // Actualizar la reserva de buffer si cambió el tamaño
            _recordingService.UpdateBufferReservation();
            
            var result = System.Windows.MessageBox.Show(
                "Configuración guardada. Para aplicar los cambios es necesario reiniciar la aplicación.\n¿Desea reiniciar ahora?", 
                "Reiniciar Aplicación", 
                System.Windows.MessageBoxButton.YesNo, 
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                RestartApplication();
            }
        }

        /// <summary>
        /// Restablece la configuración a los valores de fábrica.
        /// </summary>
        private void ResetSettings()
        {
            if (System.Windows.MessageBox.Show("¿Estás seguro de que quieres restaurar los valores por defecto?", "Confirmar", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes)
            {
                _settingsService.ResetToDefaults();
                OnPropertyChanged(nameof(Settings));
                
                if (System.Windows.MessageBox.Show("Valores restaurados. Se recomienda reiniciar para aplicar todos los cambios.\n¿Desea reiniciar ahora?", "Reiniciar", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes)
                {
                    RestartApplication();
                }
            }
        }

        /// <summary>
        /// Recarga la configuración desde el archivo JSON.
        /// Útil si se edita el archivo externamente mientras la app corre.
        /// </summary>
        private void ReloadSettings()
        {
            _settingsService.LoadSettings();
            OnPropertyChanged(nameof(Settings));
        }

        /// <summary>
        /// Reinicia la aplicación actual. Soluciona problemas de ruta con .NET 5+.
        /// </summary>
        private void RestartApplication()
        {
            var fileName = Process.GetCurrentProcess().MainModule?.FileName;
            
            // Fix para .NET Core/5+ donde MainModule puede apuntar a la .dll en lugar del .exe
            if (fileName != null && fileName.EndsWith(".dll"))
            {
                fileName = System.IO.Path.ChangeExtension(fileName, ".exe");
            }
            
            if (fileName != null && System.IO.File.Exists(fileName))
            {
                // Pasar --show-ui para indicar que debe mostrar la ventana de configuración
                Process.Start(fileName, "--show-ui");
                System.Windows.Application.Current.Shutdown();
            }
        }

        /// <summary>
        /// Abre una carpeta en el explorador de archivos de Windows.
        /// </summary>
        /// <param name="path">Ruta absoluta del directorio a abrir.</param>
        private void OpenFolder(string path)
        {
            try
            {
                _storageService.EnsureDirectoriesExist();
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show($"No se pudo abrir la carpeta: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }
}
