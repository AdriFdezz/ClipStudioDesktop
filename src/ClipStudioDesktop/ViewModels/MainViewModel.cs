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
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Management;

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
    /// Modelo para representar un monitor/pantalla en la UI.
    /// </summary>
    public class MonitorItem
    {
        public string DisplayName { get; set; } = "";
        public int Index { get; set; } = 0;
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
        private bool _isIdentifying;
        private readonly System.Windows.Threading.DispatcherTimer _timer;
        private MicrophoneMonitor? _micMonitor;
        private bool _isMicMonitorEnabled;
        private double _micLevel;
        private int _dotCounter = 0;
        private int _animationTick = 0;
        
        /// <summary>
        /// Acceso directo a la configuración global para el Binding en XAML.
        /// </summary>
        public AppSettings Settings => _settingsService.CurrentSettings;

        public ObservableCollection<string> AudioDevices { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<MicrophoneDevice> AvailableMicrophones { get; set; } = new ObservableCollection<MicrophoneDevice>();
        public ObservableCollection<MonitorItem> AvailableMonitors { get; set; } = new ObservableCollection<MonitorItem>();
        
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
        public ICommand IdentifyMonitorsCommand { get; }

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

        /// <summary>
        /// Inicializa una nueva instancia de <see cref="MainViewModel"/>.
        /// </summary>
        /// <param name="settingsService">Servicio para gestionar la configuración de la aplicación.</param>
        /// <param name="storageService">Servicio para gestionar directorios y rutas de archivos.</param>
        /// <param name="recordingService">Servicio principal para orquestar la grabación.</param>
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
            IdentifyMonitorsCommand = new RelayCommand(_ => IdentifyMonitors(), _ => !_isIdentifying);

            LoadAudioDevices();
            LoadAvailableMicrophones();
            LoadAvailableMonitors();
            
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

        /// <summary>
        /// Formatea un tamaño en bytes a una cadena legible (MB o GB).
        /// </summary>
        /// <param name="bytes">Tamaño en bytes.</param>
        /// <returns>Cadena formateada con 2 decimales.</returns>
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
        /// Carga los monitores disponibles. Utiliza System.Windows.Forms.Screen para la lista base,
        /// y enriquece los nombres utilizando WMI y PnP IDs para mostrar el modelo real.
        /// </summary>
        private void LoadAvailableMonitors()
        {
            AvailableMonitors.Clear();
            try
            {
                var screens = System.Windows.Forms.Screen.AllScreens;
                for (int i = 0; i < screens.Length; i++)
                {
                    var screen = screens[i];
                    string resolution = $"{screen.Bounds.Width}x{screen.Bounds.Height}";
                    
                    // Obtener nombre amigable (ej. "DELL U2415")
                    string friendlyName = GetMonitorFriendlyName(screen.DeviceName);
                    if (string.IsNullOrEmpty(friendlyName) || friendlyName.Contains("Generic")) 
                    {
                        // Fallback si no encuentra nada o es muy generico
                        friendlyName = "Monitor Genérico";
                    }

                    string name = $"Pantalla {i + 1} - {friendlyName} ({resolution})";
                    if (screen.Primary) name += " [Principal]";

                    AvailableMonitors.Add(new MonitorItem
                    {
                        DisplayName = name,
                        Index = i,
                        DeviceName = screen.DeviceName
                    });
                }

                // Validar selección actual
                int currentIdx = _settingsService.CurrentSettings.Monitor.SelectedMonitorIndex;
                string savedDevice = _settingsService.CurrentSettings.Monitor.SelectedMonitorDeviceName;

                // Buscar el índice del monitor principal real
                int primaryIdx = 0;
                for (int i = 0; i < screens.Length; i++)
                {
                    if (screens[i].Primary)
                    {
                        primaryIdx = i;
                        break;
                    }
                }

                // Si no hay monitor guardado o el índice es inválido, forzar el monitor principal
                bool isValid = currentIdx >= 0 && currentIdx < AvailableMonitors.Count;
                if (!isValid || string.IsNullOrEmpty(savedDevice))
                {
                    _settingsService.CurrentSettings.Monitor.SelectedMonitorIndex = primaryIdx;
                    _settingsService.CurrentSettings.Monitor.SelectedMonitorDeviceName = screens[primaryIdx].DeviceName;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading monitors: {ex.Message}");
                AvailableMonitors.Add(new MonitorItem { DisplayName = "Pantalla Principal (Default)", Index = 0 });
            }
        }

        /// <summary>
        /// Busca el nombre amigable (Marketing Name) de un monitor dado su nombre de dispositivo.
        /// Utiliza una combinación de EnumDisplayDevices (PnP) y WMI (WmiMonitorID) para obtener el modelo exacto.
        /// </summary>
        /// <param name="screenDeviceName">Nombre del dispositivo de pantalla (ej. \\.\DISPLAY1).</param>
        /// <returns>Nombre del modelo (ej. "C27G4Z") o cadena vacía si no se encuentra.</returns>
        private string GetMonitorFriendlyName(string screenDeviceName)
        {
            try
            {
                // 1. Obtener el PnP Device ID mediante EnumDisplayDevices
                var device = new NativeMethods.DISPLAY_DEVICE();
                device.cb = Marshal.SizeOf(device);

                for (uint id = 0; NativeMethods.EnumDisplayDevices(null, id, ref device, 0); id++)
                {
                    if (device.DeviceName == screenDeviceName)
                    {
                        var monitor = new NativeMethods.DISPLAY_DEVICE();
                        monitor.cb = Marshal.SizeOf(monitor);

                        if (NativeMethods.EnumDisplayDevices(device.DeviceName, 0, ref monitor, 0))
                        {
                            string pnpDeviceId = monitor.DeviceID; // Ejemplo: MONITOR\BNQ78C8\{GUID}
                            
                            // 2. Intentar obtener el nombre real (Marketing Name) via WMI
                            try 
                            {
                                // Normalizar ID para buscar en WMI. 
                                // El formato típico de Enum es: MONITOR\BNQ78C8\{GUID}
                                // El formato típico de WMI es: DISPLAY\BNQ78C8\4&2a3...
                                
                                // Estrategia: Extraer el ID del Hardware (ej: BNQ78C8) y buscarlo en el InstanceName de WMI
                                string hardwareId = "";
                                var parts = pnpDeviceId.Split('\\');
                                if (parts.Length >= 2)
                                {
                                    hardwareId = parts[1]; // BNQ78C8
                                }

                                using (var searcher = new ManagementObjectSearcher("root\\wmi", "SELECT * FROM WmiMonitorID"))
                                {
                                    foreach (ManagementObject queryObj in searcher.Get())
                                    {
                                        string instanceName = queryObj["InstanceName"]?.ToString() ?? "";
                                        
                                        // Comprobar si contiene el ID del Hardware
                                        if (!string.IsNullOrEmpty(hardwareId) && instanceName.Contains(hardwareId, StringComparison.OrdinalIgnoreCase))
                                        {
                                            var userFriendlyNameCodes = (UInt16[])queryObj["UserFriendlyName"];
                                            if (userFriendlyNameCodes != null && userFriendlyNameCodes.Length > 0)
                                            {
                                                string name = new string(userFriendlyNameCodes.Select(x => (char)x).ToArray()).Trim('\0');
                                                if (!string.IsNullOrWhiteSpace(name))
                                                {
                                                    return name;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception wmiEx) 
                            {
                                Debug.WriteLine($"WMI Monitor Error: {wmiEx.Message}");
                            }

                            // 3. Fallback: Si WMI falla, usar DeviceString (A veces es "Generic PnP Monitor")
                            if (!string.IsNullOrEmpty(monitor.DeviceString))
                            {
                                return monitor.DeviceString;
                            }
                        }
                    }
                    device.cb = Marshal.SizeOf(device);
                }
            }
            catch (Exception ex)
            {
               Debug.WriteLine($"Error getting monitor friendly name: {ex.Message}");
            }
            return "";
        }

        /// <summary>
        /// Clase interna para llamadas nativas a la API de Windows (User32).
        /// Necesaria para obtener información avanzada de dispositivos de pantalla.
        /// </summary>
        private static class NativeMethods
        {
            /// <summary>
            /// Enumera los dispositivos de pantalla disponibles en el sistema.
            /// </summary>
            [DllImport("user32.dll")]
            public static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

            /// <summary>
            /// Estructura que recibe información sobre un dispositivo de pantalla.
            /// </summary>
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
            public struct DISPLAY_DEVICE
            {
                [MarshalAs(UnmanagedType.U4)]
                public int cb;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
                public string DeviceName;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
                public string DeviceString;
                [MarshalAs(UnmanagedType.U4)]
                public DisplayDeviceStateFlags StateFlags;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
                public string DeviceID;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
                public string DeviceKey;
            }

            /// <summary>
            /// Flags que indican el estado de un dispositivo de pantalla.
            /// </summary>
            [Flags]
            public enum DisplayDeviceStateFlags : int
            {
                AttachedToDesktop = 0x1,
                MultiDriver = 0x2,
                PrimaryDevice = 0x4,
                MirroringDriver = 0x8,
                VGACompatible = 0x10,
                Removable = 0x20,
                ModesPruned = 0x8000000,
                Remote = 0x4000000,
                Disconnect = 0x2000000
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
                
                // Animación de puntos cuando está en espera
                _animationTick++;
                if (_animationTick >= 5)
                {
                    _animationTick = 0;
                    _dotCounter++;
                    if (_dotCounter > 3) _dotCounter = 0;
                    
                    RemainingSpace = "Espacio Restante: Esperando a comenzar una grabación" + new string('.', _dotCounter);
                }
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

        /// <summary>
        /// Obtiene todos los archivos dentro de un directorio dado.
        /// </summary>
        /// <param name="path">Ruta absoluta del directorio a escanear.</param>
        /// <returns>Array de <see cref="FileInfo"/> con los archivos encontrados, o vacío si no existe.</returns>
        private FileInfo[] GetAllFiles(string path)
        {
            if (!Directory.Exists(path)) return Array.Empty<FileInfo>();
            return new DirectoryInfo(path).GetFiles("*.*", SearchOption.TopDirectoryOnly);
        }
        
        /// <summary>
        /// Comando asíncrono para alternar la grabación de video.
        /// </summary>
        private async System.Threading.Tasks.Task ToggleVideoRecording()
        {
            await _recordingService.ToggleRecordingAsync(videoEnabled: true);
        }

        /// <summary>
        /// Comando asíncrono para alternar la grabación de solo audio.
        /// </summary>
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
                LoadAvailableMonitors();

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
        /// Muestra una ventana de identificación en cada monitor conectado.
        /// Cada ventana muestra el número de monitor y utiliza animaciones (FadeIn/Static/FadeOut).
        /// </summary>
        private async void IdentifyMonitors()
        {
            if (_isIdentifying) return;

            try
            {
                _isIdentifying = true;
                (IdentifyMonitorsCommand as RelayCommand)?.RaiseCanExecuteChanged();
                
                var screens = System.Windows.Forms.Screen.AllScreens;
                var windows = new List<Window>();
                
                // Obtener el factor de escala DPI para convertir píxeles físicos a unidades lógicas de WPF
                double scaleX = 1.0;
                double scaleY = 1.0;

                var mainWindow = System.Windows.Application.Current.MainWindow;
                if (mainWindow != null)
                {
                    var source = PresentationSource.FromVisual(mainWindow);
                    if (source?.CompositionTarget != null)
                    {
                        scaleX = source.CompositionTarget.TransformToDevice.M11;
                        scaleY = source.CompositionTarget.TransformToDevice.M22;
                    }
                }

                foreach (var screen in screens)
                {
                    var win = new Views.IdentifyWindow(Array.IndexOf(screens, screen) + 1);
                    win.WindowStartupLocation = WindowStartupLocation.Manual;
                    
                    // IMPORTANTE: Convertir píxeles a unidades WPF (DPI Aware)
                    win.Left = screen.Bounds.Left / scaleX;
                    win.Top = screen.Bounds.Top / scaleY;
                    win.Width = screen.Bounds.Width / scaleX;
                    win.Height = screen.Bounds.Height / scaleY;
                    
                    win.Show();
                    windows.Add(win);
                }

                await Task.Delay(5500);

                foreach (var w in windows)
                {
                    try { w.Close(); } catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al identificar monitores: {ex.Message}");
            }
            finally
            {
                _isIdentifying = false;
                (IdentifyMonitorsCommand as RelayCommand)?.RaiseCanExecuteChanged();
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


    /// <summary>
    /// Contiene métodos e interfaces nativas de la API de Windows (User32) necesarios para operaciones de bajo nivel.
    /// Se utiliza principalmente para obtener información detallada sobre dispositivos de pantalla.
    /// </summary>
    internal static class NativeMethods
    {
        /// <summary>
        /// Enumera los dispositivos de visualización (monitores) del sistema actual.
        /// </summary>
        /// <param name="lpDevice">Nombre del dispositivo a consultar (null para iniciar enumeración).</param>
        /// <param name="iDevNum">Índice del dispositivo (basado en 0).</param>
        /// <param name="lpDisplayDevice">Referencia a la estructura donde se recibirán los datos.</param>
        /// <param name="dwFlags">Flags para controlar la operación (0 por defecto).</param>
        /// <returns>True si se encontró el dispositivo, False si no hay más dispositivos.</returns>
        [DllImport("user32.dll")]
        public static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        /// <summary>
        /// Estructura que representa la información de un dispositivo de pantalla en la API de Windows.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DISPLAY_DEVICE
        {
            /// <summary>
            /// Tamaño de la estructura en bytes. Debe inicializarse antes de llamar a la API.
            /// </summary>
            [MarshalAs(UnmanagedType.U4)]
            public int cb;

            /// <summary>
            /// Nombre interno del dispositivo (ej. \\.\DISPLAY1).
            /// </summary>
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;

            /// <summary>
            /// Descripción legible del dispositivo.
            /// </summary>
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;

            /// <summary>
            /// Flags de estado del dispositivo (activo, primario, etc.).
            /// </summary>
            [MarshalAs(UnmanagedType.U4)]
            public int StateFlags;

            /// <summary>
            /// Identificador único del dispositivo (PnP ID).
            /// </summary>
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;

            /// <summary>
            /// Clave de registro asociada al dispositivo.
            /// </summary>
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }
    }
}
