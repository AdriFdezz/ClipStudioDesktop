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
    /// <summary>
    /// Lógica de interacción principal para App.xaml.
    /// Gestiona el ciclo de vida de la aplicación, instancia única (Mutex),
    /// inicialización de servicios (DI manual) y el icono de la bandeja del sistema.
    /// </summary>
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
        // Necesitamos una ventana para adjuntar los atajos de teclado, incluso si está oculta
        private Window _messageWindow = null!;
        private string? _lastSavedFilePath;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Verificación de Instancia Única y Reintentos para Reinicio Automático
            const string mutexName = "ClipStudioDesktop_SingleInstance_Mutex";
            bool createdNew = false;
            
            for (int i = 0; i < 10; i++) // Intentar por hasta 2 segundos (10 * 200ms)
            {
                try
                {
                    _mutex = new Mutex(true, mutexName, out createdNew);
                    if (createdNew) break;
                    
                    // Si el mutex existe pero pertenece a otro proceso, liberar y esperar
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
                // No mostramos MessageBox en fallos silenciosos/reinicios si se desea, pero lo mantenemos por ahora comentado

                // System.Windows.MessageBox.Show("Clip Studio Desktop ya está en ejecución.", "Aplicación ya iniciada", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                Shutdown();
                return;
            }
            
            base.OnStartup(e);

            // Manejo Global de Excepciones
            // Captura excepciones en el hilo de la interfaz de usuario (UI Thread)
            this.DispatcherUnhandledException += (s, args) =>
            {
                System.Windows.MessageBox.Show($"Ocurrió una excepción no controlada: {args.Exception.Message}\n\nTraza de la pila:\n{args.Exception.StackTrace}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                args.Handled = true;
            };

            // Captura excepciones en tareas asíncronas (Task) no observadas
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                System.Windows.MessageBox.Show($"Ocurrió una excepción de tarea no observada: {args.Exception.Message}\n\nTraza de la pila:\n{args.Exception.StackTrace}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                args.SetObserved();
            };

            // Captura excepciones críticas en el dominio de la aplicación (AppDomain) que no fueron capturadas antes
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var exception = args.ExceptionObject as Exception;
                System.Windows.MessageBox.Show($"Ocurrió una excepción crítica no controlada: {exception?.Message}\n\nTraza de la pila:\n{exception?.StackTrace}", "Error Crítico", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            };

            // Inicializar Servicios (DI Manual)
            _settingsService = new SettingsService();
            _hotKeyService = new HotKeyService();
            _storageService = new StorageService(_settingsService);
            _recordingService = new RecordingService(_settingsService, _storageService);
            _screenshotService = new ScreenshotService(_storageService, _settingsService, _hotKeyService);

            // Aplicar configuración de inicio
            StartupHelper.SetStartup(_settingsService.CurrentSettings.General.StartWithWindows);

            // Inicializar ViewModel y Ventana Principal
            _mainViewModel = new MainViewModel(_settingsService, _storageService, _recordingService);
            _mainWindow = new Views.MainWindow(_mainViewModel);
            
            // Solo mostrar la ventana si se inició con --show-ui (reinicio desde configuración)
            // De lo contrario, iniciar en segundo plano (solo bandeja del sistema)
            bool showUI = e.Args.Contains("--show-ui");
            if (showUI)
            {
                _mainWindow.Show();
            }

            // Asegurar existencia de directorios
            _storageService.EnsureDirectoriesExist();

            // Crear una ventana oculta para manejar mensajes de Windows (Identificadores de ventana)
            _messageWindow = new Window
            {
                Width = 0,
                Height = 0,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                Visibility = Visibility.Hidden
            };
            _messageWindow.Show(); // Debe mostrarse para obtener el handle, pero es 0x0 y oculta
            var handle = new WindowInteropHelper(_messageWindow).Handle;
            _hotKeyService.Initialize(handle);

            RegisterConfiguredHotkeys();

            // Iniciar grabación (Deshabilitado por defecto)
            // await _recordingService.StartRecordingAsync();

            // Crear el Icono de la Bandeja del Sistema (TaskbarIcon)
            _taskbarIcon = new TaskbarIcon();
            _taskbarIcon.ToolTipText = "Clip Studio Desktop";
            
            // Cargar icono personalizado

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
                    await Task.Delay(100); // Pequeño delay para suavidad visual
                    ShowFileInExplorer(_lastSavedFilePath);
                }
            };

            // Crear Menú Contextual (Tray Menu)
            var contextMenu = new System.Windows.Controls.ContextMenu();

            // Opción: Grabar Video
            var videoItem = new System.Windows.Controls.MenuItem();
            videoItem.Header = "Grabar Video";
            
            // Opción: Grabar Audio
            var audioItem = new System.Windows.Controls.MenuItem();
            audioItem.Header = "Grabar Audio";

            // Función local para actualizar estado del menú
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
                        audioItem.IsEnabled = false; // Mutualmente exclusivo
                    }
                    else
                    {
                        videoItem.Header = "Grabar Video";
                        videoItem.IsEnabled = false; // Mutualmente exclusivo
                        
                        audioItem.Header = "Detener Audio";
                        audioItem.IsEnabled = true;
                    }
                }
            }

            // Suscribirse a cambios de estado
            _recordingService.RecordingStateChanged += (s, isRecording) => 
            {
                this.Dispatcher.Invoke(() => UpdateMenuState(isRecording));
            };

            // Suscribirse a evento ClipSaved
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

            // Suscribirse a evento ScreenshotSaved
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

            // Suscribirse a evento ClipboardCopied
            _screenshotService.ClipboardCopied += (s, e) =>
            {
                this.Dispatcher.Invoke(() =>
                {
                    ShowNotification("Captura Copiada", "Selección copiada al portapapeles");
                });
            };

            // Suscribirse a evento BeforeCapture para ocultar notificaciones antes de la captura
            _screenshotService.BeforeCapture += (s, e) =>
            {
                this.Dispatcher.Invoke(() =>
                {
                    // Cancelar timer de cierre automático y ocultar inmediatamente
                    _notificationCts?.Cancel();
                    _taskbarIcon?.HideBalloonTip();
                });
            };

            // Manejadores de Clic
            videoItem.Click += async (s, args) => 
            {
                await _recordingService.ToggleRecordingAsync(videoEnabled: true);
            };
            
            audioItem.Click += async (s, args) => 
            {
                await _recordingService.ToggleRecordingAsync(videoEnabled: false);
            };

            // Estado Inicial
            UpdateMenuState(_recordingService.IsRecording);

            contextMenu.Items.Add(videoItem);
            contextMenu.Items.Add(audioItem);

            // Submenú: Abrir Carpetas
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
            
            // Manejar doble clic para abrir configuración
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
                            System.Diagnostics.Debug.WriteLine($"[App.xaml.cs] Excepción en manejador de atajo: {ex.Message}\n{ex.StackTrace}");
                            System.Windows.MessageBox.Show($"Error al ejecutar atajo: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    });
                    successCount++;
                }
                catch (Exception ex)
                {
                    // Registrar error al registrar atajo
                    failCount++;
                    System.Diagnostics.Debug.WriteLine($"Error al registrar atajo {hotkey.Key}: {ex.Message}");
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"Atajos registrados: {successCount} exitosos, {failCount} fallidos");
        }

        private System.Threading.CancellationTokenSource? _notificationCts;

        private async void ShowNotification(string title, string message, string? filePath = null)
        {
            if (_settingsService.CurrentSettings.General.ShowNotifications && _taskbarIcon != null)
            {
                // Cancelar cualquier notificación/temporizador previo
                _notificationCts?.Cancel();
                _notificationCts = new System.Threading.CancellationTokenSource();
                var token = _notificationCts.Token;

                _lastSavedFilePath = filePath;
                
                try
                {
                    // 1. Ocultar notificación anterior inmediatamente
                    _taskbarIcon.HideBalloonTip();
                    
                    // Pequeña espera para asegurar que Windows procese el cierre (evita "glitches" si es muy rápido)
                    await Task.Delay(50, token);
                    
                    // 2. Mostrar nueva notificación
                    _taskbarIcon.ShowBalloonTip(title, message, BalloonIcon.Info);

                    // 3. Esperar 3 segundos
                    await Task.Delay(3000, token);

                    // 4. Ocultar si nadie más nos canceló
                    if (!token.IsCancellationRequested)
                    {
                        _taskbarIcon.HideBalloonTip();
                    }
                }
                catch (TaskCanceledException)
                {
                    // Ignorar, significa que una nueva notificación tomó el control
                }
            }
        }

        /// <summary>
        /// Abre la ventana principal de configuración.
        /// Si está minimizada, la restaura.
        /// </summary>
        private void OpenConfiguration()
        {
            _mainWindow.Show();
            _mainWindow.Activate();
            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;
        }

        /// <summary>
        /// Reproduce un sonido de notificación si está habilitado en la configuración.
        /// </summary>
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
                        System.Diagnostics.Debug.WriteLine($"Error al reproducir sonido: {ex.Message}");
                    }
                });
            }
        }

        #region API Shell para transición suave en Explorador
        [DllImport("shell32.dll", ExactSpelling = true)]
        private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr apidl, uint dwFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ILCreateFromPath(string pszPath);

        [DllImport("shell32.dll")]
        private static extern void ILFree(IntPtr pidl);

        /// <summary>
        /// Abre el explorador de archivos con el archivo especificado seleccionado.
        /// </summary>
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

        /// <summary>
        /// Método llamado al salir de la aplicación.
        /// Libera recursos críticos como el icono de la bandeja, servicios y el Mutex.
        /// </summary>
        /// <param name="e">Argumentos del evento de salida.</param>
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
