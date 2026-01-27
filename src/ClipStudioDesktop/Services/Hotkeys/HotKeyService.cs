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
            
            System.Diagnostics.Debug.WriteLine($"Registrando hotkey: {keyCombination} -> Modifiers: {modifiers}, Key: {key}");
            
            if (key == Key.None)
            {
                System.Diagnostics.Debug.WriteLine($"Invalid hotkey combination: {keyCombination} (No key found)");
                return;
            }

            int id = _currentId++;
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            System.Diagnostics.Debug.WriteLine($"VirtualKey: {vk} (0x{vk:X})");
            
            if (RegisterHotKey(_windowHandle, id, (uint)modifiers, vk))
            {
                _callbacks.Add(id, action);
                System.Diagnostics.Debug.WriteLine($"✓ Hotkey '{keyCombination}' registrado correctamente con ID {id}");
            }
            else
            {
                // Failed to register
                int errorCode = Marshal.GetLastWin32Error();
                System.Diagnostics.Debug.WriteLine($"✗ Error registrando '{keyCombination}'. Error: {errorCode}");
                throw new InvalidOperationException($"No se pudo registrar el atajo '{keyCombination}'. Código de error: {errorCode}. Es probable que otra aplicación ya lo esté usando.");
            }
        }

        public void UnregisterHotKey(string keyCombination)
        {
            // Implementation would require tracking IDs by combination
            // For now, we'll rely on Dispose to clear all
        }

        public bool IsSuspended { get; set; }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                if (IsSuspended)
                {
                    // Ignore hotkeys when suspended
                    return IntPtr.Zero;
                }

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

            System.Diagnostics.Debug.WriteLine($"Parsing: {combination}");

            foreach (var part in parts)
            {
                string trimmed = part.Trim();
                System.Diagnostics.Debug.WriteLine($"  Part: '{trimmed}'");
                
                // Handle modifier keys explicitly
                if (trimmed.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || 
                    trimmed.Equals("Control", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Control;
                    System.Diagnostics.Debug.WriteLine($"    -> Added Control modifier");
                }
                else if (trimmed.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Shift;
                    System.Diagnostics.Debug.WriteLine($"    -> Added Shift modifier");
                }
                else if (trimmed.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Alt;
                    System.Diagnostics.Debug.WriteLine($"    -> Added Alt modifier");
                }
                else if (trimmed.Equals("Win", StringComparison.OrdinalIgnoreCase) || 
                         trimmed.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Windows;
                    System.Diagnostics.Debug.WriteLine($"    -> Added Windows modifier");
                }
                // Handle number keys (1-9, 0)
                else if (trimmed.Length == 1 && char.IsDigit(trimmed[0]))
                {
                    if (Enum.TryParse("D" + trimmed, true, out Key numKey))
                    {
                        key = numKey;
                        System.Diagnostics.Debug.WriteLine($"    -> Key: {key}");
                    }
                }
                // Handle letter keys and other keys
                else if (Enum.TryParse(trimmed, true, out Key k))
                {
                    key = k;
                    System.Diagnostics.Debug.WriteLine($"    -> Key: {key}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"    -> UNKNOWN: '{trimmed}'");
                }
            }

            System.Diagnostics.Debug.WriteLine($"Result: Modifiers={modifiers}, Key={key}");
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
