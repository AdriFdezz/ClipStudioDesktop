using ClipStudioDesktop.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ClipStudioDesktop.Services.Settings
{
    public class SettingsService : ISettingsService
    {
        private readonly string _configPath;
        public AppSettings CurrentSettings { get; private set; }

        public SettingsService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appData, "ClipStudioDesktop");
            Directory.CreateDirectory(appFolder);
            _configPath = Path.Combine(appFolder, "config.json");
            
            CurrentSettings = new AppSettings();
            LoadSettings();
        }

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
                        
                        // Ensure new hotkeys exist if updating from old version
                        EnsureHotkeys(CurrentSettings);
                        
                        return;
                    }
                }
                catch
                {
                    // Log error or ignore to use defaults
                }
            }

            // If file doesn't exist or failed to load, use defaults and save
            ResetToDefaults();
        }

        private void EnsureHotkeys(AppSettings settings)
        {
            bool changed = false;
            
            // Check for Alt+V (Clipboard Selection)
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
                // Handle save error
            }
        }

        public void ResetToDefaults()
        {
            CurrentSettings = new AppSettings();
            
            // Initialize default hotkeys
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
