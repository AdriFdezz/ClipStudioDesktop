using ClipStudioDesktop.Models;

namespace ClipStudioDesktop.Services
{
    public interface ISettingsService
    {
        AppSettings CurrentSettings { get; }
        void LoadSettings();
        void SaveSettings();
        void ResetToDefaults();
    }
}
