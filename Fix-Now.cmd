@echo off
rem 立刻检查并修正一次（不常驻），用于手动触发或验证。
rem Fix the audio quality of the running Apple Music instance, once, then exit.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Keeper.ps1" -Once
echo.
pause
