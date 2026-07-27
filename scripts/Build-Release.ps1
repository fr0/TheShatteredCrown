<#
.SYNOPSIS
    Builds The Shattered Crown and stages a clean, upload-ready copy.

.DESCRIPTION
    Compiles the dialogue DSL, builds the assembly in Release, validates
    every XML file, then copies ONLY the files the game needs into a
    staging folder. Development material (C# source, .agd dialogue
    sources, scripts, tools, docs, logs, .git) is left behind.

    Steam Workshop uploads are done from RimWorld itself (dev mode ->
    mod list -> Upload to Workshop), which publishes whatever is in the
    mod folder. Pointing that at the raw repo would ship the source tree
    and the .git directory to every subscriber; pointing it at this
    staging folder ships the mod.

.PARAMETER OutputPath
    Where to stage. Default: <repo>\dist\TheShatteredCrown

.PARAMETER InstallToMods
    Also copy the staged folder into RimWorld's Mods directory as
    "TheShatteredCrown_Release", ready for the in-game uploader.

.PARAMETER SkipBuild
    Stage the assembly as it stands; skip dialogue compile and dotnet build.

.EXAMPLE
    .\scripts\Build-Release.ps1
    .\scripts\Build-Release.ps1 -InstallToMods
#>
[CmdletBinding()]
param(
    [string]$OutputPath,
    [switch]$InstallToMods,
    [switch]$SkipBuild,
    # Set the mod version (e.g. 1.1.0) in BOTH About.xml and the csproj
    # before building, so the two can never drift apart.
    [string]$SetVersion
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputPath) { $OutputPath = Join-Path $root 'dist\TheShatteredCrown' }

$modsDir = 'X:\SteamLibrary\steamapps\common\RimWorld\Mods'
$releaseFolderName = 'TheShatteredCrown_Release'

# Everything the GAME loads. Anything not listed here does not ship.
$shipItems = @('About', '1.6', 'CE', 'Textures', 'LoadFolders.xml')
# Dev-only files that live inside shipping folders.
$excludeNames = @('WorkshopDescription.txt')

function Write-Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }
function Write-Ok($text)   { Write-Host "    $text" -ForegroundColor Green }
function Write-Warn2($text){ Write-Host "    WARNING: $text" -ForegroundColor Yellow }

Write-Host "The Shattered Crown - release build" -ForegroundColor White
Write-Host "repo:   $root"
Write-Host "output: $OutputPath"

# ---------------------------------------------------------------- version
$aboutFile = Join-Path $root 'About\About.xml'
$csprojFile = Join-Path $root 'Source\TheShatteredCrown\TheShatteredCrown.csproj'

