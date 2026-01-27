using System.Threading.Tasks;

namespace ClipStudioDesktop.Services.Screenshot
{
    public interface IScreenshotService
    {
        Task CaptureFullScreenAsync();
        Task<bool> CaptureSelectionAsync();
        Task<bool> CaptureSelectionToClipboardAsync();
        event System.EventHandler<string>? ScreenshotSaved;
    }
}
