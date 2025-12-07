using ClipStudioDesktop.Models;

namespace ClipStudioDesktop.Services.Settings
{
    public interface ISettingsService
    {
        AppSettings CurrentSettings { get; }
        void LoadSettings();
        void SaveSettings();
        void ResetToDefaults();
    }
}
