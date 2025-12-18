# Script para ver los logs de debug de ClipStudioDesktop
# Los mensajes Debug.WriteLine se pueden ver con DebugView o directamente con este script

Write-Host "Iniciando ClipStudioDesktop y monitoreando logs..." -ForegroundColor Green
Write-Host "Presiona Ctrl+C para detener" -ForegroundColor Yellow
Write-Host ""

# Iniciar la aplicación
$process = Start-Process -FilePath ".\src\ClipStudioDesktop\bin\Debug\net8.0-windows\ClipStudioDesktop.exe" -PassThru

Write-Host "Aplicación iniciada (PID: $($process.Id))" -ForegroundColor Cyan
Write-Host ""
Write-Host "NOTA: Para ver los logs de Debug.WriteLine:" -ForegroundColor Yellow
Write-Host "1. Descarga DebugView de https://learn.microsoft.com/en-us/sysinternals/downloads/debugview" -ForegroundColor White
Write-Host "2. Ejecuta Dbgview.exe como administrador" -ForegroundColor White
Write-Host "3. Ve a Capture > Capture Global Win32" -ForegroundColor White
Write-Host "4. Los mensajes de MediaFoundationRecorder aparecerán ahí" -ForegroundColor White
Write-Host ""
Write-Host "Presiona Enter para cerrar la aplicación y salir..." -ForegroundColor Yellow

Read-Host

if (!$process.HasExited) {
    Stop-Process -Id $process.Id -Force
    Write-Host "Aplicación cerrada" -ForegroundColor Green
}
