using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace ClipStudioDesktop.Services.Hotkeys
{
    public class HotKeyService : IHotKeyService, IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;
        private IntPtr _windowHandle;
        private HwndSource? _source;
        private int _currentId;
        private readonly Dictionary<int, Action> _callbacks = new();

        public void Initialize(IntPtr windowHandle)
        {
            _windowHandle = windowHandle;
            _source = HwndSource.FromHwnd(_windowHandle);
            _source?.AddHook(HwndHook);
        }

        public void RegisterHotKey(string keyCombination, Action action)
        {
            if (_windowHandle == IntPtr.Zero)
                throw new InvalidOperationException("HotKeyService not initialized with window handle.");

            var (modifiers, key) = ParseKeyCombination(keyCombination);
            
            if (key == Key.None)
            {
                System.Diagnostics.Debug.WriteLine($"Invalid hotkey combination: {keyCombination} (No key found)");
                return;
            }

            int id = _currentId++;
            
            if (RegisterHotKey(_windowHandle, id, (uint)modifiers, (uint)KeyInterop.VirtualKeyFromKey(key)))
            {
                _callbacks.Add(id, action);
            }
            else
            {
                // Failed to register
                int errorCode = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"No se pudo registrar el atajo '{keyCombination}'. Código de error: {errorCode}. Es probable que otra aplicación ya lo esté usando.");
            }
        }

        public void UnregisterHotKey(string keyCombination)
        {
            // Implementation would require tracking IDs by combination
            // For now, we'll rely on Dispose to clear all
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (_callbacks.TryGetValue(id, out var action))
                {
                    action?.Invoke();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private (ModifierKeys, Key) ParseKeyCombination(string combination)
        {
            var parts = combination.Split('+');
            ModifierKeys modifiers = ModifierKeys.None;
            Key key = Key.None;

            foreach (var part in parts)
            {
                string trimmed = part.Trim();
                
                // Handle aliases
                if (trimmed.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) trimmed = "Control";
                if (trimmed.Equals("Shift", StringComparison.OrdinalIgnoreCase)) trimmed = "Shift";
                if (trimmed.Equals("Alt", StringComparison.OrdinalIgnoreCase)) trimmed = "Alt";
                
                if (Enum.TryParse(trimmed, true, out ModifierKeys mod))
                {
                    modifiers |= mod;
                }
                else if (Enum.TryParse(trimmed, true, out Key k))
                {
                    key = k;
                }
                // Handle number keys specifically if needed (e.g. "1" -> D1)
                else if (trimmed.Length == 1 && char.IsDigit(trimmed[0]))
                {
                     if (Enum.TryParse("D" + trimmed, true, out Key numKey))
                     {
                         key = numKey;
                     }
                }
            }

            // If no modifiers found but "Ctrl" or "Alt" or "Shift" was in the string, it might have been parsed as Key.LeftCtrl etc.
            // But we want ModifierKeys for RegisterHotKey.
            // Actually, Enum.TryParse<ModifierKeys> handles "Control", "Alt", "Shift".
            
            return (modifiers, key);
        }

        public void Dispose()
        {
            _source?.RemoveHook(HwndHook);
            foreach (var id in _callbacks.Keys)
            {
                UnregisterHotKey(_windowHandle, id);
            }
            _callbacks.Clear();
        }
    }
}
