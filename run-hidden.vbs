' run-hidden.vbs - start the keeper with no console window (used by autostart)
Option Explicit
Dim fso, sh, dir, cmd
Set fso = CreateObject("Scripting.FileSystemObject")
Set sh  = CreateObject("WScript.Shell")
dir = fso.GetParentFolderName(WScript.ScriptFullName)
cmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ _
      & dir & "\Keeper.ps1"" -Mode direct"
sh.Run cmd, 0, False
