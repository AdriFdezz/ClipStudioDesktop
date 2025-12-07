using System.Threading.Tasks;

namespace ClipStudioDesktop.Services.Recording
{
    public interface IRecordingService
    {
        bool IsRecording { get; }
        Task StartRecordingAsync();
        Task StopRecordingAsync();
        Task SaveClipAsync(int durationSeconds, bool isVideo);
    }
}
