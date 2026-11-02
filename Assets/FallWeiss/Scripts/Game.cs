// Fall Weiss: Poland 1939 — main game controller.
// Port of game.js: startMission, director/waves, capture points (+ Final Push), tickets, end conditions, spawn protection,
// pickups, kill streaks/banners, resupply, CP income, fog-of-war spotting, support strikes (Stukas / artillery),
// flybys, the Westerplatte battleship, time of day / rain, first-person controller with the original iron-sight
// viewmodels, the RTS command map, minimap, HUD and a live-battle main menu.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FallWeiss
{
    public enum GState { Menu, Playing, Dead, Paused, End }

    public class Game : MonoBehaviour
    {
        public static Game I;
        public Mission[] missions; public Mission M; public int missionIdx, ME, EN = 1;
        public GState state = GState.Menu; public bool rts;
        public readonly List<Unit> units = new List<Unit>();
        public readonly HashSet<Unit> selected = new HashSet<Unit>();
        public Unit player, lastPlayer;
        public float t; public int[] tix = new int[2]; public int kills, losses; public float cp;
        const float WaveTime = 15;   // reinforcement wave interval for the player's redeploy
        public Fx fx = new Fx(); public SoundBank audio = new SoundBank(); public Visuals vis = new Visuals();
        Camera cam; Light sun; Transform unitRoot; int nextId = 1, slotCounter;
        float aiT, raidT, respT, hitT, dmgT, supportCd, aiSup, visT, todT, fireT, resT, shipT, barT, flyT, streakT; int streak, barrageLeft; bool hitKill, hitHead, armSupport;
        float?[] waveNext = new float?[2];
        string endTitle, endWhy; bool won;
        readonly List<(string s, float t)> msgs = new List<(string, float)>();
        int menuSide = 0, menuMission = 0, classIdx = 0; float menuOrbit; bool menuBuilt;
        Texture2D minimap;
        float szT;
        public bool tp, orderOpen, buyOpen; bool respSquad, hold, gasp; float orderT, buyT, breath = 1, shake, convT; int lastClass;
        Rect miniRect; readonly List<LineRenderer> pathLines = new List<LineRenderer>();
        class Convoy { public Transform m; public float s, wait, L; public Vector2 A, dir; }
        readonly List<Convoy> convoy = new List<Convoy>();
        class Beam { public Transform t; public float life; public Renderer r; }
        readonly List<Beam> beams = new List<Beam>();

        // first person state (port of fp)
        float yaw, pitch, ads, fov = 70, recoil, bob, eye = 1.55f, swT, fireQueued, gT, lean; Vector3 tpPivot;
        Transform vm; string vmKind; readonly List<Grenade> grenades = new List<Grenade>();
        class Grenade { public Vector3 p, v; public float fuse; public Transform t; public Unit by; }
        static readonly float[] STAND = { 1.55f, .95f, .4f }, SPD = { 1, .55f, .25f };
        static readonly Dictionary<string, Vector4> HIP = new Dictionary<string, Vector4>
        {   // x, y, z, ry (three.js camera space; z and ry are mirrored when applied)
            ["sniper"] = new Vector4(.21f, -.19f, -.02f, .06f), ["rifle"] = new Vector4(.21f, -.19f, -.02f, .06f), ["smg"] = new Vector4(.2f, -.18f, -.02f, .07f), ["bar"] = new Vector4(.2f, -.18f, -.03f, .06f),
            ["mg"] = new Vector4(.19f, -.17f, -.03f, .05f), ["pistol"] = new Vector4(.13f, -.12f, -.02f, .04f), ["knife"] = new Vector4(.18f, -.1f, .02f, -.5f), ["sabre"] = new Vector4(.2f, -.08f, .02f, -.6f)
        };

        // pickups / banners / planes / fires
        class Pickup { public float x, z, t; public bool med; public Transform g; }
        readonly List<Pickup> pickups = new List<Pickup>();
        class Banner { public string a, b; public Color c; public float t; }
        Banner banner;
        class Plane { public Transform t, prop; public float t0, speed, lat, life, x, z, dx, dz, y; public bool strike; public bool[] dropped; public int team; }
        readonly List<Plane> planes = new List<Plane>();
        class Fire { public Vector3 p; public bool big; public float life; public Obst b; }
        readonly List<Fire> fires = new List<Fire>();
        class Marker { public Transform t; public float life; }
        class PingFx { public Transform t; public float life; public Renderer r; public Color c; }
        readonly List<PingFx> pings = new List<PingFx>(); Transform[] spRings; Light inLight; MaterialPropertyBlock pingMpb;
        /// Port of pingAt(): expanding, fading ring where an RTS order lands.
        void PingAt(float x, float z, Color c)
        {
            var g = new GameObject("ping"); g.AddComponent<MeshFilter>().sharedMesh = World.RingMesh(.85f, 1, 32); var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = Mats.Transparent(Color.white, null, false, true); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            g.transform.position = new Vector3(x, World.H(x, z) + .4f, z); pings.Add(new PingFx { t = g.transform, life = .6f, r = r, c = c });
        }
        void UpdatePings(float dt)
        {
            pingMpb ??= new MaterialPropertyBlock();
            for (int i = pings.Count - 1; i >= 0; i--)
            {
                var p = pings[i]; p.life -= dt; if (p.life <= 0 || !p.t) { if (p.t) Destroy(p.t.gameObject); pings.RemoveAt(i); continue; }
                float k = p.life / .6f; p.t.localScale = Vector3.one * (1 + (1 - k) * 2.5f); pingMpb.SetColor("_Color", new Color(p.c.r, p.c.g, p.c.b, k)); p.r.SetPropertyBlock(pingMpb);
            }
        }
        /// Port of updateSpawnZones(): translucent rings marking both deployment zones (they move forward as objectives fall).
        void UpdateSpawnZones()
        {
            if (spRings == null || spRings[0] == null)
            {
                spRings = new Transform[2];
                for (int tm = 0; tm < 2; tm++)
                {
                    var g = new GameObject("SpawnZone" + tm); g.AddComponent<MeshFilter>().sharedMesh = World.RingMesh(.85f, 1, 48); var r = g.AddComponent<MeshRenderer>();
                    r.sharedMaterial = Mats.Transparent(tm == ME ? new Color(.4f, .8f, 1, .3f) : new Color(1, .6f, .4f, .3f), null, false, true); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                    g.transform.localScale = new Vector3(18, 1, 18); spRings[tm] = g.transform;
                }
            }
            for (int tm = 0; tm < 2; tm++) { var s = SpawnPos(tm); spRings[tm].position = new Vector3(s.x, World.H(s.x, s.z) + .5f, s.z); }
        }
        /// Port of updateLights(): a warm fill light follows the camera while inside an enterable building.
        void UpdateInteriorLight()
        {
            if (!inLight) { var g = new GameObject("InteriorLight"); inLight = g.AddComponent<Light>(); inLight.type = LightType.Point; inLight.color = FU.Hex(0xffe2b0); inLight.range = 22; inLight.intensity = 0; inLight.shadows = LightShadows.None; }
            bool fps = !rts && (state == GState.Playing || state == GState.Dead); var p = cam.transform.position; float on = 0;
            if (fps) foreach (var bd in Interiors.bds) if (bd.alive && p.x > bd.x0 && p.x < bd.x1 && p.z > bd.z0 && p.z < bd.z1) { on = 1; break; }
            inLight.intensity += (on * 2.2f - inLight.intensity) * .25f; inLight.transform.position = p + Vector3.up * .4f; inLight.enabled = inLight.intensity > .02f;
        }
        Unit EnemyAtScreen(Vector2 mp)
        {
            Unit best = null; float bd = 24 * Screen.height / 1080f;
            foreach (var u in units)
            {
                if (!u.alive || u.team == ME || !u.visible) continue; var sp = cam.WorldToScreenPoint(new Vector3(u.x, u.y + u.def.bh * .5f + .4f, u.z)); if (sp.z < 0) continue;
                float d = Vector2.Distance(sp, mp); if (d < bd) { bd = d; best = u; }
            }
            return best;
        }
        readonly List<Marker> markers = new List<Marker>();
        class Shell { public float at; public Vector3 p; public float r, dmg; public int team; }
        readonly List<Shell> shells = new List<Shell>();

        // rts state
        Vector3 rtsPos; float rtsYaw, rtsDist = 80; Vector2 dragStart; bool dragging;

        // ---------------- boot ----------------
        void Awake()
        {
            hipDot = PlayerPrefs.GetInt("FW.HipDot", 0) == 1;
            I = this;
            var ta = Resources.Load<TextAsset>("missions");
            missions = JsonUtility.FromJson<MissionList>(ta.text).missions;
            foreach (var m in missions) { m.lanes ??= new FLine[0]; m.rails ??= new FLine[0]; m.roads ??= new FLine[0]; m.segs ??= new FSeg[0]; m.props ??= new FProp[0]; m.landmarks ??= new FLandmark[0]; m.villages ??= new FArea[0]; m.orchards ??= new FRect[0]; m.forests ??= new FArea[0]; m.build ??= new FBuild[0]; }
            cam = Camera.main; if (!cam) { var cg = new GameObject("Main Camera"); cg.tag = "MainCamera"; cam = cg.AddComponent<Camera>(); cg.AddComponent<AudioListener>(); }
            cam.nearClipPlane = .05f; cam.farClipPlane = 1400; cam.fieldOfView = 70;
            sun = FindObjectsByType<Light>(FindObjectsInactive.Exclude).FirstOrDefault(l => l.type == LightType.Directional);
            if (!sun) { var lg = new GameObject("Sun"); sun = lg.AddComponent<Light>(); sun.type = LightType.Directional; }
            fx.Init(); audio.Init(); audio.listener = cam.transform; vis.Init(cam, sun);
            EnterMenu();
        }

        // ---------------- menu with a live battle behind it ----------------
        void EnterMenu()
        {
            Time.timeScale = 1; StopAllCoroutines(); range?.Dispose(); range = null; ClearAll(); state = GState.Menu; rts = false; Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            BuildBackdrop(menuMission);
        }
        void BuildBackdrop(int id)
        {
            ClearAll(); M = missions[id]; ME = menuSide; EN = 1 - ME; World.Build(M); vis.Apply(M); unitRoot = new GameObject("Units").transform;
            t = 60; tix[0] = tix[1] = 999; aiT = 2; waveNext[0] = waveNext[1] = null; flyT = 8; barT = 2; barrageLeft = 6; shipT = 4;
            for (int s = 0; s < 2; s++) foreach (var f in M.Forces(s)) PlaceForce(s, f);
            foreach (var u in units) u.prot = 0; Fires(); Structures.BuildCover(); BuildConvoy(); menuBuilt = true; menuOrbit = Random.value * 6.28f;
        }
        void MenuCamera(float dt)
        {
            menuOrbit += dt * .035f; var c = World.points.Count > 2 ? new Vector3(World.points[1].x, 0, World.points[1].z) : Vector3.zero;
            float r = M.half * .55f; var p = c + new Vector3(Mathf.Sin(menuOrbit) * r, 0, Mathf.Cos(menuOrbit) * r); p.y = World.H(p.x, p.z) + 38;
            cam.transform.position = p; cam.transform.rotation = Quaternion.LookRotation(new Vector3(c.x, World.H(c.x, c.z) + 4, c.z) - p); cam.fieldOfView = 55;
        }

        // ---------------- mission start ----------------
        TestRange range;   // non-null while the weapons Test Range is running
        [System.NonSerialized] public float debugAds = -1;
        public void StartRange() { int i = System.Array.FindIndex(missions, m => m.range); if (i >= 0) StartMission(i, 0); }
        public void StartMission(int id, int side)
        {
            missionIdx = menuMission = id; M = missions[id]; ME = side; EN = 1 - side;
            range?.Dispose(); range = null; ClearAll(); SoldierLib.forceAll = M.range; if (M.range) { ME = 0; EN = 1; }
            World.Build(M); vis.Apply(M);
            unitRoot = new GameObject("Units").transform;
            t = 0; tix[0] = tix[1] = M.tickets; tix[1 - M.attacker] = Mathf.RoundToInt(M.tickets * 1.6f); kills = losses = 0; cp = 120;
            aiT = 2; raidT = 75; aiSup = 140; supportCd = 0; waveNext[0] = waveNext[1] = null; msgs.Clear(); nextId = 1; slotCounter = 0; selected.Clear();
            streak = 0; streakT = 0; shipT = 6; barT = 4; barrageLeft = M.barrage; flyT = 25; resT = 5; banner = null;
            for (int s = 0; s < 2; s++) foreach (var f in M.Forces(s)) PlaceForce(s, f);
            if (M.range) { range = new TestRange(this); range.Setup(); }
            Fires(); minimap = Visuals.BakeMinimap(M); Structures.BuildCover(); BuildConvoy(); respSquad = false; lastClass = 0; breath = 1;
            state = GState.Playing; menuBuilt = false;
            Msg(ME == M.attacker ? "ATTACK: take the objectives in order. Every death costs one reinforcement." : "DEFEND: hold the line until the clock runs out. Lost positions cannot be retaken.");
            var sp0 = M.spawn[ME]; var rifles = units.Where(u => u.team == ME && u.type == "rifle").OrderBy(u => FU.Hyp(u.x - sp0.x, u.z - sp0.z)).ToList();
            Unit leader = rifles.FirstOrDefault();
            if (leader == null || ME != M.attacker || FU.Hyp(leader.x - sp0.x, leader.z - sp0.z) > 40) { var q = FreeSpot(sp0.x, sp0.z, 10, .6f); leader = SpawnUnit(ME, "rifle", q.x, q.y, FaceEnemy(ME)); }
            leader.InitWeapons(Defs.CLASSES[0].weps[ME]); leader.gren = 2;
            foreach (var u in units.Where(u => u.team == ME && u != leader && u.def.kind == "inf" && FU.Hyp(u.x - leader.x, u.z - leader.z) < 60).OrderBy(u => FU.Hyp(u.x - leader.x, u.z - leader.z)).Take(8)) { u.squad = true; u.follow = true; u.slot = slotCounter++; }
            rts = false; Time.timeScale = 1;
            EnterFps(leader);
            ShowBanner(M.name.ToUpper(), M.date + " — " + (ME == M.attacker ? "attack" : "defend"), UI.Gold);
            Msg("Hold RMB to aim. V = squad orders · B = support · Tab = command map · Esc = pause.");
        }
        void Fires() { fires.Clear(); for (int i = 0; i < World.fires.Count; i++) fires.Add(new Fire { p = World.fires[i], big = World.bigFire[i], life = -1, b = World.fireObst[i] }); }
        public void AddBurning(Obst b) { fires.Add(new Fire { p = new Vector3((b.x0 + b.x1) / 2, b.y1 + .5f, (b.z0 + b.z1) / 2), big = true, life = -1, b = b }); }
        public void RemoveBurning(Obst b) { fires.RemoveAll(f => f.b == b); }
        public void Shake(float s) { shake = Mathf.Max(shake, s); }
        public Unit NearestEnemy(Unit u) { Unit b = null; float bd = 1e9f; foreach (var e in units) { if (!e.alive || e.team == u.team) continue; float d = FU.Hyp(e.x - u.x, e.z - u.z); if (d < bd) { bd = d; b = e; } } return b; }
        float burnT;
        void Burning(float dt)
        {   // port of the burnT block: smoking buildings slowly burn down and collapse
            burnT -= dt; if (burnT > 0) return; burnT = .5f;
            foreach (var b in World.obst.ToArray()) if (b.smoking && !b.dead && b.hp > 0 && !b.low) { b.hp -= .7f; if (b.hp <= 0) Structures.Collapse(this, b); }
        }
        public void AddFire(Vector3 p, float life) { fires.Add(new Fire { p = p, big = false, life = life }); }

        void ClearAll()
        {
            StopAllCoroutines();
            foreach (var u in units) if (u.go) Destroy(u.go); units.Clear(); player = lastPlayer = null; if (unitRoot) Destroy(unitRoot.gameObject);
            if (vm) { Destroy(vm.gameObject); vm = null; vmKind = null; vmFist = null; }
            foreach (var p in pickups) if (p.g) Destroy(p.g.gameObject); pickups.Clear();
            foreach (var p in planes) if (p.t) Destroy(p.t.gameObject); planes.Clear();
            foreach (var c in convoy) if (c.m) Destroy(c.m.gameObject); convoy.Clear();
            foreach (var b in beams) if (b.t) Destroy(b.t.gameObject); beams.Clear(); tp = orderOpen = buyOpen = false; dmgSrc.Clear(); foreach (var l in pathLines) if (l) l.enabled = false;
            foreach (var m in markers) if (m.t) Destroy(m.t.gameObject); markers.Clear();
            foreach (var p in pings) if (p.t) Destroy(p.t.gameObject); pings.Clear(); if (spRings != null) foreach (var r in spRings) if (r) Destroy(r.gameObject); spRings = null;
            grenades.ForEach(gr => { if (gr.t) Destroy(gr.t.gameObject); }); grenades.Clear(); smokes.ForEach(sm => { if (sm.t) Destroy(sm.t.gameObject); }); smokes.Clear(); aiSmokeT[0] = aiSmokeT[1] = 0; shells.Clear(); fires.Clear(); selected.Clear();
            fx.Clear(); World.Clear();
        }

        float FaceEnemy(int team) { var a = M.spawn[team]; var b = M.spawn[1 - team]; return Mathf.Atan2(b.x - a.x, b.z - a.z); }

        public Unit SpawnUnit(int team, string type, float x, float z, float heading)
        {
            var u = new Unit { id = nextId++, team = team, type = type, def = Defs.For(team, type), x = x, z = z, heading = heading, turretH = heading };
            if (type == "rifle" && u.id % 8 == 0) u.officer = true;   // a squad leader in every few riflemen (MP 38 + Luger / wz.29 + Vis)
            u.y = World.H(x, z); u.hp = u.max = u.def.hp; u.home = new Vector2(x, z); u.cd = Random.value; u.think = Random.value * .3f; u.anim = Random.value * 6; u.prot = t + 3;
            u.BuildModel(unitRoot); units.Add(u); return u;
        }

        public Vector2 FreeSpot(float cx, float cz, float sp, float r)
        {
            for (int i = 0; i < 40; i++)
            {
                float a = Random.value * 6.28f, d = Mathf.Sqrt(Random.value) * sp, x = Mathf.Clamp(cx + Mathf.Cos(a) * d, -M.half + 3, M.half - 3), z = Mathf.Clamp(cz + Mathf.Sin(a) * d, -M.half + 3, M.half - 3);
                if (!World.InBuildingG(x, z, r + .5f) && !World.InWater(x, z)) return new Vector2(x, z);
            }
            for (float rr = sp + 4; rr < sp + 60; rr += 4) for (int i = 0; i < 16; i++)
                {   // widen the search instead of dropping units inside a building
                    float a = i / 16f * 6.28f, x = Mathf.Clamp(cx + Mathf.Cos(a) * rr, -M.half + 3, M.half - 3), z = Mathf.Clamp(cz + Mathf.Sin(a) * rr, -M.half + 3, M.half - 3);
                    if (!World.InBuildingG(x, z, r + .5f) && !World.InWater(x, z)) return new Vector2(x, z);
                }
            return new Vector2(cx, cz);
        }

        void PlaceForce(int team, FForce f)
        {
            if (f.type == "cav" && team == 0) return;
            float cx, cz; if (f.loc < 0) { cx = M.spawn[team].x; cz = M.spawn[team].z; } else { var p = World.points[Mathf.Min(f.loc, World.points.Count - 1)]; cx = p.x; cz = p.z; }
            float dir = M.spawn[1 - M.attacker].z > M.spawn[M.attacker].z ? 1 : -1;
            for (int i = 0; i < f.n; i++)
            {
                var q = FreeSpot(cx, cz + (f.loc < 0 ? 0 : (team == M.attacker ? -3 : 3) * dir), f.sp, Defs.For(team, f.type).r);
                var u = SpawnUnit(team, f.type, q.x, q.y, FaceEnemy(team)); if (f.loc >= 0) { u.home = q; }
            }
        }

        // ---------------- helpers used by units ----------------
        public bool InSZ(Unit e) { var s = SpawnPos(e.team); return FU.Hyp(e.x - s.x, e.z - s.z) < 18; }
        public bool IsProt(Unit e) => e.prot > t && InSZ(e);
        public CapPoint ActivePoint() { int i = ActiveIdx(); return i < World.points.Count ? World.points[i] : null; }
        int ActiveIdx() { int i = World.points.FindIndex(p => !p.locked); return i < 0 ? World.points.Count : i; }
        public Vector3 SpawnPos(int team)
        {
            int att = M.attacker; if (team != att) return new Vector3(M.spawn[team].x, 0, M.spawn[team].z);
            int k = -1; for (int i = 0; i < World.points.Count; i++) if (World.points[i].locked) k = i; if (k < 0) return new Vector3(M.spawn[att].x, 0, M.spawn[att].z);
            var p = World.points[k]; var sp = M.spawn[att]; float dx = sp.x - p.x, dz = sp.z - p.z, l = Mathf.Max(.01f, FU.Hyp(dx, dz));
            // forward spawn 24 m behind the last captured point; in towns/forts that can land inside a building or rubble,
            // so walk back toward the base until the spot is open ground (otherwise reinforcements spawn stuck in walls)
            for (float o = 24; o < l; o += 4) { float x = p.x + dx / l * o, z = p.z + dz / l * o; if (!World.InBuildingG(x, z, 3) && !World.InWater(x, z)) return new Vector3(x, 0, z); }
            return new Vector3(sp.x, 0, sp.z);
        }
        public Vector2 FollowSlot(Unit u)
        {
            int row = u.slot / 4 + 1; float lat = ((u.slot % 4) - 1.5f) * 3.2f, back = 4 + row * 3.5f, h = player.heading;
            return new Vector2(player.x - Mathf.Sin(h) * back + Mathf.Cos(h) * lat, player.z - Mathf.Cos(h) * back - Mathf.Sin(h) * lat);
        }
        public void Msg(string s) { if (state == GState.Menu) return; msgs.Add((s, Time.unscaledTime)); if (msgs.Count > 7) msgs.RemoveAt(0); }
        public void HitMarker(bool kill, bool head) { hitT = .25f; hitKill = kill; hitHead = head; }
        public void DamageFlash(Unit by = null, Vector3? from = null)
        {
            dmgT = .4f; Vector3? src = from ?? (by != null ? new Vector3(by.x, by.y, by.z) : (Vector3?)null); if (!src.HasValue) return;
            foreach (var d in dmgSrc) if (FU.Hyp(d.p.x - src.Value.x, d.p.z - src.Value.z) < 4) { d.p = src.Value; d.t = 2.4f; return; }
            dmgSrc.Add(new DmgSrc { p = src.Value, t = 2.4f }); if (dmgSrc.Count > 6) dmgSrc.RemoveAt(0);
        }
        public Vector3 lastBoom;
        /// Rotate subsequent IMGUI drawing about a pivot given in 1080p HUD space (GUIUtility.RotateAroundPivot takes
        /// screen pixels, so with the scaled HUD matrix it spun things around the wrong point and off-screen).
        static void RotHud(float deg, Vector2 pivot) { GUI.matrix = GUI.matrix * Matrix4x4.TRS(pivot, Quaternion.Euler(0, 0, deg), Vector3.one) * Matrix4x4.TRS(-pivot, Quaternion.identity, Vector3.one); }
        class DmgSrc { public Vector3 p; public float t; }
        readonly List<DmgSrc> dmgSrc = new List<DmgSrc>(); static Texture2D dmgArc;
        /// Bearing (radians, 0 = facing) of a world point relative to the player's current view — recomputed every frame,
        /// so the marker stays locked to the compass direction of the shooter while the camera turns.
        float RelBearing(Vector3 p) { var c = cam.transform.position; return FU.AngDiff(yaw, Mathf.Atan2(p.x - c.x, p.z - c.z)); }
        void DrawDamageDir(float W, float H)
        {
            if (dmgSrc.Count == 0) return;
            if (!dmgArc)
            {   // crescent wedge like the original #dmgdir (radial red gradient, fading at the tips)
                const int TW = 256, TH = 48; dmgArc = new Texture2D(TW, TH, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp }; var px = new Color[TW * TH]; float ro = 174.5f, ri = 128;
                for (int y = 0; y < TH; y++) for (int x = 0; x < TW; x++) { float gy = TH - 1 - y, d = Mathf.Sqrt((x - 128f) * (x - 128f) + (ro - gy) * (ro - gy)), k = Mathf.Clamp01((d - ri) / (ro - ri)), edge = Mathf.Clamp01((ro - d) / 4), ang = 1 - Mathf.Pow(Mathf.Abs(x - 128f) / 128f, 2); px[y * TW + x] = new Color(1, .16f, .12f, k * k * .9f * edge * ang); }
                dmgArc.SetPixels(px); dmgArc.Apply();
            }
            var m = GUI.matrix; var c = new Vector2(W / 2, H / 2);
            foreach (var d in dmgSrc)
            {
                float a = Mathf.Clamp01(d.t / .9f); GUI.matrix = m; RotHud(RelBearing(d.p) * Mathf.Rad2Deg, c);
                UI.Tex(new Rect(c.x - 110, c.y - 168, 220, 41), dmgArc, new Color(1, 1, 1, a));
            }
            GUI.matrix = m;
        }
        /// Compass strip (heading ticks, cardinal letters, objective + red damage-source markers).
        void DrawCompass(float W)
        {
            float cw = 520, cx = W / 2, y = 64, half = 75 * Mathf.Deg2Rad, hd = yaw; var r = new Rect(cx - cw / 2, y, cw, 22);
            UI.Fill(r, new Color(.03f, .04f, .025f, .45f)); UI.Frame(r, new Color(.42f, .48f, .29f, .5f));
            string[] L = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            for (int i = 0; i < 24; i++)
            {
                float b = i * 15 * Mathf.Deg2Rad, rel = FU.AngDiff(hd, b); if (Mathf.Abs(rel) > half) continue; float x = cx + rel / half * cw / 2;
                if (i % 3 == 0) UI.Shadow(new Rect(x - 20, y + 1, 40, 20), L[i / 3], UI.center, i == 0 ? UI.Gold : UI.Text, .9f); else UI.Fill(new Rect(x - .5f, y + 14, 1, 6), new Color(1, 1, 1, .45f));
            }
            int ai = ActiveIdx(); if (ai < World.points.Count) { var p = World.points[ai]; float rel = RelBearing(new Vector3(p.x, 0, p.z)); float x = cx + Mathf.Clamp(rel / half, -1, 1) * cw / 2; UI.Fill(new Rect(x - 3, y - 4, 6, 4), UI.Gold); }
            foreach (var d in dmgSrc) { float rel = RelBearing(d.p), x = cx + Mathf.Clamp(rel / half, -1, 1) * cw / 2, a = Mathf.Clamp01(d.t / .9f); UI.Fill(new Rect(x - 4, y + 20, 8, 5), new Color(1, .2f, .15f, a)); UI.Fill(new Rect(x - 1, y, 2, 22), new Color(1, .2f, .15f, .5f * a)); }
            UI.Fill(new Rect(cx - 1, y - 3, 2, 5), Color.white);
        }
        void ShowBanner(string a, string b, Color c) { if (state == GState.Menu) return; banner = new Banner { a = a, b = b, c = c, t = Time.unscaledTime }; }

        // ---------------- main loop ----------------
        void Update()
        {
            var kb = Keyboard.current; var ms = Mouse.current; if (kb == null || ms == null) return;
            float dt = Mathf.Min(Time.deltaTime, .05f);
            if (state == GState.Menu)
            {
                if (!menuBuilt) return; t += dt; for (int i = 0; i < units.Count; i++) units[i].Update(this, dt); foreach (var u in units) u.SyncTransform(dt);
                Director(dt); UpdateCapture(dt, true); UpdateFires(dt); World.Tick(dt); Burning(dt); UpdateConvoy(dt); UpdateShipAndSky(dt); UpdatePlanes(dt); UpdateShells(); fx.Update(dt, cam); vis.Update(dt, t, false); MenuCamera(dt); CleanupDead(dt);
                foreach (var b in World.blades) if (b) b.Rotate(0, 0, -dt * 23, Space.Self);
                return;
            }
            if (state == GState.End) { fx.Update(dt, cam); vis.Update(dt, t, !rts); return; }
            if (kb.escapeKey.wasPressedThisFrame) { if (state == GState.Paused) Resume(); else if (state == GState.Playing || state == GState.Dead) { state = GState.Paused; Time.timeScale = 0; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; } return; }
            if (state == GState.Paused) return;

            t += dt; supportCd -= dt;
            float own = World.points.Count(p => p.locked && p.owner == ME); cp += dt * (1.5f + own * .5f) + dt * (.8f + own * .6f);
            for (int i = 0; i < units.Count; i++) units[i].Update(this, dt);
            if (player != null && player.alive && !rts) UpdatePlayer(dt, kb, ms);
            foreach (var u in units) u.SyncTransform(dt);
            if (player != null && player.alive) { resT -= dt; if (resT <= 0) { resT = 5; Resupply(player); } }
            streakT -= dt; if (streakT <= 0) streak = 0;
            UpdateGrenades(dt); UpdateSmoke(dt); UpdatePickups(dt); UpdatePlanes(dt); UpdateShells(); UpdateMarkers(dt);
            if (range == null) { Director(dt); UpdateCapture(dt, false); Raids(dt); } else range.Update(dt, kb); Spotting(dt); UpdateFires(dt); UpdateShipAndSky(dt); World.Tick(dt); Burning(dt); UpdateConvoy(dt); UpdateBeams(dt); UpdatePaths(); UpdatePings(dt); szT -= dt; if (szT <= 0) { szT = .5f; UpdateSpawnZones(); } shake = Mathf.Max(0, shake - dt * 3);
            foreach (var b in World.blades) if (b) b.Rotate(0, 0, -dt * 23, Space.Self);
            fx.Update(dt, cam); vis.Update(dt, t, !rts); vis.Suppression(player != null && player.alive && !rts ? player.supp : 0); UpdateInteriorLight();
            hitT -= dt; dmgT -= dt; ammoShowT -= dt; for (int i = dmgSrc.Count - 1; i >= 0; i--) { dmgSrc[i].t -= dt; if (dmgSrc[i].t <= 0) dmgSrc.RemoveAt(i); }
            CleanupDead(dt);
            if (kb.bKey.wasPressedThisFrame && !rts && state == GState.Playing) FpsSupport();

            if (state == GState.Dead) { respT -= dt; DeadCamera(dt); if (kb.xKey.wasPressedThisFrame) respSquad = !respSquad; for (int i = 0; i < Defs.CLASSES.Length; i++) if (kb[Key.Digit1 + i].wasPressedThisFrame && !rts) classIdx = i; if (respT <= 0 && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) Deploy(classIdx); else if (!rts && kb.fKey.wasPressedThisFrame) PossessSomething(); if (kb.tabKey.wasPressedThisFrame) { rts = !rts; } if (rts) UpdateRts(dt, kb, ms); }
            else if (rts) { UpdateRts(dt, kb, ms); if (kb.tabKey.wasPressedThisFrame) LeaveRts(); }
            else if (kb.tabKey.wasPressedThisFrame) EnterRts();
            CheckEnd();
        }

        void Resume() { state = player != null && player.alive ? GState.Playing : GState.Dead; Time.timeScale = 1; if (!rts && state == GState.Playing) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; } }

        void CleanupDead(float dt)
        {
            for (int i = units.Count - 1; i >= 0; i--) { var u = units[i]; if (!u.alive && u.deadT > (u.Foot ? 8 : 60) && u != player && u != lastPlayer) { if (u.go) Destroy(u.go); units.RemoveAt(i); } }
        }

        // fog of war (port of the visT block in step())
        void Spotting(float dt)
        {
            visT -= dt; if (visT > 0) return; visT = .3f;
            var mine = units.Where(u => u.alive && u.team == ME && u.rout <= 0).ToList();
            foreach (var u in units)
            {
                if (u.team == ME || !u.alive) { if (u != player) u.SetVisible(true); continue; }
                foreach (var m in mine)
                {
                    float d = FU.Hyp(m.x - u.x, m.z - u.z); if (d >= M.vis) continue;
                    if (d < 28 || World.LOS(new Vector3(m.x, m.y + 1.5f, m.z), new Vector3(u.x, u.y + 1.2f * u.StM, u.z))) { u.seenT = t; break; }
                }
                u.SetVisible(t - u.seenT < 2.2f);
            }
        }

        // ---------------- director (port of director()) ----------------
        void Director(float dt)
        {
            if (M.waves == null || M.waves.Length == 0) return;
            aiT -= dt; if (aiT > 0) return; aiT = 2; int att = M.attacker, act = ActiveIdx(); var fresh = new List<Unit>();
            for (int team = 0; team < 2; team++)
            {
                var w = M.waves[Mathf.Min(team, M.waves.Length - 1)]; if (waveNext[team] == null) waveNext[team] = state == GState.Menu ? t + w.every : w.t0;
                if (t >= waveNext[team] && tix[team] > 0) { fresh.AddRange(SpawnWave(team, w)); waveNext[team] += w.every; }
            }
            var pts = World.points;
            for (int team = 0; team < 2; team++)
            {
                var mine = units.Where(u => u.alive && u.team == team && !(u.squad && player != null && player.alive) && !u.manual && u != player && u.rout <= 0).ToList();
                if (team == att)
                {
                    if (act >= pts.Count) continue; var tp = pts[act]; bool hot = PointHot(tp, team); var sp = M.spawn[att];
                    if (hot && state != GState.Menu) AiSmoke(team, tp);
                    float ux = sp.x - tp.x, uz = sp.z - tp.z, ul = Mathf.Max(.01f, FU.Hyp(ux, uz)); ux /= ul; uz /= ul; float px = -uz, pz = ux;
                    foreach (var u in mine)
                    {
                        if (u.order != null || u.focus != null) continue; if (u.idle < 1.2f && !fresh.Contains(u)) continue;
                        string kind = u.def.kind; float jt = tp.inside ? 3 : 12; var dest = new Vector2(tp.x + FU.Rand(-jt, jt), tp.z + FU.Rand(-jt, jt));
                        if (u.type == "sniper") { var s = Standoff(u, tp); if (s == null) { u.idle = 0; continue; } dest = s.Value; }
                        else if ((kind == "tank" || kind == "gun" || u.type == "mg") && hot) { var s = Standoff(u, tp); if (s == null) { u.idle = 0; continue; } dest = s.Value + new Vector2(FU.Rand(-5, 5), FU.Rand(-5, 5)); }
                        else if (u.type == "rifle" && u.id % 4 == 0 && FU.Hyp(u.x - tp.x, u.z - tp.z) > 70)
                        {
                            float side = u.id % 8 == 0 ? 1 : -1; u.order = new Order { x = tp.x + ux * 40 + px * side * 44, z = tp.z + uz * 40 + pz * side * 44 };
                            u.queue.Clear(); u.queue.Add(new Order { x = dest.x, z = dest.y, cx = tp.x, cz = tp.z }); u.idle = 0; u.leash = 40; continue;
                        }
                        u.order = new Order { x = dest.x, z = dest.y }; if (FU.Hyp(dest.x - tp.x, dest.y - tp.z) <= tp.r + 13) { u.order.cx = tp.x; u.order.cz = tp.z; } u.queue.Clear(); u.idle = 0; u.leash = 40;
                    }
                }
                else
                {
                    var tg = new List<CapPoint>(); if (act < pts.Count) tg.Add(pts[act]); if (act + 1 < pts.Count) tg.Add(pts[act + 1]); if (tg.Count == 0) continue;
                    foreach (var u in mine)
                    {
                        if (u.order != null || u.focus != null || u.def.kind == "gun" || u.type == "sniper") continue; if (u.target != null && FU.Hyp(u.x - u.target.x, u.z - u.target.z) < u.def.range) continue;
                        CapPoint best = null; float bd = 1e9f; foreach (var p in tg) { float d = FU.Hyp(u.x - p.x, u.z - p.z); if (d < bd) { bd = d; best = p; } }
                        if (bd > 40 && (u.idle > 1.5f || fresh.Contains(u))) { float jd = best.inside ? 3 : 10; u.order = new Order { x = best.x + FU.Rand(-jd, jd), z = best.z + FU.Rand(-jd, jd), cx = best.x, cz = best.z }; u.queue.Clear(); u.leash = 35; u.idle = 0; }
                    }
                }
            }
            if (state != GState.Menu && t >= aiSup) { AiSupport(); aiSup = t + FU.Rand(90, 130); }
        }
        List<Unit> SpawnWave(int team, FWave w)
        {
            var o = new List<Unit>(); var b = SpawnPos(team);
            if (state == GState.Menu && units.Count(u => u.alive) > 150) return o;
            foreach (var c in w.comp) { if (!Defs.BASE.ContainsKey(c.type) || (team == 0 && c.type == "cav")) continue; for (int i = 0; i < c.n; i++) { var p = FreeSpot(b.x, b.z, 14, 1); o.Add(SpawnUnit(team, c.type, p.x, p.y, FaceEnemy(team))); spawned[team]++; } }
            if (team == EN) Msg(team == M.attacker ? "Enemy reinforcements are arriving." : "Enemy reserves moving up.");
            return o;
        }
        Vector2? Standoff(Unit u, CapPoint p)
        {
            float so = u.type == "sniper" ? 125 : u.type == "mg" ? 46 : u.type == "at" ? 62 : 55, dx = u.x - p.x, dz = u.z - p.z, l = Mathf.Max(.01f, FU.Hyp(dx, dz));
            if (l < so + 8) return null; return new Vector2(p.x + dx / l * so, p.z + dz / l * so);
        }
        bool PointHot(CapPoint p, int team) => units.Any(e => e.alive && e.team != team && FU.Hyp(e.x - p.x, e.z - p.z) < 38);

        void Raids(float dt)
        {
            raidT -= dt; if (raidT > 0) return; raidT = FU.Rand(85, 130); int a = ActiveIdx(); if (a >= World.points.Count) return; var tp = World.points[a];
            CallSupport(M.attacker, tp.x + FU.Rand(-10, 10), tp.z + FU.Rand(-10, 10), true);
            Msg(M.attacker == ME ? "Preparatory bombardment on " + tp.n + "!" : "Enemy bombardment on " + tp.n + "!");
        }

        // ---------------- support: Stukas (Germans) / artillery (Poles) — port of callSupport ----------------
        void CallSupport(int team, float x, float z, bool free)
        {
            if (!free) supportCd = 90;
            float gz = World.H(x, z);
            var ring = new GameObject("StrikeMarker"); ring.transform.position = new Vector3(x, gz + .4f, z); float rs = team == 0 ? 14 : 15; ring.transform.localScale = new Vector3(rs, 1, rs);
            ring.AddComponent<MeshFilter>().sharedMesh = World.RingMesh(.9f, 1, 48); ring.AddComponent<MeshRenderer>().sharedMaterial = Mats.Transparent(new Color(1, .2f, .13f, .8f)); markers.Add(new Marker { t = ring.transform, life = 3.4f });
            for (int i = 0; i < 6; i++) fx.Puff(new Vector3(x + FU.Rand(-3, 3), gz + .5f, z + FU.Rand(-3, 3)), new Color(1, .2f, .13f, .6f), 1, 3, 3.2f, Vector3.up * 6);
            if (team == EN) Msg(team == 0 ? "⚠ STUKAS INBOUND — clear the marked area!" : "⚠ ARTILLERY BARRAGE — clear the marked area!"); else Msg(team == 0 ? "Stukas inbound." : "Barrage called in.");
            if (team == 0)
            {
                audio.Shot("siren", new Vector3(x, gz + 40, z)); var sp = M.spawn[0]; float dx = x - sp.x, dz = z - sp.z, l = Mathf.Max(.01f, FU.Hyp(dx, dz)); dx /= l; dz /= l;
                foreach (float lat in new[] { -7f, 7f }) SpawnPlane(team, x, z, dx, dz, lat, 90, true, 9);
            }
            else for (int k = 0; k < 14; k++) shells.Add(new Shell { at = t + 3 + k * .45f + FU.Rand(0, .3f), p = new Vector3(x + FU.Rand(-14, 14), 0, z + FU.Rand(-14, 14)), r = 6, dmg = 80, team = -1 });
        }
        void SpawnPlane(int team, float x, float z, float dx, float dz, float lat, float speed, bool strike, float life, float y = 0)
        {
            var go = ModelLib.Spawn("stuka", null, false); var pl = new Plane { t = go.transform, prop = ModelLib.Find(go.transform, "prop"), t0 = t + (strike ? 3 : 0), speed = speed, lat = lat, life = life, x = x, z = z, dx = dx, dz = dz, y = y, strike = strike, dropped = new bool[5], team = team };
            go.transform.rotation = Quaternion.LookRotation(new Vector3(dx, 0, dz)); planes.Add(pl);
        }
        void UpdatePlanes(float dt)
        {
            float[] bombs = { -16, -8, 0, 8, 16 };
            for (int i = planes.Count - 1; i >= 0; i--)
            {
                var p = planes[i]; p.life -= dt; float s = (t - p.t0) * p.speed;
                float px = p.x + p.dx * s - p.dz * p.lat, pz = p.z + p.dz * s + p.dx * p.lat;
                float y = p.strike ? World.H(px, pz) + 40 + Mathf.Max(0, s) * .25f : p.y;
                p.t.position = new Vector3(px, y, pz); p.t.rotation = Quaternion.LookRotation(new Vector3(p.dx, p.strike ? (s < 0 ? -.12f : .2f) : 0, p.dz));
                if (p.prop) p.prop.Rotate(0, 0, dt * 2300, Space.Self);
                if (p.strike) for (int b = 0; b < 5; b++) if (!p.dropped[b] && s >= bombs[b]) { p.dropped[b] = true; float bx = p.x + p.dx * bombs[b] - p.dz * p.lat, bz = p.z + p.dz * bombs[b] + p.dx * p.lat; Combat.Explode(this, new Vector3(bx, World.H(bx, bz), bz), 7, 110, -1, null, null); }
                if (p.life <= 0) { Destroy(p.t.gameObject); planes.RemoveAt(i); }
            }
        }
        void UpdateShells() { for (int i = shells.Count - 1; i >= 0; i--) { var s = shells[i]; if (t >= s.at) { shells.RemoveAt(i); Combat.Explode(this, new Vector3(s.p.x, World.H(s.p.x, s.p.z), s.p.z), s.r, s.dmg, s.team, null, null); } } }
        void UpdateMarkers(float dt) { for (int i = markers.Count - 1; i >= 0; i--) { var m = markers[i]; m.life -= dt; if (m.t) m.t.gameObject.SetActive(Mathf.Sin(m.life * 12) > -.3f); if (m.life <= 0) { Destroy(m.t.gameObject); markers.RemoveAt(i); } } }
        void AiSupport()
        {
            var mine = units.Where(u => u.alive && u.team == ME).ToList(); if (mine.Count == 0) return; Unit best = null; int bs = -1;
            for (int i = 0; i < 10; i++) { var c = mine[Random.Range(0, mine.Count)]; int s = mine.Count(o => FU.Hyp(o.x - c.x, o.z - c.z) < 12); if (s > bs) { bs = s; best = c; } }
            if (best != null) CallSupport(EN, best.x, best.z, true);
        }
        void FpsSupport()
        {
            if (supportCd > 0) { Msg("Support not ready (" + Mathf.CeilToInt(supportCd) + " s)."); return; }
            if (Physics.Raycast(cam.transform.position, cam.transform.forward, out var h, 400, ~0, QueryTriggerInteraction.Ignore)) { if (player != null && FU.Hyp(h.point.x - player.x, h.point.z - player.z) < 32) { Msg("Too close — aim further away (friendly fire!)."); return; } CallSupport(ME, h.point.x, h.point.z, false); }
            else Msg("No target — aim at the ground.");
        }

        // ambient war: battleship, distant barrage, flybys, fires, time of day
        void UpdateShipAndSky(float dt)
        {
            if (World.ship) { shipT -= dt; if (shipT <= 0) { shipT = FU.Rand(7, 14); foreach (var g in World.shipGuns) { var m = g.TransformPoint(new Vector3(0, .3f, 11)); fx.Puff(m, new Color(1, .75f, .38f, 1), 3, 7, .3f, Vector3.zero, true); fx.Puff(m + Vector3.up, new Color(.33f, .33f, .33f, .7f), 3, 12, 3, new Vector3(-2, 2, 0)); } audio.Shot("boom", World.ship.position); StartCoroutine(Later(2.2f, FakeBoom)); } }
            barT -= dt; if (barT <= 0 && barrageLeft > 0) { barrageLeft--; barT = FU.Rand(1.2f, 3.2f); FakeBoom(); }
            flyT -= dt;
            if (flyT <= 0)
            {
                flyT = FU.Rand(45, 75); float dir = M.spawn[1 - M.attacker].z > M.spawn[M.attacker].z ? 1 : -1;
                SpawnPlane(0, FU.Rand(-80, 80), -dir * 330, 0, dir, 0, 95, false, 8, 85); audio.Shot("siren", cam.transform.position + Vector3.up * 80, .5f);
            }
            todT -= dt; if (todT <= 0) { todT = 1; vis.Tod(t); if (rts) vis.RtsFog(true); }
        }
        System.Collections.IEnumerator Later(float s, System.Action a) { yield return new WaitForSeconds(s); a(); }
        void FakeBoom()
        {
            if (World.points.Count == 0) return; var p = World.points[Random.Range(0, World.points.Count)]; float a = Random.value * 6.28f, d = 22 + Random.value * 40, x = p.x + Mathf.Cos(a) * d, z = p.z + Mathf.Sin(a) * d;
            if (Mathf.Abs(x) > M.half || Mathf.Abs(z) > M.half) return; fx.Explosion(new Vector3(x, World.H(x, z), z), FU.Rand(3, 5.5f)); audio.Shot("boom", new Vector3(x, World.H(x, z), z));
        }
        void UpdateFires(float dt)
        {
            fireT -= dt; if (fireT > 0) return; fireT = .22f;
            for (int i = fires.Count - 1; i >= 0; i--) { var f = fires[i]; if (f.b != null && f.b.dead) { fires.RemoveAt(i); continue; } if (f.life > 0) { f.life -= .22f; if (f.life <= 0) { fires.RemoveAt(i); continue; } } if ((f.p - cam.transform.position).sqrMagnitude < 500 * 500) fx.Fire(f.p, f.big); }
        }

        // ---------------- capture / end (port of updateCapture incl. Final Push / checkEnd) ----------------
        void UpdateCapture(float dt, bool menu)
        {
            int att = M.attacker, act = ActiveIdx(); var pts = World.points;
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i]; int a = 0, d = 0;
                foreach (var u in units) { if (!u.alive) continue; if (FU.Hyp(u.x - p.x, u.z - p.z) < p.r) { if (u.team == att) a++; else d++; } }
                p.g = a; p.e = d;
                if (!p.locked && i == act)
                {
                    int diff = a - d; float boost = p.pushT > 0 ? 2.2f : 1; p.pushT -= dt;
                    if (p.ap > p.best + .005f) { p.best = p.ap; p.stallT = 0; } else p.stallT += dt;
                    if (!menu && p.stallT > 75 && !(p.pushT > 0))
                    {
                        p.stallT = 0; p.pushT = 40;
                        foreach (var u in units.Where(u => u.alive && u.team == att && !u.manual && u != player && u.rout <= 0 && u.def.kind != "gun")) { u.order = new Order { x = p.x + FU.Rand(-9, 9), z = p.z + FU.Rand(-9, 9), cx = p.x, cz = p.z }; u.queue.Clear(); u.leash = 60; u.idle = 0; }
                        tix[att] += 8; SpawnWave(att, M.waves[Mathf.Min(att, M.waves.Length - 1)]);
                        ShowBanner("FINAL PUSH", att == ME ? "Reinforcements surge — take " + p.n : "The enemy is surging on " + p.n, att == ME ? UI.Green : UI.Red);
                    }
                    // RO2 Territory: attackers alone capture (faster with more men); outnumbering defenders captures slowly;
                    // an even or losing contest pauses it; defenders alone roll it back; an empty zone holds its progress
                    float depth = 1 - .08f * i;   // later positions are dug in deeper: each one takes longer to take
                    if (a > 0 && d == 0) p.ap = Mathf.Min(1, p.ap + Mathf.Min(6, a) * .07f * boost * depth * dt);
                    else if (a > d) p.ap = Mathf.Min(1, p.ap + Mathf.Min(6, diff) * .025f * boost * depth * dt);
                    else if (a == 0 && d > 0 && p.ap > 0) p.ap = Mathf.Max(0, p.ap - Mathf.Min(4, d) * .04f * dt);
                }
                if (!p.locked && p.ap >= 1)
                {
                    p.locked = true; p.owner = att; tix[att] += 15;
                    foreach (var cu in units) if (cu.alive && cu.team == att && FU.Hyp(cu.x - p.x, cu.z - p.z) < p.r) { cu.score += 50; if (cu == player) Msg("+50 objective captured"); }
                    if (menu) { if (pts.All(q => q.locked)) foreach (var q in pts) { q.locked = false; q.ap = 0; q.owner = 1 - att; } }
                    else { ShowBanner(att == ME ? "POSITION CAPTURED" : "POSITION LOST", p.n, att == ME ? UI.Green : UI.Red); if (att == ME) cp += 25; Msg(p.n + (att == ME ? " captured! +15 reinforcements." : " has fallen to the enemy.")); }
                }
                Color own = p.owner == ME ? new Color(.33f, .93f, .33f) : new Color(.93f, .27f, .2f);
                p.cloth.sharedMaterial = Mats.Unlit(own);
                p.ring.sharedMaterial = Mats.Transparent(p.locked ? new Color(own.r, own.g, own.b, .55f) : i == act ? new Color(1, .85f, .4f, .6f) : new Color(.6f, .6f, .6f, .25f));
            }
        }
        void CheckEnd()
        {
            if (range != null || (state != GState.Playing && state != GState.Dead)) return; int att = M.attacker, def = 1 - att;
            if (World.points.All(p => p.locked)) { EndGame(att, att == ME ? "All 5 objectives captured!" : "The enemy has captured every objective."); return; }
            if (tix[att] <= 0) { EndGame(def, def == ME ? "The attackers have run out of reinforcements." : "Your reinforcements are exhausted."); return; }
            if (t >= M.time) EndGame(def, def == ME ? "You held out until the clock ran down." : "Time ran out before the objectives were taken.");
        }
        void EndGame(int winner, string why)
        {
            state = GState.End; won = winner == ME; endTitle = won ? "VICTORY" : "DEFEAT"; endWhy = why;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true; if (vm) vm.gameObject.SetActive(false);
        }

        // ---------------- pickups / streaks / resupply (new in this version of the game) ----------------
        public void DropPickup(float x, float z)
        {
            if (state == GState.Menu || pickups.Count >= 12) return; bool med = Random.value < .4f;
            var g = new GameObject(med ? "Medkit" : "Ammo").transform;
            void B(Vector3 p, Vector3 s, Color c) { var b = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(b.GetComponent<Collider>()); b.transform.SetParent(g, false); b.transform.localPosition = p; b.transform.localScale = s; b.GetComponent<Renderer>().sharedMaterial = Mats.Lit(c); }
            B(new Vector3(0, .3f, 0), new Vector3(.7f, .45f, .5f), med ? FU.Hex(0xf2f2f2) : FU.Hex(0x5a6a3a));
            if (med) { B(new Vector3(0, .56f, 0), new Vector3(.5f, .08f, .12f), FU.Hex(0xc82020)); B(new Vector3(0, .56f, 0), new Vector3(.12f, .08f, .5f), FU.Hex(0xc82020)); } else B(new Vector3(0, .45f, 0), new Vector3(.72f, .1f, .52f), FU.Hex(0xcaa83a));
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(beam.GetComponent<Collider>()); beam.transform.SetParent(g, false); beam.transform.localPosition = new Vector3(0, 2.6f, 0); beam.transform.localScale = new Vector3(.16f, 2.5f, .16f);
            var br = beam.GetComponent<Renderer>(); br.sharedMaterial = Mats.Transparent(med ? new Color(1, .35f, .35f, .45f) : new Color(.48f, 1, .48f, .45f), null, true); br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            g.position = new Vector3(x, World.H(x, z) + .1f, z); pickups.Add(new Pickup { x = x, z = z, med = med, g = g, t = 45 });
        }
        void UpdatePickups(float dt)
        {
            for (int i = pickups.Count - 1; i >= 0; i--)
            {
                var k = pickups[i]; k.t -= dt; k.g.Rotate(0, dt * 86, 0); k.g.position = new Vector3(k.x, World.H(k.x, k.z) + .15f + Mathf.Sin(t * 3 + i) * .06f, k.z);
                bool take = false;
                if (player != null && player.alive && player.def.kind == "inf" && FU.Hyp(player.x - k.x, player.z - k.z) < 1.9f)
                {
                    take = true; audio.Shot("pickup", cam.transform.position);
                    if (k.med) { player.hp = Mathf.Min(player.max, player.hp + 45); player.bandages = Mathf.Max(player.bandages, 2); Msg("+ Medical kit: health restored"); } else { FullResupply(player); Msg("+ Ammo crate: ammo and grenades restocked"); }
                }
                if (take || k.t <= 0) { Destroy(k.g.gameObject); pickups.RemoveAt(i); }
            }
        }
        void FullResupply(Unit u)
        {
            if (u.weps == null) return; u.Save(); foreach (var id in u.weps) { var w = Defs.WEAPONS[id]; if (w.clip <= 0) continue; var s = u.ws[id]; u.ws[id] = new Vector2Int(s.x, w.reserve); }
            int clip = u.clip; u.Sync(); u.clip = clip; u.gren = Mathf.Max(u.gren, Defs.CLASSES[classIdx].gren); u.smoke = Mathf.Max(u.smoke, Defs.CLASSES[classIdx].smoke);
        }
        void Resupply(Unit u)
        {
            if (u.weps == null) return; var sp = SpawnPos(ME); if (FU.Hyp(u.x - sp.x, u.z - sp.z) > 16) return; bool any = false; u.Save();
            foreach (var id in u.weps) { var w = Defs.WEAPONS[id]; if (w.clip <= 0) continue; var s = u.ws[id]; if (s.y < w.reserve * .98f) { u.ws[id] = new Vector2Int(s.x, w.reserve); any = true; } }
            int clip = u.clip; float rl = u.reload; u.Sync(); u.clip = clip; u.reload = rl; int g = Defs.CLASSES[classIdx].gren; if (u.gren < g) { u.gren = g; any = true; } if (u.bandages < 2) { u.bandages = 2; any = true; } int sm = Defs.CLASSES[classIdx].smoke; if (u.smoke < sm) { u.smoke = sm; any = true; } if (any) Msg("Resupplied at the ammo crates.");
        }
        public void OnPlayerKill(Unit tgt, bool head)
        {
            streakT = 6; streak++; int s = streak; cp += 2;
            if (s == 2) ShowBanner("DOUBLE KILL", "+2 CP", UI.Gold);
            else if (s == 3) { ShowBanner("TRIPLE KILL — ADRENALINE", "health +30, stamina restored", FU.Hex(0xff9a4a)); if (player != null) { player.hp = Mathf.Min(player.max, player.hp + 30); player.stam = 1; } }
            else if (s == 5) { ShowBanner("RAMPAGE!", "free grenades +2, +10 CP", FU.Hex(0xff5a4a)); if (player != null) player.gren += 2; cp += 10; }
            else if (s > 5 && s % 2 == 1) { ShowBanner("UNSTOPPABLE ×" + s, "+5 CP", FU.Hex(0xff5a4a)); cp += 5; }
            else if (head && s == 1) ShowBanner("HEADSHOT", "", FU.Hex(0xffe27a));
            if (tgt.def.kind == "tank") ShowBanner("TANK DESTROYED", "+15 CP", FU.Hex(0xff9a4a));
        }

        // ---------------- first person (port of enterFps / updatePlayer / playerFire) ----------------
        void EnterFps(Unit u)
        {
            if (player != null && player != u) ReleasePlayer();
            foreach (var su in selected) if (su.ring) su.ring.SetActive(false); selected.Clear();
            player = lastPlayer = u; u.order = null; u.queue.Clear(); u.follow = false; u.squad = false; u.focus = null; u.target = null; u.manual = false;
            if (u.weps == null) u.InitWeapons(u.officer ? Defs.OfficerLoadout(u.team) : Defs.Loadout(u.team, u.type));
            u.SetRenderers(false); u.ring.SetActive(false);
            if (u.sv) { u.SetRenderers(true); u.sv.SetFirstPerson(!tp, cam.transform); }   // own body + gun replace the viewmodel
            foreach (var c in u.go.GetComponentsInChildren<Collider>()) c.enabled = false;
            yaw = u.heading; pitch = 0; ads = 0; recoil = 0; rts = false; state = GState.Playing;
            BuildViewModel(); Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; vis.Tod(t);
        }
        void ReleasePlayer()
        {
            if (player == null) return; var u = player; player = null; if (u.sv) u.sv.SetFirstPerson(false, null);
            if (u.alive) { u.SetRenderers(true); u.ring.SetActive(selected.Contains(u)); foreach (var c in u.go.GetComponentsInChildren<Collider>()) c.enabled = true; u.home = new Vector2(u.x, u.z); u.manual = true; }
            if (vm) vm.gameObject.SetActive(false);
        }
        public void BuildViewModel()
        {
            var w = player.Weapon; string kind = w == null ? "none" : w.vm;
            if (player.sv && w != null && !w.melee) { if (vm) Destroy(vm.gameObject); vm = null; vmKind = ""; return; }   // POLYGON body/gun is the viewmodel
            if (vm && vmKind == player.team + kind) { vm.gameObject.SetActive(true); return; }
            if (vm) Destroy(vm.gameObject); vm = null; vmKind = player.team + kind;
            if (w == null || kind == "none" || !ModelLib.Has("vm_" + player.team + "_" + kind)) return;
            vm = ModelLib.Spawn("vm_" + player.team + "_" + kind, cam.transform, false).transform; vm.localPosition = Vector3.zero; vm.localRotation = Quaternion.identity;
            vmFist = null;
            if (w.melee && vm.childCount > 0)
            {   // split the blade + grip + hand off the forearm onto a wrist pivot, so the blade can be held upright and swung
                var box = vm.GetChild(0); float gz = kind == "sabre" ? .24f : .28f;
                vmFist = new GameObject("fist").transform; vmFist.SetParent(box, false); vmFist.localPosition = new Vector3(0, -.01f, gz);
                var parts = new List<Transform>(); foreach (Transform c in box) if (c != vmFist && (c.localScale.z < .38f || c.localPosition.z > .3f)) parts.Add(c);
                foreach (var c in parts) if (c != vmFist) c.SetParent(vmFist, true);
            }
        }
        Transform vmFist;

        void UpdatePlayer(float dt, Keyboard kb, Mouse ms)
        {
            var u = player; var w = u.Weapon; bool foot = u.def.kind != "tank" && u.def.kind != "gun";
            var md = ms.delta.ReadValue() * .0022f * Mathf.Lerp(1, fov / 70f, .85f);
            yaw += md.x; pitch = Mathf.Clamp(pitch + md.y, -1.45f, 1.45f);
            if (u.def.kind == "inf") { if (kb.cKey.wasPressedThisFrame) u.stance = u.stance == 1 ? 0 : 1; if (kb.zKey.wasPressedThisFrame) u.stance = u.stance == 2 ? 0 : 2; } else u.stance = 0;
            int st = u.stance;
            float f = 0, s = 0; if (kb.wKey.isPressed) f++; if (kb.sKey.isPressed) f--; if (kb.dKey.isPressed) s++; if (kb.aKey.isPressed) s--;
            float fx = Mathf.Sin(yaw), fz = Mathf.Cos(yaw), rx = Mathf.Cos(yaw), rz = -Mathf.Sin(yaw);
            float mx = fx * f + rx * s, mz = fz * f + rz * s, len = FU.Hyp(mx, mz); u.moving = len > 0;
            bool wantSpr = kb.leftShiftKey.isPressed && f > 0 && st == 0 && ads < .3f && u.bandaging <= 0; u.sprinting = wantSpr && u.stam > (u.spr ? .02f : .2f); u.spr = u.sprinting;
            if (u.sprinting) u.stam = Mathf.Max(0, u.stam - dt * (foot ? .16f : .08f)); else u.stam = Mathf.Min(1, u.stam + dt * (len > 0 ? .12f : .25f));
            if (len > 0)
            {
                mx /= len; mz /= len; float sp = u.def.speed * (u.sprinting ? Defs.SprintMul(u.def.kind) : 1) * SPD[st] * (u.bandaging > 0 ? .4f : 1) * (1 - ads * .45f) * (1 - Mathf.Clamp(World.Slope(u.x, u.z) - .25f, 0, .4f)), ox = u.x, oz = u.z;
                u.x += mx * sp * dt; u.z += mz * sp * dt; u.Collide(units, u); if (World.Blocked(u.x, u.z)) { u.x = ox; u.z = oz; }
                if (u.def.kind == "tank") u.heading += Mathf.Clamp(FU.AngDiff(u.heading, Mathf.Atan2(mx, mz)), -dt * 2, dt * 2);
                bob += dt * (u.sprinting ? 12 : st > 0 ? 5 : 8) * sp / u.def.speed;
            }
            if (foot) u.heading = yaw; u.turretH = yaw; u.y = Interiors.SurfY(u.x, u.z, u.y);
            u.cd -= dt; u.gcd -= dt; u.cd2 -= dt; u.supp = Mathf.Max(0, u.supp - dt * .25f); u.heat = Mathf.Max(0, u.heat - dt * (w != null && w.mg ? .06f : .5f)); if (u.overheated && u.heat < .35f) { u.overheated = false; Msg("Barrel has cooled."); }
            if (u.hp < 35 && u.bandaging <= 0 && u.bleed <= 0) u.hp = Mathf.Max(1, u.hp - dt * 1.1f);
            if (u.bandaging <= 0) { Combat.Bleed(this, u, dt, false); if (!u.alive) return; }
            if (kb.hKey.wasPressedThisFrame) { if (u.bandages > 0 && u.bandaging <= 0 && (u.hp < u.max || u.bleed > 0)) { u.bandaging = 1.8f; Msg("Bandaging…"); } else if (u.bandages <= 0) Msg("No bandages left — resupply at your spawn."); }
            if (u.bandaging > 0) { u.bandaging -= dt; if (u.bandaging <= 0) { u.hp = Mathf.Min(u.max, u.hp + 55); u.bleed = 0; u.bandages--; Msg("Bandaged — bleeding stopped."); } }
            if (kb.kKey.wasPressedThisFrame) ammoShowT = 3;   // check ammo (RO2): feel the magazine
            if (kb.tKey.wasPressedThisFrame) { tp = !tp; if (u.sv) { u.SetRenderers(true); u.sv.SetFirstPerson(!tp, cam.transform); } else u.SetRenderers(tp); Msg(tp ? "Third person" : "First person"); }
            if (kb.fKey.wasPressedThisFrame) { SwapControl(); return; }
            if (kb.nKey.wasPressedThisFrame) { buyOpen = !buyOpen; orderOpen = false; buyT = 8; }
            if (kb.vKey.wasPressedThisFrame) { orderOpen = !orderOpen; buyOpen = false; orderT = 6; }
            orderT -= dt; buyT -= dt; if (orderT <= 0) orderOpen = false; if (buyT <= 0) buyOpen = false;
            for (int i = 0; i < 6; i++) if (kb[Key.Digit1 + i].wasPressedThisFrame && (orderOpen || buyOpen))
                {
                    if (orderOpen) { orderOpen = false; switch (i) { case 0: SquadCommand("follow"); break; case 1: SquadCommand("move"); break; case 2: SquadCommand("cover"); break; case 3: SquadCommand("hold"); break; case 4: SquadCommand("order"); break; case 5: SquadCommand("move", true); break; } }
                    else { buyOpen = false; var l = Defs.BUY.Where(b => !(b.key == "cav" && ME == 0)).ToList(); if (i < l.Count) Buy(l[i], true); }
                    return;
                }
            gT += dt; if (gT > 40) { gT = 0; u.gren = Mathf.Min(4, u.gren + 1); }
            for (int i = 0; i < 3; i++) if (!orderOpen && !buyOpen && kb[Key.Digit1 + i].wasPressedThisFrame && u.weps != null && i < u.weps.Length && i != u.wi) { u.SwitchWeapon(i); BuildViewModel(); w = u.Weapon; Msg("▸ " + w.name); ammoShowT = 2; }
            float scroll = ms.scroll.ReadValue().y; if (Mathf.Abs(scroll) > .1f && u.weps != null && u.weps.Length > 1) { u.SwitchWeapon((u.wi + (scroll < 0 ? 1 : u.weps.Length - 1)) % u.weps.Length); BuildViewModel(); w = u.Weapon; Msg("▸ " + w.name); ammoShowT = 2; }
            if (w != null && w.clip > 0)
            {
                bool boltGun = w.vm == "rifle" || w.vm == "sniper";
                if (u.reload > 0)
                {
                    if (u.rkind == ReloadKind.Single && ms.leftButton.wasPressedThisFrame) u.reloadStop = true;   // fire interrupts single loading
                    u.reload -= dt;
                    if (u.reload <= 0)
                    {
                        if (u.rkind == ReloadKind.Single)
                        {   // one round pressed in; carry on until full, out of ammo or interrupted, then the bolt closes
                            if (u.reserve > 0) { u.clip++; u.reserve--; }
                            if (u.clip < w.clip && u.reserve > 0 && !u.reloadStop) u.reload = u.reloadDur;
                            else { u.reload = 0; u.rkind = ReloadKind.None; u.singlesActive = false; u.cd = Mathf.Max(u.cd, .45f); ammoShowT = 2.5f; }
                        }
                        else { int take = Mathf.Min(w.clip - u.clip, u.reserve); u.clip += take; u.reserve -= take; u.rkind = ReloadKind.None; ammoShowT = 2.5f; }
                    }
                }
                else if ((u.clip <= 0 && u.reserve > 0) || (kb.rKey.wasPressedThisFrame && u.clip < w.clip && u.reserve > 0))
                {
                    u.reloadStop = false;
                    // Kar98k: a full stripper clip when the magazine is empty, single rounds to top up (scoped rifles always load singly)
                    if (boltGun && (w.scope || u.clip > 0 || u.reserve < w.clip)) { u.rkind = ReloadKind.Single; u.singlesActive = true; u.reload = u.reloadDur = .62f; }
                    else { u.rkind = boltGun ? ReloadKind.Stripper : ReloadKind.Mag; u.reload = u.reloadDur = w.reload; }
                }
            }
            bool adsHeld = ms.rightButton.isPressed;
            ads += ((adsHeld && w != null && !w.melee && u.def.kind != "tank" && !u.sprinting && !(u.reload > 0) && u.bandaging <= 0 && u.barrelT <= 0 ? 1 : 0) - ads) * Mathf.Min(1, dt * 9);
            if (debugAds >= 0) ads = debugAds;   // CLI / test captures: force the aim amount
            if (ms.leftButton.wasPressedThisFrame) fireQueued = .25f; fireQueued = Mathf.Max(0, fireQueued - dt);
            bool firing = ms.leftButton.isPressed;
            if (w != null)
            {
                if (w.melee) { if (firing && u.cd <= 0 && !u.sprinting && u.bandaging <= 0) Melee(u, w); }
                else if (w.mg && (u.overheated || u.barrelT > 0)) { if (fireQueued > 0 && u.overheated && u.barrelT <= 0) { fireQueued = 0; Msg("Barrel too hot — [X] change barrel."); } }
                else if (!u.sprinting && !(u.reload > 0) && u.bandaging <= 0 && u.cd <= 0 && (u.clip > 0 || w.clip == 0) && (w.auto ? firing : fireQueued > 0)) { fireQueued = 0; PlayerFire(u, w, st); }
            }
            if (u.def.kind == "tank" && adsHeld && u.cd2 <= 0 && !u.sprinting) CoaxFire(u);
            if (kb.gKey.wasPressedThisFrame) { if (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) ThrowSmoke(u); else ThrowGrenade(u); }
            // RO2 barrel change: an MG that overheats can't fire until the crew swaps the barrel (a few seconds, gun down)
            if (w != null && w.mg && kb.xKey.wasPressedThisFrame && u.barrelT <= 0 && u.heat > .2f) { u.barrelT = 4.5f; Msg("Changing barrel…"); }
            if (u.barrelT > 0) { u.barrelT -= dt; if (u.barrelT <= 0) { u.heat = 0; u.overheated = false; Msg("Fresh barrel in."); } }
            mgRest = w != null && w.mg && !u.moving && foot && (st == 2 || (ads > .5f && RestedOnCover()));
            // breath holding (Shift while aiming, standing still) — port of fp.hold / fp.gasp
            if (ads > .5f && kb.leftShiftKey.isPressed && !u.moving && breath > 0 && !gasp) { hold = true; breath = Mathf.Max(0, breath - dt * .2f); if (breath <= 0) gasp = true; }
            else { hold = false; breath = Mathf.Min(1, breath + dt * .16f); if (breath > .4f) gasp = false; }
            lean += ((foot ? (kb.eKey.isPressed ? 1 : 0) - (kb.qKey.isPressed ? 1 : 0) : 0) - lean) * Mathf.Min(1, dt * 8);
            // camera
            swT += dt; float amp = foot ? (.0009f + (1 - u.stam) * .002f + u.supp * .003f + (u.moving ? .002f : 0) + (gasp ? .0015f : 0)) * (1 + (1 - ads) * 1.2f) * (st == 2 ? .4f : st == 1 ? .7f : 1) * (hold ? .15f : 1) * (mgRest ? .3f : 1) * (w != null && w.scope ? 2.2f : 1) * (1 - ads * .15f) : 0;
            float swx = (Mathf.Sin(swT * 1.3f) + Mathf.Sin(swT * 2.7f + 1.2f) * .6f) * amp, swy = (Mathf.Sin(swT * 1.1f + .7f) + Mathf.Sin(swT * 2.3f + 2.1f) * .6f) * amp;
            recoil *= Mathf.Max(0, 1 - dt * Mathf.Lerp(4.5f, 8, ads));   // slower recovery from the hip
            eye += ((foot ? (u.def.kind == "cav" ? 2.9f : STAND[st]) : u.def.kind == "tank" ? 2.9f : 1.5f) - eye) * Mathf.Min(1, dt * 7);
            float bb = foot && u.moving ? Mathf.Sin(bob) * .04f * (1 - ads) : 0, lo = lean * .38f;
            if (tp)
            {   // third person (port of the fp.tp camera): orbit behind the unit, pulled in by walls / terrain
                string kd = u.def.kind; float baseD = kd == "tank" ? 11 : kd == "gun" ? 7 : kd == "cav" ? 5.5f : 3.4f, dd = baseD * (1 - ads * .4f), side = (kd == "tank" || kd == "gun") ? 0 : .8f * (1 - ads * .5f), up = kd == "tank" ? 2 : kd == "gun" ? 1.4f : .3f;
                float pivY = foot ? (kd == "cav" ? 2.5f : STAND[st]) : (kd == "tank" ? 2.4f : 1.6f);
                tpPivot = new Vector3(u.x, u.y + pivY, u.z); var f3 = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch)); var rgt = new Vector3(Mathf.Cos(yaw), 0, -Mathf.Sin(yaw));
                var target = tpPivot - f3 * dd + rgt * side + Vector3.up * up;
                if (Physics.Linecast(tpPivot, target, out var hit, ~0, QueryTriggerInteraction.Ignore)) target = hit.point + (tpPivot - target).normalized * .3f;
                cam.transform.position = target; cam.transform.rotation = Quaternion.Euler((-pitch - recoil * .4f - swy) * Mathf.Rad2Deg, (yaw + swx) * Mathf.Rad2Deg, 0);
            }
            else
            {
                cam.transform.position = new Vector3(u.x + Mathf.Cos(yaw) * lo, u.y + eye + bb - Mathf.Abs(lean) * .08f, u.z - Mathf.Sin(yaw) * lo);
                cam.transform.rotation = Quaternion.Euler((-pitch - recoil * .6f - swy) * Mathf.Rad2Deg, (yaw + swx) * Mathf.Rad2Deg, -lean * .16f * Mathf.Rad2Deg);
            }
            if (shake > 0) cam.transform.position += Random.insideUnitSphere * shake * .12f;
            float tf = Mathf.Lerp(tp ? 64 : 72, w != null ? w.ads : 40, ads * (tp ? .45f : 1)) + (u.sprinting ? 5 : 0); fov = tf; cam.fieldOfView = tf;
            if (u.sv)
            {   // the unit's own POLYGON model is the viewmodel / third-person body: feed it the camera aim
                u.sv.ads = ads; u.sv.hasAim = true; u.sv.aimPoint = cam.transform.position + cam.transform.forward * 80;
                u.sv.raise = ads > .3f || Time.time - u.lastShot < .8f; u.sv.hideFP = w != null && ((w.scope && ads > .85f) || w.melee) && !tp;
                if (u.sv.firstPerson == tp) u.sv.SetFirstPerson(!tp, cam.transform);
            }
            if (vm && w != null)
            {
                bool scoped = w.scope && ads > .85f && !tp; vm.gameObject.SetActive(!scoped && !tp);
                var hp = HIP.TryGetValue(w.vm, out var h) ? h : HIP["rifle"]; float a = ads, rl = u.reload > 0 ? 1 : 0, sprint = u.sprinting ? 1 : 0, mv = u.moving ? 1 : 0;
                var hip = new Vector3(hp.x, hp.y, -hp.z);
                vm.localPosition = Vector3.Lerp(hip, Vector3.zero, a) + new Vector3(mv * Mathf.Sin(bob * .5f) * .008f * (1 - a) - sprint * .05f, -rl * .22f - sprint * .07f + mv * Mathf.Abs(Mathf.Sin(bob)) * .012f * (1 - a), -recoil * .07f);
                float rxv = recoil * .22f + rl * .5f + sprint * .35f, ryv = Mathf.Lerp(hp.w, 0, a) + sprint * .45f, rzv = -lean * .12f + (w.melee ? recoil * .8f : 0);
                vm.localRotation = Quaternion.AngleAxis(-rxv * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(-ryv * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(rzv * Mathf.Rad2Deg, Vector3.forward);
                if (w.melee) MeleePose(w, mv, sprint, dt);
            }
        }

        void PlayerFire(Unit u, WeaponDef w, int st)
        {
            bool cannon = u.def.pen; float spread = (w.spread > 0 ? w.spread : .005f) + (w.mg ? u.heat * .012f : 0); spread *= u.moving ? 1.8f : 1; spread *= Mathf.Lerp(w.vm == "pistol" || w.vm == "smg" ? 2.6f : 3.4f, 1, ads);   // RO2: hip fire is a last resort
            spread *= st == 2 ? .5f : st == 1 ? .75f : 1; spread *= 1 + u.supp * 1.5f;
            if (w.mg) spread *= mgRest ? .35f : 2.2f;   // RO2 bipod: an MG is only accurate deployed (prone or rested on cover)
            if (w.clip > 0) u.clip--; u.cd = cannon ? u.def.rof : w.rof; u.lastShot = Time.time;
            var dir = (cam.transform.rotation * new Vector3(FU.Rand(-spread, spread), FU.Rand(-spread, spread), 1)).normalized; var o = cam.transform.position;
            float range = w.range > 0 ? w.range : u.def.range * 2.2f;
            var (dist, hitU, head) = CastRay(o, dir, range, u.team);
            Vector3 muzzle;
            if (tp) { var A = o + dir * dist; var d2 = (A - tpPivot); float L = d2.magnitude; dir = d2 / Mathf.Max(.01f, L); o = tpPivot + dir * .6f; (dist, hitU, head) = CastRay(o, dir, Mathf.Max(2, L + 6), u.team); muzzle = tpPivot + dir * 1.3f; }
            else muzzle = vm && vm.gameObject.activeSelf ? vm.TransformPoint(new Vector3(0, -.02f, w.vm == "pistol" ? .38f : w.vm == "smg" ? .75f : 1.0f)) : o + cam.transform.forward * .9f;
            var end = o + dir * dist;
            fx.Tracer(muzzle, end); fx.Flash(muzzle, .1f); audio.Shot(cannon ? "cannon" : w.snd, o);
            float rc = (cannon ? .8f : w.recoil) * (w.mg ? (mgRest ? .35f : 1.7f) : 1); recoil = Mathf.Min(1, recoil + rc * (w.auto ? .35f : 1)); float kick = w.mg ? (mgRest ? .3f : 1.6f) : 1; pitch += (cannon ? .045f : w.auto ? .005f : .024f) * (1.4f - ads * .75f) * kick; yaw += FU.Rand(-1, 1) * (w.auto ? .003f : .0025f) * (1.6f - ads * .6f) * kick;
            if (w.mg) { u.heat = Mathf.Min(1, u.heat + .016f); if (u.heat >= 1 && !u.overheated) { u.overheated = true; Msg("Barrel overheated! Press [X] to change the barrel."); } }
            if (cannon) { if (hitU != null) Combat.Damage(this, hitU, u.def.dmg * 1.1f, true, false, u); Combat.Explode(this, end, u.def.splash, u.def.dmg * .5f, u.team, hitU, u); shake = Mathf.Max(shake, .9f); }
            else if (hitU != null) { float dmg = w.dmg * Mathf.Lerp(1, w.fallMul, Mathf.Clamp01((dist - w.fallNear) / (w.fallFar - w.fallNear))); Combat.Bullet(this, hitU, dmg, castZone, u); }
            else { Combat.Suppress(this, end.x, end.z, u.team, .12f, 4); if (dist < range - .1f) fx.Dust(end); }
        }

        HitZone castZone;   // zone of the last unit CastRay hit
        (float, Unit, bool) CastRay(Vector3 o, Vector3 dir, float range, int team)
        {
            castZone = HitZone.Body;
            var hits = Physics.RaycastAll(o, dir, range, ~0, QueryTriggerInteraction.Collide); System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                var uh = h.collider.GetComponent<UnitHit>();
                if (uh != null) { if (!uh.u.alive || uh.u.team == team) continue; castZone = uh.zone; return (h.distance, uh.u, uh.head); }
                if (h.collider.isTrigger) continue;
                return (h.distance, null, false);
            }
            return (range, null, false);
        }

        /// Blade held upright in the fist; an attack is a diagonal slash from upper-right to lower-left (wind-up, cut, recover).
        float meleeT = -1;
        struct MPose { public Vector3 hand; public float pitch, yaw, roll; public MPose(Vector3 h, float p, float y, float r) { hand = h; pitch = p; yaw = y; roll = r; } }
        static MPose Lerp(MPose a, MPose b, float k) { k = k * k * (3 - 2 * k); return new MPose(Vector3.Lerp(a.hand, b.hand, k), Mathf.Lerp(a.pitch, b.pitch, k), Mathf.Lerp(a.yaw, b.yaw, k), Mathf.Lerp(a.roll, b.roll, k)); }
        void MeleePose(WeaponDef w, float mv, float sprint, float dt)
        {
            bool sab = w.vm == "sabre"; var hand = new Vector3(0, -.01f, sab ? .24f : .28f);   // grip point in the viewmodel (blade runs along +z from here)
            var rest = new MPose(new Vector3(.2f, sab ? -.3f : -.25f, .3f), -62, -6, 14);
            var wind = new MPose(new Vector3(.27f, -.13f, .22f), -84, -14, -24);
            var cut = new MPose(new Vector3(-.16f, -.3f, .38f), 20, -72, 0);
            MPose pose = rest; float dur = sab ? .52f : .42f;
            if (meleeT >= 0)
            {
                meleeT += dt; float p = meleeT / dur;
                pose = p < .26f ? Lerp(rest, wind, p / .26f) : p < .52f ? Lerp(wind, cut, (p - .26f) / .26f) : Lerp(cut, rest, Mathf.Clamp01((p - .52f) / .48f));
                if (p >= 1) meleeT = -1;
            }
            var bobO = new Vector3(mv * Mathf.Sin(bob * .5f) * .01f - sprint * .04f, mv * Mathf.Abs(Mathf.Sin(bob)) * .014f - sprint * .06f, 0);
            var q = Quaternion.AngleAxis(pose.roll - lean * 7, Vector3.forward) * Quaternion.AngleAxis(pose.yaw, Vector3.up) * Quaternion.AngleAxis(pose.pitch + sprint * 25, Vector3.right);
            if (vmFist)
            {   // forearm stays roughly level, reaching in from the lower right; only the wrist turns the blade
                var arm = Quaternion.AngleAxis(-lean * 7, Vector3.forward) * Quaternion.AngleAxis(-12 + (pose.hand.x - .2f) * 90, Vector3.up) * Quaternion.AngleAxis(-6 + (pose.hand.y + .25f) * 60, Vector3.right);
                vm.localRotation = arm; vm.localPosition = pose.hand + bobO - arm * hand; vmFist.localRotation = Quaternion.Inverse(arm) * q;
            }
            else { vm.localRotation = q; vm.localPosition = pose.hand + bobO - q * hand; }   // rotate about the fist, not the elbow
        }
        void Melee(Unit u, WeaponDef w)
        {
            u.cd = w.rof; meleeT = 0; if (u.sv) u.sv.Stab();
            var (d, hit, head) = CastRay(cam.transform.position, cam.transform.forward, w.reach + .3f, u.team);
            if (hit == null) foreach (var e in units) { if (!e.alive || e.team == u.team) continue; var to = e.Chest - cam.transform.position; if (to.magnitude < w.reach && Vector3.Angle(cam.transform.forward, to) < 25) { hit = e; break; } }
            if (hit != null && !hit.def.armor) Combat.Damage(this, hit, w.dmg, false, false, u);
            audio.Shot("rifle", u.Eye, .4f);
        }

        void ThrowGrenade(Unit u)
        {
            if (u.gren <= 0 || u.gcd > 0 || !u.Foot) return; u.gren--; u.gcd = .9f;
            var d = (cam.transform.rotation * new Vector3(0, .12f, 1)).normalized; var g = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(g.GetComponent<Collider>());
            g.transform.localScale = Vector3.one * .28f; g.GetComponent<Renderer>().sharedMaterial = Mats.Lit(0x2a3a22);
            grenades.Add(new Grenade { p = cam.transform.position + d * .8f, v = d * 18, fuse = 2.4f, t = g.transform, by = u });
        }
        // ---------------- smoke grenades (RO2: RDG-1 / Nebelhandgranate) ----------------
        class Smoke { public Vector3 p; public float life, age; public Transform t; public Vector3 v; public bool landed; }
        readonly List<Smoke> smokes = new List<Smoke>();
        float[] aiSmokeT = new float[2];
        const float SmokeLife = 38, SmokeR = 7.5f;
        void ThrowSmoke(Unit u)
        {
            if (u.smoke <= 0) { Msg("No smoke grenades (Officers and Assault carry them)."); return; }
            if (u.gcd > 0 || !u.Foot) return; u.smoke--; u.gcd = .9f;
            var d = (cam.transform.rotation * new Vector3(0, .12f, 1)).normalized; var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(g.GetComponent<Collider>());
            g.transform.localScale = new Vector3(.12f, .14f, .12f); g.GetComponent<Renderer>().sharedMaterial = Mats.Lit(0x5b5f55);
            smokes.Add(new Smoke { p = cam.transform.position + d * .8f, v = d * 16, life = SmokeLife + 2, t = g.transform }); Msg("Smoke out!");
        }
        /// AI attackers pop smoke between themselves and a defended objective before pushing in.
        void AiSmoke(int team, CapPoint tp)
        {
            if (t < aiSmokeT[team]) return; aiSmokeT[team] = t + FU.Rand(45, 80);
            Unit best = null; float bd = 1e9f;
            foreach (var u in units) { if (!u.alive || u.team != team || u == player || !u.Foot || u.rout > 0) continue; float d = FU.Hyp(u.x - tp.x, u.z - tp.z); if (d > 35 && d < 90 && d < bd) { bd = d; best = u; } }
            if (best == null) return; float k = .45f; var p = new Vector3(best.x + (tp.x - best.x) * k, 0, best.z + (tp.z - best.z) * k); p.y = World.H(p.x, p.z);
            smokes.Add(new Smoke { p = p, life = SmokeLife, landed = true });
            if (team == EN) Msg("The enemy is laying smoke!");
        }
        void UpdateSmoke(float dt)
        {
            World.smokes.Clear();
            for (int i = smokes.Count - 1; i >= 0; i--)
            {
                var s = smokes[i];
                if (!s.landed)
                {   // flies and bounces like a grenade, starts pouring smoke after a short fuse
                    s.v.y -= 22 * dt; var np = s.p + s.v * dt;
                    if (Physics.Linecast(s.p, np, out var hit, ~0, QueryTriggerInteraction.Ignore)) { np = hit.point + hit.normal * .15f; s.v = Vector3.Reflect(s.v, hit.normal) * .3f; }
                    float gy = World.H(np.x, np.z) + .1f; if (np.y < gy) { np.y = gy; s.v.y = -s.v.y * .3f; s.v.x *= .5f; s.v.z *= .5f; }
                    s.p = np; if (s.t) s.t.position = np;
                    if (SmokeLife + 2 - s.life > 1.6f) s.landed = true;
                }
                s.life -= dt; if (s.landed) s.age += dt;
                if (s.life <= 0) { if (s.t) Destroy(s.t.gameObject); smokes.RemoveAt(i); continue; }
                if (!s.landed) continue;
                // grows over ~4 s, holds, thins out over the last 8 s
                float r = SmokeR * Mathf.Clamp01(s.age / 4f) * Mathf.Clamp01(s.life / 8f);
                World.smokes.Add(new Vector4(s.p.x, s.p.y + 1.6f, s.p.z, r));
                if (Random.value < dt * 9)
                {
                    var o = Random.insideUnitSphere * r * .55f; o.y = Mathf.Abs(o.y) * .5f;
                    fx.Puff(s.p + o + Vector3.up * .5f, new Color(.78f, .78f, .76f, .55f), 2.5f, 7.5f, 4.5f, new Vector3(FU.Rand(-.4f, .4f), .5f, FU.Rand(-.4f, .4f)));
                }
            }
        }
        /// Bipod / rest check for MGs: something solid just under the muzzle, clear air at eye height.
        bool mgRest;
        bool RestedOnCover()
        {
            var f = cam.transform.forward; f.y = 0; if (f.sqrMagnitude < 1e-4f) return false; f.Normalize(); var e = cam.transform.position;
            bool low = Physics.Raycast(e - Vector3.up * .3f, f, 1.1f, ~0, QueryTriggerInteraction.Ignore);
            bool high = Physics.Raycast(e + Vector3.up * .05f, f, 1.1f, ~0, QueryTriggerInteraction.Ignore);
            return low && !high;
        }

        void UpdateGrenades(float dt)
        {
            for (int i = grenades.Count - 1; i >= 0; i--)
            {
                var g = grenades[i]; g.fuse -= dt; g.v.y -= 22 * dt; var np = g.p + g.v * dt;
                if (Physics.Linecast(g.p, np, out var hit, ~0, QueryTriggerInteraction.Ignore)) { np = hit.point + hit.normal * .15f; g.v = Vector3.Reflect(g.v, hit.normal) * .35f; }
                float gy = World.H(np.x, np.z) + .14f; if (np.y < gy) { np.y = gy; g.v.y = -g.v.y * .35f; g.v.x *= .6f; g.v.z *= .6f; }
                g.p = np; g.t.position = np;
                if (g.fuse <= 0) { Destroy(g.t.gameObject); grenades.RemoveAt(i); Combat.Explode(this, g.p, 6.5f, 120, g.by.team, null, g.by); }
            }
        }

        void SquadOrder()
        {
            var sq = units.Where(x => x.alive && x.team == ME && x != player && x.squad).ToList(); if (sq.Count == 0) { Msg("You have no squad."); return; }
            if (sq.Any(x => x.follow))
            {
                Vector3 p = Physics.Raycast(cam.transform.position, cam.transform.forward, out var h, 300, ~0, QueryTriggerInteraction.Ignore) ? h.point : player.Eye + cam.transform.forward * 30;
                int i = 0; foreach (var u in sq) { u.follow = false; u.order = new Order { x = p.x + ((i % 4) - 1.5f) * 3, z = p.z + (i / 4) * 3, cx = p.x, cz = p.z, force = true }; u.queue.Clear(); i++; }
                Msg("Squad: move to position.");
            }
            else { foreach (var u in sq) { u.follow = true; u.order = null; u.queue.Clear(); } Msg("Squad: follow me."); }
        }

        public void OnPlayerDeath()
        {
            state = GState.Dead; respT = range != null ? 3 : Mathf.Max(5, WaveTime - (t % WaveTime)); streak = 0;   // RO2: you come back with the next reinforcement wave
            if (vm) vm.gameObject.SetActive(false);
            if (player != null && player.go) { player.SetRenderers(true); if (player.sv) player.sv.SetFirstPerson(false, null); }
            Msg("You were killed. Choose a class and redeploy."); Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        void DeadCamera(float dt)
        {
            if (rts || lastPlayer == null) return; var p = new Vector3(lastPlayer.x, lastPlayer.y, lastPlayer.z);
            cam.transform.position = Vector3.Lerp(cam.transform.position, p + new Vector3(0, 9, -6), dt * 2); cam.transform.LookAt(p); cam.fieldOfView = 60;
        }
        void Deploy(int ci)
        {
            var c = Defs.CLASSES[ci]; var sp = RespawnPoint(); var q = FreeSpot(sp.x, sp.z, 10, .6f);
            player = null; lastClass = ci; breath = 1; tp = false;
            var u = new Unit { id = nextId++, team = ME, type = c.type, def = Defs.For(ME, c.type), x = q.x, z = q.y, heading = FaceEnemy(ME), turretH = FaceEnemy(ME) };
            u.y = World.H(q.x, q.y); u.hp = u.max = u.def.hp; u.home = q; u.prot = t + 3; u.InitWeapons(c.weps[ME]); u.gren = c.gren; u.smoke = c.smoke; u.BuildModel(unitRoot); units.Add(u);
            foreach (var o in units.Where(o => o.alive && o.team == ME && o != u && o.def.kind == "inf" && !o.squad && FU.Hyp(o.x - u.x, o.z - u.z) < 40).Take(4)) { o.squad = true; o.follow = true; o.slot = slotCounter++ % 12; o.manual = false; }
            rts = false; EnterFps(u);
        }

        // ---------------- RTS command map ----------------
        void EnterRts()
        {
            rts = true; if (player != null) { rtsPos = new Vector3(player.x, 0, player.z); rtsYaw = yaw; ReleasePlayer(); }
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true; vis.Tod(t); vis.RtsFog(true);
            Msg("Command map: LMB select · RMB move · Alt+RMB attack-move · Shift queue · 1-6 buy · B support · F take control · Tab back");
        }
        void LeaveRts()
        {
            var pick = selected.FirstOrDefault(u => u.alive && u.team == ME) ?? (lastPlayer != null && lastPlayer.alive ? lastPlayer : null);
            if (pick != null) { pick.manual = false; EnterFps(pick); } else { rts = false; state = GState.Dead; respT = 0; vis.Tod(t); }
        }
        void UpdateRts(float dt, Keyboard kb, Mouse ms)
        {
            float pan = rtsDist * .9f * dt; Vector3 fw = new Vector3(Mathf.Sin(rtsYaw), 0, Mathf.Cos(rtsYaw)), rt = new Vector3(fw.z, 0, -fw.x);
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) rtsPos += fw * pan; if (kb.sKey.isPressed || kb.downArrowKey.isPressed) rtsPos -= fw * pan;
            bool ctl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            if ((kb.dKey.isPressed && !ctl) || kb.rightArrowKey.isPressed) rtsPos += rt * pan; if ((kb.aKey.isPressed && !ctl) || kb.leftArrowKey.isPressed) rtsPos -= rt * pan;
            if (kb.qKey.isPressed) rtsYaw -= dt * 1.5f; if (kb.eKey.isPressed) rtsYaw += dt * 1.5f;
            float sc = ms.scroll.ReadValue().y; if (Mathf.Abs(sc) > .1f) rtsDist = Mathf.Clamp(rtsDist * (sc > 0 ? .88f : 1.14f), 20, 260);
            float H = M.half; rtsPos.x = Mathf.Clamp(rtsPos.x, -H, H); rtsPos.z = Mathf.Clamp(rtsPos.z, -H, H); rtsPos.y = World.H(rtsPos.x, rtsPos.z);
            var camTarget = rtsPos - fw * rtsDist * .55f + Vector3.up * rtsDist * .85f;
            cam.transform.position = Vector3.Lerp(cam.transform.position, camTarget, Mathf.Min(1, dt * 8)); cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(rtsPos - camTarget), Mathf.Min(1, dt * 8)); cam.fieldOfView = 60;
            for (int i = 0; i < Defs.BUY.Length; i++) if (kb[Key.Digit1 + i].wasPressedThisFrame) Buy(Defs.BUY[i]);
            if (kb.bKey.wasPressedThisFrame) ArmSupport();
            Vector2 mp = ms.position.ReadValue();
            if (ms.leftButton.wasPressedThisFrame && !OverHud(mp))
            {
                if (armSupport) { if (GroundAt(mp, out var gp)) { armSupport = false; CallSupport(ME, gp.x, gp.z, false); } return; }
                dragStart = mp; dragging = true;
            }
            if (ms.leftButton.wasReleasedThisFrame && dragging)
            {
                dragging = false; bool add = kb.leftShiftKey.isPressed; if (!add) { foreach (var u in selected) if (u.ring) u.ring.SetActive(false); selected.Clear(); }
                if ((mp - dragStart).magnitude < 6)
                {
                    var ray = cam.ScreenPointToRay(mp);
                    foreach (var h in Physics.RaycastAll(ray, 900, ~0, QueryTriggerInteraction.Collide).OrderBy(h => h.distance)) { var uh = h.collider.GetComponent<UnitHit>(); if (uh != null && uh.u.alive && uh.u.team == ME) { Select(uh.u); break; } if (!h.collider.isTrigger) break; }
                }
                else
                {
                    var r = Rect.MinMaxRect(Mathf.Min(dragStart.x, mp.x), Mathf.Min(dragStart.y, mp.y), Mathf.Max(dragStart.x, mp.x), Mathf.Max(dragStart.y, mp.y));
                    foreach (var u in units) { if (!u.alive || u.team != ME) continue; var sp = cam.WorldToScreenPoint(new Vector3(u.x, u.y + 1, u.z)); if (sp.z > 0 && r.Contains(sp)) Select(u); }
                }
            }
            var enC = ms.rightButton.wasPressedThisFrame && selected.Count > 0 && !kb.leftShiftKey.isPressed && !kb.leftCtrlKey.isPressed ? EnemyAtScreen(mp) : null;
            if (enC != null)
            {   // right-click an enemy: focus fire
                foreach (var u in selected) { if (!u.alive) continue; u.focus = enC; u.order = null; u.queue.Clear(); u.follow = false; u.manual = true; u.hide = null; }
                PingAt(enC.x, enC.z, FU.Hex(0xff3322)); Msg("Engage " + Defs.NAMES[enC.team][enC.type] + "!");
            }
            else if (ms.rightButton.wasPressedThisFrame && selected.Count > 0 && GroundAt(mp, out var tgt))
            {
                bool queue = kb.leftShiftKey.isPressed, attackMove = kb.leftAltKey.isPressed; int i = 0, n = selected.Count, cols = Mathf.CeilToInt(Mathf.Sqrt(n));
                foreach (var u in selected)
                {
                    if (!u.alive) continue; var o = new Order { x = tgt.x + ((i % cols) - (cols - 1) / 2f) * 3.2f, z = tgt.z + (i / cols - (cols - 1) / 2f) * 3.2f, cx = tgt.x, cz = tgt.z, force = !attackMove, fast = kb.leftCtrlKey.isPressed };
                    u.follow = false; u.manual = true; u.leash = 25; if (queue && u.order != null) u.queue.Add(o); else { u.order = o; u.queue.Clear(); u.focus = null; }
                    i++;
                }
                PingAt(tgt.x, tgt.z, kb.leftCtrlKey.isPressed ? UI.Gold : attackMove ? new Color(1, .4f, .3f) : new Color(.4f, 1, .4f));
            }
            if (kb.hKey.wasPressedThisFrame) foreach (var u in selected) { u.order = null; u.queue.Clear(); u.focus = null; u.follow = false; u.manual = true; u.hide = null; u.home = new Vector2(u.x, u.z); u.leash = 5; }
            if (kb.aKey.wasPressedThisFrame && kb.leftCtrlKey.isPressed) { foreach (var u in units) if (u.alive && u.team == ME) Select(u); }
            if (kb.spaceKey.wasPressedThisFrame && selected.Count > 0) { float sx = 0, sz = 0; int n = 0; foreach (var u in selected) if (u.alive) { sx += u.x; sz += u.z; n++; } if (n > 0) { rtsPos.x = sx / n; rtsPos.z = sz / n; } }
            if (kb.fKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame) { PossessSomething(); return; }
        }
        void ArmSupport() { if (supportCd > 0) Msg("Support not ready (" + Mathf.CeilToInt(supportCd) + " s)."); else { armSupport = true; Msg((ME == 0 ? "Stukas" : "Artillery") + " armed — click the target."); } }
        void Select(Unit u) { selected.Add(u); if (u.ring) u.ring.SetActive(true); }
        bool GroundAt(Vector2 mp, out Vector3 p) { var ray = cam.ScreenPointToRay(mp); if (Physics.Raycast(ray, out var h, 1500, ~0, QueryTriggerInteraction.Ignore)) { p = h.point; return true; } p = default; return false; }
        bool OverHud(Vector2 mp) { float s = Screen.height / 1080f; var g = new Vector2(mp.x / s, (Screen.height - mp.y) / s); return mp.y < 150 * s || Screen.height - mp.y < 92 * s || miniRect.Contains(g); }
        void Buy(BuyDef b) => Buy(b, false);
        void Buy(BuyDef b, bool fps)
        {
            if (b.key == "cav" && ME == 0) { Msg("Cavalry is not available to the Wehrmacht."); return; }
            if (cp < b.cost) { Msg("Not enough command points (" + b.cost + " needed)."); return; }
            cp -= b.cost; var sp = SpawnPos(ME); foreach (var u in selected) if (u.ring) u.ring.SetActive(false); selected.Clear();
            for (int i = 0; i < b.n; i++) { var q = FreeSpot(sp.x, sp.z, 8, 1); var u = SpawnUnit(ME, b.key, q.x, q.y, FaceEnemy(ME)); if (fps && u.def.kind == "inf" && player != null) { u.squad = true; u.follow = true; u.slot = slotCounter++ % 12; } else { u.manual = true; Select(u); } }
            Msg(b.label + " deployed at the rally point.");
        }

        // ---------------- squad commands (port of toggleOrders / doOrder / squadCommand / aimInfo) ----------------
        (Unit en, Vector3? pt) AimInfo()
        {
            Unit en = null; Vector3? pt = null; var o = cam.transform.position; var dir = cam.transform.forward;
            foreach (var h in Physics.RaycastAll(o, dir, 450, ~0, QueryTriggerInteraction.Collide).OrderBy(h => h.distance))
            {
                var uh = h.collider.GetComponent<UnitHit>();
                if (uh != null) { if (uh.u.alive && uh.u.team != ME && uh.u.visible && uh.u != player) { en = uh.u; pt = h.point; break; } continue; }
                if (h.collider.isTrigger) continue; pt = h.point; break;
            }
            if (en == null) { float best = 450; foreach (var e in units) { if (!e.alive || e.team == ME || !e.visible) continue; var to = e.Chest - o; float along = Vector3.Dot(to, dir); if (along < 0 || along > best) continue; if ((to - dir * along).magnitude < 1.2f) { best = along; en = e; } } }
            return (en, pt);
        }
        List<Unit> SquadMembers() => units.Where(x => x.alive && x.team == ME && x != player && x.squad).ToList();
        void SquadCommand(string kind, bool force = false)
        {
            if (player == null) return;
            if (kind == "follow")
            {
                foreach (var x in units.Where(x => x.alive && x.team == ME && x != player && x.Foot && FU.Hyp(x.x - player.x, x.z - player.z) < 60)) { x.squad = true; x.manual = false; }
                var all = SquadMembers(); foreach (var x in all) { x.follow = true; x.slot = slotCounter++ % 16; x.order = null; x.focus = null; x.hide = null; } Msg("Squad: on me (" + all.Count + ")."); return;
            }
            var sq = SquadMembers(); if (sq.Count == 0) { Msg("No squad. Press V then 1 near friendly infantry to recruit them."); return; }
            if (kind == "hold") { foreach (var x in sq) { x.follow = false; x.order = null; x.focus = null; x.home = new Vector2(x.x, x.z); } Msg("Squad: hold position."); return; }
            var (en, pt) = AimInfo();
            if (kind == "order" && en != null) { foreach (var x in sq) { x.focus = en; x.follow = false; x.order = null; x.hide = null; } Msg("Squad: engage " + Defs.NAMES[en.team][en.type] + "!"); fx.Puff(en.Chest, new Color(1, .2f, .13f, .9f), .6f, 2, .6f, Vector3.zero, true); return; }
            if (!pt.HasValue) { Msg("Aim at the ground to give a move order."); return; }
            OrderMove(sq, pt.Value.x, pt.Value.z, kind != "order", force); foreach (var x in sq) { x.follow = false; if (kind == "cover" && x.order != null) x.order.cover = true; }
            WaypointFx(pt.Value, kind == "cover" ? new Color(.4f, .67f, 1) : new Color(.4f, 1, .4f)); Msg(kind == "cover" ? "Squad: take cover there." : force ? "Squad: double time!" : "Squad: move up.");
        }
        void OrderMove(List<Unit> list, float x, float z, bool force, bool fast)
        {
            int i = 0, cols = Mathf.CeilToInt(Mathf.Sqrt(list.Count));
            foreach (var u in list) { if (!u.alive) continue; u.order = new Order { x = x + ((i % cols) - (cols - 1) / 2f) * 3.2f, z = z + (i / cols - (cols - 1) / 2f) * 3.2f, cx = x, cz = z, force = force, fast = fast }; u.queue.Clear(); u.focus = null; u.hide = null; u.leash = 25; i++; }
        }
        void WaypointFx(Vector3 p, Color c)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(b.GetComponent<Collider>()); b.transform.position = new Vector3(p.x, World.H(p.x, p.z) + 7, p.z); b.transform.localScale = new Vector3(.24f, 7, .24f);
            var r = b.GetComponent<Renderer>(); r.sharedMaterial = Mats.Transparent(new Color(c.r, c.g, c.b, .55f), null, true); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beams.Add(new Beam { t = b.transform, life = 5, r = r }); fx.Puff(p + Vector3.up * .3f, new Color(c.r, c.g, c.b, .9f), .6f, 2.4f, .8f, Vector3.zero, true);
        }
        void UpdateBeams(float dt) { for (int i = beams.Count - 1; i >= 0; i--) { var b = beams[i]; b.life -= dt; if (b.life <= 0) { Destroy(b.t.gameObject); beams.RemoveAt(i); } else b.t.localScale = new Vector3(.24f * Mathf.Min(1, b.life), 7, .24f * Mathf.Min(1, b.life)); } }

        /// Port of swapControl: take over the friendly unit you are looking at (or the nearest one).
        /// Port of possessSomething(): first selected friendly, else the nearest friendly (not a gun) to the map/camera focus.
        void PossessSomething()
        {
            var pick = selected.FirstOrDefault(u => u.alive && u.team == ME);
            if (pick == null) { var c = rts ? rtsPos : (lastPlayer != null ? new Vector3(lastPlayer.x, 0, lastPlayer.z) : SpawnPos(ME)); pick = units.Where(u => u.alive && u.team == ME && u.def.kind != "gun").OrderBy(u => FU.Hyp(u.x - c.x, u.z - c.z)).FirstOrDefault(); }
            if (pick == null) { Msg("No friendly units left to take control of."); return; }
            pick.manual = false; EnterFps(pick); if (tp) pick.SetRenderers(true); Msg("Now controlling: " + Defs.NAMES[pick.team][pick.type]);
        }
        /// Friendly unit under the crosshair (for SwapControl and the HUD hint).
        Unit LookFriend(float maxD = 260)
        {
            if (player == null) return null; Unit pick = null; var o = cam.transform.position; var dir = cam.transform.forward; float best = maxD;
            foreach (var e in units) { if (!e.alive || e.team != ME || e == player) continue; var to = e.Chest - o; float along = Vector3.Dot(to, dir); if (along < 0 || along > best) continue; float rr = e.Foot ? 1.4f : 3f; if ((to - dir * along).magnitude < rr) { best = along; pick = e; } }
            return pick;
        }
        /// Test hooks used by TestDriver ("dbg ..." lines).
        public int coaxShots, coaxHits; public int[] spawned = new int[2];
        public string DebugCmd(string a)
        {
            var w = a.Split(' ');
            switch (w[0])
            {
                case "stats":
                    {
                        var sb = new System.Text.StringBuilder();
                        for (int tm = 0; tm < 2; tm++) { var sp = SpawnPos(tm); var al = units.Where(u => u.alive && u.team == tm).ToList(); sb.Append($"T{tm}{(tm == ME ? "*" : "")} alive={al.Count} tix={tix[tm]} next={(waveNext[tm].HasValue ? waveNext[tm].Value.ToString("0") : "-")} sp=({sp.x:0},{sp.z:0}) nearSp={al.Count(u => FU.Hyp(u.x - sp.x, u.z - sp.z) < 20)} waves+={spawned[tm]} inBld={al.Count(u => u.Foot && World.InBuildingG(u.x, u.z, 0) && u.y < World.H(u.x, u.z) + .5f)} | "); }
                        sb.Append($"t={t:0} act={ActiveIdx()} dead={units.Count(u => !u.alive)} coax={coaxShots}/{coaxHits}"); return sb.ToString();
                    }
                case "kill": if (player != null) { Combat.Kill(this, player, null, false); return "killed player"; } return "no player";
                case "possess":
                    {
                        var pk = units.Where(u => u.alive && u.team == ME && u.type == w[1] && u != player).OrderBy(u => player != null ? FU.Hyp(u.x - player.x, u.z - player.z) : 0).FirstOrDefault();
                        if (pk == null) { Buy(Defs.BUY.First(b => b.key == w[1]), true); pk = units.LastOrDefault(u => u.alive && u.team == ME && u.type == w[1]); }
                        if (pk == null) return "none"; EnterFps(pk); return "possessed " + pk.type + " weps=" + (pk.weps != null ? string.Join(",", pk.weps) : "null");
                    }
                case "weapon":
                    { if (player == null || player.weps == null) return "no"; for (int i = 0; i < player.weps.Length; i++) if (Defs.WEAPONS[player.weps[i]].melee) { player.SwitchWeapon(i); BuildViewModel(); return "melee " + player.weps[i]; } return "no melee in " + string.Join(",", player.weps); }
                case "hurt": if (player != null) { var e = units.FirstOrDefault(u => u.alive && u.team != ME); if (e != null) { Combat.Damage(this, player, 5, false, false, e); return $"hit from ({e.x:0},{e.z:0}) player=({player.x:0},{player.z:0}) yaw={yaw * Mathf.Rad2Deg:0}"; } } return "no";
                case "yaw": yaw = float.Parse(w[1], System.Globalization.CultureInfo.InvariantCulture) * Mathf.Deg2Rad; return "yaw set";
                case "buy": Buy(Defs.BUY.First(b => b.key == w[1]), true); return "bought " + w[1];
                case "cp": cp = 999; return "cp";
                case "killaim":
                    {
                        var o = cam.transform.position; Unit best = null; float bd = 1e9f;
                        bool fr = w.Length > 1 && w[1] == "friend"; foreach (var e in units) { if (!e.alive || e == player || (fr ? e.team != ME : e.team == ME) || !e.Foot || e.def.kind != "inf") continue; float d = (e.Chest - o).magnitude; if (d > 6 && d < bd && d < 120 && World.LOS(o, e.Chest)) { bd = d; best = e; } }
                        if (best == null) return "none"; var v = best.Chest - o; yaw = Mathf.Atan2(v.x, v.z); pitch = Mathf.Atan2(v.y, FU.Hyp(v.x, v.z)); Combat.Kill(this, best, player, false); return $"killed {best.type} d={bd:0}";
                    }
                case "ray":
                    {
                        var o = cam.transform.position; var d = cam.transform.forward; var hs = Physics.RaycastAll(o, d, 260, ~0, QueryTriggerInteraction.Collide); System.Array.Sort(hs, (a2, b2) => a2.distance.CompareTo(b2.distance));
                        var (dist, hu, hd) = CastRay(o, d, 240, player != null ? player.team : ME);
                        return $"cam=({o.x:0.0},{o.y:0.0},{o.z:0.0}) cast={dist:0.0} unit={(hu != null ? hu.type : "-")} hits: " + string.Join("; ", hs.Take(6).Select(h => $"{h.collider.name}{(h.collider.isTrigger ? "[T]" : "")}@{h.distance:0.0}"));
                    }
                case "aim":
                    {
                        if (player == null) return "no"; var o = cam.transform.position; Unit best = null; float bd = 1e9f;
                        foreach (var e in units) { if (!e.alive || e.team == ME) continue; float d = (e.Chest - o).magnitude; if (d < bd && d < 200 && World.LOS(o, e.Chest)) { bd = d; best = e; } }
                        if (best == null) return "no target in LOS"; var v = best.Chest - o; yaw = Mathf.Atan2(v.x, v.z); pitch = Mathf.Atan2(v.y, FU.Hyp(v.x, v.z)); return $"aim {best.type} d={bd:0}";
                    }
                case "god": if (player != null) { player.max = player.hp = 5000; } return "god";
                case "heat": if (player != null) { player.heat = w.Length > 1 ? float.Parse(w[1], System.Globalization.CultureInfo.InvariantCulture) : .97f; return "heat " + player.heat + " overheated=" + player.overheated + " barrelT=" + player.barrelT; } return "no player";
            }
            return "?";
        }
        void SwapControl()
        {
            if (player == null) return; var pick = LookFriend();
            if (pick == null) { float bd = 20; foreach (var e in units) { if (!e.alive || e.team != ME || e == player) continue; float d = FU.Hyp(e.x - player.x, e.z - player.z); if (d < bd) { bd = d; pick = e; } } }
            if (pick == null) { Msg("Look at a friendly unit (or stand next to one) and press F to take control."); return; }
            var prev = player; prev.Save(); prev.stance = 0; if (prev.def.kind != "tank") prev.heading = yaw; prev.turretH = yaw; ReleasePlayer(); prev.manual = false; prev.SetRenderers(true); if (prev.Foot) { prev.squad = true; prev.follow = true; prev.slot = slotCounter++ % 16; }
            EnterFps(pick); if (tp) pick.SetRenderers(true); Msg("Now controlling: " + Defs.NAMES[pick.team][pick.type]);
        }
        /// Tank coaxial machine gun (RMB while driving) — port of playerFire(u, true).
        void CoaxFire(Unit u)
        {
            u.cd2 = .1f; float spread = .006f * (u.moving ? 1.8f : 1); var dir = (cam.transform.rotation * new Vector3(FU.Rand(-spread, spread), FU.Rand(-spread, spread), 1)).normalized; var o = cam.transform.position;
            if (tp) { var (d0, _, _) = CastRay(o, dir, 240, u.team); var A = o + dir * d0; dir = (A - tpPivot).normalized; o = tpPivot + dir * 2; }
            var (dist, hitU, head) = CastRay(o, dir, u.def.range * 2.2f, u.team); var end = o + dir * dist;
            // muzzle like the original: just ahead of the gunner's sight, slightly right and low (it used to start inside the
            // hull 0.7 m under the camera, so every tracer looked like a vertical streak and the gun seemed not to fire forward)
            var cf = cam.transform; var muzzle = tp ? tpPivot + dir * 1.3f + cf.right * .3f - Vector3.up * .15f : cf.position + cf.forward * 1.4f + cf.right * .22f - Vector3.up * .2f;
            coaxShots++; if (hitU != null) coaxHits++;
            fx.Tracer(muzzle, end); fx.Flash(muzzle, .12f); audio.Shot("mg", muzzle); u.heat = Mathf.Min(1, u.heat + .02f);
            float fall = Mathf.Lerp(1, .8f, Mathf.Clamp01((dist - 160) / 180f));
            if (hitU != null) Combat.Bullet(this, hitU, u.def.mgdmg * fall, castZone, u); else { Combat.Suppress(this, end.x, end.z, u.team, .12f, 4); fx.Dust(end); }
        }
        /// Port of respawnPoint: base, or (X on the deploy screen) the safest friendly infantry near the front.
        Vector3 RespawnPoint()
        {
            var b = SpawnPos(ME); if (!respSquad) return b; int a = ActiveIdx(); var tp0 = World.points[Mathf.Min(a, World.points.Count - 1)]; Unit best = null; float bd = 1e9f;
            foreach (var f in units) { if (!f.alive || f.team != ME || f.def.kind != "inf" || f.rout > 0) continue; if (units.Any(e => e.alive && e.team != ME && FU.Hyp(e.x - f.x, e.z - f.z) < 45)) continue; float d = FU.Hyp(f.x - tp0.x, f.z - tp0.z); if (d < bd) { bd = d; best = f; } }
            if (best == null) { Msg("No safe squad position — spawning at base."); return b; }
            return new Vector3(best.x + FU.Rand(-4, 4), 0, best.z + FU.Rand(-4, 4));
        }

        // ---------------- supply convoy (port of buildConvoy / updateConvoy) ----------------
        void BuildConvoy()
        {
            convoy.Clear(); int att = M.attacker; var sp = M.spawn[att]; FLine road = null; float best = 0;
            foreach (var r in M.roads) { float dz = Mathf.Abs(r.z2 - r.z1); if (dz > best) { best = dz; road = r; } }
            if (road == null) return; var a = new Vector2(road.x1, road.z1); var b = new Vector2(road.x2, road.z2);
            bool startA = FU.Hyp(a.x - sp.x, a.y - sp.z) < FU.Hyp(b.x - sp.x, b.y - sp.z); var A = startA ? a : b; var B = startA ? b : a; float L = (B - A).magnitude;
            for (int i = 0; i < 3; i++) { var m = ModelLib.Spawn("truck_" + att, World.root).transform; m.gameObject.SetActive(false); convoy.Add(new Convoy { m = m, s = -i * 24 - FU.Rand(0, 40), A = A, dir = (B - A) / L, L = L }); }
        }
        void UpdateConvoy(float dt)
        {
            foreach (var c in convoy)
            {
                float stop = c.L * .6f; if (c.wait > 0) { c.wait -= dt; if (c.wait <= 0) c.s = -FU.Rand(10, 70); } else { c.s += dt * 7; if (c.s >= stop) { c.s = stop; c.wait = FU.Rand(14, 26); } }
                float s = Mathf.Clamp(c.s, 0, stop), x = c.A.x + c.dir.x * s + c.dir.y * 1.8f, z = c.A.y + c.dir.y * s - c.dir.x * 1.8f;
                var sp = SpawnPos(1 - M.attacker); bool vis = c.s >= 0 && FU.Hyp(x - sp.x, z - sp.z) > 18;
                if (c.m.gameObject.activeSelf != vis) c.m.gameObject.SetActive(vis);
                c.m.position = new Vector3(x, World.H(x, z), z); c.m.rotation = Quaternion.Euler(0, Mathf.Atan2(c.dir.x, c.dir.y) * Mathf.Rad2Deg, 0);
            }
        }

        // ---------------- RTS order lines (port of updatePaths) ----------------
        void UpdatePaths()
        {
            int k = 0;
            if (rts) foreach (var u in selected)
                {
                    if (!u.alive || u.order == null) continue; var pts = new List<Vector3> { new Vector3(u.x, 0, u.z), u.order.C }; foreach (var q in u.queue) pts.Add(q.C);   // lines end at the centre of the order ring, not each soldier's formation slot
                    for (int i = 0; i < pts.Count - 1 && k < 80; i++)
                    {
                        if (k >= pathLines.Count) { var g = new GameObject("path"); var lr = g.AddComponent<LineRenderer>(); lr.positionCount = 2; lr.startWidth = lr.endWidth = .18f; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; pathLines.Add(lr); }
                        var l = pathLines[k]; var o = i == 0 ? u.order : u.queue[i - 1]; l.sharedMaterial = Mats.Transparent(o.force ? new Color(1, .85f, .4f, .8f) : new Color(.4f, 1, .4f, .8f), null, true, true);
                        l.SetPosition(0, pts[i] + Vector3.up * (World.H(pts[i].x, pts[i].z) + .8f)); l.SetPosition(1, pts[i + 1] + Vector3.up * (World.H(pts[i + 1].x, pts[i + 1].z) + .8f)); l.enabled = true; k++;
                    }
                }
            for (; k < pathLines.Count; k++) if (pathLines[k]) pathLines[k].enabled = false;
        }

        // =====================================================================================================
        // HUD / menus (IMGUI, laid out on a 1080p virtual canvas and scaled)
        // =====================================================================================================
        float S => Screen.height / 1080f; float VW => Screen.width / S; const float VH = 1080;
        Vector2 ToGui(Vector3 screen) => new Vector2(screen.x / S, (Screen.height - screen.y) / S);

        void OnGUI()
        {
            UI.Ensure(); GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(S, S, 1)); float W = VW, H = VH;
            if (state == GState.Menu) { DrawMenu(W, H); return; }
            DrawIcons();
            DrawTop(W);
            float my = 92; foreach (var (s, tt) in msgs) { float age = Time.unscaledTime - tt; if (age > 7) continue; UI.Shadow(new Rect(16, my, 640, 22), s, UI.small, UI.Text, Mathf.Clamp01((7 - age) / 1.4f)); my += 20; }
            if (dmgT > 0) UI.Fill(new Rect(0, 0, W, H), new Color(.6f, 0, 0, dmgT * .45f));
            if (state == GState.Playing && !rts && player != null && player.alive) { DrawCompass(W); DrawDamageDir(W, H); }
            DrawBanner(W);
            if (range != null && (state == GState.Playing || state == GState.Dead)) range.Draw(W, H);
            if ((state == GState.Playing || state == GState.Dead) && Keyboard.current != null && Keyboard.current.lKey.isPressed) { DrawScoreboard(W, H); return; }
            if (state == GState.End) { DrawEnd(W, H); return; }
            if (state == GState.Paused) { DrawPause(W, H); return; }
            DrawMinimap(new Rect(14, H - (rts ? 214 : 176), rts ? 200 : 162, rts ? 200 : 162));
            if (rts) DrawRts(W, H);
            else if (state == GState.Playing && player != null) DrawFps(W, H);
            if (state == GState.Dead && !rts) DrawDead(W, H);
            else if (state == GState.Dead && rts) UI.Shadow(new Rect(0, H - 170, W, 26), "Tab: return to the redeploy screen · F: take control of the selected (or nearest) unit", UI.center, UI.Gold);
        }

        void DrawIcons()
        {   // unit icon sprites (port of iconMat markers) — all units on the map; friendlies at range in first person
            foreach (var u in units)
            {
                if (!u.alive || u == player || !u.visible) continue; bool friend = u.team == ME; if (!rts && (!friend || FU.Hyp(u.x - cam.transform.position.x, u.z - cam.transform.position.z) < 12)) continue;
                var sp = cam.WorldToScreenPoint(new Vector3(u.x, u.y + u.def.bh + .9f, u.z)); if (sp.z < 0 || sp.z > (rts ? 700 : 90)) continue;
                var g = ToGui(sp); float sz = rts ? 26 : 18; var r = new Rect(g.x - sz / 2, g.y - sz / 2, sz, sz);
                UI.Tex(r, Visuals.Icon(friend, u.type), new Color(1, 1, 1, rts ? .92f : .6f));
                if (rts && friend) { UI.Fill(new Rect(r.x, r.yMax + 1, sz, 3), new Color(0, 0, 0, .7f)); UI.Fill(new Rect(r.x, r.yMax + 1, sz * u.hp / u.max, 3), selected.Contains(u) ? UI.Green : new Color(.4f, .75f, 1)); if (u.vet > 0) UI.Shadow(new Rect(r.xMax, r.y - 4, 30, 16), new string('★', u.vet), UI.small, UI.Gold); }
            }
            foreach (var k in pickups) { var sp = cam.WorldToScreenPoint(new Vector3(k.x, World.H(k.x, k.z) + 1.4f, k.z)); if (sp.z < 0 || sp.z > 60) continue; var g = ToGui(sp); UI.Shadow(new Rect(g.x - 40, g.y - 10, 80, 20), k.med ? "✚ MEDKIT" : "▣ AMMO", UI.center, k.med ? FU.Hex(0xff7a7a) : UI.Green, .9f); }
        }

        void DrawTop(float W)
        {
            UI.Tex(new Rect(0, 0, W, 90), UI.gradT, Color.white);
            float left = Mathf.Max(0, M.time - t); string tm = $"{(int)(left / 60)}:{(int)(left % 60):00}"; int att = M.attacker;
            UI.Shadow(new Rect(18, 8, 520, 30), M.name.ToUpper(), UI.h2, UI.Text); UI.Shadow(new Rect(18, 40, 600, 22), M.date + "  ·  " + (ME == att ? "ATTACKING" : "DEFENDING") + " as " + Defs.SIDE[ME], UI.small, UI.Dim);
            UI.Shadow(new Rect(18, 60, 600, 22), $"Reinforcements  <color=#6bd36b>{tix[ME]}</color>  ·  enemy <color=#e05a4a>{tix[EN]}</color>", UI.small, UI.Text);
            UI.Shadow(new Rect(W - 330, 8, 312, 34), "⏱ " + tm, new GUIStyle(UI.h2) { alignment = TextAnchor.UpperRight }, left < 120 ? UI.Red : UI.Text);
            UI.Shadow(new Rect(W - 330, 42, 312, 26), $"CP {Mathf.FloorToInt(cp)}", new GUIStyle(UI.h2) { alignment = TextAnchor.UpperRight, fontSize = 20 }, UI.Gold);
            float bw = 170, gap = 8, x0 = W / 2 - World.points.Count * (bw + gap) / 2; int act = ActiveIdx();
            for (int i = 0; i < World.points.Count; i++)
            {
                var p = World.points[i]; var r = new Rect(x0 + i * (bw + gap), 12, bw, 46);
                Color border = p.locked ? (p.owner == ME ? UI.Green : UI.Red) : i == act ? UI.Gold : new Color(.55f, .55f, .55f, .6f);
                UI.Fill(r, new Color(0, 0, 0, .45f));
                if (p.locked) UI.Fill(r, new Color(border.r, border.g, border.b, .28f)); else if (i == act) UI.Fill(new Rect(r.x, r.y, r.width * p.ap, r.height), new Color(UI.Gold.r, UI.Gold.g, UI.Gold.b, .3f));
                UI.Frame(r, border, i == act ? 2 : 1); if (i == act) UI.Frame(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(UI.Gold.r, UI.Gold.g, UI.Gold.b, .25f + .2f * Mathf.Sin(Time.unscaledTime * 4)));
                UI.Shadow(new Rect(r.x + 6, r.y + 3, r.width - 12, 20), (i + 1) + ". " + p.n, new GUIStyle(UI.small) { alignment = TextAnchor.UpperCenter, wordWrap = false, clipping = TextClipping.Clip }, p.locked || i == act ? UI.Text : UI.Dim);
                if (i == act) UI.Shadow(new Rect(r.x + 6, r.y + 23, r.width - 12, 20), $"⚔ {p.g} vs {p.e}" + (p.pushT > 0 ? "  PUSH" : ""), new GUIStyle(UI.small) { alignment = TextAnchor.UpperCenter }, UI.Gold);
            }
        }

        void DrawBanner(float W)
        {
            if (banner == null) return; float age = Time.unscaledTime - banner.t; if (age > 3.2f) { banner = null; return; }
            float a = Mathf.Clamp01(age * 6) * Mathf.Clamp01((3.2f - age) * 2), sc = 1 + Mathf.Max(0, .25f - age) * 1.2f;
            var st = new GUIStyle(UI.h1) { fontSize = (int)(46 * sc) }; UI.Fill(new Rect(W / 2 - 420, 214, 840, 2), new Color(banner.c.r, banner.c.g, banner.c.b, .6f * a));
            UI.Shadow(new Rect(0, 226, W, 60), UI.Spaced(banner.a), st, banner.c, a);
            if (!string.IsNullOrEmpty(banner.b)) UI.Shadow(new Rect(0, 284, W, 26), banner.b, UI.center, UI.Text, a);
            UI.Fill(new Rect(W / 2 - 420, 316, 840, 2), new Color(banner.c.r, banner.c.g, banner.c.b, .6f * a));
        }

        void DrawMinimap(Rect r)
        {
            if (!minimap) return; miniRect = r;
            if (rts)
            {   // click the minimap: LMB jumps the camera, RMB orders the selection there
                var ev = Event.current;
                if (ev.type == EventType.MouseDown && r.Contains(ev.mousePosition))
                {
                    float wx = ((ev.mousePosition.x - r.x) / r.width - .5f) * 2 * M.half, wz = ((1 - (ev.mousePosition.y - r.y) / r.height) - .5f) * 2 * M.half;
                    if (ev.button == 0) { rtsPos.x = wx; rtsPos.z = wz; } else if (selected.Count > 0) { OrderMove(selected.Where(u => u.alive).ToList(), wx, wz, !Keyboard.current.leftAltKey.isPressed, false); foreach (var u in selected) { u.manual = true; u.follow = false; } }
                    ev.Use();
                }
            }
            UI.Fill(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(0, 0, 0, .55f)); GUI.DrawTexture(r, minimap); UI.Frame(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), UI.Olive);
            float Hm = M.half; Vector2 F(float x, float z) => new Vector2(r.x + (x / Hm / 2 + .5f) * r.width, r.y + (1 - (z / Hm / 2 + .5f)) * r.height);
            int act = ActiveIdx();
            for (int i = 0; i < World.points.Count; i++) { var p = World.points[i]; var q = F(p.x, p.z); Color c = p.locked ? (p.owner == ME ? UI.Green : UI.Red) : i == act ? UI.Gold : new Color(.85f, .85f, .85f); UI.Frame(new Rect(q.x - 6, q.y - 6, 12, 12), c, 2); }
            foreach (var u in units) { if (!u.alive || (u.team != ME && !u.visible)) continue; var q = F(u.x, u.z); float k = u.def.kind == "tank" ? 4 : 2.5f; UI.Fill(new Rect(q.x - k / 2, q.y - k / 2, k, k), u.team == ME ? (selected.Contains(u) ? Color.white : UI.Green) : UI.Red); }
            if (player != null && player.alive) { var q = F(player.x, player.z); UI.Fill(new Rect(q.x - 3, q.y - 3, 6, 6), Color.yellow); var d = new Vector2(Mathf.Sin(yaw), -Mathf.Cos(yaw)) * 9; UI.Fill(new Rect(q.x + d.x - 1.5f, q.y + d.y - 1.5f, 3, 3), Color.yellow); }
            else if (rts) { var q = F(rtsPos.x, rtsPos.z); UI.Frame(new Rect(q.x - 10, q.y - 8, 20, 16), Color.yellow); }
        }

        void DrawFps(float W, float H)
        {
            var u = player; var w = u.Weapon; bool scoped = w != null && w.scope && ads > .85f;
            if (scoped)
            {
                float r = H * .46f; UI.Fill(new Rect(0, 0, W / 2 - r, H), Color.black); UI.Fill(new Rect(W / 2 + r, 0, W / 2 - r, H), Color.black);
                UI.Fill(new Rect(W / 2 - r, 0, 2 * r, H / 2 - r), Color.black); UI.Fill(new Rect(W / 2 - r, H / 2 + r, 2 * r, H / 2 - r), Color.black);
                UI.Fill(new Rect(W / 2 - r, H / 2 - 1, 2 * r, 2), Color.black); UI.Fill(new Rect(W / 2 - 1, H / 2, 2, r), Color.black); UI.Fill(new Rect(W / 2 - 1.5f, H / 2 - 30, 3, 30), Color.black);
            }
            else if (hipDot && ads < .3f && (w == null || !w.melee) && u.def.kind != "tank") UI.Fill(new Rect(W / 2 - 1.5f, H / 2 - 1.5f, 3, 3), new Color(1, 1, 1, .7f));   // optional RO2 hip dot
            if (!scoped && Event.current.type == EventType.Repaint) { var lf = LookFriend(60); if (lf != null) UI.Shadow(new Rect(W / 2 - 260, H / 2 + 40, 520, 24), "[F] Take control: " + Defs.NAMES[lf.team][lf.type], UI.center, UI.Gold); }
            float px = 196, py = H - 108; UI.Fill(new Rect(px, py, 340, 92), new Color(.03f, .04f, .025f, .55f)); UI.Frame(new Rect(px, py, 340, 92), new Color(.42f, .48f, .29f, .8f));
            UI.Shadow(new Rect(px + 12, py + 6, 320, 22), Defs.NAMES[u.team][u.type].ToUpper() + (u.vet > 0 ? "  " + new string('★', u.vet) : ""), UI.small, UI.Gold);
            bool hurt = u.hp < 35 || u.bleed > 0; float pulse = hurt ? .6f + .4f * Mathf.Sin(Time.unscaledTime * 8) : 1;
            float hpf = u.hp / u.max; string cond = hpf > .85f && u.bleed <= 0 ? "UNHURT" : hpf > .55f ? "WOUNDED" : hpf > .25f ? "BADLY WOUNDED" : "CRITICAL";   // RO2: condition, never numbers
            UI.Shadow(new Rect(px + 12, py + 27, 320, 22), cond + (u.bleed > 0 ? "  ·  bleeding" : ""), UI.small, hurt ? new Color(.88f, .35f, .29f, pulse) : hpf > .85f ? UI.Green : UI.Gold);
            UI.Fill(new Rect(px + 12, py + 50, 240, 5), new Color(.15f, .15f, .15f, .9f)); UI.Fill(new Rect(px + 12, py + 50, 240 * u.stam, 5), FU.Hex(0xe8c547));
            UI.Shadow(new Rect(px + 12, py + 62, 320, 22), (u.bandaging > 0 ? "BANDAGING…" : u.stance == 2 ? "PRONE" : u.stance == 1 ? "CROUCH" : "STANDING") + (hurt && u.bandaging <= 0 ? "   ·  BLEEDING [H]" : "") + (u.supp > .5f ? "   ·  SUPPRESSED" : "") + (hold ? "   ·  holding breath" : gasp ? "   ·  gasping" : "") + (u.barrelT > 0 ? "   ·  CHANGING BARREL" : w != null && w.mg ? (mgRest ? "   ·  MG DEPLOYED" : "   ·  MG NOT DEPLOYED") : "") + (tp ? "   ·  3rd person" : ""), UI.small, hurt ? UI.Red : UI.Dim);
            if (w != null && w.mg && u.heat > .05f)
            {   // barrel heat gauge (RO2): white-hot means stop and change it
                var hr = new Rect(W - 268, H - 140, 240, 6); UI.Fill(hr, new Color(0, 0, 0, .5f));
                UI.Fill(new Rect(hr.x, hr.y, hr.width * u.heat, hr.height), Color.Lerp(new Color(1, .65f, .2f, .85f), new Color(1, .2f, .1f, 1), u.heat));
                UI.Shadow(new Rect(W - 520, H - 156, 492, 16), u.overheated ? "BARREL OVERHEATED — [X] change" : "barrel heat", new GUIStyle(UI.small) { alignment = TextAnchor.UpperRight }, u.overheated ? UI.Red : UI.Dim);
            }
            if (ads > .5f && breath < 1) { UI.Fill(new Rect(W / 2 - 60, H / 2 + 60, 120, 4), new Color(0, 0, 0, .5f)); UI.Fill(new Rect(W / 2 - 60, H / 2 + 60, 120 * breath, 4), gasp ? UI.Red : new Color(.7f, .85f, 1, .8f)); }
            if (orderOpen || buyOpen)
            {
                var mr = new Rect(W / 2 - 170, H * .36f, 340, orderOpen ? 216 : 40 + 28 * 6); GUI.Box(mr, GUIContent.none, UI.panel); UI.Frame(mr, UI.Gold);
                string txt = orderOpen ? "<b><color=#ffd96a>SQUAD ORDERS</color></b>\n[1] Follow me / recruit nearby\n[2] Move to where I look\n[3] Take cover there\n[4] Hold position\n[5] Attack what I aim at\n[6] Double-time there\n[V] cancel"
                    : "<b><color=#ffd96a>REQUEST REINFORCEMENTS</color></b>  (" + Mathf.FloorToInt(cp) + " CP)\n" + string.Join("\n", Defs.BUY.Where(b => !(b.key == "cav" && ME == 0)).Select((b, i) => $"[{i + 1}] {b.label} — {b.cost} CP"));
                GUI.Label(new Rect(mr.x + 18, mr.y + 12, mr.width - 30, mr.height - 20), txt, new GUIStyle(UI.label) { fontSize = 16 });
            }
            string ammo = w == null ? "" : w.melee ? "" : w.clip > 0 ? (u.reload > 0 ? "RELOADING" : ammoShowT > 0 ? AmmoEstimate(u, w) : "") : (u.cd > 0 ? "LOADING" : "READY");
            if (u.def.kind == "tank") UI.Shadow(new Rect(W - 520, H - 136, 496, 22), "LMB main gun  ·  RMB coaxial MG" + (u.cd2 > -.15f ? "  ▮▮▮" : ""), new GUIStyle(UI.small) { alignment = TextAnchor.UpperRight }, UI.Gold);
            UI.Shadow(new Rect(W - 520, H - 112, 496, 48), ammo, ammoShowT > 0 && u.reload <= 0 ? new GUIStyle(UI.ammo) { fontSize = 22 } : UI.ammo, u.clip == 0 && w != null && w.clip > 0 ? UI.Red : UI.Text);
            UI.Shadow(new Rect(W - 520, H - 62, 496, 24), (w != null ? w.name : "") + "     ✱ " + u.gren + " grenades" + (u.smoke > 0 ? "   ☁ " + u.smoke + " smoke" : "") + "   ✚ " + u.bandages + " bandages", new GUIStyle(UI.small) { alignment = TextAnchor.UpperRight }, UI.Dim);
            var sq = units.Count(x => x.alive && x.team == ME && x.squad && x != player);
            UI.Shadow(new Rect(px, H - 132, W - px - 540, 22), $"Squad {sq} · {(supportCd > 0 ? "support " + Mathf.CeilToInt(supportCd) + "s" : "[B] " + (ME == 0 ? "Stukas" : "artillery") + " ready")}   [V] orders  [N] reinforce  [F] take control  [H] bandage  [K] check ammo  [L] scoreboard  [T] 3rd person  [Q/E] lean  [C]/[Z] stance  [G] grenade  [Shift+G] smoke  [X] MG barrel  [Tab] map", UI.small, new Color(1, 1, 1, .6f));
        }

        void DrawRts(float W, float H)
        {
            if (dragging) { var mp = Mouse.current.position.ReadValue(); var a = ToGui(dragStart); var b = ToGui(mp); var r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)); UI.Fill(r, new Color(.4f, 1, .4f, .12f)); UI.Frame(r, UI.Green); }
            var panel = new Rect(232, H - 140, W - 232 - 16, 126); GUI.Box(panel, GUIContent.none, UI.panel);
            var groups = selected.Where(u => u.alive).GroupBy(u => u.type).Select(g => g.Count() + "× " + Defs.NAMES[ME][g.Key]);
            UI.Shadow(new Rect(panel.x + 14, panel.y + 10, 420, 70), selected.Count == 0 ? "No units selected — drag to box-select." : "<b>SELECTED</b>\n" + string.Join(", ", groups), UI.small, UI.Text);
            UI.Shadow(new Rect(panel.x + 14, panel.y + 80, 470, 44), "LMB select · RMB move · Alt+RMB attack-move · Ctrl fast · Shift queue · H halt · Ctrl+A all · Space centre · Q/E rotate · F/P/Tab take control", UI.small, new Color(1, 1, 1, .55f));
            float bx = panel.x + 500;
            for (int i = 0; i < Defs.BUY.Length; i++)
            {
                var b = Defs.BUY[i]; bool off = b.key == "cav" && ME == 0; GUI.enabled = !off && cp >= b.cost;
                if (GUI.Button(new Rect(bx + i * 116, panel.y + 12, 110, 54), $"[{i + 1}] {b.label}\n<color=#ffd96a>{b.cost} CP</color>", UI.button)) Buy(b);
            }
            GUI.enabled = supportCd <= 0;
            if (GUI.Button(new Rect(bx, panel.y + 72, 226, 42), supportCd > 0 ? $"[B] {(ME == 0 ? "Stukas" : "Artillery")} ({Mathf.CeilToInt(supportCd)} s)" : armSupport ? "[B] Click a target…" : $"[B] Call {(ME == 0 ? "Stukas" : "artillery")}", UI.button)) ArmSupport();
            GUI.enabled = true;
        }

        void DrawDead(float W, float H)
        {
            UI.Fill(new Rect(0, 0, W, H), new Color(.15f, .02f, .01f, .35f));
            UI.Shadow(new Rect(0, 200, W, 70), UI.Spaced("KILLED IN ACTION"), new GUIStyle(UI.h1) { fontSize = 54 }, UI.Red);
            UI.Shadow(new Rect(0, 272, W, 30), respT > 0 ? $"Redeploy in {Mathf.CeilToInt(respT)}…" : "Choose a class and deploy  (1–" + Defs.CLASSES.Length + ", Space)", UI.center, UI.Text);
            int nc = Defs.CLASSES.Length; float bw = 210, x0 = W / 2 - nc * (bw + 12) / 2 + 6;
            for (int i = 0; i < nc; i++)
            {
                var c = Defs.CLASSES[i]; var r = new Rect(x0 + i * (bw + 12), 340, bw, 130);
                if (GUI.Button(r, GUIContent.none, i == classIdx ? UI.cardSel : UI.card)) classIdx = i;
                UI.Shadow(new Rect(r.x + 14, r.y + 10, r.width - 28, 28), "[" + (i + 1) + "] " + c.label.ToUpper(), new GUIStyle(UI.h2) { fontSize = 20 }, i == classIdx ? UI.Gold : UI.Text);
                string wl = string.Join("\n", c.weps[ME].Select(id => "· " + Defs.WEAPONS[id].name)) + "\n· grenades ×" + c.gren;
                UI.Shadow(new Rect(r.x + 14, r.y + 42, r.width - 28, 86), wl, UI.small, UI.Dim);
            }
            if (GUI.Button(new Rect(W / 2 - 150, 488, 300, 34), "Spawn at: " + (respSquad ? "SQUAD (front line)" : "BASE") + "   [X]", UI.button)) respSquad = !respSquad;
            GUI.enabled = respT <= 0; if (GUI.Button(new Rect(W / 2 - 150, 530, 300, 52), "DEPLOY", UI.bigButton)) Deploy(classIdx); GUI.enabled = true;
            UI.Shadow(new Rect(0, 594, W, 24), "F: take control of a nearby soldier, tank or gun  ·  Tab: command map", UI.center, UI.Dim);
        }

        // ---------------- RO2-style HUD helpers ----------------
        float ammoShowT; bool hipDot;
        static string AmmoEstimate(Unit u, WeaponDef w)
        {   // feel of the magazine / clip, not a count
            int c = u.clip, max = w.clip; string feel = c <= 0 ? "Empty" : c >= max ? "Full" : c > max * .6f ? "Heavy" : c > max * .3f ? "Light" : "Nearly empty";
            string kind = w.vm == "rifle" || w.vm == "sniper" ? (w.scope ? "loose rounds" : "stripper clips") : w.mg ? "belts" : "magazines";
            int spare = w.vm == "sniper" ? u.reserve : Mathf.CeilToInt(u.reserve / (float)Mathf.Max(1, max));
            string sp = spare <= 0 ? "no spare " + kind : w.vm == "sniper" ? (spare > 15 ? "plenty of" : spare > 5 ? "some" : "few") + " " + kind : spare + " spare " + kind;
            return (w.mg ? "Belt" : w.vm == "rifle" || w.vm == "sniper" ? "Magazine" : "Magazine") + ": " + feel + "  ·  " + sp;
        }

        // ---------------- scoreboard (hold L) ----------------
        void DrawScoreboard(float W, float H)
        {
            var r = new Rect(W / 2 - 430, 110, 860, 560); GUI.Box(r, GUIContent.none, UI.panel); UI.Frame(r, UI.Gold);
            UI.Shadow(new Rect(r.x, r.y + 12, r.width, 34), UI.Spaced("SCOREBOARD"), new GUIStyle(UI.h2) { alignment = TextAnchor.MiddleCenter }, UI.Gold);
            for (int tm = 0; tm < 2; tm++)
            {
                int side = tm == 0 ? ME : EN; float x = r.x + 24 + tm * 420, y = r.y + 58;
                var all = units.Where(u => u.team == side && u.def.kind != "gun").ToList();
                int total = all.Sum(u => u.score), held = World.points.Count(p => p.locked && p.owner == side);
                string role = range != null ? "" : side == M.attacker ? "ATTACKING" : "DEFENDING";
                UI.Shadow(new Rect(x, y, 400, 26), Defs.SIDE[side].ToUpper() + "   " + role, UI.small, side == ME ? UI.Green : UI.Red); y += 24;
                UI.Shadow(new Rect(x, y, 400, 22), $"Score {total}  ·  objectives {held}/{World.points.Count}  ·  reinforcements {tix[side]}", UI.small, UI.Text); y += 30;
                UI.Shadow(new Rect(x, y, 400, 20), "SOLDIER                              SCORE   K   D", UI.small, UI.Dim); y += 22;
                foreach (var u in all.OrderByDescending(u => u.score).ThenBy(u => u.deaths).Take(16))
                {
                    string n = Defs.NAMES[u.team][u.type] + " #" + u.id; if (n.Length > 30) n = n.Substring(0, 30);
                    UI.Shadow(new Rect(x, y, 260, 20), (u == player ? "▶ " : "   ") + n, UI.small, u == player ? UI.Gold : u.alive ? UI.Text : UI.Dim);
                    UI.Shadow(new Rect(x + 262, y, 60, 20), u.score.ToString(), UI.small, UI.Text); UI.Shadow(new Rect(x + 318, y, 30, 20), u.kills.ToString(), UI.small, UI.Text); UI.Shadow(new Rect(x + 350, y, 30, 20), u.deaths.ToString(), UI.small, UI.Text);
                    y += 21;
                }
            }
            UI.Shadow(new Rect(r.x, r.yMax - 30, r.width, 22), "Kill 10  ·  kill defending the objective 15  ·  capture 50 each  ·  you redeploy with the next reinforcement wave (every 15 s)", UI.center, UI.Dim);
        }

        void DrawPause(float W, float H)
        {
            UI.Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, .6f)); UI.Shadow(new Rect(0, 250, W, 70), UI.Spaced("PAUSED"), UI.h1, UI.Text);
            if (GUI.Button(new Rect(W / 2 - 150, 360, 300, 50), "Resume", UI.bigButton)) Resume();
            if (GUI.Button(new Rect(W / 2 - 150, 420, 300, 44), "Restart mission", UI.button)) { Time.timeScale = 1; StartMission(missionIdx, ME); }
            if (GUI.Button(new Rect(W / 2 - 150, 474, 300, 44), "Quit to menu", UI.button)) EnterMenu();
            if (GUI.Button(new Rect(W / 2 + 160, 420, 220, 44), "Hip dot: " + (hipDot ? "ON" : "OFF"), UI.button)) { hipDot = !hipDot; PlayerPrefs.SetInt("FW.HipDot", hipDot ? 1 : 0); }
            GUI.Box(new Rect(W / 2 - 420, 560, 840, 170), GUIContent.none, UI.panel);
            GUI.Label(new Rect(W / 2 - 400, 574, 800, 150), "<b><color=#ffd96a>INFANTRY</color></b>  WASD move · Shift sprint · LMB fire · RMB iron sights · R reload · 1–3 / wheel weapons · C crouch · Z prone · Q/E lean · G grenade · V squad orders · N reinforcements · F take control of a friendly · H bandage · T third person · Shift (aiming) hold breath · B call support\n\n<b><color=#ffd96a>COMMAND MAP</color></b>  Tab · LMB select / drag · RMB move · Alt+RMB attack-move · Shift queue · 1–6 buy units · B support strike · P / F take control of a unit · Ctrl+A select all · Space centre · minimap: LMB jump, RMB order", UI.small);
        }

        void DrawEnd(float W, float H)
        {
            UI.Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, .62f));
            UI.Shadow(new Rect(0, 210, W, 80), UI.Spaced(endTitle), new GUIStyle(UI.h1) { fontSize = 72 }, won ? UI.Green : UI.Red);
            UI.Shadow(new Rect(0, 294, W, 30), M.name + " — " + M.date, UI.center, UI.Text);
            UI.Shadow(new Rect(0, 326, W, 30), endWhy, UI.center, UI.Gold);
            GUI.Box(new Rect(W / 2 - 360, 380, 720, 90), GUIContent.none, UI.panel);
            string[] lab = { "OBJECTIVES HELD", "ENEMY DESTROYED", "YOUR LOSSES", "TIME" }; string[] val = { World.points.Count(p => p.owner == ME) + " / " + World.points.Count, kills.ToString(), losses.ToString(), $"{(int)(t / 60)}:{(int)(t % 60):00}" };
            for (int i = 0; i < 4; i++) { UI.Shadow(new Rect(W / 2 - 350 + i * 175, 392, 170, 20), lab[i], new GUIStyle(UI.small) { alignment = TextAnchor.UpperCenter }, UI.Dim); UI.Shadow(new Rect(W / 2 - 350 + i * 175, 416, 170, 40), val[i], new GUIStyle(UI.h2) { alignment = TextAnchor.UpperCenter }, UI.Text); }
            if (won && M.name.Contains("Warsaw")) GUI.Label(new Rect(W / 2 - 420, 480, 840, 80), "On 17 September the Soviet Union invaded from the east. Warsaw capitulated on 28 September and the last Polish units surrendered at Kock on 6 October 1939. Poland was partitioned, and the Second World War in Europe had begun.", new GUIStyle(UI.small) { alignment = TextAnchor.UpperCenter });
            float y = 570;
            if (GUI.Button(new Rect(W / 2 - 340, y, 210, 50), "Retry", UI.button)) StartMission(missionIdx, ME);
            if (won && missionIdx < missions.Length - 1 && GUI.Button(new Rect(W / 2 - 105, y, 210, 50), "Next mission", UI.bigButton)) StartMission(missionIdx + 1, ME);
            if (GUI.Button(new Rect(W / 2 + 130, y, 210, 50), "Menu", UI.button)) EnterMenu();
        }

        void DrawMenu(float W, float H)
        {
            UI.Tex(new Rect(0, 0, 1150, H), UI.gradL, Color.white); UI.Tex(new Rect(0, H - 170, W, 170), UI.gradB, new Color(1, 1, 1, .9f));
            UI.Shadow(new Rect(70, 52, 1000, 96), UI.Spaced("FALL WEISS"), new GUIStyle(UI.title) { fontSize = 86 }, UI.Gold);
            UI.Shadow(new Rect(74, 146, 900, 30), UI.Spaced("POLAND  ·  SEPTEMBER 1939"), new GUIStyle(UI.h3) { fontSize = 20 }, UI.Text);
            UI.Fill(new Rect(74, 182, 520, 2), new Color(UI.Gold.r, UI.Gold.g, UI.Gold.b, .7f));
            UI.Shadow(new Rect(74, 190, 640, 22), "Red Orchestra-style infantry combat with a Steel Division command layer.", UI.small, UI.Dim);
            UI.Shadow(new Rect(74, 230, 300, 22), "CHOOSE YOUR ARMY", UI.h3, UI.Dim);
            for (int s = 0; s < 2; s++)
            {
                var r = new Rect(74 + s * 266, 256, 256, 84);
                if (GUI.Button(r, GUIContent.none, menuSide == s ? UI.cardSel : UI.card)) { if (menuSide != s) { menuSide = s; ME = s; EN = 1 - s; } }
                UI.Tex(new Rect(r.x + 12, r.y + 14, 56, 56), s == 0 ? UI.emblemG : UI.emblemP, Color.white);
                UI.Shadow(new Rect(r.x + 80, r.y + 14, 170, 30), s == 0 ? "WEHRMACHT" : "POLISH ARMY", new GUIStyle(UI.h2) { fontSize = 21 }, menuSide == s ? UI.Gold : UI.Text);
                UI.Shadow(new Rect(r.x + 80, r.y + 44, 170, 30), s == 0 ? "Heer · Luftwaffe" : "Wojsko Polskie", UI.small, UI.Dim);
            }
            UI.Shadow(new Rect(74, 358, 400, 22), "CHOOSE YOUR BATTLE", UI.h3, UI.Dim);
            for (int i = 0; i < missions.Length; i++)
            {
                var m = missions[i]; var r = new Rect(74, 384 + i * 74, 522, 66); bool sel = i == menuMission; bool att = m.attacker == menuSide;
                if (GUI.Button(r, GUIContent.none, sel ? UI.cardSel : UI.card)) { if (menuMission != i) { menuMission = i; BuildBackdrop(i); } }
                UI.Shadow(new Rect(r.x + 14, r.y + 8, 40, 50), (i + 1).ToString(), new GUIStyle(UI.h2) { fontSize = 34 }, sel ? UI.Gold : new Color(UI.Dim.r, UI.Dim.g, UI.Dim.b, .7f));
                UI.Shadow(new Rect(r.x + 58, r.y + 9, 340, 26), m.name.ToUpper(), new GUIStyle(UI.h2) { fontSize = 19, wordWrap = false }, sel ? UI.Gold : UI.Text);
                UI.Shadow(new Rect(r.x + 58, r.y + 37, 340, 22), m.date, UI.small, UI.Dim);
                var tag = new Rect(r.xMax - 96, r.y + 20, 82, 26); UI.Fill(tag, new Color(0, 0, 0, .35f)); UI.Frame(tag, att ? UI.Red : UI.Green);
                UI.Shadow(tag, att ? "ATTACK" : "DEFEND", new GUIStyle(UI.small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold }, att ? UI.Red : UI.Green);
            }
            var M0 = missions[menuMission]; bool a0 = M0.attacker == menuSide;
            var bp = new Rect(W - 640, 256, 576, 700); GUI.Box(bp, GUIContent.none, UI.panel);
            UI.Shadow(new Rect(bp.x + 22, bp.y + 18, bp.width - 44, 22), "OPERATIONAL BRIEFING", UI.h3, UI.Dim);
            UI.Shadow(new Rect(bp.x + 22, bp.y + 42, bp.width - 44, 40), M0.name.ToUpper(), new GUIStyle(UI.h2) { fontSize = 30 }, UI.Gold);
            UI.Shadow(new Rect(bp.x + 22, bp.y + 84, bp.width - 44, 22), M0.date + "   ·   you " + (a0 ? "ATTACK" : "DEFEND") + " as " + Defs.SIDE[menuSide], UI.small, a0 ? UI.Red : UI.Green);
            UI.Fill(new Rect(bp.x + 22, bp.y + 114, bp.width - 44, 1), new Color(.42f, .48f, .29f, 1));
            GUI.Label(new Rect(bp.x + 22, bp.y + 126, bp.width - 44, 170), M0.brief, new GUIStyle(UI.label) { fontSize = 15 });
            UI.Shadow(new Rect(bp.x + 22, bp.y + 300, 300, 22), "OBJECTIVES — taken in order", UI.h3, UI.Dim);
            for (int i = 0; i < M0.points.Length; i++) UI.Shadow(new Rect(bp.x + 30, bp.y + 328 + i * 24, bp.width - 60, 22), (i + 1) + ".  " + M0.points[i].n, UI.small, UI.Text);
            string[] lab = { "REINFORCEMENTS", "TIME LIMIT", "WEATHER" };
            string[] val = { (a0 ? M0.tickets : Mathf.RoundToInt(M0.tickets * 1.6f)).ToString(), $"{(int)(M0.time / 60)}:00", M0.hasRain ? "rain later" : M0.city ? "smoke, haze" : "clear" };
            for (int i = 0; i < 3; i++) { UI.Shadow(new Rect(bp.x + 22 + i * 180, bp.y + 466, 176, 20), lab[i], UI.small, UI.Dim); UI.Shadow(new Rect(bp.x + 22 + i * 180, bp.y + 486, 176, 34), val[i], new GUIStyle(UI.h2) { fontSize = 24 }, UI.Text); }
            UI.Shadow(new Rect(bp.x + 22, bp.y + 540, bp.width - 44, 60), a0 ? "Every death costs one reinforcement. Capture all five positions before the clock or your reserves run out." : "Hold every position until the clock runs out. Lost positions cannot be retaken.", UI.small, UI.Dim);
            if (GUI.Button(new Rect(bp.x + 22, bp.yMax - 82, bp.width - 44, 62), UI.Spaced("BEGIN OPERATION"), UI.bigButton)) StartMission(menuMission, menuSide);
            UI.Shadow(new Rect(74, H - 46, W - 148, 24), "WASD move · Mouse aim · LMB fire · RMB iron sights · Tab command map · B support · Esc pause", UI.small, new Color(1, 1, 1, .55f));
            UI.Shadow(new Rect(W - 420, H - 46, 346, 24), "Unity port · " + missions.Length + " missions", new GUIStyle(UI.small) { alignment = TextAnchor.UpperRight }, new Color(1, 1, 1, .35f));
        }
    }
}
