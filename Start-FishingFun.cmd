@echo off
if not exist "%~dp0Source\bin\Debug\Chrome.exe" (
    echo FishingFun UI executable not found. Build Source\FishingFunUI\FishingFun.UI.csproj first.
    pause
    exit /b 1
)
cd /d "%~dp0Source\bin\Debug"
start "" "Chrome.exe"
