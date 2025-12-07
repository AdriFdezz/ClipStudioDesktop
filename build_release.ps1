# Build Script for Clip Studio Desktop

Write-Host "Building Clip Studio Desktop..." -ForegroundColor Cyan

# 0. Check for .NET SDK
try {
    $dotnetVersion = dotnet --version
    Write-Host "Found .NET SDK version: $dotnetVersion" -ForegroundColor Gray
} catch {
    Write-Host "ERROR: .NET SDK not found in PATH." -ForegroundColor Red
    Write-Host "Please install .NET 8.0 SDK from: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Red
    exit 1
}

# 1. Clean previous build
if (Test-Path ".\publish") {
    Remove-Item ".\publish" -Recurse -Force
}

# 2. Download FFmpeg if not present
$ffmpegPath = ".\ffmpeg.exe"
if (-not (Test-Path $ffmpegPath)) {
    Write-Host "FFmpeg not found. Downloading..." -ForegroundColor Yellow
    $ffmpegUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip"
    $zipPath = ".\ffmpeg.zip"
    
    try {
        Invoke-WebRequest -Uri $ffmpegUrl -OutFile $zipPath
        
        Write-Host "Extracting FFmpeg..." -ForegroundColor Yellow
        Expand-Archive -Path $zipPath -DestinationPath ".\ffmpeg_temp" -Force
        
        # Find ffmpeg.exe in the extracted folder (it's usually in a subfolder)
        $extractedFfmpeg = Get-ChildItem -Path ".\ffmpeg_temp" -Recurse -Filter "ffmpeg.exe" | Select-Object -First 1
        
        if ($extractedFfmpeg) {
            Copy-Item $extractedFfmpeg.FullName -Destination $ffmpegPath
            Write-Host "FFmpeg downloaded and placed in root." -ForegroundColor Green
        } else {
            Write-Host "Error: Could not find ffmpeg.exe in the downloaded zip." -ForegroundColor Red
        }
    }
    catch {
        Write-Host "Failed to download FFmpeg: $_" -ForegroundColor Red
    }
    finally {
        # Cleanup
        if (Test-Path $zipPath) { Remove-Item $zipPath }
        if (Test-Path ".\ffmpeg_temp") { Remove-Item ".\ffmpeg_temp" -Recurse -Force }
    }
}

# 3. Publish single-file executable
# -c Release: Release configuration (optimized)
# -r win-x64: Target Windows 64-bit
# --self-contained true: Include .NET Runtime (no need to install .NET on target machine)
# -p:PublishSingleFile=true: Bundle everything into one .exe
# -p:IncludeNativeLibrariesForSelfExtract=true: Extract native libs automatically
Write-Host "Publishing self-contained executable..." -ForegroundColor Yellow
dotnet publish src/ClipStudioDesktop/ClipStudioDesktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish

# 4. Copy FFmpeg to publish folder
if (Test-Path $ffmpegPath) {
    Write-Host "Copying ffmpeg.exe to publish folder..." -ForegroundColor Yellow
    Copy-Item $ffmpegPath ".\publish\ffmpeg.exe"
} else {
    Write-Host "WARNING: ffmpeg.exe still not found. The installer will be missing it." -ForegroundColor Red
}

Write-Host "Build complete!" -ForegroundColor Green
Write-Host "Executable is located in: .\publish\ClipStudioDesktop.exe" -ForegroundColor Cyan
