#Requires -Version 5.1
<#
.SYNOPSIS
    One-click demo: starts the local proxy with the real providers (Gemini, ElevenLabs), builds and launches the game.
.PARAMETER Offline
    Start the proxy in offline practice mode (fake providers, no API quota) instead of live.
.PARAMETER Live
    Kept for compatibility; live is the default.
.PARAMETER NoBackend
    Launch only the game; do not start or wait for a proxy.
.PARAMETER Godot
    Godot .NET executable (non-console). Overrides $env:USA_GODOT and the default download location.
.PARAMETER DryRun
    Print what would run and exit without starting anything.
.PARAMETER BackendOnly
    Start the proxy, check /health, then stop it again without building or launching the game (self-test).
#>
[CmdletBinding()]
param(
    [switch]$Offline,
    [switch]$Live,
    [switch]$NoBackend,
    [string]$Godot,
    [switch]$DryRun,
    [switch]$BackendOnly
)
$ErrorActionPreference = 'Stop'
# Live providers are the demo default; -Offline opts into the fake ones.
$Live = -not $Offline

$repo = (Resolve-Path $PSScriptRoot).Path
$healthUrl = 'http://127.0.0.1:8787/health'
$defaultGodot = 'C:\Users\initi\Downloads\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe'
if (-not $Godot) { $Godot = if ($env:USA_GODOT) { $env:USA_GODOT } else { $defaultGodot } }
$logDir = Join-Path $env:TEMP 'usa-demo'
$backendScript = Join-Path $repo 'backend\run_backend.ps1'
$userData = Join-Path $env:APPDATA 'Godot\app_userdata\Underfunded Space Agency'

function Get-Health {
    try { return Invoke-RestMethod -Uri $healthUrl -TimeoutSec 2 } catch { return $null }
}

function Stop-Tree([int]$ProcessId) {
    & taskkill.exe /PID $ProcessId /T /F *> $null
}

Write-Host ''
Write-Host '=== Underfunded Space Agency demo ===' -ForegroundColor Cyan
if ($NoBackend) { $mode = 'game only (no backend)' }
elseif ($Live) { $mode = 'LIVE providers' }
else { $mode = 'offline practice (fake providers)' }
Write-Host "Mode : $mode"
Write-Host "Repo : $repo"
Write-Host "Godot: $Godot"
if ($Live -and -not $NoBackend) {
    Write-Host 'WARNING: live mode uses API quota (Gemini / ElevenLabs).' -ForegroundColor Yellow
}

if ($DryRun) {
    Write-Host ''
    Write-Host '[dry run] nothing will be started. Commands:' -ForegroundColor Yellow
    if (-not $NoBackend) {
        $fake = if ($Live) { 'false' } else { 'true' }
        Write-Host "  (reuse the proxy if $healthUrl already answers)"
        Write-Host "  child env USE_FAKE_PROVIDERS=$fake"
        Write-Host "  powershell -NoProfile -ExecutionPolicy Bypass -File `"$backendScript`"   (hidden; logs: $logDir\backend.out.log, backend.err.log)"
        Write-Host "  wait up to 30 s for $healthUrl"
    }
    if (-not $BackendOnly) {
        Write-Host "  dotnet build UnderfundedSpaceAgency.csproj -v q --nologo   (cwd $repo)"
        Write-Host '  child env USA_BACKEND_AUTOSTART=0'
        Write-Host "  `"$Godot`" --path `"$repo`"   (waits for the window to close)"
    }
    if (-not $NoBackend) { Write-Host '  on exit: stop the proxy only if this script started it (taskkill /T /F)' }
    exit 0
}

if (-not $BackendOnly -and -not (Test-Path -LiteralPath $Godot)) {
    throw "Godot not found at '$Godot'. Pass -Godot <path> or set `$env:USA_GODOT."
}

