using System.Threading.Tasks;

namespace ClipStudioDesktop.Services
{
    public interface IRecordingService
    {
        Task StartRecordingAsync();
        Task StopRecordingAsync();
        Task SaveClipAsync(int durationSeconds, bool isVideo);
    }
}
