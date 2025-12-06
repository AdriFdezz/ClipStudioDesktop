using System;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using System.Drawing;
using ClipStudioDesktop.Services;
using System.Windows.Interop;

namespace ClipStudioDesktop
{
    public partial class App : Application
    {
        private TaskbarIcon? _taskbarIcon;
        private ISettingsService _settingsService;
        private IHotKeyService _hotKeyService;
        // We need a window to attach hotkeys to, even if hidden
        private Window _messageWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Initialize Services
            _settingsService = new SettingsService();
            _hotKeyService = new HotKeyService();

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
                    _hotKeyService.RegisterHotKey(hotkey.Key, () => 
                    {
                        // Placeholder action
                        MessageBox.Show($"Hotkey pressed: {hotkey.Key} ({hotkey.Type})");
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
            foreach (Window window in Windows)
            {
                if (window is Views.MainWindow)
                {
                    window.Show();
                    window.Activate();
                    if (window.WindowState == WindowState.Minimized)
                        window.WindowState = WindowState.Normal;
                    return;
                }
            }

            var mainWindow = new Views.MainWindow();
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _taskbarIcon?.Dispose();
            (_hotKeyService as IDisposable)?.Dispose();
            base.OnExit(e);
        }
    }
}
