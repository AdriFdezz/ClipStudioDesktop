# Instalación de Clip Studio Desktop

## Opción 1: Ejecutable Portable (Recomendado para desarrollo)

1. Ejecuta el script de construcción:
   ```powershell
   .\build_release.ps1
   ```
   Este script descargará automáticamente FFmpeg y compilará la aplicación.

2. Ve a la carpeta `publish`.
3. Ejecuta `ClipStudioDesktop.exe`.

## Opción 2: Crear Instalador MSI (Requiere WiX Toolset)

Si deseas crear un instalador `.msi` profesional:

1. Instala **WiX Toolset v3.11** (o superior) desde [wixtoolset.org](https://wixtoolset.org/).
2. Asegúrate de haber ejecutado `.\build_release.ps1` primero para generar los archivos en `publish`.
3. Abre una terminal en la carpeta `installer`.
4. Ejecuta los siguientes comandos (reemplaza `PUT-GUID-HERE` en `setup.wxs` con GUIDs reales generados si es para producción):

   ```powershell
   # Compilar
   candle setup.wxs -dPublishDir="..\publish" -arch x64

   # Enlazar
   light setup.wixobj -o ClipStudioDesktop.msi -ext WixUIExtension
   ```

5. Obtendrás `ClipStudioDesktop.msi` que instalará la aplicación y FFmpeg en `Program Files`.
