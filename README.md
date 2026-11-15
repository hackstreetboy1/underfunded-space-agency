# Underfunded Space Agency v1

Playable demo of mission 1, *Out of Control* (Godot 4.7.2 .NET, C#). You are CAPCOM: talk the crew of a damaged spacecraft through the emergency by voice or text and bring them home.

## What you need

- **Godot 4.7.2 .NET** for Windows: the "Godot Engine - .NET" download from https://godotengine.org/download/archive/4.7.2-stable/ (the standard build cannot run C#). Unzip it into your Downloads folder or next to this folder and the launcher finds it.
- **.NET 11 SDK**: https://dotnet.microsoft.com/download/dotnet/11.0
- **Python 3.12 or newer**: https://www.python.org/downloads/ (keep the `py` launcher, or tick "Add python.exe to PATH")
- An internet connection on the first run, and API keys for live mode: Gemini (https://aistudio.google.com/apikey) and ElevenLabs (https://elevenlabs.io/app/settings/api-keys).

## Get it

**ZIP:** on GitHub, *Code > Download ZIP*, then extract it anywhere.

**Git:**

```
git clone https://github.com/hackstreetboy1/underfunded-space-agency.git
```

The longest path in the repo is 157 characters, so both work in an ordinary folder. Only if you extract or clone into a folder whose own path is over about 100 characters can Windows refuse a file as "Path too long"; pick a shorter folder such as `C:\Games` then. If a file does go missing, the launcher notices that `backend\.venv` no longer runs and builds a fresh one.

## Play

1. Put your keys in `backend\.env` (copy `backend\.env.example`, or let the first run create it):
   ```
   GEMINI_API_KEY=your-gemini-key
   ELEVENLABS_API_KEY=your-elevenlabs-key
   ```
2. Double-click `Start-Demo.bat`. Windows may warn that the file came from the internet; choose *Run* (or *More info > Run anyway*).

The launcher starts the local AI proxy (`backend\`, live Gemini + ElevenLabs), builds the C# project, launches the game, and stops the proxy when you close the window.

- `-Offline`: fake providers (no keys or API quota, canned replies)
- `-NoBackend`: game only
- `-Godot <exe>` or `$env:USA_GODOT`: use a Godot .NET executable the launcher did not find

### First run

The repo is the full package: the game, the backend, its Python environment (`backend\.venv`) and its cache of NASA media and crew voice lines. Left out: `backend\.env`, which holds the API keys, and 28 ElevenLabs SDK files for features the game never uses, whose paths are too long for a Windows ZIP extract (listed in `.gitignore`).

`backend\.venv` points at the Python install of the PC that built it. If it does not run on yours, the launcher builds a fresh environment from `backend\requirements.txt` in `%LOCALAPPDATA%\USA-venv` (about a minute). It reinstalls when `requirements.txt` changes. If `backend\.env` is missing, it copies `.env.example` there and says which keys to add.

Logs, saves and repros: `%APPDATA%\Godot\app_userdata\Underfunded Space Agency\`. Press F12 in the game for the debug overlay.

Never commit or share `backend\.env`: it holds the API keys.
