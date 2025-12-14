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
            // 1. Check in application directory
            string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            if (File.Exists(localPath)) return localPath;

            // 2. Check in a 'tools' subdirectory
            string toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "ffmpeg.exe");
            if (File.Exists(toolsPath)) return toolsPath;

            // 3. Assume in PATH
            return "ffmpeg"; 
        }
    }
}
