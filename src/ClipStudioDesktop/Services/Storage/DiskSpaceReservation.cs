using System;
using System.IO;

namespace ClipStudioDesktop.Services.Storage
{
    /// <summary>
    /// Gestiona la reserva de espacio en disco para el buffer
    /// </summary>
    public static class DiskSpaceReservation
    {
        private const string PLACEHOLDER_FILE = ".space_reservation";

        /// <summary>
        /// Reserva espacio en disco creando un archivo placeholder
        /// </summary>
        public static bool ReserveSpace(string bufferPath, long bytesToReserve)
        {
            try
            {
                Directory.CreateDirectory(bufferPath);
                string placeholderPath = Path.Combine(bufferPath, PLACEHOLDER_FILE);

                // Verificar espacio disponible en el disco
                var driveInfo = new DriveInfo(Path.GetPathRoot(bufferPath) ?? "C:\\");
                if (driveInfo.AvailableFreeSpace < bytesToReserve)
                {
                    System.Diagnostics.Debug.WriteLine($"DiskSpaceReservation: Espacio insuficiente. Disponible: {driveInfo.AvailableFreeSpace / 1024 / 1024 / 1024}GB, Necesario: {bytesToReserve / 1024 / 1024 / 1024}GB");
                    return false;
                }

                // Crear archivo placeholder del tamaño especificado
                using (var fs = new FileStream(placeholderPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.SetLength(bytesToReserve);
                }

                System.Diagnostics.Debug.WriteLine($"DiskSpaceReservation: Reservados {bytesToReserve / 1024 / 1024 / 1024}GB en {bufferPath}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DiskSpaceReservation: Error al reservar espacio: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Libera el espacio reservado eliminando el archivo placeholder
        /// </summary>
        public static void ReleaseSpace(string bufferPath)
        {
            try
            {
                string placeholderPath = Path.Combine(bufferPath, PLACEHOLDER_FILE);
                if (File.Exists(placeholderPath))
                {
                    File.Delete(placeholderPath);
                    System.Diagnostics.Debug.WriteLine($"DiskSpaceReservation: Espacio liberado en {bufferPath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DiskSpaceReservation: Error al liberar espacio: {ex.Message}");
            }
        }

        /// <summary>
        /// Ajusta el tamaño de la reserva
        /// </summary>
        public static bool AdjustReservation(string bufferPath, long newBytesToReserve)
        {
            ReleaseSpace(bufferPath);
            return ReserveSpace(bufferPath, newBytesToReserve);
        }

        /// <summary>
        /// Obtiene el tamaño actual de la reserva
        /// </summary>
        public static long GetCurrentReservationSize(string bufferPath)
        {
            try
            {
                string placeholderPath = Path.Combine(bufferPath, PLACEHOLDER_FILE);
                if (File.Exists(placeholderPath))
                {
                    return new FileInfo(placeholderPath).Length;
                }
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Calcula el espacio usado por el buffer (archivos reales sin incluir la reserva)
        /// </summary>
        public static long CalculateBufferSize(string bufferPath)
        {
            long totalSize = 0;
            try
            {
                if (Directory.Exists(bufferPath))
                {
                    foreach (var file in Directory.GetFiles(bufferPath))
                    {
                        string fileName = Path.GetFileName(file);
                        if (fileName != PLACEHOLDER_FILE)
                        {
                            totalSize += new FileInfo(file).Length;
                        }
                    }
                    
                    // Incluir subdirectorios (audio, video, etc)
                    foreach (var dir in Directory.GetDirectories(bufferPath))
                    {
                        foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                        {
                            totalSize += new FileInfo(file).Length;
                        }
                    }
                }
            }
            catch { }
            return totalSize;
        }

        /// <summary>
        /// Ajusta dinámicamente la reserva basándose en el espacio usado
        /// </summary>
        public static void UpdateReservation(string bufferPath, long totalReservationBytes)
        {
            try
            {
                long bufferUsed = CalculateBufferSize(bufferPath);
                long reservationNeeded = Math.Max(0, totalReservationBytes - bufferUsed);
                
                string placeholderPath = Path.Combine(bufferPath, PLACEHOLDER_FILE);
                if (File.Exists(placeholderPath))
                {
                    using (var fs = new FileStream(placeholderPath, FileMode.Open, FileAccess.Write, FileShare.None))
                    {
                        fs.SetLength(reservationNeeded);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DiskSpaceReservation: Error al actualizar reserva: {ex.Message}");
            }
        }
    }
}
