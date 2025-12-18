using System;
using System.Threading;
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

        protected override void OnStartup(StartupEventArgs e)
        {
            // Single instance check
            const string mutexName = "ClipStudioDesktop_SingleInstance_Mutex";
            _mutex = new Mutex(true, mutexName, out bool createdNew);
            
            if (!createdNew)
            {
                // Ya hay una instancia ejecutándose
                System.Windows.MessageBox.Show("Clip Studio Desktop ya está en ejecución.", "Aplicación ya iniciada", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
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

            // Create Context Menu
            var contextMenu = new System.Windows.Controls.ContextMenu();

            // Pause/Resume
            var toggleItem = new System.Windows.Controls.MenuItem();
            toggleItem.Header = _recordingService.IsRecording ? "Pausar Grabación" : "Iniciar Grabación";
            
            // Subscribe to state changes to keep menu in sync
            _recordingService.RecordingStateChanged += (s, isRecording) => 
            {
                this.Dispatcher.Invoke(() => 
                {
                    toggleItem.Header = isRecording ? "Pausar Grabación" : "Iniciar Grabación";
                });
            };

            toggleItem.Click += async (s, args) => 
            {
                if (_recordingService.IsRecording)
                {
                    await _recordingService.StopRecordingAsync();
                }
                else
                {
                    await _recordingService.StartRecordingAsync();
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
                        // Show processing window for clips
                        Views.ProcessingWindow? processingWindow = null;
                        if (hotkey.Type == "audio" || hotkey.Type == "video")
                        {
                            if (!_recordingService.IsRecording)
                            {
                                System.Windows.MessageBox.Show("La grabación no está activa. Actívela desde el menú de estado o la bandeja del sistema.", "Grabación Inactiva", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }

                            // Ensure UI thread
                            System.Windows.Application.Current.Dispatcher.Invoke(() => 
                            {
                                processingWindow = new Views.ProcessingWindow();
                                processingWindow.Show();
                            });
                        }

                        try
                        {
                            System.Diagnostics.Debug.WriteLine($"[App.xaml.cs] Starting SaveClipAsync - Type: {hotkey.Type}, Duration: {hotkey.Duration}s");
                            
                            if (hotkey.Type == "audio")
                            {
                                var saveTask = _recordingService.SaveClipAsync(hotkey.Duration, false);
                                var timeoutTask = System.Threading.Tasks.Task.Delay(30000); // 30 second timeout
                                var completedTask = await System.Threading.Tasks.Task.WhenAny(saveTask, timeoutTask);
                                
                                if (completedTask == timeoutTask)
                                {
                                    System.Diagnostics.Debug.WriteLine("[App.xaml.cs] Audio SaveClipAsync TIMEOUT");
                                    System.Windows.MessageBox.Show("El guardado de audio tardó demasiado y se canceló.", "Timeout", MessageBoxButton.OK, MessageBoxImage.Warning);
                                }
                                else
                                {
                                    await saveTask; // Re-await to get exceptions
                                    System.Diagnostics.Debug.WriteLine("[App.xaml.cs] Audio SaveClipAsync completed");
                                }
                            }
                            else if (hotkey.Type == "video")
                            {
                                var saveTask = _recordingService.SaveClipAsync(hotkey.Duration, true);
                                var timeoutTask = System.Threading.Tasks.Task.Delay(30000); // 30 second timeout
                                var completedTask = await System.Threading.Tasks.Task.WhenAny(saveTask, timeoutTask);
                                
                                if (completedTask == timeoutTask)
                                {
                                    System.Diagnostics.Debug.WriteLine("[App.xaml.cs] Video SaveClipAsync TIMEOUT");
                                    System.Windows.MessageBox.Show("El guardado de video tardó demasiado y se canceló.", "Timeout", MessageBoxButton.OK, MessageBoxImage.Warning);
                                }
                                else
                                {
                                    await saveTask; // Re-await to get exceptions
                                    System.Diagnostics.Debug.WriteLine("[App.xaml.cs] Video SaveClipAsync completed");
                                }
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
                        finally
                        {
                            System.Diagnostics.Debug.WriteLine("[App.xaml.cs] Entering finally block");
                            if (processingWindow != null)
                            {
                                try
                                {
                                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        System.Diagnostics.Debug.WriteLine("[App.xaml.cs] Closing ProcessingWindow");
                                        processingWindow.Close();
                                        System.Diagnostics.Debug.WriteLine("[App.xaml.cs] ProcessingWindow closed successfully");
                                    });
                                }
                                catch (Exception closeEx)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[App.xaml.cs] Error closing window: {closeEx.Message}");
                                }
                            }
                            System.Diagnostics.Debug.WriteLine("[App.xaml.cs] Finally block completed");
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
