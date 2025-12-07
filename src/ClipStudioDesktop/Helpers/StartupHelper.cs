using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Reflection;

namespace ClipStudioDesktop.Helpers
{
    public static class StartupHelper
    {
        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "ClipStudioDesktop";

        public static void SetStartup(bool enable)
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true))
                {
                    if (key == null) return;

                    if (enable)
                    {
                        string? location = Process.GetCurrentProcess().MainModule?.FileName;
                        if (location != null)
                        {
                            // If it's a dll (dotnet run), we might need the exe. 
                            // But for published single file, MainModule.FileName is correct.
                            if (location.EndsWith(".dll"))
                            {
                                location = location.Replace(".dll", ".exe");
                            }
                            
                            key.SetValue(AppName, $"\"{location}\"");
                        }
                    }
                    else
                    {
                        key.DeleteValue(AppName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting startup: {ex.Message}");
            }
        }
    }
}
