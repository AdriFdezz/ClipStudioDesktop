using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Hardcodet.Wpf.TaskbarNotification;
using System.Drawing;
using ClipStudioDesktop.Services.Screenshot;
using ClipStudioDesktop.Services.Storage;
using ClipStudioDesktop.Services.Hotkeys;
using ClipStudioDesktop.Services.Settings;
using ClipStudioDesktop.Services.Recording;
using ClipStudioDesktop.ViewModels;

using ClipStudioDesktop.Helpers;

namespace ClipStudioDesktop
{
    public partial class App : System.Windows.Application
    {
        private TaskbarIcon? _taskbarIcon;
        private ISettingsService _settingsService;
        private IHotKeyService _hotKeyService;
        private IRecordingService _recordingService;
        private IStorageService _storageService;
        private IScreenshotService _screenshotService;
        private MainViewModel _mainViewModel;
        private Views.MainWindow _mainWindow;
        // We need a window to attach hotkeys to, even if hidden
        private Window _messageWindow;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Global exception handling
            this.DispatcherUnhandledException += (s, args) =>
            {
                System.Windows.MessageBox.Show($"An unhandled exception occurred: {args.Exception.Message}\n\nStack Trace:\n{args.Exception.StackTrace}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                args.Handled = true;
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                System.Windows.MessageBox.Show($"An unobserved task exception occurred: {args.Exception.Message}\n\nStack Trace:\n{args.Exception.StackTrace}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                args.SetObserved();
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var exception = args.ExceptionObject as Exception;
                System.Windows.MessageBox.Show($"A critical unhandled exception occurred: {exception?.Message}\n\nStack Trace:\n{exception?.StackTrace}", "Critical Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            };

            // Initialize Services
            _settingsService = new SettingsService();
            _hotKeyService = new HotKeyService();
            _storageService = new StorageService(_settingsService);
            _recordingService = new RecordingService(_settingsService, _storageService);
            _screenshotService = new ScreenshotService(_storageService, _settingsService);

            // Apply Startup Setting
            StartupHelper.SetStartup(_settingsService.CurrentSettings.General.StartWithWindows);

            // Initialize ViewModel and Window
            _mainViewModel = new MainViewModel(_settingsService, _storageService, _recordingService);
            _mainWindow = new Views.MainWindow(_mainViewModel);

            // Ensure directories
            _storageService.EnsureDirectoriesExist();

            // Create a hidden window to handle messages
            _messageWindow = new Window
            {
                Width = 0,
                Height = 0,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                Visibility = Visibility.Hidden
            };
            _messageWindow.Show(); // Must show to get handle, but it's 0x0 and hidden
            var handle = new WindowInteropHelper(_messageWindow).Handle;
            _hotKeyService.Initialize(handle);

            RegisterConfiguredHotkeys();

            // Start Recording
            await _recordingService.StartRecordingAsync();

            // Create the TaskbarIcon
            _taskbarIcon = new TaskbarIcon();
            _taskbarIcon.ToolTipText = "Clip Studio Desktop";
            
            // Use a default system icon since we don't have a custom one yet
            _taskbarIcon.Icon = SystemIcons.Application;

            // Create Context Menu
            var contextMenu = new System.Windows.Controls.ContextMenu();

            // Pause/Resume
            var toggleItem = new System.Windows.Controls.MenuItem();
            toggleItem.Header = "Pausar Grabación";
            toggleItem.Click += async (s, args) => 
            {
                if (_recordingService.IsRecording)
                {
                    await _recordingService.StopRecordingAsync();
                    toggleItem.Header = "Reanudar Grabación";
                }
                else
                {
                    await _recordingService.StartRecordingAsync();
                    toggleItem.Header = "Pausar Grabación";
                }
            };
            contextMenu.Items.Add(toggleItem);

            // Open Folders
            var openFoldersItem = new System.Windows.Controls.MenuItem();
            openFoldersItem.Header = "Abrir carpeta de clips";
            
            var openAudio = new System.Windows.Controls.MenuItem { Header = "Audio" };
            openAudio.Click += (s, args) => System.Diagnostics.Process.Start("explorer.exe", _storageService.GetAudioFolder());
            openFoldersItem.Items.Add(openAudio);

            var openVideo = new System.Windows.Controls.MenuItem { Header = "Video" };
            openVideo.Click += (s, args) => System.Diagnostics.Process.Start("explorer.exe", _storageService.GetVideoFolder());
            openFoldersItem.Items.Add(openVideo);

            var openImages = new System.Windows.Controls.MenuItem { Header = "Imágenes" };
            openImages.Click += (s, args) => System.Diagnostics.Process.Start("explorer.exe", _storageService.GetImageFolder());
            openFoldersItem.Items.Add(openImages);

            contextMenu.Items.Add(openFoldersItem);

            contextMenu.Items.Add(new System.Windows.Controls.Separator());
            
            var configItem = new System.Windows.Controls.MenuItem();
            configItem.Header = "Configuración";
            configItem.Click += (s, args) => OpenConfiguration();
            contextMenu.Items.Add(configItem);

            contextMenu.Items.Add(new System.Windows.Controls.Separator());

            var exitItem = new System.Windows.Controls.MenuItem();
            exitItem.Header = "Salir";
            exitItem.Click += (s, args) => Shutdown();
            contextMenu.Items.Add(exitItem);

            _taskbarIcon.ContextMenu = contextMenu;
            
            // Handle double click to open config
            _taskbarIcon.TrayMouseDoubleClick += (s, args) => OpenConfiguration();
        }

        private void RegisterConfiguredHotkeys()
        {
            foreach (var hotkey in _settingsService.CurrentSettings.Hotkeys)
            {
                try 
                {
                    _hotKeyService.RegisterHotKey(hotkey.Key, async () => 
                    {
                        if (hotkey.Type == "audio")
                        {
                            await _recordingService.SaveClipAsync(hotkey.Duration, false);
                        }
                        else if (hotkey.Type == "video")
                        {
                            await _recordingService.SaveClipAsync(hotkey.Duration, true);
                        }
                        else if (hotkey.Type == "screenshot")
                        {
                            if (hotkey.Mode == "selection")
                            {
                                await _screenshotService.CaptureSelectionAsync();
                            }
                            else
                            {
                                await _screenshotService.CaptureFullScreenAsync();
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    // Log error registering hotkey
                    System.Diagnostics.Debug.WriteLine($"Failed to register hotkey {hotkey.Key}: {ex.Message}");
                }
            }
        }

        private void OpenConfiguration()
        {
            _mainWindow.Show();
            _mainWindow.Activate();
            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _taskbarIcon?.Dispose();
            (_hotKeyService as IDisposable)?.Dispose();
            _recordingService?.Dispose();
            base.OnExit(e);
        }
    }
}
