# Underfunded Space Agency v1

Playable demo of mission 1, *Out of Control* (Godot 4.7.2 .NET, C#).

## Play

Double-click `Start-Demo.bat`. It starts the local AI proxy (`backend/`, live Gemini + ElevenLabs using the keys in `backend/.env`), builds the C# project, launches the game, and stops the proxy when you close the window.

- `-Offline`: fake providers (no API quota, canned replies)
- `-NoBackend`: game only
- `-Godot <exe>` or `$env:USA_GODOT`: Godot .NET executable if it is not in the default Downloads location

Needs Godot 4.7.2 .NET, the .NET 11 SDK and Python 3.12 or newer.

### First run after cloning

The repo is the full package: the game, the backend, its Python environment (`backend/.venv`) and its cache of NASA media and crew voice lines. Only `backend/.env`, which holds the API keys, is left out.

Some files in `backend/.venv` sit more than 200 characters deep, so clone with long paths on, or Windows git reports "Filename too long":

```
git clone -c core.longpaths=true https://github.com/hackstreetboy1/underfunded-space-agency.git
```

On the first run `Start-Demo.bat` fills in what a new PC is missing:

- `backend/.venv` points at the Python install of the PC that built it. If it doesn't run on yours, the launcher builds a fresh environment from `backend/requirements.txt` (needs internet, about a minute) in `%LOCALAPPDATA%\USA-venv`. It reinstalls when `requirements.txt` changes.
- It copies `backend/.env.example` to `backend/.env`. Put your own `GEMINI_API_KEY` and `ELEVENLABS_API_KEY` in it for live mode, or play with `-Offline`.

Logs, saves and repros: `%APPDATA%\Godot\app_userdata\Underfunded Space Agency\`. Press F12 in the game for the debug overlay.

Do not share `backend/.env`: it holds the API keys.
