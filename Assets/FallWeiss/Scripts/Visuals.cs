// Fall Weiss — atmosphere & presentation: gradient sky + sun, drifting clouds, URP post-processing
// (ACES, bloom, grading, vignette/suppression, grain), shadow/AA quality, time-of-day (port of TOD/updateTod),
// rain (port of updateRain), instanced grass, unit icon sprites (port of iconMat) and the minimap (bakeMini).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FallWeiss
{
    public class Visuals
    {
        Camera cam; Light sun; Material skyMat; Volume vol; Vignette vig; ColorAdjustments grade; Bloom bloom; MaterialPropertyBlock cloudMpb;
        readonly List<Transform> clouds = new List<Transform>(); Transform cloudRoot;
        ParticleSystem rain; Mission M;
        Mesh grassMesh; readonly Dictionary<(int, int), List<Matrix4x4[]>> grass = new Dictionary<(int, int), List<Matrix4x4[]>>(); Material[] grassMats; RenderParams[] grassRP;
        const float GrassCell = 80; float grassDist = 140;
        Color top, hor, sunc; Vector3 sunDir; float fogNear, fogFar;
        public float rainI;

        public void Init(Camera c, Light s)
        {
            cam = c; sun = s;
            var sh = Shader.Find("FallWeiss/Sky"); if (!sh) { var r = Resources.Load<Material>("FallWeissSky"); if (r) sh = r.shader; }
            if (sh) skyMat = new Material(sh);
            // post-processing volume (replaces the template profile at runtime; the asset on disk is untouched)
            vol = Object.FindAnyObjectByType<Volume>(); if (vol && !vol.isGlobal) vol = null;   // a local (collider) volume would confine the whole post stack to its box
            if (!vol) { var g = new GameObject("Global Volume"); vol = g.AddComponent<Volume>(); vol.isGlobal = true; }
            var prof = ScriptableObject.CreateInstance<VolumeProfile>(); vol.profile = prof; vol.priority = 10;
            var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
            bloom = prof.Add<Bloom>(true); bloom.threshold.Override(1.35f); bloom.intensity.Override(.28f); bloom.scatter.Override(.5f);   // was 1.0 / .45 / .65: with the 6x sun disc that smeared a quarter-screen halo and blinded anyone facing the dawn (Mokra)
            grade = prof.Add<ColorAdjustments>(true); grade.postExposure.Override(.3f); grade.contrast.Override(12); grade.saturation.Override(-6);
            vig = prof.Add<Vignette>(true); vig.intensity.Override(.22f); vig.smoothness.Override(.45f); vig.color.Override(Color.black);
            var fg = prof.Add<FilmGrain>(true); fg.type.Override(FilmGrainLookup.Thin1); fg.intensity.Override(.18f);
            var smh = prof.Add<ShadowsMidtonesHighlights>(true); smh.shadows.Override(new Vector4(.98f, 1f, 1.04f, 0)); smh.highlights.Override(new Vector4(1.03f, 1f, .96f, 0));
            // camera & pipeline quality
            var cd = cam.GetUniversalAdditionalCameraData(); cd.renderPostProcessing = true;
            sun.shadowStrength = .82f; sun.shadowBias = .05f; sun.shadowNormalBias = .4f;
            SetQuality(1);
            MakeClouds(); MakeRain(); MakeGrassMesh();
        }

        /// Graphics preset (settings screen): 0 low, 1 medium (default), 2 high. Shadow reach and softness, cascade count, SMAA and how far grass is drawn.
        /// The old fixed setting was the HIGH preset on every machine (220 m soft shadows in four cascades, SMAA high, all 30 000 grass tufts every frame).
        static UniversalRenderPipelineAsset urpOrig; static float origShadow; static int origCascades;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() { urpOrig = null; }
        static void RestoreUrp() { if (urpOrig) { urpOrig.shadowDistance = origShadow; urpOrig.shadowCascadeCount = origCascades; } urpOrig = null; Application.quitting -= RestoreUrp; }
        public void SetQuality(int q)
        {
            q = Mathf.Clamp(q, 0, 2); var cd = cam.GetUniversalAdditionalCameraData();
            cd.antialiasing = q == 0 ? AntialiasingMode.None : AntialiasingMode.SubpixelMorphologicalAntiAliasing; cd.antialiasingQuality = q == 2 ? AntialiasingQuality.High : AntialiasingQuality.Medium;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {   // in the editor this is the project's own pipeline asset: remember its values and put them back when Play stops, so a preset never ends up saved to disk
                if (urpOrig == null) { urpOrig = urp; origShadow = urp.shadowDistance; origCascades = urp.shadowCascadeCount; Application.quitting += RestoreUrp; }
                urp.shadowDistance = q == 0 ? 80 : q == 1 ? 150 : 220; urp.shadowCascadeCount = q == 2 ? 4 : 2;
            }
            sun.shadows = q == 0 ? LightShadows.Hard : LightShadows.Soft;
            grassDist = q == 0 ? 90 : q == 1 ? 140 : 200;
        }

        // ---------------- per mission ----------------
        public void Apply(Mission m)
        {
            M = m; top = FU.Hex(m.skyTop); hor = FU.Hex(m.skyHor); sunc = FU.Hex(m.sun); fogNear = m.fogNear; fogFar = m.fogFar;
            sunDir = new Vector3(m.sunDir[0], m.sunDir[1], m.sunDir[2]).normalized;
            if (skyMat) { RenderSettings.skybox = skyMat; cam.clearFlags = CameraClearFlags.Skybox; } else cam.clearFlags = CameraClearFlags.SolidColor;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.ambientMode = AmbientMode.Trilight;
            bool gloom = m.city || m.name.Contains("Modlin") || m.name.Contains("Starachowice");
            grade.saturation.Override(gloom ? -18 : -6); grade.colorFilter.Override(gloom ? new Color(1, .96f, .9f) : new Color(1, .985f, .95f));
            foreach (var c in clouds) c.GetComponent<Renderer>().sharedMaterial = Mats.Transparent(m.city ? new Color(.72f, .68f, .64f, .75f) : new Color(1, 1, 1, .8f), cloudTex, false, true);
            BuildGrass(); Tod(0);
        }

        /// Port of updateTod(): sky, sun height/colour, ambient and fog drift across the mission clock.
        public void Tod(float t)
        {
            float ph = M != null && M.hasTod ? Mathf.Clamp01(t / (M.time * .85f)) : 0;
            Color tp = top, hz = hor, sc = sunc; float dy = sunDir.y, fk = 1;
            if (M != null && M.hasTod)
            {
                tp = Color.Lerp(FU.Hex(M.todFrom.top), FU.Hex(M.todTo.top), ph); hz = Color.Lerp(FU.Hex(M.todFrom.hor), FU.Hex(M.todTo.hor), ph); sc = Color.Lerp(FU.Hex(M.todFrom.sun), FU.Hex(M.todTo.sun), ph);
                dy = Mathf.Lerp(M.todFrom.dirY, M.todTo.dirY, ph); fk = Mathf.Lerp(M.todFrom.fog, M.todTo.fog, ph);
            }
            float hl = Mathf.Max(.01f, new Vector2(sunDir.x, sunDir.z).magnitude), f = 1 - dy * .6f;
            var d = new Vector3(sunDir.x / hl * f, dy, sunDir.z / hl * f).normalized;
            if (rainI > 0) { tp = Color.Lerp(tp, new Color(.4f, .43f, .47f), rainI * .6f); hz = Color.Lerp(hz, new Color(.55f, .58f, .6f), rainI * .5f); }
            if (skyMat) { skyMat.SetColor("_Top", tp); skyMat.SetColor("_Hor", hz); skyMat.SetColor("_SunC", sc * (1 - rainI * .8f)); skyMat.SetVector("_SunDir", d); }
            sun.transform.rotation = Quaternion.LookRotation(-d); sun.color = sc; sun.intensity = (.95f + dy * 1.6f) * (1 - rainI * .4f);
            RenderSettings.ambientSkyColor = Color.Lerp(tp, Color.white, .55f) * (1.05f + dy * .5f); RenderSettings.ambientEquatorColor = Color.Lerp(hz, Color.white, .25f) * (.95f + dy * .35f); RenderSettings.ambientGroundColor = FU.Hex(0x6a6248) * .75f;
            // fog: tinted a little toward the zenith so it never reads as a bright wall in the sun's colour; never starts inside rifle range.
            // Mokra's dawn (fogNear 102 x todFrom.fog .35) used to start at 36 m and bleach the whole battlefield beige.
            var fc = Color.Lerp(hz, tp, .22f); RenderSettings.fogColor = fc; cam.backgroundColor = hz;
            RenderSettings.fogStartDistance = Mathf.Max(80, fogNear * Mathf.Max(fk, .6f)); RenderSettings.fogEndDistance = Mathf.Max(520, fogFar * (.6f + .4f * fk) * (1 - rainI * .35f));
        }
        public void RtsFog(bool rts) { if (rts) { RenderSettings.fogStartDistance *= 3; RenderSettings.fogEndDistance *= 3; } }

        public string VolumeInfo()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                sb.Append($"[{v.gameObject.name} global={v.isGlobal} prio={v.priority} weight={v.weight} enabled={v.enabled} mine={(v == vol)} profile={(v.profile ? v.profile.name : "null")}: ");
                if (v.profile) foreach (var c in v.profile.components) sb.Append(c.GetType().Name + (c.active ? "" : "(off)") + " ");
                sb.Append("] ");
            }
            var cd = cam.GetUniversalAdditionalCameraData(); sb.Append($" camPost={cd.renderPostProcessing} aa={cd.antialiasing} layer={cd.volumeLayerMask.value}");
            return sb.ToString();
        }
        public string OtherVolumesOff() { int n = 0; foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (v != vol) { v.enabled = false; n++; } return n + " other volumes disabled"; }
        public string FxOff(string which)
        {
            foreach (var c in vol.profile.components) if (c.GetType().Name.ToLowerInvariant().StartsWith(which.ToLowerInvariant()) && which.Length > 0) { c.active = false; return c.GetType().Name + " off"; }
            return "no such override: " + which;
        }
        public void PostOff() { Post(false); }
        /// Post stack on for first person; off for the command map / deployment (cleaner planning view, and it sidesteps a glow the top-down camera picks up at the map edge — see roadmap T16).
        public void Post(bool on) { var cd = cam.GetUniversalAdditionalCameraData(); cd.renderPostProcessing = on; }
        public void BloomOff() { if (bloom != null) bloom.active = false; }
        public string CloudInfo() { int on = 0; float y = 0; foreach (var c in clouds) { if (c.GetComponent<Renderer>().enabled) on++; y = c.position.y; } return on + "/" + clouds.Count + " y=" + y.ToString("0"); }
        public void CloudsOff() { foreach (var c in clouds) c.GetComponent<Renderer>().enabled = false; }
        public void Suppression(float s) { if (vig != null) { vig.intensity.Override(.22f + s * .38f); } }

        public void Update(float dt, float gameT, bool showRain)
        {
            // clouds drift and wrap around the camera
            var cp = cam.transform.position;
            foreach (var c in clouds) { var p = c.position + new Vector3(2.2f, 0, .8f) * dt; if (p.x - cp.x > 700) p.x -= 1400; if (p.z - cp.z > 700) p.z -= 1400; if (p.x - cp.x < -700) p.x += 1400; if (p.z - cp.z < -700) p.z += 1400; c.position = p; }
            // clouds thin out as the camera climbs (command map / deployment): from the ground they are sky, from 150 m up they were a wall
            float ck = Mathf.Clamp01(1 - (cp.y - 60) / 90); cloudMpb ??= new MaterialPropertyBlock(); cloudMpb.SetColor("_Color", new Color(1, 1, 1, (M != null && M.city ? .75f : .8f) * ck));
            foreach (var c in clouds) { var r = c.GetComponent<Renderer>(); r.enabled = ck > .02f; if (r.enabled) r.SetPropertyBlock(cloudMpb); }
            // rain (port of rainIntensity / updateRain)
            float ri = 0;
            if (M != null && M.hasRain) { float ph = gameT / (M.time * .85f); if (ph > M.rainStart && ph < M.rainEnd) ri = Mathf.Sin(Mathf.PI * (ph - M.rainStart) / (M.rainEnd - M.rainStart)) * M.rainPeak; }
            rainI = ri;
            var em = rain.emission; em.rateOverTime = showRain ? 9000 * ri : 0; rain.transform.position = cp + Vector3.up * 22;
            // grass
            // only the 80 m cells within grassDist of the camera are drawn (the whole map used to be submitted every frame, most of it behind the fog)
            if (grassMats != null && grass.Count > 0)
            {
                int cx = Mathf.FloorToInt(cp.x / GrassCell), cz = Mathf.FloorToInt(cp.z / GrassCell), n = Mathf.CeilToInt(grassDist / GrassCell), k = 0; float lim = (grassDist + GrassCell * .71f) * (grassDist + GrassCell * .71f);
                for (int ix = cx - n; ix <= cx + n; ix++) for (int iz = cz - n; iz <= cz + n; iz++)
                {
                    float dx = (ix + .5f) * GrassCell - cp.x, dz = (iz + .5f) * GrassCell - cp.z; if (dx * dx + dz * dz > lim) continue;
                    if (grass.TryGetValue((ix, iz), out var batches)) { k = (ix * 7 + iz * 13) & 0x7fff; foreach (var b in batches) Graphics.RenderMeshInstanced(grassRP[k++ % grassRP.Length], grassMesh, 0, b); }   // colour fixed per cell, so it does not flicker as the camera crosses cells
                }
            }
        }

        // ---------------- clouds ----------------
        Texture2D cloudTex;
        void MakeClouds()
        {
            cloudTex = new Texture2D(256, 256, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[256 * 256]; var rnd = new System.Random(7);
            for (int i = 0; i < 40; i++)
            {
                float cx = 40 + (float)rnd.NextDouble() * 176, cy = 70 + (float)rnd.NextDouble() * 116, r = 20 + (float)rnd.NextDouble() * 40;
                for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++) { float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / r; if (d < 1) { int k = y * 256 + x; float a = (1 - d) * (1 - d) * .35f; px[k].a = Mathf.Min(1, px[k].a + a); } }
            }
            for (int i = 0; i < px.Length; i++) { float a = px[i].a; px[i] = new Color(1, 1, 1, Mathf.Clamp01(a * 1.2f)); }
            cloudTex.SetPixels(px); cloudTex.Apply();
            cloudRoot = new GameObject("Clouds").transform; var rr = new System.Random(3);
            for (int i = 0; i < 9; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.Destroy(q.GetComponent<Collider>()); q.transform.SetParent(cloudRoot);
                q.transform.position = new Vector3((float)rr.NextDouble() * 1400 - 700, 380 + (float)rr.NextDouble() * 120, (float)rr.NextDouble() * 1400 - 700);   // was 230–310 m: the command-map camera (150 m up) sat right under a 500 m quad and saw a white sheet
                q.transform.rotation = Quaternion.Euler(90, (float)rr.NextDouble() * 360, 0); float s = 380 + (float)rr.NextDouble() * 260; q.transform.localScale = new Vector3(s, s, 1);
                var r = q.GetComponent<Renderer>(); r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; clouds.Add(q.transform);
            }
        }

        // ---------------- rain ----------------
        void MakeRain()
        {
            var g = new GameObject("Rain"); rain = g.AddComponent<ParticleSystem>(); rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = rain.main; main.loop = true; main.startLifetime = 1.3f; main.startSpeed = 0; main.startSize = .035f; main.maxParticles = 12000; main.simulationSpace = ParticleSystemSimulationSpace.World; main.startColor = new Color(.75f, .8f, .86f, .5f);
            var vel = rain.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World; vel.x = new ParticleSystem.MinMaxCurve(.6f); vel.y = new ParticleSystem.MinMaxCurve(-34f); vel.z = new ParticleSystem.MinMaxCurve(.2f);
            var sh = rain.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(90, 1, 90);
            var em = rain.emission; em.rateOverTime = 0;
            var pr = g.GetComponent<ParticleSystemRenderer>(); pr.renderMode = ParticleSystemRenderMode.Stretch; pr.lengthScale = 1; pr.velocityScale = .035f; pr.sharedMaterial = Mats.Transparent(Color.white); pr.shadowCastingMode = ShadowCastingMode.Off;
            rain.Play();
        }

        // ---------------- grass (instanced tufts, port of the 15k cone field) ----------------
        void MakeGrassMesh()
        {
            var v = new List<Vector3>(); var t = new List<int>(); var n = new List<Vector3>();
            for (int k = 0; k < 3; k++)
            {
                float a = k * Mathf.PI / 3; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * .09f; int i = v.Count;
                v.Add(-d); v.Add(d); v.Add(new Vector3(d.x * .2f, .3f, d.z * .2f)); for (int j = 0; j < 3; j++) n.Add(Vector3.up);
                t.AddRange(new[] { i, i + 2, i + 1, i, i + 1, i + 2 });
            }
            grassMesh = new Mesh(); grassMesh.SetVertices(v); grassMesh.SetNormals(n); grassMesh.SetTriangles(t, 0); grassMesh.RecalculateBounds();
            grassMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2000);
            var cols = new[] { 0x4a6526, 0x56702c, 0x617733, 0x6e7536 }; grassMats = new Material[cols.Length]; grassRP = new RenderParams[cols.Length];
            for (int i = 0; i < cols.Length; i++) { grassMats[i] = new Material(Mats.Lit(FU.Hex(cols[i]))) { enableInstancing = true }; grassRP[i] = new RenderParams(grassMats[i]) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true, worldBounds = new Bounds(Vector3.zero, Vector3.one * 2000) }; }
        }
        void BuildGrass()
        {
            grass.Clear(); if (M.city) return;
            var rnd = new System.Random(M.seed); float H = M.half + 10; var list = new List<Matrix4x4>(); int target = Mathf.RoundToInt(30000 * Mathf.Clamp(World.AreaK, 1, 6));   // 30 000 was fixed whatever the map size: one tuft per 30 m² on the 960 m maps. Scales with area now (cells are culled by distance, so the draw cost does not)
            for (int i = 0; i < target * 2 && list.Count < target; i++)
            {
                float x = ((float)rnd.NextDouble() - .5f) * 2 * H, z = ((float)rnd.NextDouble() - .5f) * 2 * H;
                if (World.InBuilding(x, z, 1) || World.InWater(x, z) || World.Slope(x, z) > .5f || World.TrenchD(x, z) < 3 || (M.hasSea && World.H(x, z) < 3)) continue;
                bool road = false; foreach (var r in M.roads) if (FU.SegD(x, z, r.x1, r.z1, r.x2, r.z2) < r.w / 2 + 1) { road = true; break; }
                if (road) continue;
                float meadow = Mathf.PerlinNoise(x * .02f + M.seed, z * .02f); float s = (.6f + (float)rnd.NextDouble() * .6f) * (meadow > .6f ? 1.35f : 1);   // patches of taller growth where the meadow noise peaks
                list.Add(Matrix4x4.TRS(new Vector3(x, World.H(x, z) - .03f, z), Quaternion.Euler(0, (float)rnd.NextDouble() * 360, 0), new Vector3(s, s * (.8f + (float)rnd.NextDouble() * .6f), s)));
            }
            // bucket by 80 m cell so Update can cull by distance
            var cells = new Dictionary<(int, int), List<Matrix4x4>>();
            foreach (var m in list) { var p = m.GetPosition(); var key = (Mathf.FloorToInt(p.x / GrassCell), Mathf.FloorToInt(p.z / GrassCell)); if (!cells.TryGetValue(key, out var l)) cells[key] = l = new List<Matrix4x4>(); l.Add(m); }
            foreach (var kv in cells) { var bl = new List<Matrix4x4[]>(); for (int i = 0; i < kv.Value.Count; i += 1023) bl.Add(kv.Value.GetRange(i, Mathf.Min(1023, kv.Value.Count - i)).ToArray()); grass[kv.Key] = bl; }
        }

        // ---------------- unit icons (port of iconMat canvas drawing) ----------------
        static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
        public static Texture2D Icon(bool friend, string type)
        {
            string k = (friend ? "f" : "e") + type; if (icons.TryGetValue(k, out var tx) && tx) return tx;
            const int S = 64; var px = new Color[S * S]; Color col = friend ? FU.Hex(0x6bff6b) : FU.Hex(0xff5a4a);
            void P(int x, int y, Color c) { if (x >= 0 && y >= 0 && x < S && y < S) px[(S - 1 - y) * S + x] = c; }
            void Dot(float x, float y, float r) { for (int yy = (int)(y - r - 1); yy <= y + r + 1; yy++) for (int xx = (int)(x - r - 1); xx <= x + r + 1; xx++) if ((xx - x) * (xx - x) + (yy - y) * (yy - y) <= r * r) P(xx, yy, col); }
            void Line(float x0, float y0, float x1, float y1, float w = 2) { int n = (int)(Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0)) * 2) + 1; for (int i = 0; i <= n; i++) Dot(Mathf.Lerp(x0, x1, i / (float)n), Mathf.Lerp(y0, y1, i / (float)n), w); }
            for (int y = 14; y < 50; y++) for (int x = 6; x < 58; x++) P(x, y, new Color(.04f, .055f, .03f, .62f));
            Line(6, 14, 58, 14); Line(58, 14, 58, 50); Line(58, 50, 6, 50); Line(6, 50, 6, 14);
            switch (type)
            {
                case "rifle": case "mg": Line(10, 18, 54, 46); Line(54, 18, 10, 46); if (type == "mg") Dot(32, 32, 6); break;
                case "sniper": for (int i = 0; i < 60; i++) { float a = i / 60f * 6.283f; Dot(32 + Mathf.Cos(a) * 11, 32 + Mathf.Sin(a) * 11, 1.6f); } Line(32, 14, 32, 50, 1.6f); Line(8, 32, 56, 32, 1.6f); break;
                case "at": Line(14, 46, 32, 18); Line(32, 18, 50, 46); Line(50, 46, 14, 46); break;
                case "tank": for (int i = 0; i < 80; i++) { float a = i / 80f * 6.283f; Dot(32 + Mathf.Cos(a) * 17, 32 + Mathf.Sin(a) * 8, 1.8f); } for (int y = 30; y < 34; y++) for (int x = 28; x < 36; x++) P(x, y, col); break;
                default: Line(12, 46, 52, 18); break;
            }
            tx = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear }; tx.SetPixels(px); tx.Apply(); icons[k] = tx; return tx;
        }

        // ---------------- minimap (port of bakeMini) ----------------
        public static Texture2D BakeMinimap(Mission m)
        {
            const int S = 200; var tx = new Texture2D(S, S, TextureFormat.RGB24, false) { filterMode = FilterMode.Bilinear }; var px = new Color[S * S]; float H = m.half;
            for (int j = 0; j < S; j++) for (int i = 0; i < S; i++)
                {
                    float x = (i / (float)S - .5f) * 2 * H, z = (j / (float)S - .5f) * 2 * H, h = World.H(x, z);
                    float sh = Mathf.Clamp(.55f + (World.H(x - 3, z) - World.H(x + 3, z)) * .06f + (World.H(x, z - 3) - World.H(x, z + 3)) * .04f, .25f, 1);
                    Color c = m.city ? new Color(.35f, .35f, .35f) : new Color(.27f, .39f, .22f); if (World.InWater(x, z)) c = new Color(.2f, .35f, .5f);
                    px[j * S + i] = c * sh * 1.3f;
                }
            foreach (var b in World.obst)
            {
                if (b.low) continue; int x0 = (int)((b.x0 / H / 2 + .5f) * S), x1 = (int)((b.x1 / H / 2 + .5f) * S), z0 = (int)((b.z0 / H / 2 + .5f) * S), z1 = (int)((b.z1 / H / 2 + .5f) * S);
                for (int z = Mathf.Max(0, z0); z <= Mathf.Min(S - 1, Mathf.Max(z1, z0 + 1)); z++) for (int x = Mathf.Max(0, x0); x <= Mathf.Min(S - 1, Mathf.Max(x1, x0 + 1)); x++) px[z * S + x] = FU.Hex(0x25231e);
            }
            foreach (var r in m.roads) for (int k = 0; k <= 200; k++) { float x = Mathf.Lerp(r.x1, r.x2, k / 200f), z = Mathf.Lerp(r.z1, r.z2, k / 200f); int i = (int)((x / H / 2 + .5f) * S), j = (int)((z / H / 2 + .5f) * S); if (i >= 0 && j >= 0 && i < S && j < S) px[j * S + i] = FU.Hex(0x8a7c5e); }
            tx.SetPixels(px); tx.Apply(); return tx;
        }
    }
}
