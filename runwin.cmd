cd /d C:\MyWork\GitCode\ToastFish
del .\bin\Debug\ToastFish.exe
del .\bin\Release\ToastFish.exe
call pkill ToastFish.exe
call build.bat -Configuration Debug
"C:\MyWork\GitCode\ToastFish\bin\Debug\ToastFish.exe"
pause

