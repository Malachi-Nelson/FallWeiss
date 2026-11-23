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

### Deploying your troops
Before each battle you place your side's troops on the map: rifle sections, MG teams, AT guns, tanks, marksmen and cavalry,
scaled by the battle size. Pick a unit, tap the bright area of the map, drag to move; **Auto-deploy** puts the rest at their
historical positions. Only what you place fights. Attackers deploy round their assembly area; defenders anywhere from just in
front of position A back, and defenders you place hold that ground until the position they guard falls. The computer always
deploys its side historically. **Your troops: Automatic** in the menu skips the planner (`js/deploy.js`).

### On a phone
Turn the phone sideways. Left thumb moves (push to the edge to run), right thumb looks; FIRE also steers the aim while held.
Buttons: AIM, RELOAD, NADE, CROUCH, PRONE, SWAP, MAP and pause; their size is a menu setting (Small / Normal / Large). Touch
**aim assist** (on by default, can be switched off) slows the view across an enemy near the sights and draws it gently onto him.
The screen stays on during a battle, Android's back button pauses instead of leaving, the game pauses when the phone is turned
upright, and the resolution adjusts itself to hold the frame rate. On iPhone, Share → Add to Home Screen plays full screen.

### Playing online
The menu's **Play online** card lists battles hosted in your room. Pick your side, then **Join**; or set up a battle and press
**Host this battle online**. **New private room** gives you an invite link (`…/FallWeiss/#abc123`) for friends only.
Co-op and versus are the same thing here: humans pick sides, the AI fills both armies.

- No game server: like Loss of Signal, HoodWar and Zombies, every page talks through free public MQTT relays
  (`js/net/relay.js`, MQTT.js 4.3.7, MIT licence), so home routers and phone networks are not in the way.
- The host's page runs the battle (all AI, objectives, tickets, artillery, Stukas) and sends a snapshot about 7 times a second
  (`js/net/online.js`); everyone else runs their own soldier and draws the snapshots. What you hit on your screen is what you hit.
- The host must keep their tab open: if it closes or sleeps, the battle ends for everyone.

### Graphics (1.1)
Leaf-card trees with wind and light through the leaves, a cloud deck whose shadows drift across the ground, bloom and sun shafts,
texel-snapped sun shadows (4096 on desktop High), ground darkening under trees and along walls, field-to-field colour variation,
moving water, smoke lit by the time of day, and grass sprays matched to the ground.

### Checks
`webtest/` has headless Chrome checks (serve `docs/` on 127.0.0.1:8765 first): `node webtest/smoke.mjs` opens every battle and
fights it for a while; `node webtest/online.mjs` plays a host and a joiner against each other through a stand-in relay;
`node webtest/deploy.mjs` checks the deployment rules; `node webtest/phone.mjs device=pixel|se` plays it as a phone.

| File | What is in it |
|---|---|
| `js/main.js` | Game director: missions, forces, squads and commanders, capture, waves and tickets, Stukas / artillery / the Schleswig-Holstein, ballistics, loop |
| `js/net/online.js`, `js/net/relay.js` | Online battles: lobby, host snapshots, players' reports, the relay connection |
| `js/world.js` | Terrain (World.cs port), splat-textured ground, buildings, villages, forests, rails, water, sky, collision and line-of-sight |
| `js/units.js` | Soldiers, cavalry, tanks and AT guns: RO2-style AI (spotting, cover, suppression, bounding advance), armour vs penetration |
| `js/player.js` | First-person soldier: movement, stances, projectiles with drop, bolt cycling, recoil, sway, grenades, bleeding |
| `js/look.js` | Lighting: aerial perspective / height fog, sky-lit IBL, film grade, grass |
| `js/textures.js` | Procedurally painted textures (fields, roads, cobbles, plaster, brick, tiles, thatch…) with normal maps |
| `js/audio.js` | Synthesized audio: gunshots with distance delay and reverb, cracks, cannon, Stuka siren, engines, ambience |
| `js/fx.js`, `js/hud.js`, `js/input.js`, `js/defs.js`, `js/models.js` | Effects, HUD, touch / keyboard controls, unit and weapon tables, model loader |
