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
    public enum GState { Menu, Deploy, Playing, Dead, Paused, End }   // Deploy: SD2-style phase A — the player places his starting forces before the whistle

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
        public bool tp, orderOpen, buyOpen; bool respSquad, hold, gasp; float holdZ; float orderT, buyT, breath = 1, shake, convT; int lastClass;
        Rect miniRect; readonly List<LineRenderer> pathLines = new List<LineRenderer>();
        float stepDist; bool stepFoot;   // distance since the last footstep; which foot lands next (steps alternate left/right)
        class Convoy { public Transform m; public float s, wait, L; public Vector2 A, dir; public AudioSource eng; }
        readonly List<Convoy> convoy = new List<Convoy>();
        class Beam { public Transform t; public float life; public Renderer r; }
        readonly List<Beam> beams = new List<Beam>();

        // first person state (port of fp)
        float yaw, pitch, ads, fov = 70, recoil, bob, eye = 1.55f, swT, fireQueued, gT, lean; Vector3 tpPivot;
        public float Ads => ads;
        float sens = 1, baseFov = 72;   // options (PlayerPrefs FW.Sens / FW.Fov)
        float fpsAvg = 60, fpsMin = 60, fpsMinT;
        public int[,,] killLog = new int[2, 3, 4];   // who killed whom (dbg losses)
        bool hudMin, vsync = true; int quality = 1, pauseTab, setTab, menuSettings = -1;   // settings: minimal HUD, vsync, graphics preset (0 low 1 medium 2 high); UI state of the pause/settings screens
        float tixT = -99; int tixRef; float tixRate;   // ticket trend for the HUD status line (per minute, sampled every 20 s)
        Transform vm; string vmKind; readonly List<Grenade> grenades = new List<Grenade>();
        class Grenade { public Vector3 p, v; public float fuse, rad, dmg, spin; public bool stick, rest; public Transform t; public Unit by; }
        /// Per side: the German M24 stick grenade (pull-cord friction igniter, ~4.5 s, thrown far thanks to the handle,
        /// mostly blast) and the Polish wz.33 (shorter fuse, shorter throw, more fragments).
        struct GrenSpec { public string name; public float fuse, speed, rad, dmg; public bool stick; }
        static GrenSpec Gren(int team) => team == 0
            ? new GrenSpec { name = "Stielhandgranate 24", fuse = 4.5f, speed = 22, rad = 6f, dmg = 125, stick = true }
            : new GrenSpec { name = "Granat wz.33", fuse = 3.5f, speed = 18, rad = 7.5f, dmg = 110, stick = false };
        static Transform GrenadeModel(bool stick)
        {
            var root = new GameObject(stick ? "M24" : "wz33").transform;
            GameObject Part(PrimitiveType k, Vector3 pos, Vector3 sc, int hex) { var g = GameObject.CreatePrimitive(k); Object.Destroy(g.GetComponent<Collider>()); g.transform.SetParent(root, false); g.transform.localPosition = pos; g.transform.localScale = sc; g.GetComponent<Renderer>().sharedMaterial = Mats.Lit(hex); return g; }
            if (stick)
            {   // wooden handle with the steel head on top and the screw cap at the bottom
                Part(PrimitiveType.Cylinder, new Vector3(0, 0, 0), new Vector3(.034f, .18f, .034f), 0x8a6a3e);
                Part(PrimitiveType.Cylinder, new Vector3(0, .225f, 0), new Vector3(.068f, .05f, .068f), 0x4b5045);
                Part(PrimitiveType.Cylinder, new Vector3(0, -.185f, 0), new Vector3(.038f, .008f, .038f), 0x5a5a52);
            }
            else
            {   // egg-shaped body with the lever along its side
                Part(PrimitiveType.Sphere, Vector3.zero, new Vector3(.055f, .07f, .055f), 0x3d4a2e);
                Part(PrimitiveType.Cube, new Vector3(.028f, .01f, 0), new Vector3(.008f, .07f, .016f), 0x6a6a5e);
                Part(PrimitiveType.Cylinder, new Vector3(0, .04f, 0), new Vector3(.02f, .01f, .02f), 0x6a6a5e);
            }
            return root;
        }
        void AddGrenade(Unit by, Vector3 p, Vector3 v, float fuse)
        {
            var s = Gren(by.team); var m = GrenadeModel(s.stick); m.position = p;
            grenades.Add(new Grenade { p = p, v = v, fuse = fuse, t = m, by = by, rad = s.rad, dmg = s.dmg, stick = s.stick, spin = FU.Rand(540, 820) });
        }
        float cookT = -1;   // > = 0 while the player holds G: the fuse is already burning (RO2-style cooking)
        static readonly float[] STAND = { 1.55f, .95f, .4f }, SPD = { 1, .55f, .25f };
        // muzzle of each viewmodel kind (bore height, distance from the eye), matching the v4 viewmodels
        /// Where the barrel of the current v4 viewmodel actually ends, in vm space: a "muzzle" marker if the model has one, otherwise the
        /// farthest point of its meshes from the eye. The old fixed offsets (VmMuzzle) sat inside the barrel on the rifles, so the flash was
        /// hidden by the gun's own geometry.
        Transform vmMuzzleT; MeshFilter vmGunMf; Vector3[] vmGunVerts;
        /// The viewmodel's gun mesh (and its vertices), for VmMuzzleNow(). A "muzzle" marker, if the model has one, is used directly.
        Transform FindVmMuzzle()
        {
            vmGunMf = null; vmGunVerts = null; if (!vm) return null; var mk = ModelLib.Find(vm, "muzzle"); if (mk) return mk;
            MeshFilter best = null; int bestV = -1;
            foreach (var mf in vm.GetComponentsInChildren<MeshFilter>(false))
            {   // the gun is the biggest active mesh that isn't an arm / hand part; hidden clip / round / follower parts don't count
                string n = mf.name.ToLowerInvariant(); if (!mf.sharedMesh || !mf.gameObject.activeInHierarchy || n.Contains("arm") || n.Contains("hand") || n.Contains("grip") || n.Contains("sleeve") || n.Contains("glove") || n.Contains("fist") || n == "clip" || n == "round" || n == "follower" || n == "port") continue;
                if (mf.sharedMesh.vertexCount > bestV) { bestV = mf.sharedMesh.vertexCount; best = mf; }
            }
            if (!best) return null; vmGunMf = best; vmGunVerts = best.sharedMesh.vertices; return null;
        }
        /// Where the gun's barrel ends right now: the gun mesh's vertex farthest from the eye, in world space. Measured from the
        /// rendered geometry each shot, so it is right whatever the model's pivot, axes or current animation pose.
        Vector3? VmMuzzleNow()
        {
            if (vmMuzzleT) return vmMuzzleT.position; if (!vmGunMf || vmGunVerts == null) return null;
            var l2w = vmGunMf.transform.localToWorldMatrix; var eye = cam.transform.position; float bd = -1; Vector3 bp = Vector3.zero;
            for (int i = 0; i < vmGunVerts.Length; i++) { var wp = l2w.MultiplyPoint3x4(vmGunVerts[i]); float dd = (wp - eye).sqrMagnitude; if (dd > bd) { bd = dd; bp = wp; } }
            return bp;
        }
        static Vector3 VmMuzzle(string k) => k == "pistol" ? new Vector3(0, -.017f, .38f) : k == "smg" ? new Vector3(0, -.031f, .75f) : k == "bar" ? new Vector3(0, -.037f, 1.05f)
            : k == "mg" ? new Vector3(0, -.046f, 1.15f) : k == "ckm" ? new Vector3(0, -.087f, 1.3f) : k == "sniper" ? new Vector3(0, -.068f, 1.0f) : new Vector3(0, -.026f, 1.0f);
        static readonly Dictionary<string, Vector4> HIP = new Dictionary<string, Vector4>
        {   // x, y, z, ry (three.js camera space; z and ry are mirrored when applied)
            ["sniper"] = new Vector4(.21f, -.19f, -.02f, .06f), ["rifle"] = new Vector4(.21f, -.19f, -.02f, .06f), ["smg"] = new Vector4(.2f, -.18f, -.02f, .07f), ["bar"] = new Vector4(.2f, -.18f, -.03f, .06f),
            ["mg"] = new Vector4(.19f, -.17f, -.03f, .05f), ["ckm"] = new Vector4(.19f, -.15f, -.03f, .05f), ["pistol"] = new Vector4(.13f, -.12f, -.02f, .04f), ["knife"] = new Vector4(.18f, -.1f, .02f, -.5f), ["sabre"] = new Vector4(.2f, -.08f, .02f, -.6f)
        };

        // pickups / banners / planes / fires
        class Pickup { public float x, z, t; public bool med; public Transform g; }
        readonly List<Pickup> pickups = new List<Pickup>();
        class Banner { public string a, b; public Color c; public float t; }
        Banner banner;
        class Plane { public Transform t, prop; public float t0, speed, lat, life, x, z, dx, dz, y; public bool strike; public bool[] dropped; public int team; public AudioSource snd, eng; }
        readonly List<Plane> planes = new List<Plane>();
        class Fire { public Vector3 p; public bool big; public float life; public Obst b; }
        readonly List<Fire> fires = new List<Fire>();
        class Marker { public Transform t; public float life; }
        class PingFx { public Transform t; public float life; public Renderer r; public Color c; }
        readonly List<PingFx> pings = new List<PingFx>(); Transform[] spRings; Light inLight; MaterialPropertyBlock pingMpb;

        // ---------------- pre-game deployment (SD2 phase A on a 1939 scale) ----------------
        /// One tray card = one formation to place: a rifle section, an MG team, a gun, a tank, a cavalry troop, a sniper.
        /// Both sides deploy through cards: the player by hand (or the Auto button), the computer through PlaceCardAi.
        class DeployCard { public string type, label; public int n; public float sp; public bool placed, star; public Vector2 pos; public float face; public Transform ring; public List<Unit> units = new List<Unit>(); }
        readonly List<DeployCard> cards = new List<DeployCard>(); int cardSel = -1; float deployT, deployFace; Rect dz; GameObject dzMesh; LineRenderer dzLine; Transform ghost; Renderer ghostR;
        readonly List<(string type, int n)>[] reserve = { new List<(string, int)>(), new List<(string, int)>() };   // per team: formations left in the tray (player) or with no valid spot (AI); they arrive with the first reinforcement wave
        const float DeployTime = 90;
        bool InDeploy(float x, float z) => InZone(dz, x, z);
        static bool InZone(Rect z, float x, float z2) => x > z.xMin && x < z.xMax && z2 > z.yMin && z2 < z.yMax;
        /// The strip of ground a side may deploy on. Mission data (`deploy0/1` = x0, z0, x1, z1) wins; otherwise: the attacker gets everything
        /// from his map edge to 140 m short of the first objective, the defender everything from 35 m in front of the first objective back to his edge.
        Rect DeployZone(int team)
        {
            var d = team == 0 ? M.deploy0 : M.deploy1;
            if (d != null && d.Length >= 4) return Rect.MinMaxRect(Mathf.Min(d[0], d[2]), Mathf.Min(d[1], d[3]), Mathf.Max(d[0], d[2]), Mathf.Max(d[1], d[3]));
            var s = M.spawn[team]; var e = M.spawn[1 - team]; float dir = e.z >= s.z ? 1 : -1, H = M.half, a, b;
            if (World.points.Count == 0) { a = s.z - dir * 40; b = s.z + dir * 150; }
            else if (team == M.attacker) { a = s.z - dir * 40; b = World.points[0].z - dir * 140; if ((b - a) * dir < 60) b = a + dir * 60; }
            else { a = World.points[0].z + dir * 35; b = s.z - dir * 60; if ((b - a) * (-dir) < 80) b = a - dir * 80; }   // defender: dir points at the attacker
            return Rect.MinMaxRect(-H + 10, Mathf.Clamp(Mathf.Min(a, b), -H + 10, H - 10), H - 10, Mathf.Clamp(Mathf.Max(a, b), -H + 10, H - 10));
        }
        /// A side's `forces` as tray cards: big rifle entries become sections of 8, tanks go one by one, everything else stays one card.
        List<DeployCard> MakeCards(int team)
        {
            var list = new List<DeployCard>(); float face = FaceEnemy(team);
            foreach (var f in M.Forces(team))
            {
                if ((f.type == "cav" && team == 0) || !Defs.BASE.ContainsKey(f.type)) continue; int left = f.n;
                while (left > 0)
                {
                    int n = f.type == "rifle" ? Mathf.Min(left, left > 12 ? 8 : left) : f.type == "tank" ? 1 : left; left -= n;
                    string nm = Defs.NAMES[team][f.type];
                    var c = new DeployCard { type = f.type, n = n, sp = f.type == "rifle" ? Mathf.Clamp(n * 1.4f, 6, 12) : Mathf.Min(f.sp, 10), face = face, label = f.type == "rifle" ? (n >= 6 ? "Rifle section ×" + n : "Rifle team ×" + n) : f.type == "mg" ? (n > 1 ? "MG teams ×" + n : "MG team") : f.type == "at" ? (n > 1 ? "AT guns ×" + n : "AT gun") : f.type == "cav" ? "Cavalry troop ×" + n : n > 1 ? nm + " ×" + n : nm };
                    // the force's historical objective is remembered as a hint for the placer (defenders are pre-placed from it)
                    if (f.loc >= 0 && World.points.Count > 0) { var p = World.points[Mathf.Min(f.loc, World.points.Count - 1)]; c.pos = new Vector2(p.x, p.z); } else c.pos = new Vector2(M.spawn[team].x, M.spawn[team].z);
                    list.Add(c);
                }
            }
            var first = list.FirstOrDefault(c => c.type == "rifle"); if (first != null) first.star = true;
            return list;
        }
        /// The player's tray: defenders start with every card already placed where history put it (by the same scorer the AI uses), attackers with an empty map.
        void BuildCards(int team)
        {
            cards.Clear(); cards.AddRange(MakeCards(team)); cardSel = -1; deployFace = FaceEnemy(team);
            if (team != M.attacker) { var taken = new HashSet<Structures.Cover>(); foreach (var c in cards) if (PlaceCardAi(team, c, taken)) c.placed = true; }
            foreach (var c in cards) if (c.placed) CardRing(c);
            cardSel = cards.FindIndex(c => !c.placed);
        }
        void CardRing(DeployCard c)
        {
            if (!c.ring) { var g = new GameObject("deploy " + c.label); g.AddComponent<MeshFilter>().sharedMesh = World.RingMesh(.86f, 1, 40); var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = Mats.Transparent(new Color(.45f, 1, .5f, .55f), null, false, true); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; c.ring = g.transform; }
            c.ring.position = new Vector3(c.pos.x, World.H(c.pos.x, c.pos.y) + .45f, c.pos.y); c.ring.localScale = new Vector3(c.sp + 2, 1, c.sp + 2);
        }
        /// Translucent, terrain-hugging sheet over the deployment zone plus a bright border line.
        void BuildZoneMesh()
        {
            if (dzMesh) Destroy(dzMesh);
            float step = 6; int nx = Mathf.CeilToInt(dz.width / step) + 1, nz = Mathf.CeilToInt(dz.height / step) + 1;
            var v = new Vector3[nx * nz]; var tri = new int[(nx - 1) * (nz - 1) * 6]; int k = 0;
            for (int j = 0; j < nz; j++) for (int i = 0; i < nx; i++)
                {
                    float x = Mathf.Min(dz.xMax, dz.xMin + i * step), z = Mathf.Min(dz.yMax, dz.yMin + j * step); v[j * nx + i] = new Vector3(x, World.H(x, z) + .3f, z);
                    if (i < nx - 1 && j < nz - 1) { int a = j * nx + i; tri[k++] = a; tri[k++] = a + nx; tri[k++] = a + 1; tri[k++] = a + 1; tri[k++] = a + nx; tri[k++] = a + nx + 1; }
                }
            var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; m.vertices = v; m.triangles = tri; m.RecalculateBounds();
            dzMesh = new GameObject("DeployZone"); dzMesh.AddComponent<MeshFilter>().sharedMesh = m; var mr = dzMesh.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Mats.Transparent(new Color(.35f, .95f, .45f, .22f), null, false, true); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            // border: a line that follows the ground around the rectangle
            var lg = new GameObject("DeployZoneEdge"); lg.transform.SetParent(dzMesh.transform, false); dzLine = lg.AddComponent<LineRenderer>();
            var pts = new List<Vector3>(); void Edge(float x0, float z0, float x1, float z1) { int n = Mathf.Max(2, Mathf.CeilToInt(FU.Hyp(x1 - x0, z1 - z0) / 8)); for (int i = 0; i < n; i++) { float x = Mathf.Lerp(x0, x1, i / (float)n), z = Mathf.Lerp(z0, z1, i / (float)n); pts.Add(new Vector3(x, World.H(x, z) + .6f, z)); } }
            Edge(dz.xMin, dz.yMin, dz.xMax, dz.yMin); Edge(dz.xMax, dz.yMin, dz.xMax, dz.yMax); Edge(dz.xMax, dz.yMax, dz.xMin, dz.yMax); Edge(dz.xMin, dz.yMax, dz.xMin, dz.yMin);
            dzLine.positionCount = pts.Count; dzLine.SetPositions(pts.ToArray()); dzLine.loop = true; dzLine.widthMultiplier = 1.2f; dzLine.sharedMaterial = Mats.Transparent(new Color(.5f, 1, .55f, .9f), null, false, true); dzLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; dzLine.receiveShadows = false;
            // ghost: follows the mouse while a card is selected, green where the card can go, red where it cannot
            var gg = new GameObject("DeployGhost"); gg.transform.SetParent(dzMesh.transform, false); gg.AddComponent<MeshFilter>().sharedMesh = World.RingMesh(.8f, 1, 40); ghostR = gg.AddComponent<MeshRenderer>(); ghostR.sharedMaterial = Mats.Transparent(Color.white, null, false, true); ghostR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; ghostR.receiveShadows = false; ghost = gg.transform; gg.SetActive(false);
        }
        void ClearDeploy() { reserve[0].Clear(); reserve[1].Clear(); foreach (var c in cards) if (c.ring) Destroy(c.ring.gameObject); cards.Clear(); cardSel = -1; if (dzMesh) Destroy(dzMesh); dzMesh = null; dzLine = null; ghost = null; ghostR = null; }
        /// Everything a card needs to stand on the ground: inside the zone, dry, not inside a building (infantry may use buildings — they get the floor).
        bool CardSpotOk(DeployCard c, float x, float z) => InDeploy(x, z) && !World.InWater(x, z) && (c.type == "rifle" || c.type == "sniper" || c.type == "mg" || !World.InBuildingG(x, z, 2));

        // ---- the computer's placer (also behind the player's Auto button and the defender's pre-placement) ----
        /// Picks a position for one card inside `team`'s zone. Defenders: riflemen on cover facing the approach (trenches and low walls
        /// first), MG teams on the cover with the longest clear sightline down the approach, AT guns on low cover that sees a road, snipers
        /// well back. Attackers: an assembly area along the zone's front edge, out of sight of the first objective where the ground allows,
        /// tanks on or beside a road, guns and snipers a little behind. `taken` keeps formations from stacking on one spot.
        bool PlaceCardAi(int team, DeployCard c, HashSet<Structures.Cover> taken)
        {
            var zone = team == ME && state == GState.Deploy ? dz : DeployZone(team); var es = M.spawn[1 - team];
            float Face(float x, float z) => Mathf.Atan2(es.x - x, es.z - z);
            bool Crowded(float x, float z, float r) => cards.Any(o => o != c && o.placed && FU.Hyp(o.pos.x - x, o.pos.y - z) < r) || aiPlaced.Any(o => FU.Hyp(o.x - x, o.y - z) < r);
            float Sight(float x, float z)
            {   // how far a gun here can see down the approach: longest clear ray toward the enemy side, ±35°
                float best = 0, h0 = Face(x, z); var eye = new Vector3(x, World.H(x, z) + 1.1f, z);
                for (int k = -3; k <= 3; k++) { float a = h0 + k * .3f; for (float d = 60; d <= 260; d += 50) { var p = new Vector3(x + Mathf.Sin(a) * d, 0, z + Mathf.Cos(a) * d); p.y = World.H(p.x, p.z) + 1; if (Mathf.Abs(p.x) > M.half || Mathf.Abs(p.z) > M.half || !World.LOS(eye, p)) break; best = Mathf.Max(best, d); } }
                return best;
            }
            if (team != M.attacker && World.points.Count > 0)
            {
                var p = c.pos; float toE = Face(p.x, p.y);   // c.pos = the historical objective (MakeCards)
                bool FacesEnemy(Structures.Cover k) { if (!k.n.HasValue) return true; float ex = es.x - k.x, ez = es.z - k.z, l = Mathf.Max(.01f, FU.Hyp(ex, ez)); return (k.n.Value.x * ex + k.n.Value.y * ez) / l < -.25f; }
                bool Free(Structures.Cover k) => !taken.Contains(k) && (k.b == null || !k.b.dead) && InZone(zone, k.x, k.z) && !World.InBuildingG(k.x, k.z, .3f) && !World.InWater(k.x, k.z) && Nav.Passable(k.x, k.z, false) && !Crowded(k.x, k.z, c.type == "rifle" ? 7 : 3);
                var cov = Structures.Near(p.x, p.y, c.type == "mg" || c.type == "at" || c.type == "sniper" ? 70 : 48).Where(k => FacesEnemy(k) && Free(k)).ToList();
                float Fwd(Structures.Cover k) => (k.x - p.x) * Mathf.Sin(toE) + (k.z - p.y) * Mathf.Cos(toE);
                var sightOf = new Dictionary<Structures.Cover, float>(); float S(Structures.Cover k) { if (!sightOf.TryGetValue(k, out var v)) sightOf[k] = v = Sight(k.x, k.z); return v; }
                Structures.Cover pick =
                    c.type == "mg" ? cov.OrderByDescending(k => S(k) + (k.trench ? 40 : k.low ? 20 : 0) + Fwd(k) * .3f).FirstOrDefault()
                    : c.type == "at" ? (cov.Where(k => (k.low || k.trench) && S(k) >= 110).OrderByDescending(k => (World.NearRoadG(k.x, k.z, 14) ? 30 : 0) + S(k)).FirstOrDefault() ?? cov.Where(k => S(k) >= 60).OrderByDescending(k => S(k)).FirstOrDefault())   // the longest field of fire wins; the road is a tie-breaker (tanks flank, they do not drive up the road). No low cover with a view: any cover with one, never the blind spot in the middle of the farmyard
                    : c.type == "sniper" ? cov.OrderBy(k => Fwd(k) - S(k) * .4f).FirstOrDefault()
                    : cov.OrderByDescending(k => (k.trench ? 30 : k.low ? 22 : 12) + Mathf.Clamp(Fwd(k), -30, 30) * .5f + FU.Rand(0, 14) - FU.Hyp(k.x - p.x, k.z - p.y) * .35f).FirstOrDefault();
                if (pick != null) { taken.Add(pick); c.pos = new Vector2(pick.x, pick.z); c.face = Face(pick.x, pick.z); return true; }
                var q = FreeSpot(p.x + Mathf.Sin(toE) * 6, p.y + Mathf.Cos(toE) * 6, 12, 1); if (!InZone(zone, q.x, q.y)) return false; c.pos = q; c.face = toE; return true;
            }
            else
            {   // attacker (or a map without objectives): assembly area
                var s = M.spawn[team]; float dir = es.z >= s.z ? 1 : -1, front = dir > 0 ? zone.yMax - 12 : zone.yMin + 12; Vector3 obj = World.points.Count > 0 ? new Vector3(World.points[0].x, World.H(World.points[0].x, World.points[0].z) + 1.6f, World.points[0].z) : new Vector3(es.x, 0, es.z);
                float depth = c.type == "tank" ? 4 : c.type == "rifle" || c.type == "cav" ? 10 : c.type == "mg" ? 22 : 34;   // tanks lead, sections next, MGs and guns behind
                Vector2 best = default; float bs = -1e9f;
                for (int i = 0; i < 28; i++)
                {
                    float x = Mathf.Lerp(zone.xMin + 10, zone.xMax - 10, (i + .5f) / 28f) + FU.Rand(-6, 6), z = front - dir * (depth + FU.Rand(0, 12));
                    if (!InZone(zone, x, z) || World.InWater(x, z) || World.InBuildingG(x, z, c.sp) || !Nav.Passable(x, z, c.type == "tank") || Crowded(x, z, c.type == "rifle" ? 14 : 9)) continue;
                    float sc = -Mathf.Abs(x - s.x) * .08f;                                                                  // stay near the axis of advance
                    if (c.type == "tank" || c.type == "at") sc += World.NearRoadG(x, z, 12) ? 25 : 0;                        // vehicles and guns by the road
                    if (!World.LOS(new Vector3(x, World.H(x, z) + 1.6f, z), obj)) sc += 18;                                 // out of sight of the first objective
                    if (c.type == "mg" && Sight(x, z) >= 160) sc += 12;                                                      // an MG that can already see something useful
                    if (sc > bs) { bs = sc; best = new Vector2(x, z); }
                }
                if (bs <= -1e8f) { var q = FreeSpot(s.x, s.z, 20, 1); if (!InZone(zone, q.x, q.y)) return false; best = q; }
                c.pos = best; c.face = Face(best.x, best.y); return true;
            }
        }
        readonly List<Vector2> aiPlaced = new List<Vector2>();
        /// The computer deploys its whole tray through the same cards and the same placer, then spawns only what it placed.
        void AiDeploy(int team)
        {
            aiPlaced.Clear(); var taken = new HashSet<Structures.Cover>(); int placed = 0, kept = 0;
            foreach (var c in MakeCards(team))
            {
                if (!PlaceCardAi(team, c, taken)) { kept++; reserve[team].Add((c.type, c.n)); continue; }   // nowhere valid for it: it arrives with the first wave, like an unplaced player card (it used to be dropped)
                aiPlaced.Add(c.pos); SpawnCard(team, c); placed++;
            }
            aiPlaced.Clear();
            if (kept > 0) Debug.Log($"[FallWeiss] AI side {team}: {placed} formations deployed, {kept} had no valid spot and wait for the first wave");
        }
        /// Puts a card on the ground: n units in its footprint, facing its way, homes set so the Director's leash keeps them there.
        void SpawnCard(int team, DeployCard c)
        {
            float r = Defs.For(team, c.type).r; sectCounter++;
            for (int i = 0; i < c.n; i++)
            {
                var q = i == 0 && SpotOk(c.pos.x, c.pos.y, r) ? c.pos : FreeSpot(c.pos.x, c.pos.y, c.sp, r);
                var u = SpawnUnit(team, c.type, q.x, q.y, c.face); u.home = q; c.units.Add(u); u.sect = sectCounter;
                if (i == 0 && (c.type == "mg" || c.type == "at" || c.type == "sniper")) { var cv = Structures.Find(u, M.spawn[1 - team].x, M.spawn[1 - team].z, t); if (cv != null && FU.Hyp(cv.x - q.x, cv.z - q.y) < 4) u.hide = new Hide { x = cv.x, z = cv.z, t = 40, fire = true, low = cv.low, trench = cv.trench }; }
            }
        }
        void UpdateDeploy(float dt, Keyboard kb, Mouse ms)
        {
            deployT -= dt;
            // camera: same controls as the command map
            float pan = rtsDist * .9f * dt; Vector3 fw = new Vector3(Mathf.Sin(rtsYaw), 0, Mathf.Cos(rtsYaw)), rt = new Vector3(fw.z, 0, -fw.x);
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) rtsPos += fw * pan; if (kb.sKey.isPressed || kb.downArrowKey.isPressed) rtsPos -= fw * pan;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) rtsPos += rt * pan; if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) rtsPos -= rt * pan;
            if (kb.qKey.isPressed) rtsYaw -= dt * 1.5f; if (kb.eKey.isPressed) rtsYaw += dt * 1.5f;
            float sc = ms.scroll.ReadValue().y; if (Mathf.Abs(sc) > .1f) rtsDist = Mathf.Clamp(rtsDist * (sc > 0 ? .88f : 1.14f), 20, 320);
            float H = M.half; rtsPos.x = Mathf.Clamp(rtsPos.x, -H, H); rtsPos.z = Mathf.Clamp(rtsPos.z, -H, H); rtsPos.y = World.H(rtsPos.x, rtsPos.z);
            var camTarget = rtsPos - fw * rtsDist * .55f + Vector3.up * rtsDist * .85f;
            cam.transform.position = Vector3.Lerp(cam.transform.position, camTarget, Mathf.Min(1, dt * 8)); cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(rtsPos - camTarget), Mathf.Min(1, dt * 8)); cam.fieldOfView = 60;
            // tray keys
            for (int i = 0; i < Mathf.Min(9, cards.Count); i++) if (kb[Key.Digit1 + i].wasPressedThisFrame) cardSel = cardSel == i ? -1 : i;
            if (kb.tabKey.wasPressedThisFrame && cards.Count > 0) { int st = cardSel; for (int k = 1; k <= cards.Count; k++) { int j = (st + k) % cards.Count; if (!cards[j].placed) { cardSel = j; break; } } }
            if (kb.rKey.wasPressedThisFrame) { deployFace += Mathf.PI / 4; if (cardSel >= 0 && cards[cardSel].placed) cards[cardSel].face = deployFace; }
            if (kb.xKey.wasPressedThisFrame && cardSel >= 0 && cards[cardSel].type == "rifle") { foreach (var c in cards) c.star = false; cards[cardSel].star = true; Msg("You will take the field with " + cards[cardSel].label + "."); }
            if (kb.deleteKey.wasPressedThisFrame && cardSel >= 0 && cards[cardSel].placed) Unplace(cards[cardSel]);
            if (kb.vKey.wasPressedThisFrame) AutoPlace();
            Vector2 mp = ms.position.ReadValue(); float sS = Screen.height / 1080f; bool overHud = mp.y < 206 * sS || Screen.height - mp.y < 136 * sS || miniRect.Contains(new Vector2(mp.x / sS, (Screen.height - mp.y) / sS));
            Vector3 gp = default; bool onGround = !overHud && GroundAt(mp, out gp);
            // ghost ring under the cursor
            if (ghost)
            {
                bool show = cardSel >= 0 && onGround; ghost.gameObject.SetActive(show);
                if (show) { var c = cards[cardSel]; bool ok = CardSpotOk(c, gp.x, gp.z); ghost.position = new Vector3(gp.x, gp.y + .5f, gp.z); ghost.localScale = new Vector3(c.sp + 2, 1, c.sp + 2); ghostR.sharedMaterial = Mats.Transparent(ok ? new Color(.5f, 1, .55f, .7f) : new Color(1, .35f, .3f, .7f), null, false, true); }
            }
            if (ms.leftButton.wasPressedThisFrame && onGround)
            {
                if (cardSel >= 0)
                {
                    var c = cards[cardSel];
                    if (CardSpotOk(c, gp.x, gp.z)) { c.pos = new Vector2(gp.x, gp.z); c.face = deployFace; c.placed = true; CardRing(c); PingAt(gp.x, gp.z, UI.Green); cardSel = cards.FindIndex(k => !k.placed); }
                    else { PingAt(gp.x, gp.z, UI.Red); Msg(InDeploy(gp.x, gp.z) ? "Not there — water or a building." : "Outside your deployment zone."); }
                }
                else
                {   // click a placed formation to pick it up again
                    int best = -1; float bd = 1e9f; for (int i = 0; i < cards.Count; i++) if (cards[i].placed) { float d = FU.Hyp(cards[i].pos.x - gp.x, cards[i].pos.y - gp.z); if (d < cards[i].sp + 3 && d < bd) { bd = d; best = i; } }
                    if (best >= 0) { cardSel = best; Unplace(cards[best]); }
                }
            }
            if (ms.rightButton.wasPressedThisFrame && onGround && cardSel >= 0 && cards[cardSel].placed)
            {   // right-click: face the formation toward the point
                var c = cards[cardSel]; c.face = Mathf.Atan2(gp.x - c.pos.x, gp.z - c.pos.y); deployFace = c.face; PingAt(gp.x, gp.z, UI.Gold);
            }
            if (kb.spaceKey.wasPressedThisFrame) { rtsPos.x = dz.center.x; rtsPos.z = dz.center.y; }
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || deployT <= 0) BeginBattle();
        }
        void Unplace(DeployCard c) { c.placed = false; if (c.ring) { Destroy(c.ring.gameObject); c.ring = null; } }
        /// Auto button / V: the computer's placer fills in whatever the player has not placed yet.
        void AutoPlace()
        {
            var taken = new HashSet<Structures.Cover>(); int n = 0;
            foreach (var c in cards) if (!c.placed && PlaceCardAi(ME, c, taken)) { c.placed = true; CardRing(c); n++; }
            if (n > 0) Msg(n + " formation" + (n > 1 ? "s" : "") + " placed automatically."); cardSel = cards.FindIndex(c => !c.placed);
        }
        /// Only what was placed takes the field. Unplaced cards go to the reserve and arrive with the first reinforcement wave.
        void BeginBattle()
        {
            if (state != GState.Deploy) return;
            reserve[ME].Clear();
            foreach (var c in cards)
            {
                if (!c.placed) { reserve[ME].Add((c.type, c.n)); continue; }
                SpawnCard(ME, c); if (c.ring) { Destroy(c.ring.gameObject); c.ring = null; }
            }
            if (dzMesh) { Destroy(dzMesh); dzMesh = null; dzLine = null; ghost = null; ghostR = null; }
            var star = cards.FirstOrDefault(c => c.star && c.placed) ?? cards.FirstOrDefault(c => c.type == "rifle" && c.placed);
            Unit lead = star?.units.Where(u => u.alive && u.type == "rifle").OrderBy(u => FU.Hyp(u.x - star.pos.x, u.z - star.pos.y)).FirstOrDefault();
            int held = reserve[ME].Sum(r => r.n); cards.Clear(); cardSel = -1;
            TakeTheField(lead);
            if (held > 0) Msg($"{reserve[ME].Count} formation{(reserve[ME].Count > 1 ? "s" : "")} ({held} men / vehicles) held in reserve — they arrive with the first reinforcement wave.");
        }
        /// Reserve formations join the player's first wave at the rally point.
        List<Unit> SpawnReserve(int team)
        {
            var o = new List<Unit>(); var res = reserve[team]; if (res.Count == 0) return o; var b = SpawnPos(team);
            foreach (var (type, n) in res) { if (!Defs.BASE.ContainsKey(type)) continue; sectCounter++; for (int i = 0; i < n; i++) { var p = FreeSpot(b.x, b.z, 16, 1); var nu = SpawnUnit(team, type, p.x, p.y, FaceEnemy(team)); nu.sect = sectCounter; o.Add(nu); spawned[team]++; } }
            if (team == ME) Msg("Reserve formations have arrived at the rally point."); res.Clear(); return o;
        }


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
                    g.transform.localScale = new Vector3(SPR, 1, SPR); spRings[tm] = g.transform;
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
            audio.Interior(on > 0);
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
            sens = PlayerPrefs.GetFloat("FW.Sens", 1); baseFov = PlayerPrefs.GetFloat("FW.Fov", 72); SoundBank.MasterVol = PlayerPrefs.GetFloat("FW.VolMaster", 1); SoundBank.OwnVol = PlayerPrefs.GetFloat("FW.VolOwn", 1); SoundBank.WorldVol = PlayerPrefs.GetFloat("FW.VolWorld", 1); SoundBank.ExploVol = PlayerPrefs.GetFloat("FW.VolExplo", 1); SoundBank.AmbVol = PlayerPrefs.GetFloat("FW.VolAmb", 1);
            hipDot = PlayerPrefs.GetInt("FW.HipDot3", 0) == 1;   // off by default (no reticle, RO2-style); pause-screen toggle turns it on
            hitMarkers = PlayerPrefs.GetInt("FW.HitMarkers", 1) == 1;
            hudMin = PlayerPrefs.GetInt("FW.HudMin", 0) == 1; quality = Mathf.Clamp(PlayerPrefs.GetInt("FW.Quality", 1), 0, 2); vsync = PlayerPrefs.GetInt("FW.VSync", 1) == 1; QualitySettings.vSyncCount = vsync ? 1 : 0; Application.targetFrameRate = -1;
            I = this;
            var ta = Resources.Load<TextAsset>("missions");
            missions = JsonUtility.FromJson<MissionList>(ta.text).missions;
            foreach (var m in missions) { m.lanes ??= new FLine[0]; m.rails ??= new FLine[0]; m.roads ??= new FLine[0]; m.segs ??= new FSeg[0]; m.props ??= new FProp[0]; m.landmarks ??= new FLandmark[0]; m.villages ??= new FArea[0]; m.orchards ??= new FRect[0]; m.forests ??= new FArea[0]; m.build ??= new FBuild[0]; }
            cam = Camera.main; if (!cam) { var cg = new GameObject("Main Camera"); cg.tag = "MainCamera"; cam = cg.AddComponent<Camera>(); cg.AddComponent<AudioListener>(); }
            cam.nearClipPlane = .05f; cam.farClipPlane = 1400; cam.fieldOfView = 70;
            sun = FindObjectsByType<Light>(FindObjectsInactive.Exclude).FirstOrDefault(l => l.type == LightType.Directional);
            if (!sun) { var lg = new GameObject("Sun"); sun = lg.AddComponent<Light>(); sun.type = LightType.Directional; }
            fx.Init(); audio.Init(); audio.listener = cam.transform; if (cam.GetComponent<AudioListener>() && !cam.GetComponent<MasterLimiter>()) { audio.AttachReverb(cam.gameObject); cam.gameObject.AddComponent<MasterLimiter>(); } vis.Init(cam, sun); vis.SetQuality(quality);
            EnterMenu();
        }

        // ---------------- menu with a live battle behind it ----------------
        void EnterMenu()
        {
            Time.timeScale = 1; StopAllCoroutines(); range?.Dispose(); range = null; ClearAll(); audio.StopAll(); vis.Post(true); state = GState.Menu; rts = false; Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            BuildBackdrop(menuMission);
        }
        void BuildBackdrop(int id)
        {
            ClearAll(); M = missions[id]; SoldierLib.forceAll = M.range; ME = menuSide; EN = 1 - ME; World.Build(M); vis.Apply(M); unitRoot = new GameObject("Units").transform;
            t = 60; tix[0] = tix[1] = 999; aiT = 2; waveNext[0] = waveNext[1] = null; flyT = 8; barT = 2; barrageLeft = 6; shipT = 4;
            for (int s = 0; s < 2; s++) foreach (var f in M.Forces(s)) PlaceForce(s, f);
            foreach (var u in units) u.prot = 0; Fires(); Structures.BuildCover(); BuildLines(); Nav.Build(); BuildConvoy(); menuBuilt = true; menuOrbit = Random.value * 6.28f;
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
        public void StartMission(int id, int side) => StartMission(id, side, true);
        public void StartMission(int id, int side, bool deployPhase)
        {
            missionIdx = id; if (!missions[id].range) menuMission = id; M = missions[id]; ME = side; EN = 1 - side;
            range?.Dispose(); range = null; ClearAll(); SoldierLib.forceAll = M.range; if (M.range) { ME = 0; EN = 1; }
            World.Build(M); vis.Apply(M);
            audio.StopAll(); audio.Ambience("amb_wind", .22f); if (!M.range) audio.Ambience("amb_battle", .07f); warT = 4;   // the generic battle bed is now mostly replaced by positional off-map fire (UpdateAmbience)   // beds sat at ~-30 dBFS and masked every shot past 30 m   // countryside wind bed + far-off fighting
            unitRoot = new GameObject("Units").transform;
            t = 0; tix[0] = tix[1] = M.tickets; tix[1 - M.attacker] = Mathf.RoundToInt(M.tickets * 1.6f); kills = losses = 0; cp = 120;
            aiT = 2; raidT = 75; aiSup = 140; supportCd = 0; waveNext[0] = waveNext[1] = null; msgs.Clear(); nextId = 1; slotCounter = 0; selected.Clear();
            streak = 0; streakT = 0; shipT = 6; barT = 4; barrageLeft = M.barrage; flyT = 25; resT = 5; banner = null;
            bool deploy = deployPhase && !M.range;
            if (M.range) { range = new TestRange(this); range.Setup(); }
            Fires(); if (minimap) Destroy(minimap); minimap = Visuals.BakeMinimap(M); Structures.BuildCover(); BuildLines(); Nav.Build(); BuildConvoy(); respSquad = false; lastClass = 0; breath = 1; menuBuilt = false;
            if (deploy)
            {   // the enemy takes up his positions now; yours wait in the tray until you place them (or the 90 s run out)
                AiDeploy(EN);
                state = GState.Deploy; rts = true; Time.timeScale = 1; dz = DeployZone(ME); BuildCards(ME); BuildZoneMesh(); deployT = DeployTime;
                rtsPos = new Vector3(dz.center.x, 0, dz.center.y); rtsYaw = FaceEnemy(ME); rtsDist = 180; cam.transform.position = rtsPos + Vector3.up * 150; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; vis.Tod(0); vis.RtsFog(true); vis.Post(false);
                ShowBanner("DEPLOYMENT", M.name + " — place your forces inside the green zone", UI.Gold);
                Msg("Place your formations inside the green zone. V places the rest automatically. Enter begins.");
                return;
            }
            for (int s = 0; s < 2; s++) AiDeploy(s);   // no deployment phase (test runs): both sides are placed by the same placer
            state = GState.Playing;
            TakeTheField(null);
        }
        /// Start of play proper: the Director's clock, the whistle, and the player dropped into a squad.
        /// `lead` = the rifleman to play (the starred deployment section); otherwise the man standing in the thickest knot of friendly
        /// infantry — for the attacker the group nearest his spawn, for the defender the group holding the first objective — is picked
        /// and the eight men around him are recruited. (Before, a defender was dropped at the base spawn 100–200 m behind everyone.)
        void TakeTheField(Unit lead)
        {
            state = GState.Playing; t = 0; waveNext[0] = waveNext[1] = null; tixT = -99; tixRate = 0;
            if (!M.range) Msg(ME == M.attacker ? "ATTACK: take the objectives in order. Every death costs one reinforcement." : "DEFEND: hold the line until the clock runs out. Lost positions cannot be retaken.");
            var sp0 = M.spawn[ME]; var anchor = ME == M.attacker || World.points.Count == 0 ? new Vector2(sp0.x, sp0.z) : new Vector2(World.points[0].x, World.points[0].z);
            var inf = units.Where(u => u.team == ME && u.alive && u.def.kind == "inf").ToList();
            Unit leader = lead != null && lead.alive ? lead : inf.Where(u => u.type == "rifle").OrderByDescending(u => inf.Count(o => FU.Hyp(o.x - u.x, o.z - u.z) < 20) - FU.Hyp(u.x - anchor.x, u.z - anchor.y) * .02f).FirstOrDefault();
            if (leader == null) { var q = FreeSpot(anchor.x, anchor.y, 10, .6f); leader = SpawnUnit(ME, "rifle", q.x, q.y, FaceEnemy(ME)); }
            leader.InitWeapons(Defs.CLASSES[0].weps[ME]); leader.gren = Defs.CLASSES[0].gren; leader.cls = 0; leader.officer = false; classIdx = 0; leader.secure = -1; leader.order = null;
            foreach (var u in inf.Where(u => u != leader && FU.Hyp(u.x - leader.x, u.z - leader.z) < 40).OrderBy(u => FU.Hyp(u.x - leader.x, u.z - leader.z)).Take(8)) { u.squad = true; u.follow = true; u.slot = slotCounter++; u.secure = -1; u.order = null; }
            rts = false; Time.timeScale = 1; selected.Clear(); vis.Tod(0);
            EnterFps(leader);
            ShowBanner(M.name.ToUpper(), M.range ? M.date : M.date + " — " + (ME == M.attacker ? "attack" : "defend"), UI.Gold);
            if (!M.range) Msg("Hold RMB to aim. V = squad orders · B = support · Tab = command map · Esc = pause and controls.");
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
            System.Array.Clear(killLog, 0, killLog.Length); playerZero.Clear(); zeroShowT = 0;
            AudioListener.pause = false; Time.timeScale = 1;   // Restart / Quit from the pause screen used to carry the paused listener into the next mission (silent until the next Esc-Esc)
            StopAllCoroutines(); spStage[0] = spStage[1] = -99; sgT.Clear();
            foreach (var u in units) if (u.go) Destroy(u.go); units.Clear(); player = lastPlayer = null; if (unitRoot) Destroy(unitRoot.gameObject);
            if (vm) { Destroy(vm.gameObject); vm = null; vmKind = null; vmFist = null; }
            foreach (var p in pickups) if (p.g) Destroy(p.g.gameObject); pickups.Clear();
            foreach (var p in planes) if (p.t) Destroy(p.t.gameObject); planes.Clear();
            foreach (var c in convoy) if (c.m) Destroy(c.m.gameObject); convoy.Clear();
            foreach (var b in beams) if (b.t) Destroy(b.t.gameObject); beams.Clear(); tp = orderOpen = buyOpen = false; armSupport = armSecure = dragging = false; cookT = meleeT = -1; if (tankVm) { Destroy(tankVm.gameObject); tankVm = null; tankVmTeam = -1; } AudioListener.pause = false; dmgSrc.Clear(); foreach (var l in pathLines) if (l) l.enabled = false;
            foreach (var m in markers) if (m.t) Destroy(m.t.gameObject); markers.Clear();
            foreach (var p in pings) if (p.t) Destroy(p.t.gameObject); pings.Clear(); if (spRings != null) foreach (var r in spRings) if (r) Destroy(r.gameObject); spRings = null;
            grenades.ForEach(gr => { if (gr.t) Destroy(gr.t.gameObject); }); grenades.Clear(); foreach (var r in rounds) fx.EndStreak(r.lr); rounds.Clear(); secureState.Clear(); armSecure = false; smokes.ForEach(sm => { if (sm.t) Destroy(sm.t.gameObject); }); smokes.Clear(); aiSmokeT[0] = aiSmokeT[1] = 0; shells.Clear(); fires.Clear(); selected.Clear();
            ClearDeploy(); fx.Clear(); World.Clear();
        }

        float FaceEnemy(int team) { var a = M.spawn[team]; var b = M.spawn[1 - team]; return Mathf.Atan2(b.x - a.x, b.z - a.z); }

        public Unit SpawnUnit(int team, string type, float x, float z, float heading)
        {
            var u = new Unit { id = nextId++, team = team, type = type, def = Defs.For(team, type), x = x, z = z, heading = heading, turretH = heading };
            if (type == "rifle" && u.id % 8 == 0) u.officer = true;   // a squad leader in every few riflemen (MP 38 + Luger / wz.29 + Vis)
            u.y = World.H(x, z); u.hp = u.max = u.def.hp; u.home = new Vector2(x, z); u.cd = Random.value; u.think = Random.value * .3f; u.anim = Random.value * 6; u.prot = t + 3;
            if (u.def.kind == "inf" && type == "rifle") u.gren = u.officer ? 2 : (u.id % 3 == 0 ? 2 : 1);   // riflemen carry one or two
            u.BuildModel(unitRoot); units.Add(u);
            if (u.def.kind == "tank" || u.def.kind == "gun") Crew.Spawn(this, u);   // T27: real soldiers on the seats, not figures baked into the hull
            return u;
        }

        public Vector2 FreeSpot(float cx, float cz, float sp, float r)
        {
            for (int i = 0; i < 40; i++)
            {
                float a = Random.value * 6.28f, d = Mathf.Sqrt(Random.value) * sp, x = Mathf.Clamp(cx + Mathf.Cos(a) * d, -M.half + 3, M.half - 3), z = Mathf.Clamp(cz + Mathf.Sin(a) * d, -M.half + 3, M.half - 3);
                if (SpotOk(x, z, r)) return new Vector2(x, z);
            }
            for (float rr = sp + 4; rr < sp + 90; rr += 4) for (int i = 0; i < 16; i++)
                {   // widen the search instead of dropping units inside a building / the sea / a 40° bank
                    float a = i / 16f * 6.28f, x = Mathf.Clamp(cx + Mathf.Cos(a) * rr, -M.half + 3, M.half - 3), z = Mathf.Clamp(cz + Mathf.Sin(a) * rr, -M.half + 3, M.half - 3);
                    if (SpotOk(x, z, r)) return new Vector2(x, z);
                }
            return new Vector2(cx, cz);
        }
        /// A place a unit can actually stand and walk away from: not in a building, not in water, not on a slope it would slide down,
        /// and on a cell the pathfinder considers passable (so a fresh unit never starts inside a fence line or a wire belt).
        bool SpotOk(float x, float z, float r)
        {
            if (World.InBuildingG(x, z, r + .5f) || World.InWater(x, z) || World.Slope(x, z) > .47f) return false;   // .47 ≈ 25°
            return Nav.Passable(x, z, r > .9f);
        }

        void PlaceForce(int team, FForce f)
        {
            if (f.type == "cav" && team == 0) return;
            float cx, cz; if (f.loc < 0 || World.points.Count == 0) { cx = M.spawn[team].x; cz = M.spawn[team].z; } else { var p = World.points[Mathf.Min(f.loc, World.points.Count - 1)]; cx = p.x; cz = p.z; }
            float dir = M.spawn[1 - M.attacker].z > M.spawn[M.attacker].z ? 1 : -1;
            for (int i = 0; i < f.n; i++)
            {
                var q = FreeSpot(cx, cz + (f.loc < 0 ? 0 : (team == M.attacker ? -3 : 3) * dir), f.sp, Defs.For(team, f.type).r);
                var u = SpawnUnit(team, f.type, q.x, q.y, FaceEnemy(team)); if (f.loc >= 0) { u.home = q; }
            }
        }

        // ---------------- helpers used by units ----------------
        public bool InSZ(Unit e) { var s = SpawnPos(e.team); return FU.Hyp(e.x - s.x, e.z - s.z) < SPR; }
        public bool IsProt(Unit e) => e.prot > t && InSZ(e);
        public CapPoint ActivePoint() { int i = ActiveIdx(); return i < World.points.Count ? World.points[i] : null; }
        int ActiveIdx() { int i = World.points.FindIndex(p => !p.locked); return i < 0 ? World.points.Count : i; }
        /// Staged spawns: both sides' spawns follow the front. Attackers deploy behind the last position they took, out of
        /// sight of the next one; defenders deploy behind the position under attack, toward the next one back. Each stage
        /// is worked out once (when a position falls) and every spawn is a protected area.
        readonly Vector3[] spCache = new Vector3[2]; readonly int[] spStage = { -99, -99 }; const float SPR = 22, SPG = 6;
        public Vector3 SpawnPos(int team)
        {
            var bs = new Vector3(M.spawn[team].x, 0, M.spawn[team].z); var pts = World.points;
            if (range != null || pts.Count == 0) return bs;
            int cap = 0; while (cap < pts.Count && pts[cap].locked) cap++;
            if (spStage[team] == cap) return spCache[team];
            bool first = spStage[team] == -99; spStage[team] = cap; var v = ComputeSpawn(team, cap); spCache[team] = v;
            if (!first && state == GState.Playing && team == ME && FU.Hyp(v.x - bs.x, v.z - bs.z) > 1)
                Msg(team == M.attacker ? "Spawn moved up behind " + pts[Mathf.Max(0, cap - 1)].n + "." : "Spawn falls back behind " + (cap < pts.Count ? pts[cap].n : "the last line") + ".");
            return v;
        }
        Vector3 ComputeSpawn(int team, int cap)
        {
            var pts = World.points; int att = M.attacker; var bs = new Vector2(M.spawn[team].x, M.spawn[team].z); var B = new Vector3(bs.x, 0, bs.y);
            Vector2 anchor, away, fight; float dist; float rr;
            if (team == att)
            {
                if (cap <= 0) return B;
                var p = pts[cap - 1]; anchor = new Vector2(p.x, p.z); rr = p.r;
                Vector2 nxt = cap < pts.Count ? new Vector2(pts[cap].x, pts[cap].z) : anchor + (anchor - bs).normalized * 80; fight = nxt;
                away = ((anchor - nxt).normalized * .65f + (bs - anchor).normalized * .35f).normalized;
                dist = Mathf.Clamp((nxt - anchor).magnitude * .4f, 35, 85) + Mathf.Min(rr, 40) * .5f;
            }
            else
            {
                if (cap >= pts.Count) return B;
                var p = pts[cap]; anchor = new Vector2(p.x, p.z); fight = anchor; rr = p.r;
                Vector2 back = cap + 1 < pts.Count ? new Vector2(pts[cap + 1].x, pts[cap + 1].z) : bs;
                away = (back - anchor).normalized; if (away.sqrMagnitude < .5f) away = (bs - anchor).normalized;
                dist = Mathf.Min(Mathf.Clamp((back - anchor).magnitude * .5f, 45, 110) + Mathf.Min(rr, 40) * .5f, (bs - anchor).magnitude);
            }
            Vector2? best = null; float bestS = -1e9f; float Hh = M.half - 14;
            var fe = new Vector3(fight.x, World.H(fight.x, fight.y) + 1.7f, fight.y);
            for (int ai = -4; ai <= 4; ai++) for (int di = -1; di <= 2; di++)
                {
                    float ang = ai * 10 * Mathf.Deg2Rad, dd = dist + di * 12, c = Mathf.Cos(ang), s = Mathf.Sin(ang);
                    var dir = new Vector2(away.x * c - away.y * s, away.x * s + away.y * c); var q = anchor + dir * dd;
                    if (Mathf.Abs(q.x) > Hh || Mathf.Abs(q.y) > Hh || World.InWater(q.x, q.y) || World.InBuildingG(q.x, q.y, 4)) continue;
                    bool bad = false; foreach (var z in pts) if (z.Contains(q.x, q.y) || FU.Hyp(z.x - q.x, z.z - q.y) < z.r + 10) bad = true; if (bad) continue;
                    float sc = -Mathf.Abs(dd - dist) * .05f - ai * ai * .03f - World.Slope(q.x, q.y) * 5;
                    foreach (var u in units) if (u.alive && u.team != team && FU.Hyp(u.x - q.x, u.z - q.y) < 45) sc -= 1.5f;
                    if (!World.LOS(fe, new Vector3(q.x, World.H(q.x, q.y) + 1.5f, q.y))) sc += 2.5f;   // out of sight of the fight
                    if (sc > bestS) { bestS = sc; best = q; }
                }
            if (best != null) return new Vector3(best.Value.x, 0, best.Value.y);
            float l = (bs - anchor).magnitude;   // fallback: walk back toward the base to the first open spot
            for (float o = dist; o < l; o += 4) { var q = anchor + (bs - anchor) / l * o; if (!World.InBuildingG(q.x, q.y, 3) && !World.InWater(q.x, q.y)) return new Vector3(q.x, 0, q.y); }
            return B;
        }
        /// Spawn protection: enemies in a team's spawn are warned, AI is turned back, and anyone who stays dies.
        readonly Dictionary<Unit, float> sgT = new Dictionary<Unit, float>(); float sgMsgT;
        void SpawnGuard(float dt)
        {
            if (range != null) return;
            for (int tm = 0; tm < 2; tm++)
            {
                var s = SpawnPos(tm); var b = new Vector3(M.spawn[tm].x, 0, M.spawn[tm].z);
                foreach (var u in units)
                {
                    if (!u.alive) { sgT.Remove(u); continue; } if (u.team == tm) continue;
                    float ds = FU.Hyp(u.x - s.x, u.z - s.z), db = FU.Hyp(u.x - b.x, u.z - b.z);
                    if (ds >= SPR && db >= SPR) { if (sgT.Count > 0) sgT.Remove(u); continue; }
                    if (u != player && u.order == null || (u != player && u.order != null && FU.Hyp(u.order.x - s.x, u.order.z - s.z) < SPR))
                    {   // AI: step back out of the protected area
                        var c = ds < SPR ? s : b; float dx = u.x - c.x, dz = u.z - c.z, l = Mathf.Max(.1f, FU.Hyp(dx, dz));
                        u.order = new Order { x = c.x + dx / l * (SPR + 12), z = c.z + dz / l * (SPR + 12), cx = u.x, cz = u.z, force = true, fast = true }; u.queue.Clear(); u.secure = -1;
                    }
                    float v = (sgT.TryGetValue(u, out var o) ? o : 0) + dt; sgT[u] = v;
                    if (u == player && Time.unscaledTime > sgMsgT) { sgMsgT = Time.unscaledTime + .9f; ShowBanner("ENEMY SPAWN AREA", "Leave now — " + Mathf.CeilToInt(SPG - v) + " s", UI.Red); }
                    if (v > SPG) { sgT.Remove(u); if (u == player) Msg("Killed in the enemy spawn area."); Combat.Kill(this, u, null, false); }
                }
            }
        }
        /// Topography check for the loaded map: relief, objective prominence and footing, building pads, road grades,
        /// water and the map edge. Appends to Logs/fallweiss_topo.txt and returns a one-line summary.
        bool showHit;
        static void ShowHitbox(UnitHit h, bool on)
        {
            var old = h.transform.Find("HitViz"); if (old) Destroy(old.gameObject); if (!on) return;
            var bc = h.GetComponent<BoxCollider>(); var sc = h.GetComponent<SphereCollider>();
            var g = GameObject.CreatePrimitive(sc ? PrimitiveType.Sphere : PrimitiveType.Cube); Destroy(g.GetComponent<Collider>()); g.name = "HitViz"; g.transform.SetParent(h.transform, false);
            if (bc) { g.transform.localPosition = bc.center; g.transform.localScale = bc.size; } else if (sc) { g.transform.localPosition = sc.center; g.transform.localScale = Vector3.one * sc.radius * 2; }
            var c = h.zone == HitZone.Head ? new Color(1, .2f, .2f, .35f) : h.zone == HitZone.Chest ? new Color(1, .6f, .1f, .35f) : h.zone == HitZone.Stomach ? new Color(1, 1, .2f, .35f) : new Color(.3f, .7f, 1, .35f);
            g.GetComponent<Renderer>().sharedMaterial = Mats.Transparent(c);
        }
        /// Gun lab: for each weapon and range, aim at the chest of a standing soldier and fire 60 aimed single shots and,
        /// for automatics, 12 five-round bursts with the real kick, through the real hitboxes and the real spread model.
        /// Reports hit rate, zones, mean damage per hit and how many shots it takes to drop a man on average.
        Unit labDummy;
        string GunLab(string which, string stance)
        {
            if (player == null) return "no player"; int st = stance == "prone" ? 2 : stance == "crouch" ? 1 : 0; bool dep = stance == "deployed";
            var list = which == "all" ? new[] { "kar98k", "wz29", "kar98kz", "mp38", "bar28", "mg34", "ckm30", "p38", "vis", "p08" } : which.Split(',');
            float[] ranges = { 10, 25, 50, 100, 200, 300 };
            if (labDummy == null || !labDummy.alive) { labDummy = SpawnUnit(EN, "rifle", player.x, player.z + 50, Mathf.PI); labDummy.hp = labDummy.max = 1e7f; labDummy.stance = 0; return "target placed - wait a moment for its model, then run gunlab again"; }
            var dummy = labDummy; dummy.hp = dummy.max = 1e7f; dummy.stance = 0; dummy.order = null; dummy.target = null;
            var sb = new System.Text.StringBuilder($"GUN LAB ({stance}) — hit% single · zones H/C/S/A/L · dmg/hit · shots-to-kill | burst hits/5\n");
            var rnd = new System.Random(7); float R() => (float)(rnd.NextDouble() * 2 - 1);
            var eye = new Vector3(player.x, World.H(player.x, player.z) + (st == 2 || dep ? .33f : st == 1 ? .95f : 1.55f), player.z);
            foreach (var id in list)
            {
                if (!Defs.WEAPONS.TryGetValue(id, out var wd) || wd.melee) continue; sb.Append($"{wd.name,-24}");
                foreach (var rg in ranges)
                {
                    float maxR = wd.range > 0 ? wd.range : 600; if (rg > maxR * 1.6f) { sb.Append($" {rg,3:0}m: --          "); continue; }
                    // stand the dummy at range, facing the shooter, and let the physics scene catch up with its bones
                    dummy.x = player.x; dummy.z = player.z + rg; dummy.y = World.H(dummy.x, dummy.z); dummy.heading = Mathf.PI; dummy.SyncTransform(0); Physics.SyncTransforms();
                    var aimAt = dummy.Chest; var fwd = (aimAt - eye).normalized; var rot = Quaternion.LookRotation(fwd);
                    int hits = 0; var zc = new int[6]; float dsum = 0; int N = 60;
                    float sp = ShotSpread(wd, 0, false, 1, dep ? 2 : st, 0, false, wd.mg && (dep || st == 2));
                    for (int n = 0; n < N; n++)
                    {
                        var dir = (rot * new Vector3(R() * sp, R() * sp, 1)).normalized; var (dd, hu, hd) = CastRay(eye, dir, maxR * 2, player.team, player);
                        if (hu == dummy) { hits++; zc[(int)castZone]++; dsum += wd.dmg * Mathf.Lerp(1, wd.fallMul, Mathf.Clamp01((dd - wd.fallNear) / (wd.fallFar - wd.fallNear))) * Combat.ZoneMul(castZone); }
                    }
                    float dph = hits > 0 ? dsum / hits : 0, stk = hits > 0 ? Mathf.Ceil(100 / Mathf.Max(1, dph)) / (hits / (float)N) : 999;
                    string burst = "";
                    if (wd.auto)
                    {   // five-round bursts, the kick climbing shot by shot with no correction
                        int bh = 0; for (int b = 0; b < 12; b++) { float climb = 0, side = 0; for (int k = 0; k < 5; k++) { var dir = (rot * Quaternion.Euler(-climb * Mathf.Rad2Deg, side * Mathf.Rad2Deg, 0) * new Vector3(R() * sp, R() * sp, 1)).normalized; var (_, hu, _) = CastRay(eye, dir, maxR * 2, player.team, player); if (hu == dummy) bh++; float kick = wd.mg ? (wd.mg && (dep || st == 2) ? .3f : 1.6f) : 1; climb += .005f * (1.4f - .75f) * kick; side += (float)rnd.NextDouble() * .003f * kick * (rnd.NextDouble() < .5 ? -1 : 1); } }
                        burst = $" b{bh / 12f:0.0}";
                    }
                    sb.Append($" {rg,3:0}m:{hits * 100 / N,3}% {zc[1]}/{zc[2]}/{zc[3]}/{zc[4]}/{zc[5]} d{dph:0} stk{(stk > 99 ? "--" : stk.ToString("0.0"))}{burst} |");
                }
                sb.Append("\n");
            }
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(Application.isEditor ? "Logs" : Application.dataPath + "/../Logs", "fallweiss_gunlab.txt"), System.DateTime.Now + "\n" + sb + "\n"); } catch { }
            return "written to Logs/fallweiss_gunlab.txt";
        }
        string Topo()
        {
            var sb = new System.Text.StringBuilder(); float Hh = M.half; var issues = new List<string>();
            float mn = 1e9f, mx = -1e9f, steep = 0, n = 0;
            for (float x = -Hh; x <= Hh; x += 8) for (float z = -Hh; z <= Hh; z += 8)
                {
                    if (World.InWater(x, z)) continue; float h = World.H(x, z); mn = Mathf.Min(mn, h); mx = Mathf.Max(mx, h); n++; if (World.Slope(x, z) > .7f) steep++;
                }
            sb.AppendLine($"== {M.name}  half={Hh}  relief={M.relief}  height {mn:0.0}..{mx:0.0} m (range {mx - mn:0.0})  ground steeper than 35deg: {steep / Mathf.Max(1, n) * 100:0.0}%");
            foreach (var p in World.points)
            {
                float hc = World.H(p.x, p.z), ring = 0; int rn = 0;
                for (int i = 0; i < 24; i++) { float a = i * .2618f, d = p.r + 50; float x = p.x + Mathf.Sin(a) * d, z = p.z + Mathf.Cos(a) * d; if (Mathf.Abs(x) < Hh && Mathf.Abs(z) < Hh && !World.InWater(x, z)) { ring += World.H(x, z); rn++; } }
                float prom = rn > 0 ? hc - ring / rn : 0, sl = World.Slope(p.x, p.z);
                sb.AppendLine($"  P {p.n}: {(p.rect ? $"rect {p.hw * 2:0}x{p.hd * 2:0}" : $"circle r={p.r:0}")} {(p.inside ? "interior" : p.key ?? "")} centre slope {sl:0.00}  prominence {prom:+0.0;-0.0} m  water={World.InWater(p.x, p.z)}");
                if (sl > .45f && !p.inside) issues.Add(p.n + " centre slope " + sl.ToString("0.00"));
                if (World.InWater(p.x, p.z) && World.BridgeAt(p.x, p.z) == 0) issues.Add(p.n + " centre in water");
            }
            int pads = 0; foreach (var b in Interiors.bds)
            {
                float y = World.H((b.x0 + b.x1) / 2, (b.z0 + b.z1) / 2), worst = 0;
                foreach (var c in new[] { new Vector2(b.x0 - 6, b.z0 - 6), new Vector2(b.x1 + 6, b.z0 - 6), new Vector2(b.x0 - 6, b.z1 + 6), new Vector2(b.x1 + 6, b.z1 + 6), new Vector2(b.x0, b.z0), new Vector2(b.x1, b.z1) })
                    worst = Mathf.Max(worst, Mathf.Abs(World.H(c.x, c.y) - y));
                if (worst > 3.5f) { pads++; if (pads <= 6) issues.Add($"{b.k} at ({(b.x0 + b.x1) / 2:0},{(b.z0 + b.z1) / 2:0}) pad step {worst:0.0} m"); }
            }
            float road = 0; Vector2 rw = Vector2.zero;
            foreach (var r in M.roads) { float L = FU.Hyp(r.x2 - r.x1, r.z2 - r.z1); for (float t = 0; t < L - 4; t += 4) { float x = Mathf.Lerp(r.x1, r.x2, t / L), z = Mathf.Lerp(r.z1, r.z2, t / L); if (Mathf.Abs(x) > Hh - 30 || Mathf.Abs(z) > Hh - 30) continue; float x2 = Mathf.Lerp(r.x1, r.x2, (t + 4) / L), z2 = Mathf.Lerp(r.z1, r.z2, (t + 4) / L); float g = Mathf.Abs(World.H(x2, z2) - World.H(x, z)) / 4; if (g > road) { road = g; rw = new Vector2(x, z); } } }
            if (road > .35f) issues.Add($"road grade {road * 100:0}% at ({rw.x:0},{rw.y:0})");
            sb.AppendLine($"  buildings {Interiors.bds.Count}, pads with a >3.5 m step: {pads}; steepest road grade {road * 100:0}%");
            sb.AppendLine("  ISSUES: " + (issues.Count == 0 ? "none" : string.Join("; ", issues)));
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(Application.isEditor ? "Logs" : Application.dataPath + "/../Logs", "fallweiss_topo.txt"), sb.ToString()); } catch { }
            return $"{M.name}: range {mx - mn:0.0} m, steep {steep / Mathf.Max(1, n) * 100:0.0}%, pads>3.5m {pads}, road {road * 100:0}%, issues {issues.Count}";
        }
        public Vector2 FollowSlot(Unit u)
        {
            int row = u.slot / 4 + 1; float lat = ((u.slot % 4) - 1.5f) * 3.2f, back = 4 + row * 3.5f, h = player.heading;
            return new Vector2(player.x - Mathf.Sin(h) * back + Mathf.Cos(h) * lat, player.z - Mathf.Cos(h) * back - Mathf.Sin(h) * lat);
        }
        /// AI think-interval multiplier by distance from the camera (1 near, up to 2.5 far away); the command map sees everything, so it uses a flat 1.5.
        public float ThinkScale(float x, float z) { if (rts || cam == null) return 1.5f; var c = cam.transform.position; float d = FU.Hyp(x - c.x, z - c.z); return d > 320 ? 2.5f : d > 170 ? 1.6f : 1; }
        public void Msg(string s)
        {
            if (state == GState.Menu) return; msgs.Add((s, Time.unscaledTime)); if (msgs.Count > 7) msgs.RemoveAt(0);
            // signals traffic (support calls, objectives, reinforcements) comes in over the field radio as Morse; squad orders are spoken, so no cue
            if (Time.unscaledTime - radioT > 1.5f && cam && (s.StartsWith("⚠") || RADIO.Any(w => s.IndexOf(w, System.StringComparison.OrdinalIgnoreCase) >= 0)))
            { radioT = Time.unscaledTime; audio.Shot("radio", cam.transform.position); }
        }
        float radioT = -9; static readonly string[] RADIO = { "inbound", "barrage", "captured", "lost", "reinforce" };
        public void HitMarker(bool kill, bool head) { hitT = .35f; hitKill = kill; hitHead = head; }
        public void DamageFlash(Unit by = null, Vector3? from = null)
        {
            dmgT = .4f; Vector3? src = from ?? (by != null ? new Vector3(by.x, by.y, by.z) : (Vector3?)null); if (!src.HasValue) return;
            foreach (var d in dmgSrc) if (FU.Hyp(d.p.x - src.Value.x, d.p.z - src.Value.z) < 4) { d.p = src.Value; d.t = 2.4f; return; }
            dmgSrc.Add(new DmgSrc { p = src.Value, t = 2.4f }); if (dmgSrc.Count > 6) dmgSrc.RemoveAt(0);
        }
        public Vector3 lastBoom; public float lastBoomR;
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
            float cw = 520, cx = W / 2, y = 70, half = 75 * Mathf.Deg2Rad, hd = yaw; var r = new Rect(cx - cw / 2, y, cw, 22);   // N = map north (+z, the top of the minimap); Mission.north no longer rotates the strip
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
            { float f = 1 / Mathf.Max(1e-4f, Time.unscaledDeltaTime); fpsAvg = Mathf.Lerp(fpsAvg, f, .04f); if (Time.unscaledTime > fpsMinT + 5) { fpsMinT = Time.unscaledTime; fpsMin = f; } else fpsMin = Mathf.Min(fpsMin, f); }
            if (state == GState.Menu)
            {
                if (!menuBuilt) return; t += dt; for (int i = 0; i < units.Count; i++) units[i].Update(this, dt); foreach (var u in units) u.SyncTransform(dt);
                Director(dt); SecureTick(dt); UpdateCapture(dt, true); UpdateRounds(dt); UpdateFires(dt); World.Tick(dt); Nav.Tick(); Burning(dt); UpdateConvoy(dt); UpdateShipAndSky(dt); UpdatePlanes(dt); UpdateShells(); fx.Update(dt, cam); vis.Update(dt, t, false); MenuCamera(dt); CleanupDead(dt);
                foreach (var b in World.blades) if (b) b.Rotate(0, 0, -dt * 23, Space.Self);
                return;
            }
            if (state == GState.End) { fx.Update(dt, cam); vis.Update(dt, t, !rts); return; }
            if (state == GState.Deploy)
            {   // the clock does not run and nobody moves: only the camera, the tray and the markers
                if (kb.escapeKey.wasPressedThisFrame) { EnterMenu(); return; }   // Esc during deployment: back to the menu (nothing to pause yet)
                UpdateDeploy(dt, kb, ms); if (state != GState.Deploy) return;
                foreach (var u in units) u.SyncTransform(dt); UpdatePings(dt); World.Tick(dt); Nav.Tick(); fx.Update(dt, cam); vis.Update(dt, 0, false); UpdateFires(dt); UpdateAmbience(dt);
                return;
            }
            if (kb.escapeKey.wasPressedThisFrame) { if (state == GState.Paused) Resume(); else if (state == GState.Playing || state == GState.Dead) { pausedFrom = state; state = GState.Paused; Time.timeScale = 0; AudioListener.pause = true; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; } return; }
            if (state == GState.Paused) return;

            t += dt; supportCd -= dt;
            float own = World.points.Count(p => p.locked && p.owner == ME); cp += dt * (1.5f + own * .5f);
            if (t - tixT >= 20) { if (tixT >= 0) tixRate = (tix[ME] - tixRef) * (60f / Mathf.Max(1, t - tixT)); tixT = t; tixRef = tix[ME]; }   // one income line (the port had two added together, doubling the economy)
            for (int i = 0; i < units.Count; i++) units[i].Update(this, dt);
            if (player != null && player.alive && !rts) UpdatePlayer(dt, kb, ms);
            foreach (var u in units) u.SyncTransform(dt);
            if (player != null && player.alive) { resT -= dt; if (resT <= 0) { resT = 5; Resupply(player); } }
            streakT -= dt; if (streakT <= 0) streak = 0;
            audio.Tick(); UpdateRounds(dt); UpdateGrenades(dt); UpdateSmoke(dt); UpdatePickups(dt); UpdatePlanes(dt); UpdateShells(); UpdateMarkers(dt);
            if (range == null) { Director(dt); SecureTick(dt); UpdateCapture(dt, false); SpawnGuard(dt); Raids(dt); AiGrenades(dt); } else range.Update(dt, kb); Spotting(dt); UpdateFires(dt); UpdateAmbience(dt); UpdateShipAndSky(dt); World.Tick(dt); Nav.Tick(); Burning(dt); UpdateConvoy(dt); UpdateBeams(dt); UpdatePaths(); UpdatePings(dt); szT -= dt; if (szT <= 0) { szT = .5f; UpdateSpawnZones(); } shake = Mathf.Max(0, shake - dt * 3);
            foreach (var b in World.blades) if (b) b.Rotate(0, 0, -dt * 23, Space.Self);
            fx.Update(dt, cam); vis.Update(dt, t, !rts); vis.Suppression(player != null && player.alive && !rts ? player.supp : 0); UpdateInteriorLight();
            hitT -= dt; dmgT -= dt; ammoShowT -= dt; zeroShowT -= dt; for (int i = dmgSrc.Count - 1; i >= 0; i--) { dmgSrc[i].t -= dt; if (dmgSrc[i].t <= 0) dmgSrc.RemoveAt(i); }
            CleanupDead(dt);
            if (kb.bKey.wasPressedThisFrame && !rts && state == GState.Playing) FpsSupport();

            if (state == GState.Dead) { respT -= dt; DeadCamera(dt); if (kb.xKey.wasPressedThisFrame) respSquad = !respSquad; for (int i = 0; i < Defs.CLASSES.Length; i++) if (kb[Key.Digit1 + i].wasPressedThisFrame && !rts) classIdx = i; if (respT <= 0 && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) TryDeploy(classIdx); else if (!rts && kb.fKey.wasPressedThisFrame) PossessSomething(); if (kb.tabKey.wasPressedThisFrame) { rts = !rts; vis.Tod(t); vis.Post(!rts); if (rts) { var lp = lastPlayer != null ? new Vector3(lastPlayer.x, 0, lastPlayer.z) : SpawnPos(ME); rtsPos = lp; vis.RtsFog(true); } } if (rts) UpdateRts(dt, kb, ms); }
            else if (rts) { UpdateRts(dt, kb, ms); if (kb.tabKey.wasPressedThisFrame) LeaveRts(); }
            else if (kb.tabKey.wasPressedThisFrame) EnterRts();
            CheckEnd();
        }

        GState pausedFrom = GState.Playing;   // pausing from the command map used to come back as Dead (player == null while in the map)
        void Resume() { state = pausedFrom == GState.Dead || pausedFrom == GState.Playing ? pausedFrom : (player != null && player.alive ? GState.Playing : GState.Dead); Time.timeScale = 1; AudioListener.pause = false; if (!rts && state == GState.Playing) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; } }

        void CleanupDead(float dt)
        {
            for (int i = units.Count - 1; i >= 0; i--) { var u = units[i]; if (!u.alive && u.deadT > (u.Foot ? 90 : 300) && u != player && u != lastPlayer) { if (u.go) Destroy(u.go); units.RemoveAt(i); } }
            int bodies = 0; for (int i = units.Count - 1; i >= 0; i--) { var u = units[i]; if (u.alive || !u.Foot || u == player || u == lastPlayer) continue; if (++bodies > 60 && u.deadT > 12) { if (u.go) Destroy(u.go); units.RemoveAt(i); } }   // RO2 leaves the dead where they fell for a long while; cap the count for performance (oldest go first since ids rise)
        }

        // fog of war (port of the visT block in step())
        void Spotting(float dt)
        {
            visT -= dt; if (visT > 0) return; visT = .3f;
            var mine = units.Where(u => u.alive && u.team == ME && u.rout <= 0).ToList();
            foreach (var u in units)
            {
                if (u.team == ME || !u.alive) { if (u != player) u.SetVisible(true); continue; }
                if (t - u.seenT < 1f) { u.visible = true; u.Show(true); continue; }   // seen within the last second: still visible for another 1.2 s whatever we find — skip the raycasts this tick
                foreach (var m in mine)
                {
                    float d = FU.Hyp(m.x - u.x, m.z - u.z); if (d >= (m == player ? 700 : M.vis)) continue;   // you see as far as the world is drawn; the men report only what is near them
                    var eye = m == player && m.alive && !rts ? cam.transform.position : new Vector3(m.x, m.y + m.def.my * m.StM, m.z);
                    if (d < 28 || World.LOS(eye, new Vector3(u.x, u.y + 1.2f * u.StM, u.z))) { u.seenT = t; break; }
                }
                // fog of war (SD2-style) is for the command map and the HUD icons. In first person nothing is hidden: RO2 draws every
                // man in view, and a sniper's 400 m target is a target. Enemies that aren't spotted simply get no icon or map blip.
                u.visible = t - u.seenT < 2.2f;
                u.Show(!rts || u.visible);
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
                if (act >= pts.Count) continue;
                if (team == att)
                {   // SD2-style: every idle attacker joins the group securing the active objective, while one or two
                    // MGs / guns stay behind on each captured point so it isn't left empty
                    var kept = new int[pts.Count];
                    foreach (var u in mine)
                    {
                        if (u.secure == act) continue;
                        if (u.secure >= 0 && u.secure < act && (u.type == "mg" || u.def.kind == "gun") && kept[u.secure] < 2) { kept[u.secure]++; continue; }
                        if (u.order != null && u.secure < 0 && !fresh.Contains(u) && u.idle < 1.2f) continue;
                        u.secure = act; u.order = null; u.queue.Clear();
                    }
                }
                else
                {   // defence in depth: most defenders hold the contested point, every third man the next one back;
                    // when the contested point is outnumbered, half of the depth group is pulled forward to reinforce it
                    int second = act + 1 < pts.Count ? act + 1 : act; var cp0 = pts[act];
                    foreach (var u in mine) { if (u.def.kind == "gun" || u.secure >= act) continue; u.secure = u.id % 3 == 0 ? second : act; u.order = null; }
                    if (second != act && Strength(att, cp0.x, cp0.z, cp0.r + 60) > Strength(team, cp0.x, cp0.z, cp0.r + 25) * 1.3f)
                    { int k = 0; foreach (var u in mine) if (u.secure == second && u.def.kind != "gun" && k++ % 2 == 0) { u.secure = act; u.order = null; u.hide = null; } }
                }
            }
            if (state != GState.Menu && t >= aiSup) { AiSupport(); aiSup = t + FU.Rand(90, 130); }
        }
        // =====================================================================================================
        // "Secure objective" (Steel Division 2-style control-point command): a group given an objective works out
        // the rest itself — it masses at a rally point out of the line of fire, attacks together once assembled
        // (MGs, snipers and guns take overwatch, tanks push to the edge, riflemen go in, smoke if it is hot), falls
        // back to regroup if the assault is shredded, and once the point is theirs digs into cover facing the enemy.
        // =====================================================================================================
        // ---------------- frontlines (roadmap T7, slice 1): the ground between objectives is taken line by line ----------------
        // A line is a cluster of cover (hedges, walls, house fronts, trenches) across the attack axis with the next objective behind it.
        // An attacking group assembles at the first line ahead of it, dwells in that cover, then moves to the next; the last line
        // hands over to the normal assault (bound + go in). A line reached by 60 % of the group is "taken" and becomes the front:
        // later groups assemble there, not at the base. Defenders ignore lines for now (counter-attacks are the next slice).
        public class FrontLine { public Vector2 c, axis; public float along, w; public List<Structures.Cover> cover = new List<Structures.Cover>(); public int obj, heldBy = -1; public float heldT; public string name; }
        public readonly List<FrontLine> lines = new List<FrontLine>();
        void BuildLines()
        {
            lines.Clear(); var pts = World.points; if (M == null || M.range || pts.Count == 0 || M.spawn == null) return;
            var att = new Vector2(M.spawn[M.attacker].x, M.spawn[M.attacker].z);
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i]; var from = i == 0 ? att : new Vector2(pts[i - 1].x, pts[i - 1].z); var to = new Vector2(p.x, p.z);
                var axis = to - from; float L = axis.magnitude; if (L < 140) continue; axis /= L; var side = new Vector2(-axis.y, axis.x);
                var mid = (from + to) / 2; var bins = new Dictionary<int, List<Structures.Cover>>();
                foreach (var c in Structures.Near(mid.x, mid.y, L / 2 + 40))
                {
                    var d = new Vector2(c.x, c.z) - from; float a = Vector2.Dot(d, axis), sd = Mathf.Abs(Vector2.Dot(d, side));
                    if (a < 45 || a > L - p.r - 20 || sd > 110) continue;                                    // in the corridor, clear of the start, up to 20 m short of the zone (a forward trench 40 m out is a line)
                    if (!c.trench) { if (!c.n.HasValue) continue; var e = (to - new Vector2(c.x, c.z)).normalized; if (Vector2.Dot(c.n.Value, e) > -.25f) continue; }   // the obstacle must be between the man and the objective
                    int bin = Mathf.FloorToInt(a / 60); if (!bins.TryGetValue(bin, out var l)) bins[bin] = l = new List<Structures.Cover>(); l.Add(c);
                }
                foreach (var kv in bins.OrderBy(k => k.Key))
                {
                    var l = kv.Value; if (l.Count < 4) continue;
                    float cx = l.Average(c => c.x), cz = l.Average(c => c.z); var cc = new Vector2(cx, cz);
                    float w = l.Max(c => Vector2.Dot(new Vector2(c.x, c.z) - from, side)) - l.Min(c => Vector2.Dot(new Vector2(c.x, c.z) - from, side));
                    int tr = l.Count(c => c.trench), hi = l.Count(c => !c.trench && !c.low);
                    string kind = tr > l.Count / 2 ? "trench line" : hi > l.Count / 2 ? "the houses" : "the hedgerow";
                    lines.Add(new FrontLine { c = cc, axis = axis, along = Vector2.Dot(cc - from, axis), w = Mathf.Max(24, w), cover = l, obj = i, name = kind + " before " + p.n });
                }
            }
        }
        /// The next line an attacking group should take on the way to objective pi: the nearest one ahead of the group's median position that it has not taken yet.
        FrontLine NextLine(int team, int pi, List<Unit> list)
        {
            var p = World.points[pi]; var P = new Vector2(p.x, p.z);
            float med = 0; int n = 0; foreach (var u in list) if (u.def.kind != "gun") { med += FU.Hyp(u.x - p.x, u.z - p.z); n++; } if (n == 0) return null; med /= n;   // mean distance to the objective
            FrontLine best = null; float bestD = -1;
            foreach (var ln in lines) { if (ln.obj != pi || ln.heldBy == team) continue; float dObj = (P - ln.c).magnitude; if (dObj > med - 15) continue; if (dObj > bestD) { bestD = dObj; best = ln; } }   // ahead of the group, nearest first
            return best;
        }
        /// Put a group into the cover of a line, facing the objective (dwell before the next bound).
        void TakeLine(FrontLine ln, int team, List<Unit> list, CapPoint p, float dwell)
        {
            ln.heldBy = team; ln.heldT = t; var free = new List<Structures.Cover>(ln.cover);
            foreach (var u in list)
            {
                if (u.def.kind == "gun" || u.def.kind == "tank" || !u.alive) continue; Structures.Cover pick = null; float bd = 1e9f;
                foreach (var c in free) { float d = FU.Hyp(c.x - u.x, c.z - u.z); if (d < bd) { bd = d; pick = c; } }
                if (pick == null) break; free.Remove(pick); pick.taken = t; pick.by = u; u.order = null;
                u.hide = new Hide { x = pick.x, z = pick.z, t = dwell + FU.Rand(0, 3), fire = true, low = pick.low, trench = pick.trench };
            }
        }

        class SecureState { public int phase; public float since, startStr; public bool told, bounded; public FrontLine line; public float lineT; }   // phase 0 assemble, 1 assault (bounded = past the last bound), 2 hold, 3 regroup
        int sectCounter;   // section ids (cohesion): men who deployed or arrived together stay together in the assault
        /// A section's own corner of the objective: the same seed for every man in it, so a section ends up in one part of the zone
        /// instead of eight men spread across 80 m of it. Sections without an id fall back to the old per-man spread.
        Vector2 SectionSpot(Unit u, CapPoint p, float k)
        {
            if (u.sect < 0) return p.rect ? p.RandIn(.75f) : p.RandIn(Mathf.Min(.7f, 30 / Mathf.Max(1, p.r)));
            var rng = new System.Random(u.sect * 7919 + p.n.GetHashCode()); float a = (float)rng.NextDouble() * 6.283f, d = (float)rng.NextDouble();
            float rr = p.rect ? Mathf.Min(p.hw, p.hd) * .7f : p.r * k; var c = new Vector2(p.x + Mathf.Cos(a) * rr * d, p.z + Mathf.Sin(a) * rr * d);
            return new Vector2(c.x + FU.Rand(-5, 5), c.y + FU.Rand(-5, 5));
        }
        readonly Dictionary<int, SecureState> secureState = new Dictionary<int, SecureState>(); float secT; bool armSecure;
        static float Weight(Unit u) => (u.def.kind == "tank" ? 4 : u.type == "mg" ? 1.5f : u.def.kind == "gun" ? 1.2f : u.type == "sniper" ? .8f : 1) * Mathf.Clamp01(u.hp / u.max) * (u.rout > 0 ? .3f : 1);
        /// Combat strength of a team inside a circle (SD2's "area of influence": more units, stronger zone).
        float Strength(int team, float x, float z, float r) { float s = 0; foreach (var u in units) if (u.alive && u.team == team && FU.Hyp(u.x - x, u.z - z) < r) s += Weight(u); return s; }
        void SOrder(Unit u, float x, float z, CapPoint p, bool force, bool fast) { u.order = new Order { x = x, z = z, cx = p.x, cz = p.z, force = force, fast = fast }; u.queue.Clear(); u.idle = 0; u.leash = Mathf.Max(u.leash, 40); }
        bool Support(Unit u) => u.type == "mg" || u.type == "sniper" || u.def.kind == "gun";
        void SecureTick(float dt)
        {
            secT -= dt; if (secT > 0) return; secT = 1; var pts = World.points;
            foreach (var g in units.Where(u => u.alive && u.secure >= 0 && u != player && u.rout <= 0).GroupBy(u => u.team * 100 + u.secure).ToList())
            {
                int team = g.Key / 100, pi = g.Key % 100; var list = g.ToList();
                if (pi >= pts.Count || (pts[pi].locked && pts[pi].owner != team)) { foreach (var u in list) u.secure = -1; secureState.Remove(g.Key); continue; }   // objective gone: back to the director
                var p = pts[pi]; bool attacking = team == M.attacker && !p.locked;
                if (!secureState.TryGetValue(g.Key, out var st)) { st = new SecureState { phase = attacking ? 0 : 2, since = t, startStr = list.Sum(Weight) }; secureState[g.Key] = st; }
                bool mineGroup = team == ME && list.Any(u => u.manual || u.squad); string pn = p.n;
                var own = SpawnPos(team); float dx = own.x - p.x, dz = own.z - p.z, l = Mathf.Max(.01f, FU.Hyp(dx, dz)); dx /= l; dz /= l;
                var rally = new Vector2(p.x + dx * (p.r + 45), p.z + dz * (p.r + 45));
                // frontlines: while assembling, the rally is the next untaken line on the way in (sweep & hold), the final rally only after the last line
                if (attacking && st.phase == 0)
                {
                    if (st.line != null && st.line.heldBy == team && (st.lineT < st.since || t - st.lineT > 7)) st.line = null;   // dwell over (or another group took it first): on to the next line
                    if (st.line == null) { st.line = NextLine(team, pi, list); if (st.line != null) { st.since = t; st.told = false; } }   // (this used to re-pick the moment a line was taken, so the 7 s hold in cover never happened)
                    if (st.line != null) rally = st.line.c - st.line.axis * 6;                  // just behind the cover
                }
                var ne = units.Where(e => e.alive && e.team != team && FU.Hyp(e.x - p.x, e.z - p.z) < p.r + 90).OrderBy(e => FU.Hyp(e.x - p.x, e.z - p.z)).FirstOrDefault();
                var threat = ne != null ? new Vector2(ne.x, ne.z) : new Vector2(p.x - dx * 60, p.z - dz * 60);
                if (!attacking && st.phase != 2) { st.phase = 2; st.since = t; if (mineGroup) Msg(pn + " secured — digging in."); }
                float fs = Strength(team, p.x, p.z, p.r + 30), es = Strength(1 - team, p.x, p.z, p.r + 35), now = list.Sum(Weight);
                if (st.phase == 0)
                {   // assemble out of the line of fire
                    int near = 0, need = 0; bool late = t - st.since > 15;   // slower RO2-pace infantry get a little longer to reach the rally before being counted as stragglers
                    foreach (var u in list) { if (u.def.kind == "gun") continue; float d = FU.Hyp(u.x - rally.x, u.z - rally.y); if (late && d > 110) continue; need++; if (d < 26) near++; else if (u.order == null && (u.hide == null || u.supp < .4f)) { u.hide = null; SOrder(u, rally.x + FU.Rand(-8, 8), rally.y + FU.Rand(-8, 8), p, true, true); } }   // stragglers far behind join the assault later instead of holding it up
                    if (!st.told) { st.told = true; if (mineGroup) Msg(st.line != null ? "Moving up to " + st.line.name + "." : "Forming up to take " + pn + "."); foreach (var u in list) Bark(u, "moveup", .3f); }
                    if (st.line != null && st.line.heldBy != team)
                    {   // a line is taken when most of the group is on it (or it has waited long enough): dig into its cover and dwell, then the next line
                        if (near >= Mathf.Max(1, need * .6f) || t - st.since > 24) { TakeLine(st.line, team, list, p, 7); foreach (var u in list) Bark(u, "hold", .3f); st.lineT = t; if (team == ME) Msg((mineGroup ? "Taken: " : "Our men hold ") + st.line.name + "."); else if (st.line.heldBy == EN && FU.Hyp(st.line.c.x - (player != null ? player.x : 0), st.line.c.y - (player != null ? player.z : 0)) < 160) Msg("Enemy on " + st.line.name + "!"); }
                    }
                    else if (near >= Mathf.Max(1, need * .6f) || t - st.since > 22)
                    {
                        st.phase = 1; st.since = t; st.startStr = now; st.told = false;
                        foreach (var u in list) { u.order = null; u.hide = null; AssaultOrder(u, p, rally); }
                        if (es > fs * .6f && state != GState.Menu) AiSmoke(team, p);
                        if (mineGroup) Msg("Assaulting " + pn + "!");
                    }
                }
                else if (st.phase == 1)
                {   // assault in two bounds: riflemen go to a line 40 m short of the zone first (tanks and MGs already have the zone under fire),
                    // then into the zone together; break off and regroup if the attack is shredded against a stronger defence
                    if (now < st.startStr * .45f && es > fs * 1.5f) { st.phase = 3; st.since = t; st.bounded = false; foreach (var u in list) { u.hide = null; if (u.def.kind == "tank" || Support(u)) continue; SOrder(u, rally.x + FU.Rand(-8, 8), rally.y + FU.Rand(-8, 8), p, true, true); } if (mineGroup) Msg("Assault on " + pn + " stalled — falling back to regroup."); }   // armour and overwatch stay where they are and keep firing
                    else
                    {
                        if (!st.bounded)
                        {
                            var bound = new Vector2(p.x + dx * (p.r + 40), p.z + dz * (p.r + 40)); int atB = 0, foot = 0;
                            foreach (var u in list) { if (u.def.kind == "tank" || Support(u) || u.def.kind == "gun") continue; foot++; if (FU.Hyp(u.x - bound.x, u.z - bound.y) < 30 || FU.Hyp(u.x - p.x, u.z - p.z) < p.r + 10) atB++; }
                            if (foot == 0 || atB >= foot * .55f || t - st.since > 30) { st.bounded = true; foreach (var u in list) { u.order = null; if (u.def.kind != "tank" && !Support(u)) { AssaultOrder(u, p, rally); Bark(u, "charge", .35f); } } if (mineGroup) Msg("Going in!"); }
                        }
                        foreach (var u in list) if (u.order == null && u.hide == null && u.focus == null && u.idle > 1) AssaultOrder(u, p, rally, !st.bounded);
                        // armour follows the infantry in once the zone is more ours than theirs: before that it fires from the flank
                        if (st.bounded && fs > es * 1.2f) foreach (var u in list) if (u.def.kind == "tank" && FU.Hyp(u.x - p.x, u.z - p.z) > p.r + 30 && (u.order == null || u.idle > 4)) { var q = SectionSpot(u, p, .5f); SOrder(u, q.x, q.y, p, false, false); }
                    }
                }
                else if (st.phase == 3) { if (t - st.since > 18) { st.phase = 0; st.since = t; } }
                if (st.phase == 2) foreach (var u in list) if (u.order == null && u.focus == null && (u.hide == null || u.hide.t < 1)) HoldOrder(u, p, threat);
            }
        }
        void AssaultOrder(Unit u, CapPoint p, Vector2 rally, bool toBound = false)
        {
            if (u.def.kind == "gun") return;
            if (Support(u)) { var so = Standoff(u, p); if (so != null) SOrder(u, so.Value.x + FU.Rand(-4, 4), so.Value.y + FU.Rand(-4, 4), p, false, false); return; }   // overwatch
            if (u.def.kind == "tank")
            {   // armour does not lead the charge into a village: it stands off on a flank, 70–90 m out, and shoots the infantry in
                // (left or right of the axis by id, so a platoon covers both sides); SecureTick moves it in once the zone is won
                var d = (rally - new Vector2(p.x, p.z)).normalized; var side = new Vector2(-d.y, d.x) * (u.id % 2 == 0 ? 1 : -1); float so = p.r + (u.id % 3) * 10 + 50;
                var q = new Vector2(p.x, p.z) + d * so * .7f + side * so * .6f; var f = FreeSpot(q.x, q.y, 12, 2); SOrder(u, f.x, f.y, p, false, false); return;
            }
            if (toBound) { var P = new Vector2(p.x, p.z); var b = P + (rally - P).normalized * (p.r + 40); var o = SectionSpot(u, p, .5f) - P; SOrder(u, b.x + o.x * .3f, b.y + o.y * .3f, p, false, false); return; }   // first bound: the line 40 m short of the zone, each section on its own side
            var q2 = SectionSpot(u, p, .7f); SOrder(u, q2.x, q2.y, p, false, u.def.kind == "cav");   // into the zone, by sections, not onto the flag
        }
        void HoldOrder(Unit u, CapPoint p, Vector2 threat)
        {
            u.home = new Vector2(p.x, p.z); u.leash = p.r + 20; if (u.def.kind == "gun") return;
            float d = FU.Hyp(u.x - p.x, u.z - p.z); bool sup = Support(u) || u.def.kind == "tank";
            if (!sup && d < p.r + 10 && u.hideCd <= 0)
            {   // into cover inside the zone, facing the enemy
                var c = Structures.Find(u, threat.x, threat.y, t);
                if (c != null && FU.Hyp(c.x - p.x, c.z - p.z) < p.r + 12) { u.hide = new Hide { x = c.x, z = c.z, t = 25, fire = true, low = c.low, trench = c.trench }; return; }
            }
            if (d > p.r + (sup ? 20 : 6) || (u.idle > 8 && d > p.r))
            {
                var dir = (new Vector2(p.x, p.z) - threat).normalized; float back = sup ? Mathf.Min(p.r * .6f, 30) + 6 : 0, j = p.inside ? 2 : Mathf.Min(p.r * .5f, 25);
                var q = p.rect ? p.RandIn(.6f) : new Vector2(p.x + FU.Rand(-j, j), p.z + FU.Rand(-j, j)); SOrder(u, q.x + dir.x * back, q.y + dir.y * back, p, false, false);
            }
        }
        /// Player command: give the selected units / squad an objective to secure.
        void SecureCmd(List<Unit> list, int pi)
        {
            if (pi < 0 || pi >= World.points.Count) { Msg("No objective there."); return; }
            var p = World.points[pi]; if (p.locked && p.owner != ME) { Msg(p.n + " is lost and cannot be retaken."); return; }
            int n = 0; foreach (var u in list) { if (!u.alive || u == player || u.def.kind == "gun") continue; u.secure = pi; u.follow = false; u.order = null; u.queue.Clear(); u.focus = null; u.hide = null; u.manual = true; n++; }
            if (n == 0) { Msg("No units to send."); return; }
            secureState.Remove(ME * 100 + pi); PingAt(p.x, p.z, UI.Gold); Msg($"{n} unit{(n == 1 ? "" : "s")} ordered to secure {p.n}.");
        }
        int PointAt(float x, float z) { int best = -1; float bd = 1e9f; for (int i = 0; i < World.points.Count; i++) { var p = World.points[i]; float d = FU.Hyp(p.x - x, p.z - z); if (d < p.r + 25 && d < bd) { bd = d; best = i; } } return best; }

        List<Unit> SpawnWave(int team, FWave w)
        {
            var o = new List<Unit>(); var b = SpawnPos(team);
            if (state == GState.Menu && units.Count(u => u.alive) > 150) return o;
            o.AddRange(SpawnReserve(team));
            foreach (var c in w.comp) { if (!Defs.BASE.ContainsKey(c.type) || (team == 0 && c.type == "cav")) continue; sectCounter++; for (int i = 0; i < c.n; i++) { var p = FreeSpot(b.x, b.z, 14, 1); var nu = SpawnUnit(team, c.type, p.x, p.y, FaceEnemy(team)); nu.sect = sectCounter; o.Add(nu); spawned[team]++; } }
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
                var sp = M.spawn[0]; float dx = x - sp.x, dz = z - sp.z, l = Mathf.Max(.01f, FU.Hyp(dx, dz)); dx /= l; dz /= l;
                foreach (float lat in new[] { -7f, 7f }) SpawnPlane(team, x, z, dx, dz, lat, 90, true, 9);
                var lead = planes[planes.Count - 2]; lead.snd = audio.Follow("siren", lead.t);   // the wail rides the lead Stuka's dive and is cut at bomb release
            }
            else
            {   // the battery firing behind the caller's own line is heard before the shells arrive (it used to be silent until impact)
                float rear = team == M.attacker ? -1 : 1;
                for (int g = 0; g < 4; g++) { float dl = g * .35f + FU.Rand(0, .2f); var gun = Far(rear, 250); gun.y += 2; StartCoroutine(Later(dl, () => FarSound("rumble", gun, .5f, GunPitch))); }
                for (int k = 0; k < 14; k++) shells.Add(new Shell { at = t + 3 + k * .45f + FU.Rand(0, .3f), p = new Vector3(x + FU.Rand(-14, 14), 0, z + FU.Rand(-14, 14)), r = 6, dmg = 80, team = -1 });
            }
        }
        void SpawnPlane(int team, float x, float z, float dx, float dz, float lat, float speed, bool strike, float life, float y = 0)
        {
            var go = ModelLib.Spawn("stuka", null, false); var pl = new Plane { t = go.transform, prop = ModelLib.Find(go.transform, "prop"), t0 = t + (strike ? 3 : 0), speed = speed, lat = lat, life = life, x = x, z = z, dx = dx, dz = dz, y = y, strike = strike, dropped = new bool[5], team = team };
            go.transform.rotation = Quaternion.LookRotation(new Vector3(dx, 0, dz)); planes.Add(pl);
            pl.eng = audio.Follow("plane", go.transform, y > 100 ? .25f : .55f, true, 30, 700, .8f); if (pl.eng && y > 100) pl.eng.pitch = .8f;   // high bombers: bigger, slower-turning engines, distant   // inverted-V12 engine (Bf 109 recording, nearest to the Ju 87's Jumo 211); Doppler sweeps the pass
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
                if (p.strike) for (int b = 0; b < 5; b++) if (!p.dropped[b] && s >= bombs[b]) { p.dropped[b] = true; float bx = p.x + p.dx * bombs[b] - p.dz * p.lat, bz = p.z + p.dz * bombs[b] + p.dx * p.lat; Combat.Explode(this, new Vector3(bx, World.H(bx, bz), bz), 7, 110, -1, null, null, true); }
                if (p.snd && p.strike && s > bombs[4] + 6) SoundBank.Fade(p.snd, dt, .45f);   // pulling out of the dive: siren dies away, no tail over the bomb bursts
                else if (p.snd && !p.strike && p.life < 2.5f) SoundBank.Fade(p.snd, dt, 2f);
                if (p.eng && p.life < 2.5f) SoundBank.Fade(p.eng, dt, 2f);
                if (p.life <= 0) { Destroy(p.t.gameObject); planes.RemoveAt(i); }
            }
        }
        void UpdateShells() { for (int i = shells.Count - 1; i >= 0; i--) { var s = shells[i]; if (t >= s.at) { shells.RemoveAt(i); Combat.Explode(this, new Vector3(s.p.x, World.H(s.p.x, s.p.z), s.p.z), s.r, s.dmg, s.team, null, null, true); } } }
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
                SpawnPlane(0, FU.Rand(-80, 80), -dir * 330, 0, dir, 0, 95, false, 8, 85);    // level flyover: engine only (SpawnPlane gives every plane its engine) — the siren only sounds in a dive
            }
            todT -= dt; if (todT <= 0) { todT = 1; vis.Tod(t); if (rts) vis.RtsFog(true); }
            Invasion(dt);
        }
        /// The invasion beyond the map: German guns flashing on the horizon behind the attackers and shells landing deep
        /// in the Polish rear, bomber streams droning east, firefights in the distance, towns burning on the skyline.
        float horT = 3, bomberT = 20, farFightT = 6, farSmokeT;
        Vector3 Far(float side, float spread)
        {   // a point beyond the map edge: side +1 = behind the defenders, -1 = behind the attackers
            var a = M.spawn[M.attacker]; var d = M.spawn[1 - M.attacker]; var ax = new Vector2(d.x - a.x, d.z - a.z).normalized; var pr = new Vector2(-ax.y, ax.x);
            float r = M.half + FU.Rand(120, 380), x = ax.x * r * side + pr.x * FU.Rand(-spread, spread), z = ax.y * r * side + pr.y * FU.Rand(-spread, spread); return new Vector3(x, World.H(Mathf.Clamp(x, -M.half, M.half), Mathf.Clamp(z, -M.half, M.half)), z);
        }
        void FarSound(string k, Vector3 src, float vol, float pitch = -1)
        {   // distant sounds arrive late and quiet, from the right direction (Shot adds the last 60 m of travel itself)
            var l = cam.transform.position; var dir = (src - l); float dist = dir.magnitude; var at = l + dir.normalized * 60;
            StartCoroutine(Later(Mathf.Min(3.5f, Mathf.Max(0, dist - 60) / 343f), () => audio.Shot(k, at, vol, false, 0, pitch)));
        }
        // Field artillery has one clean recording (see SoundBank.ONLY): the gun's report is played a little sharp and the shell's burst
        // deeper and duller, so firing and landing no longer sound like the same event, and repeats don't sound copy-pasted.
        static float GunPitch => FU.Rand(1.04f, 1.14f);
        static float BurstPitch => FU.Rand(.8f, .92f);
        void Invasion(float dt)
        {
            if (range != null || World.points.Count == 0 || state == GState.Menu) return;
            horT -= dt;
            if (horT <= 0)
            {   // German batteries firing from behind the attackers, and their shells bursting behind the Polish line
                horT = FU.Rand(2.5f, 6.5f); var gun = Far(-1, 500); gun.y += 2;
                fx.Puff(gun, new Color(1, .7f, .35f, 1), 6, 14, .25f, Vector3.zero, true); FarSound("rumble", gun, .55f, GunPitch);
                var land = Far(1, 450); StartCoroutine(Later(FU.Rand(3, 6), () => { fx.Puff(land + Vector3.up * 3, new Color(1, .6f, .3f, 1), 8, 18, .3f, Vector3.zero, true); fx.Puff(land + Vector3.up * 8, new Color(.2f, .18f, .16f, .8f), 10, 26, 6, Vector3.up * 3); FarSound("rumble", land, .7f, BurstPitch); }));
            }
            bomberT -= dt;
            if (bomberT <= 0)
            {   // a bomber stream at altitude heading for the Polish rear
                bomberT = FU.Rand(55, 95); float dir = M.spawn[1 - M.attacker].z > M.spawn[M.attacker].z ? 1 : -1, x0 = FU.Rand(-M.half * .6f, M.half * .6f), y = FU.Rand(170, 240); int n = Random.Range(3, 7);
                for (int k = 0; k < n; k++) { int row = (k + 1) / 2, sg = k % 2 == 0 ? 1 : -1; SpawnPlane(0, x0 + sg * row * 22, -dir * (M.half + 260) - dir * row * 26, 0, dir, 0, 62, false, (2 * M.half + 520) / 62, y + row * 4); }
                audio.Shot("drone", cam.transform.position + Vector3.up * 60, .7f);
            }
            farFightT -= dt;
            if (farFightT <= 0)
            {   // a firefight somewhere off the flank: tracer bursts and the stutter of an MG
                farFightT = FU.Rand(4, 10); var a = Far(FU.Rand(-.3f, 1), 700); var b = a + new Vector3(FU.Rand(-120, 120), 0, FU.Rand(-120, 120)); a.y += 1.5f; b.y = World.H(Mathf.Clamp(b.x, -M.half, M.half), Mathf.Clamp(b.z, -M.half, M.half)) + 1;
                int burst = Random.Range(5, 12); for (int k = 0; k < burst; k++) { int kk = k; StartCoroutine(Later(kk * .09f, () => fx.Tracer(a, b + new Vector3(FU.Rand(-6, 6), FU.Rand(-1, 3), FU.Rand(-6, 6))))); }
                FarSound("mg", a, .25f);
            }
            farSmokeT -= dt;
            if (farSmokeT <= 0)
            {   // burning towns and villages on the skyline
                farSmokeT = .45f; foreach (var s in World.farSmoke) fx.Puff(s + new Vector3(FU.Rand(-6, 6), FU.Rand(0, 6), FU.Rand(-6, 6)), new Color(.13f, .12f, .11f, .75f), FU.Rand(10, 16), FU.Rand(30, 46), FU.Rand(14, 20), new Vector3(FU.Rand(.5f, 2), FU.Rand(4, 7), FU.Rand(-.5f, .5f)));
            }
        }
        System.Collections.IEnumerator Later(float s, System.Action a) { yield return new WaitForSeconds(s); a(); }
        void FakeBoom()
        {
            if (World.points.Count == 0) return; var p = World.points[Random.Range(0, World.points.Count)]; float a = Random.value * 6.28f, d = 22 + Random.value * 40, x = p.x + Mathf.Cos(a) * d, z = p.z + Mathf.Sin(a) * d;
            if (Mathf.Abs(x) > M.half || Mathf.Abs(z) > M.half) return; fx.Explosion(new Vector3(x, World.H(x, z), z), FU.Rand(3, 5.5f)); audio.Shot("boom", new Vector3(x, World.H(x, z), z));
        }
        /// Voice bark (roadmap A2): one short line per event from the man's position, rate-limited per man (6–10 s) and per side (1.2 s) so a
        /// platoon under fire does not become a choir. Silent until recordings exist in Resources/Audio/voice_<g|p>_<event>/.
        readonly float[] sideBarkT = new float[2];
        public void Bark(Unit u, string ev, float chance = .6f)
        {
            if (u == null || !u.alive || !u.Foot || u == player) return; string k = "voice_" + (u.team == 0 ? "g" : "p") + "_" + ev; if (!audio.Has(k)) return;
            if (Time.time < u.barkT || Time.time < sideBarkT[u.team] || Random.value > chance) return;
            u.barkT = Time.time + FU.Rand(6, 10); sideBarkT[u.team] = Time.time + 1.2f; audio.Shot(k, u.Eye, 1);
        }
        // ---------------- ambience (roadmap T8: A1 positional distant battle, A3 wind gusts, A6 footsteps by surface) ----------------
        float warT, gustT, gustK = 1;
        /// Footstep with the surface under the boot: hard and bright on a road, hollow with a little debris on a floor, dull in grass, heavy in water.
        public void Footstep(float x, float z, Vector3 at, float vol, bool own, float pan = 0)
        {
            float depth = World.Depth(x, z); string sf = depth > .06f ? "water" : World.InBuildingG(x, z, 0) ? "floor" : World.NearRoadG(x, z, 0) ? "road" : "grass";
            switch (sf)
            {
                case "road": audio.Shot("step", at, vol * 1.25f, own, pan, FU.Rand(1.1f, 1.2f)); break;
                case "floor": audio.Shot("step", at, vol * 1.3f, own, pan, FU.Rand(.82f, .9f)); if (Random.value < .3f) audio.Shot("debris", at, vol * .12f, own, pan); break;
                case "water": audio.Shot("step", at, vol * 1.4f, own, pan, FU.Rand(.62f, .72f)); break;
                default: audio.Shot("step", at, vol * .9f, own, pan, FU.Rand(.92f, 1.0f)); break;
            }
        }
        /// The war beyond the map edge: single rifle shots, MG bursts, guns and rumbles played at real positions 650–1100 m away on the enemy's side,
        /// so the battle has a direction before you see it. The wind bed gusts (Perlin), swelling with rain. Nothing here needs new recordings.
        void UpdateAmbience(float dt)
        {
            if (M == null || M.range) return;
            gustT -= dt; if (gustT <= 0) { gustT = .25f; gustK = .55f + .6f * Mathf.PerlinNoise(Time.time * .07f, 3.7f) + vis.rainI * .5f; audio.Ambience("amb_wind", .22f * gustK); }
            warT -= dt; if (warT > 0 || listenerPos == default) return;
            warT = FU.Rand(2.5f, 7f) * (t < 20 ? 1.6f : 1);
            // direction: toward the enemy's rear (their spawn, mirrored across the centre), ±70°
            var es = SpawnPos(EN); float ang = Mathf.Atan2(es.x, es.z) + FU.Rand(-1.2f, 1.2f), dist = FU.Rand(650, 1100);
            var p = listenerPos + new Vector3(Mathf.Sin(ang), 0, Mathf.Cos(ang)) * dist; p.y = World.H(Mathf.Clamp(p.x, -M.half, M.half), Mathf.Clamp(p.z, -M.half, M.half)) + 2;
            float r = Random.value; string k = r < .4f ? "k98" : r < .65f ? (EN == 0 ? "mg42" : "browning") : r < .85f ? "cannon" : "rumble";
            int n = k == "mg42" || k == "browning" ? Random.Range(4, 9) : 1; float gain = k == "rumble" ? 1.2f : k == "cannon" ? 3 : 6;   // the distance law alone would put a rifle at 800 m below the -42 dB cut: this is many rifles, not one
            StartCoroutine(FarBurst(k, p, n, gain));
        }
        System.Collections.IEnumerator FarBurst(string k, Vector3 p, int n, float gain)
        {
            for (int i = 0; i < n; i++) { audio.Shot(k, p, gain); if (n > 1) yield return new WaitForSeconds(FU.Rand(.08f, .13f)); }
        }
        Vector3 listenerPos => audio.listener ? audio.listener.position : default;

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
                foreach (var u in units) { if (!u.alive) continue; if (p.Contains(u.x, u.z)) { if (u.team == att) a++; else d++; } }
                p.g = a; p.e = d;
                if (!p.locked && i == act)
                {
                    int diff = a - d; float boost = p.pushT > 0 ? 2.2f : 1; p.pushT -= dt;
                    if (p.ap > p.best + .005f) { p.best = p.ap; p.stallT = 0; } else p.stallT += dt;
                    if (!menu && p.stallT > 75 && !(p.pushT > 0))
                    {
                        p.stallT = 0; p.pushT = 40;
                        foreach (var u in units.Where(u => u.alive && u.team == att && !u.manual && u != player && u.rout <= 0 && u.def.kind != "gun")) { var q = p.RandIn(.6f); u.order = new Order { x = q.x, z = q.y, cx = p.x, cz = p.z }; u.queue.Clear(); u.leash = 60; u.idle = 0; }
                        tix[att] += 8; if (M.waves != null && M.waves.Length > 0) SpawnWave(att, M.waves[Mathf.Min(att, M.waves.Length - 1)]);
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
                    foreach (var cu in units) if (cu.alive && cu.team == att && p.Contains(cu.x, cu.z)) { cu.score += 50; if (cu == player) Msg("+50 objective captured"); }
                    if (menu) { if (pts.All(q => q.locked)) { foreach (var q in pts) { q.locked = false; q.ap = 0; q.owner = 1 - att; q.best = 0; q.stallT = 0; } foreach (var ln in lines) ln.heldBy = -1; secureState.Clear(); } }
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
            if (World.points.Count > 0 && World.points.All(p => p.locked)) { EndGame(att, att == ME ? "All " + World.points.Count + " objectives captured!" : "The enemy has captured every objective."); return; }
            if (tix[att] <= 0) { EndGame(def, def == ME ? "The attackers have run out of reinforcements." : "Your reinforcements are exhausted."); return; }
            if (tix[def] <= 0 && !units.Any(u => u.alive && u.team == def)) { EndGame(att, def == ME ? "Your force has been wiped out." : "The defenders have been wiped out."); return; }   // the defender could sit on the death screen with nothing left
            if (t >= M.time) EndGame(def, def == ME ? "You held out until the clock ran down." : "Time ran out before the objectives were taken.");
        }
        int NextMission() { for (int i = missionIdx + 1; i < missions.Length; i++) if (!missions[i].range) return i; return -1; }
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
                    if (k.med) { player.hp = Mathf.Min(player.max, player.hp + 15); player.bleed = 0; player.bandages = Mathf.Max(player.bandages, 2); Msg("+ Medical kit: wounds dressed, bandages restocked"); } else { FullResupply(player); Msg("+ Ammo crate: ammo and grenades restocked"); }
                }
                if (take || k.t <= 0) { Destroy(k.g.gameObject); pickups.RemoveAt(i); }
            }
        }
        void FullResupply(Unit u)
        {
            if (u.weps == null) return; u.Save(); foreach (var id in u.weps) { var w = Defs.WEAPONS[id]; if (w.clip <= 0) continue; var s = u.ws[id]; u.ws[id] = new Vector2Int(s.x, w.reserve); }
            int clip = u.clip; u.Sync(); u.clip = clip; var c = ClassOf(u); if (c != null) { u.gren = Mathf.Max(u.gren, c.gren); u.smoke = Mathf.Max(u.smoke, c.smoke); }
        }
        void Resupply(Unit u)
        {
            if (u.weps == null) return; var sp = SpawnPos(ME); if (FU.Hyp(u.x - sp.x, u.z - sp.z) > 16) return; bool any = false; u.Save();
            foreach (var id in u.weps) { var w = Defs.WEAPONS[id]; if (w.clip <= 0) continue; var s = u.ws[id]; if (s.y < w.reserve * .98f) { u.ws[id] = new Vector2Int(s.x, w.reserve); any = true; } }
            int clip = u.clip; float rl = u.reload; u.Sync(); u.clip = clip; u.reload = rl;
            var c = ClassOf(u); if (c != null) { if (u.gren < c.gren) { u.gren = c.gren; any = true; } if (u.smoke < c.smoke) { u.smoke = c.smoke; any = true; } }
            if (u.Foot && u.bandages < 2) { u.bandages = 2; any = true; } if (any) Msg("Resupplied at the ammo crates.");
        }
        /// The infantry class a soldier belongs to: the one he was deployed as, or (for a soldier you took control of) the
        /// class matching his unit type and weapons. Null for crews, horsemen and anything without a class.
        ClassDef ClassOf(Unit u)
        {
            if (u.cls >= 0 && u.cls < Defs.CLASSES.Length) return Defs.CLASSES[u.cls];
            if (u.def.kind != "inf") return null; string k = u.officer ? "officer" : u.type == "mg" ? "gunner" : u.type == "sniper" ? "sniper" : u.type == "rifle" ? "rifleman" : null;
            if (k == "rifleman" && u.weps != null && u.weps.Length > 0 && (u.weps[0] == "mp38" || u.weps[0] == "bar28")) k = "assault";
            return k == null ? null : Defs.CLASSES.FirstOrDefault(c => c.key == k);
        }
        public void OnPlayerKill(Unit tgt, bool head)
        {
            streakT = 6; streak++; cp += 2;   // no kill-streak banners or streak rewards
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
            if (u.sv) { u.SetRenderers(true); u.sv.SetFirstPerson(!tp, cam.transform); } else if (tp) u.SetRenderers(true);   // own body + gun replace the viewmodel; third person needs a visible body whatever the model
            cookT = -1; meleeT = -1; hold = gasp = false; fireQueued = 0;   // a grenade cooked by the last body must not go off in the new one's hand
            foreach (var c in u.go.GetComponentsInChildren<Collider>()) c.enabled = true;   // hitboxes stay live: AI rounds are real rays and must be able to strike you; your own casts skip you via CastRay(shooter)
            yaw = u.heading; pitch = 0; ads = 0; recoil = 0; rts = false; state = GState.Playing;
            BuildViewModel(); Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; vis.Tod(t); vis.Post(true); hintUntil = Time.unscaledTime + 14; hudStance = -1; hudHp = u.hp; hudWep = null;
        }
        void ReleasePlayer()
        {
            if (player == null) return; var u = player; player = null; if (u.sv) u.sv.SetFirstPerson(false, null); if (vm) vm.gameObject.SetActive(false); if (tankVm) tankVm.gameObject.SetActive(false);
            if (u.alive) { u.SetRenderers(true); u.ring.SetActive(selected.Contains(u)); foreach (var c in u.go.GetComponentsInChildren<Collider>()) c.enabled = true; u.home = new Vector2(u.x, u.z); u.manual = true; }
            if (vm) vm.gameObject.SetActive(false);
        }
        public void BuildViewModel()
        {
            var w = player.Weapon; string kind = w == null ? "none" : w.vm;
            if (player.sv && w != null && !w.melee) { if (vm) Destroy(vm.gameObject); vm = null; vmKind = ""; return; }   // POLYGON body/gun is the viewmodel
            // a weapon with its own viewmodel (vm_<team>_<id>, e.g. the Walther P38) uses it instead of its kind's (the Luger)
            string vmName = w != null && ModelLib.Has("vm_" + player.team + "_" + w.id) ? "vm_" + player.team + "_" + w.id : "vm_" + player.team + "_" + kind;
            if (vm && vmKind == vmName + "/" + (w != null ? w.id : "")) { vm.gameObject.SetActive(true); return; }
            if (vm) Destroy(vm.gameObject); vm = null; vmKind = vmName + "/" + (w != null ? w.id : "");
            if (w == null || kind == "none" || !ModelLib.Has(vmName)) return;
            vm = ModelLib.Spawn(vmName, cam.transform, false).transform; vm.localPosition = Vector3.zero; vm.localRotation = Quaternion.identity;
            vmFist = null; MountPackViewModel(w); VmCollectParts();
            vmMuzzleT = null; vmGunMf = null; if (vmMuzzleLocal == null && !w.melee) vmMuzzleT = FindVmMuzzle();   // v4 viewmodels: a marker on the real barrel tip (follows the gun's animation)
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
            var md = ms.delta.ReadValue() * .0022f * sens * Mathf.Lerp(1, fov / 70f, .85f);
            yaw += md.x; pitch = Mathf.Clamp(pitch + md.y, -1.45f, 1.45f);
            if (u.def.kind == "inf") { if (u.stanceT <= 0 && kb.cKey.wasPressedThisFrame) u.SetStance(u.stance == 1 ? 0 : 1); if (u.stanceT <= 0 && kb.zKey.wasPressedThisFrame) u.SetStance(u.stance == 2 ? 0 : 2); } else u.stance = 0;   // RO2: getting up / down takes time, during which you can neither fire nor sprint
            float pwd = World.Depth(u.x, u.z);
            if (u.def.kind == "inf" && pwd > .5f && u.stance == 2) { u.stance = 1; Msg("Too deep to go prone."); }
            if (u.def.kind == "inf" && pwd > .86f && u.stance != 0) { u.stance = 0; }
            int st = u.stance; u.stanceT = Mathf.Max(0, u.stanceT - dt);
            float f = 0, s = 0; if (kb.wKey.isPressed) f++; if (kb.sKey.isPressed) f--; if (kb.dKey.isPressed) s++; if (kb.aKey.isPressed) s--;
            float fx = Mathf.Sin(yaw), fz = Mathf.Cos(yaw), rx = Mathf.Cos(yaw), rz = -Mathf.Sin(yaw);
            float mx = fx * f + rx * s, mz = fz * f + rz * s, len = FU.Hyp(mx, mz); u.moving = len > 0;
            bool wantSpr = kb.leftShiftKey.isPressed && f > 0 && st == 0 && ads < .3f && u.bandaging <= 0 && pwd < .5f && u.stanceT <= 0; u.sprinting = wantSpr && u.stam > (u.spr ? .02f : .2f); u.spr = u.sprinting;
            if (u.sprinting) u.stam = Mathf.Max(0, u.stam - dt * (foot ? .09f : .05f)); else u.stam = Mathf.Min(1, u.stam + dt * (len > 0 ? .14f : .3f));   // ~11 s of sprint with a full bar (was ~6 s), back in ~7 s walking
            if (len > 0)
            {
                mx /= len; mz /= len; float sp = u.def.speed * (u.sprinting ? Defs.SprintMul(u.def.kind) : 1) * SPD[st] * (u.bandaging > 0 ? .4f : 1) * (u.stanceT > 0 ? .3f : 1) * (1 - ads * .45f) * (1 - Mathf.Clamp(World.Slope(u.x, u.z) - .25f, 0, .4f)) * World.WadeMul(u, pwd) * (u.immobile ? 0 : 1), ox = u.x, oz = u.z;
                u.x += mx * sp * dt; u.z += mz * sp * dt; u.Collide(units, u); if (World.BlockedFor(u, u.x, u.z)) { u.x = ox; u.z = oz; }
                if (u.def.kind == "inf" && st != 2 && pwd < .3f)
                {   // hobnailed boots: one step per stride at a real cadence (3.6 m/s walk / 1.7 m stride ~ 2.1 steps/s; sprint ~ 2.8/s; crouch ~ 1.65/s),
                    // feet alternating left/right in the stereo field with a little level variation; prone crawling and wading make none
                    stepDist += FU.Hyp(u.x - ox, u.z - oz);
                    if (stepDist >= (u.sprinting ? 2.2f : st == 1 ? 1.2f : 1.7f))
                    { stepDist = 0; stepFoot = !stepFoot; Footstep(u.x, u.z, cam.transform.position, (st == 1 ? .5f : u.sprinting ? 1.1f : .8f) * FU.Rand(.8f, 1.1f), true, stepFoot ? -.2f : .2f); }
                }
                if (u.def.kind == "tank") u.heading += Mathf.Clamp(FU.AngDiff(u.heading, Mathf.Atan2(mx, mz)), -dt * 2, dt * 2);
                bob += dt * (u.sprinting ? 12 : st > 0 ? 5 : 8) * sp / u.def.speed;
            }
            if (foot) { u.heading += FU.AngDiff(u.heading, yaw) * Mathf.Min(1, dt * 10); u.turretH = yaw; }   // the body swings round a beat behind the eyes
            else if (u.def.kind == "gun")
            {   // field gun: the tube traverses ±28° on the carriage; past that the crew swings the whole gun round (2.4 rad/s, as the AI crews
                // do), and while being manhandled it is hauled round to face the way the gunner looks. Before this only the "turret" node
                // took the camera yaw and the carriage never turned, so the model looked bolted to the ground.
                const float TRAV = .5f; float off = FU.AngDiff(u.heading, yaw);
                if (u.moving) u.heading += Mathf.Clamp(off, -dt * 2f, dt * 2f);
                else if (Mathf.Abs(off) > TRAV) u.heading += Mathf.Clamp(off - Mathf.Sign(off) * TRAV, -dt * 2.4f, dt * 2.4f);
                u.turretH = u.heading + Mathf.Clamp(FU.AngDiff(u.heading, yaw), -TRAV, TRAV);
            }
            else u.turretH = yaw;
            u.GroundY(); u.Swim();
            u.cd -= dt; u.gcd -= dt; u.cd2 -= dt; u.supp = Mathf.Max(0, u.supp - dt * (t - u.suppT < 1.5f ? .05f : .3f)); u.heat = Mathf.Max(0, u.heat - dt * (w != null && w.mg ? .06f : .5f)); if (u.overheated && u.heat < .35f) { u.overheated = false; Msg("Barrel has cooled."); }
            if (u.bandaging <= 0) { Combat.Bleed(this, u, dt, false); if (!u.alive) return; }
            if (kb.hKey.wasPressedThisFrame) { if (u.bandages > 0 && u.bandaging <= 0 && (u.hp < u.max || u.bleed > 0)) { u.bandaging = 1.8f; Msg("Bandaging…"); } else if (u.bandages <= 0) Msg("No bandages left — resupply at your spawn."); }
            if (u.bandaging > 0) { u.bandaging -= dt; if (u.bandaging <= 0) { u.bleed = 0; u.bandages--; for (int wi2 = 0; wi2 < u.wounds.Length; wi2++) u.wounds[wi2] = Mathf.Min(u.wounds[wi2], .5f); Msg("Bandaged — bleeding stopped."); } }   // RO2: a field dressing stops the bleeding, it does not give health back
            if (kb.kKey.wasPressedThisFrame) ammoShowT = 3;   // check ammo (RO2): feel the magazine
            if (kb.pageUpKey.wasPressedThisFrame || kb.pageDownKey.wasPressedThisFrame) AdjustZero(u.Weapon, u.def.pen, kb.pageUpKey.wasPressedThisFrame ? 1 : -1);   // RO2: Page Up / Page Down set the rear sight
            if (kb.tKey.wasPressedThisFrame) { tp = !tp; if (u.sv) { u.SetRenderers(true); u.sv.SetFirstPerson(!tp, cam.transform); } else u.SetRenderers(tp); if (vm) vm.gameObject.SetActive(!tp); Msg(tp ? "Third person" : "First person"); }   // the first-person rifle used to float in front of the third-person camera
            if (kb.fKey.wasPressedThisFrame) { SwapControl(); return; }
            if (kb.nKey.wasPressedThisFrame) { buyOpen = !buyOpen; orderOpen = false; buyT = 8; }
            if (kb.vKey.wasPressedThisFrame) { orderOpen = !orderOpen; buyOpen = false; orderT = 6; }
            orderT -= dt; buyT -= dt; if (orderT <= 0) orderOpen = false; if (buyT <= 0) buyOpen = false;
            for (int i = 0; i < 7; i++) if (kb[Key.Digit1 + i].wasPressedThisFrame && (orderOpen || buyOpen))
                {
                    if (orderOpen) { orderOpen = false; switch (i) { case 0: SquadCommand("follow"); break; case 1: SquadCommand("move"); break; case 2: SquadCommand("cover"); break; case 3: SquadCommand("hold"); break; case 4: SquadCommand("order"); break; case 5: SquadCommand("move", true); break; case 6: { var sqm = SquadMembers(); if (sqm.Count == 0) Msg("No squad. Press V then 1 near friendly infantry to recruit them."); else { var (_, apt) = AimInfo(); int pi = apt.HasValue ? PointAt(apt.Value.x, apt.Value.z) : -1; SecureCmd(sqm, pi >= 0 ? pi : ActiveIdx()); } break; } } }
                    else { buyOpen = false; var l = BuyList(); if (i < l.Count) Buy(l[i], true); }
                    return;
                }
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
                    audio.Shot(w.mg ? "reload_mg" : boltGun ? "reload_bolt" : "reload_mag", cam.transform.position);
                }
            }
            bool adsHeld = ms.rightButton.isPressed;
            ads += ((adsHeld && w != null && !w.melee && u.def.kind != "tank" && !u.sprinting && !(u.reload > 0) && u.bandaging <= 0 && u.barrelT <= 0 && u.stanceT <= 0 ? 1 : 0) - ads) * Mathf.Min(1, dt * 9);
            if (debugAds >= 0) ads = debugAds;   // CLI / test captures: force the aim amount
            if (ms.leftButton.wasPressedThisFrame) fireQueued = .25f; fireQueued = Mathf.Max(0, fireQueued - dt);
            bool firing = ms.leftButton.isPressed;
            if (w != null)
            {
                if (w.melee) { if (firing && u.cd <= 0 && !u.sprinting && u.bandaging <= 0) Melee(u, w); }
                else if (w.mg && (u.overheated || u.barrelT > 0)) { if (fireQueued > 0 && u.overheated && u.barrelT <= 0) { fireQueued = 0; Msg("Barrel too hot — [X] change barrel."); } }
                else
                {
                    u.cd = Mathf.Max(u.cd, -dt);   // an idle gun doesn't bank shots
                    for (int n = 0; n < 4 && !u.sprinting && !(u.reload > 0) && u.bandaging <= 0 && u.stanceT <= 0 && pwd <= World.SWIM && u.cd <= 0 && (u.clip > 0 || w.clip == 0) && (w.auto ? firing : fireQueued > 0) && !(w.mg && u.overheated); n++)
                    { fireQueued = 0; PlayerFire(u, w, st); if (!w.auto) break; }   // several rounds per frame at low frame rates
                }
            }
            if (u.def.kind == "tank" && adsHeld && u.cd2 <= 0 && !u.sprinting) CoaxFire(u);
            if (kb.gKey.wasPressedThisFrame) { if (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) ThrowSmoke(u); else if (u.gren > 0 && u.gcd <= 0 && u.Foot) { cookT = 0; Msg(Gren(u.team).stick ? "Cord pulled — release G to throw." : "Pin pulled — release G to throw."); } }
            if (cookT >= 0)
            {
                cookT += dt;
                if (!kb.gKey.isPressed) ThrowGrenade(u);
                else if (cookT >= Gren(u.team).fuse) { cookT = -1; u.gren--; Msg("Held too long — it went off in your hand."); Combat.Explode(this, new Vector3(u.x, u.y + 1, u.z), Gren(u.team).rad, Gren(u.team).dmg, u.team, null, u); return; }   // held too long
            }
            // RO2 barrel change: an MG that overheats can't fire until the crew swaps the barrel (a few seconds, gun down)
            if (w != null && w.mg && kb.xKey.wasPressedThisFrame && u.barrelT <= 0 && u.heat > .2f) { u.barrelT = 4.5f; Msg("Changing barrel…"); }
            if (kb.eKey.wasPressedThisFrame && !kb.leftShiftKey.isPressed && !kb.qKey.isPressed) { if (ToggleVehicle()) return; }   // E: climb out of / into a vehicle (T27); Q/E lean still works
            if (u.barrelT > 0) { u.barrelT -= dt; if (u.barrelT <= 0) { u.heat = 0; u.overheated = false; Msg("Fresh barrel in."); } }
            mgRest = w != null && w.mg && !u.moving && foot && (st == 2 || (ads > .5f && RestedOnCover()));
            // breath holding (Shift while aiming, standing still) — port of fp.hold / fp.gasp
            if (ads > .5f && kb.leftShiftKey.isPressed && !u.moving && breath > 0 && !gasp) { hold = true; breath = Mathf.Max(0, breath - dt * .2f); if (breath <= 0) gasp = true; }
            else { hold = false; breath = Mathf.Min(1, breath + dt * .16f); if (breath > .4f) gasp = false; }
            lean += ((foot ? (kb.eKey.isPressed ? 1 : 0) - (kb.qKey.isPressed ? 1 : 0) : 0) - lean) * Mathf.Min(1, dt * 8);
            // camera
            swT += dt; float amp = foot ? (.0009f + (1 - u.stam) * .002f + u.supp * .003f + (u.moving ? .002f : 0) + (gasp ? .0015f : 0)) * (1 + (1 - ads) * 1.2f) * (st == 2 ? .4f : st == 1 ? .7f : 1) * (hold ? .15f : 1) * (mgRest ? .1f : 1) * (w != null && w.scope ? 2.2f : 1) * (1 - ads * .15f) : 0;
            float swx = (Mathf.Sin(swT * 1.3f) + Mathf.Sin(swT * 2.7f + 1.2f) * .6f) * amp, swy = (Mathf.Sin(swT * 1.1f + .7f) + Mathf.Sin(swT * 2.3f + 2.1f) * .6f) * amp;
            recoil *= Mathf.Max(0, 1 - dt * Mathf.Lerp(4.5f, 8, ads));   // slower recovery from the hip
            eye += ((foot ? (u.def.kind == "cav" ? 2.9f : mgRest ? (st == 2 ? .33f : Mathf.Min(STAND[st], restTop > 0 ? restTop - u.y + .2f : STAND[st])) : STAND[st]) : u.def.kind == "tank" ? 2.5f : 1.5f) - eye) * Mathf.Min(1, dt * 7);
            float bb = foot && u.moving ? Mathf.Sin(bob) * .04f * (1 - ads) : 0, lo = lean * .38f;
            if (tp)
            {   // third person (port of the fp.tp camera): orbit behind the unit, pulled in by walls / terrain
                string kd = u.def.kind; float baseD = kd == "tank" ? 11 : kd == "gun" ? 7 : kd == "cav" ? 5.5f : 3.4f, dd = baseD * (1 - ads * .4f), side = (kd == "tank" || kd == "gun") ? 0 : .8f * (1 - ads * .5f), up = kd == "tank" ? 2 : kd == "gun" ? 1.4f : .3f;
                float pivY = foot ? (kd == "cav" ? 2.5f : STAND[st]) : (kd == "tank" ? 2.4f : 1.6f);
                tpPivot = new Vector3(u.x, u.y + pivY, u.z); var f3 = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch)); var rgt = new Vector3(Mathf.Cos(yaw), 0, -Mathf.Sin(yaw));
                var target = tpPivot - f3 * dd + rgt * side + Vector3.up * up;
                if (Physics.Linecast(tpPivot, target, out var hit, ~0, QueryTriggerInteraction.Ignore)) target = hit.point + (tpPivot - target).normalized * .3f;
                cam.transform.position = target; cam.transform.rotation = Quaternion.Euler((-pitch - recoil * .4f - swy) * Mathf.Rad2Deg, (yaw + swx) * Mathf.Rad2Deg, 0);
                if (debugOrbit.HasValue)
                {   // test hook (dbg orbit <deg> <dist>): look at the player's own model from any side
                    float oy = yaw + debugOrbit.Value * Mathf.Deg2Rad; var c = new Vector3(u.x, u.y + 1.35f, u.z);
                    cam.transform.position = c + new Vector3(Mathf.Sin(oy), .12f, Mathf.Cos(oy)) * debugOrbitD; cam.transform.rotation = Quaternion.LookRotation(c - cam.transform.position);
                }
            }
            else
            {
                cam.transform.position = new Vector3(u.x + Mathf.Cos(yaw) * lo, u.y + eye + bb - Mathf.Abs(lean) * .08f, u.z - Mathf.Sin(yaw) * lo);
                cam.transform.rotation = Quaternion.Euler((-pitch - recoil * .6f - swy) * Mathf.Rad2Deg, (yaw + swx) * Mathf.Rad2Deg, -lean * .16f * Mathf.Rad2Deg);
            }
            if (shake > 0) cam.transform.position += Random.insideUnitSphere * shake * .12f;
            holdZ += ((hold || (mgRest && ads > .5f && kb.leftShiftKey.isPressed) ? 1 : 0) - holdZ) * Mathf.Min(1, dt * 6);
            float tf = Mathf.Lerp(tp ? baseFov - 8 : baseFov, w != null ? w.ads : 40, ads * (tp ? .45f : 1)) * Mathf.Lerp(1, w != null && w.scope ? .8f : .68f, holdZ * ads) + (u.sprinting ? 5 : 0);
            if (u.def.kind == "tank" && !tp) tf = Mathf.Lerp(baseFov, 26, ads);   // gunner's telescope (TZF 4 on the Panzer II: 2.5x)
            fov = tf; cam.fieldOfView = tf;
            if (u.sv)
            {   // the unit's own POLYGON model is the viewmodel / third-person body: feed it the camera aim
                u.sv.ads = ads; u.sv.hasAim = true; u.sv.aimPoint = cam.transform.position + cam.transform.forward * 80;
                u.sv.raise = ads > .3f || Time.time - u.lastShot < .8f; u.sv.hideFP = w != null && ((w.scope && ads > .85f) || w.melee) && !tp;
                if (u.sv.firstPerson == tp) u.sv.SetFirstPerson(!tp, cam.transform);
            }
            else if (foot) u.aimP = Mathf.Clamp(pitch, -.6f, .6f);   // v4 models: in third person the gun follows the camera pitch
            TankViewModel(u, dt);
            if (vm && w != null)
            {
                bool scoped = w.scope && ads > .85f && !tp; vm.gameObject.SetActive(!scoped && !tp);
                vmRlW = Mathf.MoveTowards(vmRlW, u.reload > 0 ? 1 : 0, dt * 3.5f);
                var hp = HIP.TryGetValue(w.vm, out var h) ? h : HIP["rifle"]; float a = ads, rl = vmParts.Count > 0 ? 0 : Mathf.SmoothStep(0, 1, vmRlW), sprint = u.sprinting ? 1 : 0, mv = u.moving ? 1 : 0;
                var hip = new Vector3(hp.x, hp.y, -hp.z);
                vm.localPosition = Vector3.Lerp(hip, Vector3.zero, a) + new Vector3(mv * Mathf.Sin(bob * .5f) * .008f * (1 - a) - sprint * .05f, -rl * .22f - sprint * .07f + mv * Mathf.Abs(Mathf.Sin(bob)) * .012f * (1 - a), -recoil * .07f);
                float rxv = recoil * .22f + rl * .5f + sprint * .35f, ryv = Mathf.Lerp(hp.w, 0, a) + sprint * .45f, rzv = -lean * .12f + (w.melee ? recoil * .8f : 0);
                vm.localRotation = Quaternion.AngleAxis(-rxv * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(-ryv * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(rzv * Mathf.Rad2Deg, Vector3.forward);
                if (w.melee) MeleePose(w, mv, sprint, dt);
                else VmAnim(u, w, dt);
            }
        }

        // ---------------- v4 viewmodel animation (RO2-style): every moving part is its own node in the model ----------------
        // rifle: bolt (lift + draw), stripper clip in the charger guides, single round, magazine follower; SMG / BAR: magazine,
        // reciprocating cocking handle; MG34: feed cover, belt drum, belt stepping one link per shot, charging handle; ckm:
        // cloth belt, cocking handle (pulled twice); P08 toggle / Vis slide; casings leave the 'port' marker.
        readonly Dictionary<string, Transform> vmParts = new Dictionary<string, Transform>();
        readonly Dictionary<Transform, Pose> vmBase = new Dictionary<Transform, Pose>();
        float vmRlW, vmShotSeen = -99, vmCloseT = -99, vmEjectFor = -99; bool vmWasSingles, vmFromEmpty, vmWasReload; Transform vmPort;
        static float Seg(float x, float a, float b) => Mathf.Clamp01((x - a) / (b - a));
        static float Ease(float x) => x * x * (3 - 2 * x);
        void VmCollectParts()
        {
            vmParts.Clear(); vmBase.Clear(); vmPort = null; vmShotSeen = -99; vmCloseT = -99; vmWasSingles = false; vmWasReload = false; vmRlW = 0;
            if (!vm) return;
            foreach (var n in new[] { "bolt", "clip", "round", "follower", "mag", "chandle", "cover", "drum", "belt", "toggle", "slide" })
            { var t = ModelLib.Find(vm, n); if (t) { vmParts[n] = t; vmBase[t] = new Pose(t.localPosition, t.localRotation); } }
            vmPort = ModelLib.Find(vm, "port");
            foreach (var n in new[] { "clip", "round", "follower" }) if (vmParts.TryGetValue(n, out var t)) t.gameObject.SetActive(false);
        }
        Transform VP(string n) => vmParts.TryGetValue(n, out var t) ? t : null;
        /// POLYGON pack gun in first person (Kar98k, MP40 for the MP 38, Luger): replaces the v4 gun mesh, rear and front sight
        /// tops on the eye line (so ADS looks straight down the pack's own sights), the pack grip where the v4 wrist is; its bolt,
        /// magazine and toggle take the v4 part names so the same animations drive them. Casings and flashes use its port / muzzle.
        Vector3? vmMuzzleLocal; float? debugOrbit; float debugOrbitD = 2.2f;
        void MountPackViewModel(WeaponDef w)
        {
            vmMuzzleLocal = null; if (!vm || vm.childCount == 0 || w == null) return;
            // Kar98k stays on the project's own Mauser in first person: the pack K98's rear sight is a solid block with no notch, so down
            // its sights you saw a grey slab with the front post barely peeking over it. The v4 rifle (the wz.29 uses the same one) has a
            // proper notch-and-post sight picture. Third person still shows the pack gun.
            if (w.id == "kar98k" || w.id == "mp38") return;   // the pack MP40 has the same solid-block rear sight; the v4 SMG model has a real notch
            var g = PackGuns.Get(PackGuns.KeyFor(w.id)); if (g == null) return;
            var box = vm.GetChild(0); var rig = g.rig; var gr = ModelLib.Find(box, "grip_r"); if (!gr) return;
            PackGuns.Blank(ModelLib.Find(box, "gunmesh"));
            foreach (var n in new[] { "bolt", "mag", "toggle", "chandle", "slide" }) { var t = ModelLib.Find(box, n); if (t) { PackGuns.Blank(t); t.name = "v4_" + n; } }
            var R = Quaternion.FromToRotation((rig.sightFront - rig.sight).normalized, Vector3.forward);
            var wrist = gr.localPosition + (w.vm == "pistol" ? new Vector3(.006f, -.008f, -.05f) : new Vector3(.008f, -.017f, -.046f));   // fist centre -> wrist
            // cheek weld: eye ~10 cm ahead of the butt (rifles / SMG), pistol held out at arm's length; the v4 arms follow the grip
            float relief = w.vm == "pistol" ? .42f : Mathf.Max(.18f, rig.sight.z - rig.stock.z - .10f);
            var pos = new Vector3(0, 0, relief) - R * rig.sight;
            var root = PackGuns.Spawn(g, box, false); root.localRotation = R; root.localPosition = pos;
            var arms = ModelLib.Find(box, "arms"); if (arms) arms.localPosition += pos + R * rig.rh - wrist;
            var port = ModelLib.Find(box, "port"); if (port) port.localPosition = pos + R * rig.port;
            vmMuzzleLocal = pos + R * rig.muzzle;
            Transform Re(Transform part, Vector3 pivot, string name)
            {   // new pivot (gun frame) for an animated part, named as the v4 part so VmAnim drives it
                var p = new GameObject(name).transform; p.SetParent(root, false); p.localPosition = pivot; part.SetParent(p, true); return p;
            }
            Transform Bone(string b) { for (int i = 0; i < root.childCount; i++) if (root.GetChild(i).name == b) return root.GetChild(i); return null; }
            PackGuns.Part PartOf(string b) { foreach (var p in g.parts) if (p.bone == b) return p; return null; }
            if (g.key == "K98") { var b = Bone("Weapon_handle"); var pp = PartOf("Weapon_handle"); if (b && pp != null) Re(b, new Vector3(rig.muzzle.x, rig.muzzle.y, pp.b.center.z), "bolt"); }   // turns about the bore
            if (g.key == "MP40") { var b = Bone("Weapon_handle.001"); var pp = PartOf("Weapon_handle.001"); if (b && pp != null) Re(b, pp.b.center, "mag"); }
            if (g.key == "Luger")
            {   // magazine = lowest moving part, toggle = highest (breaks up about its rear pin)
                PackGuns.Part lo = null, hi = null;
                foreach (var p in g.parts) { if (p.bone == rig.main) continue; if (lo == null || p.b.center.y < lo.b.center.y) lo = p; if (hi == null || p.b.center.y > hi.b.center.y) hi = p; }
                if (lo != null && Bone(lo.bone)) Re(Bone(lo.bone), lo.b.center, "mag");
                if (hi != null && hi != lo && Bone(hi.bone)) Re(Bone(hi.bone), new Vector3(hi.b.center.x, hi.b.min.y, hi.b.min.z), "toggle");
            }
        }
        void VmSet(string n, Vector3 dp, Quaternion dq, bool show = true)
        {
            var t = VP(n); if (!t) return; var b = vmBase[t]; t.localPosition = b.position + dp; t.localRotation = b.rotation * dq;
            if (t.gameObject.activeSelf != show) t.gameObject.SetActive(show);
        }
        void VmEject(WeaponDef w, Vector3 dirLocal)
        {
            if (!vmPort || !vm) return; var lib = SoldierLib.I; bool small = w.vm == "pistol" || w.vm == "smg";
            var pf = lib == null ? null : w.mg ? lib.caseMG : small ? lib.casePistol : lib.caseK98;
            var vel = vm.TransformDirection(dirLocal.normalized) * FU.Rand(2.2f, 3.4f) + Vector3.up * FU.Rand(.2f, .8f);
            SoldierProps.Eject(pf, small ? .019f : .057f, vmPort.position, vm.rotation * Quaternion.Euler(0, 90, 0), vel, 3.5f);
        }
        void VmAnim(Unit u, WeaponDef w, float dt)
        {
            if (vmParts.Count == 0) return;
            float now = Time.time, since = now - u.lastShot, p = u.reload > 0 && u.reloadDur > 0 ? Mathf.Clamp01(1 - u.reload / u.reloadDur) : 0;
            bool rel = u.reload > 0, newShot = u.lastShot > vmShotSeen + .001f; if (newShot) vmShotSeen = u.lastShot;
            if (rel && !vmWasReload) vmFromEmpty = u.clip == 0; vmWasReload = rel;
            var I = Quaternion.identity; string v = w.vm; float lower = 0;     // lower: head comes off the stock (bolt work, reloads)
            if (v == "rifle" || v == "sniper")
            {
                float L = 0, B = 0; float c = Seg(since, .16f, Mathf.Max(.5f, w.rof - .08f)); bool clipShow = false, roundShow = false; float clipY = 0, roundY = 0;
                if (!rel && since < w.rof + .1f && c < 1) { L = c < .5f ? Seg(c, 0, .22f) : 1 - Seg(c, .78f, 1); B = Seg(c, .22f, .45f) - Seg(c, .52f, .78f); if (c >= .4f && vmEjectFor != u.lastShot) { vmEjectFor = u.lastShot; VmEject(w, new Vector3(1, .9f, -.2f)); } }
                if (!rel && u.clip == 0 && c >= .45f) { L = 1; B = 1; }                                       // magazine empty: the follower holds the bolt open
                if (vmWasSingles && !u.singlesActive) vmCloseT = now; vmWasSingles = u.singlesActive;
                if (u.singlesActive) { L = 1; B = 1; roundShow = p > .08f && p < .86f; roundY = .045f * (1 - Ease(Seg(p, .08f, .5f))) - .016f * Ease(Seg(p, .5f, .82f)); }
                else if (rel && u.rkind == ReloadKind.Stripper)
                {
                    L = 1 - Seg(p, .84f, .96f); B = 1 - Seg(p, .68f, .84f);
                    clipShow = p > .1f && p < .64f; clipY = .07f * (1 - Ease(Seg(p, .1f, .28f))) - .03f * Ease(Seg(p, .32f, .58f));
                }
                else if (now - vmCloseT < .5f) { float q = Seg(now - vmCloseT, 0, .45f); B = 1 - Seg(q, 0, .55f); L = 1 - Seg(q, .55f, 1); }
                VmSet("bolt", Vector3.back * .085f * Ease(B), Quaternion.AngleAxis(80 * Ease(L), Vector3.forward));
                VmSet("clip", new Vector3(0, clipY, 0), I, clipShow); VmSet("round", new Vector3(0, roundY, 0), I, roundShow);
                VmSet("follower", Vector3.zero, I, B > .5f && u.clip == 0);
                lower = Mathf.Max(L, B) * .6f + (rel ? .4f : 0);
            }
            else if (v == "smg" || v == "bar" || v == "pistol")
            {
                if (newShot) VmEject(w, v == "pistol" && VP("toggle") ? new Vector3(.3f, 1.4f, -.3f) : new Vector3(1, .6f, -.2f));
                float k = Mathf.Sin(Mathf.PI * Seg(since, 0, Mathf.Min(w.rof, .14f)));                     // one recoil cycle of the action
                bool open = u.clip == 0 && !(rel && p > .86f);                                              // empty: slide / toggle locked back
                float magY = rel && u.rkind == ReloadKind.Mag ? -(v == "pistol" ? .16f : .3f) * (Ease(Seg(p, .05f, .28f)) - Ease(Seg(p, .5f, .76f))) : 0;
                VmSet("mag", new Vector3(0, magY, 0), I, !(rel && p > .28f && p < .5f));
                float ch = vmFromEmpty && rel ? Seg(p, .8f, .88f) - Seg(p, .9f, .97f) : 0;
                VmSet("chandle", Vector3.back * (.05f * k + .06f * ch), I);
                VmSet("slide", Vector3.back * .028f * Mathf.Max(k, open ? 1 : 0), I);
                { float tg = Mathf.Max(k, open ? 1 : 0); VmSet("toggle", Vector3.back * .012f * tg, Quaternion.AngleAxis(-24 * tg, Vector3.right)); }   // toggle breaks upward at the knee as the breech recoils
                lower = rel ? 1 : 0;
            }
            else if (v == "mg" || v == "ckm")
            {
                if (newShot) VmEject(w, new Vector3(FU.Rand(-.2f, .2f), -1, -.15f));
                float step = Seg(since, 0, w.rof * .85f), pitchB = v == "mg" ? .0147f : .03f;               // belt advances one link per round
                bool mr = rel && u.rkind == ReloadKind.Mag;
                float out_ = mr ? Ease(Seg(p, .16f, .34f)) - Ease(Seg(p, .5f, .7f)) : 0; bool hid = mr && p > .34f && p < .5f;
                if (v == "mg")
                {
                    VmSet("cover", Vector3.zero, Quaternion.AngleAxis(70 * (mr ? Ease(Seg(p, .03f, .15f)) - Ease(Seg(p, .74f, .86f)) : 0), Vector3.right));
                    VmSet("drum", new Vector3(-.12f, -.22f, 0) * out_, I, !hid);
                    VmSet("belt", new Vector3(pitchB * (step - 1), 0, 0) + new Vector3(-.12f, -.22f, 0) * out_, I, !hid);
                    VmSet("chandle", Vector3.back * .09f * (mr ? Seg(p, .88f, .93f) - Seg(p, .94f, .98f) : 0), I);
                }
                else
                {
                    VmSet("belt", new Vector3(0, pitchB * (step - 1), 0) + new Vector3(-.08f, -.25f, 0) * out_, I, !hid);
                    VmSet("chandle", Vector3.back * .1f * (mr ? Seg(p, .72f, .77f) - Seg(p, .78f, .82f) + Seg(p, .86f, .9f) - Seg(p, .91f, .95f) : 0), I);
                }
                lower = rel ? 1 : 0;
            }
            // reload / bolt pose: the gun comes down off the eye, rolls and tips its action toward the camera
            float r = Ease(Mathf.Clamp01(lower)); vmLowW = Mathf.MoveTowards(vmLowW, r, dt * 4);
            float lw = Ease(vmLowW); bool rifleRel = (v == "rifle" || v == "sniper");
            vm.localPosition += new Vector3(-.05f * lw, -.05f * lw, .05f * lw);
            vm.localRotation = vm.localRotation * Quaternion.Euler(rifleRel ? -12 * lw : -8 * lw, 6 * lw, (rifleRel ? 22 : v == "mg" || v == "ckm" ? 10 : 30) * lw);
        }
        float vmLowW;

        /// Cone half-width (radians, per axis) of one shot. Hip fire is a last resort, moving costs accuracy, lower stances
        /// steady the gun, suppression shakes it, held breath tightens it; MGs are only accurate deployed on the bipod.
        public static float ShotSpread(WeaponDef w, float heat, bool moving, float adsK, int st, float supp, bool held, bool rested)
        {
            float s = (w.spread > 0 ? w.spread : .005f) + (w.mg ? heat * heat * .004f : 0);   // a hot barrel only opens the group near its limit
            s *= moving ? 1.8f : 1;
            s *= Mathf.Lerp(w.vm == "pistol" || w.vm == "smg" ? 2.6f : w.mg ? 2f : 3.4f, 1, adsK);
            s *= st == 2 ? .5f : st == 1 ? .75f : 1;
            s *= 1 + supp * 1.5f;
            if (held) s *= .8f;
            if (w.mg) s *= rested ? .22f : 3f;
            return s;
        }
        /// In a tank (first person) the turret front is in view: the gun mantlet, the main gun barrel and the coaxial MG
        /// beside it. Hip view looks over the gun; in the gunner's sight it sits low and dead ahead. The barrel recoils
        /// when it fires, and both barrels are where their rounds and flashes come from.
        Transform tankVm, tankBarrel, tankGunTip, tankCoaxTip, tankVmTurret; float tankKick; int tankVmTeam = -1; Vector3 tankEye;
        void TankViewModel(Unit u, float dt)
        {
            bool on = u.def.kind == "tank" && !tp && u.alive && state == GState.Playing;
            if (!on) { if (tankVm) tankVm.gameObject.SetActive(false); return; }
            if (!tankVm || tankVmTeam != u.team)
            {
                if (tankVm) Destroy(tankVm.gameObject); tankVmTeam = u.team; int body = u.team == 0 ? 0x4b5048 : 0x505a3e, dark = 0x262824; tankVmTurret = null;
                if (ModelLib.Has("tankview_" + u.team))
                {   // the vehicle's own model (crew left out): turret, mantlet, main gun, coax and hull front, seen from the hatch
                    tankVm = ModelLib.Spawn("tankview_" + u.team, null, false).transform; tankVmTurret = ModelLib.Find(tankVm, "turret");
                    foreach (var c in tankVm.GetComponentsInChildren<Collider>()) Destroy(c);
                    tankBarrel = new GameObject("Barrel").transform; tankBarrel.SetParent(tankVmTurret, false);
                    tankGunTip = new GameObject("GunTip").transform; tankGunTip.SetParent(tankVmTurret, false);
                    tankCoaxTip = new GameObject("CoaxTip").transform; tankCoaxTip.SetParent(tankVmTurret, false);
                    if (u.team == 0) { tankGunTip.localPosition = new Vector3(-.07f, .25f, 2.04f); tankCoaxTip.localPosition = new Vector3(.15f, .25f, 1.27f); tankEye = new Vector3(0, 1.08f, -.27f); }   // 2 cm KwK 30, MG 34, cupola
                    else { tankGunTip.localPosition = new Vector3(0, .28f, 2.1f); tankCoaxTip.localPosition = new Vector3(.17f, .26f, 1.1f); tankEye = new Vector3(-.15f, 1.08f, -.2f); }      // 37 mm Bofors, ckm wz.30, hatch
                }
                else
                {
                tankVm = new GameObject("TankView").transform; tankVm.SetParent(cam.transform, false);
                Transform Part(Transform par, PrimitiveType k, Vector3 pos, Vector3 rot, Vector3 sc, int hex) { var g = GameObject.CreatePrimitive(k); Destroy(g.GetComponent<Collider>()); g.transform.SetParent(par, false); g.transform.localPosition = pos; g.transform.localEulerAngles = rot; g.transform.localScale = sc; var r = g.GetComponent<Renderer>(); r.sharedMaterial = Mats.Lit(hex); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; return g.transform; }
                Part(tankVm, PrimitiveType.Cube, new Vector3(0, 0, .05f), Vector3.zero, new Vector3(.62f, .3f, .2f), body);                   // gun mantlet
                tankBarrel = new GameObject("Barrel").transform; tankBarrel.SetParent(tankVm, false);
                Part(tankBarrel, PrimitiveType.Cylinder, new Vector3(0, 0, .25f), new Vector3(90, 0, 0), new Vector3(.16f, .25f, .16f), body);   // barrel sleeve
                Part(tankBarrel, PrimitiveType.Cylinder, new Vector3(0, 0, 1.55f), new Vector3(90, 0, 0), new Vector3(.085f, 1.1f, .085f), dark); // main gun tube
                Part(tankBarrel, PrimitiveType.Cylinder, new Vector3(0, 0, 2.62f), new Vector3(90, 0, 0), new Vector3(.11f, .045f, .11f), dark);  // muzzle
                tankGunTip = new GameObject("GunTip").transform; tankGunTip.SetParent(tankBarrel, false); tankGunTip.localPosition = new Vector3(0, 0, 2.7f);
                Part(tankVm, PrimitiveType.Cylinder, new Vector3(.3f, .05f, .3f), new Vector3(90, 0, 0), new Vector3(.06f, .22f, .06f), dark);    // coax MG jacket
                Part(tankVm, PrimitiveType.Cylinder, new Vector3(.3f, .05f, .72f), new Vector3(90, 0, 0), new Vector3(.025f, .22f, .025f), dark); // coax barrel
                tankCoaxTip = new GameObject("CoaxTip").transform; tankCoaxTip.SetParent(tankVm, false); tankCoaxTip.localPosition = new Vector3(.3f, .05f, .95f);
                }
            }
            if (tankVmTurret)
            {   // world-space: hull on the vehicle heading, turret on the aim, the commander's eye on the camera
                tankKick = Mathf.MoveTowards(tankKick, 0, dt * .9f); tankVm.gameObject.SetActive(ads < .85f);
                tankVm.rotation = Quaternion.Euler(0, u.heading * Mathf.Rad2Deg, 0); tankVmTurret.localRotation = Quaternion.Euler(0, (yaw - u.heading) * Mathf.Rad2Deg, 0);
                tankVm.position += cam.transform.position - tankVmTurret.TransformPoint(tankEye) - tankVmTurret.forward * tankKick * .12f;
                return;
            }
            tankVm.gameObject.SetActive(true); tankKick = Mathf.MoveTowards(tankKick, 0, dt * .9f);
            tankVm.localPosition = Vector3.Lerp(new Vector3(-.32f, -.58f, 1.15f), new Vector3(-.2f, -.4f, 1.75f), ads); tankVm.localRotation = Quaternion.identity;   // the sight sits beside the gun: mantlet, barrel and coax all in frame
            tankBarrel.localPosition = new Vector3(0, 0, -tankKick);   // recoil: the tube runs back into the mantlet and returns
        }
        void PlayerFire(Unit u, WeaponDef w, int st)
        {
            bool cannon = u.def.pen; if (cannon && u.gunDead) { u.cd = 1; Msg("Main gun is out of action — coaxial MG only."); return; }
            float spread = ShotSpread(w, u.heat, u.moving, ads, st, u.supp, hold, mgRest);
            if (w.clip > 0) u.clip--; u.cd = Mathf.Max(u.cd, -.05f) + (cannon ? u.def.rof : w.rof); u.lastShot = Time.time;
            if (u.def.kind == "gun" && Crew.Alive(u) == 0) u.cd *= 1.8f;   // T27: no loader — you lay, load and fire the piece yourself
            var dir = (cam.transform.rotation * new Vector3(FU.Rand(-spread, spread), FU.Rand(-spread, spread), 1)).normalized; var o = cam.transform.position;
            float range = w.range > 0 ? w.range : cannon ? 1200 : 800;   // a rifle round carries well past anything on these maps; it used to stop dead at def.range*2.2 (165 m for a Mauser)
            var (dist, _, _) = CastRay(o, dir, range, u.team, u);   // where the sights point (only to aim the round; the round itself flies and drops)
            Vector3 muzzle;
            if (tp) { var A = o + dir * dist; var d2 = (A - tpPivot); float L = d2.magnitude; dir = d2 / Mathf.Max(.01f, L); o = tpPivot + dir * .6f; muzzle = !u.sv && u.muzzleT ? u.muzzleT.position : tpPivot + dir * 1.3f; }
            else muzzle = cannon && tankGunTip && tankVm.gameObject.activeSelf ? tankGunTip.position : u.sv && u.sv.MuzzleOnScreen(cam).HasValue ? u.sv.MuzzleOnScreen(cam).Value : vm && vm.gameObject.activeSelf ? (vmMuzzleLocal.HasValue ? vm.TransformPoint(vmMuzzleLocal.Value) : VmMuzzleNow() ?? vm.TransformPoint(VmMuzzle(w.vm))) : o + cam.transform.forward * .9f;
            if (tp && u.sv && u.sv.MuzzleWorld.HasValue && !cannon) muzzle = u.sv.MuzzleWorld.Value;   // third person: straight from the model's barrel
            var end = o + dir * dist;
            fx.MuzzleFlash(muzzle, cannon ? cam.transform.forward : (end - muzzle), cannon ? 1.4f : w.vm == "pistol" ? .16f : w.mg ? .32f : .26f); audio.Shot(cannon ? "cannon" : w.snd, o, 1, true);   // own weapon: dry, centred, full level
            // RO2: rifles and SMGs fire invisible rounds; only machine guns load tracer (one in five) and shells are seen in flight
            bool tracer = cannon || (w.mg && ++u.tracerN % 5 == 0);
            { float spd = MuzzleSpeed(w, cannon); dir = Elevate(dir, Elevation(spd, Drag(w, cannon), PlayerZero(w, cannon))); }   // sights zeroed (Page Up / Page Down)
            if (cannon) FireRound(u, o, muzzle, dir, MuzzleSpeed(w, true), null, true, true, u.def.dmg, u.def.splash, u.def.dmg * .5f); else FireRound(u, o, muzzle, dir, MuzzleSpeed(w, false), w, false, tracer);
            if (cannon) tankKick = .38f;
            float rc = (cannon ? .8f : w.recoil) * (w.mg ? (mgRest ? .35f : 1.7f) : 1); recoil = Mathf.Min(1, recoil + rc * (w.auto ? .35f : 1)); float kick = w.mg ? (mgRest ? .3f : 1.6f) : 1; pitch += (cannon ? .045f : w.climb >= 0 ? w.climb : w.auto ? .005f : .024f) * (1.4f - ads * .75f) * kick; yaw += FU.Rand(-1, 1) * (w.wobble >= 0 ? w.wobble : w.auto ? .003f : .0025f) * (1.6f - ads * .6f) * kick;
            if (w.mg) { u.heat = Mathf.Min(1, u.heat + .016f); if (u.heat >= 1 && !u.overheated) { u.overheated = true; Msg("Barrel overheated! Press [X] to change the barrel."); } }
            if (cannon) shake = Mathf.Max(shake, .9f);
        }

        // ---------------- rounds in flight (RO2: real projectiles with drop, drag and flight time; no hitscan) ----------------
        class Round { public Vector3 p, vis, v; public Vector2 from; public Unit by; public int team; public float travelled, life = 6, drag; public WeaponDef w; public bool cannon, tracer, cracked; public float dmg, splash, cdmg, supp; public LineRenderer lr; }
        readonly List<Round> rounds = new List<Round>();
        /// Muzzle velocity by weapon: 7.92 mm Mauser 760, MG 34 755, ckm wz.30 (.30-06) 850, BAR 805, 9 mm 380-390, pistols 350, 37 mm / 2 cm ~780 m/s.
        public static float MuzzleSpeed(WeaponDef w, bool cannon)
        {
            if (cannon || w == null) return 780; switch (w.vm) { case "ckm": return 850; case "bar": return 805; case "mg": return 755; case "smg": return 385; case "pistol": return 350; default: return 760; }
        }
        /// Launch a round. `o` is where it really starts (the eye / the gun), `vis` where its streak is drawn from (the muzzle).
        /// Small arms carry the weapon's own damage and falloff; a cannon round carries the vehicle's direct damage and burst.
        /// Velocity loss per metre: a 9 mm pistol round sheds speed much faster than a 7.92 mm spitzer; a 37 mm / 2 cm shell barely slows.
        public static float Drag(WeaponDef w, bool cannon) => cannon ? .00018f : w != null && (w.vm == "smg" || w.vm == "pistol") ? .0011f : .00045f;

        // ---------------- sight zeroing (RO2): rounds leave slightly nose-up and cross the line of sight at the zero range ----------------
        // Before, every round left exactly along the line of sight and started falling from the first metre: a Kar98k was 34 cm low at
        // 200 m and 81 cm low at 300 m, so a chest shot at 250 m went into the dirt. Zeroed at 300 m the same rifle stays within ±22 cm
        // of the aim point out to ~350 m — RO2's "battle sight" — and Page Up / Page Down sets the sights for longer shots.
        static readonly Dictionary<long, float> elevCache = new Dictionary<long, float>();
        /// Launch elevation (radians above the line of sight) that puts a round with this speed and drag back on the line at `range` metres.
        /// Solved once per (speed, drag, 5 m range bucket) with the exact integrator UpdateRounds uses, so what the sights promise is what flies.
        public static float Elevation(float speed, float drag, float range)
        {
            if (range < 5 || speed < 1) return 0; int rb = Mathf.RoundToInt(range / 5); long key = ((long)Mathf.RoundToInt(speed) << 40) ^ ((long)Mathf.RoundToInt(drag * 1e6f) << 16) ^ rb;
            if (elevCache.TryGetValue(key, out var a)) return a; float R = rb * 5; a = 0;
            for (int it = 0; it < 6; it++)
            {
                float vx = speed * Mathf.Cos(a), vy = speed * Mathf.Sin(a), x = 0, y = 0, dt = 1 / 90f, hitY = 0; bool got = false;
                for (int n = 0; n < 2000 && !got; n++)
                {
                    float sp = Mathf.Sqrt(vx * vx + vy * vy), nx = x + vx * dt, ny = y + vy * dt;
                    if (nx >= R) { float f = (R - x) / Mathf.Max(1e-4f, nx - x); hitY = y + (ny - y) * f; got = true; break; }
                    x = nx; y = ny; vy -= 9.81f * dt; float k = Mathf.Max(.2f, 1 - drag * sp * dt); vx *= k; vy *= k;
                }
                if (!got) break; a -= Mathf.Atan2(hitY, R);
            }
            a = Mathf.Clamp(a, 0, .2f); elevCache[key] = a; return a;
        }
        /// Rear-sight settings per weapon kind (metres) and the default the soldier starts with.
        public static int[] ZeroSteps(WeaponDef w, bool cannon)
        {
            if (cannon) return new[] { 200, 300, 400, 500, 600, 800, 1000, 1200 };
            if (w == null) return new[] { 300 };
            switch (w.vm)
            {
                case "pistol": return new[] { 50 };
                case "smg": return new[] { 100, 200 };                                              // MP 38: flip sight, 100 / 200 m
                case "mg": case "ckm": return new[] { 200, 300, 400, 500, 600, 700, 800, 1000 };
                case "bar": return new[] { 100, 200, 300, 400, 500, 600, 800 };
                case "knife": case "sabre": case "none": return new[] { 0 };
                default: return new[] { 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000 };     // Kar98k / wz.29 tangent sight (RO2: 100 m steps)
            }
        }
        public static int DefaultZero(WeaponDef w, bool cannon) => cannon ? 400 : w == null ? 300 : w.vm == "pistol" ? 50 : w.vm == "smg" ? 100 : w.mg || w.vm == "ckm" ? 400 : 300;
        readonly Dictionary<string, int> playerZero = new Dictionary<string, int>();   // per weapon id (the tank gun is "tank_gun")
        int PlayerZero(WeaponDef w, bool cannon) { string id = cannon ? "tank_gun" : w != null ? w.id : ""; return playerZero.TryGetValue(id, out var z) ? z : DefaultZero(w, cannon); }
        float zeroShowT;
        void AdjustZero(WeaponDef w, bool cannon, int dir)
        {
            var steps = ZeroSteps(w, cannon); if (steps.Length < 2) return; int cur = PlayerZero(w, cannon), i = System.Array.IndexOf(steps, cur); if (i < 0) i = 0;
            int ni = Mathf.Clamp(i + dir, 0, steps.Length - 1); if (ni == i) return;
            playerZero[cannon ? "tank_gun" : w.id] = steps[ni]; zeroShowT = 2.5f; audio.Shot("reload_bolt", cam.transform.position, .12f, true);   // a click of the sight leaf
        }
        /// Tilts a direction up by `elev` radians in its own vertical plane.
        public static Vector3 Elevate(Vector3 dir, float elev)
        {
            if (elev <= 0) return dir; var right = Vector3.Cross(Vector3.up, dir); if (right.sqrMagnitude < 1e-6f) return dir;
            return Quaternion.AngleAxis(-elev * Mathf.Rad2Deg, right.normalized) * dir;
        }

        public void FireRound(Unit by, Vector3 o, Vector3 vis, Vector3 dir, float speed, WeaponDef w, bool cannon, bool tracer, float dmg = 0, float splash = 0, float cdmg = 0)
        {
            var r = new Round { p = o, vis = vis, v = dir.normalized * speed, from = new Vector2(o.x, o.z), by = by, team = by != null ? by.team : -1, w = w, cannon = cannon, tracer = tracer, dmg = dmg, splash = splash, cdmg = cdmg };
            r.drag = Drag(w, cannon);
            r.supp = by == player ? .12f : .16f;
            if (tracer) r.lr = fx.Streak(vis, !cannon);
            rounds.Add(r);
        }
        void UpdateRounds(float dt)
        {
            for (int i = rounds.Count - 1; i >= 0; i--)
            {
                var r = rounds[i]; float sp = r.v.magnitude, step = sp * dt; var dir = r.v / Mathf.Max(.01f, sp);
                var (dist, hu, _) = CastRay(r.p, dir, step, r.team, r.by);
                if (!r.cracked && r.by != null && r.by != player) { Combat.NearMiss(this, r.by, r.p, r.p + dir * dist, hu); r.cracked = (player != null && (r.p - player.Eye).sqrMagnitude < 16); }
                Combat.Whiz(this, r.p, r.p + dir * dist, r.team, r.cannon ? .45f : r.w != null && r.w.mg ? .08f : r.w != null && (r.w.vm == "smg" || r.w.vm == "pistol") ? .05f : .11f, r.from);
                if (hu != null || dist < step - 1e-3f) { Impact(r, r.p + dir * dist, hu, dir); fx.EndStreak(r.lr); rounds.RemoveAt(i); continue; }
                r.p += dir * step; r.vis += dir * step; r.travelled += step; r.life -= dt;
                r.v.y -= 9.81f * dt; r.v *= Mathf.Max(.2f, 1 - r.drag * step);
                if (r.life <= 0 || r.p.y < World.H(r.p.x, r.p.z) - 3 || Mathf.Abs(r.p.x) > M.half + 600 || Mathf.Abs(r.p.z) > M.half + 600) { fx.EndStreak(r.lr); rounds.RemoveAt(i); continue; }
                if (r.lr) fx.Streak(r.lr, r.vis - dir * Mathf.Min(r.cannon ? 1.2f : BulletFx.Length, r.travelled), r.vis, cam);
            }
        }
        void Impact(Round r, Vector3 e, Unit hu, Vector3 dir)
        {
            if (r.cannon)
            {
                if (hu != null) Combat.Shell(this, hu, r.by, r.by != null ? r.by.def : null, dir, r.travelled, r.dmg, e);
                var bp = hu != null ? e : new Vector3(e.x, Mathf.Max(World.H(e.x, e.z), e.y), e.z); Combat.Explode(this, bp, r.splash, r.cdmg, r.team, hu, r.by, true);
                return;
            }
            if (hu != null)
            {
                float dmg = r.w != null && r.w.dmg > 0 ? r.w.dmg * Mathf.Lerp(1, r.w.fallMul, Mathf.Clamp01((r.travelled - r.w.fallNear) / (r.w.fallFar - r.w.fallNear))) : r.dmg;
                Combat.Bullet(this, hu, dmg, castZone, r.by); return;
            }
            Combat.Suppress(this, e.x, e.z, r.team, r.supp * .5f, 4, r.from); fx.Dust(e);
            // A5 slice: a round that strikes masonry chips it audibly (the debris set pitched up); earth only puffs. Within 70 m of the listener, 40 % of hits.
            if (audio.listener && (audio.listener.position - e).sqrMagnitude < 70 * 70 && Random.value < .4f && World.InBuildingG(e.x, e.z, .7f)) audio.Shot("debris", e, .22f, false, 0, FU.Rand(1.2f, 1.45f));
        }

        public HitZone castZone;   // zone of the last unit CastRay hit
        /// A bullet's path: the first living soldier, horse, gun or tank it meets — on either side (RO2: friendly fire is on) — or the
        /// first solid thing. `shooter` is the man firing (his own hitboxes are skipped). Trees are checked afterwards (TreeStop). `team` is
        /// kept for the callers' sake but no longer lets a round pass through friends.
        public (float, Unit, bool) CastRay(Vector3 o, Vector3 dir, float range, int team, Unit shooter = null)
        {
            var r = CastSolid(o, dir, range, shooter);
            return World.trees.Count == 0 ? r : TreeStop(o, dir, r);
        }
        (float, Unit, bool) CastSolid(Vector3 o, Vector3 dir, float range, Unit shooter)
        {
            castZone = HitZone.Body;
            var hits = Physics.RaycastAll(o, dir, range, ~0, QueryTriggerInteraction.Collide); System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                var uh = h.collider.GetComponent<UnitHit>();
                if (uh != null)
                {
                    if (!uh.u.alive || uh.u == shooter) continue; castZone = uh.zone; bool hd = uh.head;
                    if (uh.zone == HitZone.Arm || uh.zone == HitZone.Leg)
                        foreach (var h2 in hits)
                        {   // a round through a limb carries on into the body right behind it (an arm across the chest does not stop a rifle bullet)
                            if (h2.distance <= h.distance || h2.distance > h.distance + .6f) continue; var u2 = h2.collider.GetComponent<UnitHit>();
                            if (u2 != null && u2.u == uh.u && (u2.zone == HitZone.Chest || u2.zone == HitZone.Stomach || u2.zone == HitZone.Head)) { castZone = u2.zone; hd = u2.head; break; }
                        }
                    return (h.distance, uh.u, hd);
                }
                if (h.collider.isTrigger) continue;
                return (h.distance, null, false);
            }
            return (range, null, false);
        }
        /// Woods are not glass (the batched tree meshes carry no colliders): a trunk on the path stops the round with a shower of
        /// splinters; each crown it passes through has a chance of turning it — about one round in seven per crown, so a man deep
        /// in a wood is hard to hit on purpose but far from safe.
        (float, Unit, bool) TreeStop(Vector3 o, Vector3 dir, (float d, Unit u, bool hd) r)
        {
            var a = o; var b = o + dir * r.d;
            if (World.TreeTrunk(a, b, out float tt)) { var p = a + dir * (r.d * tt); fx.Puff(p, new Color(.5f, .38f, .24f, .8f), .25f, .9f, .5f, Vector3.up * .8f); castZone = HitZone.Body; return (r.d * tt, null, false); }
            float tc = World.FirstCanopy(a, b);
            if (tc < 1 && Random.value < .14f) { var p = a + dir * (r.d * tc); fx.Puff(p, new Color(.3f, .42f, .2f, .7f), .3f, 1.1f, .6f, new Vector3(0, -.6f, 0)); castZone = HitZone.Body; return (r.d * tc, null, false); }
            return r;
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
            var (d, hit, head) = CastRay(cam.transform.position, cam.transform.forward, w.reach + .3f, u.team, u);
            if (hit == null) foreach (var e in units) { if (!e.alive || e.team == u.team) continue; var to = e.Chest - cam.transform.position; if (to.magnitude < w.reach && Vector3.Angle(cam.transform.forward, to) < 25) { hit = e; break; } }
            if (hit != null && !hit.def.armor) Combat.Damage(this, hit, w.dmg, false, false, u);
            audio.Shot("melee", u.Eye, .4f);   // swing swish (was the generic "rifle" key, which now resolves to a real gunshot)
        }

        void ThrowGrenade(Unit u)
        {
            if (u.gren <= 0 || u.gcd > 0 || !u.Foot) return; u.gren--; u.gcd = .9f; var s = Gren(u.team); float cooked = Mathf.Max(0, cookT); cookT = -1;
            var d = (cam.transform.rotation * new Vector3(0, s.stick ? .16f : .12f, 1)).normalized;   // the handle gives a higher, longer lob
            AddGrenade(u, cam.transform.position + d * .8f, d * s.speed * (u.stance == 2 ? .6f : u.stance == 1 ? .85f : 1), Mathf.Max(.15f, s.fuse - cooked));
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
        bool mgRest; float restTop;
        bool RestedOnCover()
        {
            var f = cam.transform.forward; f.y = 0; if (f.sqrMagnitude < 1e-4f) return false; f.Normalize(); var e = cam.transform.position;
            bool low = Physics.Raycast(e - Vector3.up * .3f, f, 1.1f, ~0, QueryTriggerInteraction.Ignore);
            bool high = Physics.Raycast(e + Vector3.up * .05f, f, 1.1f, ~0, QueryTriggerInteraction.Ignore);
            restTop = 0; if (low && !high && Physics.Raycast(e + f * .75f + Vector3.up * .1f, Vector3.down, out var top, 1.2f, ~0, QueryTriggerInteraction.Ignore)) restTop = top.point.y;   // the bipod sits on top of the cover
            return low && !high;
        }

        /// AI riflemen lob grenades at men in buildings, trenches and cover, or at bunched-up groups, 14-32 m away,
        /// never with a friend near the target. Germans cook the stick grenade a second first.
        float aiGrenT;
        void AiGrenades(float dt)
        {
            aiGrenT -= dt; if (aiGrenT > 0) return; aiGrenT = .5f;
            foreach (var u in units)
            {
                if (u != player && u.gcd > 0) u.gcd -= .5f;   // AI grenade cooldown runs here (the player's runs in the input loop)
                if (!u.alive || u == player || u.gren <= 0 || u.gcd > 0 || !u.Foot || u.rout > 0 || u.def.kind != "inf" || Random.value > .22f) continue;
                Unit tg = null; float bd = 1e9f;
                foreach (var e in units)
                {
                    if (!e.alive || e.team == u.team || !e.Foot) continue; float d = FU.Hyp(e.x - u.x, e.z - u.z); if (d < 14 || d > 32 || d >= bd) continue;
                    bool worth = Interiors.At(e.x, e.z) != null || e.hide != null || units.Count(o => o.alive && o.team == e.team && FU.Hyp(o.x - e.x, o.z - e.z) < 6) >= 3;
                    if (!worth || units.Any(o => o.alive && o.team == u.team && FU.Hyp(o.x - e.x, o.z - e.z) < 8)) continue;
                    bd = d; tg = e;
                }
                if (tg == null) continue;
                var s = Gren(u.team); float cook = s.stick ? 1.2f : .4f, tx = tg.x + FU.Rand(-2.5f, 2.5f), tz = tg.z + FU.Rand(-2.5f, 2.5f);
                var o0 = new Vector3(u.x, u.y + 1.5f, u.z); float dx = tx - o0.x, dz = tz - o0.z, L = FU.Hyp(dx, dz), ang = 40 * Mathf.Deg2Rad, dy = World.H(tx, tz) - o0.y;
                float v2 = 22 * L * L / (2 * Mathf.Cos(ang) * Mathf.Cos(ang) * Mathf.Max(1, L * Mathf.Tan(ang) - dy)), v = Mathf.Min(s.speed * 1.05f, Mathf.Sqrt(Mathf.Max(1, v2)));
                AddGrenade(u, o0, new Vector3(dx / L * Mathf.Cos(ang) * v, Mathf.Sin(ang) * v, dz / L * Mathf.Cos(ang) * v), s.fuse - cook);
                u.gren--; u.gcd = FU.Rand(10, 18); u.lastShot = Time.time; Bark(u, "grenade", .9f);
                if (tg == player || FU.Hyp(tg.x - (player != null ? player.x : 1e6f), tg.z - (player != null ? player.z : 1e6f)) < 10) Msg(u.team == 0 ? "\u201eHandgranate!\u201c — grenade incoming!" : "\u201eGranat!\u201d — grenade incoming!");
            }
        }
        void UpdateGrenades(float dt)
        {
            for (int i = grenades.Count - 1; i >= 0; i--)
            {
                var g = grenades[i]; g.fuse -= dt; g.v.y -= 22 * dt; var np = g.p + g.v * dt;
                if (Physics.Linecast(g.p, np, out var hit, ~0, QueryTriggerInteraction.Ignore)) { np = hit.point + hit.normal * .15f; g.v = Vector3.Reflect(g.v, hit.normal) * (g.stick ? .25f : .35f); }
                float gy = World.H(np.x, np.z) + (g.stick ? .05f : .08f), wy = World.WaterY(np.x, np.z);
                if (np.y < gy) { np.y = gy; g.v.y = -g.v.y * (g.stick ? .2f : .35f); g.v.x *= g.stick ? .45f : .6f; g.v.z *= g.stick ? .45f : .6f; if (g.v.sqrMagnitude < .5f) g.rest = true; }
                if (wy > -900 && np.y < wy) { g.v *= .3f; if (g.fuse > .2f && Random.value < .3f) fx.Puff(new Vector3(np.x, wy, np.z), new Color(.8f, .86f, .9f, .6f), .2f, .6f, .4f, Vector3.up); }
                g.p = np; g.t.position = np;
                if (!g.rest) g.t.Rotate(g.stick ? new Vector3(g.spin * dt, 0, 0) : new Vector3(g.spin * dt * .4f, g.spin * dt * .3f, 0), Space.Self);   // the stick tumbles end over end
                else if (g.stick) g.t.rotation = Quaternion.Slerp(g.t.rotation, Quaternion.Euler(90, g.t.eulerAngles.y, 0), dt * 10);   // and comes to rest lying on its side
                if (g.fuse <= 0)
                {
                    Destroy(g.t.gameObject); grenades.RemoveAt(i);
                    bool wet = wy > -900 && g.p.y < wy - .2f; Combat.Explode(this, g.p, g.rad * (wet ? .5f : 1), g.dmg * (wet ? .4f : 1), g.by.team, null, g.by);   // water smothers a grenade
                    if (wet) for (int k = 0; k < 6; k++) fx.Puff(new Vector3(g.p.x, wy, g.p.z), new Color(.85f, .9f, .95f, .9f), .6f, 2.4f, 1.2f, new Vector3(FU.Rand(-1, 1), FU.Rand(5, 9), FU.Rand(-1, 1)));
                }
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
            state = GState.Dead; respT = range != null ? 3 : Mathf.Max(5, WaveTime - (t % WaveTime)); streak = 0; cookT = -1; meleeT = -1;   // RO2: you come back with the next reinforcement wave
            if (vm) vm.gameObject.SetActive(false); if (tankVm) tankVm.gameObject.SetActive(false);
            if (player != null && player.go) { player.SetRenderers(true); if (player.sv) player.sv.SetFirstPerson(false, null); }
            Msg("You were killed. Choose a class and redeploy."); Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        void DeadCamera(float dt)
        {
            if (rts || lastPlayer == null) return; var p = new Vector3(lastPlayer.x, lastPlayer.y, lastPlayer.z);
            cam.transform.position = Vector3.Lerp(cam.transform.position, p + new Vector3(0, 9, -6), dt * 2); cam.transform.LookAt(p); cam.fieldOfView = 60;
        }
        /// RO2: no reinforcements left means no redeploy — take control of a survivor instead.
        bool CanDeploy => range != null || tix[ME] > 0;
        void TryDeploy(int ci) { if (CanDeploy) Deploy(ci); else Msg("No reinforcements left — press F to take control of a surviving soldier."); }
        void Deploy(int ci)
        {
            var c = Defs.CLASSES[ci]; var sp = RespawnPoint(); var q = FreeSpot(sp.x, sp.z, 10, .6f);
            player = null; lastClass = ci; breath = 1; tp = false;
            var u = new Unit { id = nextId++, team = ME, type = c.type, def = Defs.For(ME, c.type), x = q.x, z = q.y, heading = FaceEnemy(ME), turretH = FaceEnemy(ME) };
            u.y = World.H(q.x, q.y); u.hp = u.max = u.def.hp; u.home = q; u.prot = t + 3; u.InitWeapons(c.weps[ME]); u.gren = c.gren; u.smoke = c.smoke; u.cls = ci; u.BuildModel(unitRoot); units.Add(u);
            foreach (var o in units.Where(o => o.alive && o.team == ME && o != u && o.def.kind == "inf" && !o.squad && FU.Hyp(o.x - u.x, o.z - u.z) < 40).Take(4)) { o.squad = true; o.follow = true; o.slot = slotCounter++ % 12; o.manual = false; }
            rts = false; EnterFps(u);
        }

        // ---------------- RTS command map ----------------
        void EnterRts()
        {
            rts = true; if (player != null) { rtsPos = new Vector3(player.x, 0, player.z); rtsYaw = yaw; ReleasePlayer(); }
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true; vis.Tod(t); vis.RtsFog(true); vis.Post(false);
            Msg("Command map: LMB select · RMB move · Alt+RMB attack-move · Shift queue · 1-6 buy · B support · F take control · Tab back");
        }
        void LeaveRts()
        {
            var pick = selected.FirstOrDefault(u => u.alive && u.team == ME) ?? (lastPlayer != null && lastPlayer.alive ? lastPlayer : null);
            if (pick != null) { pick.manual = false; EnterFps(pick); } else { rts = false; state = GState.Dead; respT = range != null ? 3 : Mathf.Max(5, WaveTime - (t % WaveTime)); vis.Tod(t); }   // was respT = 0: leaving the map with nobody to take skipped the reinforcement wave
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
            var bl = BuyList(); for (int i = 0; i < bl.Count; i++) if (kb[Key.Digit1 + i].wasPressedThisFrame) Buy(bl[i]);
            if (kb.bKey.wasPressedThisFrame) ArmSupport();
            Vector2 mp = ms.position.ReadValue();
            if (ms.leftButton.wasPressedThisFrame && !OverHud(mp))
            {
                if (armSupport) { if (GroundAt(mp, out var gp)) { armSupport = false; CallSupport(ME, gp.x, gp.z, false); } return; }
                if (armSecure) { armSecure = false; if (GroundAt(mp, out var sgp)) SecureCmd(selected.ToList(), PointAt(sgp.x, sgp.z)); return; }
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
                    foreach (var u in units) { if (!u.alive || u.team != ME || u.mount != null) continue; var sp = cam.WorldToScreenPoint(new Vector3(u.x, u.y + 1, u.z)); if (sp.z > 0 && r.Contains(sp)) Select(u); }
                }
            }
            var enC = ms.rightButton.wasPressedThisFrame && selected.Count > 0 && !kb.leftShiftKey.isPressed && !kb.leftCtrlKey.isPressed ? EnemyAtScreen(mp) : null;
            if (enC != null)
            {   // right-click an enemy: focus fire
                foreach (var u in selected) { if (!u.alive) continue; u.secure = -1; u.focus = enC; u.order = null; u.queue.Clear(); u.follow = false; u.manual = true; u.hide = null; }
                PingAt(enC.x, enC.z, FU.Hex(0xff3322)); Msg("Engage " + Defs.NAMES[enC.team][enC.type] + "!");
            }
            else if (ms.rightButton.wasPressedThisFrame && selected.Count > 0 && GroundAt(mp, out var tgt))
            {
                bool queue = kb.leftShiftKey.isPressed, attackMove = kb.leftAltKey.isPressed; int i = 0, n = selected.Count, cols = Mathf.CeilToInt(Mathf.Sqrt(n));
                foreach (var u in selected)
                {
                    if (!u.alive) continue; var o = new Order { x = tgt.x + ((i % cols) - (cols - 1) / 2f) * 3.2f, z = tgt.z + (i / cols - (cols - 1) / 2f) * 3.2f, cx = tgt.x, cz = tgt.z, force = !attackMove, fast = kb.leftCtrlKey.isPressed };
                    u.follow = false; u.manual = true; u.secure = -1; u.leash = 25; if (queue && u.order != null) u.queue.Add(o); else { u.order = o; u.queue.Clear(); u.focus = null; }
                    i++;
                }
                PingAt(tgt.x, tgt.z, kb.leftCtrlKey.isPressed ? UI.Gold : attackMove ? new Color(1, .4f, .3f) : new Color(.4f, 1, .4f));
            }
            if (kb.hKey.wasPressedThisFrame) foreach (var u in selected) { u.secure = -1; u.order = null; u.queue.Clear(); u.focus = null; u.follow = false; u.manual = true; u.hide = null; u.home = new Vector2(u.x, u.z); u.leash = 5; }
            if (kb.aKey.wasPressedThisFrame && kb.leftCtrlKey.isPressed) { foreach (var u in units) if (u.alive && u.team == ME && u.mount == null) Select(u); }
            if (kb.cKey.wasPressedThisFrame) { if (selected.Count == 0) Msg("Select units first, then C and click an objective."); else { armSecure = true; Msg("Secure: click an objective."); } }
            if (kb.spaceKey.wasPressedThisFrame && selected.Count > 0) { float sx = 0, sz = 0; int n = 0; foreach (var u in selected) if (u.alive) { sx += u.x; sz += u.z; n++; } if (n > 0) { rtsPos.x = sx / n; rtsPos.z = sz / n; } }
            if (kb.fKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame) { PossessSomething(); return; }
        }
        void ArmSupport() { if (supportCd > 0) Msg("Support not ready (" + Mathf.CeilToInt(supportCd) + " s)."); else { armSupport = true; Msg((ME == 0 ? "Stukas" : "Artillery") + " armed — click the target."); } }
        void Select(Unit u) { selected.Add(u); if (u.ring) u.ring.SetActive(true); }
        bool GroundAt(Vector2 mp, out Vector3 p) { var ray = cam.ScreenPointToRay(mp); if (Physics.Raycast(ray, out var h, 1500, ~0, QueryTriggerInteraction.Ignore)) { p = h.point; return true; } p = default; return false; }
        bool OverHud(Vector2 mp) { float s = Screen.height / 1080f; var g = new Vector2(mp.x / s, (Screen.height - mp.y) / s); return mp.y < 150 * s || Screen.height - mp.y < 156 * s || miniRect.Contains(g); }
        List<BuyDef> BuyList() => Defs.BUY.Where(b => !(b.key == "cav" && ME == 0)).ToList();
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
                var all = SquadMembers(); foreach (var x in all) { x.secure = -1; x.follow = true; x.slot = slotCounter++ % 16; x.order = null; x.focus = null; x.hide = null; } Msg("Squad: on me (" + all.Count + ")."); return;
            }
            var sq = SquadMembers(); if (sq.Count == 0) { Msg("No squad. Press V then 1 near friendly infantry to recruit them."); return; }
            if (kind == "hold") { foreach (var x in sq) { x.secure = -1; x.follow = false; x.order = null; x.focus = null; x.home = new Vector2(x.x, x.z); } Msg("Squad: hold position."); return; }
            var (en, pt) = AimInfo();
            if (kind == "order" && en != null) { foreach (var x in sq) { x.secure = -1; x.focus = en; x.follow = false; x.order = null; x.hide = null; } Msg("Squad: engage " + Defs.NAMES[en.team][en.type] + "!"); fx.Puff(en.Chest, new Color(1, .2f, .13f, .9f), .6f, 2, .6f, Vector3.zero, true); return; }
            if (!pt.HasValue) { Msg("Aim at the ground to give a move order."); return; }
            OrderMove(sq, pt.Value.x, pt.Value.z, kind != "order", force); foreach (var x in sq) { x.follow = false; if (kind == "cover" && x.order != null) x.order.cover = true; }
            WaypointFx(pt.Value, kind == "cover" ? new Color(.4f, .67f, 1) : new Color(.4f, 1, .4f)); Msg(kind == "cover" ? "Squad: take cover there." : force ? "Squad: double time!" : "Squad: move up.");
        }
        void OrderMove(List<Unit> list, float x, float z, bool force, bool fast)
        {
            int i = 0, cols = Mathf.CeilToInt(Mathf.Sqrt(list.Count));
            foreach (var u in list) { if (!u.alive) continue; u.secure = -1; u.order = new Order { x = x + ((i % cols) - (cols - 1) / 2f) * 3.2f, z = z + (i / cols - (cols - 1) / 2f) * 3.2f, cx = x, cz = z, force = force, fast = fast }; u.queue.Clear(); u.focus = null; u.hide = null; u.leash = 25; i++; }
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
            if (pick == null) { var c = rts ? rtsPos : (lastPlayer != null ? new Vector3(lastPlayer.x, 0, lastPlayer.z) : SpawnPos(ME)); pick = units.Where(u => u.alive && u.team == ME && u.def.kind != "gun" && u.mount == null).OrderBy(u => FU.Hyp(u.x - c.x, u.z - c.z)).FirstOrDefault(); }
            if (pick == null) { Msg("No friendly units left to take control of."); return; }
            pick.manual = false; EnterFps(pick); if (tp) pick.SetRenderers(true); Msg("Now controlling: " + Defs.NAMES[pick.team][pick.type]);
        }
        /// Friendly unit under the crosshair (for SwapControl and the HUD hint).
        Unit LookFriend(float maxD = 260)
        {
            if (player == null) return null; Unit pick = null; var o = cam.transform.position; var dir = cam.transform.forward; float best = maxD;
            foreach (var e in units) { if (!e.alive || e.team != ME || e == player || e.mount != null) continue; var to = e.Chest - o; float along = Vector3.Dot(to, dir); if (along < 0 || along > best) continue; float rr = e.Foot ? 1.4f : 3f; if ((to - dir * along).magnitude < rr) { best = along; pick = e; } }
            return pick;
        }
        /// Test hooks used by TestDriver ("dbg ..." lines).
        public int coaxShots, coaxHits; public int[] spawned = new int[2];
        public string DebugCmd(string a)
        {
            var w = a.Split(' ');
            switch (w[0])
            {
                case "fog": return $"fog={RenderSettings.fog} mode={RenderSettings.fogMode} start={RenderSettings.fogStartDistance:0} end={RenderSettings.fogEndDistance:0} col={RenderSettings.fogColor} cam={cam.transform.position} far={cam.farClipPlane} clouds={vis.CloudInfo()}";
                case "fogoff": RenderSettings.fog = false; return "fog off";
                case "bloomoff": vis.BloomOff(); return "bloom off";
                case "volumes": return vis.VolumeInfo();
                case "fxoff": return vis.FxOff(w.Length > 1 ? w[1] : "");
                case "othervolumesoff": return vis.OtherVolumesOff();
                case "smokeoff": World.farSmoke.Clear(); fx.Clear(); return "far smoke off";
                case "postoff": vis.PostOff(); return "post off";
                case "cloudsoff": vis.CloudsOff(); return "clouds off";
                case "losses": { var sb = new System.Text.StringBuilder(); string[] by = { "enemy", "FRIENDLY", "none" }, wk = { "rifle", "tank", "gun", "mg/other" }; for (int tm = 0; tm < 2; tm++) { sb.Append($"T{tm} dead:"); for (int b = 0; b < 3; b++) for (int k = 0; k < 4; k++) if (killLog[tm, b, k] > 0) sb.Append($" {by[b]}/{wk[k]}={killLog[tm, b, k]}"); sb.Append(" | "); } return sb.ToString(); }
                case "inf":
                    {   // T10 diagnostics: are the soldiers' models actually there, enabled, with meshes, at finite transforms?
                        var sb = new System.Text.StringBuilder();
                        foreach (var u in units.Where(u => u.alive && u.def.kind == "inf").Take(w.Length > 1 ? int.Parse(w[1]) : 3))
                        {
                            if (!u.go) { sb.Append($"[{u.type}#{u.id} NO GO] "); continue; }
                            var rs = u.go.GetComponentsInChildren<Renderer>(true); int en = 0, vis = 0, nomesh = 0, nan = 0; Bounds b = default; bool first = true;
                            foreach (var r in rs) { if (r.enabled) en++; if (r.isVisible) vis++; var mf = r.GetComponent<MeshFilter>(); if (mf && !mf.sharedMesh) nomesh++; var q = r.transform.rotation; var pp = r.transform.position; if (float.IsNaN(q.x) || float.IsNaN(q.w) || float.IsNaN(pp.x) || float.IsNaN(pp.y)) nan++; if (r.enabled) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); } }
                            var tp0 = u.go.transform.position; var mat = rs.Length > 0 ? rs[0].sharedMaterial : null; string matName = mat ? mat.name + "/" + (mat.shader ? mat.shader.name : "noshader") : "null";
                            sb.Append($"[{u.type}#{u.id} t{u.team} active={u.go.activeInHierarchy} shown={u.shown} vis={u.visible} st={u.stance} pos=({u.x:0.0},{u.y:0.0},{u.z:0.0}) H={World.H(u.x, u.z):0.0} tpos=({tp0.x:0.0},{tp0.y:0.0},{tp0.z:0.0}) scale={u.go.transform.lossyScale} rends={rs.Length} en={en} visible={vis} nomesh={nomesh} nan={nan} bounds={b.center}/{b.size} layer={u.go.layer} mat={matName}] ");
                        }
                        bool h0 = ModelLib.Has("inf_0_rifle"), h1 = ModelLib.Has("inf_1_rifle"); var vcc = Mats.VC.GetColor("_BaseColor");
                        sb.Append($" has(inf_0_rifle)={h0} has(inf_1_rifle)={h1} cam=({cam.transform.position}) mask={cam.cullingMask:X} far={cam.farClipPlane} rts={rts} state={state} VC={Mats.VC.shader.name}/{Mats.VC.shader.isSupported} vcColor={vcc}");
                        return sb.ToString();
                    }
                case "vmdiag":
                    {   // T10 diagnostics: why would the player's hands / rifle not render in a build?
                        var sb = new System.Text.StringBuilder(); var w0 = player != null ? player.Weapon : null; int team0 = player != null ? player.team : 0;
                        string pdesc = player == null ? "null" : player.type + "#" + player.id + " alive=" + player.alive + " go=" + (player.go ? player.go.activeInHierarchy.ToString() : "null") + " sv=" + (player.sv != null);
                        string wdesc = w0 == null ? "null" : w0.id + "/" + w0.vm; string vmn = w0 != null ? "vm_" + team0 + "_" + w0.id : "-"; string vmk = "vm_" + team0 + "_" + (w0 != null ? w0.vm : "rifle");
                        sb.Append("player=" + pdesc + " weapon=" + wdesc + " has[" + vmn + "]=" + ModelLib.Has(vmn) + " has[" + vmk + "]=" + ModelLib.Has(vmk));
                        sb.Append(" vm=" + (vm ? vm.name + " active=" + vm.gameObject.activeInHierarchy + " kids=" + vm.childCount : "null") + " ");
                        if (vm) foreach (var r in vm.GetComponentsInChildren<Renderer>(true).Take(3)) sb.Append("[r " + r.name + " en=" + r.enabled + " vis=" + r.isVisible + " layer=" + r.gameObject.layer + " b=" + r.bounds.size + " mat=" + (r.sharedMaterial ? r.sharedMaterial.name + "/" + (r.sharedMaterial.shader ? r.sharedMaterial.shader.name + " ok=" + r.sharedMaterial.shader.isSupported : "noshader") : "null") + "] ");
                        sb.Append("cam mask=" + cam.cullingMask.ToString("X") + " near=" + cam.nearClipPlane + " fov=" + cam.fieldOfView + " VC=" + Mats.VC.shader.name + " ok=" + Mats.VC.shader.isSupported + " lit=" + (Shader.Find("Universal Render Pipeline/Lit") ? "found" : "MISSING"));
                        if (player != null && player.go) { int n = 0, vis = 0; foreach (var r in player.go.GetComponentsInChildren<Renderer>(true)) { n++; if (r.enabled && r.isVisible) vis++; } sb.Append(" bodyRenderers=" + n + " visible=" + vis + " shown=" + player.shown); }
                        return sb.ToString();
                    }
                case "lines": return lines.Count + " lines: " + string.Join(" | ", lines.Select(l => $"{l.name} @({l.c.x:0},{l.c.y:0}) w={l.w:0} n={l.cover.Count} held={l.heldBy}"));
                case "tab": pauseTab = int.Parse(w[1]); setTab = w.Length > 2 ? int.Parse(w[2]) : setTab; return $"pauseTab={pauseTab} setTab={setTab}";
                case "menusettings": menuSettings = menuSettings < 0 ? 0 : -1; return "menuSettings=" + menuSettings;
                case "fps": return $"fps avg={fpsAvg:0.0} min(5s)={fpsMin:0.0} units={units.Count(u => u.alive)} quality={quality} vsync={QualitySettings.vSyncCount}";
                case "quality": quality = Mathf.Clamp(int.Parse(w[1]), 0, 2); vis.SetQuality(quality); return "quality " + quality;
                case "stats":
                    {
                        var sb = new System.Text.StringBuilder();
                        for (int tm = 0; tm < 2; tm++) { var sp = SpawnPos(tm); var al = units.Where(u => u.alive && u.team == tm).ToList(); sb.Append($"T{tm}{(tm == ME ? "*" : "")} alive={al.Count} tix={tix[tm]} next={(waveNext[tm].HasValue ? waveNext[tm].Value.ToString("0") : "-")} sp=({sp.x:0},{sp.z:0}) nearSp={al.Count(u => FU.Hyp(u.x - sp.x, u.z - sp.z) < 20)} waves+={spawned[tm]} inBld={al.Count(u => u.Foot && World.InBuildingG(u.x, u.z, 0) && u.y < World.H(u.x, u.z) + .5f)} | "); }
                        sb.Append($"t={t:0} act={ActiveIdx()} dead={units.Count(u => !u.alive)} coax={coaxShots}/{coaxHits} nav={Nav.searches}/{Nav.fails} rounds={rounds.Count}"); return sb.ToString();
                    }
                case "kill": if (player != null) { Combat.Kill(this, player, null, false); return "killed player"; } return "no player";
                case "wound": if (player != null) { Combat.Bullet(this, player, 20, HitZone.Arm, units.FirstOrDefault(u => u.alive && u.team != ME)); return "arm wound, hp=" + player.hp.ToString("0") + " bleed=" + player.bleed.ToString("0.0"); } return "no player";
                case "cookoff": Combat.forceCookOff = true; return "next tank death cooks off";
                case "spawn":
                    {   // test hook: every spawn stage for both sides (simulates the positions falling, then restores them)
                        var pts = World.points; var keep = pts.Select(q => q.locked).ToArray(); var sb = new System.Text.StringBuilder();
                        for (int k = 0; k <= pts.Count; k++)
                        {
                            for (int i = 0; i < pts.Count; i++) pts[i].locked = i < k;
                            sb.Append("stage" + k + ":"); for (int tm = 0; tm < 2; tm++) { var s = SpawnPos(tm); sb.Append($" T{tm}({s.x:0},{s.z:0}) sl={World.Slope(s.x, s.z):0.00} water={World.InWater(s.x, s.z)} bld={World.InBuildingG(s.x, s.z, 2)}"); }
                            sb.Append(" | ");
                        }
                        for (int i = 0; i < pts.Count; i++) pts[i].locked = keep[i]; spStage[0] = spStage[1] = -99; return sb.ToString();
                    }
                case "topo": return Topo();
                case "gunlab": return GunLab(w.Length > 1 ? w[1] : "all", w.Length > 2 ? w[2] : "stand");
                case "hitbox":
                    {   // test hook: zone sizes on the nearest soldier, and "dbg hitbox show" draws every hitbox
                        if (w.Length > 1 && w[1] == "show") { showHit = !showHit; foreach (var hu in units) foreach (var h in hu.go ? hu.go.GetComponentsInChildren<UnitHit>(true) : new UnitHit[0]) ShowHitbox(h, showHit); return "hitboxes " + (showHit ? "shown" : "hidden"); }
                        var tgu = units.Where(x => x.alive && x != player && x.def.kind == "inf").OrderBy(x => player != null ? FU.Hyp(x.x - player.x, x.z - player.z) : 0).FirstOrDefault(); if (tgu == null || !tgu.go) return "none";
                        return string.Join("; ", tgu.go.GetComponentsInChildren<UnitHit>(true).Select(h => { var bc = h.GetComponent<BoxCollider>(); var sc = h.GetComponent<SphereCollider>(); return h.zone + (bc ? $" {bc.size.x:0.00}x{bc.size.y:0.00}x{bc.size.z:0.00}" : sc ? $" r{sc.radius:0.00}" : "") + (h.fitted ? "" : "*"); })) + "   (* = default size, not fitted to the mesh)";
                    }
                case "water":
                    {   // test hook: list inland water; "dbg water pond i" / "dbg water brook i" puts the player in it
                        var sb = new System.Text.StringBuilder($"ponds {World.ponds.Count}, brooks {World.brooks.Count}, river/sea level {(World.WL.HasValue ? World.WL.Value.ToString("0.0") : "-")} | ");
                        foreach (var p in World.ponds) sb.Append($"pond ({p.x:0},{p.z:0}) r={p.r:0} level={p.level:0.0} depth@centre={World.Depth(p.x, p.z):0.00}; ");
                        foreach (var b in World.brooks) { var m = b.p[b.p.Length / 2]; sb.Append($"brook {b.p.Length} pts, mid depth {World.Depth(m.x, m.y):0.00}, level {b.lv[0]:0.0}->{b.lv[b.lv.Length - 1]:0.0}; "); }
                        if (w.Length > 2 && player != null)
                        {
                            int i = int.Parse(w[2]); Vector2 q = w[1] == "brook" && World.brooks.Count > 0 ? World.brooks[i % World.brooks.Count].p[World.brooks[i % World.brooks.Count].p.Length / 2] : World.ponds.Count > 0 ? new Vector2(World.ponds[i % World.ponds.Count].x, World.ponds[i % World.ponds.Count].z) : new Vector2(player.x, player.z);
                            if (w[1] == "river" && M.hasRiver) q = new Vector2(M.bridges[0] + 25, M.RiverC(M.bridges[0] + 25));
                            player.x = q.x; player.z = q.y + 6; player.y = World.H(player.x, player.z); player.stance = 2; yaw = Mathf.PI; pitch = -.1f;
                            sb.Append($" player in water: depth {World.Depth(player.x, player.z):0.00}");
                        }
                        return sb.ToString();
                    }
                case "house":
                    {   // test hook: stand 10 m off the i-th building (or a building of kind k), facing it
                        if (player == null || Interiors.bds.Count == 0) return "no player / buildings"; var bl = Interiors.bds.Where(b => w.Length < 3 || b.k == w[2]).ToList(); if (bl.Count == 0) return "none of that kind";
                        var b = bl[int.Parse(w[1]) % bl.Count]; float cx = (b.x0 + b.x1) / 2, cz = (b.z0 + b.z1) / 2, d = Mathf.Max(b.x1 - b.x0, b.z1 - b.z0) * .5f + 10;
                        float hang = (int.Parse(w[1]) * 1.9f) % 6.283f; player.x = cx - Mathf.Sin(hang) * d; player.z = cz - Mathf.Cos(hang) * d; player.y = World.H(player.x, player.z); yaw = hang; pitch = .08f;
                        return $"{b.k} at ({cx:0},{cz:0}) {b.x1 - b.x0:0.0}x{b.z1 - b.z0:0.0} floors={b.floors} gable={b.gable} baseY={b.baseY:0.0} ground={World.H(cx, cz):0.0}";
                    }
                case "secure":
                    {   // test hook: report every objective group (team, point, phase, size, strengths)
                        var sb = new System.Text.StringBuilder();
                        foreach (var g in units.Where(u => u.alive && u.secure >= 0).GroupBy(u => u.team * 100 + u.secure))
                        { var p = World.points[g.Key % 100]; secureState.TryGetValue(g.Key, out var st2); sb.Append($"T{g.Key / 100}@{p.n}:{(st2 == null ? "-" : new[] { "assemble", "assault", "hold", "regroup" }[st2.phase])} n={g.Count()} own={Strength(g.Key / 100, p.x, p.z, p.r + 30):0.0} enemy={Strength(1 - g.Key / 100, p.x, p.z, p.r + 35):0.0} | "); }
                        return sb.Length == 0 ? "no groups" : sb.ToString();
                    }
                case "vm":
                    {   // test hook: dbg vm <fovAds> <handAds> <backAds>
                        var ci = System.Globalization.CultureInfo.InvariantCulture;
                        if (w.Length > 1) SoldierVisual.vmFovAds = float.Parse(w[1], ci); if (w.Length > 2) SoldierVisual.vmHandAds = float.Parse(w[2], ci); if (w.Length > 3) SoldierVisual.vmBackAds = float.Parse(w[3], ci);
                        return $"vm fovAds={SoldierVisual.vmFovAds} handAds={SoldierVisual.vmHandAds} backAds={SoldierVisual.vmBackAds}";
                    }
                case "fence":
                    {   // test hook: put the player 10 m from the nearest unbroken fence, facing it
                        int alive = World.breakables.Count(k => !k.dead); if (w.Length > 1 && w[1] == "count") return "fence/hedge chunks standing " + alive + "/" + World.breakables.Count;
                        if (player == null) return "no player"; World.Breakable best = null; float bd = 1e9f;
                        foreach (var k in World.breakables) { if (k.dead || k.kind != "fence") continue; float d = FU.Hyp((k.x0 + k.x1) / 2 - player.x, (k.z0 + k.z1) / 2 - player.z); if (d < bd) { bd = d; best = k; } }
                        if (best == null) return "no fence";
                        float bx = (best.x0 + best.x1) / 2, bz = (best.z0 + best.z1) / 2, dx = bx - player.x, dz = bz - player.z, l = Mathf.Max(.01f, FU.Hyp(dx, dz)); dx /= l; dz /= l;
                        player.x = bx - dx * 10; player.z = bz - dz * 10; player.y = World.H(player.x, player.z); yaw = Mathf.Atan2(dx, dz); player.heading = player.turretH = yaw; pitch = -.12f;
                        return $"facing fence at ({bx:0},{bz:0}); standing {alive}/{World.breakables.Count}";
                    }
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
                        var (dist, hu, hd) = CastRay(o, d, 240, player != null ? player.team : ME, player);
                        return $"cam=({o.x:0.0},{o.y:0.0},{o.z:0.0}) cast={dist:0.0} unit={(hu != null ? hu.type : "-")} hits: " + string.Join("; ", hs.Take(6).Select(h => $"{h.collider.name}{(h.collider.isTrigger ? "[T]" : "")}@{h.distance:0.0}"));
                    }
                case "aim":
                    {
                        if (player == null) return "no"; var o = cam.transform.position; Unit best = null; float bd = 1e9f;
                        foreach (var e in units) { if (!e.alive || e.team == ME) continue; float d = (e.Chest - o).magnitude; if (d < bd && d < 200 && World.LOS(o, e.Chest)) { bd = d; best = e; } }
                        if (best == null) return "no target in LOS"; var v = best.Chest - o; yaw = Mathf.Atan2(v.x, v.z); pitch = Mathf.Atan2(v.y, FU.Hyp(v.x, v.z)); return $"aim {best.type} d={bd:0}";
                    }
                case "god": if (player != null) { player.max = player.hp = 5000; } return "god";
                case "tp": if (player == null) return "no"; tp = w.Length < 2 || w[1] != "0"; player.SetRenderers(tp); if (vm) vm.gameObject.SetActive(!tp); return "tp " + tp;
                case "orbit":
                    if (w.Length < 2 || w[1] == "off") { debugOrbit = null; return "orbit off"; }
                    debugOrbit = float.Parse(w[1], System.Globalization.CultureInfo.InvariantCulture); debugOrbitD = w.Length > 2 ? float.Parse(w[2], System.Globalization.CultureInfo.InvariantCulture) : 2.2f; return "orbit " + debugOrbit;
                case "ads": debugAds = w.Length > 1 ? float.Parse(w[1], System.Globalization.CultureInfo.InvariantCulture) : -1; return "ads " + debugAds;
                case "wep":
                    {   // test hook: switch the player to weapon id w[1] (works without keyboard focus)
                        if (player == null || player.weps == null || w.Length < 2) return "no"; int i = System.Array.IndexOf(player.weps, w[1]); if (i < 0) return "not carried: " + string.Join(",", player.weps);
                        player.SwitchWeapon(i); BuildViewModel(); return "weapon " + w[1];
                    }
                case "fire": if (player != null && player.Weapon != null && player.clip > 0) { PlayerFire(player, player.Weapon, player.stance); return "fired, clip " + player.clip; } return "no";
                case "reload":
                    {   // test hook: start a reload as [R] would (w[1] = "empty" first empties the magazine)
                        var pw = player != null ? player.Weapon : null; if (pw == null || pw.clip <= 0) return "no";
                        if (w.Length > 1 && w[1] == "empty") player.clip = 0; else if (player.clip >= pw.clip) player.clip = pw.clip - 1;
                        bool bg = pw.vm == "rifle" || pw.vm == "sniper"; player.reloadStop = false;
                        if (bg && (pw.scope || player.clip > 0)) { player.rkind = ReloadKind.Single; player.singlesActive = true; player.reload = player.reloadDur = .62f; }
                        else { player.rkind = bg ? ReloadKind.Stripper : ReloadKind.Mag; player.reload = player.reloadDur = pw.reload; }
                        return "reload " + player.rkind;
                    }
                case "heat": if (player != null) { player.heat = w.Length > 1 ? float.Parse(w[1], System.Globalization.CultureInfo.InvariantCulture) : .97f; return "heat " + player.heat + " overheated=" + player.overheated + " barrelT=" + player.barrelT; } return "no player";
            }
            return "?";
        }
        void SwapControl()
        {
            if (player == null) return; var pick = LookFriend();
            if (pick == null) { float bd = 20; foreach (var e in units) { if (!e.alive || e.team != ME || e == player || e.mount != null) continue; float d = FU.Hyp(e.x - player.x, e.z - player.z); if (d < bd) { bd = d; pick = e; } } }
            if (pick == null) { Msg("Look at a friendly unit (or stand next to one) and press F to take control."); return; }
            var prev = player; prev.Save(); prev.stance = 0; if (prev.def.kind != "tank") prev.heading = yaw; prev.turretH = yaw; ReleasePlayer(); prev.manual = false; prev.SetRenderers(true); if (prev.Foot) { prev.squad = true; prev.follow = true; prev.slot = slotCounter++ % 16; }
            EnterFps(pick); if (tp) pick.SetRenderers(true); Msg("Now controlling: " + Defs.NAMES[pick.team][pick.type]);
        }
        /// E (T27): leave the vehicle you are in as one of its crew, or climb into the friendly tank / gun beside you.
        bool ToggleVehicle()
        {
            var u = player; if (u == null || !u.alive) return false;
            if (u.def.kind == "tank" || u.def.kind == "gun")
            {
                Unit c = u.playerCrew != null && u.playerCrew.alive && u.playerCrew.mount == u ? u.playerCrew : null;
                if (c == null && u.crew != null) foreach (var k in u.crew) if (k.alive && k.mount == u) { c = k; break; }
                if (c == null) { c = SpawnUnit(u.team, Crew.CrewType(u), u.x, u.z, u.heading); Crew.Mount(this, c, u); }   // the last man: you
                u.Save(); u.stance = 0; ReleasePlayer(); u.manual = true; u.playerCrew = null; u.order = null;
                Crew.Dismount(this, c, bail: false);
                EnterFps(c); yaw = u.heading; Msg("Out of the " + Defs.NAMES[u.team][u.type] + " — E to climb back in.");
                return true;
            }
            if (!u.Foot) return false;
            Unit v = null; float bd = 4.5f;
            foreach (var e in units) { if (!e.alive || e.team != ME || (e.def.kind != "tank" && e.def.kind != "gun")) continue; float d = FU.Hyp(e.x - u.x, e.z - u.z) - e.def.r; if (d < bd) { bd = d; v = e; } }
            if (v == null) return false;
            if (Crew.Alive(v) >= Crew.CrewSize(v)) { Unit spare = null; foreach (var k in v.crew) if (k.alive && k.mount == v && (spare == null || k.seat > spare.seat)) spare = k; if (spare != null) Crew.Dismount(this, spare, bail: false); }   // full: one of the men makes room
            u.Save(); ReleasePlayer(); Crew.Mount(this, u, v); v.playerCrew = u;
            if (v.weps == null) v.InitWeapons(Defs.Loadout(v.team, v.type));
            EnterFps(v); if (tp) v.SetRenderers(true); Msg("In the " + Defs.NAMES[v.team][v.type] + " — E to get out.");
            return true;
        }
        /// The vehicle you were in is lost but one of the crew lives: you are him now, outside, with a pistol.
        public void PlayerBail(Unit v, Unit c)
        {
            ReleasePlayer(); if (vm) vm.gameObject.SetActive(false); if (tankVm) tankVm.gameObject.SetActive(false);
            EnterFps(c); c.rout = 0; c.supp = .5f; c.mor = 40; yaw = c.heading; Msg("Bailed out! The " + Defs.NAMES[v.team][v.type] + " is gone — you have your sidearm.");
        }
        /// Tank coaxial machine gun (RMB while driving) — port of playerFire(u, true).
        void CoaxFire(Unit u)
        {
            u.cd2 = .1f; float spread = .006f * (u.moving ? 1.8f : 1); var dir = (cam.transform.rotation * new Vector3(FU.Rand(-spread, spread), FU.Rand(-spread, spread), 1)).normalized; var o = cam.transform.position;
            if (tp) { var (d0, _, _) = CastRay(o, dir, 240, u.team, u); var A = o + dir * d0; dir = (A - tpPivot).normalized; o = tpPivot + dir * 2; }
            var (dist, hitU, head) = CastRay(o, dir, 800, u.team, u); var end = o + dir * dist;
            // muzzle like the original: just ahead of the gunner's sight, slightly right and low (it used to start inside the
            // hull 0.7 m under the camera, so every tracer looked like a vertical streak and the gun seemed not to fire forward)
            var cf = cam.transform; var muzzle = tp ? tpPivot + dir * 1.3f + cf.right * .3f - Vector3.up * .15f : tankCoaxTip && tankVm.gameObject.activeSelf ? tankCoaxTip.position : cf.position + cf.forward * 1.4f + cf.right * .22f - Vector3.up * .2f;
            coaxShots++; if (hitU != null) coaxHits++;   // (aim-line statistics for dbg stats; the round itself is a projectile)
            fx.MuzzleFlash(muzzle, end - muzzle, .22f); audio.Shot(u.team == 0 ? "mg42" : "browning", muzzle, 1, true); u.heat = Mathf.Min(1, u.heat + .02f);
            dir = Elevate(dir, Elevation(755, Drag(null, false), PlayerZero(null, true)));   // the coax is boresighted with the main gun: it shares its range setting
            FireRound(u, o, muzzle, dir, 755, Defs.WEAPONS[u.team == 0 ? "mg34" : "ckm30"], false, ++u.tracerN % 5 == 0);   // the coax is the same gun as the infantry MG: same round, same falloff
        }
        /// Port of respawnPoint: base, or (X on the deploy screen) the safest friendly infantry near the front.
        Vector3 RespawnPoint()
        {
            var b = SpawnPos(ME); if (!respSquad || World.points.Count == 0) return b; int a = ActiveIdx(); var tp0 = World.points[Mathf.Min(a, World.points.Count - 1)]; Unit best = null; float bd = 1e9f;
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
            for (int i = 0; i < 3; i++) { var m = ModelLib.Spawn("truck_" + att, World.root).transform; Crew.AddDriver(m, att); m.gameObject.SetActive(false); convoy.Add(new Convoy { m = m, s = -i * 24 - FU.Rand(0, 40), A = A, dir = (B - A) / L, L = L }); }
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
                if (vis)
                {   // Opel Blitz 3.6 (1939): engine starts once the lorry is in sight; it labours when driving and drops to idle when parked
                    if (!c.eng) c.eng = audio.Follow("truck", c.m, .6f, true, 6, 500, 0);
                    else if (!c.eng.isPlaying) c.eng.Play();
                    if (c.eng) { bool drive = c.wait <= 0 && c.s < stop; c.eng.pitch = Mathf.Lerp(c.eng.pitch, drive ? 1.12f : .86f, dt * 2); c.eng.volume = Mathf.Lerp(c.eng.volume, drive ? .6f : .32f, dt * 2); }
                }
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
            // Everything here draws with explicit Rects (no GUILayout), so the Layout pass has nothing to do: skipping it halves the GUI cost per frame.
            if (Event.current.type == EventType.Layout) return; bool rp = Event.current.type == EventType.Repaint;
            UI.Ensure(); GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(S, S, 1)); float W = VW, H = VH;
            if (state == GState.Menu) { DrawMenu(W, H); return; }
            if (rp) DrawIcons();
            DrawTop(W);
            float my = 96;   // always under the header / objective strip (messages used to climb to y 42 once the header faded and run under the strip)
            for (int i = Mathf.Max(0, msgs.Count - 3); i < msgs.Count; i++) { var (s, tt) = msgs[i]; float age = Time.unscaledTime - tt; if (age > 5) continue; float mh = Mathf.Max(20, UI.TextH(s, UI.small, 600)); UI.Shadow(new Rect(16, my, 600, mh), s, UI.small, UI.Text, Mathf.Clamp01((6 - age) / 1.2f)); my += mh; }
            if (dmgT > 0) UI.Fill(new Rect(0, 0, W, H), new Color(.6f, 0, 0, dmgT * .45f));
            if (rp && state == GState.Playing && !rts && player != null && player.alive) { DrawCompass(W); DrawDamageDir(W, H); if (!hudMin) { DrawStatusLine(W); DrawSquadStrip(H); } }
            DrawBanner(W);
            if (range != null && (state == GState.Playing || state == GState.Dead)) range.Draw(W, H);
            if ((state == GState.Playing || state == GState.Dead) && Keyboard.current != null && Keyboard.current.lKey.isPressed) { DrawScoreboard(W, H); return; }
            if (state == GState.End) { DrawEnd(W, H); return; }
            if (state == GState.Paused) { DrawPause(W, H); return; }
            DrawMinimap(new Rect(14, H - (rts ? 214 : 176), rts ? 200 : 162, rts ? 200 : 162));
            if (state == GState.Deploy) DrawDeploy(W, H);
            else if (rts) DrawRts(W, H);
            else if (state == GState.Playing && player != null) DrawFps(W, H);
            if (state == GState.Dead && !rts) DrawDead(W, H);
            else if (state == GState.Dead && rts) UI.Shadow(new Rect(0, H - 170, W, 26), "Tab: return to the redeploy screen · F: take control of the selected (or nearest) unit", UI.center, UI.Gold);
        }

        void DrawIcons()
        {   // unit icon sprites (port of iconMat markers) — all units on the map; friendlies at range in first person
            foreach (var u in units)
            {
                if (!u.alive || u == player || !u.visible) continue; bool friend = u.team == ME; if (!rts && (!friend || FU.Hyp(u.x - cam.transform.position.x, u.z - cam.transform.position.z) < 12)) continue;
                var sp = cam.WorldToScreenPoint(new Vector3(u.x, u.y + u.def.bh + .9f, u.z)); if (sp.z < 0 || sp.z > (rts ? 700 : 45)) continue;
                var g = ToGui(sp); float sz = rts ? 26 : 18; var r = new Rect(g.x - sz / 2, g.y - sz / 2, sz, sz);
                UI.Tex(r, Visuals.Icon(friend, u.type), new Color(1, 1, 1, rts ? .92f : .4f * Mathf.Clamp01((45 - sp.z) / 15f)));
                if (rts && friend) { UI.Fill(new Rect(r.x, r.yMax + 1, sz, 3), new Color(0, 0, 0, .7f)); UI.Fill(new Rect(r.x, r.yMax + 1, sz * u.hp / u.max, 3), selected.Contains(u) ? UI.Green : new Color(.4f, .75f, 1)); if (u.vet > 0) UI.Shadow(new Rect(r.xMax, r.y - 4, 30, 16), new string('★', u.vet), UI.small, UI.Gold); }
            }
            foreach (var k in pickups) { var sp = cam.WorldToScreenPoint(new Vector3(k.x, World.H(k.x, k.z) + 1.4f, k.z)); if (sp.z < 0 || sp.z > 60) continue; var g = ToGui(sp); UI.Shadow(new Rect(g.x - 40, g.y - 10, 80, 20), k.med ? "✚ MEDKIT" : "▣ AMMO", UI.center, k.med ? FU.Hex(0xff7a7a) : UI.Green, .9f); }
        }

        void DrawTop(float W)
        {
            UI.Tex(new Rect(0, 0, W, 90), UI.gradT, Color.white);
            float hdr = range != null ? 1 : Mathf.Clamp01((14 - t) / 2);   // RO2: the mission title is shown at the start, then gets out of the way
            if (hdr > 0) UI.Shadow(new Rect(18, 8, 520, 30), M.name.ToUpper(), UI.h2, UI.Text, hdr);
            if (range != null) { UI.Shadow(new Rect(18, 40, 600, 22), M.date + "  ·  unlimited ammo, no clock", UI.line, UI.Dim); return; }   // nothing to attack or defend on the range
            float left = Mathf.Max(0, M.time - t); string tm = $"{(int)(left / 60)}:{(int)(left % 60):00}"; int att = M.attacker;
            if (hdr > 0) UI.Shadow(new Rect(18, 40, 600, 22), M.date + "  ·  " + (ME == att ? "ATTACKING" : "DEFENDING") + " as " + Defs.SIDE[ME], UI.line, UI.Dim, hdr);
            UI.Shadow(new Rect(18, Mathf.Lerp(14, 60, hdr), 600, 22), $"<color=#6bd36b>▲ {tix[ME]}</color>" + (tixRate < -.5f ? $" <color=#e05a4a>{tixRate:0}/min</color>" : "") + $"    <color=#e05a4a>▼ {tix[EN]}</color>", UI.line, UI.Text);
            UI.Shadow(new Rect(W - 330, 8, 312, 34), "⏱ " + tm, UI.h2R, left < 120 ? UI.Red : UI.Text);
            UI.Shadow(new Rect(W - 330, 42, 312, 26), $"CP {Mathf.FloorToInt(cp)}", UI.h2R20, UI.Gold);
            // objective strip: boxes shrink to fit between the mission header and the clock, names are shortened with "…" instead of being cut on both sides
            int n = World.points.Count, act = ActiveIdx(); float gap = 8, bw = Mathf.Clamp((W - 2 * 400 - gap * (n - 1)) / Mathf.Max(1, n), 100, 176), x0 = W / 2 - (n * bw + (n - 1) * gap) / 2;
            var nameSt = UI.lineC; var subSt = UI.lineC;
            for (int i = 0; i < n; i++)
            {
                var p = World.points[i]; var r = new Rect(x0 + i * (bw + gap), 12, bw, 46);
                Color border = p.locked ? (p.owner == ME ? UI.Green : UI.Red) : i == act ? UI.Gold : new Color(.55f, .55f, .55f, .6f);
                UI.Fill(r, new Color(0, 0, 0, .45f));
                if (p.locked) UI.Fill(r, new Color(border.r, border.g, border.b, .28f)); else if (i == act) UI.Fill(new Rect(r.x, r.y, r.width * p.ap, r.height), new Color(UI.Gold.r, UI.Gold.g, UI.Gold.b, .3f));
                UI.Frame(r, border, i == act ? 2 : 1); if (i == act) UI.Frame(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(UI.Gold.r, UI.Gold.g, UI.Gold.b, .25f + .2f * Mathf.Sin(Time.unscaledTime * 4)));
                UI.Shadow(new Rect(r.x + 6, r.y + 3, r.width - 12, 20), UI.Fit((i + 1) + ". " + p.n, nameSt, r.width - 12), nameSt, p.locked || i == act ? UI.Text : UI.Dim);
                string sub = i == act ? $"⚔ {p.g} vs {p.e}" + (p.pushT > 0 ? "  PUSH" : "") : p.locked ? (p.owner == ME ? "held" : "lost") : "";
                if (sub != "") UI.Shadow(new Rect(r.x + 6, r.y + 23, r.width - 12, 20), sub, subSt, i == act ? UI.Gold : p.owner == ME ? UI.Green : UI.Red);
            }
        }

        /// One line under the compass with what an RO2/SD2 player keeps glancing at: next reinforcement wave, ticket trend, support readiness.
        void DrawStatusLine(float W)
        {
            if (range != null || M == null) return;
            var parts = new List<string>();
            float wv = waveNext[ME].HasValue ? waveNext[ME].Value - t : -1; if (wv > 0) parts.Add($"Reinforcements {(int)(wv / 60)}:{(int)(wv % 60):00}");
            string sup = ME == 0 ? "Stukas" : "Artillery"; parts.Add(supportCd > 0 ? $"{sup} {Mathf.CeilToInt(supportCd)} s" : $"<color=#ffd96a>{sup} ready [B]</color>");
            UI.Shadow(new Rect(W / 2 - 320, 96, 640, 20), string.Join("   ·   ", parts), UI.smallC, new Color(1, 1, 1, .62f));
        }
        /// RO2 squad list: who is still with you, how hurt, pinned or bleeding, and how far away. Left edge, above the minimap.
        void DrawSquadStrip(float H)
        {
            if (player == null) return;
            var sq = units.Where(o => o.alive && o.team == ME && o.squad && o != player).OrderBy(o => o.slot).Take(8).ToList();
            if (sq.Count == 0) return; float x = 14, y = H - 176 - 14 - sq.Count * 22;
            UI.Fill(new Rect(x - 3, y - 24, 178, sq.Count * 22 + 28), new Color(0, 0, 0, .35f));
            UI.Shadow(new Rect(x, y - 22, 170, 18), $"SQUAD  {sq.Count}", UI.small, UI.Dim);
            foreach (var o in sq)
            {
                string g = o.type == "mg" ? "MG" : o.type == "at" ? "AT" : o.type == "sniper" ? "SN" : o.officer ? "LD" : "R"; float d = FU.Hyp(o.x - player.x, o.z - player.z);
                Color bar = o.bleed > 0 ? new Color(.95f, .3f, .25f) : o.hp < o.max * .5f ? FU.Hex(0xe8c547) : UI.Green;
                UI.Shadow(new Rect(x, y, 26, 20), g, UI.small, o.Pinned ? FU.Hex(0xf29a3a) : UI.Text);
                UI.Fill(new Rect(x + 28, y + 7, 66, 6), new Color(0, 0, 0, .6f)); UI.Fill(new Rect(x + 28, y + 7, 66 * Mathf.Clamp01(o.hp / o.max), 6), bar);
                string note = o.bleed > 0 ? "bleeding" : o.Pinned ? "pinned" : o.bandaging > 0 ? "bandaging" : !o.follow ? "holding" : d > 45 ? "far" : "";
                UI.Shadow(new Rect(x + 100, y, 74, 20), note != "" ? note : Mathf.RoundToInt(d) + " m", UI.small, o.bleed > 0 ? new Color(.95f, .3f, .25f) : UI.Dim);
                y += 22;
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
                    if (ev.button == 0) { rtsPos.x = wx; rtsPos.z = wz; } else if (ev.button == 1 && selected.Count > 0) { OrderMove(selected.Where(u => u.alive).ToList(), wx, wz, !Keyboard.current.leftAltKey.isPressed, false); foreach (var u in selected) { u.manual = true; u.follow = false; } }
                    ev.Use();
                }
            }
            UI.Fill(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(0, 0, 0, .55f)); GUI.DrawTexture(r, minimap); UI.Frame(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), UI.Olive);
            float Hm = M.half; Vector2 F(float x, float z) => new Vector2(r.x + (x / Hm / 2 + .5f) * r.width, r.y + (1 - (z / Hm / 2 + .5f)) * r.height);
            int act = ActiveIdx();
            if (state == GState.Deploy) { var a = F(dz.xMin, dz.yMax); var b = F(dz.xMax, dz.yMin); var zr = Rect.MinMaxRect(a.x, a.y, b.x, b.y); UI.Fill(zr, new Color(.3f, 1, .4f, .18f)); UI.Frame(zr, UI.Green); foreach (var c in cards) if (c.placed) { var q = F(c.pos.x, c.pos.y); UI.Fill(new Rect(q.x - 2, q.y - 2, 4, 4), UI.Gold); } }
            foreach (var ln in lines) { var sd = new Vector2(-ln.axis.y, ln.axis.x) * ln.w / 2; var a = F(ln.c.x - sd.x, ln.c.y - sd.y); var b = F(ln.c.x + sd.x, ln.c.y + sd.y); Color lc = ln.heldBy < 0 ? new Color(1, 1, 1, .35f) : ln.heldBy == ME ? UI.Green : UI.Red; HudLine(a, b, 1.5f, lc); }   // frontlines: grey untaken, green/red held
            for (int i = 0; i < World.points.Count; i++) { var p = World.points[i]; var q = F(p.x, p.z); Color c = p.locked ? (p.owner == ME ? UI.Green : UI.Red) : i == act ? UI.Gold : new Color(.85f, .85f, .85f); float zs = Mathf.Max(6, p.r / Hm / 2 * r.width); UI.Frame(new Rect(q.x - zs, q.y - zs, zs * 2, zs * 2), c, 2); }
            foreach (var u in units) { if (!u.alive || (u.team != ME && !u.visible)) continue; var q = F(u.x, u.z); float k = u.def.kind == "tank" ? 4 : 2.5f; UI.Fill(new Rect(q.x - k / 2, q.y - k / 2, k, k), u.team == ME ? (selected.Contains(u) ? Color.white : UI.Green) : UI.Red); }
            if (player != null && player.alive) { var q = F(player.x, player.z); UI.Fill(new Rect(q.x - 3, q.y - 3, 6, 6), Color.yellow); var d = new Vector2(Mathf.Sin(yaw), -Mathf.Cos(yaw)) * 9; UI.Fill(new Rect(q.x + d.x - 1.5f, q.y + d.y - 1.5f, 3, 3), Color.yellow); }
            else if (rts) { var q = F(rtsPos.x, rtsPos.z); UI.Frame(new Rect(q.x - 10, q.y - 8, 20, 16), Color.yellow); }
        }

        // ---------------- RO2-style "HUD on demand": widgets fade in when their value changes, then fade out ----------------
        float hintUntil, stanceShowT = -99, bodyShowT = -99, weapShowT = -99, hudHp; int hudStance = -1; string hudWep; bool hitMarkers = true;
        static float OnDemand(float since, float hold) => Mathf.Clamp01(1 - (Time.unscaledTime - since - hold) / .8f);
        static Color A(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);
        /// Body silhouette: each hit zone turns red while it bleeds, amber once bandaged (RO2 ROHUDWidgetStatus).
        void DrawBody(Rect r, Unit u, float a)
        {
            float k = r.width / 50f, pulse = .65f + .35f * Mathf.Sin(Time.unscaledTime * 7), hpf = u.hp / u.max;
            Color baseC = Color.Lerp(new Color(.86f, .4f, .32f), new Color(.84f, .86f, .74f), Mathf.Clamp01(hpf * 1.4f - .1f));
            Color Z(HitZone z) { float w = u.wounds[(int)z]; return w >= 1 && u.bleed > 0 ? new Color(.95f, .2f, .15f, pulse) : w > 0 ? new Color(.96f, .62f, .2f) : baseC; }
            void P(float x, float y, float w, float h, Color c) { var q = new Rect(r.x + x * k, r.y + y * k, w * k, h * k); UI.Fill(new Rect(q.x - 1, q.y - 1, q.width + 2, q.height + 2), new Color(0, 0, 0, .45f * a)); UI.Fill(q, A(c, a)); }
            P(19, 0, 12, 13, Z(HitZone.Head)); P(13, 15, 24, 22, Z(HitZone.Chest)); P(14, 38, 22, 15, Z(HitZone.Stomach));
            P(4, 16, 8, 36, Z(HitZone.Arm)); P(38, 16, 8, 36, Z(HitZone.Arm)); P(14, 55, 10, 55, Z(HitZone.Leg)); P(26, 55, 10, 55, Z(HitZone.Leg));
        }
        /// Stance figure: standing, crouched or prone.
        void DrawStance(Rect r, int st, float a)
        {
            Color c = A(new Color(.84f, .86f, .74f), a); void P(float x, float y, float w, float h) => UI.Fill(new Rect(r.x + x, r.y + y, w, h), c);
            if (st == 0) { P(11, 0, 8, 8); P(10, 10, 10, 18); P(10, 29, 4, 21); P(16, 29, 4, 21); }
            else if (st == 1) { P(13, 14, 8, 8); P(11, 23, 10, 14); P(7, 37, 14, 4); P(7, 37, 4, 13); P(17, 41, 4, 9); }
            else { P(0, 40, 8, 8); P(9, 42, 24, 6); P(33, 43, 17, 4); }
        }
        void DrawCook(float W, float H)
        {
            if (cookT < 0 || player == null) return; float left = Gren(player.team).fuse - cookT;
            UI.Shadow(new Rect(W / 2 - 150, H / 2 + 92, 300, 22), $"{Gren(player.team).name} — {left:0.0} s", UI.lineC, left < 1.5f ? UI.Red : UI.Gold, 1);
        }
        void DrawHitMarker(float W, float H)
        {   // not in RO2 (it has none, by design) — optional, toggled on the pause screen
            if (!hitMarkers || hitT <= 0) return;
            float a = Mathf.Clamp01(hitT / .35f), gap = 7 + (1 - a) * 4, L = hitKill ? 12 : 8;
            Color c = hitKill ? new Color(1, .28f, .22f, a) : hitHead ? new Color(1, .85f, .35f, a) : new Color(1, 1, 1, a * .9f);
            var m = GUI.matrix; var cp = new Vector2(W / 2, H / 2); RotHud(45, cp);
            UI.Fill(new Rect(cp.x - gap - L, cp.y - 1, L, 2), c); UI.Fill(new Rect(cp.x + gap, cp.y - 1, L, 2), c); UI.Fill(new Rect(cp.x - 1, cp.y - gap - L, 2, L), c); UI.Fill(new Rect(cp.x - 1, cp.y + gap, 2, L), c);
            GUI.matrix = m;
        }

        static void HudLine(Vector2 a, Vector2 b, float wd, Color c)
        {
            var m = GUI.matrix; var d = b - a; RotHud(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a); UI.Fill(new Rect(a.x, a.y - wd / 2, d.magnitude, wd), c); GUI.matrix = m;
        }
        /// Gunner's telescope: black eyepiece surround, a soft edge, and the reticle of the vehicle's sight.
        /// Panzer II, TZF 4: the large aiming triangle with smaller triangles either side for leads. 7TP: a chevron on a stadia line.
        void DrawTankSight(float W, float H, int team)
        {
            float r = H * .44f, cx = W / 2, cy = H / 2; var blk = Color.black;
            for (float y = 0; y < H; y += 3)
            {
                float dy = y + 1.5f - cy, hw = Mathf.Abs(dy) < r ? Mathf.Sqrt(r * r - dy * dy) : 0;
                UI.Fill(new Rect(0, y, cx - hw + 1, 3), blk); UI.Fill(new Rect(cx + hw - 1, y, W - cx - hw + 1, 3), blk);
            }
            for (int k = 0; k < 6; k++) { float rr = r - k * 2.5f; for (int i = 0; i < 64; i++) { float a0 = i * Mathf.PI / 32, a1 = (i + 1) * Mathf.PI / 32; HudLine(new Vector2(cx + Mathf.Cos(a0) * rr, cy + Mathf.Sin(a0) * rr), new Vector2(cx + Mathf.Cos(a1) * rr, cy + Mathf.Sin(a1) * rr), 3, new Color(0, 0, 0, .28f)); } }
            var ink = new Color(.05f, .05f, .05f, .92f); float s = r / 22;
            if (team == 0)
            {
                void Tri(float x, float h) { HudLine(new Vector2(x, cy), new Vector2(x - h * .6f, cy + h), 2, ink); HudLine(new Vector2(x, cy), new Vector2(x + h * .6f, cy + h), 2, ink); HudLine(new Vector2(x - h * .6f, cy + h), new Vector2(x + h * .6f, cy + h), 2, ink); }
                Tri(cx, s * 2.2f); for (int i = 1; i <= 3; i++) { Tri(cx - i * s * 3.2f, s * 1.1f); Tri(cx + i * s * 3.2f, s * 1.1f); }
                for (int i = 0; i <= 8; i++) { float y = cy - r * .55f + i * r * .09f; HudLine(new Vector2(cx - r * .62f, y), new Vector2(cx - r * .62f + (i % 2 == 0 ? s * 1.4f : s * .8f), y), 2, ink); }   // range scale (left)
            }
            else
            {
                HudLine(new Vector2(cx - s * 1.6f, cy + s * 1.8f), new Vector2(cx, cy), 2, ink); HudLine(new Vector2(cx, cy), new Vector2(cx + s * 1.6f, cy + s * 1.8f), 2, ink);
                HudLine(new Vector2(cx - r * .8f, cy + s * 4), new Vector2(cx - s * 2.5f, cy + s * 4), 2, ink); HudLine(new Vector2(cx + s * 2.5f, cy + s * 4), new Vector2(cx + r * .8f, cy + s * 4), 2, ink);
                for (int i = 1; i <= 5; i++) { HudLine(new Vector2(cx - s * 2.5f - i * s * 3, cy + s * 4), new Vector2(cx - s * 2.5f - i * s * 3, cy + s * 3), 2, ink); HudLine(new Vector2(cx + s * 2.5f + i * s * 3, cy + s * 4), new Vector2(cx + s * 2.5f + i * s * 3, cy + s * 3), 2, ink); }
                HudLine(new Vector2(cx, cy + s * 5), new Vector2(cx, cy + r * .8f), 2, ink);
            }
        }

        void DrawFps(float W, float H)
        {
            var u = player; var w = u.Weapon; bool scoped = w != null && w.scope && ads > .85f; float now = Time.unscaledTime;
            if (scoped)
            {
                float r = H * .46f; UI.Fill(new Rect(0, 0, W / 2 - r, H), Color.black); UI.Fill(new Rect(W / 2 + r, 0, W / 2 - r, H), Color.black);
                UI.Fill(new Rect(W / 2 - r, 0, 2 * r, H / 2 - r), Color.black); UI.Fill(new Rect(W / 2 - r, H / 2 + r, 2 * r, H / 2 - r), Color.black);
                UI.Fill(new Rect(W / 2 - r, H / 2 - 1, 2 * r, 2), Color.black); UI.Fill(new Rect(W / 2 - 1, H / 2, 2, r), Color.black); UI.Fill(new Rect(W / 2 - 1.5f, H / 2 - 30, 3, 30), Color.black);
            }
            else if (u.def.kind == "tank" && !tp && ads > .85f) DrawTankSight(W, H, u.team);
            else if (hipDot && (ads < .3f || w == null || w.melee || u.def.kind == "tank" || u.def.kind == "cav"))
            {   // hip / unsighted / mounted aim point: a small dot with a dark outline so it reads on sky and on mud
                float dS = u.def.kind == "tank" ? 4 : 3;
                UI.Fill(new Rect(W / 2 - dS / 2 - 1, H / 2 - dS / 2 - 1, dS + 2, dS + 2), new Color(0, 0, 0, .45f)); UI.Fill(new Rect(W / 2 - dS / 2, H / 2 - dS / 2, dS, dS), new Color(1, 1, 1, .8f));
            }
            DrawHitMarker(W, H); DrawCook(W, H);
            if (!scoped && !hudMin && Event.current.type == EventType.Repaint) { var lf = LookFriend(25); if (lf != null) UI.Shadow(new Rect(W / 2 - 200, H / 2 + 44, 400, 20), Defs.NAMES[lf.team][lf.type] + "   <color=#9aa57a>F</color>", UI.lineC, new Color(.6f, 1, .6f, .75f)); }
            var ln = UI.line;
            // ---- soldier status (bottom left, next to the minimap) ----
            if (u.Foot)
            {
                if (u.stance != hudStance) { hudStance = u.stance; stanceShowT = now; }
                if (Mathf.Abs(u.hp - hudHp) > .5f) { hudHp = u.hp; bodyShowT = now; }
                bool hurt = u.bleed > 0 || u.bandaging > 0 || u.hp < u.max * .55f;
                float bx = 196, by = H - 132, aBody = Mathf.Max(hurt ? 1 : OnDemand(bodyShowT, 4), .22f);
                DrawBody(new Rect(bx, by, 46, 103), u, aBody);
                float aSt = Mathf.Max(OnDemand(stanceShowT, 3), u.stance > 0 ? .35f : .18f); DrawStance(new Rect(bx + 62, by + 48, 50, 50), u.stance, aSt);
                if (u.stam < .97f || u.sprinting) { UI.Fill(new Rect(bx + 62, by + 104, 60, 4), new Color(0, 0, 0, .5f)); UI.Fill(new Rect(bx + 62, by + 104, 60 * u.stam, 4), A(FU.Hex(0xe8c547), .9f)); }
                if (u.bandaging > 0) UI.Shadow(new Rect(bx + 62, by + 8, 260, 20), "BANDAGING…", ln, FU.Hex(0xf29a3a));
                else if (u.bleed > 0) UI.Shadow(new Rect(bx + 62, by + 8, 260, 20), "BLEEDING  —  H", ln, new Color(.95f, .3f, .25f, .65f + .35f * Mathf.Sin(now * 7)));
            }
            // ---- hint bar: only for the first seconds of each life ----
            float aHint = hudMin ? 0 : Mathf.Clamp01((hintUntil - now) / 2f);
            if (aHint > 0)
            {
                string sup = supportCd > 0 ? "support " + Mathf.CeilToInt(supportCd) + " s" : "B " + (ME == 0 ? "Stukas" : "artillery");
                UI.Shadow(new Rect(196, H - 160, W - 196 - 560, 22), UI.Fit($"V orders    N reinforce    {sup}    F take control    Tab map    Esc controls", ln, W - 196 - 560), ln, new Color(1, 1, 1, .6f), aHint);
            }
            if (w != null && w.mg && u.heat > .05f)
            {   // barrel heat gauge (RO2): white-hot means stop and change it
                var hr = new Rect(W - 268, H - 140, 240, 6); UI.Fill(hr, new Color(0, 0, 0, .5f));
                UI.Fill(new Rect(hr.x, hr.y, hr.width * u.heat, hr.height), Color.Lerp(new Color(1, .65f, .2f, .85f), new Color(1, .2f, .1f, 1), u.heat));
                if (u.overheated) UI.Shadow(new Rect(W - 520, H - 158, 492, 16), u.barrelT > 0 ? "CHANGING BARREL…" : "BARREL OVERHEATED  —  X", UI.lineR, UI.Red);
            }
            if (u.supp > .6f && u.Foot) UI.Shadow(new Rect(W / 2 - 150, H / 2 + 112, 300, 20), u.Pinned ? "PINNED DOWN" : "SUPPRESSED", UI.lineC, new Color(1, .35f, .25f, .45f + .35f * Mathf.Sin(now * 6)));
            if (ads > .5f && breath < 1) { UI.Fill(new Rect(W / 2 - 60, H / 2 + 60, 120, 4), new Color(0, 0, 0, .5f)); UI.Fill(new Rect(W / 2 - 60, H / 2 + 60, 120 * breath, 4), gasp ? UI.Red : new Color(.7f, .85f, 1, .8f)); }
            if (orderOpen || buyOpen)
            {
                string txt = orderOpen ? "<b><color=#ffd96a>SQUAD ORDERS</color></b>\n[1] Follow me / recruit nearby\n[2] Move to where I look\n[3] Take cover there\n[4] Hold position\n[5] Attack what I aim at\n[6] Double-time there\n[7] Secure the objective (the one you aim at, or the active one)\n<color=#9aa57a>[V] close</color>"
                    : "<b><color=#ffd96a>REINFORCEMENTS</color></b>   <color=#9aa57a>" + Mathf.FloorToInt(cp) + " CP available</color>\n" + string.Join("\n", BuyList().Select((b, i) => (cp >= b.cost ? "" : "<color=#7a7f6a>") + $"[{i + 1}] {b.label} — {b.cost} CP" + (cp >= b.cost ? "" : "</color>"))) + "\n<color=#9aa57a>[N] close</color>";
                var pst = UI.label; float mw = 400, mh = UI.TextH(txt, pst, mw - 36) + 26; var mr = new Rect(W / 2 - mw / 2, H * .34f, mw, mh);
                GUI.Box(mr, GUIContent.none, UI.panel); UI.Frame(mr, UI.Gold); GUI.Label(new Rect(mr.x + 18, mr.y + 13, mw - 36, mh - 20), txt, pst);
            }
            // ---- weapon (bottom right): shown on switch, reload, ammo check (K) or when it matters ----
            string wid = w != null ? w.id : ""; if (wid != hudWep || u.reload > 0 || ammoShowT > 0 || zeroShowT > 0) { hudWep = wid; weapShowT = now; }
            bool crew = u.def.kind == "tank" || u.def.kind == "gun";
            float aW = crew ? 1 : Mathf.Max(OnDemand(weapShowT, 3), w != null && w.clip > 0 && u.clip == 0 ? 1 : 0);
            var rs = UI.lineR;
            if (crew)
            {
                UI.Shadow(new Rect(W - 520, H - 112, 496, 48), u.cd > 0 ? "LOADING" : "READY", UI.ammo, u.cd > 0 ? UI.Dim : UI.Text);
                if (u.def.kind == "tank") UI.Shadow(new Rect(W - 520, H - 62, 496, 22), $"Range {PlayerZero(null, true)} m  (PgUp / PgDn)   LMB main gun   RMB coaxial MG", rs, zeroShowT > 0 ? UI.Gold : A(UI.Dim, .7f));
                // crew and damage state (T27 / T28): the men left, what is broken, what is being repaired
                int cAlive = Crew.Alive(u), cTot = Crew.CrewSize(u); string cs = $"Crew {cAlive}/{cTot}" + (u.def.kind == "gun" && cAlive == 0 ? "  — serving the gun alone (slow)" : "") + "   E leave";
                UI.Shadow(new Rect(W - 520, H - 138, 496, 22), cs, rs, cAlive < cTot ? UI.Gold : A(UI.Dim, .7f));
                if (u.def.kind == "tank" && (u.immobile || u.gunDead || u.hp < u.max * .3f))
                {
                    string dmg = (u.immobile ? "TRACKS / ENGINE OUT" : "") + (u.gunDead ? (u.immobile ? "  ·  " : "") + "MAIN GUN OUT" : "") + (u.hp < u.max * .3f ? "  ·  FIRE" : "");
                    if (u.repairT > 0) dmg += $"     repairing {u.repairWhat} {Mathf.RoundToInt(u.repairT / u.repairDur * 100)}%" + (t - u.lastHit < 2 ? " (under fire)" : "");
                    else if (cAlive < 2) dmg += "     no one spare to repair";
                    bool blink = Mathf.Sin(Time.unscaledTime * 6) > 0 || u.repairT > 0;
                    UI.Shadow(new Rect(W / 2 - 300, H - 200, 600, 24), dmg, UI.lineC, u.repairT > 0 ? A(UI.Gold, .9f) : blink ? A(UI.Red, .9f) : A(UI.Red, .5f));
                }
            }
            else if (aW > 0 && w != null)
            {
                string ammo = w.melee ? "" : u.reload > 0 ? "RELOADING" : u.clip == 0 && w.clip > 0 ? "EMPTY" : ammoShowT > 0 ? AmmoEstimate(u, w) : "";
                UI.Shadow(new Rect(W - 520, H - 112, 496, 48), ammo, ammoShowT > 0 && u.reload <= 0 && u.clip > 0 ? UI.ammo22 : UI.ammo, u.clip == 0 && w.clip > 0 ? UI.Red : UI.Text, aW);
                string kit = new string('✱', Mathf.Clamp(u.gren, 0, 6)) + (u.smoke > 0 ? "  " + new string('☁', Mathf.Min(u.smoke, 4)) : "") + "  " + new string('✚', Mathf.Clamp(u.bandages, 0, 4));
                int zr = PlayerZero(w, false); string zs = w.melee || ZeroSteps(w, false).Length < 2 ? "" : "   " + zr + " m";
                UI.Shadow(new Rect(W - 520, H - 62, 496, 22), w.name + zs + "     " + kit, rs, zeroShowT > 0 ? UI.Gold : UI.Dim, aW);
            }
            if (w != null && w.mg && !crew && u.barrelT <= 0 && ads > .3f) UI.Shadow(new Rect(W / 2 - 150, H / 2 + 70, 300, 20), mgRest ? "BIPOD DEPLOYED" : "NOT DEPLOYED", UI.lineC, mgRest ? A(UI.Green, .7f) : A(UI.Gold, .8f));
        }

        void DrawRts(float W, float H)
        {
            if (dragging) { var mp = Mouse.current.position.ReadValue(); var a = ToGui(dragStart); var b = ToGui(mp); var r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)); UI.Fill(r, new Color(.4f, 1, .4f, .12f)); UI.Frame(r, UI.Green); }
            var panel = new Rect(232, H - 150, W - 232 - 16, 136); GUI.Box(panel, GUIContent.none, UI.panel);
            var groups = selected.Where(u => u.alive).GroupBy(u => u.type).Select(g => g.Count() + "× " + Defs.NAMES[ME][g.Key]);
            string sel = selected.Count == 0 ? "No selection" : "<b>SELECTED</b>  " + string.Join(", ", groups);
            UI.Shadow(new Rect(panel.x + 14, panel.y + 10, 430, 44), sel, UI.small, UI.Text);
            UI.Shadow(new Rect(panel.x + 14, panel.y + 58, 430, 70), "RMB move · Alt+RMB attack-move · C secure · H halt\nF take control · Tab back · Esc: all controls", UI.small, new Color(1, 1, 1, .55f));
            var list = BuyList(); float bx = panel.x + 460, gap = 6, bw = Mathf.Clamp((panel.xMax - 14 - bx - gap * (list.Count - 1)) / Mathf.Max(1, list.Count), 96, 190);
            var bst = UI.cardBtn;
            for (int i = 0; i < list.Count; i++)
            {
                var b = list[i]; GUI.enabled = cp >= b.cost;
                if (GUI.Button(new Rect(bx + i * (bw + gap), panel.y + 12, bw, 58), UI.Fit($"[{i + 1}] {b.label}", bst, bw - 8) + $"\n<color=#ffd96a>{b.cost} CP</color>", bst)) Buy(b);
            }
            GUI.enabled = supportCd <= 0;
            if (GUI.Button(new Rect(bx, panel.y + 78, Mathf.Max(226, bw * 2 + gap), 44), supportCd > 0 ? $"[B] {(ME == 0 ? "Stukas" : "Artillery")} ({Mathf.CeilToInt(supportCd)} s)" : armSupport ? "[B] Click a target…" : $"[B] Call {(ME == 0 ? "Stukas" : "artillery")}", UI.button)) ArmSupport();
            GUI.enabled = true;
        }

        void DrawDeploy(float W, float H)
        {
            // labels over placed formations
            foreach (var c in cards)
            {
                if (!c.placed) continue; var sp = cam.WorldToScreenPoint(new Vector3(c.pos.x, World.H(c.pos.x, c.pos.y) + 2, c.pos.y)); if (sp.z < 0) continue; var g = ToGui(sp);
                bool sel = cardSel >= 0 && cards[cardSel] == c; string txt = (c.star ? "★ " : "") + c.label; float tw = UI.small.CalcSize(new GUIContent(txt)).x + 14;
                UI.Fill(new Rect(g.x - tw / 2, g.y - 24, tw, 20), new Color(0, 0, 0, sel ? .7f : .45f)); UI.Shadow(new Rect(g.x - tw / 2, g.y - 24, tw, 20), txt, UI.center, sel ? UI.Gold : UI.Green);
                var fd = new Vector2(Mathf.Sin(c.face), -Mathf.Cos(c.face)) * 16; UI.Fill(new Rect(g.x + fd.x - 2, g.y + fd.y - 2, 4, 4), sel ? UI.Gold : UI.Green);
            }
            // header + timer bar
            int left = cards.Count(c => !c.placed); float tk = Mathf.Clamp01(deployT / DeployTime);
            UI.Fill(new Rect(0, 96, W, 30), new Color(0, 0, 0, .45f)); UI.Fill(new Rect(0, 124, W * tk, 3), tk < .2f ? UI.Red : UI.Gold);
            UI.Shadow(new Rect(0, 100, W, 24), $"DEPLOYMENT   {Mathf.CeilToInt(Mathf.Max(0, deployT))} s   ·   {cards.Count - left} of {cards.Count} formations on the ground" + (left > 0 ? $"   ·   {left} in reserve (arrive with the first wave)" : ""), UI.center, UI.Gold);
            // tray: one row per formation type, cards inside
            var panel = new Rect(232, H - 206, W - 232 - 16, 192); GUI.Box(panel, GUIContent.none, UI.panel);
            var bst = UI.trayBtn;
            float cw = 176, ch = 28, gap = 5, x0 = panel.x + 12, y0 = panel.y + 10, right = panel.xMax - 300; int perRow = Mathf.Max(1, Mathf.FloorToInt((right - x0) / (cw + gap)));
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i]; var r = new Rect(x0 + (i % perRow) * (cw + gap), y0 + (i / perRow) * (ch + gap), cw, ch); if (r.yMax > panel.yMax - 6) break;
                var col = GUI.color; GUI.color = i == cardSel ? UI.Gold : c.placed ? new Color(.78f, 1, .82f) : new Color(1, 1, 1, .85f);
                string key = i < 9 ? (i + 1).ToString() : "·";
                if (GUI.Button(r, UI.Fit($"{key}  {(c.star ? "★ " : "")}{c.label}{(c.placed ? "  ✓" : "")}", bst, cw - 10), bst)) cardSel = i == cardSel ? -1 : i;
                GUI.color = col;
            }
            // right column: what to do, auto, begin
            string hint = cardSel >= 0 ? (cards[cardSel].placed ? "Selected: " + cards[cardSel].label + " — RMB faces it, R rotates, Del lifts it" : "Click inside the zone to place " + cards[cardSel].label) : left > 0 ? "Pick a card (1–9, Tab or click)" : "All formations placed";
            UI.Shadow(new Rect(right, panel.y + 10, 288, 44), hint, UI.small, UI.Text);
            UI.Shadow(new Rect(right, panel.y + 58, 288, 40), "Click a placed formation to move it · X = the section you lead · Space centres the zone", UI.small, new Color(1, 1, 1, .5f));
            GUI.enabled = left > 0; if (GUI.Button(new Rect(right, panel.yMax - 92, 288, 36), $"AUTO-PLACE THE REST  [V]", UI.button)) AutoPlace(); GUI.enabled = true;
            if (GUI.Button(new Rect(right, panel.yMax - 54, 288, 46), "BEGIN  [Enter]", UI.bigButton)) BeginBattle();
        }

        void DrawDead(float W, float H)
        {
            UI.Fill(new Rect(0, 0, W, H), new Color(.15f, .02f, .01f, .35f));
            UI.Shadow(new Rect(0, 200, W, 70), UI.Spaced("KILLED IN ACTION"), UI.h1s54, UI.Red);
            UI.Shadow(new Rect(0, 272, W, 30), respT > 0 ? $"Redeploy in {Mathf.CeilToInt(respT)}…" : "Choose a class and deploy  (1–" + Defs.CLASSES.Length + ", Space)", UI.center, UI.Text);
            int nc = Defs.CLASSES.Length; float bw = 236, x0 = W / 2 - nc * (bw + 12) / 2 + 6;
            for (int i = 0; i < nc; i++)
            {
                var c = Defs.CLASSES[i]; var r = new Rect(x0 + i * (bw + 12), 340, bw, 150);
                if (GUI.Button(r, GUIContent.none, i == classIdx ? UI.cardSel : UI.card)) classIdx = i;
                var cst = UI.h2s18;
                UI.Shadow(new Rect(r.x + 14, r.y + 10, r.width - 28, 28), UI.Fit("[" + (i + 1) + "] " + c.label.ToUpper(), cst, r.width - 28), cst, i == classIdx ? UI.Gold : UI.Text);
                string wl = string.Join("\n", c.weps[ME].Select(id => "· " + Defs.WEAPONS[id].name)) + "\n· grenades ×" + c.gren + (c.smoke > 0 ? "\n· smoke grenades ×" + c.smoke : "");
                UI.Shadow(new Rect(r.x + 14, r.y + 42, r.width - 28, 104), wl, UI.small, UI.Dim);
            }
            if (GUI.Button(new Rect(W / 2 - 150, 508, 300, 34), "Spawn at: " + (respSquad ? "SQUAD (front line)" : "BASE") + "   [X]", UI.button)) respSquad = !respSquad;
            GUI.enabled = respT <= 0 && CanDeploy; if (GUI.Button(new Rect(W / 2 - 150, 550, 300, 52), CanDeploy ? "DEPLOY" : "NO REINFORCEMENTS", UI.bigButton)) TryDeploy(classIdx); GUI.enabled = true;
            UI.Shadow(new Rect(0, 614, W, 24), "F: take control of a nearby soldier, tank or gun  ·  Tab: command map", UI.center, UI.Dim);
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
            var r = new Rect(W / 2 - 450, 104, 900, 620); GUI.Box(r, GUIContent.none, UI.panel); UI.Frame(r, UI.Gold);
            UI.Shadow(new Rect(r.x, r.y + 12, r.width, 34), UI.Spaced("SCOREBOARD"), UI.h2C, UI.Gold);
            var ln = UI.line; var num = UI.lineR;
            string foot = range != null ? "Test Range — scores are not kept." : "Kill 10  ·  kill while defending the objective 15  ·  capture 50  ·  you redeploy with the next reinforcement wave (every " + (int)WaveTime + " s)";
            float fh = UI.TextH(foot, UI.small, r.width - 48), rowsTop = r.y + 136, rows = Mathf.Max(4, Mathf.FloorToInt((r.yMax - fh - 22 - rowsTop) / 21));
            for (int tm = 0; tm < 2; tm++)
            {
                int side = tm == 0 ? ME : EN; float x = r.x + 24 + tm * 438, cw = 414, y = r.y + 58;
                var all = units.Where(u => u.team == side && u.def.kind != "gun").ToList();
                int total = all.Sum(u => u.score), held = World.points.Count(p => p.locked && p.owner == side);
                string role = range != null ? "" : side == M.attacker ? "ATTACKING" : "DEFENDING";
                UI.Shadow(new Rect(x, y, cw, 24), Defs.SIDE[side].ToUpper() + "   " + role, ln, side == ME ? UI.Green : UI.Red); y += 24;
                UI.Shadow(new Rect(x, y, cw, 22), UI.Fit($"Score {total}  ·  objectives {held}/{World.points.Count}  ·  reserves {tix[side]}", ln, cw), ln, UI.Text); y += 30;
                UI.Shadow(new Rect(x, y, 240, 20), "SOLDIER", ln, UI.Dim); UI.Shadow(new Rect(x + 250, y, 70, 20), "SCORE", num, UI.Dim); UI.Shadow(new Rect(x + 326, y, 40, 20), "K", num, UI.Dim); UI.Shadow(new Rect(x + 370, y, 40, 20), "D", num, UI.Dim);
                y = rowsTop; UI.Fill(new Rect(x, y - 3, cw - 4, 1), new Color(.42f, .48f, .29f, .6f));
                foreach (var u in all.OrderByDescending(u => u == player).ThenByDescending(u => u.score).ThenByDescending(u => u.kills).ThenBy(u => u.deaths).Take((int)rows))
                {
                    bool me = u == player; Color c = me ? UI.Gold : u.alive ? UI.Text : UI.Dim;
                    if (me) UI.Fill(new Rect(x - 4, y - 1, cw, 21), new Color(UI.Gold.r, UI.Gold.g, UI.Gold.b, .12f));
                    UI.Shadow(new Rect(x, y, 240, 20), UI.Fit((me ? "> " : "  ") + Defs.NAMES[u.team][u.type] + " #" + u.id + (u.alive ? "" : "  †"), ln, 240), ln, c);
                    UI.Shadow(new Rect(x + 250, y, 70, 20), u.score.ToString(), num, c); UI.Shadow(new Rect(x + 326, y, 40, 20), u.kills.ToString(), num, c); UI.Shadow(new Rect(x + 370, y, 40, 20), u.deaths.ToString(), num, c);
                    y += 21;
                }
            }
            UI.Shadow(new Rect(r.x + 24, r.yMax - fh - 14, r.width - 48, fh), foot, UI.center, UI.Dim);
        }

        void DrawPause(float W, float H)
        {
            UI.Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, .6f)); UI.Shadow(new Rect(0, 150, W, 70), UI.Spaced("PAUSED"), UI.h1, UI.Text);
            var box = new Rect(W / 2 - 440, 240, 880, 470); GUI.Box(box, GUIContent.none, UI.panel);
            pauseTab = UI.Tabs(new Rect(box.x + 20, box.y + 16, box.width - 40, 36), new[] { "GAME", "SETTINGS", "CONTROLS" }, pauseTab);
            var body = new Rect(box.x + 20, box.y + 68, box.width - 40, box.height - 88);
            if (pauseTab == 0)
            {
                float bx = W / 2 - 150, by = body.y + 60;
                if (GUI.Button(new Rect(bx, by, 300, 50), "Resume", UI.bigButton)) Resume();
                if (GUI.Button(new Rect(bx, by + 64, 300, 44), "Restart mission", UI.button)) { Time.timeScale = 1; StartMission(missionIdx, ME); }
                if (GUI.Button(new Rect(bx, by + 118, 300, 44), "Quit to menu", UI.button)) EnterMenu();
                UI.Shadow(new Rect(body.x, body.yMax - 30, body.width, 22), M != null ? M.name + "  ·  " + M.date + "  ·  " + (ME == M.attacker ? "attacking" : "defending") + " as " + Defs.SIDE[ME] : "", UI.smallC, UI.Dim);
            }
            else if (pauseTab == 1) DrawSettings(body);
            else DrawControls(body);
        }

        /// Settings, nested one level: Controls & HUD / Graphics / Audio. Shared by the pause screen and the main menu; every value is saved to PlayerPrefs as it changes.
        void DrawSettings(Rect r)
        {
            setTab = UI.Tabs(new Rect(r.x + 100, r.y, r.width - 200, 30), new[] { "INPUT & HUD", "GRAPHICS", "AUDIO" }, setTab);
            float x = r.x + 60, w = r.width - 120, y = r.y + 56; bool ch = false;
            if (setTab == 0)
            {
                float ns = UI.Slider(x, y, w, "Mouse sensitivity", sens, .2f, 3f, "0.00"); y += 30; if (ns != sens) { sens = ns; PlayerPrefs.SetFloat("FW.Sens", ns); ch = true; }
                float nf = UI.Slider(x, y, w, "Field of view", baseFov, 60, 95, "0"); y += 36; if (nf != baseFov) { baseFov = nf; PlayerPrefs.SetFloat("FW.Fov", nf); ch = true; }
                bool nh = UI.Toggle(x, y, w, "Hip-fire dot  (RO2 has none)", hipDot); y += 34; if (nh != hipDot) { hipDot = nh; PlayerPrefs.SetInt("FW.HipDot3", nh ? 1 : 0); ch = true; }
                bool nm = UI.Toggle(x, y, w, "Hit markers", hitMarkers); y += 34; if (nm != hitMarkers) { hitMarkers = nm; PlayerPrefs.SetInt("FW.HitMarkers", nm ? 1 : 0); ch = true; }
                bool mn = UI.Toggle(x, y, w, "Minimal HUD  (no squad list, status line or hints)", hudMin); y += 34; if (mn != hudMin) { hudMin = mn; PlayerPrefs.SetInt("FW.HudMin", mn ? 1 : 0); ch = true; }
                UI.Shadow(new Rect(x, r.yMax - 48, w, 40), "Key bindings are listed under CONTROLS. The HUD fades widgets in when their value changes and out again a few seconds later; K shows the ammunition estimate at any time.", UI.small, UI.Dim);
            }
            else if (setTab == 1)
            {
                int nq = UI.Choice(x, y, w, "Quality preset", new[] { "LOW", "MEDIUM", "HIGH" }, quality); y += 34; if (nq != quality) { quality = nq; PlayerPrefs.SetInt("FW.Quality", nq); vis.SetQuality(nq); ch = true; }
                bool nv = UI.Toggle(x, y, w, "V-sync", vsync); y += 34; if (nv != vsync) { vsync = nv; PlayerPrefs.SetInt("FW.VSync", nv ? 1 : 0); QualitySettings.vSyncCount = nv ? 1 : 0; ch = true; }
                string[] desc = { "LOW — shadows to 80 m, hard, 2 cascades; no anti-aliasing; grass to 90 m. For integrated graphics.", "MEDIUM — shadows to 150 m, soft, 2 cascades; SMAA medium; grass to 140 m. The default.", "HIGH — shadows to 220 m, soft, 4 cascades; SMAA high; grass to 200 m." };
                UI.Shadow(new Rect(x, y + 6, w, 44), desc[quality], UI.small, UI.Dim); y += 56;
                UI.Shadow(new Rect(x, y, w, 60), $"Resolution {Screen.width}×{Screen.height}  ·  {(Screen.fullScreen ? "full screen" : "windowed")}  ·  post-processing is always on in first person and off on the command map.", UI.small, UI.Dim);
            }
            else
            {
                float nm = UI.Slider(x, y, w, "Master volume", SoundBank.MasterVol, 0, 1, "0%"); y += 30; if (nm != SoundBank.MasterVol) { SoundBank.MasterVol = nm; PlayerPrefs.SetFloat("FW.VolMaster", nm); ch = true; }
                UI.Fill(new Rect(x, y + 4, w, 1), new Color(.42f, .48f, .29f, .6f)); y += 14;
                float no = UI.Slider(x, y, w, "Your weapon", SoundBank.OwnVol, 0, 1, "0%"); y += 30; if (no != SoundBank.OwnVol) { SoundBank.OwnVol = no; PlayerPrefs.SetFloat("FW.VolOwn", no); ch = true; }
                float nw = UI.Slider(x, y, w, "Battlefield (other guns, engines)", SoundBank.WorldVol, 0, 1, "0%"); y += 30; if (nw != SoundBank.WorldVol) { SoundBank.WorldVol = nw; PlayerPrefs.SetFloat("FW.VolWorld", nw); ch = true; }
                float ne = UI.Slider(x, y, w, "Explosions", SoundBank.ExploVol, 0, 1, "0%"); y += 30; if (ne != SoundBank.ExploVol) { SoundBank.ExploVol = ne; PlayerPrefs.SetFloat("FW.VolExplo", ne); ch = true; }
                float na = UI.Slider(x, y, w, "Ambience (wind, distant battle)", SoundBank.AmbVol, 0, 1, "0%"); y += 30; if (na != SoundBank.AmbVol) { SoundBank.AmbVol = na; PlayerPrefs.SetFloat("FW.VolAmb", na); ch = true; }
                UI.Shadow(new Rect(x, r.yMax - 48, w, 40), "Loud sounds are limited at the output, so the master can stay high. Your own weapon is already a little quieter than the ones firing at you, as in RO2.", UI.small, UI.Dim);
            }
            if (ch) PlayerPrefs.Save();
        }

        void DrawControls(Rect r)
        {
            string inf = "<b><color=#ffd96a>INFANTRY</color></b>\nWASD move · Shift sprint · C crouch · Z prone\nQ / E lean · LMB fire · RMB iron sights\nShift while aiming: hold breath\nR reload · K check ammo · 1–3 / wheel weapons\nPage Up / Page Down: sight range (zeroing)\nG grenade · Shift+G smoke grenade\nH bandage · T third person · L scoreboard\nMG: go prone or rest on cover to deploy,\n      X changes an overheated barrel";
            string cmd = "<b><color=#ffd96a>COMMAND</color></b>\nV squad orders (7: secure objective) · N reinforcements\nB call support (aim at the target)\nF take control of the friendly you look at\nE climb into / out of a tank or gun (crews bail out when it is lost)\n\n<b><color=#ffd96a>COMMAND MAP</color></b>  (Tab)\nLMB select / drag · RMB move\nAlt+RMB attack-move · Ctrl+RMB fast\nShift queue · H halt · Ctrl+A all\nC + click: secure an objective\n1–6 buy units · B support strike\nF / P take control\nminimap: LMB jump · RMB order";
            string dep = "<b><color=#ffd96a>DEPLOYMENT</color></b>\n1–9 / Tab pick a card · LMB place · RMB face\nR rotate · X lead this section · Del lift\nV auto-place the rest · Enter begin";
            float cw = (r.width - 24) / 2; GUI.Label(new Rect(r.x, r.y + 8, cw, r.height - 16), inf + "\n\n" + dep, UI.small); GUI.Label(new Rect(r.x + cw + 24, r.y + 8, cw, r.height - 16), cmd, UI.small);
        }

        void DrawEnd(float W, float H)
        {
            UI.Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, .62f));
            UI.Shadow(new Rect(0, 210, W, 80), UI.Spaced(endTitle), UI.h1s72, won ? UI.Green : UI.Red);
            UI.Shadow(new Rect(0, 294, W, 30), M.name + " — " + M.date, UI.center, UI.Text);
            UI.Shadow(new Rect(0, 326, W, 30), endWhy, UI.center, UI.Gold);
            GUI.Box(new Rect(W / 2 - 360, 380, 720, 90), GUIContent.none, UI.panel);
            string[] lab = { "OBJECTIVES HELD", "ENEMY DESTROYED", "YOUR LOSSES", "TIME" }; string[] val = { World.points.Count(p => p.owner == ME) + " / " + World.points.Count, kills.ToString(), losses.ToString(), $"{(int)(t / 60)}:{(int)(t % 60):00}" };
            for (int i = 0; i < 4; i++) { UI.Shadow(new Rect(W / 2 - 350 + i * 175, 392, 170, 20), lab[i], UI.smallC, UI.Dim); UI.Shadow(new Rect(W / 2 - 350 + i * 175, 416, 170, 40), val[i], UI.h2UC, UI.Text); }
            if (won && M.name.Contains("Warsaw")) GUI.Label(new Rect(W / 2 - 420, 480, 840, 80), "On 17 September the Soviet Union invaded from the east. Warsaw capitulated on 28 September and the last Polish units surrendered at Kock on 6 October 1939. Poland was partitioned, and the Second World War in Europe had begun.", UI.smallC);
            float y = 570;
            if (GUI.Button(new Rect(W / 2 - 340, y, 210, 50), "Retry", UI.button)) StartMission(missionIdx, ME);
            int next = NextMission(); if (won && next >= 0 && GUI.Button(new Rect(W / 2 - 105, y, 210, 50), "Next mission", UI.bigButton)) StartMission(next, ME);
            if (GUI.Button(new Rect(W / 2 + 130, y, 210, 50), "Menu", UI.button)) EnterMenu();
        }

        void DrawMenu(float W, float H)
        {
            UI.Tex(new Rect(0, 0, 1150, H), UI.gradL, Color.white); UI.Tex(new Rect(0, H - 170, W, 170), UI.gradB, new Color(1, 1, 1, .9f));
            UI.Shadow(new Rect(70, 52, 1000, 96), UI.Spaced("FALL WEISS"), UI.titleBig, UI.Gold);
            UI.Shadow(new Rect(74, 146, 900, 30), UI.Spaced("POLAND  ·  SEPTEMBER 1939"), UI.h3s20, UI.Text);
            UI.Fill(new Rect(74, 182, 520, 2), new Color(UI.Gold.r, UI.Gold.g, UI.Gold.b, .7f));
            UI.Shadow(new Rect(74, 190, 900, 22), "Red Orchestra-style infantry combat with a Steel Division command layer.", UI.small, UI.Dim);
            if (menuSettings >= 0)
            {   // settings over the menu, same panel as the pause screen; drawn before the menu buttons so they cannot take the clicks
                UI.Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, .55f)); var box = new Rect(W / 2 - 440, 220, 880, 500); GUI.Box(box, GUIContent.none, UI.panel); UI.Frame(box, UI.Gold);
                UI.Shadow(new Rect(box.x, box.y + 14, box.width, 30), UI.Spaced("SETTINGS"), UI.h2C, UI.Gold);
                DrawSettings(new Rect(box.x + 20, box.y + 60, box.width - 40, box.height - 130));
                if (GUI.Button(new Rect(W / 2 - 110, box.yMax - 60, 220, 42), "DONE", UI.bigButton) || (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)) menuSettings = -1;
                return;
            }
            UI.Shadow(new Rect(74, 230, 300, 22), "CHOOSE YOUR ARMY", UI.h3, UI.Dim);
            for (int s = 0; s < 2; s++)
            {
                var r = new Rect(74 + s * 266, 256, 256, 84);
                if (GUI.Button(r, GUIContent.none, menuSide == s ? UI.cardSel : UI.card)) { if (menuSide != s) { menuSide = s; ME = s; EN = 1 - s; } }
                UI.Tex(new Rect(r.x + 12, r.y + 14, 56, 56), s == 0 ? UI.emblemG : UI.emblemP, Color.white);
                UI.Shadow(new Rect(r.x + 80, r.y + 14, 170, 30), s == 0 ? "WEHRMACHT" : "POLISH ARMY", UI.h2s21, menuSide == s ? UI.Gold : UI.Text);
                UI.Shadow(new Rect(r.x + 80, r.y + 44, 170, 30), s == 0 ? "Heer · Luftwaffe" : "Wojsko Polskie", UI.small, UI.Dim);
            }
            UI.Shadow(new Rect(74, 358, 400, 22), "CHOOSE YOUR BATTLE", UI.h3, UI.Dim);
            for (int i = 0; i < missions.Length; i++)
            {
                var m = missions[i]; var r = new Rect(74, 384 + i * 74, 522, 66); bool sel = i == menuMission; bool att = m.attacker == menuSide;
                if (GUI.Button(r, GUIContent.none, sel ? UI.cardSel : UI.card)) { if (menuMission != i) { menuMission = i; BuildBackdrop(i); } }
                UI.Shadow(new Rect(r.x + 14, r.y + 8, 40, 50), (i + 1).ToString(), UI.h2s34, sel ? UI.Gold : new Color(UI.Dim.r, UI.Dim.g, UI.Dim.b, .7f));
                UI.Shadow(new Rect(r.x + 58, r.y + 9, 340, 26), m.name.ToUpper(), UI.h2s19, sel ? UI.Gold : UI.Text);
                UI.Shadow(new Rect(r.x + 58, r.y + 37, 340, 22), m.date, UI.small, UI.Dim);
                var tag = new Rect(r.xMax - 96, r.y + 20, 82, 26); Color tc = m.range ? UI.Dim : att ? UI.Red : UI.Green; UI.Fill(tag, new Color(0, 0, 0, .35f)); UI.Frame(tag, tc);
                UI.Shadow(tag, m.range ? "PRACTICE" : att ? "ATTACK" : "DEFEND", UI.smallBoldC, tc);
            }
            var M0 = missions[menuMission]; bool a0 = M0.attacker == menuSide;
            var bp = new Rect(W - 640, 256, 576, 700); GUI.Box(bp, GUIContent.none, UI.panel);
            UI.Shadow(new Rect(bp.x + 22, bp.y + 18, bp.width - 44, 22), "OPERATIONAL BRIEFING", UI.h3, UI.Dim);
            UI.Shadow(new Rect(bp.x + 22, bp.y + 42, bp.width - 44, 40), M0.name.ToUpper(), UI.h2s30, UI.Gold);
            UI.Shadow(new Rect(bp.x + 22, bp.y + 84, bp.width - 44, 22), M0.range ? M0.date + "   ·   free practice" : M0.date + "   ·   you " + (a0 ? "ATTACK" : "DEFEND") + " as " + Defs.SIDE[menuSide], UI.small, M0.range ? UI.Dim : a0 ? UI.Red : UI.Green);
            UI.Fill(new Rect(bp.x + 22, bp.y + 114, bp.width - 44, 1), new Color(.42f, .48f, .29f, 1));
            GUI.Label(new Rect(bp.x + 22, bp.y + 126, bp.width - 44, 170), M0.brief, UI.label15);
            if (!M0.range)
            {
                UI.Shadow(new Rect(bp.x + 22, bp.y + 300, 300, 22), "OBJECTIVES — taken in order", UI.h3, UI.Dim);
                for (int i = 0; i < Mathf.Min(5, M0.points.Length); i++) UI.Shadow(new Rect(bp.x + 30, bp.y + 328 + i * 24, bp.width - 60, 22), UI.Fit((i + 1) + ".  " + M0.points[i].n, UI.line, bp.width - 60), UI.line, UI.Text);
            }
            string[] lab = { M0.range ? "AMMUNITION" : "REINFORCEMENTS", "TIME LIMIT", "WEATHER" };
            string[] val = { M0.range ? "unlimited" : (a0 ? M0.tickets : Mathf.RoundToInt(M0.tickets * 1.6f)).ToString(), M0.range ? "none" : $"{(int)(M0.time / 60)}:00", M0.hasRain ? "rain later" : M0.city ? "smoke, haze" : "clear" };
            for (int i = 0; i < 3; i++) { UI.Shadow(new Rect(bp.x + 22 + i * 180, bp.y + 466, 176, 20), lab[i], UI.small, UI.Dim); UI.Shadow(new Rect(bp.x + 22 + i * 180, bp.y + 486, 176, 34), val[i], UI.h2s24, UI.Text); }
            UI.Shadow(new Rect(bp.x + 22, bp.y + 540, bp.width - 44, 60), M0.range ? "Try every weapon on live targets and a tank. F1–F11 or the wheel switch weapons, O shows hit zones." : a0 ? "Every death costs one reinforcement. Capture all " + M0.points.Length + " positions before the clock or your reserves run out." : "Hold every position until the clock runs out. Lost positions cannot be retaken.", UI.small, UI.Dim);
            if (GUI.Button(new Rect(bp.x + 22, bp.yMax - 82, bp.width - 44, 62), UI.Spaced(M0.range ? "ENTER THE RANGE" : "BEGIN OPERATION"), UI.bigButton)) StartMission(menuMission, menuSide);
            if (GUI.Button(new Rect(74, H - 96, 160, 36), "SETTINGS", UI.button)) menuSettings = menuSettings < 0 ? 0 : -1;
            if (GUI.Button(new Rect(244, H - 96, 160, 36), "QUIT", UI.button)) Application.Quit();
            UI.Shadow(new Rect(74, H - 46, W - 148, 24), "WASD move · Mouse aim · LMB fire · RMB iron sights · Tab command map · B support · Esc pause", UI.small, new Color(1, 1, 1, .55f));
            UI.Shadow(new Rect(W - 420, H - 46, 346, 24), "Unity port · " + missions.Length + " missions", UI.smallR, new Color(1, 1, 1, .35f));
        }
    }
}