if ($SetVersion) {
    if ($SetVersion -notmatch '^\d+\.\d+\.\d+$') {
        throw "Version must look like 1.2.3 (got '$SetVersion')."
    }
    Write-Step "Setting version to $SetVersion"
    # Text edits, not XML round-trips: preserves comments and formatting.
    $a = Get-Content $aboutFile -Raw
    if ($a -match '<modVersion[^>]*>') {
        $a = [regex]::Replace($a, '(<modVersion[^>]*>)[^<]*(</modVersion>)', "`${1}$SetVersion`${2}")
    } else {
        $a = $a -replace '(</packageId>)', "`$1`r`n  <modVersion IgnoreIfNoMatchingField=`"True`">$SetVersion</modVersion>"
    }
    Set-Content $aboutFile -Value $a -Encoding utf8 -NoNewline
    $c = Get-Content $csprojFile -Raw
    $c = [regex]::Replace($c, '<Version>[^<]*</Version>', "<Version>$SetVersion</Version>")
    $c = [regex]::Replace($c, '<AssemblyVersion>[^<]*</AssemblyVersion>', "<AssemblyVersion>$SetVersion.0</AssemblyVersion>")
    $c = [regex]::Replace($c, '<FileVersion>[^<]*</FileVersion>', "<FileVersion>$SetVersion.0</FileVersion>")
    Set-Content $csprojFile -Value $c -Encoding utf8 -NoNewline
    Write-Ok 'About.xml and csproj updated'
}

# Read back and require agreement: a mismatch means someone edited one.
[xml]$aboutXml = Get-Content $aboutFile -Raw
$modVersion = $aboutXml.ModMetaData.modVersion
if ($modVersion -is [System.Xml.XmlElement]) { $modVersion = $modVersion.InnerText }
$csprojVersion = ([regex]::Match((Get-Content $csprojFile -Raw), '<Version>([^<]*)</Version>')).Groups[1].Value
if (-not $modVersion) {
    Write-Warn2 'About.xml has no <modVersion>. Set one with -SetVersion 1.0.0'
} elseif ($modVersion -ne $csprojVersion) {
    throw "Version mismatch: About.xml says '$modVersion', csproj says '$csprojVersion'. Use -SetVersion to fix both."
} else {
    Write-Host "version: $modVersion" -ForegroundColor Green
}

# ---------------------------------------------------------------- build
if (-not $SkipBuild) {
    Write-Step 'Compiling dialogue (.agd -> Defs/Dialogues)'
    Push-Location $root
    try {
        $dialogue = & py 'scripts\compile_dialogue.py' 2>&1
        if ($LASTEXITCODE -ne 0) {
            $dialogue | ForEach-Object { Write-Host $_ }
            throw 'Dialogue compile failed.'
        }
        $sceneCount = ([regex]::Matches(($dialogue -join "`n"), '->')).Count
        Write-Ok "$sceneCount dialogue files compiled"
    }
    finally { Pop-Location }

    Write-Step 'Building assembly (Release)'
    $proj = Join-Path $root 'Source\TheShatteredCrown\TheShatteredCrown.csproj'
    $build = & dotnet build $proj -c Release -v quiet 2>&1
    if ($LASTEXITCODE -ne 0) {
        $build | ForEach-Object { Write-Host $_ }
        throw 'Assembly build failed.'
    }
    Write-Ok 'TheShatteredCrown.dll built'
}

