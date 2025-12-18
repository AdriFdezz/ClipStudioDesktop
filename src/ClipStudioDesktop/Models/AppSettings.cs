using System.Collections.Generic;
using System.IO;
using System;

namespace ClipStudioDesktop.Models
{
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

    public class GeneralSettings
    {
        public bool StartWithWindows { get; set; } = true;
        public bool ShowNotifications { get; set; } = true;
        public bool PlaySoundOnClip { get; set; } = false;
    }

    public class PathSettings
    {
        public string TempBuffer { get; set; } = Path.Combine(Path.GetTempPath(), "ClipStudioDesktop", "buffer");
        public string AudioClips { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ClipStudio", "Audio");
        public string VideoClips { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ClipStudio", "Video");
        public string Screenshots { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ClipStudio", "Imagenes");
    }

    public class AudioSettings
    {
        public string Format { get; set; } = "mp3";
        public int Bitrate { get; set; } = 192;
        public int SampleRate { get; set; } = 48000;
        public int Channels { get; set; } = 2;
        public string Source { get; set; } = "system";
        public string SelectedAudioDevice { get; set; } = ""; // Empty = auto-detect
        public bool EnableMicrophone { get; set; } = false; // Include microphone in recordings
        public string SelectedMicrophone { get; set; } = ""; // Empty = default microphone
    }

    public class VideoSettings
    {
        public string Format { get; set; } = "mp4";
        public string Codec { get; set; } = "h264";
        public string Resolution { get; set; } = "1920x1080";
        public int Framerate { get; set; } = 60;
        public int Bitrate { get; set; } = 8000;
        public string Compression { get; set; } = "balanced";
        public bool CapturePrimaryMonitorOnly { get; set; } = true; // Use Desktop Duplication for primary monitor only
    }

    public class ScreenshotSettings
    {
        public string Format { get; set; } = "png";
        public int Quality { get; set; } = 95;
        public string Monitor { get; set; } = "primary";
        public int MonitorIndex { get; set; } = 0;
        public bool IncludeCursor { get; set; } = false;
        public int CaptureDelay { get; set; } = 0;
        public bool CopyToClipboard { get; set; } = true;
    }

    public class HotKeyConfig
    {
        public string Key { get; set; } = "";
        public string Type { get; set; } = ""; // audio, video, screenshot
        public int Duration { get; set; } // seconds
        public string Mode { get; set; } = ""; // selection, fullscreen (for screenshots)
    }

    public class BufferSettings
    {
        // Tamaño máximo del buffer en GB (para audio + video combinados)
        public double MaxBufferSizeGB { get; set; } = 5.0; // 5GB por defecto
        
        // Calculados en bytes para uso interno
        public long MaxBufferBytes => (long)(MaxBufferSizeGB * 1024 * 1024 * 1024);
    }
}
