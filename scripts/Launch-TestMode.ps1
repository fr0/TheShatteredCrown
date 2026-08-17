# Launches RimWorld with ONLY Core + DLCs + The Shattered Crown enabled.
# Backs up your real mod list first, then restores it automatically when the
# game exits (or on Ctrl+C). Your normal 300-mod setup is never lost.
#
# Usage:  powershell -ExecutionPolicy Bypass -File scripts\Launch-TestMode.ps1
# Optional: -QuickTest  (skips the main menu, generates a small dev test map)
#           -WithMO     (add Medieval Overhaul and its required frameworks,
#                        loaded BEFORE this mod: overhauls first, content
#                        after)
#           -WithCE     (add Combat Extended, loaded AFTER this mod - CE
#                        patches other people's content and only sees defs
#                        already loaded, so the reverse order silently
#                        leaves our weapons without CE stats)
#           Both switches are independent on purpose: TSC+CE alone is the
#           cleanest way to tell a CE problem from an MO one.
#           -AutoClose  (unattended load checks: kill the game after
#                        -AutoCloseSeconds [default 90] - long enough to load
#                        defs and write errors to Player.log - then restore.
#                        Kill/restore ordering hardened: poll until the process
#                        is really gone, then grace time for any config flush,
#                        THEN restore, and verify the restored entry count.)

param([switch]$QuickTest, [switch]$AutoClose, [int]$AutoCloseSeconds = 90,
      [switch]$WithMO, [switch]$WithCE,
      # Add Vehicle Framework + Vanilla Vehicles Expanded (loaded BEFORE
      # this mod: framework and content first, our patches after).
      [switch]$WithVehicles,
      # Any additional packageIds to activate (comma-separated), loaded
      # before our mods. For one-off compat tests:
      #   -ExtraMods "el.invmech"
      [string]$ExtraMods,
      # Test the STANDALONE Turn-Based Combat build (dist\TurnBasedCombat)
      # WITHOUT The Shattered Crown: installs the dist copy into Mods and
      # activates it in place of TSC (no RFoW either - minimal surface).
      [switch]$Standalone)

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

# The list is composed rather than fixed so the optional overhauls can be
# slotted in at the right POSITION, which is the whole point of testing
# with them: order is the thing most likely to be wrong.
$mods = [System.Collections.Generic.List[string]]::new()
$mods.AddRange([string[]]@(
    "brrainz.harmony",
    "ludeon.rimworld",
    "ludeon.rimworld.royalty",
    "ludeon.rimworld.ideology",
    "ludeon.rimworld.biotech",
    "ludeon.rimworld.anomaly",
    "ludeon.rimworld.odyssey"
))

if ($WithMO) {
    # Medieval Overhaul's own required frameworks, then MO, all ahead of us.
    $mods.AddRange([string[]]@(
        "oskarpotocki.vanillafactionsexpanded.core",
        "syrchalis.processor.framework",
        "dankpyon.medieval.overhaul"
    ))
}

if ($WithVehicles) {
    # VVE needs Vanilla Expanded Framework (VEF.* types); -WithMO adds the
    # same framework, so only add it once.
    if (-not $mods.Contains("oskarpotocki.vanillafactionsexpanded.core")) {
        $mods.Add("oskarpotocki.vanillafactionsexpanded.core")
    }
    $mods.AddRange([string[]]@(
        "smashphil.vehicleframework",
        "oskarpotocki.vanillavehiclesexpanded",
        # Deployable Turret: backpack apparel that deploys a Building_TurretGun
        # (covered by the building-turret hold; good comp/turret test surface).
        "pal5k.deployableturret"
    ))
}

if ($ExtraMods) {
    foreach ($id in ($ExtraMods -split ',')) {
        $trimmed = $id.Trim().ToLowerInvariant()
        if ($trimmed -and -not $mods.Contains($trimmed)) { $mods.Add($trimmed) }
    }
}

if ($Standalone) {
    # Refresh the installed copy from dist so the test always runs the
    # latest build, then activate it INSTEAD of TSC.
    $distMod = Join-Path (Split-Path -Parent $PSScriptRoot) "dist\TurnBasedCombat"
    if (-not (Test-Path "$distMod\1.6\Assemblies\TurnBasedCombat.dll")) {
        Write-Host "No standalone build found. Run: py scripts\build_standalone.py" -ForegroundColor Red
        exit 1
    }
    $installed = Join-Path $gameDir "Mods\TurnBasedCombat"
    if (Test-Path $installed) { Remove-Item $installed -Recurse -Force }
    Copy-Item $distMod -Destination $installed -Recurse -Force
    Write-Host "Installed standalone build to Mods\TurnBasedCombat" -ForegroundColor Cyan
    $mods.Add("fr0.turnbasedcombat")
} else {
    $mods.Add("mlie.nwnrealfogofwar")
    $mods.Add("fr0.theshatteredcrown")
}

if ($WithCE) {
    # AFTER us, always. See About.xml's loadBefore rule for why.
    # LOWERCASE, like every id RimWorld writes itself: the loader matches
    # case-insensitively but the mod-list UI does not, so mixed case loads
    # the mod and then draws it greyed out with an "Enable" button.
    $mods.Add("ceteam.combatextended")
}

$activeMods = ($mods | ForEach-Object { "    <li>$_</li>" }) -join "`n"

$modsConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<ModsConfigData>
  <version>1.6.4871 rev590</version>
  <activeMods>
$activeMods
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

$extras = @()
if ($WithMO) { $extras += "Medieval Overhaul" }
if ($WithCE) { $extras += "Combat Extended" }
if ($WithVehicles) { $extras += "Vehicle Framework + Vanilla Vehicles Expanded + Deployable Turret" }
if ($extras.Count -gt 0) {
    Write-Host ("Test list includes: " + ($extras -join ", ")) -ForegroundColor Cyan
}

$gameArgs = @()
if ($QuickTest) { $gameArgs += "-quicktest" }

Write-Host "Launching RimWorld (test mod list). Your normal mod list will be restored when the game closes."
try {
    if ($gameArgs.Count -gt 0) {
        $proc = Start-Process "$gameDir\RimWorldWin64.exe" -ArgumentList $gameArgs -PassThru
    } else {
        $proc = Start-Process "$gameDir\RimWorldWin64.exe" -PassThru
    }
    if ($AutoClose) {
        Write-Host "Auto-close in $AutoCloseSeconds seconds (unattended load check)."
        $deadline = (Get-Date).AddSeconds($AutoCloseSeconds)
        while ((Get-Date) -lt $deadline -and -not $proc.HasExited) {
            Start-Sleep -Seconds 5
        }
        if (-not $proc.HasExited) {
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        }
        # The dying process can flush ModsConfig.xml AFTER a too-early
        # restore: wait until it is truly gone, then a grace period.
        while (Get-Process RimWorldWin64 -ErrorAction SilentlyContinue) {
            Start-Sleep -Seconds 2
        }
        Start-Sleep -Seconds 6
    } else {
        $proc.WaitForExit()
    }
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
