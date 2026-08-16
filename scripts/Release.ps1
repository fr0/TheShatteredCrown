# One-command release session for BOTH mods.
#
#   powershell -ExecutionPolicy Bypass -File scripts\Release.ps1 -TscVersion 1.0.6 -TbcVersion 1.1.0
#
# Does, in order:
#   1. Stamps the TSC version and runs the full release build
#      (Build-Release.ps1 -SetVersion -PrepareUpload: dialogue compile,
#      Release assembly, XML validation, clean staging, install to Mods,
#      dev junction unhooked so the uploader sees exactly one copy).
#   2. Stamps the standalone version and builds dist\TurnBasedCombat
#      (build_standalone.py --set-version).
#   3. Installs the standalone build into Mods\TurnBasedCombat.
#   4. Swaps in a minimal mod list with BOTH mods active (real list backed
#      up, same guards as Launch-TestMode.ps1) and launches the game:
#      enable Development mode and upload each mod from the Mods screen.
#   5. On game close: carries any new PublishedFileId.txt back into the
#      repo (losing it would make the next upload a SECOND Workshop item),
#      restores the dev junction and your real mod list.

param(
    [Parameter(Mandatory)][string]$TscVersion,
    [Parameter(Mandatory)][string]$TbcVersion
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$gameDir = 'X:\SteamLibrary\steamapps\common\RimWorld'
$modsDir = Join-Path $gameDir 'Mods'
$cfgDir  = "$env:USERPROFILE\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios"
$cfg     = Join-Path $cfgDir 'Config\ModsConfig.xml'
$backup  = "$cfg.userbackup"

foreach ($v in @($TscVersion, $TbcVersion)) {
    if ($v -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got '$v')." }
}
if (Get-Process RimWorldWin64 -ErrorAction SilentlyContinue) {
    Write-Host 'RimWorld is already running - close it first.' -ForegroundColor Red
    exit 1
}

# ------------------------------------------------------------ build both mods
Write-Host "`n=== The Shattered Crown $TscVersion ===" -ForegroundColor White
& (Join-Path $PSScriptRoot 'Build-Release.ps1') -SetVersion $TscVersion -PrepareUpload

Write-Host "`n=== Turn-Based Combat $TbcVersion ===" -ForegroundColor White
Push-Location $root
try {
    & py 'scripts\build_standalone.py' --set-version $TbcVersion
    if ($LASTEXITCODE -ne 0) { throw 'Standalone build failed.' }
}
finally { Pop-Location }

$distTbc = Join-Path $root 'dist\TurnBasedCombat'
$installedTbc = Join-Path $modsDir 'TurnBasedCombat'
if (Test-Path $installedTbc) { Remove-Item $installedTbc -Recurse -Force }
Copy-Item $distTbc -Destination $installedTbc -Recurse -Force
Write-Host "Installed standalone build to Mods\TurnBasedCombat" -ForegroundColor Green

# ------------------------------------------------------- upload mod list swap
# Same clobber guard as Launch-TestMode.ps1: never back up what is already
# a small leftover test list over the only copy of the real one.
$existingCount = (Select-String -Path $cfg -Pattern '<li>' -AllMatches).Matches.Count
if ((Test-Path $backup) -and $existingCount -lt 40) {
    $backupCount = (Select-String -Path $backup -Pattern '<li>' -AllMatches).Matches.Count
    Write-Host "Current mod list has only $existingCount entries - restoring the $backupCount-entry backup first." -ForegroundColor Yellow
    Copy-Item $backup $cfg -Force
    $existingCount = $backupCount
}
$originalCount = $existingCount
Copy-Item $cfg $backup -Force
Write-Host "Backed up your mod list to $backup"

# Both mods active: verify in one session, upload both from the Mods screen.
# (The standalone stands down while TSC is loaded - that is by design.)
$modsConfig = @"
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
    <li>fr0.turnbasedcombat</li>
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
"@
$modsConfig | Out-File $cfg -Encoding utf8

Write-Host @"

READY TO UPLOAD - in the game:
  1. Options -> check "Development mode".
  2. Mods screen: select each mod, click "Upload to Steam Workshop"
     (first time) / "Update on Steam Workshop" (afterwards):
       - The Shattered Crown  $TscVersion
       - Turn-Based Combat    $TbcVersion
  3. Wait for "Upload succeeded" on each before closing the game.
Your real mod list and the dev junction are restored when the game closes.
"@ -ForegroundColor Cyan

# ------------------------------------------------------------------- launch
try {
    $proc = Start-Process (Join-Path $gameDir 'RimWorldWin64.exe') -PassThru
    $proc.WaitForExit()
    # The dying process can flush ModsConfig.xml after exit: wait it out.
    while (Get-Process RimWorldWin64 -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 2 }
    Start-Sleep -Seconds 6
}
finally {
    # Carry Workshop ids back BEFORE -RestoreDev deletes the release copy:
    # losing PublishedFileId.txt makes the next upload a NEW Workshop item.
    $tscId = Join-Path $modsDir 'TheShatteredCrown_Release\About\PublishedFileId.txt'
    if (Test-Path $tscId) {
        Copy-Item $tscId (Join-Path $root 'About\PublishedFileId.txt') -Force
        Write-Host 'TSC PublishedFileId carried back to About\.' -ForegroundColor Green
    }
    $tbcId = Join-Path $installedTbc 'About\PublishedFileId.txt'
    if (Test-Path $tbcId) {
        Copy-Item $tbcId (Join-Path $root 'docs\standalone\PublishedFileId.txt') -Force
        Write-Host 'TBC PublishedFileId carried back to docs\standalone\.' -ForegroundColor Green
    }

    Copy-Item $backup $cfg -Force
    $entryCount = (Select-String -Path $cfg -Pattern '<li>' -AllMatches).Matches.Count
    if ($entryCount -lt $originalCount) {
        Write-Host "WARNING: restored $entryCount entries but started with $originalCount - check $backup!" -ForegroundColor Red
    } else {
        Write-Host "Restored your original mod list ($entryCount entries)." -ForegroundColor Green
    }

    & (Join-Path $PSScriptRoot 'Build-Release.ps1') -RestoreDev
}

Write-Host @"

Done. Remember, per mod, after a FIRST upload:
  - set visibility:   .\scripts\Build-Release.ps1 -OpenItemPage
  - page description: .\scripts\Build-Release.ps1 -CopyDescription (TSC)
                      docs\standalone\WorkshopDescription.txt (TBC, paste by hand)
"@
