using FFMpegCore;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ClipStudioDesktop.Services.Video
{
    public static class FFmpegHelper
    {
        public static async Task EnsureFFmpegInstalledAsync()
        {
            // Check if ffmpeg is already available in path or local folder
            try 
            {
                // This will throw if ffmpeg is not found
                GlobalFFOptions.Configure(new FFOptions { BinaryFolder = "./" });
                
                // If we are here, we might want to download it if not present
                // But FFMpegCore.GlobalFFOptions doesn't have a simple "Check" method without running something.
                // We'll assume if the user doesn't have it, we download it.
                
                string ffmpegPath = Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg.exe");
                if (!File.Exists(ffmpegPath))
                {
                    await FFMpegDownloader.DownloadFFMpeg("ffmpeg");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking/downloading FFmpeg: {ex.Message}");
            }
        }

        public static string GetFFmpegPath()
        {
            string localPath = Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg.exe");
            if (File.Exists(localPath)) return localPath;
            return "ffmpeg"; // Assume in PATH
        }
    }
}
