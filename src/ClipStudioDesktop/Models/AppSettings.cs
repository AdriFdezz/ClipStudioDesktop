using System.Collections.Generic;
using System.IO;
using System;

namespace ClipStudioDesktop.Models
{
    /// <summary>
    /// Modelo raíz que contiene toda la configuración de la aplicación.
    /// <para>Se serializa/deserializa a JSON para persistencia.</para>
    /// </summary>
    public class AppSettings
    {
        public GeneralSettings General { get; set; } = new();
        public PathSettings Paths { get; set; } = new();
        public AudioSettings Audio { get; set; } = new();
        public VideoSettings Video { get; set; } = new();
        public ScreenshotSettings Screenshot { get; set; } = new();
        public List<HotKeyConfig> Hotkeys { get; set; } = new();
        public BufferSettings Buffer { get; set; } = new();
    }

    /// <summary>
    /// Configuraciones generales de comportamiento de la aplicación.
    /// </summary>
    public class GeneralSettings
    {
        /// <summary>Iniciar la aplicación minimizada junto con Windows.</summary>
        public bool StartWithWindows { get; set; } = true;
        
        /// <summary>Mostrar notificaciones de sistema (toast) al completar acciones.</summary>
        public bool ShowNotifications { get; set; } = true;
        
        /// <summary>Reproducir sonido de notificación al guardar una captura o grabación.</summary>
        public bool PlaySoundOnClip { get; set; } = true;
    }

    /// <summary>
    /// Definición de rutas de almacenamiento para los archivos generados.
    /// </summary>
    public class PathSettings
    {
        /// <summary>Carpeta temporal para caché y archivos intermedios.</summary>
        public string Cache { get; set; } = Path.Combine(Path.GetTempPath(), "ClipStudioDesktop", "cache");
        
