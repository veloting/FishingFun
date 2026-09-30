@echo off
setlocal DisableDelayedExpansion
if not exist "%~dp0Source\bin\Debug\Chrome.exe" (
    echo FishingFun UI executable not found. Build Source\FishingFunUI\FishingFun.UI.csproj first.
    pause
    exit /b 1
)
set "FISHINGFUN_START_DIRECTORY=%~dp0Source\bin\Debug"
set "FISHINGFUN_START_MODE=%~1"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -Command "$ErrorActionPreference = 'Stop'; try { $launch = @{ FilePath = (Join-Path $env:FISHINGFUN_START_DIRECTORY 'Chrome.exe'); WorkingDirectory = $env:FISHINGFUN_START_DIRECTORY }; if ($env:FISHINGFUN_START_MODE -ine '/normal') { $launch.Verb = 'RunAs' }; Start-Process @launch } catch { Write-Host ('FishingFun was not started. Administrator approval may have been cancelled, or launch failed: ' + $_.Exception.Message); exit 1 }"
if errorlevel 1 (
    pause
    exit /b 1
)
exit /b 0
