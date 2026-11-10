# MD Search to Selection - no-admin installer / uninstaller / check (portable)
# Author: MD (Mayank Dholariya)
# Installs for the current Windows user only into %APPDATA%\Autodesk\ApplicationPlugins\MD.SearchToSelection.bundle
param([ValidateSet('Install','Uninstall','Check')][string]$Action = 'Install')
$ErrorActionPreference = 'Stop'
$Here   = Split-Path -Parent $MyInvocation.MyCommand.Path
$Src    = Join-Path $Here 'bundles\MD.SearchToSelection.bundle'
$Target = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'
$Dest   = Join-Path $Target 'MD.SearchToSelection.bundle'

function Say($t, $c = 'Gray') { Write-Host $t -ForegroundColor $c }
function Ver($dir) {
  $x = Join-Path $dir 'PackageContents.xml'
  if (-not (Test-Path $x)) { return $null }
  try { return ([xml](Get-Content $x -Raw)).ApplicationPackage.AppVersion } catch { return '?' }
}
function Get-NavisDirs {
  $dirs = @()
  $pf = $env:ProgramW6432; if (-not $pf) { $pf = $env:ProgramFiles }
  $base = Join-Path $pf 'Autodesk'
  if (Test-Path $base) {
    $dirs += Get-ChildItem $base -Directory -ErrorAction SilentlyContinue |
      Where-Object { $_.Name -match '^Navisworks (Manage|Simulate) \d{4}$' -and (Test-Path (Join-Path $_.FullName 'Roamer.exe')) } |
      ForEach-Object { $_.FullName }
  }
  return $dirs | Sort-Object -Unique
}
function Test-NavisRunning {
  if (Get-Process -Name Roamer -ErrorAction SilentlyContinue) {
    Say ''; Say 'Navisworks is OPEN (Roamer.exe is running). Close every Navisworks window, then run this again.' Yellow
    return $true
  }
  return $false
}
function Show-Status {
  Say ''
  $nw = Get-NavisDirs
  if ($nw) { Say 'Navisworks found:' Cyan; $nw | ForEach-Object { Say "  $_" } } else { Say 'Navisworks: none found in Program Files (the add-in still installs).' Yellow }
  $v = Ver $Dest
  if ($v) { Say "MD Search to Selection installed: v$v  ($Dest)" Green } else { Say 'MD Search to Selection: not installed for this user.' Yellow }
}

Say "MD Search to Selection  -  $Action  (no admin)   Author: MD (Mayank Dholariya)" Cyan
try {
  switch ($Action) {
    'Install' {
      if (-not (Test-Path (Join-Path $Src 'PackageContents.xml'))) { throw "bundles\MD.SearchToSelection.bundle not found next to this script. Extract the whole zip first." }
      if (Test-NavisRunning) { exit 2 }
      New-Item -ItemType Directory -Force -Path $Target | Out-Null
      if (Test-Path $Dest) { Remove-Item -Recurse -Force (Join-Path $Dest 'Contents') -ErrorAction SilentlyContinue }
      Copy-Item -Recurse -Force $Src $Target
      Get-ChildItem -Recurse -File $Dest | Unblock-File -ErrorAction SilentlyContinue
      Say "Installed v$(Ver $Dest) to $Dest" Green
      Say 'Start Navisworks, then Tool add-ins 1 > MD Search to Selection.'
      Show-Status
    }
    'Uninstall' {
      if (Test-NavisRunning) { exit 2 }
      if (Test-Path $Dest) { Remove-Item -Recurse -Force $Dest; Say "Removed $Dest" Green } else { Say 'Not installed - nothing to remove.' Yellow }
      Say "Your settings in $env:APPDATA\MDSearchToSelection (log) were kept."
    }
    'Check' { Show-Status }
  }
} catch { Say "ERROR: $($_.Exception.Message)" Red; exit 1 }
