using System;

namespace ClipStudioDesktop.Services.Hotkeys
{
    /// <summary>
    /// Interfaz para el servicio de gestión de atajos de teclado globales (HotKeys).
    /// </summary>
    public interface IHotKeyService
    {
        /// <summary>
        /// Inicializa el servicio registrando el hook de ventana necesario para interceptar mensajes de teclas.
        /// </summary>
        /// <param name="windowHandle">Handle (IntPtr) de la ventana principal de la aplicación.</param>
        void Initialize(IntPtr windowHandle);

        /// <summary>
        /// Registra un nuevo atajo global.
        /// </summary>
        /// <param name="keyCombination">Cadena que representa la combinación (ej. "Ctrl+Alt+R").</param>
        /// <param name="action">Acción a ejecutar cuando se presione el atajo.</param>
        void RegisterHotKey(string keyCombination, Action action);

        /// <summary>
        /// Desregistra un atajo de teclado previamente registrado.
        /// </summary>
        /// <param name="keyCombination">La combinación de teclas a eliminar.</param>
        void UnregisterHotKey(string keyCombination);

        /// <summary>
        /// Indica si el procesamiento de atajos está suspendido temporalmente.
        /// </summary>
        bool IsSuspended { get; set; }
    }
}
