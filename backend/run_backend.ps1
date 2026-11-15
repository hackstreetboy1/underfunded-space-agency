#Requires -Version 5.1
<#
.SYNOPSIS
    Starts the local AI proxy on 127.0.0.1:8787 using the backend virtual environment.
#>
$ErrorActionPreference = 'Stop'

$backendDir = $PSScriptRoot
# backend\.venv, or the short per-user folder Start-Demo.ps1 uses when the repo path is too long for the packages.
$candidates = @(
    (Join-Path $backendDir '.venv\Scripts\python.exe'),
    (Join-Path $env:LOCALAPPDATA 'USA-venv\Scripts\python.exe')
)
# The first one that actually runs: a backend\.venv copied from another PC (it is committed to the repo) points at that
# PC's Python install and fails to start here.
$python = $null
$ErrorActionPreference = 'Continue'   # a broken venv writes to stderr; only the exit code matters here
foreach ($candidate in $candidates) {
    if (-not (Test-Path -LiteralPath $candidate)) { continue }
    & $candidate -c 'import fastapi, uvicorn' *> $null
    if ($LASTEXITCODE -eq 0) { $python = $candidate; break }
}
$ErrorActionPreference = 'Stop'

if (-not $python) {
    Write-Error "Backend Python environment not found (looked for $($candidates -join ' and ')). Run Start-Demo.bat once to create it, or see backend/README.md."
    exit 1
}

Push-Location $backendDir
try {
    # uvicorn logs to stderr; under 'Stop' Windows PowerShell turns the first redirected stderr line into a
    # terminating error (e.g. when the game's launcher or a log redirect captures the output).
    $ErrorActionPreference = 'Continue'
    & $python -m uvicorn app.main:create_app --factory --host 127.0.0.1 --port 8787 --no-access-log
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
