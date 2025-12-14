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
                int x = 0, y = 0, width = 0, height = 0;

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
                        if (screen != null)
                        {
                            x = screen.Bounds.X;
                            y = screen.Bounds.Y;
                            width = screen.Bounds.Width;
                            height = screen.Bounds.Height;
                        }
                    }
                }
                else // "primary" or default
                {
                    var screen = System.Windows.Forms.Screen.PrimaryScreen;
                    if (screen != null)
                    {
                        x = screen.Bounds.X;
                        y = screen.Bounds.Y;
                        width = screen.Bounds.Width;
                        height = screen.Bounds.Height;
                    }
                }

                if (width == 0 || height == 0) return Task.CompletedTask;

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

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        public Task<bool> CaptureSelectionAsync() => PerformSelectionCaptureAsync(true);

        public Task<bool> CaptureSelectionToClipboardAsync() => PerformSelectionCaptureAsync(false);

        private async Task<bool> PerformSelectionCaptureAsync(bool saveToFile)
        {
            bool success = false;
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

                // 2. Show Selection Window
                // Must run on UI thread
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
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
                            if (saveToFile)
                            {
                                SaveScreenshot(cropped);
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
            return success;
        }

        private void CopyToClipboard(Bitmap bitmap)
        {
            try
            {
                IntPtr hBitmap = bitmap.GetHbitmap();
                try
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    
                    System.Windows.Clipboard.SetImage(source);
                    PlayShutterSound();
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

        private void SaveScreenshot(Bitmap bitmap)
        {
            string folder = _storageService.GetImageFolder();
            _storageService.EnsureDirectoriesExist();

            string fileName = $"screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
            string filePath = Path.Combine(folder, fileName);

            bitmap.Save(filePath, ImageFormat.Png);

            PlayShutterSound();
        }

        private void PlayShutterSound()
        {
            try
            {
                // Try to find a shutter sound in Windows Media folder
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
                    // Fallback - Do nothing to avoid annoying system beeps
                    // System.Media.SystemSounds.Asterisk.Play();
                }
            }
            catch { }
        }
    }
}
