@echo off
rem 在 direct（窗口秒开）和 stealth（完全无界面变化）之间切换
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Switch-Mode.ps1"
echo.
pause
