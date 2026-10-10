@echo off
rem 用记事本打开 run-hidden.vbs（里面可改 -Mode 参数）
if exist "%~dp0run-hidden.vbs" (
    start "" notepad.exe "%~dp0run-hidden.vbs"
) else (
    start "" notepad.exe "%LOCALAPPDATA%\Programs\AppleMusicLosslessKeeper\run-hidden.vbs"
)
