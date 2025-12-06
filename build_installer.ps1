# Build Installer Script
# Este script compila la aplicación y genera el instalador MSI usando WiX Toolset

$ErrorActionPreference = "Stop"

# 1. Compilar la aplicación y descargar dependencias (FFmpeg)
Write-Host "=== Paso 1: Compilando Aplicación ===" -ForegroundColor Cyan
.\build_release.ps1

# 2. Verificar si WiX Toolset está instalado (o descargar versión local)
$wixPath = "C:\Program Files (x86)\WiX Toolset v3.11\bin"
$localWixPath = ".\tools\wix"

if (-not (Test-Path "$wixPath\candle.exe") -and -not (Test-Path "$localWixPath\candle.exe")) {
    Write-Host "WiX Toolset no encontrado. Descargando versión portable..." -ForegroundColor Yellow
    
    if (-not (Test-Path ".\tools")) { New-Item -ItemType Directory -Path ".\tools" | Out-Null }
    
    $wixUrl = "https://github.com/wixtoolset/wix3/releases/download/wix3112rtm/wix311-binaries.zip"
    $wixZip = ".\tools\wix.zip"
    
    try {
        Invoke-WebRequest -Uri $wixUrl -OutFile $wixZip
        Expand-Archive -Path $wixZip -DestinationPath $localWixPath -Force
        Remove-Item $wixZip
        Write-Host "WiX Toolset descargado correctamente." -ForegroundColor Green
    }
    catch {
        Write-Host "Error descargando WiX Toolset: $_" -ForegroundColor Red
        exit 1
    }
}

if (Test-Path "$localWixPath\candle.exe") {
    Write-Host "Usando WiX Toolset local en $localWixPath" -ForegroundColor Cyan
    $wixPath = (Resolve-Path $localWixPath).Path
}

# Agregar WiX al PATH temporalmente
$env:Path += ";$wixPath"

# 3. Compilar el instalador (Candle)
Write-Host "=== Paso 2: Generando objetos WiX (Candle) ===" -ForegroundColor Cyan
$publishDir = (Resolve-Path ".\publish").Path
# Nota: Pasamos la variable PublishDir para que setup.wxs sepa dónde buscar los archivos
candle.exe -dPublishDir="$publishDir" -arch x64 -ext WixUIExtension -out installer\setup.wixobj installer\setup.wxs

if ($LASTEXITCODE -ne 0) {
    Write-Host "Error al ejecutar candle.exe" -ForegroundColor Red
    exit 1
}

# 4. Enlazar el instalador (Light)
Write-Host "=== Paso 3: Creando MSI (Light) ===" -ForegroundColor Cyan
# -sval suprime validaciones estrictas que a veces fallan falsamente
light.exe -ext WixUIExtension -out ClipStudioDesktop_Setup.msi installer\setup.wixobj -sval

if ($LASTEXITCODE -ne 0) {
    Write-Host "Error al ejecutar light.exe" -ForegroundColor Red
    exit 1
}

Write-Host "=== ¡Instalador Creado con Éxito! ===" -ForegroundColor Green
Write-Host "Archivo: $(Resolve-Path .\ClipStudioDesktop_Setup.msi)" -ForegroundColor Green
