@echo off
setlocal
rem ToastFish release entry: forwards all arguments to putver.ps1
rem Reads the Gitee token from .env, creates the Gitee release from the MSI
rem already in bin\Release and uploads it as an attachment. It does not build.
rem Usage:
rem   putver.bat
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0putver.ps1" %*
exit /b %ERRORLEVEL%
