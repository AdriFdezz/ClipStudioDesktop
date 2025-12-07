using ClipStudioDesktop.Helpers;
using ClipStudioDesktop.Models;
using ClipStudioDesktop.Services.Settings;
using ClipStudioDesktop.Services.Storage;
using ClipStudioDesktop.Services.Recording;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace ClipStudioDesktop.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly IStorageService _storageService;
        private readonly IRecordingService _recordingService;
        private readonly System.Windows.Threading.DispatcherTimer _timer;

        public AppSettings Settings => _settingsService.CurrentSettings;

        public string StatusText => _recordingService.IsRecording ? "Grabando (Activo)" : "Pausado";
        public string MemoryUsageText { get; private set; } = "Calculando...";
        public string BufferSizeText { get; private set; } = "Calculando...";
        public string ClipsTodayText { get; private set; } = "0";
        public string SpaceUsedText { get; private set; } = "Calculando...";

        public ICommand SaveCommand { get; }
        public ICommand ResetCommand { get; }
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
            OpenAudioFolderCommand = new RelayCommand(_ => OpenFolder(_storageService.GetAudioFolder()));
            OpenVideoFolderCommand = new RelayCommand(_ => OpenFolder(_storageService.GetVideoFolder()));
            OpenImagesFolderCommand = new RelayCommand(_ => OpenFolder(_storageService.GetImageFolder()));

            _timer = new System.Windows.Threading.DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(2);
            _timer.Tick += UpdateStats;
            _timer.Start();
            UpdateStats(null, EventArgs.Empty);
        }

        private void UpdateStats(object? sender, EventArgs e)
        {
            OnPropertyChanged(nameof(StatusText));
            
            using (var proc = Process.GetCurrentProcess())
            {
                MemoryUsageText = $"{proc.PrivateMemorySize64 / 1024 / 1024} MB";
            }
            OnPropertyChanged(nameof(MemoryUsageText));

            try
            {
                long bufferSize = GetDirectorySize(_settingsService.CurrentSettings.Paths.TempBuffer);
                BufferSizeText = $"{bufferSize / 1024 / 1024} MB (Disco)";
            }
            catch { BufferSizeText = "N/A"; }
            OnPropertyChanged(nameof(BufferSizeText));

            try
            {
                var audioFiles = GetFilesToday(_storageService.GetAudioFolder());
                var videoFiles = GetFilesToday(_storageService.GetVideoFolder());
                var imageFiles = GetFilesToday(_storageService.GetImageFolder());
                
                ClipsTodayText = $"{audioFiles.Length + videoFiles.Length + imageFiles.Length}";
                
                long totalSize = audioFiles.Sum(f => f.Length) + videoFiles.Sum(f => f.Length) + imageFiles.Sum(f => f.Length);
                SpaceUsedText = $"{totalSize / 1024 / 1024} MB";
            }
            catch 
            { 
                ClipsTodayText = "Error";
                SpaceUsedText = "Error";
            }
            OnPropertyChanged(nameof(ClipsTodayText));
            OnPropertyChanged(nameof(SpaceUsedText));
        }

        private long GetDirectorySize(string path)
        {
            if (!Directory.Exists(path)) return 0;
            return new DirectoryInfo(path).GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }

        private FileInfo[] GetFilesToday(string path)
        {
            if (!Directory.Exists(path)) return Array.Empty<FileInfo>();
            var today = DateTime.Today;
            return new DirectoryInfo(path).GetFiles().Where(f => f.CreationTime.Date == today).ToArray();
        }

        private void SaveSettings()
        {
            _settingsService.SaveSettings();
            StartupHelper.SetStartup(_settingsService.CurrentSettings.General.StartWithWindows);
            System.Windows.MessageBox.Show("Configuración guardada correctamente.", "Clip Studio Desktop", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
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
