@echo off
cd /d "%~dp0Source\bin\Debug"
if not exist "Chrome.exe" (
    echo FishingFun UI executable not found. Build Source\FishingFunUI\FishingFun.UI.csproj first.
    pause
    exit /b 1
)
start "" "Chrome.exe"
