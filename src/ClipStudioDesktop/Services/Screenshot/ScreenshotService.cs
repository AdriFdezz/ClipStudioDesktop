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
    public class ScreenshotService : IScreenshotService
    {
        private readonly IStorageService _storageService;
        private readonly ISettingsService _settingsService;

        public ScreenshotService(IStorageService storageService, ISettingsService settingsService)
        {
            _storageService = storageService;
            _settingsService = settingsService;
        }

        public Task CaptureFullScreenAsync()
        {
            try
            {
                var settings = _settingsService.CurrentSettings.Screenshot;
                int x, y, width, height;

                if (settings.Monitor == "all")
                {
                    x = (int)SystemParameters.VirtualScreenLeft;
                    y = (int)SystemParameters.VirtualScreenTop;
                    width = (int)SystemParameters.VirtualScreenWidth;
                    height = (int)SystemParameters.VirtualScreenHeight;
                }
                else if (settings.Monitor == "specific")
                {
                    var screens = System.Windows.Forms.Screen.AllScreens;
                    if (settings.MonitorIndex >= 0 && settings.MonitorIndex < screens.Length)
                    {
                        var screen = screens[settings.MonitorIndex];
                        x = screen.Bounds.X;
                        y = screen.Bounds.Y;
                        width = screen.Bounds.Width;
                        height = screen.Bounds.Height;
                    }
                    else
                    {
                        // Fallback to primary
                        var screen = System.Windows.Forms.Screen.PrimaryScreen;
                        x = screen.Bounds.X;
                        y = screen.Bounds.Y;
                        width = screen.Bounds.Width;
                        height = screen.Bounds.Height;
                    }
                }
                else // "primary" or default
                {
                    var screen = System.Windows.Forms.Screen.PrimaryScreen;
                    x = screen.Bounds.X;
                    y = screen.Bounds.Y;
                    width = screen.Bounds.Width;
                    height = screen.Bounds.Height;
                }

                using (Bitmap bitmap = new Bitmap(width, height))
                {
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        g.CopyFromScreen(x, y, 0, 0, bitmap.Size);
                    }

                    SaveScreenshot(bitmap);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error capturing full screen: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        public async Task CaptureSelectionAsync()
        {
            try
            {
                // 1. Capture full screen first to use as background
                int screenLeft = (int)SystemParameters.VirtualScreenLeft;
                int screenTop = (int)SystemParameters.VirtualScreenTop;
                int screenWidth = (int)SystemParameters.VirtualScreenWidth;
                int screenHeight = (int)SystemParameters.VirtualScreenHeight;

                Bitmap fullScreenBitmap = new Bitmap(screenWidth, screenHeight);
                using (Graphics g = Graphics.FromImage(fullScreenBitmap))
                {
                    g.CopyFromScreen(screenLeft, screenTop, 0, 0, fullScreenBitmap.Size);
                }

                // Convert to BitmapSource for WPF
                var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                    fullScreenBitmap.GetHbitmap(),
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                // 2. Show Selection Window
                // Must run on UI thread
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var selectionWindow = new SelectionWindow(bitmapSource);
                    
                    // Position window to cover all screens
                    selectionWindow.Left = screenLeft;
                    selectionWindow.Top = screenTop;
                    selectionWindow.Width = screenWidth;
                    selectionWindow.Height = screenHeight;

                    selectionWindow.ShowDialog();

                    if (selectionWindow.IsConfirmed)
                    {
                        var rect = selectionWindow.SelectedRegion;
                        
                        // Crop the original bitmap
                        var cropRect = new Rectangle((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
                        using (Bitmap cropped = fullScreenBitmap.Clone(cropRect, fullScreenBitmap.PixelFormat))
                        {
                            SaveScreenshot(cropped);
                        }
                    }
                });

                fullScreenBitmap.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error capturing selection: {ex.Message}");
            }
        }

        private void SaveScreenshot(Bitmap bitmap)
        {
            string folder = _storageService.GetImageFolder();
            _storageService.EnsureDirectoriesExist();

            string fileName = $"screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
            string filePath = Path.Combine(folder, fileName);

            bitmap.Save(filePath, ImageFormat.Png);

            if (_settingsService.CurrentSettings.General.ShowNotifications)
            {
                // TODO: Better notification
                Application.Current.Dispatcher.Invoke(() => 
                    MessageBox.Show($"Captura guardada: {filePath}")
                );
            }
        }
    }
}
