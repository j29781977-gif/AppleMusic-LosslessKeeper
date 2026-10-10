<#
  Keeper.ps1 - entry point of AppleMusicLosslessKeeper.

  It builds src/Keeper.cs in memory (UI labels from src/strings.json become
  \uXXXX escapes, so the compiled source stays pure ASCII and cannot be broken by
  code-page issues) and runs it inside this PowerShell process.

  Why PowerShell and not a plain .exe?  On the machine this was developed on, a
  standalone .exe that asks UI Automation for the app's window only sees the
  top-level window (6 nodes), while a PowerShell process sees the whole XAML tree
  (1000+ nodes).  CLR version, UIA assemblies, bitness, application manifest and
  STA/MTA were all ruled out.  Compiling to a DLL and loading it from bytes also
  failed to load in-process, so the source is compiled in memory here.

  Usage:
    Keeper.ps1                      keep watching (used by the autostart entry)
    Keeper.ps1 -Once                fix the running instance, then exit
    Keeper.ps1 -Mode stealth        hide the window until the setting is applied
    Keeper.ps1 -Mode direct         default: never touch the window
    Keeper.ps1 -VerboseLog          log every step
    Keeper.ps1 -LogDir <path>       where keeper.log is written
#>
[CmdletBinding()]
param(
    [switch]$Once,
    [ValidateSet('direct', 'stealth', 'minimize', 'offscreen')][string]$Mode = 'direct',
    [switch]$VerboseLog,
    [switch]$NoMutex,
    [string]$LogDir = '',
    [string]$SourceDir = ''
)
$ErrorActionPreference = 'Stop'
if (-not $LogDir) { $LogDir = $PSScriptRoot }

if (-not $SourceDir) {
    foreach ($c in @((Join-Path $PSScriptRoot 'src'), $PSScriptRoot)) {
        if (Test-Path -LiteralPath (Join-Path $c 'Keeper.cs')) { $SourceDir = $c; break }
    }
}
if (-not $SourceDir) {
    Write-Error "Keeper.cs not found (looked in $PSScriptRoot\src and $PSScriptRoot)"
    exit 1
}
$csPath  = Join-Path $SourceDir 'Keeper.cs'
$strPath = Join-Path $SourceDir 'strings.json'
if (-not (Test-Path -LiteralPath $strPath)) { Write-Error "strings.json not found next to Keeper.cs"; exit 1 }

# ---------------------------------------------------------------- localization
function ConvertTo-CsEscape([string]$s) {
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $s.ToCharArray()) { [void]$sb.Append('\u' + ([int]$ch).ToString('x4')) }
    $sb.ToString()
}
function ConvertTo-CsArray($values) {
    $parts = @()
    foreach ($v in $values) { $parts += '"' + (ConvertTo-CsEscape ([string]$v)) + '"' }
    return ($parts -join ', ')
}

$strings = Get-Content -LiteralPath $strPath -Raw -Encoding UTF8 | ConvertFrom-Json
$source  = [System.IO.File]::ReadAllText($csPath, [System.Text.Encoding]::UTF8)
$source  = $source.Replace('__LOSSLESS__',  (ConvertTo-CsArray $strings.lossless))
$source  = $source.Replace('__STREAMING__', (ConvertTo-CsArray $strings.streaming))
$source  = $source.Replace('__DOWNLOAD__',  (ConvertTo-CsArray $strings.download))
$source  = $source.Replace('__HIRES__',     (ConvertTo-CsArray $strings.hires))

# ---------------------------------------------------------------- compile + run
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName WindowsBase
$refs = @(
    [System.Windows.Automation.AutomationElement].Assembly.Location
    [System.Windows.Automation.AutomationIdentifier].Assembly.Location
    [System.Windows.DependencyObject].Assembly.Location
    [System.Uri].Assembly.Location
    [System.Linq.Enumerable].Assembly.Location
) | Select-Object -Unique

Add-Type -TypeDefinition $source -ReferencedAssemblies $refs -Language CSharp -ErrorAction Stop

$argv = New-Object System.Collections.Generic.List[string]
$argv.Add("--mode=$Mode")
if ($Once)       { $argv.Add('--once') }
if ($VerboseLog) { $argv.Add('--verbose') }
if ($NoMutex)    { $argv.Add('--no-mutex') }
$argv.Add("--logdir=$LogDir")
$argv.Add("--dir=$PSScriptRoot")

[void][Keeper]::Main($argv.ToArray())
