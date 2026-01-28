using System;
using System.Threading.Tasks;

namespace ClipStudioDesktop.Services.Recording
{
    /// <summary>
    /// Interfaz para el servicio principal de grabación (Orquestador).
    /// Define las operaciones de alto nivel para controlar el ciclo de vida de la grabación (video y audio)
    /// y gestionar los eventos de estado.
    /// </summary>
    public interface IRecordingService : IDisposable
    {
        /// <summary>
        /// Indica si actualmente hay una grabación activa en curso.
        /// </summary>
        bool IsRecording { get; }

        /// <summary>
        /// Indica si el modo de grabación actual incluye video (<c>true</c>) o es solo audio (<c>false</c>).
        /// </summary>
        bool IsVideoMode { get; }

        /// <summary>
        /// Evento que se dispara cuando cambia el estado de grabación (Iniciado/Detenido).
        /// </summary>
        event EventHandler<bool> RecordingStateChanged;

        /// <summary>
        /// Evento que se dispara cuando un clip (archivo final) ha sido guardado exitosamente.
        /// El argumento es la ruta completa del archivo generado.
        /// </summary>
        event EventHandler<string> ClipSaved;

        /// <summary>
        /// Evento que notifica cambios en el tamaño estimado y físico de los archivos temporales de grabación.
        /// Útil para mostrar estadísticas de uso de disco en tiempo real.
        /// </summary>
        event EventHandler<(long Estimated, long Physical)> BufferSizeChanged;

        /// <summary>
        /// Inicia una nueva sesión de grabación.
        /// </summary>
        /// <param name="videoEnabled">Si es <c>true</c>, graba video y audio del sistema. Si es <c>false</c>, solo graba audio.</param>
        Task StartRecordingAsync(bool videoEnabled = true);

        /// <summary>
        /// Fecha y hora en que comenzó la grabación actual (o null si no se está grabando).
        /// </summary>
        DateTime? CurrentRecordingStartTime { get; }

        /// <summary>
        /// Detiene la grabación actual y finaliza los archivos (conversión y guardado).
        /// </summary>
        Task StopRecordingAsync();

        /// <summary>
        /// Alterna el estado de grabación (Inicia si está detenido, Detiene si está grabando).
        /// Si se está grabando en un modo diferente al solicitado, reinicia la grabación en el nuevo modo.
        /// </summary>
        Task ToggleRecordingAsync(bool videoEnabled = true);

        /// <summary>
        /// Método de compatibilidad para guardar un clip (Instant Replay).
        /// Actualmente puede estar deshabilitado o redirigido en implementaciones simplificadas.
        /// </summary>
        Task SaveClipAsync(int durationSeconds, bool isVideo);

        /// <summary>
        /// Limpia el búfer de grabación (si aplica).
        /// </summary>
        void ClearBuffer();

        /// <summary>
        /// Actualiza la reserva de espacio en disco para el búfer.
        /// </summary>
        void UpdateBufferReservation();
    }
}