        /// <summary>Ruta de salida para grabaciones de audio.</summary>
        public string AudioClips { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ClipStudioDesktop Multimedia", "Audio");
        
        /// <summary>Ruta de salida para grabaciones de video.</summary>
        public string VideoClips { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ClipStudioDesktop Multimedia", "Video");
        
        /// <summary>Ruta de salida para capturas de pantalla.</summary>
        public string Screenshots { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ClipStudioDesktop Multimedia", "Imagenes");
    }

    /// <summary>
    /// Configuración técnica de grabación de audio.
    /// </summary>
    public class AudioSettings
    {
        /// <summary>Formato de salida (mp3, wav, flac, etc).</summary>
        public string Format { get; set; } = "mp3";
        
        /// <summary>Bitrate en kbps (ej. 192, 320).</summary>
        public int Bitrate { get; set; } = 192;
        
        /// <summary>Frecuencia de muestreo en Hz (ej. 44100, 48000).</summary>
        public int SampleRate { get; set; } = 48000;
        
        /// <summary>Número de canales (1 mono, 2 estéreo).</summary>
        public int Channels { get; set; } = 2;
        
        /// <summary>Fuente de audio (micro, system, ambas).</summary>
        public string Source { get; set; } = "system";
        
        /// <summary>ID del dispositivo de salida de audio del sistema seleccionado.</summary>
        public string SelectedAudioDevice { get; set; } = ""; // Empty = auto-detect
        
        /// <summary>Habilitar grabación simultánea del micrófono.</summary>
        public bool EnableMicrophone { get; set; } = false; 
        
        /// <summary>ID del micrófono seleccionado.</summary>
        public string SelectedMicrophone { get; set; } = ""; // Empty = default microphone
        
        /// <summary>Ganancia adicional en dB para el micrófono (0 = desactivado).</summary>
        public double MicrophoneGainDB { get; set; } = 0; 
        
        /// <summary>Umbral de puerta de ruido en dB para el micrófono (0 = desactivado).</summary>
        public double NoiseGateDB { get; set; } = 0; 
    }

    /// <summary>
    /// Configuración técnica de grabación de video.
    /// </summary>
    public class VideoSettings
    {
        /// <summary>Formato contenedor de video (mp4, webm, avi).</summary>
        public string Format { get; set; } = "mp4";
        
        /// <summary>Códec de video (h264, vp9, etc).</summary>
        public string Codec { get; set; } = "h264";
        
        /// <summary>Resolución de salida (ej. "1920x1080" o "Native").</summary>
        public string Resolution { get; set; } = "1920x1080";
        
        /// <summary>Cuadros por segundo objetivo.</summary>
        public int Framerate { get; set; } = 60;
        
        /// <summary>Bitrate de video objetivo en kbps.</summary>
        public int Bitrate { get; set; } = 8000;
        
        /// <summary>Perfil de compresión / calidad (balanced, quality, speed).</summary>
        public string Compression { get; set; } = "balanced";
        
        /// <summary>Si es true, captura usando Desktop Duplication API solo en el monitor principal.</summary>
        public bool CapturePrimaryMonitorOnly { get; set; } = true; 
    }

    /// <summary>
    /// Configuración para capturas de pantalla estáticas.
    /// </summary>
    public class ScreenshotSettings
    {
        /// <summary>Formato de imagen (png, jpg, bmp).</summary>
        public string Format { get; set; } = "png";
        
        /// <summary>Calidad de compresión (0-100) para formatos que lo soporten (jpg).</summary>
        public int Quality { get; set; } = 95;
        
        /// <summary>Monitor objetivo ("primary", "all", o índice).</summary>
        public string Monitor { get; set; } = "primary";
        
        /// <summary>Índice del monitor específico si Monitor != "primary".</summary>
        public int MonitorIndex { get; set; } = 0;
        
        /// <summary>Incluir el cursor del ratón en la captura.</summary>
        public bool IncludeCursor { get; set; } = false;
        
        /// <summary>Retardo en milisegundos antes de capturar.</summary>
        public int CaptureDelay { get; set; } = 0;
        
        /// <summary>Copiar imagen al portapapeles automáticamente.</summary>
        public bool CopyToClipboard { get; set; } = true;
    }

    /// <summary>
    /// Definición de un atajo de teclado global.
    /// </summary>
    public class HotKeyConfig
    {
        /// <summary>Combinación de teclas (ej. "Control+Shift+R").</summary>
        public string Key { get; set; } = "";
        
        /// <summary>Tipo de acción (audio, video, screenshot).</summary>
        public string Type { get; set; } = ""; 
        
        /// <summary>Duración específica si aplica (ej. instant replay).</summary>
        public int Duration { get; set; } 
        
        /// <summary>Modo específico de la acción (ej. "fullscreen", "selection").</summary>
        public string Mode { get; set; } = ""; 

        /// <summary>
        /// Descripción legible por humanos generada dinámicamente.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string Description 
        {
            get
            {
                if (Type == "audio") return "Grabar/Detener Audio";
                if (Type == "video") return "Grabar/Detener Video";
                if (Type == "screenshot")
                {
                    if (Mode == "selection") return "Captura (Selección)";
                    if (Mode == "fullscreen") return "Captura (Pantalla Completa)";
                    if (Mode == "selection_clipboard") return "Copiar al Portapapeles (Selección)";
                    return "Captura de Pantalla";
                }
                if (Type == "drawing") return "Modo Dibujo";
                return Type;
            }
        }
    }

    /// <summary>
    /// Gestión de límites de almacenamiento y paradas de seguridad.
    /// </summary>
    public class BufferSettings
    {
        /// <summary>Tamaño máximo permitido para una grabación en GB. Si se supera, se detiene.</summary>
        public double MaxBufferSizeGB { get; set; } = 5.0; // 5GB Default
        
        /// <summary>Conversión del límite a bytes.</summary>
        public long MaxBufferBytes => (long)(MaxBufferSizeGB * 1024 * 1024 * 1024);
    }
}
