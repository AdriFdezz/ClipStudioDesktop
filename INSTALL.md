# Instalación de Clip Studio Desktop

## Opción 1: Ejecutable Portable (Recomendado para desarrollo)

1. Ejecuta el script de construcción:
   ```powershell
   .\build_release.ps1
   ```
   Este script descargará automáticamente FFmpeg y compilará la aplicación.

2. Ve a la carpeta `publish`.
3. Ejecuta `ClipStudioDesktop.exe`.

## Opción 2: Crear Instalador MSI (Automático)

Si deseas crear un instalador `.msi` profesional "Todo en Uno":

1. Ejecuta el script de instalación completo:
   ```powershell
   .\build_installer.ps1
   ```
   Este script se encargará de:
   - Compilar la aplicación.
   - Descargar FFmpeg automáticamente.
   - **Descargar WiX Toolset automáticamente** (si no está instalado).
   - Generar el instalador MSI con interfaz gráfica.

2. Encontrarás el instalador `ClipStudioDesktop_Setup.msi` en la carpeta raíz.


