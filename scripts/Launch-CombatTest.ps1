# Launches straight into a COMBAT TEST: quicktest map, colonists pre-equipped
# with medieval gear, combat skills 12, three class levels each (Warden/
# Ranger/Rogue rotation), turn-based mode armed, and an immediate raid.
# Uses the minimal test mod list (backed up / restored like Launch-TestMode).
#
# Usage:  powershell -ExecutionPolicy Bypass -File scripts\Launch-CombatTest.ps1

$gameDir = "X:\SteamLibrary\steamapps\common\RimWorld"
$cfgDir  = "$env:USERPROFILE\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios"
$cfg     = Join-Path $cfgDir "Config\ModsConfig.xml"
$backup  = "$cfg.userbackup"

if (Get-Process RimWorldWin64 -ErrorAction SilentlyContinue) {
    Write-Host "RimWorld is already running - close it first." -ForegroundColor Red
    exit 1
}

# GUARD: never let a failed run destroy the real mod list.
#
# The backup is overwritten on every launch. If a previous run died before
# restoring, ModsConfig.xml still holds the small TEST list - and backing
# THAT up would overwrite the only copy of the real one. So: refuse to
# clobber an existing backup with something that looks like a test list.
$existingCount = (Select-String -Path $cfg -Pattern "<li>" -AllMatches).Matches.Count
if ((Test-Path $backup) -and $existingCount -lt 40) {
    $backupCount = (Select-String -Path $backup -Pattern "<li>" -AllMatches).Matches.Count
    Write-Host "Current mod list has only $existingCount entries - looks like a leftover TEST list." -ForegroundColor Yellow
    Write-Host "Not overwriting the backup ($backupCount entries). Restoring from it instead." -ForegroundColor Yellow
    Copy-Item $backup $cfg -Force
    $existingCount = $backupCount
}
$originalCount = $existingCount

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
    <li>fr0.theshatteredcrown</li>
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

Write-Host "Launching COMBAT TEST (quicktest + auto-setup + raid). Mod list restores on exit."
try {
    $proc = Start-Process "$gameDir\RimWorldWin64.exe" -ArgumentList "-quicktest", "-tsccombattest" -PassThru
    $proc.WaitForExit()
}
finally {
    Copy-Item $backup $cfg -Force
    $entryCount = (Select-String -Path $cfg -Pattern "<li>" -AllMatches).Matches.Count
    if ($entryCount -lt $originalCount) {
        Write-Host "WARNING: restored $entryCount entries but started with $originalCount - check $backup!" -ForegroundColor Red
    } else {
        Write-Host "Restored your original mod list ($entryCount entries)." -ForegroundColor Green
    }
}
