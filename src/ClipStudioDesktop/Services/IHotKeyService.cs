using System;

namespace ClipStudioDesktop.Services
{
    public interface IHotKeyService
    {
        void Initialize(IntPtr windowHandle);
        void RegisterHotKey(string keyCombination, Action action);
        void UnregisterHotKey(string keyCombination);
    }
}
