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
    Godot 4.7 .NET executable (non-console). Overrides $env:USA_GODOT; without either, the launcher searches PATH,
    the folder next to this one, Downloads, Desktop, Documents, C:\Godot and Program Files.
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
$godotDownload = 'https://godotengine.org/download/archive/4.7.2-stable/'
$otherGodots = @()

function Find-Godot {
    # The project needs Godot 4.7 with .NET (the "mono" build; the standard build cannot run C#). Prefer 4.7.2 exactly,
    # then the newest 4.7.x. Other versions are remembered only to name them in the error message.
    $found = @()
    $onPath = Get-Command 'Godot*mono*win64.exe' -CommandType Application -ErrorAction SilentlyContinue
    if ($onPath) { $found += $onPath | ForEach-Object { Get-Item -LiteralPath $_.Source } }
    $roots = @(
        (Split-Path $repo -Parent),
        (Join-Path $env:USERPROFILE 'Downloads'),
        [Environment]::GetFolderPath('Desktop'),
        [Environment]::GetFolderPath('MyDocuments'),
        'C:\Godot',
        (Join-Path $env:ProgramFiles 'Godot'),
        (Join-Path $env:LOCALAPPDATA 'Programs'),
        (Join-Path $env:USERPROFILE 'scoop\apps')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique
    foreach ($root in $roots) {
        $found += Get-ChildItem -LiteralPath $root -Filter 'Godot_v4*mono_win64.exe' -File -Recurse -Depth 3 -ErrorAction SilentlyContinue
    }
    $found = $found | Sort-Object FullName -Unique
    $script:otherGodots = @($found | Where-Object { $_.Name -notlike 'Godot_v4.7*' } | ForEach-Object { $_.FullName })
    $exact = $found | Where-Object { $_.Name -like 'Godot_v4.7.2-*' } | Select-Object -First 1
    if ($exact) { return $exact.FullName }
    $minor = $found | Where-Object { $_.Name -like 'Godot_v4.7*' } | Sort-Object Name -Descending | Select-Object -First 1
    if ($minor) { return $minor.FullName }
    return $null
}

if (-not $Godot) { $Godot = $env:USA_GODOT }
if (-not $Godot -and -not $BackendOnly) { $Godot = Find-Godot }
$logDir = Join-Path $env:TEMP 'usa-demo'
$backendScript = Join-Path $repo 'backend\run_backend.ps1'
$userData = Join-Path $env:APPDATA 'Godot\app_userdata\Underfunded Space Agency'

function Get-Health {
    try { return Invoke-RestMethod -Uri $healthUrl -TimeoutSec 2 } catch { return $null }
}

function Stop-Tree([int]$ProcessId) {
    & taskkill.exe /PID $ProcessId /T /F *> $null
}

$backendDir = Join-Path $repo 'backend'
$requirements = Join-Path $backendDir 'requirements.txt'
# Where the backend environment lives. backend\.venv is committed to the repo, but it only runs on the PC that built it
# (it points at that PC's Python install). So: backend\.venv when it runs here; else a fresh backend\.venv when none
# exists and the path is short enough; else a short per-user folder, because some packages (elevenlabs) nest files
# ~210 characters deep and a fresh install fails past Windows' 260-character limit unless long paths are enabled.
# run_backend.ps1 picks the first of the same two places that runs.
$repoVenv = Join-Path $backendDir '.venv'
$shortVenv = Join-Path $env:LOCALAPPDATA 'USA-venv'
$deepestPackageFile = 215   # longest file path inside a venv for requirements.txt today (208), plus headroom
function Test-LongPathsEnabled {
    try { return (Get-ItemPropertyValue 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem' 'LongPathsEnabled') -eq 1 } catch { return $false }
}
function Test-VenvFits([string]$Dir) { return (Test-LongPathsEnabled) -or ($Dir.Length + 1 + $deepestPackageFile -le 259) }
function Test-VenvWorks([string]$Dir) {
    $exe = Join-Path $Dir 'Scripts\python.exe'
    if (-not (Test-Path -LiteralPath $exe)) { return $false }
    $previousPref = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    # Import what the backend loads at startup, so a venv with missing files (a partial ZIP extract) counts as broken.
    try { & $exe -c 'import fastapi, uvicorn, google.genai, elevenlabs.client' *> $null; return $LASTEXITCODE -eq 0 }
    catch { return $false }
    finally { $ErrorActionPreference = $previousPref }
}
if (Test-VenvWorks $repoVenv) { $venvDir = $repoVenv }
elseif (-not (Test-Path -LiteralPath (Join-Path $repoVenv 'Scripts\python.exe')) -and (Test-VenvFits $repoVenv)) { $venvDir = $repoVenv }
else { $venvDir = $shortVenv }
$venvPython = Join-Path $venvDir 'Scripts\python.exe'
$installedStamp = Join-Path $venvDir 'usa-requirements.sha256'

function Find-BasePython {
    # Python 3.12+ from the py launcher, else from PATH (skipping the Microsoft Store stub, which fails the check).
    foreach ($candidate in @(@('py', '-3'), @('python'))) {
        if (-not (Get-Command $candidate[0] -ErrorAction SilentlyContinue)) { continue }
        $exe = $candidate[0]
        $pre = @($candidate | Select-Object -Skip 1)
        $ok = & $exe @pre -c 'import sys; print(sys.version_info >= (3, 12))' 2>$null
        if ($LASTEXITCODE -eq 0 -and "$ok".Trim() -eq 'True') { return , $candidate }
    }
    return $null
}

function Initialize-Backend {
    # Creates the environment and installs backend\requirements.txt when it is missing, does not run here, or the
    # requirements changed since the last install. A fresh clone has no backend\.env (git-ignored): copy the example.
    $envFile = Join-Path $backendDir '.env'
    if (-not (Test-Path -LiteralPath $envFile)) {
        Copy-Item -LiteralPath (Join-Path $backendDir '.env.example') -Destination $envFile
        Write-Host 'Created backend\.env from .env.example. Add your GEMINI_API_KEY and ELEVENLABS_API_KEY there for live mode,' -ForegroundColor Yellow
        Write-Host 'or run Start-Demo.bat -Offline to play with the fake providers (no keys needed).' -ForegroundColor Yellow
    }

    $wantedHash = (Get-FileHash -LiteralPath $requirements -Algorithm SHA256).Hash
    $haveHash = if (Test-Path -LiteralPath $installedStamp) { (Get-Content -LiteralPath $installedStamp -Raw).Trim() } else { '' }
    $works = Test-VenvWorks $venvDir
    if ($works -and $haveHash -eq $wantedHash) { return }

    $previousPref = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'   # pip writes notices to stderr; only exit codes decide failure here
    try {
        if (-not $works -and $venvDir -eq $shortVenv -and (Test-Path -LiteralPath $venvDir)) {
            Write-Host "The backend environment in $venvDir does not run; rebuilding it ..." -ForegroundColor Yellow
            Remove-Item -LiteralPath $venvDir -Recurse -Force
        }
        if (-not (Test-Path -LiteralPath $venvPython)) {
            if (-not (Test-VenvFits $venvDir)) {
                throw "The backend packages need file paths longer than Windows allows here ($venvDir). Turn on Windows long paths (Settings > System > Advanced > Enable long paths, or ask an administrator), then run the demo again."
            }
            $base = Find-BasePython
            if (-not $base) { throw 'Python 3.12 or newer was not found. Install it from python.org (tick "Add python.exe to PATH" or keep the py launcher), then run the demo again.' }
            Write-Host "First run: creating the backend Python environment in $venvDir with '$($base -join ' ')' ..." -ForegroundColor Cyan
            $exe = $base[0]
            $pre = @($base | Select-Object -Skip 1)
            & $exe @pre -m venv $venvDir
            if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $venvPython)) { throw "Creating $venvDir failed (exit $LASTEXITCODE)." }
        }
        Write-Host 'Installing backend packages from backend\requirements.txt (needs internet; about a minute) ...' -ForegroundColor Cyan
        & $venvPython -m pip install --disable-pip-version-check -q -r $requirements
        if ($LASTEXITCODE -ne 0) { throw "Installing backend packages failed (exit $LASTEXITCODE). Check the internet connection and run the demo again." }
        Set-Content -LiteralPath $installedStamp -Value $wantedHash -Encoding ascii
        Write-Host 'Backend environment ready.' -ForegroundColor Green
    }
    finally { $ErrorActionPreference = $previousPref }
}

Write-Host ''
Write-Host '=== Underfunded Space Agency demo ===' -ForegroundColor Cyan
if ($NoBackend) { $mode = 'game only (no backend)' }
elseif ($Live) { $mode = 'LIVE providers' }
else { $mode = 'offline practice (fake providers)' }
Write-Host "Mode : $mode"
Write-Host "Repo : $repo"
Write-Host "Godot: $(if ($Godot) { $Godot } elseif ($BackendOnly) { '(not needed)' } else { '(not found)' })"
if ($Live -and -not $NoBackend) {
    Write-Host 'WARNING: live mode uses API quota (Gemini / ElevenLabs).' -ForegroundColor Yellow
}

if ($DryRun) {
    Write-Host ''
    Write-Host '[dry run] nothing will be started. Commands:' -ForegroundColor Yellow
    if (-not $NoBackend) {
        $fake = if ($Live) { 'false' } else { 'true' }
        Write-Host "  (reuse the proxy if $healthUrl already answers)"
        Write-Host "  if $venvDir is missing or requirements.txt changed: py -3 -m venv; pip install -r backend\requirements.txt"
        Write-Host '  if backend\.env is missing: copy backend\.env.example to backend\.env'
        Write-Host "  child env USE_FAKE_PROVIDERS=$fake"
        Write-Host "  powershell -NoProfile -ExecutionPolicy Bypass -File `"$backendScript`"   (hidden; logs: $logDir\backend.out.log, backend.err.log)"
        Write-Host "  wait up to 30 s for $healthUrl"
    }
    if (-not $BackendOnly) {
        Write-Host "  dotnet build UnderfundedSpaceAgency.csproj -v q --nologo   (cwd $repo)"
        Write-Host "  `"$Godot`" --headless --path `"$repo`" --import   (imports fonts, textures and models into .godot)"
        Write-Host '  child env USA_BACKEND_AUTOSTART=0'
        Write-Host "  `"$Godot`" --path `"$repo`"   (waits for the window to close)"
    }
    if (-not $NoBackend) { Write-Host '  on exit: stop the proxy only if this script started it (taskkill /T /F)' }
    exit 0
}

function Stop-WithMessage([string[]]$Lines) {
    Write-Host ''
    foreach ($line in $Lines) { Write-Host $line -ForegroundColor Red }
    exit 1
}

if (-not $BackendOnly) {
    if (-not $Godot) {
        $lines = @(
            'Godot 4.7 .NET was not found.',
            "Download 'Godot Engine - .NET' 4.7.2 for Windows from $godotDownload",
            'and unzip it into your Downloads folder (or next to this folder), then run the demo again.',
            'Or point the launcher at it: Start-Demo.bat -Godot "C:\path\to\Godot_v4.7.2-stable_mono_win64.exe"'
        )
        if ($otherGodots.Count) { $lines += "Found other Godot versions, which cannot run this project: $($otherGodots -join '; ')" }
        Stop-WithMessage $lines
    }
    if (-not (Test-Path -LiteralPath $Godot)) {
        Stop-WithMessage @("Godot not found at '$Godot' (from -Godot or `$env:USA_GODOT). Check the path and run the demo again.")
    }
    if ((Split-Path $Godot -Leaf) -notmatch 'mono') {
        Stop-WithMessage @("'$Godot' is the standard Godot build; this game needs the .NET build ('Godot Engine - .NET', file name ending in _mono_win64.exe) from $godotDownload")
    }
    $sdks = if (Get-Command dotnet -ErrorAction SilentlyContinue) { & dotnet --list-sdks 2>$null } else { @() }
    if (-not ($sdks | Where-Object { $_ -match '^(1[1-9]|[2-9]\d)\.' })) {
        Stop-WithMessage @('The .NET 11 SDK is needed to build the game. Install it from https://dotnet.microsoft.com/download/dotnet/11.0 and run the demo again.')
    }
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
            Initialize-Backend
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

        # A download or clone has no .godot folder (git-ignored): Godot's converted fonts, textures and models live there,
        # and the game cannot load any of them until they are imported. The editor does this when it opens a project;
        # launching the game directly does not, so import here (about 20 s the first time, a few seconds after).
        $firstImport = -not (Test-Path -LiteralPath (Join-Path $repo '.godot\imported'))
        Write-Host $(if ($firstImport) { 'Importing game assets (first run, about 20 seconds) ...' } else { 'Checking game assets ...' })
        $consoleGodot = $Godot -replace '\.exe$', '_console.exe'
        $importer = if (Test-Path -LiteralPath $consoleGodot) { $consoleGodot } else { $Godot }
        # Godot 4.7.2's first import sometimes crashes partway (access violation while importing a font); running it
        # again resumes where it stopped, so retry a few times.
        $importLog = Join-Path $env:TEMP 'usa-import.out.log'
        $prevAuto = $env:USA_BACKEND_AUTOSTART
        $env:USA_BACKEND_AUTOSTART = '0'
        try {
            $importCode = $null
            foreach ($attempt in 1..3) {
                if ($attempt -gt 1) { Write-Host "  Godot's importer stopped early (exit $importCode); retrying ($attempt of 3) ..." -ForegroundColor Yellow }
                $import = Start-Process -FilePath $importer -ArgumentList @('--headless', '--path', "`"$repo`"", '--import') `
                    -NoNewWindow -PassThru -Wait -RedirectStandardOutput $importLog `
                    -RedirectStandardError (Join-Path $env:TEMP 'usa-import.err.log')
                $importCode = $import.ExitCode
                if ($importCode -eq 0) { break }
            }
        }
        finally {
            if ($null -eq $prevAuto) { Remove-Item Env:\USA_BACKEND_AUTOSTART -ErrorAction SilentlyContinue } else { $env:USA_BACKEND_AUTOSTART = $prevAuto }
        }
        if ($importCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $repo '.godot\imported'))) {
            throw "Importing the game assets failed (exit $importCode) after 3 attempts. Details: $importLog. Opening the project once in the Godot editor also imports them."
        }

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
