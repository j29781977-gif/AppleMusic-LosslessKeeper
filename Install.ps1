<#
  Install.ps1 - install AppleMusicLosslessKeeper for the current user.

  * copies the keeper into %LOCALAPPDATA%\Programs\AppleMusicLosslessKeeper
  * makes it start at logon (scheduled task when allowed, otherwise the
    Startup folder)
  * starts it right away

  No administrator rights are needed.
#>
[CmdletBinding()]
param(
    [string]$Target = "$env:LOCALAPPDATA\Programs\AppleMusicLosslessKeeper"
)
$ErrorActionPreference = 'Continue'
$src = $PSScriptRoot
$taskName = 'AppleMusicLosslessKeeper'
function Say($m) { Write-Host $m }

Say "== Apple Music 无损音质保持器 安装 =="
Say "源目录 : $src"
Say "安装到 : $Target"

New-Item -ItemType Directory -Force -Path $Target | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $Target 'src') | Out-Null
foreach ($f in 'Keeper.ps1', 'run-hidden.vbs', 'Fix-Now.cmd', 'Uninstall.ps1', 'Uninstall.cmd',
                 'Switch-Mode.ps1', 'Switch-Mode.cmd', 'Edit-Config.cmd', 'README.md') {
    if (Test-Path -LiteralPath (Join-Path $src $f)) {
        Copy-Item -LiteralPath (Join-Path $src $f) -Destination $Target -Force
        Say "  已复制 $f"
    }
}
foreach ($f in 'Keeper.cs', 'strings.json') {
    Copy-Item -LiteralPath (Join-Path $src "src\$f") -Destination (Join-Path $Target 'src') -Force
    Say "  已复制 src\$f"
}
Remove-Item -LiteralPath (Join-Path $Target 'keeper.log') -Force -ErrorAction SilentlyContinue
# drop files that older versions installed
foreach ($stale in 'AppleMusicLosslessKeeper.dll', 'AppleMusicLosslessKeeper.exe', 'good-LibraryPreferences.musicdb') {
    Remove-Item -LiteralPath (Join-Path $Target $stale) -Force -ErrorAction SilentlyContinue
}

# --- 先检查 Apple Music 是否安装 -------------------------------------------
$pkgRoot = Join-Path $env:LOCALAPPDATA 'Packages'
$pkg = $null
if (Test-Path -LiteralPath $pkgRoot) {
    $pkg = Get-ChildItem -LiteralPath $pkgRoot -Directory -Filter 'AppleInc.AppleMusicWin_*' -ErrorAction SilentlyContinue |
           Select-Object -First 1
}
if ($pkg) { Say "  检测到 Apple Music 包: $($pkg.Name)" }
else { Say "  提示：没有找到 Apple Music (AppleInc.AppleMusicWin_*)，程序仍会安装并等待它出现。" }

# --- autostart: scheduled task first, Startup folder as fallback ------------
$autostart = ''
try {
    $action   = New-ScheduledTaskAction -Execute 'wscript.exe' -Argument "`"$Target\run-hidden.vbs`""
    $trigger  = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Force -ErrorAction Stop | Out-Null
    $autostart = 'logon task'
    Say "  已注册登录启动任务: $taskName"
} catch {
    Say ("  计划任务不可用（{0}），改用启动文件夹" -f $_.Exception.Message)
    $startup = [Environment]::GetFolderPath('Startup')
    Copy-Item -LiteralPath (Join-Path $Target 'run-hidden.vbs') -Destination (Join-Path $startup "$taskName.vbs") -Force
    $autostart = 'startup folder'
    Say "  已放入启动文件夹: $startup"
}

# --- start now -------------------------------------------------------------
Start-Process -FilePath 'wscript.exe' -ArgumentList "`"$Target\run-hidden.vbs`"" -WindowStyle Hidden
Start-Sleep -Seconds 3
Say "  已在后台启动"
Say ""
Say "完成（自启动方式: $autostart）。"
Say "每次你打开 Apple Music，它都会检查：若「无损音频」没打开就打开它，并把"
Say "「流播放」质量设为「高解析度无损」（ALAC 最高 24 位/192 kHz）——「下载」质量不动。"
Say "日志    : $Target\keeper.log"
Say "切换模式: 双击 Switch-Mode.cmd（direct 秒开 / stealth 全无界面变化）"
Say "卸载    : 双击 Uninstall.cmd"
