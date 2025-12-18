using System;
using System.IO;

namespace ClipStudioDesktop.Helpers
{
    /// <summary>
    /// Helper básico para localizar FFmpeg
    /// Solo se usa para post-procesamiento (merge, trim, convert)
    /// </summary>
    public static class FFmpegHelper
    {
        public static string GetFFmpegPath()
        {
            // Buscar FFmpeg en varios lugares
            string[] paths = new[]
            {
                "ffmpeg.exe", // En el directorio actual
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffmpeg.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "ffmpeg", "bin", "ffmpeg.exe")
            };

            foreach (var path in paths)
            {
                if (File.Exists(path))
                    return path;
            }

            // Por defecto, asumir que está en PATH
            return "ffmpeg";
        }
    }
}
