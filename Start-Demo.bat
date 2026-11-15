@echo off
rem Double-click demo launcher. Switches pass through: -Live, -NoBackend, -DryRun
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Demo.ps1" %*
echo.
pause
