// Fall Weiss — world generation: heightmap (port of baseHeight/fullHeight), terrain mesh, buildings,
// villages/city blocks, forests, water, bridges, fences, props and capture-point flags.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FallWeiss
{
    /// Axis-aligned obstacle (port of addCollider). Used for unit movement collision; a BoxCollider mirrors it for raycasts.
    public class Obst
    {
        public float x0, x1, z0, z1, y0, y1, h, hp, maxhp, ry0, ry1;
        public bool low, dead, rel, floor, ceil, nocover, soft, wire, smoking;
        public GameObject col, vis; public Bd bd; public string pk, kind; public int hex;
    }

    public class Tree { public float x, z, s; public int k; public bool alive = true; public long chunk; public Color c; }
    public class CapPoint
    {
        public string n; public float x, z, r, ap, pushT, best, stallT; public int owner, g, e; public bool locked, inside;
        public Renderer cloth, ring;
    }

    /// Batches boxes/cones/prisms into vertex-coloured meshes (one draw call per ~60k verts).
    public class MeshBatch
    {
        readonly List<Vector3> v = new List<Vector3>(); readonly List<Vector3> n = new List<Vector3>();
        readonly List<Color> c = new List<Color>(); readonly List<int> t = new List<int>();

        void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color col)
        {
            Vector3 nn = Vector3.Cross(b - a, d - a).normalized; int i = v.Count;
            v.Add(a); v.Add(b); v.Add(cc); v.Add(d); for (int k = 0; k < 4; k++) { n.Add(nn); c.Add(col); }
            t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }
        void Tri(Vector3 a, Vector3 b, Vector3 cc, Color col)
        {
            Vector3 nn = Vector3.Cross(b - a, cc - a).normalized; int i = v.Count;
            v.Add(a); v.Add(b); v.Add(cc); for (int k = 0; k < 3; k++) { n.Add(nn); c.Add(col); }
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }
        static Vector3 R(Vector3 o, float cs, float sn) => new Vector3(o.x * cs + o.z * sn, o.y, -o.x * sn + o.z * cs);

        /// Box centred at `ctr`, rotated `rotY` radians about Y.
        public void Box(Vector3 ctr, Vector3 size, float rotY, Color col)
        {
            col = col.linear; float cs = Mathf.Cos(rotY), sn = Mathf.Sin(rotY); Vector3 h = size * .5f;
            Vector3 P(float x, float y, float z) => ctr + R(new Vector3(x * h.x, y * h.y, z * h.z), cs, sn);
            Vector3 a = P(-1, -1, -1), b = P(1, -1, -1), cc = P(1, 1, -1), d = P(-1, 1, -1), e = P(-1, -1, 1), f = P(1, -1, 1), g = P(1, 1, 1), hh = P(-1, 1, 1);
            Quad(a, d, cc, b, col); Quad(f, g, hh, e, col); Quad(e, hh, d, a, col); Quad(b, cc, g, f, col); Quad(d, hh, g, cc, col); Quad(e, a, b, f, col);
        }
        // ---- generic transformed convex primitives (port of SolParts.add): winding is fixed so faces point away from `ctr`
        void TriO(Vector3 a, Vector3 b, Vector3 cc, Vector3 ctr, Color col)
        {
            var nn = Vector3.Cross(b - a, cc - a); if (nn.sqrMagnitude < 1e-12f) return;
            if (Vector3.Dot(nn, (a + b + cc) / 3 - ctr) < 0) Tri(a, cc, b, col); else Tri(a, b, cc, col);
        }
        static readonly Vector3[] BOXV = { new Vector3(-.5f, -.5f, -.5f), new Vector3(.5f, -.5f, -.5f), new Vector3(.5f, .5f, -.5f), new Vector3(-.5f, .5f, -.5f), new Vector3(-.5f, -.5f, .5f), new Vector3(.5f, -.5f, .5f), new Vector3(.5f, .5f, .5f), new Vector3(-.5f, .5f, .5f) };
        static readonly int[] BOXF = { 0, 3, 2, 1, 5, 6, 7, 4, 4, 7, 3, 0, 1, 2, 6, 5, 3, 7, 6, 2, 4, 0, 1, 5 };
        public void PBox(Vector3 ctr, Vector3 size, Quaternion q, Color col)
        {
            col = col.linear; var p = new Vector3[8]; for (int i = 0; i < 8; i++) p[i] = ctr + q * Vector3.Scale(BOXV[i], size);
            for (int f = 0; f < 6; f++) { var a = p[BOXF[f * 4]]; var b = p[BOXF[f * 4 + 1]]; var c2 = p[BOXF[f * 4 + 2]]; var d = p[BOXF[f * 4 + 3]]; TriO(a, b, c2, ctr, col); TriO(a, c2, d, ctr, col); }
        }
        /// Cylinder centred at `ctr` (three.js CylinderGeometry: height along local Y, radiusTop at +Y).
        public void PCyl(Vector3 ctr, float rt, float rb, float h, Quaternion q, Color col, int seg = 10, float sx = 1, float sz = 1)
        {
            col = col.linear; var top = ctr + q * new Vector3(0, h / 2, 0); var bot = ctr + q * new Vector3(0, -h / 2, 0);
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2 / seg, a1 = (i + 1) * Mathf.PI * 2 / seg;
                Vector3 P(float a, float r, float y) => ctr + q * new Vector3(Mathf.Sin(a) * r * sx, y, Mathf.Cos(a) * r * sz);
                Vector3 p0 = P(a0, rb, -h / 2), p1 = P(a1, rb, -h / 2), q0 = P(a0, rt, h / 2), q1 = P(a1, rt, h / 2);
                if (rt > .001f) { TriO(p0, p1, q1, ctr, col); TriO(p0, q1, q0, ctr, col); TriO(top, q0, q1, ctr, col); } else TriO(p0, p1, top, ctr, col);
                if (rb > .001f) TriO(bot, p0, p1, ctr, col);
            }
        }
        /// Ellipsoid (radii = scale), low-poly faceted.
        public void PSph(Vector3 ctr, Vector3 r, Quaternion q, Color col, int ws = 8, int hs = 6)
        {
            col = col.linear;
            Vector3 P(int i, int j) { float th = j * Mathf.PI / hs, ph = i * Mathf.PI * 2 / ws; return ctr + q * Vector3.Scale(new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)), r); }
            for (int j = 0; j < hs; j++) for (int i = 0; i < ws; i++)
                {
                    Vector3 a = P(i, j), b = P(i + 1, j), c2 = P(i + 1, j + 1), d = P(i, j + 1);
                    if (j > 0) TriO(a, b, c2, ctr, col); if (j < hs - 1) TriO(a, c2, d, ctr, col);
                }
        }
        static Vector3[] ICO;
        static readonly int[] ICOF = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
        public void PIco(Vector3 ctr, Vector3 s, Quaternion q, Color col)
        {
            if (ICO == null) { float t = (1 + Mathf.Sqrt(5)) / 2; ICO = new[] { new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0), new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t), new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) }; for (int i = 0; i < 12; i++) ICO[i] = ICO[i].normalized; }
            col = col.linear; for (int f = 0; f < 20; f++) TriO(ctr + q * Vector3.Scale(ICO[ICOF[f * 3]], s), ctr + q * Vector3.Scale(ICO[ICOF[f * 3 + 1]], s), ctr + q * Vector3.Scale(ICO[ICOF[f * 3 + 2]], s), ctr, col);
        }
        /// Terrain-hugging ellipse (ground patch): two rings, every vertex dropped onto the height function.
        public void PDisc(float cx, float cz, float rx, float rz, float rot, Color col, System.Func<float, float, float> hf, float lift = .06f, int seg = 12)
        {
            col = col.linear; float cs = Mathf.Cos(rot), sn = Mathf.Sin(rot);
            Vector3 P(float a, float k) { var o = R(new Vector3(Mathf.Sin(a) * rx * k, 0, Mathf.Cos(a) * rz * k), cs, sn); float x = cx + o.x, z = cz + o.z; return new Vector3(x, hf(x, z) + lift, z); }
            var c0 = new Vector3(cx, hf(cx, cz) + lift, cz);
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2 / seg, a1 = (i + 1) * Mathf.PI * 2 / seg;
                Vector3 m0 = P(a0, .5f), m1 = P(a1, .5f), o0 = P(a0, 1), o1 = P(a1, 1), below = Vector3.down * 50;
                TriO(c0, m0, m1, c0 + below, col); TriO(m0, o0, o1, m0 + below, col); TriO(m0, o1, m1, m0 + below, col);
            }
        }
        /// Truncated cone / cylinder standing on `bc`.
        public void Cyl(Vector3 bc, float r0, float r1, float h, int seg, Color col)
        {
            col = col.linear;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2 / seg, a1 = (i + 1) * Mathf.PI * 2 / seg;
                Vector3 p0 = bc + new Vector3(Mathf.Sin(a0) * r0, 0, Mathf.Cos(a0) * r0), p1 = bc + new Vector3(Mathf.Sin(a1) * r0, 0, Mathf.Cos(a1) * r0);
                Vector3 q0 = bc + new Vector3(Mathf.Sin(a0) * r1, h, Mathf.Cos(a0) * r1), q1 = bc + new Vector3(Mathf.Sin(a1) * r1, h, Mathf.Cos(a1) * r1);
                if (r1 < .001f) Tri(p0, p1, q0, col); else Quad(p0, p1, q1, q0, col);
                if (r1 >= .001f) Tri(bc + Vector3.up * h, q0, q1, col);
            }
        }
        /// Gable roof whose eaves sit at bc.y, ridge along the longer side.
        public void Gable(Vector3 bc, float w, float d, float rh, float rotY, Color col)
        {
            col = col.linear; float cs = Mathf.Cos(rotY), sn = Mathf.Sin(rotY);
            bool alongX = w >= d; float L = (alongX ? w : d) / 2 + .35f, S = (alongX ? d : w) / 2 + .45f;
            Vector3 P(float l, float y, float s) => bc + R(alongX ? new Vector3(l, y, s) : new Vector3(s, y, l), cs, sn);
            Vector3 a = P(-L, 0, -S), b = P(L, 0, -S), r0 = P(-L, rh, 0), r1 = P(L, rh, 0), c0 = P(-L, 0, S), c1 = P(L, 0, S);
            if (alongX) { Quad(a, r0, r1, b, col); Quad(c1, r1, r0, c0, col); Tri(a, c0, r0, col); Tri(b, r1, c1, col); }
            else { Quad(b, r1, r0, a, col); Quad(c0, r0, r1, c1, col); Tri(c0, a, r0, col); Tri(r1, b, c1, col); }
        }
        public int Verts => v.Count;
        public GameObject Build(string name, Transform parent, Material mat, bool shadows = true)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var m = new Mesh { indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            v.Clear(); n.Clear(); c.Clear(); t.Clear(); return go;
        }
    }

    public static partial class World
    {
        public static Mission M;
        public static Transform root;
        public static float? WL;
        public static readonly List<Obst> obst = new List<Obst>();
        public static readonly List<Tree> trees = new List<Tree>();
        public static readonly Dictionary<long, List<Tree>> treeGrid = new Dictionary<long, List<Tree>>();
        public static readonly List<CapPoint> points = new List<CapPoint>();
        static readonly List<Vector4> zones = new List<Vector4>(); // x, z, r, b   (+ height in zoneH)
        static readonly List<float> zoneH = new List<float>();
        public static Material vcMat;
        public static readonly List<Transform> blades = new List<Transform>();
        public static readonly List<Vector3> fires = new List<Vector3>(); public static readonly List<bool> bigFire = new List<bool>(); public static readonly List<Obst> fireObst = new List<Obst>();
        public static Transform ship; public static readonly List<Transform> shipGuns = new List<Transform>();

        const float GS = 2; static float[] HG; static int HN; static float HO; static int NSEED;

        // ---------------- noise (port of hash2 / vnoise / fbm) ----------------
        static float Hash2(float x, float z) { double nn = System.Math.Sin(x * 127.1 + z * 311.7 + NSEED * 74.7) * 43758.5453; return (float)(nn - System.Math.Floor(nn)); }
        public static float VNoise(float x, float z)
        {
            float xi = Mathf.Floor(x), zi = Mathf.Floor(z), xf = x - xi, zf = z - zi, u = xf * xf * (3 - 2 * xf), w = zf * zf * (3 - 2 * zf);
            float a = Hash2(xi, zi), b = Hash2(xi + 1, zi), c = Hash2(xi, zi + 1), d = Hash2(xi + 1, zi + 1);
            return a + (b - a) * u + (c - a) * w + (a - b - c + d) * u * w;
        }
        public static float Fbm(float x, float z, int o = 4) { float s = 0, a = .5f, f = 1; for (int i = 0; i < o; i++) { s += VNoise(x * f, z * f) * a; f *= 2; a *= .5f; } return s; }

        // ---------------- terrain shape ----------------
        public static float TrenchD(float x, float z)
        {
            float d = 1e9f; if (M.trenches == null) return d;
            foreach (var t in M.trenches) for (int i = 0; i + 3 < t.pts.Length; i += 2) d = Mathf.Min(d, FU.SegD(x, z, t.pts[i], t.pts[i + 1], t.pts[i + 2], t.pts[i + 3]));
            return d;
        }
        static float SeaEdge(float x, float z) => Mathf.Min(M.seaHx - Mathf.Abs(x), M.seaHz - Mathf.Abs(z)) + (Fbm(x * .03f + 4, z * .03f, 3) - .5f) * 14;
        public static int BridgeAt(float x, float z)
        {
            if (!M.hasRiver) return 0; float c = M.RiverC(x); if (Mathf.Abs(z - c) > M.rw * 1.5f) return 0;
            for (int i = 0; i < M.bridges.Length; i++) if (Mathf.Abs(x - M.bridges[i]) < 5.8f) return i + 1; return 0;
        }
        static float BaseHeight(float x, float z)
        {
            float A = M.relief, h = (Fbm(x * .007f, z * .007f, 4) - .47f) * 2.3f * A + (VNoise(x * .05f, z * .05f) - .5f) * A * .14f;
            if (M.hills != null) foreach (var hl in M.hills) { float d = FU.Hyp(x - hl.x, z - hl.z) / hl.r; if (d < 2.6f) h += hl.h * Mathf.Exp(-d * d * 1.3f); }
            if (M.hasSea) { h += 3.2f; h = Mathf.Lerp(h, -5, 1 - FU.Sm(-3, 9, SeaEdge(x, z))); }
            else { float e = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - M.half; if (e > -25) h += FU.Sm(-25, 150, e) * (35 + Fbm(x * .015f + 9, z * .015f, 3) * 110); }
            if (M.hasRiver)
            {
                h += M.rlevel + 4.5f; float d = Mathf.Abs(z - M.RiverC(x));
                h = Mathf.Lerp(h, M.rlevel + 1, 1 - FU.Sm(M.rw, M.rw * 3.4f, d)); h = Mathf.Lerp(h, M.rlevel - 1.8f, 1 - FU.Sm(M.rw * .45f, M.rw, d));
            }
            foreach (var r in M.rails) { float d = FU.SegD(x, z, r.x1, r.z1, r.x2, r.z2); h += 1.3f * (1 - FU.Sm(2.5f, 6.5f, d)) * (M.rails.Length > 1 ? .4f : 1); }
            if (M.trenches != null && M.trenches.Length > 0) { float dm = TrenchD(x, z); if (dm < 4) h += -1.8f * (1 - FU.Sm(.9f, 1.9f, dm)) + .5f * FU.Sm(1.9f, 2.6f, dm) * (1 - FU.Sm(2.6f, 3.8f, dm)); }
            return h;
        }
        /// Map editor preview: a mission's terrain height without building the world (noise, hills, river, sea, rails,
        /// trenches). Swaps the static mission / seed for the call and restores them, so it is safe from the editor.
        public static float PreviewHeight(Mission m, float x, float z)
        {
            var om = M; var os = NSEED; M = m; NSEED = m.seed;
            try { return BaseHeight(x, z); } finally { M = om; NSEED = os; }
        }
        public static float? WaterLevel(Mission m) => m.hasRiver ? m.rlevel : m.hasSea ? 0f : (float?)null;
        static float FullHeight(float x, float z)
        {
            float h = BaseHeight(x, z);
            for (int i = 0; i < zones.Count; i++) { var q = zones[i]; float d = FU.Hyp(x - q.x, z - q.y); if (d < q.z + q.w) h = Mathf.Lerp(h, zoneH[i], 1 - FU.Sm(q.z, q.z + q.w, d)); }
            if (M.hasRiver && BridgeAt(x, z) > 0) h = Mathf.Max(h, M.rlevel + 1.05f);
            return h;
        }
        static void AddZone(float x, float z, float r, float b) { zones.Add(new Vector4(x, z, r, b)); zoneH.Add(BaseHeight(x, z)); }

        public static float H(float x, float z)
        {
            float fx = Mathf.Clamp((x - HO) / GS, 0, HN - 1.001f), fz = Mathf.Clamp((z - HO) / GS, 0, HN - 1.001f);
            int ix = (int)fx, iz = (int)fz; float tx = fx - ix, tz = fz - iz; int i = iz * HN + ix;
            return Mathf.Lerp(Mathf.Lerp(HG[i], HG[i + 1], tx), Mathf.Lerp(HG[i + HN], HG[i + HN + 1], tx), tz);
        }
        public static float Slope(float x, float z) => FU.Hyp(H(x + 1.5f, z) - H(x - 1.5f, z), H(x, z + 1.5f) - H(x, z - 1.5f)) / 3;
        public static bool InWater(float x, float z) => WL.HasValue && H(x, z) < WL.Value - .35f;
        public static bool Blocked(float x, float z) => InWater(x, z) && BridgeAt(x, z) == 0;

        static bool ClearOfKey(float x, float z, float m)
        {
            foreach (var p in M.points) if (FU.Hyp(x - p.x, z - p.z) < m) return false;
            foreach (var s in M.spawn) if (FU.Hyp(x - s.x, z - s.z) < m + 8) return false;
            return true;
        }
        static bool NearRoad(float x, float z, float m) { foreach (var r in M.roads) if (FU.SegD(x, z, r.x1, r.z1, r.x2, r.z2) < r.w / 2 + m) return true; return false; }
        public static bool InBuilding(float x, float z, float m) { foreach (var b in obst) if (!b.dead && !b.low && !b.soft && x > b.x0 - m && x < b.x1 + m && z > b.z0 - m && z < b.z1 + m) return true; foreach (var bd in Interiors.bds) if (bd.alive && x > bd.x0 - m && x < bd.x1 + m && z > bd.z0 - m && z < bd.z1 + m) return true; return false; }
        static bool InRiver(float x, float z, float m) => M.hasRiver ? Mathf.Abs(z - M.RiverC(x)) < M.rw * 3.4f + m : M.hasSea ? SeaEdge(x, z) < 5 + m : false;
        static bool NearRail(float x, float z, float m) { foreach (var r in M.rails) if (FU.SegD(x, z, r.x1, r.z1, r.x2, r.z2) < m) return true; return false; }

        // ---------------- building table (port of KIND) ----------------
        class Kind { public float w, d, h, hp; public int[] wall, roof; public bool pitched; }
        static Kind K(float w, float d, float h, float hp, int[] wall, int[] roof, bool p) => new Kind { w = w, d = d, h = h, hp = hp, wall = wall, roof = roof, pitched = p };
        static readonly Dictionary<string, Kind> KIND = new Dictionary<string, Kind>
        {
            ["house"] = K(8, 7, 3.4f, 260, new[] { 0xe6dcc6, 0xefe6d0, 0xd8c9aa, 0xdccb8a, 0xc9d4d8, 0xbfcaa8, 0xe2c8b0 }, new[] { 0x8a3b2c, 0x6a3a2a, 0x7a4a30, 0x4a4a4e, 0x9a4a30 }, true),
            ["barn"] = K(14, 9, 4.6f, 300, new[] { 0x7a5a3c, 0x6a4c30 }, new[] { 0x4a3a2a, 0x3c3026 }, true),
            ["shed"] = K(5, 4, 2.6f, 140, new[] { 0x6a5238, 0x7a6244 }, new[] { 0x3c3026 }, true),
            ["barracks"] = K(26, 8, 4.2f, 540, new[] { 0xcbbf9e, 0xd8cdb0 }, new[] { 0x6b3a2a, 0x5a3426 }, true),
            ["villa"] = K(11, 9, 6.2f, 420, new[] { 0xdad0b6, 0xe2d8c2 }, new[] { 0x7a3b2c }, true),
            ["warehouse"] = K(24, 13, 7, 620, new[] { 0x9a8a76, 0x8a7c6a }, new[] { 0x4a4440 }, false),
            ["guardhouse"] = K(7, 7, 3.8f, 380, new[] { 0xcbbf9e }, new[] { 0x6b3a2a }, true),
            ["bunker"] = K(9, 6, 2.6f, 1800, new[] { 0x8a8a84 }, new[] { 0x77776f }, false),
            ["city"] = K(12, 16, 8, 520, new[] { 0xb8a58f, 0xc0b09a, 0xa89888, 0xc9b79e }, new[] { 0x4a4038, 0x555048 }, false),
            ["factory"] = K(40, 24, 10, 900, new[] { 0x8a5a48, 0x7d5242 }, new[] { 0x4a4a4a }, false),
            ["admin"] = K(40, 14, 12, 700, new[] { 0xc9b99a, 0xbfae8e }, new[] { 0x55504a }, false),
            ["stack"] = K(5, 5, 38, 1500, new[] { 0x7a4a3a }, new[] { 0x333333 }, false),
            ["tank"] = K(10, 10, 7, 600, new[] { 0x8a8d90 }, new[] { 0x777a7d }, false),
            ["boxcar"] = K(12, 3.4f, 3.6f, 200, new[] { 0x6a3a2a, 0x5a4a3a }, new[] { 0x2a2a2a }, false),
            ["ruin"] = K(12, 12, 4, 150, new[] { 0x9a8f84, 0xa89888 }, new[] { 0x3a3632 }, false),
            ["tenement"] = K(14, 22, 9, 560, new[] { 0xb8a58f, 0xc0b09a, 0xa89888 }, new[] { 0x4a4038 }, false),
            ["postoffice"] = K(36, 14, 7, 640, new[] { 0x8e4f3c, 0x854836 }, new[] { 0x3e3a36 }, false),
            ["gate"] = K(10, 24, 7, 760, new[] { 0x8a4e3a, 0x7e4634 }, new[] { 0x3e3a36 }, false),
            ["fortbarracks"] = K(34, 12, 7, 660, new[] { 0x9a5a42, 0x8e5039 }, new[] { 0x3e3a36 }, false),
            ["casemate"] = K(30, 12, 5, 1500, new[] { 0x77756c, 0x6c6a62 }, new[] { 0x55534c }, false),
            ["engineshed"] = K(36, 15, 8, 520, new[] { 0x7a4a3a, 0x6e4234 }, new[] { 0x3a3a3a }, false),
            ["sorting"] = K(30, 16, 6, 560, new[] { 0x9a8f7e, 0x8e8472 }, new[] { 0x45423e }, false),
            ["garage"] = K(24, 14, 5.5f, 480, new[] { 0x85796a, 0x7a7064 }, new[] { 0x45423e }, false),
            ["arsenal"] = K(28, 14, 6.5f, 700, new[] { 0x8a4a3a, 0x7e4434 }, new[] { 0x4a4a4a }, false),
        };

        class Spec { public float cx, cz, w, d, h; public int wall, roof; public bool pitched; public string k; public Obst col; }

        public static Obst AddObst(float x0, float x1, float z0, float z1, float h, bool low, float hp)
        {
            var b = new Obst { x0 = x0, x1 = x1, z0 = z0, z1 = z1, h = h, low = low, hp = hp, maxhp = hp }; obst.Add(b); return b;
        }

        // ---------------- build ----------------
        public static void Build(Mission m)
        {
            Clear(); M = m; NSEED = m.seed; WL = m.hasRiver ? m.rlevel : m.hasSea ? 0f : (float?)null;
            root = new GameObject("World — " + m.name).transform;
            var R = new Rng(m.seed); float Hh = m.half;
            int defend = 1 - m.attacker;

            foreach (var p in m.points) { points.Add(new CapPoint { n = p.n, x = p.x, z = p.z, r = p.r > 0 ? p.r : 14, owner = defend, inside = p.inside }); if (!(m.hasRiver && BridgeAt(p.x, p.z) > 0)) AddZone(p.x, p.z, 10, 26); }
            foreach (var s in m.spawn) AddZone(s.x, s.z, 24, 34);

            var specs = new List<Spec>();
            void SpecB(float cx, float cz, float w, float d, float h, int wall, int roof, bool pitched, float hp, string k)
            {
                if (Interiors.ENTER.TryGetValue(k, out var E) && !(k == "city" && R.Next() > .4f) && !(k == "house" && w < 5.6f))
                {   // enterable building (port of makeBd)
                    int fl = E.floors > 0 ? E.floors : E.type == "office" ? ((k == "barracks" || (k == "villa" && R.Next() < .5f)) ? 1 : 2) : 1;
                    Interiors.Make(cx, cz, w, d, h, wall, roof, pitched, k, E.type, E.hp, fl, E.thick, E.slits, (x, z, r, b) => AddZone(x, z, r, b)); return;
                }
                var col = AddObst(cx - w / 2, cx + w / 2, cz - d / 2, cz + d / 2, h + (pitched ? 2 : .6f), false, hp);
                specs.Add(new Spec { cx = cx, cz = cz, w = w, d = d, h = h, wall = wall, roof = roof, pitched = pitched, k = k, col = col });
                AddZone(cx, cz, Mathf.Max(w, d) / 2 + 1.5f, 5);
            }
            var lms = new List<(FLandmark l, Obst col)>();
            foreach (var l in m.landmarks)
            {
                if (l.k == "church") { lms.Add((l, AddObst(l.x - 5, l.x + 5, l.z - 8, l.z + 8, 9, false, 800))); AddZone(l.x, l.z, 11, 6); }
                else { lms.Add((l, AddObst(l.x - 2.5f, l.x + 2.5f, l.z - 2.5f, l.z + 2.5f, 8, false, 500))); AddZone(l.x, l.z, 6, 5); }
            }
            foreach (var b in m.build)
            {
                if (!KIND.TryGetValue(b.k, out var K)) K = KIND["house"];
                bool vary = b.k == "house" || b.k == "barn" || b.k == "shed" || b.k == "villa"; float f = vary ? .85f + R.Next() * .4f : 1, tall = (b.k == "house" && R.Next() < .3f) ? 2.4f : 0;
                SpecB(b.x, b.z, b.w > 0 ? b.w : K.w * f, b.d > 0 ? b.d : K.d * (vary ? .9f + R.Next() * .3f : 1), b.h > 0 ? b.h : K.h + tall, R.Pick(K.wall), R.Pick(K.roof), K.pitched, K.hp, b.k);
            }
            if (m.city)
            {
                for (float gx = -Hh + 15; gx < Hh - 10; gx += 44) for (float gz = -Hh + 15; gz < Hh - 10; gz += 40)
                    {
                        if (R.Next() < .1f) continue; float cx = gx + 15, cz = gz + 13;
                        bool bad = !ClearOfKey(cx, cz, 28) || Mathf.Abs(cz) > Hh - 24 || InBuilding(cx - 8, cz, 10) || InBuilding(cx + 9, cz, 10);
                        foreach (var l in lms) if (FU.Hyp(l.l.x - cx, l.l.z - cz) < 26) bad = true;
                        foreach (var l in m.lanes) if (FU.SegD(cx, cz, l.x1, l.z1, l.x2, l.z2) < l.w / 2 + 26) bad = true;
                        if (bad) continue;
                        bool ruined = R.Next() < .24f; float w = 12 + R.Next() * 3, d = 18 + R.Next() * 6; var K = ruined ? KIND["ruin"] : KIND["city"];
                        SpecB(cx - 8, cz, w, d, ruined ? 3 + R.Next() * 3 : 7 + R.Next() * 9, R.Pick(K.wall), R.Pick(K.roof), false, K.hp, ruined ? "ruin" : "city");
                        if (R.Next() < .85f) SpecB(cx + 9, cz, w - 1, d - 2, 6 + R.Next() * 9, R.Pick(KIND["city"].wall), R.Pick(KIND["city"].roof), false, 520, "city");
                    }
            }
            else foreach (var v in m.villages)
                {
                    int placed = 0, tries = 0;
                    while (placed < v.n && tries++ < 90)
                    {
                        float x = v.x + (R.Next() - .5f) * 2 * v.r, z = v.z + (R.Next() - .5f) * 2 * v.r; var K = KIND["house"]; float w = K.w * (.85f + R.Next() * .4f), d = K.d * (.9f + R.Next() * .3f);
                        if (!ClearOfKey(x, z, 26) || InBuilding(x, z, 9) || NearRoad(x, z, w / 2 + 3) || InRiver(x, z, w) || TrenchD(x, z) < 5 || (m.hasSea && SeaEdge(x, z) < 14) || NearRail(x, z, 9)) continue;
                        bool nearLm = false; foreach (var l in lms) if (FU.Hyp(l.l.x - x, l.l.z - z) < 16) nearLm = true; if (nearLm) continue;
                        SpecB(x, z, w, d, K.h + (R.Next() < .3f ? 2.4f : 0), R.Pick(K.wall), R.Pick(K.roof), true, K.hp, "house"); placed++;
                    }
                }

            // trees
            void AddT(float x, float z)
            {
                if (Mathf.Abs(x) > Hh + 40 || Mathf.Abs(z) > Hh + 40 || !ClearOfKey(x, z, 14) || NearRoad(x, z, 4) || InBuilding(x, z, 3) || InRiver(x, z, 4) || TrenchD(x, z) < 3.5f || NearRail(x, z, 8)) return;
                trees.Add(new Tree { x = x, z = z, s = .8f + R.Next() * .7f, k = R.Next() < .3f ? 1 : 0 });
            }
            foreach (var fo in m.forests) for (int i = 0; i < fo.n; i++) { float a = R.Next() * 6.28f, d = Mathf.Sqrt(R.Next()) * fo.r; AddT(fo.x + Mathf.Cos(a) * d, fo.z + Mathf.Sin(a) * d); }
            int lone = m.city ? 30 : 60; for (int i = 0; i < lone; i++) AddT((R.Next() - .5f) * 2 * (Hh + 20), (R.Next() - .5f) * 2 * (Hh + 20));
            foreach (var o in m.orchards) for (float x = o.x - o.w / 2; x <= o.x + o.w / 2; x += 6) for (float z = o.z - o.d / 2; z <= o.z + o.d / 2; z += 6)
                    {
                        if (!ClearOfKey(x, z, 14) || NearRoad(x, z, 4) || InBuilding(x, z, 3) || InRiver(x, z, 4)) continue;
                        trees.Add(new Tree { x = x + FU.Rand(-.4f, .4f), z = z + FU.Rand(-.4f, .4f), s = .5f + R.Next() * .12f, k = 1 });
                    }
            foreach (var t in trees) { long key = TKey(t.x, t.z); if (!treeGrid.TryGetValue(key, out var lst)) treeGrid[key] = lst = new List<Tree>(); lst.Add(t); }

            // low walls / fences become low obstacles before baking
            foreach (var s in m.segs) if (s.k == "fence" || s.k == "hedge") SegObst(s);

            BakeHeights();
            foreach (var b in obst) { if (b.rel) continue; b.y0 = H((b.x0 + b.x1) / 2, (b.z0 + b.z1) / 2); b.y1 = b.y0 + b.h; }

            vcMat = Mats.VC;
            BuildTerrain();
            Interiors.Finish(root);
            var mb = new MeshBatch();
            foreach (var s in specs) { var sb = new MeshBatch(); BuildingMesh(sb, s, R); s.col.vis = sb.Build("House " + s.k, root, vcMat); s.col.kind = s.k; }
            foreach (var (l, col) in lms) LandmarkModel(mb, l);
            Flush(mb, "Buildings");
            TreeMeshes(R);
            DressMap(mb, R, specs);
            Decor(mb, R);
            Flush(mb, "Decor");
            if (m.hasShip) Ship(mb);
            Water();
            foreach (var b in obst) MakeCollider(b);
            Flags();
        }

        static void Flush(MeshBatch mb, string name) { if (mb.Verts > 0) mb.Build(name, root, vcMat); }
        public static long TKey(float x, float z) => ((long)Mathf.FloorToInt(x / 10) << 32) ^ (uint)Mathf.FloorToInt(z / 10);

        static void SegObst(FSeg s)
        {
            float lx = s.x2 - s.x1, lz = s.z2 - s.z1, L = FU.Hyp(lx, lz); if (L < .1f) return;
            int n = Mathf.CeilToInt(L / 3);
            float th = s.k == "hedge" ? .9f : s.k == "stonewall" ? .7f : .3f, hh = s.k == "hedge" ? 1.5f : s.k == "stonewall" ? 1.2f : 1.1f;
            for (int i = 0; i < n; i++)
            {
                float t0 = i / (float)n, t1 = (i + 1) / (float)n, ax = s.x1 + lx * t0, az = s.z1 + lz * t0, bx = s.x1 + lx * t1, bz = s.z1 + lz * t1;
                var o = AddObst(Mathf.Min(ax, bx) - th / 2, Mathf.Max(ax, bx) + th / 2, Mathf.Min(az, bz) - th / 2, Mathf.Max(az, bz) + th / 2, hh, true, 60);
                o.dead = false;
            }
        }

        static void BakeHeights()
        {
            float S = M.half * 2 + 400; HN = Mathf.RoundToInt(S / GS) + 1; HO = -S / 2; HG = new float[HN * HN];
            for (int iz = 0; iz < HN; iz++) for (int ix = 0; ix < HN; ix++) HG[iz * HN + ix] = FullHeight(HO + ix * GS, HO + iz * GS);
        }

        static Color GroundColor(float x, float z, float h, float s, Color grass, Color[] fields)
        {
            float n = Fbm(x * .04f, z * .04f, 3);
            if (M.city)
            {
                var cc = FU.Hex(0x74706a) * (1 + (n - .45f) * .25f); cc.a = 1;
                if (Fbm(x * .1f + 5, z * .1f, 2) > .62f) cc = Color.Lerp(cc, FU.Hex(0x5a5446), .5f);
                foreach (var l in M.lanes) if (FU.SegD(x, z, l.x1, l.z1, l.x2, l.z2) < l.w / 2) cc = Color.Lerp(cc, FU.Hex(0x5e5a54), .6f);
                return cc;
            }
            Color c = grass * (1 + (n - .45f) * .35f + (h > 14 ? .05f : 0)); c.a = 1;
            if (M.name != "Westerplatte")
            {
                float a = .5f, fx = x * Mathf.Cos(a) + z * Mathf.Sin(a), fz = -x * Mathf.Sin(a) + z * Mathf.Cos(a);
                int ci = Mathf.FloorToInt(fx / 48), cj = Mathf.FloorToInt(fz / 62); float id = Hash2(ci, cj + 77);
                if (id > .52f && s < .3f && h < 20) { c = fields[(int)(id * 40) % fields.Length] * (1 - .08f * (Mathf.FloorToInt(fx / 3) & 1)); c.a = 1; }
            }
            if (s > .42f) c = Color.Lerp(c, FU.Hex(0x6e6a58), Mathf.Clamp((s - .42f) * 3, 0, .85f));
            if (WL.HasValue) { if (h < WL.Value + 1.4f) c = Color.Lerp(c, FU.Hex(M.hasSea ? 0xcdbf94 : 0x6a5a3c), M.hasSea ? .85f : .6f); else if (M.hasSea && h < WL.Value + 2.8f) c = Color.Lerp(c, FU.Hex(0xcdbf94), .3f); }
            foreach (var r in M.roads) { float d = FU.SegD(x, z, r.x1, r.z1, r.x2, r.z2); if (d < r.w / 2 + 1) c = Color.Lerp(c, FU.Hex(0x7a6c52), .85f * (1 - FU.Sm(r.w / 2 - 1, r.w / 2 + 1, d))); }
            foreach (var r in M.rails) { float d = FU.SegD(x, z, r.x1, r.z1, r.x2, r.z2); if (d < 3) c = Color.Lerp(c, FU.Hex(0x6b6558), .8f); }
            if (M.trenches != null && M.trenches.Length > 0) { float dm = TrenchD(x, z); if (dm < 3.4f) c = Color.Lerp(c, FU.Hex(0x4a3a28), .85f * (1 - FU.Sm(1.8f, 3.4f, dm))); }
            // edge of the playable area darkens
            float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - M.half - 20; if (edge > -40 && !M.hasSea) c = Color.Lerp(c, FU.Hex(0x34502e), .5f * FU.Sm(0, 120, edge) + .2f);
            return c;
        }

        static void BuildTerrain()
        {
            var verts = new Vector3[HN * HN]; var cols = new Color[HN * HN]; var tris = new int[(HN - 1) * (HN - 1) * 6];
            Color grass = FU.Hex(M.ground); var fields = new[] { 0x86913f, 0xa39a48, 0x72893a, 0x948046, 0x5f7a31, 0xb0a458 }; var fc = new Color[fields.Length]; for (int i = 0; i < fc.Length; i++) fc[i] = FU.Hex(fields[i]);
            int k = 0;
            for (int iz = 0; iz < HN; iz++) for (int ix = 0; ix < HN; ix++)
                {
                    int i = iz * HN + ix; float x = HO + ix * GS, z = HO + iz * GS, h = HG[i];
                    verts[i] = new Vector3(x, h, z);
                    float s = FU.Hyp(HG[Mathf.Min(HN - 1, iz + 1) * HN + ix] - HG[Mathf.Max(0, iz - 1) * HN + ix], HG[iz * HN + Mathf.Min(HN - 1, ix + 1)] - HG[iz * HN + Mathf.Max(0, ix - 1)]) / (2 * GS);
                    cols[i] = GroundColor(x, z, h, s, grass, fc).linear;
                    if (ix < HN - 1 && iz < HN - 1) { tris[k++] = i; tris[k++] = i + HN; tris[k++] = i + 1; tris[k++] = i + 1; tris[k++] = i + HN; tris[k++] = i + HN + 1; }
                }
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 }; mesh.MarkDynamic(); mesh.vertices = verts; mesh.colors = cols; mesh.triangles = tris; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            terrainMesh = mesh; tVerts = verts; tCols = cols; craterCount = 0; terrainDirty = colDirty = false;
            var go = new GameObject("Terrain"); go.transform.SetParent(root, false);
            var tmat = new Material(vcMat); tmat.SetFloat("_Detail", 1);
            go.AddComponent<MeshFilter>().sharedMesh = mesh; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = tmat; mr.shadowCastingMode = ShadowCastingMode.Off;
            terrainCol = go.AddComponent<MeshCollider>(); terrainCol.sharedMesh = mesh;
        }

        static void BuildingMesh(MeshBatch mb, Spec s, Rng R)
        {
            float y0 = H(s.cx, s.cz); Color wall = FU.Hex(s.wall), roof = FU.Hex(s.roof);
            if (s.k == "stack") { mb.Cyl(new Vector3(s.cx, y0 - 1, s.cz), 2.3f, 1.3f, s.h + 1, 12, wall); return; }
            if (s.k == "tank") { mb.Cyl(new Vector3(s.cx, y0 - 1, s.cz), s.w / 2, s.w / 2, s.h + 1, 16, wall); mb.Box(new Vector3(s.cx, y0 + s.h + .4f, s.cz), new Vector3(s.w * .5f, .8f, s.w * .5f), 0, roof); return; }
            mb.Box(new Vector3(s.cx, y0 + s.h / 2 - 1.25f, s.cz), new Vector3(s.w, s.h + 2.5f, s.d), 0, wall);
            // windows: dark strips on the long faces
            bool alongX = s.w >= s.d; int floors = Mathf.Max(1, Mathf.FloorToInt(s.h / 3.2f)); Color win = FU.Hex(0x2a2a2e);
            if (s.k != "bunker" && s.k != "ruin" && s.k != "boxcar" && s.k != "casemate")
                for (int f = 0; f < floors; f++)
                {
                    float wy = y0 + 1.6f + f * 3.1f; float L = alongX ? s.w : s.d; int nw = Mathf.Max(1, Mathf.FloorToInt(L / 3.4f));
                    for (int i = 0; i < nw; i++)
                    {
                        float o = -L / 2 + (i + .5f) * L / nw;
                        if (alongX) { mb.Box(new Vector3(s.cx + o, wy, s.cz + s.d / 2 + .02f), new Vector3(1, 1.3f, .06f), 0, win); mb.Box(new Vector3(s.cx + o, wy, s.cz - s.d / 2 - .02f), new Vector3(1, 1.3f, .06f), 0, win); }
                        else { mb.Box(new Vector3(s.cx + s.w / 2 + .02f, wy, s.cz + o), new Vector3(.06f, 1.3f, 1), 0, win); mb.Box(new Vector3(s.cx - s.w / 2 - .02f, wy, s.cz + o), new Vector3(.06f, 1.3f, 1), 0, win); }
                    }
                }
            if (s.pitched)
            {
                float rh = Mathf.Clamp(Mathf.Min(s.w, s.d) * (s.k == "barn" ? .3f : .42f), 1.3f, 3.8f);
                mb.Gable(new Vector3(s.cx, y0 + s.h, s.cz), s.w, s.d, rh, 0, roof);
                if (s.k != "shed" && s.k != "barn") mb.Box(new Vector3(s.cx + (alongX ? s.w * .22f : 0), y0 + s.h + rh * .45f, s.cz + (alongX ? 0 : s.d * .22f)), new Vector3(.6f, rh * .8f + .7f, .6f), 0, FU.Hex(0x6a5a4a));
                mb.Box(new Vector3(s.cx + FU.Rand(-s.w * .22f, s.w * .22f), y0 + 1.05f, s.cz + s.d / 2 + .04f), new Vector3(1.1f, 2.1f, .14f), 0, FU.Hex(0x4a3320));
            }
            else if (s.k == "ruin") { mb.Box(new Vector3(s.cx + s.w * .15f, y0 + s.h + .8f, s.cz), new Vector3(s.w * .6f, 1.6f, s.d * .5f), 0, FU.Hex(0x3a3632)); for (int i = 0; i < 4; i++) mb.Box(new Vector3(s.cx + FU.Rand(-s.w / 2, s.w / 2), y0 + s.h + .3f, s.cz + FU.Rand(-s.d / 2, s.d / 2)), new Vector3(FU.Rand(.6f, 1.6f), FU.Rand(.4f, 1), FU.Rand(.6f, 1.6f)), FU.Rand(0, 3), FU.Hex(0x4a443c)); }
            else if (s.k == "bunker") { mb.Box(new Vector3(s.cx, y0 + s.h + .25f, s.cz), new Vector3(s.w + .4f, .5f, s.d + .4f), 0, roof); mb.Box(new Vector3(s.cx, y0 + s.h * .62f, s.cz - s.d / 2 - .02f), new Vector3(s.w * .5f, .4f, .25f), 0, FU.Hex(0x0e0e0e)); mb.Box(new Vector3(s.cx, y0 + s.h * .62f, s.cz + s.d / 2 + .02f), new Vector3(s.w * .5f, .4f, .25f), 0, FU.Hex(0x0e0e0e)); }
            else { mb.Box(new Vector3(s.cx, y0 + s.h + .3f, s.cz), new Vector3(s.w + .6f, .6f, s.d + .6f), 0, roof); mb.Box(new Vector3(s.cx + FU.Rand(-2, 2), y0 + s.h + 1.1f, s.cz + FU.Rand(-3, 3)), new Vector3(1.6f, 1.2f, 1.6f), 0, FU.Hex(0x555048)); }
        }

        static void LandmarkMesh(MeshBatch mb, FLandmark l)
        {
            float y = H(l.x, l.z);
            if (l.k == "church")
            {
                Color w = FU.Hex(0xd8d0c0), r = FU.Hex(0x7a3b2c);
                mb.Box(new Vector3(l.x, y + 3.5f - 1, l.z + 2), new Vector3(10, 9, 12), 0, w); mb.Gable(new Vector3(l.x, y + 7.5f, l.z + 2), 10, 12, 4, 0, r);
                mb.Box(new Vector3(l.x, y + 8 - 1, l.z - 6), new Vector3(5, 18, 5), 0, w); mb.Cyl(new Vector3(l.x, y + 17, l.z - 6), 3, 0, 7, 8, FU.Hex(0x3a4a3e));
            }
            else if (l.k == "windmill")
            {
                mb.Cyl(new Vector3(l.x, y - 1, l.z), 2.6f, 1.8f, 10, 10, FU.Hex(0x8a6a4a)); mb.Cyl(new Vector3(l.x, y + 9, l.z), 2.2f, 0, 2.6f, 10, FU.Hex(0x4a3a2a));
                for (int i = 0; i < 4; i++) mb.Box(new Vector3(l.x + Mathf.Sin(i * Mathf.PI / 2 + .4f) * 4, y + 8 + Mathf.Cos(i * Mathf.PI / 2 + .4f) * 4, l.z - 2.4f), new Vector3(1.2f, 8, .15f), i * Mathf.PI / 2 + .4f, FU.Hex(0xd8d0c0));
            }
            else mb.Box(new Vector3(l.x, y + 4, l.z), new Vector3(4, 9, 4), 0, FU.Hex(0xd8d0c0));
        }

        // trees are grouped in 40 m chunks so individual trees can be blasted / crushed (port of killTree / blastTrees)
        static readonly Dictionary<long, GameObject> treeChunks = new Dictionary<long, GameObject>();
        static readonly Dictionary<long, List<Tree>> chunkTrees = new Dictionary<long, List<Tree>>();
        static readonly HashSet<long> dirtyChunks = new HashSet<long>();
        static long CKey(float x, float z) => ((long)Mathf.FloorToInt(x / 40) << 32) ^ (uint)Mathf.FloorToInt(z / 40);
        static void TreeMeshes(Rng R)
        {
            foreach (var t in trees)
            {
                t.c = t.k == 0 ? Color.HSVToRGB(.3f + R.Next() * .03f, .55f, .28f + R.Next() * .12f) : Color.HSVToRGB(.22f + R.Next() * .08f, .55f, .35f + R.Next() * .12f);
                t.chunk = CKey(t.x, t.z); if (!chunkTrees.TryGetValue(t.chunk, out var l)) chunkTrees[t.chunk] = l = new List<Tree>(); l.Add(t);
            }
            foreach (var k in chunkTrees.Keys) BuildChunk(k);
        }
        static void BuildChunk(long key)
        {
            if (treeChunks.TryGetValue(key, out var old) && old) Object.Destroy(old);
            var mb = new MeshBatch(); Color trunk = FU.Hex(0x4a3622);
            foreach (var t in chunkTrees[key])
            {
                if (!t.alive) continue; float y = H(t.x, t.z), s = t.s; mb.Cyl(new Vector3(t.x, y - .2f, t.z), .32f * s, .22f * s, 3 * s, 5, trunk);
                if (t.k == 0) { mb.Cyl(new Vector3(t.x, y + 1.7f * s, t.z), 2.3f * s, 0, 4.6f * s, 7, t.c); mb.Cyl(new Vector3(t.x, y + 4.8f * s, t.z), 1.5f * s, 0, 3.6f * s, 7, t.c); }
                else { mb.Cyl(new Vector3(t.x, y + 2.2f * s, t.z), 2.2f * s, 2.6f * s, 2 * s, 8, t.c); mb.Cyl(new Vector3(t.x, y + 4.2f * s, t.z), 2.6f * s, .4f * s, 2.6f * s, 8, t.c); }
            }
            treeChunks[key] = mb.Verts > 0 ? mb.Build("Trees", root, vcMat) : null;
        }
        public static void KillTree(Game g, Tree t)
        {
            if (t == null || !t.alive) return; t.alive = false; dirtyChunks.Add(t.chunk);
            if (treeGrid.TryGetValue(TKey(t.x, t.z), out var l)) l.Remove(t);
            float y = H(t.x, t.z); for (int i = 0; i < 5; i++) g.fx.Puff(new Vector3(t.x + FU.Rand(-1, 1), y + FU.Rand(1, 5), t.z + FU.Rand(-1, 1)), i % 2 == 1 ? new Color(.24f, .35f, .16f, .85f) : new Color(.35f, .27f, .19f, .85f), .6f, 1.1f, FU.Rand(.8f, 1.5f), new Vector3(FU.Rand(-3, 3), FU.Rand(1, 4), FU.Rand(-3, 3)), false, 9);
            coverDirty = true;
        }
        public static void BlastTrees(Game g, float x, float z, float r)
        {
            float R2 = r + 1.6f; int cx = Mathf.FloorToInt(x / 10), cz = Mathf.FloorToInt(z / 10), n = Mathf.CeilToInt(R2 / 10);
            for (int i = -n; i <= n; i++) for (int j = -n; j <= n; j++)
                {
                    if (!treeGrid.TryGetValue(((long)(cx + i) << 32) ^ (uint)(cz + j), out var a)) continue;
                    foreach (var t in a.ToArray()) if (FU.Hyp(t.x - x, t.z - z) < R2) KillTree(g, t);
                }
        }

        // ---------------- craters (port of crater): deform the heightmap + terrain mesh ----------------
        static Mesh terrainMesh; static Vector3[] tVerts; static Color[] tCols; static MeshCollider terrainCol; static int craterCount; static bool terrainDirty, colDirty; static float normT, colT;
        public static bool coverDirty;
        public static void Crater(float x, float z, float r, float depth)
        {
            if (terrainMesh == null || craterCount > 160 || InWater(x, z) || BridgeAt(x, z) > 0 || (M.hasRiver && InRiver(x, z, r)) || NearRoad(x, z, r + 1)) return; craterCount++;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((x - r - HO) / GS)), x1 = Mathf.Min(HN - 1, Mathf.CeilToInt((x + r - HO) / GS)), z0 = Mathf.Max(0, Mathf.FloorToInt((z - r - HO) / GS)), z1 = Mathf.Min(HN - 1, Mathf.CeilToInt((z + r - HO) / GS));
            for (int iz = z0; iz <= z1; iz++) for (int ix = x0; ix <= x1; ix++)
                {
                    float d = FU.Hyp(HO + ix * GS - x, HO + iz * GS - z); if (d >= r) continue; float k = 1 - d / r; int i = iz * HN + ix;
                    HG[i] -= depth * k * k; tVerts[i].y = HG[i]; float dk = 1 - .45f * k; tCols[i] = new Color(tCols[i].r * dk, tCols[i].g * dk, tCols[i].b * dk, 1);
                }
            terrainDirty = true; colDirty = true;
        }
        /// Applies pending terrain / tree changes (throttled like the original's normal recompute).
        public static void Tick(float dt)
        {
            normT -= dt; colT -= dt;
            if (terrainDirty && normT <= 0) { terrainMesh.vertices = tVerts; terrainMesh.colors = tCols; terrainMesh.RecalculateNormals(); terrainDirty = false; normT = .5f; }
            if (colDirty && colT <= 0 && terrainCol) { terrainCol.sharedMesh = null; terrainCol.sharedMesh = terrainMesh; colDirty = false; colT = 3; }
            if (dirtyChunks.Count > 0) { foreach (var k in dirtyChunks) BuildChunk(k); dirtyChunks.Clear(); }
        }

        static void Decor(MeshBatch mb, Rng R)
        {
            DecorPort(mb, R); GroundPatches(mb, R);
            // bridges
            if (M.hasRiver) foreach (var bx in M.bridges)
                {
                    float c = M.RiverC(bx), y = M.rlevel + 1.0f, len = M.rw * 3.1f;
                    mb.Box(new Vector3(bx, y, c), new Vector3(11.4f, .4f, len), 0, FU.Hex(0x6a5a40));
                    mb.Box(new Vector3(bx - 5.6f, y + .7f, c), new Vector3(.3f, 1.1f, len), 0, FU.Hex(0x4a3a28)); mb.Box(new Vector3(bx + 5.6f, y + .7f, c), new Vector3(.3f, 1.1f, len), 0, FU.Hex(0x4a3a28));
                }
        }

        static GameObject Place(string model, float x, float z, float rot, float yOff = 0)
        {
            var go = ModelLib.Spawn(model, root); go.transform.position = new Vector3(x, H(x, z) + yOff, z); go.transform.rotation = Quaternion.Euler(0, rot * Mathf.Rad2Deg, 0); return go;
        }
        static void LandmarkModel(MeshBatch mb, FLandmark l)
        {
            if (l.k == "church" && ModelLib.Has("church")) Place("church", l.x, l.z, 0);
            else if (l.k == "windmill" && ModelLib.Has("windmill")) { var w = Place("windmill", l.x, l.z, 0); var b = ModelLib.Find(w.transform, "blades"); if (b) blades.Add(b); }
            else LandmarkMesh(mb, l);
        }

        /// Port of dressMap(): sandbag lines and wire/tank traps in front of every objective, shell craters,
        /// ammo crates at the spawns, supply trucks, carts, haystacks, burnt-out wrecks and burning houses.
        static void DressMap(MeshBatch mb, Rng R, List<Spec> specs)
        {
            int att = M.attacker, def = 1 - att; var aSp = M.spawn[att]; var dSp = M.spawn[def];
            bool Bad(float x, float z, float m) => InRiver(x, z, 3) || InBuilding(x, z, m) || TrenchD(x, z) < 3 || InWater(x, z);
            foreach (var p in points)
            {
                float dx = aSp.x - p.x, dz = aSp.z - p.z, l = Mathf.Max(.01f, FU.Hyp(dx, dz)), ux = dx / l, uz = dz / l, rot = Mathf.Atan2(ux, uz), px = -uz, pz = ux;
                for (int j = -1; j <= 1; j++)
                {
                    float x = p.x + ux * 13 + px * j * 9.5f, z = p.z + uz * 13 + pz * j * 9.5f; if (Bad(x, z, 2)) continue;
                    var sv = Place("sandbags", x, z, rot); var o = AddObst(x - 2.6f, x + 2.6f, z - 2.6f, z + 2.6f, 1.1f, true, 120); Shrink(o, x, z, ux, uz, 4, .55f); o.vis = sv;
                }
                for (int j = -2; j <= 2; j++)
                {
                    float x = p.x + ux * 25 + px * j * 8, z = p.z + uz * 25 + pz * j * 8 + FU.Rand(-2, 2); if (Bad(x, z, 2)) continue;
                    if (j % 2 == 0) { var wv = Place("wire", x, z, rot); var o = AddObst(x - 3.5f, x + 3.5f, z - 3.5f, z + 3.5f, 1.2f, true, 60); Shrink(o, x, z, ux, uz, 3.5f, .5f); o.wire = true; o.vis = wv; }
                    else if (R.Next() < .6f) Place("tanktrap", x, z, R.Next() * 3);
                }
                for (int i = 0; i < 7; i++)
                {
                    float a = R.Next() * 6.28f, d = 8 + R.Next() * 28, x = p.x + Mathf.Cos(a) * d, z = p.z + Mathf.Sin(a) * d; if (InRiver(x, z, 3) || TrenchD(x, z) < 3 || InBuilding(x, z, 1)) continue;
                    float r = 1.8f + R.Next() * 2; mb.Cyl(new Vector3(x, H(x, z) - .02f, z), r, r * .85f, .1f, 14, FU.Hex(0x2c261c)); mb.Cyl(new Vector3(x, H(x, z) + .02f, z), r * .55f, r * .4f, .1f, 10, FU.Hex(0x221d16));
                }
            }
            for (int s = 0; s < 2; s++)
            {
                var sp = M.spawn[s]; float side = (M.spawn[1 - s].z > sp.z) ? 9 : -9;
                for (int k = 0; k < 5; k++) { float x = sp.x + (k - 2) * 3, z = sp.z + side; mb.Box(new Vector3(x, H(x, z) + .6f, z), new Vector3(1.6f, 1.2f, 1.6f), 0, FU.Hex(0x6b5a3a)); mb.Box(new Vector3(x, H(x, z) + 1.22f, z), new Vector3(1.62f, .05f, .3f), 0, FU.Hex(0xcaa83a)); }
            }
            float fwd = M.spawn[def].z > aSp.z ? 1 : -1;
            for (int i = 0; i < 4; i++) { float x = aSp.x + (i % 2 == 1 ? 18 : -18) + FU.Rand(-3, 3), z = aSp.z - fwd * (14 + (i >> 1) * 10); if (!InWater(x, z) && !InBuilding(x, z, 3)) Place("truck_" + att, x, z, fwd > 0 ? 0 : Mathf.PI); }
            for (int i = 0; i < 2; i++) { float x = dSp.x + FU.Rand(-20, 20), z = dSp.z + FU.Rand(-10, 10); if (!InWater(x, z) && !InBuilding(x, z, 3)) Place("cart", x, z, R.Next() * 6); }
            if (!M.city) for (int i = 0; i < 14; i++)
                {
                    float x = (R.Next() - .5f) * 2 * M.half, z = (R.Next() - .5f) * 2 * M.half;
                    if (!ClearOfKey(x, z, 20) || NearRoad(x, z, 3) || InBuilding(x, z, 4) || InRiver(x, z, 3) || Slope(x, z) > .3f || TrenchD(x, z) < 4) continue;
                    Place("haystack", x, z, R.Next() * 6);
                }
            for (int i = 0; i < M.wrecks; i++)
            {
                var p = points[(int)(R.Next() * points.Count) % points.Count]; float a = R.Next() * 6.28f, d = 12 + R.Next() * 24, x = p.x + Mathf.Cos(a) * d, z = p.z + Mathf.Sin(a) * d;
                if (Bad(x, z, 3)) continue; Place("wreck", x, z, R.Next() * 6); fires.Add(new Vector3(x, H(x, z) + 1.4f, z)); bigFire.Add(false); fireObst.Add(null);
            }
            var houses = new List<Spec>(); foreach (var sp in specs) if (sp.h < 12 && sp.col.hp < 1000 && sp.k != "bunker") houses.Add(sp);
            for (int i = 0; i < M.fires && houses.Count > 0; i++) { int k = (int)(R.Next() * houses.Count) % houses.Count; var hs = houses[k]; houses.RemoveAt(k); fires.Add(new Vector3(hs.cx, H(hs.cx, hs.cz) + hs.h + 1.5f, hs.cz)); bigFire.Add(true); fireObst.Add(hs.col); hs.col.smoking = true; hs.col.hp = Mathf.Min(hs.col.hp, hs.col.maxhp * .6f); }
        }
        static void Shrink(Obst o, float x, float z, float ux, float uz, float half, float th)
        {   // orient the obstacle box across the attack axis (axis-aligned approximation)
            if (Mathf.Abs(ux) > Mathf.Abs(uz)) { o.x0 = x - th; o.x1 = x + th; o.z0 = z - half; o.z1 = z + half; } else { o.x0 = x - half; o.x1 = x + half; o.z0 = z - th; o.z1 = z + th; }
            o.y0 = H(x, z); o.y1 = o.y0 + o.h;
        }

        /// Westerplatte: the battleship Schleswig-Holstein offshore (port of addShip).
        static void Ship(MeshBatch mb)
        {
            float sx = M.shipX, sz = M.shipZ, y = (WL ?? 0) + 1.2f; float rot = Mathf.Atan2(-sx, -sz) + Mathf.PI / 2; // broadside to the peninsula
            var g = new GameObject("Schleswig-Holstein").transform; g.SetParent(root, false); g.position = new Vector3(sx, y, sz); g.rotation = Quaternion.Euler(0, rot * Mathf.Rad2Deg, 0); ship = g;
            var b = new MeshBatch();
            b.Box(new Vector3(0, 2, 0), new Vector3(14, 6, 80), 0, FU.Hex(0x6b7078)); b.Box(new Vector3(0, 5.5f, 0), new Vector3(10, 3, 60), 0, FU.Hex(0x5d636a)); b.Box(new Vector3(0, 9, 4), new Vector3(8, 6, 14), 0, FU.Hex(0x5a6068));
            b.Box(new Vector3(0, 14, 4), new Vector3(3, 8, 3), 0, FU.Hex(0x4a4f55)); b.Box(new Vector3(0, 17, -8), new Vector3(2, 14, 2), 0, FU.Hex(0x42474c)); b.Box(new Vector3(0, 2.5f, 41), new Vector3(8, 4, 4), .78f, FU.Hex(0x6b7078));
            var hull = b.Build("Hull", g, vcMat); hull.transform.localPosition = Vector3.zero;
            foreach (float zz in new[] { -24f, 26f })
            {
                var t = new GameObject("Turret").transform; t.SetParent(g, false); t.localPosition = new Vector3(0, 7.5f, zz);
                var tb = new MeshBatch(); tb.Box(Vector3.zero, new Vector3(6, 3, 6), 0, FU.Hex(0x5a6068)); tb.Box(new Vector3(-1, .3f, 5), new Vector3(.6f, .6f, 9), 0, FU.Hex(0x2a2e33)); tb.Box(new Vector3(1, .3f, 5), new Vector3(.6f, .6f, 9), 0, FU.Hex(0x2a2e33)); tb.Build("Gun", t, vcMat);
                var target = new Vector3(0, t.position.y, 0); t.rotation = Quaternion.LookRotation(target - t.position); shipGuns.Add(t);
            }
        }

        static void Water()
        {
            if (!WL.HasValue) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane); go.name = "Water"; go.transform.SetParent(root, false);
            float S = (M.half * 2 + 400) / 10f; go.transform.localScale = new Vector3(S, 1, S); go.transform.position = new Vector3(0, WL.Value, 0);
            Object.Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = Mats.Lit(FU.Hex(M.hasSea ? 0x2f5f86 : 0x3d6f8f), .9f);
        }

        static void MakeCollider(Obst b)
        {
            if (b.col || b.dead) return;
            var go = new GameObject(b.rel ? "Wall " + b.pk : b.low ? "Low" : "Building"); go.transform.SetParent(root, false);
            if (b.rel) { go.transform.position = new Vector3((b.x0 + b.x1) / 2, (b.y0 + b.y1) / 2, (b.z0 + b.z1) / 2); go.AddComponent<BoxCollider>().size = new Vector3(b.x1 - b.x0, b.y1 - b.y0, b.z1 - b.z0); }
            else { go.transform.position = new Vector3((b.x0 + b.x1) / 2, b.y0 - 1 + (b.h + 1) / 2, (b.z0 + b.z1) / 2); go.AddComponent<BoxCollider>().size = new Vector3(b.x1 - b.x0, b.h + 1, b.z1 - b.z0); }
            b.col = go;
        }
        public static void MakeColliderPublic(Obst b) => MakeCollider(b);
        /// Port of inBuildingG: walls / solid buildings block, but room interiors, floors and lintels don't.
        public static bool InBuildingG(float x, float z, float m)
        {
            foreach (var b in obst)
            {
                if (b.dead || b.soft || b.floor) continue; if (b.rel && (b.pk == "roof" || b.pk == "lintel" || b.ry0 > 1.5f)) continue;
                if (x > b.x0 - m && x < b.x1 + m && z > b.z0 - m && z < b.z1 + m) return true;
            }
            return false;
        }

        static void Flags()
        {
            var ringMesh = RingMesh(.92f, 1, 48);
            foreach (var p in points)
            {
                float y = H(p.x, p.z);
                var g = new GameObject("Objective " + p.n).transform; g.SetParent(root, false); g.position = new Vector3(p.x, y, p.z);
                var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Object.Destroy(pole.GetComponent<Collider>()); pole.transform.SetParent(g, false);
                pole.transform.localPosition = new Vector3(0, 4, 0); pole.transform.localScale = new Vector3(.24f, 4, .24f); pole.GetComponent<Renderer>().sharedMaterial = Mats.Lit(0x222222);
                var cloth = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.Destroy(cloth.GetComponent<Collider>()); cloth.transform.SetParent(g, false);
                cloth.transform.localPosition = new Vector3(1.2f, 7, 0); cloth.transform.localScale = new Vector3(2.4f, 1.5f, .05f); p.cloth = cloth.GetComponent<Renderer>();
                var ring = new GameObject("Ring"); ring.transform.SetParent(g, false); ring.transform.localPosition = new Vector3(0, .35f, 0); ring.transform.localScale = new Vector3(p.r, 1, p.r);
                ring.AddComponent<MeshFilter>().sharedMesh = ringMesh; var rr = ring.AddComponent<MeshRenderer>(); rr.shadowCastingMode = ShadowCastingMode.Off; p.ring = rr;
            }
        }
        public static Mesh RingMesh(float r0, float r1, int seg)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= seg; i++) { float a = i * Mathf.PI * 2 / seg; v.Add(new Vector3(Mathf.Sin(a) * r0, 0, Mathf.Cos(a) * r0)); v.Add(new Vector3(Mathf.Sin(a) * r1, 0, Mathf.Cos(a) * r1)); }
            for (int i = 0; i < seg; i++) { int a = i * 2; t.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 }); }
            var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }

        public static void Clear()
        {
            if (root) Object.Destroy(root.gameObject);
            Interiors.Clear(); treeChunks.Clear(); chunkTrees.Clear(); dirtyChunks.Clear(); terrainMesh = null; coverDirty = true;
            obst.Clear(); trees.Clear(); treeGrid.Clear(); points.Clear(); zones.Clear(); zoneH.Clear(); blades.Clear(); fires.Clear(); bigFire.Clear(); fireObst.Clear(); shipGuns.Clear(); ship = null; smokes.Clear();
        }

        /// Smoke screens (RO2-style smoke grenades): x, y, z = centre, w = current radius. Filled by Game each frame.
        public static readonly List<Vector4> smokes = new List<Vector4>();
        /// True when the segment a-b passes through the thick part of a smoke cloud.
        public static bool Smoked(Vector3 a, Vector3 b)
        {
            for (int i = 0; i < smokes.Count; i++)
            {
                var s = smokes[i]; var c = new Vector3(s.x, s.y, s.z); float r = s.w * .8f; if (r <= .5f) continue;
                var ab = b - a; float l2 = ab.sqrMagnitude; float k = l2 > 1e-4f ? Mathf.Clamp01(Vector3.Dot(c - a, ab) / l2) : 0;
                if ((a + ab * k - c).sqrMagnitude < r * r) return true;
            }
            return false;
        }
        /// Line of sight between two world points, ignoring unit triggers. Smoke blocks it too.
        public static bool LOS(Vector3 a, Vector3 b) => !Smoked(a, b) && !Physics.Linecast(a, b, ~0, QueryTriggerInteraction.Ignore);
    }
}
