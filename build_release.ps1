# Script de Compilación para Clip Studio Desktop
# Este script compila el proyecto, gestiona dependencias como FFmpeg y prepara los archivos para distribución.

Write-Host "Compilando Clip Studio Desktop..." -ForegroundColor Cyan

# 0. Verificar el SDK de .NET
try {
    $dotnetVersion = dotnet --version
    Write-Host "SDK de .NET encontrado: versión $dotnetVersion" -ForegroundColor Gray
} catch {
    Write-Host "ERROR: No se encontró el SDK de .NET en el PATH." -ForegroundColor Red
    Write-Host "Por favor instala el SDK de .NET 8.0 desde: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Red
    exit 1
}

# 1. Limpiar compilación anterior
if (Test-Path ".\publish") {
    Remove-Item ".\publish" -Recurse -Force
}

# 2. Descargar FFmpeg si no está presente
$ffmpegPath = ".\ffmpeg.exe"
if (-not (Test-Path $ffmpegPath)) {
    Write-Host "FFmpeg not found. Downloading..." -ForegroundColor Yellow
    $ffmpegUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip"
    $zipPath = ".\ffmpeg.zip"
    
    try {
        Invoke-WebRequest -Uri $ffmpegUrl -OutFile $zipPath
        
        Write-Host "Extrayendo FFmpeg..." -ForegroundColor Yellow
        Expand-Archive -Path $zipPath -DestinationPath ".\ffmpeg_temp" -Force
        
        # Buscar ffmpeg.exe en la carpeta extraída (usualmente está en una subcarpeta)
        $extractedFfmpeg = Get-ChildItem -Path ".\ffmpeg_temp" -Recurse -Filter "ffmpeg.exe" | Select-Object -First 1
        
        if ($extractedFfmpeg) {
            Copy-Item $extractedFfmpeg.FullName -Destination $ffmpegPath
            Write-Host "FFmpeg descargado y colocado en la raíz." -ForegroundColor Green
        } else {
            Write-Host "Error: No se pudo encontrar ffmpeg.exe en el zip descargado." -ForegroundColor Red
        }
    }
    catch {
        Write-Host "Error al descargar FFmpeg: $_" -ForegroundColor Red
    }
    finally {
        # Cleanup
        if (Test-Path $zipPath) { Remove-Item $zipPath }
        if (Test-Path ".\ffmpeg_temp") { Remove-Item ".\ffmpeg_temp" -Recurse -Force }
    }
}

# 3. Publicar ejecutable autocontenido (Single-file)
# -c Release: Configuración de Release (optimizado)
# -r win-x64: Objetivo Windows 64-bit
# --self-contained true: Incluir .NET Runtime (no requiere instalar .NET en la máquina destino)
# -p:PublishSingleFile=true: Empaquetar todo en un solo .exe
# -p:IncludeNativeLibrariesForSelfExtract=true: Extraer librerías nativas automáticamente
Write-Host "Publicando ejecutable autocontenido..." -ForegroundColor Yellow
dotnet publish src/ClipStudioDesktop/ClipStudioDesktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish

# 4. Copiar FFmpeg a la carpeta de publicación
if (Test-Path $ffmpegPath) {
    Write-Host "Copiando ffmpeg.exe a la carpeta de publicación..." -ForegroundColor Yellow
    Copy-Item $ffmpegPath ".\publish\ffmpeg.exe"
} else {
    Write-Host "ADVERTENCIA: ffmpeg.exe no encontrado aún. El instalador no lo incluirá." -ForegroundColor Red
}

# 5. Copiar Assets a la carpeta de publicación
$assetsPath = "src/ClipStudioDesktop/assets"
if (Test-Path $assetsPath) {
    Write-Host "Copiando assets a la carpeta de publicación..." -ForegroundColor Yellow
    $destAssets = ".\publish\assets"
    if (-not (Test-Path $destAssets)) { New-Item -ItemType Directory -Path $destAssets | Out-Null }
    Copy-Item "$assetsPath\*" $destAssets -Recurse -Force
}

Write-Host "¡Compilación completada!" -ForegroundColor Green
Write-Host "El ejecutable se encuentra en: .\publish\ClipStudioDesktop.exe" -ForegroundColor Cyan
