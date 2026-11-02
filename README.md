# FallWeiss — Unity project

Unity version of the game (3D RTS/FPS, URP).

## Opening it
1. Install the Unity Editor version listed in `ProjectSettings/ProjectVersion.txt` (via Unity Hub).
2. Clone the repo: `git clone https://github.com/Malachi-Nelson/FallWeiss.git`
3. In Unity Hub: **Add → Add project from disk** and pick the cloned folder. The first open rebuilds `Library/` and takes a few minutes.

## Layout
- `Assets/FallWeiss/Scripts/` — game code (`Game.cs`, `World.cs`, `Unit.cs`, `SoldierVisual.cs`, …)
- `Assets/FallWeiss/Editor/` — editor tools (map editor, build, soldier library builder)
- `Assets/FallWeiss/Resources/` — runtime data (`missions.json`, `models.bytes`, materials)
- `Assets/FallWeiss/Shaders/` — custom shaders
- `Assets/WW2_German_soilders/` — third-party soldier/weapon model pack
