using ClipStudioDesktop.Views;
using ClipStudioDesktop.Services.Storage;
using ClipStudioDesktop.Services.Settings;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace ClipStudioDesktop.Services.Screenshot
{
    /// <summary>
    /// Implementación del servicio de capturas de pantalla.
    /// Utiliza GDI+ para las capturas de bajo nivel y WPF para la interacción de selección.
    /// </summary>
    public class ScreenshotService : IScreenshotService
    {
        private readonly IStorageService _storageService;
        private readonly ISettingsService _settingsService;
        private readonly Services.Hotkeys.IHotKeyService _hotKeyService;

        public ScreenshotService(IStorageService storageService, ISettingsService settingsService, Services.Hotkeys.IHotKeyService hotKeyService)
        {
            _storageService = storageService;
            _settingsService = settingsService;
            _hotKeyService = hotKeyService;
        }

        /// <summary>
        /// Captura la pantalla completa basándose en la configuración actual (monitor específico o primario).
        /// </summary>
        public async Task CaptureFullScreenAsync()
        {
            try
            {
                var settings = _settingsService.CurrentSettings.Screenshot;
                int x = 0, y = 0, width = 0, height = 0;

                // Determinar límites de la captura según configuración
                // Prioridad: Configuración Global de Monitor (salvo que se pida "all" explícitamente en Screenshot settings si existiera esa opción avanzada)
                
                int globalMonitorIdx = _settingsService.CurrentSettings.Monitor.SelectedMonitorIndex;
                var screens = System.Windows.Forms.Screen.AllScreens;

                if (settings.Monitor == "all") 
                {
                    // "all" sigue funcionando si el usuario lo forzó en config manual, 
                    // pero por defecto usaremos el monitor seleccionado global.
                    x = (int)SystemParameters.VirtualScreenLeft;
                    y = (int)SystemParameters.VirtualScreenTop;
                    width = (int)SystemParameters.VirtualScreenWidth;
                    height = (int)SystemParameters.VirtualScreenHeight;
                }
                else
                {
                    // Usar Monitor Seleccionado Globalmente
                    if (globalMonitorIdx >= 0 && globalMonitorIdx < screens.Length)
                    {
                        var screen = screens[globalMonitorIdx];
                        x = screen.Bounds.X;
                        y = screen.Bounds.Y;
                        width = screen.Bounds.Width;
                        height = screen.Bounds.Height;
                    }
                    else
                    {
                        // Fallback al primario
                        var screen = System.Windows.Forms.Screen.PrimaryScreen;
                        if (screen != null)
                        {
                            x = screen.Bounds.X;
                            y = screen.Bounds.Y;
                            width = screen.Bounds.Width;
                            height = screen.Bounds.Height;
                        }
                    }
                }

                if (width == 0 || height == 0) return;

                // Invocar evento para ocultar notificaciones antes de capturar
                BeforeCapture?.Invoke(this, EventArgs.Empty);
                await Task.Delay(100); // Dar tiempo a Windows para ocultar el balloon

                // Realizar captura con GDI+
                using (Bitmap bitmap = new Bitmap(width, height))
                {
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        g.CopyFromScreen(x, y, 0, 0, bitmap.Size);
                    }

                    SaveScreenshot(bitmap, "Captura_Pantalla_Completa");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error capturing full screen: {ex.Message}");
            }
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        public Task<bool> CaptureSelectionAsync() => PerformSelectionCaptureAsync(true);

        public Task<bool> CaptureSelectionToClipboardAsync() => PerformSelectionCaptureAsync(false);

        /// <summary>
        /// Ejecuta el flujo de captura de región:
        /// 1. Toma una "foto" instantánea de todo el escritorio.
        /// 2. Muestra una ventana transparente sobre todas las pantallas (<see cref="SelectionWindow"/>).
        /// 3. Permite al usuario dibujar el rectángulo de recorte.
        /// 4. Recorta la imagen original y la procesa (Guardar o Copiar).
        /// </summary>
        private async Task<bool> PerformSelectionCaptureAsync(bool saveToFile)
        {
            bool success = false;
            try
            {
                // 1. Capturar todo el espacio de pantalla virtual para usar como fondo en la selección
                int screenLeft = (int)SystemParameters.VirtualScreenLeft;
                int screenTop = (int)SystemParameters.VirtualScreenTop;
                int screenWidth = (int)SystemParameters.VirtualScreenWidth;
                int screenHeight = (int)SystemParameters.VirtualScreenHeight;

                // Invocar evento para ocultar notificaciones antes de capturar
                BeforeCapture?.Invoke(this, EventArgs.Empty);
                await Task.Delay(100); // Dar tiempo a Windows para ocultar el balloon

                Bitmap fullScreenBitmap = new Bitmap(screenWidth, screenHeight);
                using (Graphics g = Graphics.FromImage(fullScreenBitmap))
                {
                    g.CopyFromScreen(screenLeft, screenTop, 0, 0, fullScreenBitmap.Size);
                }

                // Convertir GDI Bitmap a WPF BitmapSource para mostrarlo en la ventana de selección
                IntPtr hBitmap = fullScreenBitmap.GetHbitmap();
                BitmapSource bitmapSource;
                try
                {
                    bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                }
                finally
                {
                    DeleteObject(hBitmap);
                }

                // Suspender hotkeys para evitar interferencias durante la selección con mouse
                if (_hotKeyService != null) _hotKeyService.IsSuspended = true;

                // 2. Mostrar Ventana de Selección (en Thread UI)
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var selectionWindow = new SelectionWindow(bitmapSource);
                    
                    // Posidionar cubriendo todo el escritorio virtual
                    selectionWindow.Left = screenLeft;
                    selectionWindow.Top = screenTop;
                    selectionWindow.Width = screenWidth;
                    selectionWindow.Height = screenHeight;

                    // Mostrar como diálogo modal
                    selectionWindow.ShowDialog();

                    if (selectionWindow.IsConfirmed)
                    {
                        var rect = selectionWindow.SelectedRegion;
                        
                        // Recortar el bitmap original usando las coordenadas relativas
                        var cropRect = new Rectangle((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
                        using (Bitmap cropped = fullScreenBitmap.Clone(cropRect, fullScreenBitmap.PixelFormat))
                        {
                            if (saveToFile)
                            {
                                SaveScreenshot(cropped, "Captura_de_Seleccion");
                            }
                            else
                            {
                                CopyToClipboard(cropped);
                            }
                        }
                        success = true;
                    }
                });

                fullScreenBitmap.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error capturing selection: {ex.Message}");
            }
            finally
            {
                 // Reactivar atajos
                 if (_hotKeyService != null) _hotKeyService.IsSuspended = false;
            }
            return success;
        }

        private void CopyToClipboard(Bitmap bitmap)
        {
            try
            {
                IntPtr hBitmap = bitmap.GetHbitmap();
                try
                {
                    // Convertir a BitmapSource para el Portapapeles de WPF
                    var source = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    
                    System.Windows.Clipboard.SetImage(source);
                    PlayShutterSound();
                    ClipboardCopied?.Invoke(this, EventArgs.Empty);
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error copying to clipboard: {ex.Message}");
            }
        }

        public event EventHandler<string>? ScreenshotSaved;
        public event EventHandler? ClipboardCopied;
        public event EventHandler? BeforeCapture;

        /// <summary>
        /// Guarda el bitmap capturado en disco, aplicando el formato seleccionado (JPG/PNG).
        /// </summary>
        private void SaveScreenshot(Bitmap bitmap, string prefix)
        {
            string folder = _storageService.GetImageFolder();
            _storageService.EnsureDirectoriesExist();

            string format = _settingsService.CurrentSettings.Screenshot.Format.ToLower();
            string extension = format == "jpg" ? "jpg" : "png";
            
            string fileName = $"{prefix}_{DateTime.Now:dd_MM_yyyy_HH_mm_ss}.{extension}";
            string filePath = Path.Combine(folder, fileName);

            if (format == "jpg")
            {
                // Configurar codificador JPG con máxima calidad
                var encoder = GetEncoder(ImageFormat.Jpeg);
                if (encoder != null)
                {
                    var encoderParameters = new EncoderParameters(1);
                    encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 100L);
                    bitmap.Save(filePath, encoder, encoderParameters);
                }
                else
                {
                    bitmap.Save(filePath, ImageFormat.Jpeg);
                }
            }
            else
            {
                bitmap.Save(filePath, ImageFormat.Png);
            }

            PlayShutterSound();
            ScreenshotSaved?.Invoke(this, filePath);
        }

        private ImageCodecInfo? GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }

        private void PlayShutterSound()
        {
            if (!_settingsService.CurrentSettings.General.PlaySoundOnClip) return;

            try
            {
                // Buscar un sonido de cámara en la carpeta de Windows Media
                string winSound = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Camera Shutter.wav");
                if (File.Exists(winSound))
                {
                    using (var player = new System.Media.SoundPlayer(winSound))
                    {
                        player.Play();
                    }
                }
                else
                {
                    // Alternativa - No hacer nada
                }
            }
            catch { }
        }
    }
}
