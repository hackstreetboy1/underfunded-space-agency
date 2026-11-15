#Requires -Version 5.1
<#
.SYNOPSIS
    Starts the local AI proxy on 127.0.0.1:8787 using the backend virtual environment.
#>
$ErrorActionPreference = 'Stop'

$backendDir = $PSScriptRoot
$python = Join-Path $backendDir '.venv\Scripts\python.exe'

if (-not (Test-Path $python)) {
    Write-Error "Virtual environment not found at $python. See backend/README.md to create it."
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
