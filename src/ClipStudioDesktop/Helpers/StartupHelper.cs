using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Reflection;

namespace ClipStudioDesktop.Helpers
{
    /// <summary>
    /// Ayudante para gestionar el arranque automático de la aplicación con Windows.
    /// <para>Modifica el Registro de Windows (HKCU\Software\Microsoft\Windows\CurrentVersion\Run).</para>
    /// </summary>
    public static class StartupHelper
    {
        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "ClipStudioDesktop";

        /// <summary>
        /// Habilita o deshabilita el inicio automático de la aplicación al arrancar Windows.
        /// </summary>
        /// <param name="enable">
        /// <c>true</c> para agregar la aplicación al inicio de Windows; 
        /// <c>false</c> para eliminarla.
        /// </param>
        public static void SetStartup(bool enable)
        {
            try
            {
                // Abrir la clave del registro para el usuario actual con permisos de escritura (true)
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true))
                {
                    if (key == null) return;

                    if (enable)
                    {
                        // Obtener la ruta del ejecutable actual
                        string? location = Process.GetCurrentProcess().MainModule?.FileName;
                        if (location != null)
                        {
                            // Si estamos corriendo como DLL (dotnet run), intentamos apuntar al EXE
                            // Aunque en publicación 'single-file', MainModule.FileName suele ser correcto.
                            if (location.EndsWith(".dll"))
                            {
                                location = location.Replace(".dll", ".exe");
                            }
                            
                            // Guardar la ruta en el registro entre comillas para manejar espacios
                            key.SetValue(AppName, $"\"{location}\"");
                        }
                    }
                    else
                    {
                        // Eliminar la entrada del registro si existe. false para no lanzar excepción si falta.
                        key.DeleteValue(AppName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                // Si falla (permisos, antivirus, etc), solo logueamos el error para no crashear la app
                Debug.WriteLine($"Error setting startup: {ex.Message}");
            }
        }
    }
}
