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
    [string]$SetVersion,
    # Build, stage, install to Mods, AND unhook the dev junction so RimWorld
    # sees exactly one copy of this packageId while you upload.
    [switch]$PrepareUpload,
    # Undo -PrepareUpload: remove the release copy, restore the junction.
    [switch]$RestoreDev,
    # Open the published Workshop item in a browser (to set visibility etc).
    [switch]$OpenItemPage,
    # Put About\WorkshopDescription.txt on the clipboard, ready to paste into
    # the Steam item page's description box.
    [switch]$CopyDescription
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputPath) { $OutputPath = Join-Path $root 'dist\TheShatteredCrown' }

$modsDir = 'X:\SteamLibrary\steamapps\common\RimWorld\Mods'
$releaseFolderName = 'TheShatteredCrown_Release'
$devLinkName = 'TheShatteredCrown'

function Get-DevJunction {
    $p = Join-Path $modsDir $devLinkName
    if (-not (Test-Path $p)) { return $null }
    $item = Get-Item $p -Force
    if ($item.LinkType -ne 'Junction' -and $item.LinkType -ne 'SymbolicLink') { return $null }
    return $item
}

function Remove-DevJunction {
    <#
        Deletes the LINK ONLY. Never Remove-Item -Recurse on a junction:
        that can follow the reparse point and delete the repository it
        points at. Directory.Delete(path,$false) removes the reparse point
        and leaves the target untouched.
    #>
    $item = Get-DevJunction
    if (-not $item) { return $null }
    $target = $item.Target
    if ($target -is [array]) { $target = $target[0] }
    [System.IO.Directory]::Delete($item.FullName, $false)
    return $target
}

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

# ---------------------------------------------------------------- description
if ($CopyDescription) {
    $descFile = Join-Path $root 'About\WorkshopDescription.txt'
    if (-not (Test-Path $descFile)) { throw "Not found: $descFile" }
    $text = Get-Content $descFile -Raw

    # Image tokens -> real URLs. Steam only renders [img] from a URL it can
    # reach; the reliable source is images already uploaded to this very
    # Workshop item, whose addresses are saved in docs\workshop\image-urls.txt
    # (one "TOKEN=url" per line) once you have them.
    $urlFile = Join-Path $root 'docs\workshop\image-urls.txt'
    $map = @{}
    if (Test-Path $urlFile) {
        foreach ($line in Get-Content $urlFile) {
            if ($line -match '^\s*([A-Z_]+)\s*=\s*(\S+)\s*$') { $map[$Matches[1]] = $Matches[2] }
        }
    }
    foreach ($k in $map.Keys) { $text = $text -replace ('\{\{' + $k + '\}\}'), $map[$k] }

    $missing = [regex]::Matches($text, '\{\{([A-Z_]+)\}\}') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
    if ($missing) {
        Write-Warn2 "Unresolved image tokens: $($missing -join ', ')"
        Write-Host @"
    To fill them in:
      1. Upload the images in docs\workshop\ to the item page (Add/Edit Images).
      2. On the item page, right-click each image -> Copy image address.
      3. Put them in docs\workshop\image-urls.txt, one per line:
             IMG_BANNER=https://steamuserimages-a.akamaihd.net/ugc/...
             IMG_DIALOGUE=https://...
             IMG_ACTIONPOINTS=https://...
      4. Re-run this command. Until then the [img] tags stay as tokens and
         will show as literal text on the page - do not paste yet.
"@ -ForegroundColor Yellow
    }

    Set-Clipboard -Value $text
    Write-Host "`nWorkshop description copied to clipboard ($($text.Length) chars)." -ForegroundColor Green
    Write-Host @"

RimWorld only sends a description when it CREATES the item, and it sends the
short <description> from About.xml - not this file. So the full page text is
always pasted by hand:

  1. .\scripts\Build-Release.ps1 -OpenItemPage
  2. On the item page: Edit -> Description
  3. Select all, paste, Save.

Steam renders the BBCode ([h1], [b], [list], [url]) in this file.
Repeat this whenever you change WorkshopDescription.txt - uploading a new
build will never update the page text.
"@
    return
}

