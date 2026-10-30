@echo off
setlocal
rem ToastFish MSI packaging entry: forwards all arguments to buildmsi.ps1
rem Usage:
rem   buildmsi.bat
rem   buildmsi.bat -SkipBuild
rem   buildmsi.bat -Version 3.0.2 -Output out\MyApp.msi

rem 清理上一次生成的安装介质（.msi 与 .wixpdb），目录不存在时跳过
if exist "bin\Release" del /q "bin\Release\ToastFish-*-x64.msi" "bin\Release\ToastFish-*-x64.wixpdb" >nul 2>nul

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0buildmsi.ps1" %*
exit /b %ERRORLEVEL%
