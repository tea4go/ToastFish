@echo off
setlocal
rem ToastFish local build entry: forwards all arguments to build.ps1
rem Usage:
rem   build.bat
rem   build.bat -Configuration Debug -Platform x64 -Clean
rem   build.bat -Package -OutputZip dist\ToastFish.zip
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
exit /b %ERRORLEVEL%
