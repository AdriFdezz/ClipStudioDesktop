using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace ClipStudioDesktop.Services.Hotkeys
{
    /// <summary>
    /// Implementación del servicio de atajos globales utilizando la API de Windows (user32.dll).
    /// <para>
    /// Permite registrar combinaciones de teclas que funcionen incluso cuando la aplicación no tiene el foco.
    /// Utiliza un Hook de mensajes de ventana para interceptar <c>WM_HOTKEY</c>.
    /// </para>
    /// </summary>
    public class HotKeyService : IHotKeyService, IDisposable
    {
        // Importación de funciones de la API de Windows (P/Invoke)
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // Constante de mensaje de Windows para notificaciones de HotKey
        private const int WM_HOTKEY = 0x0312;
        
        private IntPtr _windowHandle;
        private HwndSource? _source;
        private int _currentId;
        private readonly Dictionary<int, Action> _callbacks = new();

        /// <summary>
        /// Inicializa el servicio conectándose al bucle de mensajes de la ventana WPF.
        /// </summary>
        /// <param name="windowHandle">Puntero (HWND) a la ventana principal.</param>
        public void Initialize(IntPtr windowHandle)
        {
            _windowHandle = windowHandle;
            _source = HwndSource.FromHwnd(_windowHandle);
            // Añadir el hook para procesar mensajes de bajo nivel
            _source?.AddHook(HwndHook);
        }

        /// <summary>
        /// Registra un atajo global en el sistema operativo.
        /// </summary>
        /// <param name="keyCombination">Texto de la combinación (ej. "Ctrl + F12").</param>
        /// <param name="action">Callback a ejecutar.</param>
        /// <exception cref="InvalidOperationException">Si el servicio no está inicializado o el atajo ya está en uso.</exception>
        public void RegisterHotKey(string keyCombination, Action action)
        {
            if (_windowHandle == IntPtr.Zero)
                throw new InvalidOperationException("HotKeyService not initialized with window handle.");

            // Parsear la combinación de texto a teclas y modificadores
            var (modifiers, key) = ParseKeyCombination(keyCombination);
            
            System.Diagnostics.Debug.WriteLine($"Registrando hotkey: {keyCombination} -> Modifiers: {modifiers}, Key: {key}");
            
            if (key == Key.None)
            {
                System.Diagnostics.Debug.WriteLine($"Invalid hotkey combination: {keyCombination} (No key found)");
                return;
            }

            int id = _currentId++;
            // Convertir la tecla WPF a código de tecla virtual de Windows (Virtual Key)
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            System.Diagnostics.Debug.WriteLine($"VirtualKey: {vk} (0x{vk:X})");
            
            // Llamada a la API de Windows para registrar el atajo
            if (RegisterHotKey(_windowHandle, id, (uint)modifiers, vk))
            {
                _callbacks.Add(id, action);
                System.Diagnostics.Debug.WriteLine($"✓ Hotkey '{keyCombination}' registrado correctamente con ID {id}");
            }
            else
            {
                // Fallo al registrar (probablemente ocupado por otra app)
                int errorCode = Marshal.GetLastWin32Error();
                System.Diagnostics.Debug.WriteLine($"✗ Error registrando '{keyCombination}'. Error: {errorCode}");
                throw new InvalidOperationException($"No se pudo registrar el atajo '{keyCombination}'. Código de error: {errorCode}. Es probable que otra aplicación ya lo esté usando.");
            }
        }

        public void UnregisterHotKey(string keyCombination)
        {
            // Implementación requeriría rastrear IDs por combinación.
            // Por ahora, confiamos en Dispose para limpiar todo al cerrar.
        }

        /// <summary>
        /// Si es true, las pulsaciones de atajos serán ignoradas (no ejecutarán la acción).
        /// </summary>
        public bool IsSuspended { get; set; }

        /// <summary>
        /// Hook del procedimiento de ventana (WndProc) para escuchar mensajes del sistema.
        /// </summary>
        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                if (IsSuspended)
                {
                    // Ignorar si estamos suspendidos (ej. durante input de usuario en otro lado si fuera necesario)
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

        /// <summary>
        /// Convierte una cadena de texto (ej. "Ctrl+Shift+A") en enum ModifierKeys y Key.
        /// </summary>
        private (ModifierKeys, Key) ParseKeyCombination(string combination)
        {
            var parts = combination.Split('+');
            ModifierKeys modifiers = ModifierKeys.None;
            Key key = Key.None;

            System.Diagnostics.Debug.WriteLine($"Parsing: {combination}");

            foreach (var part in parts)
            {
                string trimmed = part.Trim();
                
                // Detectar modificadores
                if (trimmed.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || 
                    trimmed.Equals("Control", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Control;
                }
                else if (trimmed.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Shift;
                }
                else if (trimmed.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Alt;
                }
                else if (trimmed.Equals("Win", StringComparison.OrdinalIgnoreCase) || 
                         trimmed.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Windows;
                }
                // Detectar teclas numéricas (0-9)
                else if (trimmed.Length == 1 && char.IsDigit(trimmed[0]))
                {
                    if (Enum.TryParse("D" + trimmed, true, out Key numKey))
                    {
                        key = numKey;
                    }
                }
                // Detectar letras y otras teclas
                else if (Enum.TryParse(trimmed, true, out Key k))
                {
                    key = k;
                }
            }

            return (modifiers, key);
        }

        /// <summary>
        /// Libera los atajos registrados y remueve el hook de la ventana.
        /// </summary>
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
