# Compiles Dialogues/*.agd into DialogueDef XML (1.6/Defs/Dialogues/).
# PowerShell front-end for scripts/compile_dialogue.py.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\Compile-Dialogue.ps1
#   ... -Files guild_envoy.agd            # compile specific file(s)
#   ... -Watch                            # recompile automatically on save
#
# Requires Python 3 (the 'py' launcher, python3, or python on PATH).

param(
    [string[]]$Files,
    [switch]$Watch
)

$repo = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $PSScriptRoot "compile_dialogue.py"
$dialogueDir = Join-Path $repo "Dialogues"

function Find-Python {
    foreach ($candidate in @("py", "python3", "python")) {
        $cmd = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($null -eq $cmd) { continue }
        # The Microsoft Store stub 'python.exe' fails with a nag message; a real
        # interpreter answers --version with exit code 0.
        & $candidate --version *> $null
        if ($LASTEXITCODE -eq 0) { return $candidate }
    }
    return $null
}

$python = Find-Python
if ($null -eq $python) {
    Write-Host "No working Python found (tried: py, python3, python). Install Python 3 or enable the 'py' launcher." -ForegroundColor Red
    exit 1
}

function Invoke-Compile {
    param([string[]]$CompileFiles)
    $compileArgs = @($compiler)
    if ($CompileFiles) { $compileArgs += $CompileFiles }
    & $python @compileArgs
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Dialogue compile OK." -ForegroundColor Green
    } else {
        Write-Host "Dialogue compile FAILED (exit $LASTEXITCODE)." -ForegroundColor Red
    }
    return $LASTEXITCODE
}

if (-not $Watch) {
    exit (Invoke-Compile -CompileFiles $Files)
}

# ---- watch mode: recompile whenever a .agd changes ----
Write-Host "Watching $dialogueDir for changes (Ctrl+C to stop)..." -ForegroundColor Cyan
Invoke-Compile -CompileFiles $Files | Out-Null

$watcher = New-Object System.IO.FileSystemWatcher $dialogueDir, "*.agd"
$watcher.IncludeSubdirectories = $false
$watcher.EnableRaisingEvents = $true

try {
    $lastRun = Get-Date
    while ($true) {
        $result = $watcher.WaitForChanged([System.IO.WatcherChangeTypes]"Changed, Created, Renamed", 2000)
        if ($result.TimedOut) { continue }
        # Debounce: editors fire several events per save.
        if (((Get-Date) - $lastRun).TotalMilliseconds -lt 300) { continue }
        Start-Sleep -Milliseconds 150
        $lastRun = Get-Date
        Write-Host "`n[$(Get-Date -Format HH:mm:ss)] $($result.Name) changed - recompiling..." -ForegroundColor Cyan
        Invoke-Compile -CompileFiles $Files | Out-Null
    }
}
finally {
    $watcher.Dispose()
}