# ---------------------------------------------------------------- validate
Write-Step 'Validating XML'
$xmlFiles = Get-ChildItem -Path (Join-Path $root '1.6'), (Join-Path $root 'CE'), (Join-Path $root 'About') `
    -Filter *.xml -Recurse -ErrorAction SilentlyContinue
$xmlFiles += Get-Item (Join-Path $root 'LoadFolders.xml')
$badXml = 0
foreach ($f in $xmlFiles) {
    try { [xml](Get-Content $f.FullName -Raw) | Out-Null }
    catch {
        Write-Warn2 "$($f.FullName): $($_.Exception.Message)"
        $badXml++
    }
}
if ($badXml -gt 0) { throw "$badXml XML file(s) failed to parse. Fix before packaging." }
Write-Ok "$($xmlFiles.Count) XML files parsed"

Write-Step 'Sanity checks'
$aboutPath = Join-Path $root 'About\About.xml'
[xml]$about = Get-Content $aboutPath -Raw
$modName = $about.ModMetaData.name
$packageId = $about.ModMetaData.packageId
Write-Ok "name: $modName"
Write-Ok "packageId: $packageId"

# Every <supportedVersions> entry needs a matching version folder.
foreach ($v in $about.ModMetaData.supportedVersions.li) {
    if (Test-Path (Join-Path $root $v)) { Write-Ok "version folder $v present" }
    else { Write-Warn2 "About.xml lists $v but no '$v' folder exists" }
}

$dll = Join-Path $root '1.6\Assemblies\TheShatteredCrown.dll'
if (-not (Test-Path $dll)) { throw "Assembly missing: $dll" }
Write-Ok ("assembly: {0:N0} KB" -f ((Get-Item $dll).Length / 1KB))

if (-not (Test-Path (Join-Path $root 'About\Preview.png'))) {
    Write-Warn2 'About\Preview.png is missing. Steam shows a blank tile without it (recommended 640x360 or larger).'
}

# ---------------------------------------------------------------- stage
Write-Step 'Staging clean copy'

# The Workshop item id lives in About\PublishedFileId.txt and is written by
# RimWorld's uploader on FIRST publish. If it is lost, the next upload
# creates a SECOND Workshop item instead of updating yours - so carry it
# across rebuilds from wherever it survives.
$idFileRel = 'About\PublishedFileId.txt'
$existingId = $null
foreach ($candidate in @((Join-Path $OutputPath $idFileRel), (Join-Path $root $idFileRel), (Join-Path $modsDir "$releaseFolderName\$idFileRel"))) {
    if ((-not $existingId) -and (Test-Path $candidate)) {
        $existingId = (Get-Content $candidate -Raw).Trim()
        Write-Ok "carrying Workshop id $existingId (from $candidate)"
    }
}

if (Test-Path $OutputPath) { Remove-Item $OutputPath -Recurse -Force }
New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null

foreach ($item in $shipItems) {
    $src = Join-Path $root $item
    if (-not (Test-Path $src)) { Write-Warn2 "skipping missing $item"; continue }
    Copy-Item $src -Destination $OutputPath -Recurse -Force
}

# Drop dev-only files that live inside shipped folders.
foreach ($name in $excludeNames) {
    Get-ChildItem -Path $OutputPath -Filter $name -Recurse -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item $_.FullName -Force }
}
# Never ship build leftovers.
Get-ChildItem -Path $OutputPath -Include *.pdb, *.mdb, *.bak -Recurse -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-Item $_.FullName -Force }

if ($existingId) {
    Set-Content -Path (Join-Path $OutputPath $idFileRel) -Value $existingId -Encoding ascii -NoNewline
}

$staged = Get-ChildItem $OutputPath -Recurse -File
$sizeMb = ($staged | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Ok ("{0} files, {1:N1} MB" -f $staged.Count, $sizeMb)

# Loud failure if development material leaked into the package. Anchored to
# the package ROOT on purpose: 1.6\Defs\Dialogues holds the COMPILED
# dialogue defs and must ship, while a top-level Dialogues\ folder is the
# .agd source and must not.
$devRoots = @('Source', 'Dialogues', 'scripts', 'tools', 'editors', 'docs', '.git')
$leaks = $staged | Where-Object {
    $rel = $_.FullName.Substring($OutputPath.Length).TrimStart('\')
    $top = ($rel -split '\\')[0]
    ($devRoots -contains $top) -or ($_.Extension -in '.cs', '.agd', '.csproj', '.sln')
}
if ($leaks) {
    $leaks | Select-Object -First 10 | ForEach-Object { Write-Warn2 "leaked: $($_.FullName.Substring($OutputPath.Length))" }
    throw "$($leaks.Count) development file(s) leaked into the package."
}
Write-Ok 'no source or dev files in package'

# ---------------------------------------------------------------- install
if ($InstallToMods) {
    Write-Step 'Installing to RimWorld Mods'
    if (-not (Test-Path $modsDir)) { throw "Mods folder not found: $modsDir" }
    $dest = Join-Path $modsDir $releaseFolderName
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    Copy-Item $OutputPath -Destination $dest -Recurse -Force
    Write-Ok "installed to $dest"
}

Write-Host "`nDone." -ForegroundColor Green
Write-Host "Package: $OutputPath"
if ($InstallToMods) {
    Write-Host @"

To publish:
  1. Launch RimWorld with development mode ON.
  2. Open the mod list. Enable "$modName" ($releaseFolderName).
  3. Click "Upload to Steam Workshop".
  4. Paste About\WorkshopDescription.txt into the Steam page description.

The first upload writes About\PublishedFileId.txt into the RELEASE folder.
Re-run this script with -InstallToMods afterwards and that id is carried
forward automatically, so later uploads UPDATE the item instead of
creating a duplicate.
"@
} else {
    Write-Host "Re-run with -InstallToMods to place it where RimWorld's uploader can see it."
}
