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
            for (int i = 0; i < 320; i++)
            {
                var g = new GameObject("puff"); g.transform.SetParent(root); g.AddComponent<MeshFilter>().sharedMesh = puffMesh; var r = g.AddComponent<MeshRenderer>();
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; g.SetActive(false); puffs.Enqueue(g.transform);
            }
        }
        public void Clear() { foreach (var l in live) { if (l.line) l.line.enabled = false; else if (l.t) l.t.gameObject.SetActive(false); if (l.l) Object.Destroy(l.l.gameObject); } live.Clear(); }

        public void Tracer(Vector3 a, Vector3 b)
        {
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

    public class SoundBank
    {
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly List<AudioSource> pool = new List<AudioSource>(); int next;
        public Transform listener;

        public void Init()
        {
            clips["rifle"] = Make("rifle", .35f, 900, 18, .9f);
            clips["mg"] = Make("mg", .18f, 1300, 30, .7f);
            clips["cannon"] = Make("cannon", .9f, 260, 6, 1f);
            clips["boom"] = Make("boom", 1.6f, 140, 3.2f, 1f);
            clips["pickup"] = Tone("pickup", .18f, 880, 1320);
            clips["siren"] = Siren();
            var root = new GameObject("Audio").transform;
            for (int i = 0; i < 28; i++)
            {
                var g = new GameObject("src"); g.transform.SetParent(root); var s = g.AddComponent<AudioSource>();
                s.spatialBlend = 1; s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 4; s.maxDistance = 420; s.playOnAwake = false; s.dopplerLevel = 0; pool.Add(s);
            }
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
        public void Shot(string k, Vector3 p, float vol = 1)
        {
            if (!clips.TryGetValue(k, out var c)) return;
            if (listener && (listener.position - p).sqrMagnitude > 420 * 420) return;
            var s = pool[next++ % pool.Count]; s.transform.position = p; s.pitch = k == "siren" || k == "pickup" ? 1 : FU.Rand(.9f, 1.1f); s.volume = (k == "mg" ? .5f : .8f) * vol; s.spatialBlend = k == "pickup" ? 0 : 1; s.PlayOneShot(c);
        }
    }
}