# ---------------------------------------------------------------- item page
if ($OpenItemPage) {
    $idPaths = @(
        (Join-Path $root 'About\PublishedFileId.txt'),
        (Join-Path $modsDir "$releaseFolderName\About\PublishedFileId.txt"),
        (Join-Path $OutputPath 'About\PublishedFileId.txt')
    )
    $id = $null
    foreach ($p in $idPaths) {
        if ((-not $id) -and (Test-Path $p)) { $id = (Get-Content $p -Raw).Trim() }
    }
    if (-not $id) {
        throw "No PublishedFileId.txt found yet. Upload the mod once first, then re-run this."
    }
    $url = "https://steamcommunity.com/sharedfiles/filedetails/?id=$id"
    Write-Host "`nWorkshop item: $url" -ForegroundColor Green
    Write-Host "Use Edit -> Change Visibility there (Private / Unlisted / Public)."
    Start-Process $url
    return
}

# ---------------------------------------------------------------- restore
if ($RestoreDev) {
    Write-Step 'Restoring the development junction'
    $rel = Join-Path $modsDir $releaseFolderName
    if (Test-Path $rel) {
        Remove-Item $rel -Recurse -Force
        Write-Ok "removed $releaseFolderName"
    }
    if (Get-DevJunction) {
        Write-Ok 'dev junction already in place'
    } else {
        $link = Join-Path $modsDir $devLinkName
        if (Test-Path $link) { throw "$link exists and is not a junction. Move it aside first." }
        New-Item -ItemType Junction -Path $link -Target $root | Out-Null
        Write-Ok "junction restored: $link -> $root"
    }
    Write-Host "`nBack to development." -ForegroundColor Green
    return
}

if ($PrepareUpload) { $InstallToMods = $true }

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

    # The dev junction and the release copy carry the SAME packageId. With
    # both present RimWorld sees a duplicate and the upload button can point
    # at the wrong one - which would publish the whole source tree.
    $devJunction = Get-DevJunction
    if ($devJunction) {
        if ($PrepareUpload) {
            $target = Remove-DevJunction
            Set-Content -Path (Join-Path $root '.upload-junction-target.txt') -Value $target -Encoding ascii -NoNewline
            Write-Ok "dev junction unhooked (target remembered: $target)"
        } else {
            Write-Warn2 "The dev junction '$devLinkName' is still in Mods and shares this packageId."
            Write-Warn2 "Use -PrepareUpload to unhook it automatically before publishing."
        }
    }
}

Write-Host "`nDone." -ForegroundColor Green
Write-Host "Package: $OutputPath"
if ($PrepareUpload) {
    Write-Host @"

READY TO PUBLISH
  1. Start RimWorld (Steam must be running and signed in).
  2. Options -> check "Development mode".
  3. Mods list: select "$modName". It is the only copy loaded now.
  4. Click the workshop button at the bottom of its info panel:
       "Upload to Steam Workshop"  (first time)
       "Update on Steam Workshop"  (afterwards)
  5. Wait for "Upload succeeded". Do not close the game before it appears.

FIRST THING AFTERWARDS - SET VISIBILITY
  RimWorld does not set visibility when it uploads (it only sends title,
  description, preview, tags and files), so the item lands on whatever
  Steam defaults to. Do not rely on that: set it yourself straight away.

     .\scripts\Build-Release.ps1 -OpenItemPage

  opens the item on Steam. There, use Edit -> Change Visibility:
       Private   only you can see it            <- start here
       Unlisted  anyone with the link           <- good for testers
       Public    listed and searchable          <- when you are ready

  You can move between these freely at any time, as often as you like, and
  updates can be uploaded while it is Private. Note that Steam will not let
  an item go Public until you have accepted the Workshop Legal Agreement
  (a checkbox on the item page the first time).

ALSO AFTER THE FIRST UPLOAD
  - RimWorld writes About\PublishedFileId.txt into the release folder. Copy
    it back into the repo so future builds keep updating the SAME item:
       copy "$modsDir\$releaseFolderName\About\PublishedFileId.txt" "$root\About\"
  - Add the gallery images from docs\workshop\ on the item page.
  - The description is only sent on the FIRST upload; later edits are made
    on the Steam page directly.

WHEN YOU ARE DONE
     .\scripts\Build-Release.ps1 -RestoreDev
  removes the release copy and restores the dev junction.
"@
} elseif ($InstallToMods) {
    Write-Host "Installed. Use -PrepareUpload instead to also unhook the dev junction before publishing."
} else {
    Write-Host "Re-run with -PrepareUpload to install it and get publishing steps."
}
