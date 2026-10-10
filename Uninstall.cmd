@echo off
rem Remove AppleMusicLosslessKeeper from this user account.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall.ps1"
echo.
pause
