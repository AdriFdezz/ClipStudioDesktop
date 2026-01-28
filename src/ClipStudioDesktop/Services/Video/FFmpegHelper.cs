using System;
using System.IO;

namespace ClipStudioDesktop.Helpers
{
    /// <summary>
    /// Helper básico para localizar el ejecutable de FFmpeg.
    /// Esencial para las operaciones de post-procesamiento (mezcla, recorte, conversión)
    /// y grabación directa con FFmpeg si se utiliza <see cref="Services.Video.FFmpegRecorder"/>.
    /// </summary>
    public static class FFmpegHelper
    {
        /// <summary>
        /// Busca la ruta del ejecutable `ffmpeg.exe` en ubicaciones comunes.
        /// </summary>
        /// <returns>Ruta absoluta si se encuentra, o "ffmpeg" para usar el PATH del sistema.</returns>
        public static string GetFFmpegPath()
        {
            // Buscar FFmpeg en varios lugares
            string[] paths = new[]
            {
                "ffmpeg.exe",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffmpeg.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "ffmpeg", "bin", "ffmpeg.exe")
            };

            foreach (var path in paths)
            {
                if (File.Exists(path))
                    return path;
            }

            return "ffmpeg";
        }
    }
}
