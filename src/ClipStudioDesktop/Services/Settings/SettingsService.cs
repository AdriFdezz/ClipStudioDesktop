using ClipStudioDesktop.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ClipStudioDesktop.Services.Settings
{
    /// <summary>
    /// Implementación del servicio de configuración usando archivos JSON.
    /// Almacena la configuración en AppData/ClipStudioDesktop/config.json.
    /// </summary>
    public class SettingsService : ISettingsService
    {
        private readonly string _configPath;
        
        /// <summary>
        /// Propiedad pública para acceder a los ajustes actuales en memoria.
        /// </summary>
        public AppSettings CurrentSettings { get; private set; }

        /// <summary>
        /// Constructor que inicializa la ruta del archivo de configuración y carga los ajustes.
        /// </summary>
        public SettingsService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appData, "ClipStudioDesktop");
            Directory.CreateDirectory(appFolder);
            _configPath = Path.Combine(appFolder, "config.json");
            
            CurrentSettings = new AppSettings();
            LoadSettings();
        }

        /// <summary>
        /// Carga los ajustes desde el archivo JSON.
        /// Si el archivo está corrupto o no existe, restablece a valores predeterminados.
        /// </summary>
        public void LoadSettings()
        {
            if (File.Exists(_configPath))
            {
                try
                {
                    string json = File.ReadAllText(_configPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        CurrentSettings = settings;
                        
                        // Asegurar que existan atajos nuevos si se actualiza desde una versión anterior
                        EnsureHotkeys(CurrentSettings);
                        
                        return;
                    }
                }
                catch
                {
                    // Error de carga o deserialización: se ignorará para usar los valores por defecto
                }
            }

             // Si el archivo no existe o falló la carga, usar valores por defecto y guardar
            ResetToDefaults();
        }

        /// <summary>
        /// Verifica y añade atajos de teclado críticos que puedan faltar en configuraciones antiguas.
        /// </summary>
        private void EnsureHotkeys(AppSettings settings)
        {
            bool changed = false;
            
            // Verificar Alt+V (Selección al Portapapeles)
            if (!settings.Hotkeys.Exists(h => h.Type == "screenshot" && h.Mode == "selection_clipboard"))
            {
                settings.Hotkeys.Add(new HotKeyConfig { Key = "Alt+C", Type = "screenshot", Mode = "selection_clipboard" });
                changed = true;
            }

            if (changed)
            {
                SaveSettings();
            }
        }

        /// <summary>
        /// Serializa y guarda la configuración actual en el archivo JSON.
        /// </summary>
        public void SaveSettings()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(CurrentSettings, options);
                File.WriteAllText(_configPath, json);
            }
            catch
            {
                // Manejar error de guardado silenciosamente o loggear
            }
        }

        /// <summary>
        /// Restablece la configuración a los valores de fábrica y guarda el archivo.
        /// Define los atajos de teclado predeterminados.
        /// </summary>
        public void ResetToDefaults()
        {
            CurrentSettings = new AppSettings();
            
            // Inicializar atajos por defecto
            CurrentSettings.Hotkeys = new List<HotKeyConfig>
            {
                new() { Key = "Ctrl+Alt+A", Type = "audio", Duration = 0 },
                new() { Key = "Ctrl+Alt+V", Type = "video", Duration = 0 },
                
                new() { Key = "Alt+X", Type = "screenshot", Mode = "selection" },
                new() { Key = "Alt+V", Type = "screenshot", Mode = "fullscreen" },
                new() { Key = "Alt+C", Type = "screenshot", Mode = "selection_clipboard" }
            };

            SaveSettings();
        }
    }
}
