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
        public string RecordingButtonText => _recordingService.IsRecording ? "Desactivar Grabación" : "Activar Grabación";
        public string BufferSizeText { get; private set; } = "Calculando...";
        public string AudioSegmentsText { get; private set; } = "0";
        public string VideoSegmentsText { get; private set; } = "0";
        public string ReservedSpaceText { get; private set; } = "Calculando...";
        public string AudioClipsText { get; private set; } = "0";
        public string VideoClipsText { get; private set; } = "0";
        public string ImagesText { get; private set; } = "0";
        public string SpaceUsedText { get; private set; } = "Calculando...";

        public ICommand SaveCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand ToggleRecordingCommand { get; }
        public ICommand ClearBufferCommand { get; }
        public ICommand OpenAudioFolderCommand { get; }
        public ICommand OpenVideoFolderCommand { get; }
        public ICommand OpenImagesFolderCommand { get; }

        public MainViewModel(ISettingsService settingsService, IStorageService storageService, IRecordingService recordingService)
        {
            _settingsService = settingsService;
            _storageService = storageService;
            _recordingService = recordingService;

            SaveCommand = new RelayCommand(_ => SaveSettings());
            ResetCommand = new RelayCommand(_ => ResetSettings());
            ToggleRecordingCommand = new RelayCommand(async _ => await ToggleRecording());
            ClearBufferCommand = new RelayCommand(_ => ClearBuffer(), _ => !_recordingService.IsRecording);
            OpenAudioFolderCommand = new RelayCommand(_ => OpenFolder(_storageService.GetAudioFolder()));
            OpenVideoFolderCommand = new RelayCommand(_ => OpenFolder(_storageService.GetVideoFolder()));
            OpenImagesFolderCommand = new RelayCommand(_ => OpenFolder(_storageService.GetImageFolder()));

            LoadAudioDevices();
            LoadAvailableMicrophones();
            
            _timer = new System.Windows.Threading.DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(2);
            _timer.Tick += UpdateStats;
            _timer.Start();
            UpdateStats(null, EventArgs.Empty);
        }

        private void LoadAudioDevices()
        {
            AudioDevices.Clear();
            AudioDevices.Add("Audio del Sistema (NAudio)");
            
            // NAudio maneja la captura de audio automáticamente
            // No necesitamos listar dispositivos manualmente
            
            // Load saved device or default to system audio
            SelectedAudioDevice = "Audio del Sistema (NAudio)";
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
                string ffmpegPath = ClipStudioDesktop.Helpers.FFmpegHelper.GetFFmpegPath();
                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    Debug.WriteLine("FFmpeg no encontrado, usando micrófono predeterminado");
                    return;
                }

                // Use FFmpeg to list audio input devices
                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = "-list_devices true -f dshow -i dummy",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    string output = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    Debug.WriteLine("=== FFmpeg Audio Devices Output ===");
                    Debug.WriteLine(output);
                    Debug.WriteLine("=== End FFmpeg Output ===");

                    // Parse the output for audio input devices
                    var lines = output.Split('\n');
                    bool inAudioSection = false;
                    
                    foreach (var line in lines)
                    {
                        string trimmedLine = line.Trim();
                        
                        if (trimmedLine.Contains("DirectShow audio devices") || trimmedLine.Contains("dshow @ "))
                        {
                            inAudioSection = true;
                        }
                        
                        if (inAudioSection)
                        {
                            if (trimmedLine.Contains("DirectShow video devices"))
                            {
                                break; // End of audio section
                            }
                            
                            // Look for lines with "(audio)" indicating audio devices
                            // Format: [dshow @ ...] "Device Name" (audio)
                            if (trimmedLine.Contains("(audio)") && trimmedLine.Contains("\""))
                            {
                                try
                                {
                                    // Extract first quoted string
                                    int firstQuote = trimmedLine.IndexOf("\"");
                                    if (firstQuote >= 0)
                                    {
                                        int secondQuote = trimmedLine.IndexOf("\"", firstQuote + 1);
                                        if (secondQuote > firstQuote)
                                        {
                                            string deviceName = trimmedLine.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                                            
                                            // Avoid duplicates
                                            if (!string.IsNullOrWhiteSpace(deviceName) && 
                                                !AvailableMicrophones.Any(m => m.DeviceName == deviceName))
                                            {
                                                AvailableMicrophones.Add(new MicrophoneDevice
                                                {
                                                    DisplayName = deviceName,
                                                    DeviceName = deviceName
                                                });
                                                Debug.WriteLine($"Found microphone: {deviceName}");
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Error parsing line: {trimmedLine} - {ex.Message}");
                                }
                            }
                        }
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

        private void ClearBuffer()
        {
            try
            {
                _recordingService.ClearBuffer();
                
                // Restablecer el espacio reservado al tamaño configurado
                string bufferPath = _settingsService.CurrentSettings.Paths.TempBuffer;
                long bytesToReserve = _settingsService.CurrentSettings.Buffer.MaxBufferBytes;
                ClipStudioDesktop.Services.Storage.DiskSpaceReservation.ReserveSpace(bufferPath, bytesToReserve);
                
                System.Windows.MessageBox.Show("Buffer limpiado exitosamente.", "Buffer Limpio", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                UpdateStats(null, EventArgs.Empty); // Actualizar stats
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error al limpiar buffer: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void UpdateStats(object? sender, EventArgs e)
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(RecordingButtonText));

            try
            {
                string bufferPath = _settingsService.CurrentSettings.Paths.TempBuffer;
                long bufferSize = GetDirectorySize(bufferPath);
                BufferSizeText = $"{bufferSize / 1024 / 1024} MB (Disco)";
                
                // Contar segmentos de audio
                string audioPath = Path.Combine(bufferPath, "audio");
                int audioSegments = 0;
                if (Directory.Exists(audioPath))
                {
                    audioSegments = Directory.GetFiles(audioPath, "*.raw").Length;
                }
                AudioSegmentsText = audioSegments.ToString();
                
                // Contar segmentos de video
                string videoPath = Path.Combine(bufferPath, "video");
                int videoSegments = 0;
                if (Directory.Exists(videoPath))
                {
                    videoSegments = Directory.GetFiles(videoPath, "*.mp4").Length;
                }
                VideoSegmentsText = videoSegments.ToString();
                
                // Calcular espacio reservado restante
                long reservedSize = ClipStudioDesktop.Services.Storage.DiskSpaceReservation.GetCurrentReservationSize(bufferPath);
                if (reservedSize < 1024 * 1024 * 1024) // Menos de 1GB
                {
                    ReservedSpaceText = $"{reservedSize / 1024 / 1024} MB";
                }
                else
                {
                    double reservedGB = reservedSize / 1024.0 / 1024.0 / 1024.0;
                    ReservedSpaceText = $"{reservedGB:F2} GB";
                }
            }
            catch 
            { 
                BufferSizeText = "N/A";
                AudioSegmentsText = "0";
                VideoSegmentsText = "0";
                ReservedSpaceText = "N/A";
            }
            OnPropertyChanged(nameof(BufferSizeText));
            OnPropertyChanged(nameof(AudioSegmentsText));
            OnPropertyChanged(nameof(VideoSegmentsText));
            OnPropertyChanged(nameof(ReservedSpaceText));

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
                    SpaceUsedText = $"{totalMB:F0} MB";
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

        private long GetDirectorySize(string path)
        {
            if (!Directory.Exists(path)) return 0;
            
            long totalSize = 0;
            
            // Audio buffer
            string audioPath = Path.Combine(path, "audio");
            if (Directory.Exists(audioPath))
            {
                totalSize += new DirectoryInfo(audioPath)
                    .GetFiles("*.raw", SearchOption.TopDirectoryOnly)
                    .Sum(f => f.Length);
            }
            
            // Video buffer
            string videoPath = Path.Combine(path, "video");
            if (Directory.Exists(videoPath))
            {
                totalSize += new DirectoryInfo(videoPath)
                    .GetFiles("*.mp4", SearchOption.TopDirectoryOnly)
                    .Sum(f => f.Length);
            }
            
            return totalSize;
        }

        private FileInfo[] GetAllFiles(string path)
        {
            if (!Directory.Exists(path)) return Array.Empty<FileInfo>();
            return new DirectoryInfo(path).GetFiles("*.*", SearchOption.TopDirectoryOnly);
        }
        private async System.Threading.Tasks.Task ToggleRecording()
        {
            if (_recordingService.IsRecording)
            {
                await _recordingService.StopRecordingAsync();
            }
            else
            {
                await _recordingService.StartRecordingAsync();
            }
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(RecordingButtonText));
            
            // Actualizar estado del bot\u00f3n ClearBuffer
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                ((RelayCommand)ClearBufferCommand).RaiseCanExecuteChanged();
            });
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
                var fileName = Process.GetCurrentProcess().MainModule?.FileName;
                if (fileName != null)
                {
                    Process.Start(fileName);
                    System.Windows.Application.Current.Shutdown();
                }
            }
        }

        private void ResetSettings()
        {
            if (System.Windows.MessageBox.Show("¿Estás seguro de que quieres restaurar los valores por defecto?", "Confirmar", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes)
            {
                _settingsService.ResetToDefaults();
                OnPropertyChanged(nameof(Settings));
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
