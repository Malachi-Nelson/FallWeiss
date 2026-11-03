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
                if (cam) { float cd = Vector3.Distance(cam.transform.position, r.a + r.dir * head), wmin = cd * BulletFx.MinPixelAngle; r.lr.endWidth = Mathf.Max(BulletFx.HeadWidth, wmin); r.lr.startWidth = Mathf.Max(BulletFx.TailWidth, wmin * .35f); }
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
            "k98_far", "mp40_far", "mg42_far", "browning_far", "bar_far", "pistol_far", "cannon_far" };
        static readonly Dictionary<string, string> FALLBACK = new Dictionary<string, string>
        { ["k98"] = "rifle", ["p38"] = "pistol", ["pistol"] = "rifle", ["mp40"] = "smg", ["smg"] = "mg", ["mg42"] = "mg", ["browning"] = "mg", ["bar"] = "browning" };
        /// Playback level per key for recorded clips. Gunshots are peak-normalised with the transient left intact (no limiting), so this
        /// table is the gun mix at the muzzle: full-power 7.92 mm rifles and MGs loudest, 9 mm SMG and pistols a few dB under. Distance
        /// does the rest (see Shot). The _far sets take the same level; the distance law already makes them quiet.
        static readonly Dictionary<string, float> LEVEL = new Dictionary<string, float>
        { ["k98"] = 1f, ["rifle"] = 1f, ["mg"] = .9f, ["mg42"] = .9f, ["browning"] = .9f, ["bar"] = .95f, ["mp40"] = .8f, ["smg"] = .8f, ["pistol"] = .75f, ["p38"] = .75f,
          ["k98_far"] = 1f, ["mg42_far"] = 1f, ["browning_far"] = 1f, ["bar_far"] = 1f, ["mp40_far"] = .9f, ["pistol_far"] = .85f, ["cannon_far"] = 1f, ["crack"] = .6f,
          ["reload_bolt"] = .45f, ["reload_mag"] = .45f, ["reload_mg"] = .45f, ["pickup"] = .5f, ["drone"] = .9f,
          ["step"] = .3f, ["radio"] = .3f, ["pain"] = .7f, ["glass"] = .65f, ["debris"] = .75f, ["snort"] = .55f };
        static readonly HashSet<string> FLAT = new HashSet<string> { "pickup", "reload_bolt", "reload_mag", "reload_mg", "step", "radio" };   // played at the listener, no 3D

        public bool Has(string k) => real.ContainsKey(k) || clips.ContainsKey(k);

        public void Init()
        {
            foreach (var k in KEYS) { var a = Resources.LoadAll<AudioClip>("Audio/" + k); if (a != null && a.Length > 0) real[k] = a; }
            clips["rifle"] = Make("rifle", .35f, 900, 18, .9f);
            clips["mg"] = Make("mg", .18f, 1300, 30, .7f);
            clips["cannon"] = Make("cannon", .9f, 260, 6, 1f);
            clips["boom"] = Make("boom", 1.6f, 140, 3.2f, 1f);
            clips["pickup"] = Tone("pickup", .18f, 880, 1320);
            clips["siren"] = Siren();
            clips["rumble"] = Make("rumble", 3.2f, 70, 1.1f, 1f);   // distant artillery: all low end, long tail
            clips["drone"] = Drone();
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
        AudioClip Pick(string k, out string used)
        {
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
            s.volume = vol; if (!s.isPlaying) { s.clip = a[Random.Range(0, a.Length)]; s.time = Random.Range(0, s.clip.length * .9f); s.Play(); }
        }
        public void StopAmbience() { foreach (var l in loops) l.Stop(); }
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
            s.volume = (real.ContainsKey(used) && LEVEL.TryGetValue(used, out var lv) ? lv : .8f) * vol;
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
        static float Ref(string k) => GUNS.Contains(k) ? 6 : k == "boom" || k == "cannon" ? 15 : k == "rumble" ? 40 : k == "drone" ? 60 : k == "siren" ? 30 : k == "crack" ? 1.5f : 4;
        bool FarOf(string k, out string far)
        {
            for (string key = k; key != null; key = FALLBACK.TryGetValue(key, out var f) ? f : null)
                if (real.ContainsKey(key + "_far")) { far = key + "_far"; return true; }
            far = null; return false;
        }
        AudioSource Voice()
        {   // a free voice if there is one, so a scheduled or ringing sound is not cut off; otherwise the oldest
            for (int i = 0; i < pool.Count; i++) { var v = pool[(next + i) % pool.Count]; if (!v.isPlaying) { next += i + 1; return v; } }
            return pool[next++ % pool.Count];
        }
        public void Shot(string k, Vector3 p, float vol = 1)
        {
            float d = listener ? (listener.position - p).magnitude : 0;
            bool own = d < 2.5f;                                                       // your own weapon, or a sound at your head
            string want = !own && d > FAR_AT && FarOf(k, out var fk) ? fk : k;
            var c = Pick(want, out var used); if (c == null) return;
            bool rec = real.ContainsKey(used), flat = own || FLAT.Contains(used);
            string baseKey = used.EndsWith("_far") ? used.Substring(0, used.Length - 4) : used;
            float gain = flat ? 1 : Mathf.Min(1, Ref(baseKey) / Mathf.Max(d, .01f));
            if (gain < .008f) return;                                                  // under -42 dB: lost in the battle
            var s = Voice(); s.transform.position = flat && listener ? listener.position : p;
            s.pitch = used == "siren" || used == "pickup" ? 1 : rec ? FU.Rand(.98f, 1.02f) : FU.Rand(.95f, 1.05f);   // small spread only: the same gun must sound like itself
            s.volume = (rec ? (LEVEL.TryGetValue(used, out var lv) ? lv : .8f) : used == "mg" ? .5f : .8f) * vol * gain; s.spatialBlend = flat ? 0 : 1;
            s.priority = own ? 0 : (int)Mathf.Lerp(40, 230, Mathf.Clamp01(d / 600));  // near sounds win when voices run short
            var lp = s.GetComponent<AudioLowPassFilter>(); if (lp) lp.cutoffFrequency = flat || d < 80 ? 22000 : Mathf.Max(2500, 22000 * Mathf.Pow(80 / d, .8f));
            float delay = flat ? 0 : d / SPEED;
            if (delay < .05f) s.PlayOneShot(c);
            else { s.Stop(); s.clip = c; s.PlayScheduled(AudioSettings.dspTime + delay); }
        }
    }
}
