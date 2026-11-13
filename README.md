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

## Web edition (playable on phones)

`docs/` holds a browser version of Fall Weiss built with three.js. GitHub Pages serves it from this branch:
**https://malachi-nelson.github.io/FallWeiss/**

- Same battles and data as the Unity project: `docs/data/missions.json` and `docs/data/models.bin` (the original soldiers, tanks, guns and first-person weapons, converted from `Resources/models.bytes` by a lossless re-pack).
- Terrain heights are a line-for-line port of `World.cs` (BaseHeight / TrenchCut), so maps keep their authored shape.
- Phone: landscape, left thumb moves, right thumb looks; FIRE, AIM, R, stance, grenade, switch, bandage buttons. Add to the home screen for full screen.
- Desktop: WASD, mouse, LMB fire, RMB aim, C crouch, Z prone, R reload, G grenade, Q switch, B bandage, M map, Esc pause.
- `?m=<n>` opens a battle directly (0 Westerplatte … 7 Test Range); `&side=1` plays the Polish side; `&go=1` skips the menu.

| File | What is in it |
|---|---|
| `js/main.js` | Game director: missions, forces, squads and commanders, capture, waves and tickets, Stukas / artillery / the Schleswig-Holstein, ballistics, loop |
| `js/world.js` | Terrain (World.cs port), splat-textured ground, buildings, villages, forests, rails, water, sky, collision and line-of-sight |
| `js/units.js` | Soldiers, cavalry, tanks and AT guns: RO2-style AI (spotting, cover, suppression, bounding advance), armour vs penetration |
| `js/player.js` | First-person soldier: movement, stances, projectiles with drop, bolt cycling, recoil, sway, grenades, bleeding |
| `js/look.js` | Lighting: aerial perspective / height fog, sky-lit IBL, film grade, grass |
| `js/textures.js` | Procedurally painted textures (fields, roads, cobbles, plaster, brick, tiles, thatch…) with normal maps |
| `js/audio.js` | Synthesized audio: gunshots with distance delay and reverb, cracks, cannon, Stuka siren, engines, ambience |
| `js/fx.js`, `js/hud.js`, `js/input.js`, `js/defs.js`, `js/models.js` | Effects, HUD, touch / keyboard controls, unit and weapon tables, model loader |
