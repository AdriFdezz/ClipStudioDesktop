using System;

namespace ClipStudioDesktop.Services
{
    public interface IHotKeyService
    {
        void RegisterHotKey(string keyCombination, Action action);
        void UnregisterHotKey(string keyCombination);
    }
}
