// Fall Weiss — visual effects (tracers, muzzle flashes, alpha-faded smoke/fire/dust puffs, layered explosions)
// and procedural audio (port of audio.js idea: synthesised shots, booms, Stuka siren).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FallWeiss
{
    public class Fx
    {
        class Live { public Transform t; public float life, max, s0, s1, a, grav; public Renderer r; public Light l; public LineRenderer line; public Vector3 v; public Color c; }
        readonly List<Live> live = new List<Live>();
        readonly Queue<LineRenderer> lines = new Queue<LineRenderer>();
        class Round { public LineRenderer lr; public Vector3 a, dir; public float dist, s, age; }
        readonly List<Round> rounds = new List<Round>(); readonly Queue<LineRenderer> roundPool = new Queue<LineRenderer>(); Material roundMat;
        readonly Queue<Transform> puffs = new Queue<Transform>();
        Transform root; Material tracerMat; MaterialPropertyBlock mpb; Mesh puffMesh; Texture2D soft;
        Material puffBlend, puffAdd;

        public void Init()
        {
            root = new GameObject("FX").transform; mpb = new MaterialPropertyBlock();
            // soft round sprite texture for billboard puffs
            soft = new Texture2D(64, 64, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[64 * 64]; for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) { float d = Mathf.Sqrt((x - 31.5f) * (x - 31.5f) + (y - 31.5f) * (y - 31.5f)) / 31.5f; float a = Mathf.Clamp01(1 - d); px[y * 64 + x] = new Color(1, 1, 1, a * a * (3 - 2 * a)); }
            soft.SetPixels(px); soft.Apply();
            tracerMat = Mats.Transparent(new Color(1f, .82f, .4f, .95f), null, true);
            puffBlend = Mats.Transparent(Color.white, soft, false); puffAdd = Mats.Transparent(Color.white, soft, true);
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); puffMesh = q.GetComponent<MeshFilter>().sharedMesh; Object.Destroy(q);
            for (int i = 0; i < 70; i++)
            {
                var g = new GameObject("tracer"); g.transform.SetParent(root); var lr = g.AddComponent<LineRenderer>();
                lr.sharedMaterial = tracerMat; lr.positionCount = 2; lr.startWidth = .06f; lr.endWidth = .025f; lr.shadowCastingMode = ShadowCastingMode.Off; lr.enabled = false; lines.Enqueue(lr);
            }
            roundMat = Mats.Transparent(Color.white, null, true);
            for (int i = 0; i < BulletFx.Pool; i++)
            {
                var g = new GameObject("round"); g.transform.SetParent(root); var lr = g.AddComponent<LineRenderer>(); lr.sharedMaterial = roundMat; lr.positionCount = 2;
                lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false; lr.numCapVertices = 2; lr.enabled = false; roundPool.Enqueue(lr);
            }
            for (int i = 0; i < 320; i++)
            {
                var g = new GameObject("puff"); g.transform.SetParent(root); g.AddComponent<MeshFilter>().sharedMesh = puffMesh; var r = g.AddComponent<MeshRenderer>();
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; g.SetActive(false); puffs.Enqueue(g.transform);
            }
        }
        public void Clear() { foreach (var r in rounds) r.lr.enabled = false; rounds.Clear(); foreach (var l in live) { if (l.line) l.line.enabled = false; else if (l.t) l.t.gameObject.SetActive(false); if (l.l) Object.Destroy(l.l.gameObject); } live.Clear(); }

        /// A streak for a round in flight (tracer / shell): the caller moves it each frame with Streak(lr, tail, head) and
        /// returns it with EndStreak. Pooled with the fire-and-forget tracers, so it shares their limit and look.
        public LineRenderer Streak(Vector3 p, bool tracer)
        {
            if (!BulletFx.Enabled || roundPool.Count == 0) return null; var lr = roundPool.Dequeue(); roundPool.Enqueue(lr); rounds.RemoveAll(r => r.lr == lr);
            lr.startWidth = BulletFx.TailWidth; lr.endWidth = tracer ? BulletFx.HeadWidth * 1.6f : BulletFx.HeadWidth;
            var gr = new Gradient(); var head = tracer ? new Color(1f, .55f, .25f, 1f) : BulletFx.Head; var tail = tracer ? new Color(1f, .35f, .1f, 0f) : BulletFx.Tail;
            gr.SetKeys(new[] { new GradientColorKey(tail, 0), new GradientColorKey(head, 1) }, new[] { new GradientAlphaKey(tail.a, 0), new GradientAlphaKey(head.a, 1) }); lr.colorGradient = gr;
            lr.SetPosition(0, p); lr.SetPosition(1, p); lr.enabled = true; return lr;
        }
        public void Streak(LineRenderer lr, Vector3 tail, Vector3 head, Camera cam)
        {
            if (!lr) return; lr.SetPosition(0, tail); lr.SetPosition(1, head);
            if (cam) { float cd = Vector3.Distance(cam.transform.position, head), wmin = cd * BulletFx.MinPixelAngle; lr.endWidth = Mathf.Max(lr.endWidth, wmin); lr.startWidth = Mathf.Max(BulletFx.TailWidth, wmin * .35f); }
        }
        public void EndStreak(LineRenderer lr) { if (lr) lr.enabled = false; }
        public void Tracer(Vector3 a, Vector3 b)
        {
            if (World.M != null) { float wy = World.WaterY(b.x, b.z); if (wy > -900 && b.y < wy + .15f) Puff(new Vector3(b.x, wy + .05f, b.z), new Color(.85f, .9f, .95f, .85f), .3f, 1.4f, .7f, Vector3.up * 3.2f); }   // shots into water throw a splash
            if (BulletFx.Enabled && roundPool.Count > 0)
            {
                var d = b - a; float dist = d.magnitude; if (dist < .05f) return; var rlr = roundPool.Dequeue(); roundPool.Enqueue(rlr); rounds.RemoveAll(r => r.lr == rlr);
                rlr.startWidth = BulletFx.TailWidth; rlr.endWidth = BulletFx.HeadWidth;
                var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(BulletFx.Tail, 0), new GradientColorKey(BulletFx.Head, 1) }, new[] { new GradientAlphaKey(BulletFx.Tail.a, 0), new GradientAlphaKey(BulletFx.Head.a, 1) }); rlr.colorGradient = gr;
                rlr.SetPosition(0, a); rlr.SetPosition(1, a); rlr.enabled = true; rounds.Add(new Round { lr = rlr, a = a, dir = d / dist, dist = dist });
                return;
            }
            if (lines.Count == 0) return; var lr = lines.Dequeue(); lines.Enqueue(lr);
            lr.SetPosition(0, a); lr.SetPosition(1, b); lr.enabled = true;
            live.RemoveAll(x => x.line == lr); live.Add(new Live { line = lr, life = .07f, max = .07f });
        }
        /// Camera-facing soft puff. `add` = additive (fire / flashes), otherwise alpha-blended (smoke / dust).
        public void Puff(Vector3 p, Color c, float s0, float s1, float life, Vector3 v, bool add = false, float grav = 0)
        {
            var t = puffs.Dequeue(); puffs.Enqueue(t); live.RemoveAll(x => x.t == t);
            t.gameObject.SetActive(true); t.position = p; t.localScale = Vector3.one * s0 * 2; var r = t.GetComponent<Renderer>(); r.sharedMaterial = add ? puffAdd : puffBlend;
            live.Add(new Live { t = t, r = r, life = life, max = life, s0 = s0, s1 = s1, v = v, c = c, a = c.a, grav = grav });
        }
        public void Flash(Vector3 p, float s) => Puff(p, new Color(1f, .78f, .35f, 1), s, s * 1.8f, .06f, Vector3.zero, true);
        /// Muzzle blast out of the end of the barrel: a bright core at the muzzle, a forward cone of flame along the bore,
        /// two small side jets, then a puff of smoke drifting off the muzzle.
        public void MuzzleFlash(Vector3 p, Vector3 dir, float s)
        {
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward; var side = Vector3.Cross(dir, Vector3.up); if (side.sqrMagnitude < 1e-4f) side = Vector3.right; side.Normalize();
            var hot = new Color(1f, .82f, .42f, 1); var warm = new Color(1f, .6f, .25f, .9f);
            // the core is alpha-blended, not additive: an additive-only flash disappears against a bright sky or the sun, which is
            // exactly where a rifle's muzzle sits in the hip and aimed views. The glow around it stays additive.
            Puff(p + dir * s * .15f, new Color(1f, .96f, .78f, .95f), s * .4f, s * .65f, .06f, Vector3.zero, false);
            Puff(p + dir * s * .15f, hot, s * .55f, s * .9f, .045f, Vector3.zero, true);
            Puff(p + dir * s * .7f, hot, s * .45f, s * .7f, .05f, dir * 2, true);
            Puff(p + dir * s * 1.35f, warm, s * .3f, s * .45f, .045f, dir * 3, true);
            Puff(p + dir * s * .3f + side * s * .35f, warm, s * .18f, s * .28f, .035f, side * 2, true);
            Puff(p + dir * s * .3f - side * s * .35f, warm, s * .18f, s * .28f, .035f, -side * 2, true);
            Puff(p + dir * s * .5f, new Color(.62f, .6f, .56f, .35f), s * .4f, s * 1.6f, .7f, dir * .8f + Vector3.up * .5f);
        }
        public void Dust(Vector3 p) { Puff(p, new Color(.55f, .48f, .36f, .65f), .3f, 1.1f, .55f, Vector3.up * 1.2f); }
        public void Explosion(Vector3 p, float size)
        {
            Puff(p + Vector3.up * size * .3f, new Color(1f, .62f, .2f, 1), size * .6f, size * 1.5f, .35f, Vector3.up * 2, true);
            Puff(p + Vector3.up * size * .2f, new Color(1f, .9f, .6f, 1), size * .3f, size * .9f, .12f, Vector3.zero, true);
            for (int i = 0; i < 6; i++) Puff(p + new Vector3(FU.Rand(-1, 1), FU.Rand(0, 1), FU.Rand(-1, 1)) * size * .5f, i < 3 ? new Color(.45f, .38f, .28f, .8f) : new Color(.18f, .17f, .16f, .7f), size * .45f, size * FU.Rand(1.4f, 2.4f), FU.Rand(1.2f, 2.6f), new Vector3(FU.Rand(-1, 1), FU.Rand(2, 4.5f), FU.Rand(-1, 1)));
            for (int i = 0; i < 8; i++) Puff(p + Vector3.up * .3f, new Color(.25f, .2f, .14f, 1), .12f, .08f, FU.Rand(.6f, 1.1f), new Vector3(FU.Rand(-6, 6), FU.Rand(6, 12), FU.Rand(-6, 6)), false, 22);
            var lg = new GameObject("boomlight"); lg.transform.position = p + Vector3.up * 2; var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1, .7f, .4f); l.range = size * 9; l.intensity = 8; l.shadows = LightShadows.None;
            live.Add(new Live { l = l, life = .28f, max = .28f });
        }
        public void Fire(Vector3 p, bool big)
        {
            Puff(p + new Vector3(FU.Rand(-.8f, .8f), 0, FU.Rand(-.8f, .8f)), new Color(1f, .55f, .17f, .9f), big ? 1.6f : .8f, .3f, .5f, Vector3.up * 3, true);
            Puff(p + Vector3.up, new Color(.17f, .17f, .16f, .55f), 1.2f, big ? 9 : 5, 5, new Vector3(1.6f, 5, .4f));
        }

        public void Update(float dt, Camera cam)
        {
            for (int i = rounds.Count - 1; i >= 0; i--)
            {   // move each streak along its path; it is gone once its tail reaches the impact point
                var r = rounds[i]; r.s += BulletFx.Speed * dt; r.age += dt; float head = Mathf.Min(r.s, r.dist), tail = Mathf.Max(0, Mathf.Min(r.s - BulletFx.Length, r.dist));
                if (tail >= r.dist && r.age >= BulletFx.MinLife) { r.lr.enabled = false; rounds.RemoveAt(i); continue; }
                if (head - tail < .3f) tail = Mathf.Max(0, head - .3f);   // a point-blank round still shows as a short streak
                r.lr.SetPosition(0, r.a + r.dir * tail); r.lr.SetPosition(1, r.a + r.dir * head);
                if (cam)
                {
                    var hp = r.a + r.dir * head; var toHead = hp - cam.transform.position; float cd = toHead.magnitude, wmin = cd * BulletFx.MinPixelAngle;
                    // a streak flying almost straight away from (or at) the camera has no screen length, and a view-aligned LineRenderer then
                    // smears its width into a tall bar: the pink "flash" on every one of your own shots. Fade it out inside ~12 deg of the view axis.
                    float par = cd > .01f ? Mathf.Abs(Vector3.Dot(r.dir, toHead / cd)) : 1, vis = 1 - Mathf.Clamp01((par - .94f) / .04f);
                    r.lr.enabled = vis > .02f;
                    r.lr.endWidth = Mathf.Max(BulletFx.HeadWidth, wmin) * vis; r.lr.startWidth = Mathf.Max(BulletFx.TailWidth, wmin * .35f) * vis;
                }
            }
            var cr = cam ? cam.transform.rotation : Quaternion.identity;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var L = live[i]; L.life -= dt; float k = 1 - Mathf.Clamp01(L.life / L.max);
                if (L.life <= 0)
                {
                    if (L.line) L.line.enabled = false; else if (L.l) Object.Destroy(L.l.gameObject); else if (L.t) L.t.gameObject.SetActive(false);
                    live.RemoveAt(i); continue;
                }
                if (L.t)
                {
                    L.v.y -= L.grav * dt; L.t.position += L.v * dt; L.t.localScale = Vector3.one * Mathf.Lerp(L.s0, L.s1, k) * 2; L.t.rotation = cr;
                    var c = L.c; c.a = L.a * (1 - k * k); mpb.SetColor("_Color", c); L.r.SetPropertyBlock(mpb);
                }
                if (L.l) L.l.intensity = 8 * (1 - k);
            }
        }
    }

    /// Visible rounds in flight. Every shot (player, AI, distant fighting) sends a short glowing streak from the muzzle
    /// to where the bullet landed. Purely visual: hits are still resolved instantly by the raycast. Tweak freely.
    public static class BulletFx
    {
        public static bool Enabled = true;
        public static float Speed = 480;          // visual flight speed, m/s (real rifle rounds ~760; slower reads better on screen)
        public static float Length = 2.6f;        // streak length, m
        public static float HeadWidth = .07f;    // width at the front of the streak, m
        public static float TailWidth = .01f;     // width at the back, m
        public static float MinLife = .05f;       // shortest time a streak stays on screen (point-blank shots), s
        public static Color Head = new Color(1f, .88f, .55f, 1f), Tail = new Color(1f, .7f, .3f, 0f);
        public static int Pool = 180;             // rounds in flight at once before the oldest is recycled
        public static float MinPixelAngle = .0024f; // streaks never get thinner on screen than this (radians, ~1.5 px at 720p): distant fire stays visible
    }

    public class SoundBank
    {
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        /// Recorded variants (Sonniss GDC bundles, cut by the audio pipeline) in Resources/Audio/<key>/; one is picked at random per play.
        readonly Dictionary<string, AudioClip[]> real = new Dictionary<string, AudioClip[]>();
        readonly List<AudioSource> pool = new List<AudioSource>(); int next;
        readonly List<AudioSource> loops = new List<AudioSource>();
        public Transform listener;

        /// Every key the game asks for. Specific keys fall back to a generic one (and finally the synthesised clip) when no recording is installed.
        static readonly string[] KEYS = { "rifle", "k98", "pistol", "p38", "smg", "mp40", "mg", "mg42", "browning", "bar", "cannon", "boom", "rumble", "drone", "siren", "pickup",
            "crack", "reload_bolt", "reload_mag", "reload_mg", "amb_wind", "amb_battle",
            "truck", "plane", "tank_ger", "tank_pol", "hooves", "snort", "step", "pain", "glass", "debris", "radio",
            "k98_far", "mp40_far", "mg42_far", "browning_far", "bar_far", "pistol_far", "cannon_far",
            "mg42_tail", "mg42_far_tail", "browning_tail", "browning_far_tail", "mp40_tail", "mp40_far_tail", "bar_tail", "bar_far_tail",
            // voice barks (roadmap A2): drop recordings into Resources/Audio/voice_<g|p>_<event>/ and they play; nothing plays until they exist
            "voice_g_contact", "voice_g_reload", "voice_g_grenade", "voice_g_pinned", "voice_g_mandown", "voice_g_moveup", "voice_g_hold", "voice_g_charge",
            "voice_p_contact", "voice_p_reload", "voice_p_grenade", "voice_p_pinned", "voice_p_mandown", "voice_p_moveup", "voice_p_hold", "voice_p_charge",
            // inside the hull (roadmap T29): what a tank crew hears. Synthesised stand-ins play until recordings are cut (see Init)
            "armor_pen", "armor_ricochet", "engine_damaged", "fire_loop", "repair_hammer", "repair_done" };
        static readonly Dictionary<string, string> FALLBACK = new Dictionary<string, string>
        { ["k98"] = "rifle", ["p38"] = "pistol", ["pistol"] = "rifle", ["mp40"] = "smg", ["smg"] = "mg", ["mg42"] = "mg", ["browning"] = "mg", ["bar"] = "browning" };
        /// Playback level per key for recorded clips. Gunshots are peak-normalised with the transient left intact (no limiting), so this
        /// table is the gun mix at the muzzle: full-power 7.92 mm rifles and MGs loudest, 9 mm SMG and pistols a few dB under. Distance
        /// does the rest (see Shot). The _far sets take the same level; the distance law already makes them quiet.
        static readonly Dictionary<string, float> LEVEL = new Dictionary<string, float>
        { ["k98"] = 1f, ["rifle"] = 1f, ["mg"] = 1f, ["mg42"] = 1f, ["browning"] = 1f, ["bar"] = 1f, ["mp40"] = .95f, ["smg"] = .95f, ["pistol"] = 1f, ["p38"] = 1f,
          ["k98_far"] = 1f, ["mg42_far"] = 1f, ["browning_far"] = 1f, ["bar_far"] = 1f, ["mp40_far"] = .9f, ["pistol_far"] = .85f, ["cannon_far"] = 1f, ["crack"] = 1f, ["boom"] = 1f, ["cannon"] = 1f, ["rumble"] = 1f,   // (boom / cannon / rumble had no entry and fell to .8; a crack at .6 was a whisper)
          ["mg42_tail"] = 1f, ["browning_tail"] = 1f, ["bar_tail"] = 1f, ["mp40_tail"] = 1f, ["mg42_far_tail"] = 1f, ["browning_far_tail"] = 1f, ["bar_far_tail"] = 1f, ["mp40_far_tail"] = 1f,
          ["reload_bolt"] = .45f, ["reload_mag"] = .45f, ["reload_mg"] = .45f, ["pickup"] = .5f, ["drone"] = .9f,
          ["step"] = .24f, ["radio"] = .3f, ["pain"] = .7f, ["glass"] = .65f, ["debris"] = .75f, ["snort"] = .55f,
          ["armor_pen"] = 1f, ["armor_ricochet"] = .8f, ["engine_damaged"] = .8f, ["fire_loop"] = .7f, ["repair_hammer"] = .6f, ["repair_done"] = .6f };
        static readonly HashSet<string> FLAT = new HashSet<string> { "pickup", "reload_bolt", "reload_mag", "reload_mg", "radio" };   // played at the listener, no 3D (the player's own steps come through own=true; NPC steps are 3D)

        // ---- buses & ducking (A5) ----
        // No AudioMixer asset yet, so the buses live here: every voice remembers its bus and base level, and Tick() re-applies
        // bus volume × ducking each frame. Your own shot ducks the world and the ambience for a quarter second; a shell within
        // 12 m ducks everything but explosions for 0.4 s. The *Vol fields are what an audio options menu will drive.
        public enum Bus { Own, World, Explo, Foley, Amb, Ui }
        public static float MasterVol = 1, OwnVol = 1, WorldVol = 1, ExploVol = 1, FoleyVol = 1, AmbVol = 1, UiVol = 1;
        static float BusVol(Bus b) => b == Bus.Own ? OwnVol : b == Bus.World ? WorldVol : b == Bus.Explo ? ExploVol : b == Bus.Foley ? FoleyVol : b == Bus.Amb ? AmbVol : UiVol;
        static Bus BusOf(string k, bool own) =>
            GUNS.Contains(k) || k == "cannon" ? (own ? Bus.Own : k == "cannon" ? Bus.Explo : Bus.World)
            : k == "boom" || k == "rumble" || k == "debris" || k == "glass" || k == "armor_pen" || k == "armor_ricochet" || k == "fire_loop" ? Bus.Explo
            : k == "repair_hammer" || k == "repair_done" || k == "engine_damaged" ? Bus.World
            : k == "step" || k == "crack" || k == "pain" || k == "snort" || k == "melee" || k.StartsWith("reload") ? Bus.Foley
            : k == "pickup" || k == "radio" ? Bus.Ui : Bus.Amb;
        Bus[] vBus = new Bus[0]; float[] vBase = new float[0]; readonly List<float> loopBase = new List<float>();
        float duckWorldT, duckAllT;
        const float DUCK_WORLD = .35f, DUCK_ALL = .4f;
        float DuckWorld => Mathf.Lerp(1, .35f, duckWorldT / DUCK_WORLD);   // -9 dB, releasing linearly: your own shot owns the mix for a third of a second
        float DuckAll => Mathf.Lerp(1, .35f, duckAllT / DUCK_ALL);        // -9 dB
        // master lives on the AudioListener (Tick) so engines, hooves, sirens and every other Follow() source obey it too, not just the pooled voices
        float Gain(Bus b) => BusVol(b) * (b == Bus.World || b == Bus.Amb ? DuckWorld : 1) * (b != Bus.Explo && b != Bus.Own ? DuckAll : 1);

        // ---- interior reverb (A10) ----
        // One reverb filter on the listener, faded in while the camera is inside an enterable building (Game tells us each frame).
        AudioReverbFilter rev; float inside, insideWant;
        public void AttachReverb(GameObject listenerGo) { var have = listenerGo.GetComponent<AudioReverbFilter>(); rev = have ? have : listenerGo.AddComponent<AudioReverbFilter>(); rev.reverbPreset = AudioReverbPreset.User; rev.dryLevel = 0; rev.roomHF = -900; rev.decayTime = 1.25f; rev.decayHFRatio = .6f; rev.reflectionsDelay = .012f; rev.reverbDelay = .03f; rev.room = -10000; rev.reflectionsLevel = -10000; rev.reverbLevel = -10000; rev.enabled = false; }
        public void Interior(bool on) => insideWant = on ? 1 : 0;

        // ---- occlusion (A6) ----
        // A line from the listener to the source through the world's own colliders: a building or wall soaks up most of the sound,
        // a low wall (sandbags, barricade) a little, a hill (the terrain mesh) a lot. Units' hitboxes are ignored. Returns 0..2.
        public static float Occlusion(Vector3 from, Vector3 p)
        {
            var d = p - from; float L = d.magnitude; if (L < 3) return 0;
            var hits = Physics.RaycastAll(from, d / L, L - .6f, ~0, QueryTriggerInteraction.Ignore); float occ = 0;
            foreach (var h in hits)
            {
                var go = h.collider.gameObject; if (go.GetComponent<UnitHit>()) continue; string n = go.name;
                if (n.StartsWith("Building") || n.StartsWith("Wall")) occ += 1; else if (n.StartsWith("Low")) occ += .35f; else if (h.collider is MeshCollider) occ += .8f;
            }
            return Mathf.Min(occ, 2);
        }

        public bool Has(string k) => real.ContainsKey(k) || clips.ContainsKey(k);

        public void Init()
        {
            foreach (var k in KEYS)
            {
                var a = Resources.LoadAll<AudioClip>("Audio/" + k);
                if (a != null && ONLY.TryGetValue(k, out var keep)) { var f = System.Array.FindAll(a, c => System.Array.IndexOf(keep, c.name) >= 0); if (f.Length > 0) a = f; }
                if (a != null && a.Length > 0) real[k] = a;
            }
            clips["rifle"] = Make("rifle", .35f, 900, 18, .9f);
            clips["mg"] = Make("mg", .18f, 1300, 30, .7f);
            clips["cannon"] = Make("cannon", .9f, 260, 6, 1f);
            clips["boom"] = Make("boom", 1.6f, 140, 3.2f, 1f);
            clips["pickup"] = Tone("pickup", .18f, 880, 1320);
            clips["siren"] = Siren();
            clips["rumble"] = Make("rumble", 3.2f, 70, 1.1f, 1f);   // distant artillery: all low end, long tail
            clips["drone"] = Drone();
            clips["melee"] = Make("melee", .22f, 2400, 16, .45f);   // blade / butt swing: a short airy swish, never a gunshot
            // T29 stand-ins until the Sonniss cuts land: a penetration is a hard low clang with a long ring, a ricochet a short bright ping,
            // the sledge on a track link a dry clank, "done" a softer double clank, the fire bed a dull broadband roar
            clips["armor_pen"] = Make("armor_pen", 1.4f, 520, 2.6f, 1f);
            clips["armor_ricochet"] = Make("armor_ricochet", .4f, 3600, 11, .7f);
            clips["repair_hammer"] = Make("repair_hammer", .16f, 2200, 28, .8f);
            clips["repair_done"] = Make("repair_done", .5f, 1500, 9, .6f);
            clips["fire_loop"] = Make("fire_loop", 2.5f, 900, .3f, .6f);
            clips["engine_damaged"] = Make("engine_damaged", 1.2f, 140, .6f, .7f);
            var root = new GameObject("Audio").transform;
            var flatCurve = AnimationCurve.Constant(0, 1, 1);
            for (int i = 0; i < 64; i++)   // recorded shots ring for ~1 s and distant ones are scheduled late, so automatic fire needs plenty of voices
            {
                var g = new GameObject("src"); g.transform.SetParent(root); var s = g.AddComponent<AudioSource>();
                // Shot() applies the distance law itself, so Unity's own rolloff is flat (it only pans)
                s.spatialBlend = 1; s.rolloffMode = AudioRolloffMode.Custom; s.SetCustomCurve(AudioSourceCurveType.CustomRolloff, flatCurve); s.maxDistance = 5000;
                s.playOnAwake = false; s.dopplerLevel = 0; pool.Add(s);
                g.AddComponent<AudioLowPassFilter>().cutoffFrequency = 22000;   // distance muffling
            }
            Debug.Log($"[FallWeiss] SoundBank: {real.Count} recorded sound sets ({string.Join(", ", real.Keys)})");
        }
        /// Sound sets where only some recordings are usable. Field artillery ("rumble") had six: only rumble_00 is a clean distant gun
        /// (sharp thump, 2/3 of its energy under 150 Hz, one event, smooth roll-off). 01 swells in over 0.3 s, 02 is a crackle of 7 hits,
        /// 03 is near-broadband hiss, and 04 / 05 start mid-waveform at 60-68 % level (an audible click; 04 is mostly sub-bass wobble).
        static readonly Dictionary<string, string[]> ONLY = new Dictionary<string, string[]> { ["rumble"] = new[] { "rumble_00" } };
        /// Generic keys (distant firefights ask for "mg") resolve to the recorded weapon instead of the old synthesised click.
        static readonly Dictionary<string, string> ALIAS = new Dictionary<string, string> { ["rifle"] = "k98", ["mg"] = "mg42", ["smg"] = "mp40" };
        AudioClip Pick(string k, out string used)
        {
            if (!real.ContainsKey(k) && ALIAS.TryGetValue(k, out var al) && real.ContainsKey(al)) k = al;
            for (string key = k; key != null; key = FALLBACK.TryGetValue(key, out var f) ? f : null)
                if (real.TryGetValue(key, out var a)) { used = key; return a[Random.Range(0, a.Length)]; }
            for (string key = k; key != null; key = FALLBACK.TryGetValue(key, out var f) ? f : null)
                if (clips.TryGetValue(key, out var c)) { used = key; return c; }
            used = null; return null;
        }
        /// Looping 2D bed (wind, distant battle). Only plays when a recording is installed.
        public void Ambience(string k, float vol)
        {
            if (!real.TryGetValue(k, out var a)) return;
            var s = loops.Find(l => l.name == k);
            if (!s) { var g = new GameObject(k); g.transform.SetParent(pool[0].transform.parent); s = g.AddComponent<AudioSource>(); s.loop = true; s.spatialBlend = 0; s.playOnAwake = false; s.priority = 0; loops.Add(s); }
            int li = loops.IndexOf(s); while (loopBase.Count <= li) loopBase.Add(vol); loopBase[li] = vol;
            s.volume = vol * Gain(Bus.Amb); if (!s.isPlaying) { s.clip = a[Random.Range(0, a.Length)]; s.time = Random.Range(0, s.clip.length * .9f); s.Play(); }
        }
        public void StopAmbience() { foreach (var l in loops) l.Stop(); }
        /// Mission change: silence every pooled voice (scheduled distant shots and echo tails used to ring on into the menu / next map) and forget bursts and ducks.
        public void StopAll()
        {
            StopAmbience(); foreach (var s in pool) if (s) s.Stop(); if (busyUntil != null) System.Array.Clear(busyUntil, 0, busyUntil.Length);
            bursts.Clear(); duckWorldT = duckAllT = 0;
        }
        /// Filtered noise burst with exponential decay — cheap stand-in for audio.js.
        static AudioClip Make(string name, float len, float cutoff, float decay, float vol)
        {
            int rate = 44100, n = (int)(len * rate); var data = new float[n]; float lp = 0, a = Mathf.Clamp01(cutoff * 2 * Mathf.PI / rate);
            var rnd = new System.Random(name.GetHashCode());
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate, noise = (float)(rnd.NextDouble() * 2 - 1); lp += (noise - lp) * a;
                float env = Mathf.Exp(-t * decay) * (t < .004f ? t / .004f : 1); float thump = Mathf.Sin(2 * Mathf.PI * 70 * t) * Mathf.Exp(-t * decay * 1.5f) * .6f;
                data[i] = Mathf.Clamp((lp * 3 + thump) * env * vol, -1, 1);
            }
            var c = AudioClip.Create(name, n, 1, rate, false); c.SetData(data, 0); return c;
        }
        static AudioClip Tone(string name, float len, float f0, float f1)
        {
            int rate = 44100, n = (int)(len * rate); var d = new float[n]; float ph = 0;
            for (int i = 0; i < n; i++) { float t = i / (float)n; ph += 2 * Mathf.PI * Mathf.Lerp(f0, f1, t) / rate; d[i] = Mathf.Sin(ph) * .4f * (1 - t); }
            var c = AudioClip.Create(name, n, 1, rate, false); c.SetData(d, 0); return c;
        }
        /// High-altitude bomber stream: beating engine drone that swells and fades.
        static AudioClip Drone()
        {
            int rate = 44100, n = rate * 12; var d = new float[n]; float p1 = 0, p2 = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate; p1 += 2 * Mathf.PI * 92 / rate; p2 += 2 * Mathf.PI * 96.5f / rate;
                float env = Mathf.Sin(Mathf.PI * t / 12) * .55f; d[i] = (Mathf.Sin(p1) * .5f + Mathf.Sin(p2) * .5f + Mathf.Sin(p1 * 2) * .2f) * env * .45f;
            }
            var c = AudioClip.Create("drone", n, 1, rate, false); c.SetData(d, 0); return c;
        }
        /// Jericho trumpet: a rising/falling wail with vibrato.
        static AudioClip Siren()
        {
            int rate = 44100, n = rate * 4; var d = new float[n]; float ph = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate, f = 420 + 520 * Mathf.Sin(Mathf.Min(1, t / 3.2f) * Mathf.PI * .5f) + Mathf.Sin(t * 38) * 14; ph += 2 * Mathf.PI * f / rate;
                float env = Mathf.Min(1, t * 2) * Mathf.Min(1, (4 - t) * 2); d[i] = (Mathf.Sin(ph) * .5f + Mathf.Sin(ph * 2.01f) * .2f) * env * .5f;
            }
            var c = AudioClip.Create("siren", n, 1, rate, false); c.SetData(d, 0); return c;
        }
        /// A sound that travels with an object (Stuka siren on the diving plane, engines on a flyover). The caller keeps the
        /// source and fades it with Fade(); it is destroyed with the object.
        /// Optional minD / maxD set the audible range (engines and hooves on the ground carry less far than aircraft); loops start at a
        /// random point so a column of identical vehicles doesn't drone in unison. With doppler &lt; 0 the default (.5) is kept.
        public AudioSource Follow(string k, Transform t, float vol = 1, bool loop = false, float minD = 25, float maxD = 650, float doppler = -1)
        {
            var c = Pick(k, out var used); if (c == null || !t) return null;
            var s = t.gameObject.AddComponent<AudioSource>(); s.clip = c; s.loop = loop; s.spatialBlend = 1; s.rolloffMode = AudioRolloffMode.Logarithmic;   // inverse distance: -6 dB per doubling past minD
            s.minDistance = minD; s.maxDistance = maxD; s.dopplerLevel = doppler < 0 ? .5f : doppler; s.playOnAwake = false; s.priority = 32;
            s.volume = (real.ContainsKey(used) && LEVEL.TryGetValue(used, out var lv) ? lv : .8f) * vol * WorldVol;   // engines / aircraft are battlefield sounds: the Battlefield slider used to miss them
            if (loop) s.time = Random.Range(0, c.length * .95f);
            s.Play(); return s;
        }
        /// Fades a Follow() source out over `secs` from the frame it is first called, then stops it.
        public static void Fade(AudioSource s, float dt, float secs)
        {
            if (!s || !s.isPlaying) return; s.volume -= dt / Mathf.Max(.01f, secs) * Mathf.Max(s.volume, .05f) * 2.2f;
            if (s.volume <= .005f) s.Stop();
        }
        // ---- distance model (real life, slightly compressed for a game) ----
        // Full level within the reference distance, then inverse distance (-6 dB per doubling), as sound spreads in the open.
        // Past FAR_AT metres a gun switches to its distant recording (less crack, more boom and echo); beyond ~80 m the air soaks up
        // the highs; and sound arrives at 343 m/s, so distant fire is heard after the flash. Your own weapon is dry, 2D and full level.
        static readonly HashSet<string> GUNS = new HashSet<string> { "rifle", "k98", "pistol", "p38", "smg", "mp40", "mg", "mg42", "browning", "bar" };
        const float FAR_AT = 60, SPEED = 343;
        static float Ref(string k) => GUNS.Contains(k) ? 10 : k.StartsWith("voice") ? 7 : k == "boom" || k == "cannon" ? 25 : k == "rumble" ? 80 : k == "armor_pen" ? 14 : k == "repair_hammer" || k == "repair_done" ? 8 : k == "drone" ? 60 : k == "siren" ? 30 : k == "crack" ? 4f : k == "step" ? 6 : 4;   // reference distances (full level inside): a rifle at 40 m is -12 dB, not -16; a shell at 60 m -7.6, not -12
        bool FarOf(string k, out string far)
        {
            for (string key = k; key != null; key = FALLBACK.TryGetValue(key, out var f) ? f : null)
                if (real.ContainsKey(key + "_far")) { far = key + "_far"; return true; }
            far = null; return false;
        }
        double[] busyUntil = new double[0];   // per voice: DSP time its sound (including one scheduled to start later) finishes
        int Voice(double end)
        {   // a voice that is free — not ringing and not holding a sound scheduled for later (speed of sound) — else the one finishing first
            if (busyUntil == null || busyUntil.Length != pool.Count) busyUntil = new double[pool.Count];
            double now = AudioSettings.dspTime; int best = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                int j = (next + i) % pool.Count;
                if (busyUntil[j] <= now) { next = j + 1; busyUntil[j] = end; return j; }
                if (busyUntil[j] < busyUntil[best]) best = j;
            }
            busyUntil[best] = end; return best;
        }
        // ---- automatic fire: a 90 ms PUNCH per round + the same recording's TAIL, handed over sample-accurately ----
        // Every round schedules its echo to start exactly where that round's own echo begins (70 ms in, 20 ms crossfade with the
        // punch) and ends the previous round's echo at the exact sample the new round starts. So a burst is a tight string of
        // punches and the last round's echo rings out seamlessly — no gap after the burst, no wash of overlapping tails.
        class Burst { public string key; public Vector3 p; public float last; public int tail = -1; public bool far, own; public double tailEnd; public float occ, occT = -9; }
        readonly List<Burst> bursts = new List<Burst>();
        /// The burst this round belongs to: same gun (base key) within 4 m — or, for the listener's own weapon, simply his own burst,
        /// wherever he has run to since the last round (keying his bursts on position made a sprinting player start a second burst
        /// every few metres, whose echo rang over the first: the "fires twice" sound).
        Burst FindBurst(string baseKey, Vector3 p, bool own) { foreach (var x in bursts) if (x.key == baseKey && x.own == own && (own || (x.p - p).sqrMagnitude < 16)) return x; return null; }
        const double HANDOVER = 130 / 44100.0 + .07;   // punch clips start 130 samples before the onset; the tail takes over 70 ms after it
        /// Call once per frame: forgets bursts that have been silent a while.
        public void Tick()
        {
            AudioListener.volume = MasterVol;
            for (int i = bursts.Count - 1; i >= 0; i--) if (Time.time - bursts[i].last > 3) bursts.RemoveAt(i);
            float dt = Time.unscaledDeltaTime; duckWorldT = Mathf.Max(0, duckWorldT - dt); duckAllT = Mathf.Max(0, duckAllT - dt);
            if (vBase.Length != pool.Count) { vBase = new float[pool.Count]; vBus = new Bus[pool.Count]; }
            double now = AudioSettings.dspTime;
            for (int i = 0; i < pool.Count; i++) if (busyUntil != null && i < busyUntil.Length && busyUntil[i] > now) pool[i].volume = vBase[i] * Gain(vBus[i]);
            for (int i = 0; i < loops.Count && i < loopBase.Count; i++) loops[i].volume = loopBase[i] * Gain(Bus.Amb);
            if (rev)
            {
                inside = Mathf.MoveTowards(inside, insideWant, dt * 3); bool on = inside > .02f;
                if (on) { rev.room = Mathf.Lerp(-10000, -700, inside); rev.reflectionsLevel = Mathf.Lerp(-10000, -1400, inside); rev.reverbLevel = Mathf.Lerp(-10000, -900, inside); }
                if (rev.enabled != on) rev.enabled = on;
            }
        }
        /// Live world voices right now (for the budget in Play).
        int Live() { int n = 0; double now = AudioSettings.dspTime; if (busyUntil != null) foreach (var b in busyUntil) if (b > now) n++; return n; }

        /// own = the listener's own weapon (the firing code says so): dry, centred, full level, close recording, no delay.
        /// pan = stereo position of a flat (2D) sound such as a footstep: -1 left … 1 right.
        public void Shot(string k, Vector3 p, float vol = 1, bool own = false, float pan = 0, float pitch = -1)
        {
            float d = own ? 0 : listener ? (listener.position - p).magnitude : 0;
            if (!real.ContainsKey(k) && ALIAS.TryGetValue(k, out var al) && real.ContainsKey(al)) k = al;
            // ONE recording per round, never both. The old 40–100 m crossfade played the close AND the distant recording of every round in
            // that band — two different punches and two echoes per shot, which is exactly a gun "firing twice". Now the close set is used out
            // to 70 m and the distant set beyond, and a burst keeps the set it started with until the range has clearly changed
            // (back in under 55 m, out past 85 m), so a gun never flips character mid-burst. The low-pass in Play() carries the transition.
            string fk = null; bool useFar = false; float occ = 0;
            bool gun = GUNS.Contains(k) || k == "cannon";
            if (own && gun) duckWorldT = DUCK_WORLD;                                   // your own shot sits on top of the battle
            if (!own && listener && (k == "boom" || k == "cannon") && d < 12) duckAllT = DUCK_ALL;   // a shell next to you flattens everything for a moment
            if (!own && listener && (gun || k == "boom" || k == "crack"))
            {
                var ob = gun ? FindBurst(k, p, false) : null;
                if (ob != null && Time.time - ob.occT < .3f) occ = ob.occ;              // an MG burst re-tests the line every 0.3 s, not every round
                else { occ = Occlusion(listener.position, p); if (ob != null) { ob.occ = occ; ob.occT = Time.time; } }
            }
            if (!own && FarOf(k, out fk))
            {
                var b = FindBurst(k, p, false);
                useFar = b != null && Time.time - b.last < 1.5f ? (b.far ? d > 55 : d > 85) : d > 70;
            }
            Round(useFar ? fk : k, k, p, vol, own, pitch > 0 ? pitch : FU.Rand(.98f, 1.02f), useFar, pan, occ);   // one pitch per round, shared by punch and tail so the handover lines up
        }
        void Round(string key, string baseKey, Vector3 p, float vol, bool own, float pitch, bool far, float pan, float occ)
        {
            if (!real.ContainsKey(key)) { Pick(key, out var u); if (u == null) return; key = u; baseKey = u.Replace("_far", ""); }
            int v = Play(key, p, vol, own, pitch, -1, pan, occ, out double start); if (v < 0) return;
            if (!real.ContainsKey(key + "_tail")) return;                              // bolt rifles / pistols: the clip carries its own echo
            var b = FindBurst(baseKey, p, own); if (b == null) bursts.Add(b = new Burst { key = baseKey, own = own });
            b.p = p; b.last = Time.time; b.far = far; b.occ = occ; b.occT = Time.time;
            // previous echo ends as this round fires — unless its voice was stolen for another sound meanwhile (then it isn't ours to cut)
            if (b.tail >= 0 && busyUntil[b.tail] == b.tailEnd) { pool[b.tail].SetScheduledEndTime(start); busyUntil[b.tail] = start; }
            b.tail = Play(key + "_tail", p, vol, own, pitch, start + HANDOVER / pitch, 0, occ, out _);
            b.tailEnd = b.tail >= 0 ? busyUntil[b.tail] : 0;
        }
        /// Plays one clip with the distance law. at >= 0 schedules it at that DSP time (tails); returns the voice index or -1.
        int Play(string want, Vector3 p, float vol, bool own, float pitch, double at, float pan, float occ, out double start)
        {
            start = 0;
            float d = own ? 0 : listener ? (listener.position - p).magnitude : 0;
            var c = Pick(want, out var used); if (c == null) return -1;
            bool rec = real.ContainsKey(used), flat = own || FLAT.Contains(used);
            string baseKey = used.Replace("_tail", "").Replace("_far", "");
            float gain = flat ? 1 : Mathf.Min(1, Ref(baseKey) / Mathf.Max(d, .01f));
            float ok = Mathf.Clamp01(occ); gain *= Mathf.Lerp(1, .3f, ok) * (occ > 1 ? .6f : 1);   // behind a wall: -10 dB, two walls -15 dB
            if (gain < .008f) return -1;                                               // under -42 dB: lost in the battle
            if (!flat && at < 0 && gain < .03f && Live() > 24) return -1;              // voice budget: a faint shot in a busy mix isn't worth a voice
            float delay = flat ? 0 : d / SPEED;
            start = at >= 0 ? at : AudioSettings.dspTime + delay;
            // a punch whose tail follows is scheduled too (one audio buffer ahead), so punch and tail run on the same sample clock
            bool exact = at >= 0 || real.ContainsKey(used + "_tail");
            if (exact) { AudioSettings.GetDSPBufferSize(out int buf, out _); start = System.Math.Max(start, AudioSettings.dspTime + (double)buf / AudioSettings.outputSampleRate); }
            if (!rec || used == "siren" || used == "pickup") pitch = used == "siren" || used == "pickup" ? 1 : FU.Rand(.95f, 1.05f);
            int vi = Voice(start + c.length / pitch + .05f); var s = pool[vi];
            s.transform.position = flat && listener ? listener.position : p;
            s.pitch = pitch;
            if (vBase.Length != pool.Count) { vBase = new float[pool.Count]; vBus = new Bus[pool.Count]; }
            vBus[vi] = BusOf(baseKey, own); vBase[vi] = (rec ? (LEVEL.TryGetValue(used, out var lv) ? lv : .8f) : used == "mg" ? .5f : .8f) * vol * gain;
            s.volume = vBase[vi] * Gain(vBus[vi]); s.spatialBlend = flat ? 0 : 1; s.panStereo = flat ? pan : 0;
            s.priority = own ? (baseKey == "step" ? 90 : 0) : (int)Mathf.Lerp(40, 230, Mathf.Clamp01(d / 600));  // near sounds win when voices run short
            var lp = s.GetComponent<AudioLowPassFilter>();
            if (lp) { float cut = flat || d < 80 ? 22000 : Mathf.Max(2500, 22000 * Mathf.Pow(80 / d, .8f)); if (ok > 0) cut = Mathf.Min(cut, Mathf.Lerp(22000, 1600, ok)); lp.cutoffFrequency = cut; }
            if (!exact && delay < .05f) s.PlayOneShot(c);
            else { s.Stop(); s.clip = c; s.PlayScheduled(start); s.SetScheduledEndTime(start + c.length / pitch + 1); }
            return vi;
        }
    }
}
