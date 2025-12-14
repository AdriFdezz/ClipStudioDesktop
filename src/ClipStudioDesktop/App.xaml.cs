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
using NAudio.Wave;

namespace ClipStudioDesktop
{
    public partial class App : System.Windows.Application
    {
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
                        // DEBUG: Uncomment to verify hotkey trigger
                        // System.Windows.MessageBox.Show($"Atajo detectado: {hotkey.Key} ({hotkey.Type})");

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
                            if (hotkey.Type == "audio")
                            {
                                await _recordingService.SaveClipAsync(hotkey.Duration, false);
                                PlayNotificationSound();
                            }
                            else if (hotkey.Type == "video")
                            {
                                await _recordingService.SaveClipAsync(hotkey.Duration, true);
                                PlayNotificationSound();
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
                            System.Windows.MessageBox.Show($"Error al ejecutar atajo: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        finally
                        {
                            if (processingWindow != null)
                            {
                                System.Windows.Application.Current.Dispatcher.Invoke(() => processingWindow.Close());
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    // Log error registering hotkey
                    System.Windows.MessageBox.Show($"Error al registrar atajo {hotkey.Key}: {ex.Message}\nEs posible que otra aplicación lo esté usando.", "Error de Atajo", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            base.OnExit(e);
        }
    }
}
