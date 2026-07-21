# Launches RimWorld with ONLY Core + DLCs + The Shattered Crown enabled.
# Backs up your real mod list first, then restores it automatically when the
# game exits (or on Ctrl+C). Your normal 300-mod setup is never lost.
#
# Usage:  powershell -ExecutionPolicy Bypass -File scripts\Launch-TestMode.ps1
# Optional: -QuickTest  (skips the main menu, generates a small dev test map)

param([switch]$QuickTest)

$gameDir = "X:\SteamLibrary\steamapps\common\RimWorld"
$cfgDir  = "$env:USERPROFILE\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios"
$cfg     = Join-Path $cfgDir "Config\ModsConfig.xml"
$backup  = "$cfg.userbackup"

if (Get-Process RimWorldWin64 -ErrorAction SilentlyContinue) {
    Write-Host "RimWorld is already running - close it first." -ForegroundColor Red
    exit 1
}

Copy-Item $cfg $backup -Force
Write-Host "Backed up your mod list to $backup"

@'
<?xml version="1.0" encoding="utf-8"?>
<ModsConfigData>
  <version>1.6.4871 rev590</version>
  <activeMods>
    <li>brrainz.harmony</li>
    <li>ludeon.rimworld</li>
    <li>ludeon.rimworld.royalty</li>
    <li>ludeon.rimworld.ideology</li>
    <li>ludeon.rimworld.biotech</li>
    <li>ludeon.rimworld.anomaly</li>
    <li>ludeon.rimworld.odyssey</li>
    <li>cfrolik.theshatteredcrown</li>
  </activeMods>
  <knownExpansions>
    <li>ludeon.rimworld.royalty</li>
    <li>ludeon.rimworld.ideology</li>
    <li>ludeon.rimworld.biotech</li>
    <li>ludeon.rimworld.anomaly</li>
    <li>ludeon.rimworld.odyssey</li>
  </knownExpansions>
</ModsConfigData>
'@ | Out-File $cfg -Encoding utf8

$gameArgs = @()
if ($QuickTest) { $gameArgs += "-quicktest" }

Write-Host "Launching RimWorld (test mod list). Your normal mod list will be restored when the game closes."
try {
    if ($gameArgs.Count -gt 0) {
        $proc = Start-Process "$gameDir\RimWorldWin64.exe" -ArgumentList $gameArgs -PassThru
    } else {
        $proc = Start-Process "$gameDir\RimWorldWin64.exe" -PassThru
    }
    $proc.WaitForExit()
}
finally {
    Copy-Item $backup $cfg -Force
    Write-Host "Restored your original mod list." -ForegroundColor Green
}
