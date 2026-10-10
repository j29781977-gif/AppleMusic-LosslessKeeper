@echo off
rem Install AppleMusicLosslessKeeper for the current user (no admin needed).
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1"
echo.
pause
