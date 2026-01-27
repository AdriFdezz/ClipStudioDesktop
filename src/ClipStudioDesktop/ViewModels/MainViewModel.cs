using ClipStudioDesktop.Helpers;
using ClipStudioDesktop.Models;
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
    public class MicrophoneDevice
    {
        public string DisplayName { get; set; } = "";
        public string DeviceName { get; set; } = "";
    }

    public class MainViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly IStorageService _storageService;
        private readonly IRecordingService _recordingService;
        private readonly System.Windows.Threading.DispatcherTimer _timer;
        
        public AppSettings Settings => _settingsService.CurrentSettings;

        public ObservableCollection<string> AudioDevices { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<MicrophoneDevice> AvailableMicrophones { get; set; } = new ObservableCollection<MicrophoneDevice>();
        
        private string _selectedAudioDevice = "";
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
        
        public bool IsVideoCaptureEnabled => !_recordingService.IsRecording || (_recordingService.IsRecording && _recordingService.IsVideoMode);
        public bool IsAudioCaptureEnabled => !_recordingService.IsRecording || (_recordingService.IsRecording && !_recordingService.IsVideoMode);

        public string BufferSizeText { get; private set; } = "0 MB";
        
        // Properties for stats
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
            
            _timer = new System.Windows.Threading.DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(2);
            _timer.Tick += UpdateStats; // Mantener timer para clips y espacio usado
            _timer.Start();
            
            // Suscribirse a cambios en tiempo real del buffer
            _recordingService.BufferSizeChanged += OnBufferSizeChanged;
            _recordingService.RecordingStateChanged += OnRecordingStateChanged;
            
            UpdateStats(null, EventArgs.Empty);
        }

        private void OnRecordingStateChanged(object? sender, bool isRecording)
        {
             System.Windows.Application.Current.Dispatcher.Invoke(() =>
             {
                 OnPropertyChanged(nameof(StatusText));
                 OnPropertyChanged(nameof(VideoButtonText));
                 OnPropertyChanged(nameof(AudioButtonText));
                 OnPropertyChanged(nameof(IsVideoCaptureEnabled));
                 OnPropertyChanged(nameof(IsAudioCaptureEnabled));
                 if (!isRecording) 
                 {
                     BufferSizeText = "0 MB";
                     OnPropertyChanged(nameof(BufferSizeText));
                 }
             });
        }

        private void OnBufferSizeChanged(object? sender, (long Estimated, long Physical) sizes)
        {
             System.Windows.Application.Current.Dispatcher.Invoke(() =>
             {
                 UpdateBufferStats(sizes.Estimated, sizes.Physical);
             });
        }

        private void UpdateBufferStats(long estimatedBytes, long physicalBytes)
        {
            try
            {
                if (!_recordingService.IsRecording && estimatedBytes == 0)
                {
                    BufferSizeText = "0 MB";
                }
                else
                {
                    string estStr = FormatBytes(estimatedBytes);
                    string physStr = FormatBytes(physicalBytes);
                    
                    BufferSizeText = physStr;
                }
            }
            catch
            {
                BufferSizeText = "Error";
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

                // Use FFmpeg to list audio devices
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

                    // Parse for audio devices that can capture desktop audio
                    // These include: VoiceMeeter Output, Stereo Mix, CABLE Output, etc.
                    var lines = output.Split('\n');
                    string? voiceMeeterDevice = null;
                    string? stereoMixDevice = null;
                    
                    foreach (var line in lines)
                    {
                        string trimmedLine = line.Trim();
                        
                        if (trimmedLine.Contains("(audio)") && trimmedLine.Contains("\""))
                        {
                            int firstQuote = trimmedLine.IndexOf("\"");
                            if (firstQuote >= 0)
                            {
                                int secondQuote = trimmedLine.IndexOf("\"", firstQuote + 1);
                                if (secondQuote > firstQuote)
                                {
                                    string deviceName = trimmedLine.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                                    
                                    // Check if this is a loopback/virtual audio device
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
                                        
                                        // Track preferred devices for auto-selection
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
                    
                    // Auto-select best available device
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
                        SelectedAudioDevice = AudioDevices[1]; // First real device
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
                // Use NAudio to list audio capture devices (Wasapi)
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
                
                // Select default microphone if nothing is saved or saved one doesn't exist
                if (string.IsNullOrEmpty(_settingsService.CurrentSettings.Audio.SelectedMicrophone))
                {
                    // Select the default (first item - "Micrófono predeterminado")
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
                        // If saved mic not found, select default
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

        private void UpdateStats(object? sender, EventArgs e)
        {
            OnPropertyChanged(nameof(StatusText));

            
            // Stats updates for clips folder
            try
            {
                var audioFiles = GetAllFiles(_storageService.GetAudioFolder());
                var videoFiles = GetAllFiles(_storageService.GetVideoFolder());
                var imageFiles = GetAllFiles(_storageService.GetImageFolder());
                
                AudioClipsText = audioFiles.Length.ToString();
                VideoClipsText = videoFiles.Length.ToString();
                ImagesText = imageFiles.Length.ToString();
                
                long totalBytes = audioFiles.Sum(f => f.Length) + videoFiles.Sum(f => f.Length) + imageFiles.Sum(f => f.Length);
                double totalMB = totalBytes / 1024.0 / 1024.0;
                
                if (totalMB >= 1024)
                {
                    double totalGB = totalMB / 1024.0;
                    SpaceUsedText = $"{totalGB:F2} GB";
                }
                else
                {
                    SpaceUsedText = $"{totalMB:F2} MB";
                }
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

        private void ReloadSettings()
        {
            _settingsService.LoadSettings();
            OnPropertyChanged(nameof(Settings));
        }

        private void RestartApplication()
        {
            var fileName = Process.GetCurrentProcess().MainModule?.FileName;
            
            // Fix for .NET Core/5+ where MainModule might point to .dll
            if (fileName != null && fileName.EndsWith(".dll"))
            {
                fileName = System.IO.Path.ChangeExtension(fileName, ".exe");
            }
            
            if (fileName != null && System.IO.File.Exists(fileName))
            {
                Process.Start(fileName);
                System.Windows.Application.Current.Shutdown();
            }
        }

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
