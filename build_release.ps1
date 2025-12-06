# Build Script for Clip Studio Desktop

Write-Host "Building Clip Studio Desktop..." -ForegroundColor Cyan

# 1. Clean previous build
if (Test-Path ".\publish") {
    Remove-Item ".\publish" -Recurse -Force
}

# 2. Publish single-file executable
# -c Release: Release configuration (optimized)
# -r win-x64: Target Windows 64-bit
# --self-contained true: Include .NET Runtime (no need to install .NET on target machine)
# -p:PublishSingleFile=true: Bundle everything into one .exe
# -p:IncludeNativeLibrariesForSelfExtract=true: Extract native libs automatically
Write-Host "Publishing self-contained executable..." -ForegroundColor Yellow
dotnet publish src/ClipStudioDesktop/ClipStudioDesktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish

# 3. Copy FFmpeg if present
if (Test-Path "ffmpeg.exe") {
    Write-Host "Copying ffmpeg.exe..." -ForegroundColor Yellow
    Copy-Item "ffmpeg.exe" ".\publish\ffmpeg.exe"
} else {
    Write-Host "WARNING: ffmpeg.exe not found in root. Please copy it to the publish folder manually." -ForegroundColor Red
}

Write-Host "Build complete!" -ForegroundColor Green
Write-Host "Executable is located in: .\publish\ClipStudioDesktop.exe" -ForegroundColor Cyan
