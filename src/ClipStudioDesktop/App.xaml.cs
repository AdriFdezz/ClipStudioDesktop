using System;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using System.Drawing;
using ClipStudioDesktop.Services.Screenshot;
using ClipStudioDesktop.ViewModels;

namespace ClipStudioDesktop
{
    public partial class App : Application
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

            // Initialize Services
            _settingsService = new SettingsService();
            _hotKeyService = new HotKeyService();
            _storageService = new StorageService(_settingsService);
            _recordingService = new RecordingService(_settingsService, _storageService);
            _screenshotService = new ScreenshotService(_storageService, _settingsService);

            // Initialize ViewModel and Window
            _mainViewModel = new MainViewModel(_settingsService, _storageService);
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
            _recordingService?.StopRecordingAsync();
            base.OnExit(e);
        }
    }
}
