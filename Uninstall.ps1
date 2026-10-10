<#
  Uninstall.ps1 - remove AppleMusicLosslessKeeper from this user account.
#>
[CmdletBinding()]
param(
    [string]$Target = "$env:LOCALAPPDATA\Programs\AppleMusicLosslessKeeper"
)
$ErrorActionPreference = 'Continue'
$taskName = 'AppleMusicLosslessKeeper'

Write-Host "== 卸载 Apple Music 无损音质保持器 =="

# 1. stop running keepers (any copy installed under the target folder)
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -and $_.CommandLine -like '*Keeper.ps1*' -and $_.CommandLine -like "*$Target*" } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue; Write-Host "  已停止进程 $($_.ProcessId)" }

# 2. autostart entries
try { Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction Stop; Write-Host "  已删除登录启动任务" } catch { }
$startupVbs = Join-Path ([Environment]::GetFolderPath('Startup')) "$taskName.vbs"
if (Test-Path -LiteralPath $startupVbs) { Remove-Item -LiteralPath $startupVbs -Force; Write-Host "  已移除启动文件夹项" }

# 3. files
if (Test-Path -LiteralPath $Target) { Remove-Item -LiteralPath $Target -Recurse -Force; Write-Host "  已删除 $Target" }
Write-Host "完成。"
