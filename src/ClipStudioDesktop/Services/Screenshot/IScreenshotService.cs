using System.Threading.Tasks;

namespace ClipStudioDesktop.Services.Screenshot
{
    public interface IScreenshotService
    {
        Task CaptureFullScreenAsync();
        Task CaptureSelectionAsync();
    }
}
