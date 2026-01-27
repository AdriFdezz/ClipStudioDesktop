using System;
using System.Threading.Tasks;

namespace ClipStudioDesktop.Services.Recording
{
    public interface IRecordingService : IDisposable
    {
        bool IsRecording { get; }
        event EventHandler<bool> RecordingStateChanged;
        event EventHandler<string> ClipSaved;
        Task StartRecordingAsync();
        Task StopRecordingAsync();
        Task SaveClipAsync(int durationSeconds, bool isVideo);
        void ClearBuffer();
        void UpdateBufferReservation();
    }
}
