using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
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
using NAudio.Wave;

namespace ClipStudioDesktop
{
    public partial class App : System.Windows.Application
    {
        private static Mutex? _mutex;
        private TaskbarIcon? _taskbarIcon;
        private ISettingsService _settingsService = null!;
        private IHotKeyService _hotKeyService = null!;
        private IRecordingService _recordingService = null!;
        private IStorageService _storageService = null!;
        private IScreenshotService _screenshotService = null!;
        private MainViewModel _mainViewModel = null!;
        private Views.MainWindow _mainWindow = null!;
        // We need a window to attach hotkeys to, even if hidden
        private Window _messageWindow = null!;
        private string? _lastSavedFilePath;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Single instance check
            // Single instance check with Retry for Auto-Restart
            const string mutexName = "ClipStudioDesktop_SingleInstance_Mutex";
            bool createdNew = false;
            
            for (int i = 0; i < 10; i++) // Try for up to 2 seconds (10 * 200ms)
            {
                try
                {
                    _mutex = new Mutex(true, mutexName, out createdNew);
                    if (createdNew) break;
                    
                    // If mutex exists but belongs to another process, dispose our handle and wait
                    _mutex.Dispose();
                    _mutex = null;
                    System.Threading.Thread.Sleep(200);
                }
                catch
                {
                   System.Threading.Thread.Sleep(200);
                }
            }

            if (_mutex == null || !createdNew)
            {
                // Ya hay una instancia ejecutándose
                // Don't show message box on silent failures/restarts if desirable, but keeping it for now
                // System.Windows.MessageBox.Show("Clip Studio Desktop ya está en ejecución.", "Aplicación ya iniciada", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                Shutdown();
                return;
            }
            
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
            _screenshotService = new ScreenshotService(_storageService, _settingsService, _hotKeyService);

            // Apply Startup Setting
            StartupHelper.SetStartup(_settingsService.CurrentSettings.General.StartWithWindows);

            // Initialize ViewModel and Window
            _mainViewModel = new MainViewModel(_settingsService, _storageService, _recordingService);
            _mainWindow = new Views.MainWindow(_mainViewModel);
            _mainWindow.Show();

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

            // Start Recording (Disabled by default)
            // await _recordingService.StartRecordingAsync();

            // Create the TaskbarIcon
            _taskbarIcon = new TaskbarIcon();
            _taskbarIcon.ToolTipText = "Clip Studio Desktop";
            
            // Load custom icon
            try 
            {
                var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "Clip_Studio_Desktop_ico.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    _taskbarIcon.Icon = new System.Drawing.Icon(iconPath);
                }
                else
                {
                    _taskbarIcon.Icon = SystemIcons.Application;
                }
            }
            catch
            {
                _taskbarIcon.Icon = SystemIcons.Application;
            }

            _taskbarIcon.TrayBalloonTipClicked += async (s, args) =>
            {
                if (!string.IsNullOrEmpty(_lastSavedFilePath) && System.IO.File.Exists(_lastSavedFilePath))
                {
                    await Task.Delay(100); // Tiny delay for visual smoothness
                    ShowFileInExplorer(_lastSavedFilePath);
                }
            };

            // Create Context Menu
            var contextMenu = new System.Windows.Controls.ContextMenu();

            // Video Recording
            var videoItem = new System.Windows.Controls.MenuItem();
            videoItem.Header = "Grabar Video";
            
            // Audio Recording
            var audioItem = new System.Windows.Controls.MenuItem();
            audioItem.Header = "Grabar Audio";

            // Local Helper to update menu state
            void UpdateMenuState(bool isRecording)
            {
                bool isVideoMode = _recordingService.IsVideoMode;
                
                if (!isRecording)
                {
                    videoItem.Header = "Grabar Video";
                    videoItem.IsEnabled = true;
                    
                    audioItem.Header = "Grabar Audio";
                    audioItem.IsEnabled = true;
                }
                else
                {
                    if (isVideoMode)
                    {
                        videoItem.Header = "Detener Video";
                        videoItem.IsEnabled = true;
                        
                        audioItem.Header = "Grabar Audio";
                        audioItem.IsEnabled = false; // Mutually exclusive
                    }
                    else
                    {
                        videoItem.Header = "Grabar Video";
                        videoItem.IsEnabled = false; // Mutually exclusive
                        
                        audioItem.Header = "Detener Audio";
                        audioItem.IsEnabled = true;
                    }
                }
            }

            // Subscribe to state changes
            _recordingService.RecordingStateChanged += (s, isRecording) => 
            {
                this.Dispatcher.Invoke(() => UpdateMenuState(isRecording));
            };

