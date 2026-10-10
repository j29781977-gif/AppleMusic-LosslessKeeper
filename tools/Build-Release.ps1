<#
  Build-Release.ps1 - build the ZIP that you attach to a GitHub release.

  Usage:  powershell -File tools\Build-Release.ps1 [-Version 1.2.0]
  Output: dist\AppleMusicLosslessKeeper-<version>.zip
#>
[CmdletBinding()]
param(
    [string]$Version = '1.2.0'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist "AppleMusicLosslessKeeper-$Version"
Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'src') | Out-Null

$files = @('Keeper.ps1', 'run-hidden.vbs', 'Install.cmd', 'Install.ps1', 'Uninstall.cmd', 'Uninstall.ps1',
           'Fix-Now.cmd', 'Switch-Mode.cmd', 'Switch-Mode.ps1', 'Edit-Config.cmd', 'README.md', 'LICENSE')
foreach ($f in $files) {
    $p = Join-Path $root $f
    if (Test-Path -LiteralPath $p) { Copy-Item -LiteralPath $p -Destination $stage -Force }
}
foreach ($f in @('Keeper.cs', 'strings.json')) {
    Copy-Item -LiteralPath (Join-Path $root "src\$f") -Destination (Join-Path $stage 'src') -Force
}
# make sure the scripts are readable by Windows PowerShell 5.1 (UTF-8 with BOM)
foreach ($ps in Get-ChildItem -LiteralPath $stage -Recurse -Filter *.ps1) {
    $text = Get-Content -LiteralPath $ps.FullName -Raw -Encoding UTF8
    [System.IO.File]::WriteAllText($ps.FullName, $text, (New-Object System.Text.UTF8Encoding($true)))
}

$zip = Join-Path $dist "AppleMusicLosslessKeeper-$Version.zip"
Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ("release built: {0} ({1} bytes)" -f $zip, (Get-Item -LiteralPath $zip).Length)
