# Builds (if needed) and launches the WPF dialogue tester.
# Usage:  powershell -ExecutionPolicy Bypass -File scripts\Run-DialogueTester.ps1

$repo = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $repo "tools\DialogueTester\DialogueTester.csproj"
$exe  = Join-Path $repo "tools\DialogueTester\bin\Release\net472\DialogueTester.exe"

dotnet build $proj -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed." -ForegroundColor Red
    exit 1
}
Start-Process $exe
