using ClipStudioDesktop.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ClipStudioDesktop.Services
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
                new() { Key = "Ctrl+1", Type = "audio", Duration = 30 },
                new() { Key = "Ctrl+2", Type = "audio", Duration = 60 },
                new() { Key = "Ctrl+3", Type = "audio", Duration = 90 },
                new() { Key = "Ctrl+4", Type = "audio", Duration = 120 },
                new() { Key = "Ctrl+5", Type = "audio", Duration = 300 },
                
                new() { Key = "Alt+1", Type = "video", Duration = 30 },
                new() { Key = "Alt+2", Type = "video", Duration = 60 },
                new() { Key = "Alt+3", Type = "video", Duration = 90 },
                new() { Key = "Alt+4", Type = "video", Duration = 120 },
                new() { Key = "Alt+5", Type = "video", Duration = 300 },
                
                new() { Key = "Alt+X", Type = "screenshot", Mode = "selection" },
                new() { Key = "Alt+C", Type = "screenshot", Mode = "fullscreen" }
            };

            SaveSettings();
        }
    }
}
