using FFMpegCore;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ClipStudioDesktop.Services.Video
{
    public static class FFmpegHelper
    {
        public static Task EnsureFFmpegInstalledAsync()
        {
            // Configure FFMpegCore to look in the current directory
            GlobalFFOptions.Configure(new FFOptions { BinaryFolder = AppDomain.CurrentDomain.BaseDirectory });
            return Task.CompletedTask;
        }

        public static string GetFFmpegPath()
        {
            string localPath = Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg.exe");
            if (File.Exists(localPath)) return localPath;
            return "ffmpeg"; // Assume in PATH
        }
    }
}