$backendProc = $null
$exitCode = 0
try {
    if (-not $NoBackend) {
        $health = Get-Health
        $wanted = if ($Live) { 'live' } else { 'fake' }
        if ($health -and $health.mode -ne $wanted) {
            # A leftover proxy in the other mode (the game's debug auto-start runs fakes): replace it, or the demo would
            # silently talk to the canned stand-in instead of Gemini.
            Write-Host "A $($health.mode) proxy is running on 127.0.0.1:8787; stopping it to start a $wanted one." -ForegroundColor Yellow
            $owners = Get-NetTCPConnection -LocalPort 8787 -State Listen -ErrorAction SilentlyContinue |
                Select-Object -ExpandProperty OwningProcess -Unique
            foreach ($owner in $owners) { Stop-Process -Id $owner -Force -ErrorAction SilentlyContinue }
            $deadline = (Get-Date).AddSeconds(10)
            while ((Get-Date) -lt $deadline -and (Get-Health)) { Start-Sleep -Milliseconds 300 }
            $health = Get-Health
            if ($health) { throw "Could not stop the $($health.mode) proxy on 127.0.0.1:8787; close it (or the game that started it) and run the demo again." }
        }
        if ($health) {
            Write-Host "Backend already running on 127.0.0.1:8787 (mode: $($health.mode)); reusing it, will not stop it." -ForegroundColor Green
        }
        else {
            if (-not (Test-Path -LiteralPath $backendScript)) { throw "Missing $backendScript" }
            New-Item -ItemType Directory -Force -Path $logDir | Out-Null
            $outLog = Join-Path $logDir 'backend.out.log'
            $errLog = Join-Path $logDir 'backend.err.log'
            $kind = if ($Live) { 'live' } else { 'fake' }
            Write-Host "Starting backend ($kind providers); logs: $logDir"
            $previous = $env:USE_FAKE_PROVIDERS
            $env:USE_FAKE_PROVIDERS = if ($Live) { 'false' } else { 'true' }
            try {
                $backendProc = Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden -PassThru `
                    -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$backendScript`"") `
                    -RedirectStandardOutput $outLog -RedirectStandardError $errLog
            }
            finally {
                if ($null -eq $previous) { Remove-Item Env:\USE_FAKE_PROVIDERS -ErrorAction SilentlyContinue } else { $env:USE_FAKE_PROVIDERS = $previous }
            }

            $deadline = (Get-Date).AddSeconds(30)
            $health = $null
            $tick = 0
            while ((Get-Date) -lt $deadline) {
                if ($backendProc.HasExited) { break }
                $health = Get-Health
                if ($health) { break }
                $tick++
                Write-Host "  waiting for /health ... ${tick}s"
                Start-Sleep -Seconds 1
            }
            if (-not $health) {
                $why = if ($backendProc.HasExited) { "the proxy exited (code $($backendProc.ExitCode))" } else { 'no answer within 30 s' }
                throw "Backend did not become healthy: $why. See $errLog (is backend\.venv set up? see backend/README.md)."
            }
            Write-Host "Backend up: backend=$($health.backend) mode=$($health.mode)" -ForegroundColor Green
        }
    }

    if (-not $BackendOnly) {
        Write-Host 'Building C# project (dotnet build, quiet) ...'
        Push-Location $repo
        try { & dotnet build UnderfundedSpaceAgency.csproj -v q --nologo; $buildCode = $LASTEXITCODE }
        finally { Pop-Location }
        if ($buildCode -ne 0) { throw "dotnet build failed (exit $buildCode). Fix the errors above, then run the demo again." }

        Write-Host ''
        Write-Host 'Launching the game. Close its window to end the demo.' -ForegroundColor Cyan
        Write-Host "Logs : $userData\logs   (usa_<date>.log = game log, godot.log = engine log)"
        Write-Host "Repro: press F12 in the game, then 'Save repro' (saved to $userData\repros)"
        $prevAuto = $env:USA_BACKEND_AUTOSTART
        $env:USA_BACKEND_AUTOSTART = '0'
        try {
            $game = Start-Process -FilePath $Godot -ArgumentList @('--path', "`"$repo`"") -PassThru -Wait
            Write-Host "Game closed (exit $($game.ExitCode))."
        }
        finally {
            if ($null -eq $prevAuto) { Remove-Item Env:\USA_BACKEND_AUTOSTART -ErrorAction SilentlyContinue } else { $env:USA_BACKEND_AUTOSTART = $prevAuto }
        }
    }
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 1
}
finally {
    if ($backendProc -and -not $backendProc.HasExited) {
        Write-Host 'Stopping the backend this script started ...'
        Stop-Tree $backendProc.Id
    }
}
exit $exitCode
