@echo off
setlocal
rem ToastFish local run entry: kill old process, rebuild Debug, launch, pause
cd /d "%~dp0"

call pkill ToastFish.exe 2>nul
del /q "bin\Debug\ToastFish.exe" 2>nul
del /q "bin\Release\ToastFish.exe" 2>nul

call "%~dp0build.bat" -Configuration Debug
if errorlevel 1 (
    echo [runwin] build failed, abort.
    pause
    exit /b 1
)

start "" "%~dp0bin\Debug\ToastFish.exe"
