<#
  Switch-Mode.ps1 - 在两种工作模式间一键切换，并自动重启监视器。

    direct  : 窗口瞬间正常打开，后台改设置（设置页约闪现 1.3 秒）—— 默认
    stealth : 完全看不到界面变化，但窗口会晚约 8~10 秒才出现
#>
[CmdletBinding()]
param([string]$Dir = '')
$ErrorActionPreference = 'Continue'
if (-not $Dir) {
    if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'run-hidden.vbs')) { $Dir = $PSScriptRoot }
    else { $Dir = Join-Path $env:LOCALAPPDATA 'Programs\AppleMusicLosslessKeeper' }
}
$vbs = Join-Path $Dir 'run-hidden.vbs'
if (-not (Test-Path -LiteralPath $vbs)) { Write-Host "找不到 $vbs"; exit 1 }

$text = Get-Content -LiteralPath $vbs -Raw -Encoding ASCII
if ($text -match '-Mode\s+stealth') {
    $text = $text -replace '-Mode\s+stealth', '-Mode direct'
    $new = 'direct'
} else {
    $text = $text -replace '-Mode\s+direct', '-Mode stealth'
    if ($text -notmatch '-Mode\s+stealth') { $text = $text.TrimEnd() + " -Mode stealth`r`n" }
    $new = 'stealth'
}
Set-Content -LiteralPath $vbs -Value $text -Encoding ASCII
Write-Host "已切换为：$new"

Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -and $_.CommandLine -like '*Keeper.ps1*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2
Start-Process -FilePath 'wscript.exe' -ArgumentList "`"$vbs`"" -WindowStyle Hidden
Start-Sleep -Seconds 3

switch ($new) {
    'direct'  { Write-Host '现在：Apple Music 窗口瞬间打开；后台修改时设置页会闪现约 1.3 秒。' }
    'stealth' { Write-Host '现在：完全看不到界面变化；但窗口会晚约 8~10 秒才出现。' }
}
Write-Host ""
Write-Host "配置文件：$vbs"
