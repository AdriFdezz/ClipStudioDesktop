# Clip Studio Desktop

**Clip Studio Desktop** es una aplicación de Windows para captura instantánea de audio, video y capturas de pantalla mediante atajos de teclado. La aplicación graba continuamente audio y video en segundo plano y permite al usuario guardar clips de los últimos 30, 60, 90, 120 o 300 segundos.

## Características Principales

- **Inicio Automático**: Se ejecuta en segundo plano al iniciar Windows.
- **Buffer Circular**: Grabación continua sin llenar el disco, manteniendo solo los últimos minutos.
- **Atajos Globales**:
  - `CTRL + [1-5]`: Guardar audio (30s - 300s).
  - `ALT + [1-5]`: Guardar video (30s - 300s).
  - `ALT + X`: Captura de pantalla con selección.
  - `ALT + C`: Captura de pantalla completa.
- **Formatos**:
  - Audio: MP3, WAV, etc.
  - Video: MP4, AVI, etc.
  - Imágenes: PNG, JPEG, etc.
- **Interfaz**: Icono en bandeja del sistema y ventana de configuración WPF/WinUI.

## Requisitos Técnicos

- **OS**: Windows 10/11 (64-bit)
- **Framework**: .NET 8.0 (WPF/WinUI 3)

## Desarrollo

Consulte el archivo `Documento Tecnico - Clip Studio Desktop.md` para más detalles sobre la especificación.
