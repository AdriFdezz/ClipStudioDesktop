using System;
using System.Threading.Tasks;

namespace ClipStudioDesktop.Services.Recording
{
    public interface IRecordingService : IDisposable
    {
        bool IsRecording { get; }
        bool IsVideoMode { get; }
        event EventHandler<bool> RecordingStateChanged;
        event EventHandler<string> ClipSaved;
        event EventHandler<(long Estimated, long Physical)> BufferSizeChanged;
        Task StartRecordingAsync(bool videoEnabled = true);
        DateTime? CurrentRecordingStartTime { get; }
        Task StopRecordingAsync();
        Task ToggleRecordingAsync(bool videoEnabled = true);
        Task SaveClipAsync(int durationSeconds, bool isVideo);
        void ClearBuffer();
        void UpdateBufferReservation();
    }
}
