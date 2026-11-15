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
$python = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

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
