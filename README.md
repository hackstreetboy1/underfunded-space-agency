# Underfunded Space Agency v1

Playable demo of mission 1, *Out of Control* (Godot 4.7.2 .NET, C#).

## Play

Double-click `Start-Demo.bat`. It starts the local AI proxy (`backend/`, live Gemini + ElevenLabs using the keys in `backend/.env`), builds the C# project, launches the game, and stops the proxy when you close the window.

- `-Offline`: fake providers (no API quota, canned replies)
- `-NoBackend`: game only
- `-Godot <exe>` or `$env:USA_GODOT`: Godot .NET executable if it is not in the default Downloads location

Needs Godot 4.7.2 .NET, the .NET 11 SDK, and Python 3.13 (the backend's `.venv` was built against `%LOCALAPPDATA%\Programs\Python\Python313`).

Logs, saves and repros: `%APPDATA%\Godot\app_userdata\Underfunded Space Agency\`. Press F12 in the game for the debug overlay.

Do not share `backend/.env`: it holds the API keys.