            // Subscribe to ClipSaved event
            _recordingService.ClipSaved += (s, path) =>
            {
                this.Dispatcher.Invoke(() =>
                {
                    string fileName = System.IO.Path.GetFileName(path);
                    string title = "Clip Guardado";
                    
                    if (fileName.Contains("Audio")) title = "Clip De Audio Guardado";
                    else if (fileName.Contains("Video")) title = "Clip De Video Guardado";

                    ShowNotification(title, $"Clip guardado exitosamente:\n{fileName}", path);
                });
            };

            // Subscribe to ScreenshotSaved event
            _screenshotService.ScreenshotSaved += (s, path) =>
            {
                this.Dispatcher.Invoke(() =>
                {
                    string fileName = System.IO.Path.GetFileName(path);
                    string title = "Captura Guardada";

                    if (fileName.Contains("Completa")) title = "Captura De Pantalla Guardada";
                    else if (fileName.Contains("Seleccion")) title = "Captura De Seleccion Guardada";

                    ShowNotification(title, $"Captura guardada exitosamente:\n{fileName}", path);
                });
            };

            // Click Handlers
            videoItem.Click += async (s, args) => 
            {
                await _recordingService.ToggleRecordingAsync(videoEnabled: true);
            };
            
            audioItem.Click += async (s, args) => 
            {
                await _recordingService.ToggleRecordingAsync(videoEnabled: false);
            };

            // Initial State
            UpdateMenuState(_recordingService.IsRecording);

            contextMenu.Items.Add(videoItem);
            contextMenu.Items.Add(audioItem);

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
            
            var configItem = new System.Windows.Controls.MenuItem();
            configItem.Header = "Configuración";
            configItem.Click += (s, args) => OpenConfiguration();
            contextMenu.Items.Add(configItem);

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
            int successCount = 0;
            int failCount = 0;
            
            foreach (var hotkey in _settingsService.CurrentSettings.Hotkeys)
            {
                try 
                {
                    _hotKeyService.RegisterHotKey(hotkey.Key, async () => 
                    {
                        try
                        {
                            if (hotkey.Type == "audio")
                            {
                                await _recordingService.ToggleRecordingAsync(false);
                            }
                            else if (hotkey.Type == "video")
                            {
                                await _recordingService.ToggleRecordingAsync(true);
                            }
                            else if (hotkey.Type == "recording")
                            {
                                await _recordingService.ToggleRecordingAsync(true);
                            }
                            else if (hotkey.Type == "screenshot")
                            {
                                bool success = true;
                                if (hotkey.Mode == "selection")
                                {
                                    success = await _screenshotService.CaptureSelectionAsync();
                                }
                                else if (hotkey.Mode == "selection_clipboard")
                                {
                                    success = await _screenshotService.CaptureSelectionToClipboardAsync();
                                }
                                else
                                {
                                    await _screenshotService.CaptureFullScreenAsync();
                                }
                                
                                if (success)
                                {
                                    PlayNotificationSound();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[App.xaml.cs] Exception in hotkey handler: {ex.Message}\n{ex.StackTrace}");
                            System.Windows.MessageBox.Show($"Error al ejecutar atajo: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    });
                    successCount++;
                }
                catch (Exception ex)
                {
                    // Log error registering hotkey
                    failCount++;
                    System.Diagnostics.Debug.WriteLine($"Failed to register hotkey {hotkey.Key}: {ex.Message}");
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"Hotkeys registered: {successCount} success, {failCount} failed");
        }

        private void ShowNotification(string title, string message, string? filePath = null)
        {
            if (_settingsService.CurrentSettings.General.ShowNotifications && _taskbarIcon != null)
            {
                _lastSavedFilePath = filePath;
                _taskbarIcon.ShowBalloonTip(title, message, BalloonIcon.Info);
            }
        }

        private void OpenConfiguration()
        {
            _mainWindow.Show();
            _mainWindow.Activate();
            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;
        }

        private void PlayNotificationSound()
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
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error playing sound: {ex.Message}");
                    }
                });
            }
        }

        #region Shell API for Smooth Explorer Transition
        [DllImport("shell32.dll", ExactSpelling = true)]
        private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr apidl, uint dwFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ILCreateFromPath(string pszPath);

        [DllImport("shell32.dll")]
        private static extern void ILFree(IntPtr pidl);

        private void ShowFileInExplorer(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath)) return;

            IntPtr pidl = ILCreateFromPath(filePath);
            if (pidl != IntPtr.Zero)
            {
                try
                {
                    SHOpenFolderAndSelectItems(pidl, 0, IntPtr.Zero, 0);
                }
                finally
                {
                    ILFree(pidl);
                }
            }
        }
        #endregion

        protected override void OnExit(ExitEventArgs e)
        {
            _taskbarIcon?.Dispose();
            (_hotKeyService as IDisposable)?.Dispose();
            _recordingService?.Dispose();
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
