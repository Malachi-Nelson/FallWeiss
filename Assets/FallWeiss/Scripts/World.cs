// Fall Weiss — world generation: heightmap (port of baseHeight/fullHeight), terrain mesh, buildings,
// villages/city blocks, forests, water, bridges, fences, props and capture-point flags.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace FallWeiss
{
    /// Axis-aligned obstacle (port of addCollider). Used for unit movement collision; a BoxCollider mirrors it for raycasts.
    public class Obst
    {
        public float x0, x1, z0, z1, y0, y1, h, hp, maxhp, ry0, ry1;
        public bool low, dead, rel, floor, ceil, nocover, soft, wire, smoking;
        public GameObject col, vis; public Bd bd; public string pk, kind; public int hex; public List<Obst> link;
    }

    public class Tree { public float x, z, s; public int k; public bool alive = true; public long chunk; public Color c; }
    public class CapPoint
    {
        public string n; public float x, z, r, ap, pushT, best, stallT; public int owner, g, e; public bool locked, inside;
        public float hw, hd, rot; public bool rect; public string key;   // rect: the zone is a rotated rectangle (a whole building interior, a bridgehead, an embankment); r is then its outer radius
        public Renderer cloth, ring;
        public bool Contains(float px, float pz)
        {
            float dx = px - x, dz = pz - z; if (!rect) return dx * dx + dz * dz < r * r;
            float c = Mathf.Cos(rot), s = Mathf.Sin(rot); return Mathf.Abs(dx * c - dz * s) < hw && Mathf.Abs(dx * s + dz * c) < hd;
        }
        /// A random spot inside the zone, k = fraction of its size (0..1).
        public Vector2 RandIn(float k)
        {
            if (!rect) { float a = FU.Rand(0, 6.283f), d = Mathf.Sqrt(FU.Rand(0, 1)) * r * k; return new Vector2(x + Mathf.Cos(a) * d, z + Mathf.Sin(a) * d); }
            float lx = FU.Rand(-1, 1) * hw * k, lz = FU.Rand(-1, 1) * hd * k, c = Mathf.Cos(rot), s = Mathf.Sin(rot); return new Vector2(x + lx * c + lz * s, z - lx * s + lz * c);
        }
        public Vector2 Local2World(float lx, float lz) { float c = Mathf.Cos(rot), s = Mathf.Sin(rot); return new Vector2(x + lx * c + lz * s, z - lx * s + lz * c); }
    }

    /// Batches boxes/cones/prisms into vertex-coloured meshes (one draw call per ~60k verts).
    public class MeshBatch
    {
        readonly List<Vector3> v = new List<Vector3>(); readonly List<Vector3> n = new List<Vector3>();
        readonly List<Color> c = new List<Color>(); readonly List<int> t = new List<int>();
        public bool Empty => v.Count == 0;

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
        /// The triangular end walls under a gable roof, in the wall colour, flush with the wall faces.
        public void GableEnds(Vector3 bc, float w, float d, float rh, float rotY, Color col)
        {
            col = col.linear; float cs = Mathf.Cos(rotY), sn = Mathf.Sin(rotY); bool alongX = w >= d;
            float L = (alongX ? w : d) / 2 - .02f, Sw = (alongX ? d : w) / 2, S = Sw + .45f, yw = rh * (1 - Sw / S);
            Vector3 P(float l, float y, float s) => bc + R(alongX ? new Vector3(l, y, s) : new Vector3(s, y, l), cs, sn);
            foreach (var e in new[] { -L, L })
            {
                Vector3 a = P(e, -.05f, -Sw), b = P(e, -.05f, Sw), c = P(e, yw, Sw), dd = P(e, yw, -Sw), top = P(e, rh - .05f, 0);
                Quad(a, b, c, dd, col); Quad(dd, c, b, a, col); Tri(dd, c, top, col); Tri(top, c, dd, col);
            }
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
        public static bool IsTerrain(Collider c) => c != null && c == terrainCol;   // blast cover: the ground is not a wall
        public static float? WL;
        /// Inland water: ponds sit in natural hollows, brooks run downhill along authored lines; both are wadeable.
        public class Pond { public float x, z, r, level, depth; }
        public class Brook { public Vector2[] p; public float[] lv; public float w, depth; }
        public static readonly List<Pond> ponds = new List<Pond>(); public static readonly List<Brook> brooks = new List<Brook>();
        static bool carving;
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
        public static readonly List<Vector3> farSmoke = new List<Vector3>();   // burning towns beyond the map edge

        static float GS = 2; static float[] HG; static int HN; static float HO; static int NSEED;
        /// Map area relative to the original ~400 m maps: scatter counts (lone trees, haystacks, bushes, ground patches) scale with it.
        public static float AreaK => Mathf.Max(1, M.half * M.half / 40000f);

        // ---------------- noise (port of hash2 / vnoise / fbm) ----------------
        static float Hash2(float x, float z) { double nn = System.Math.Sin(x * 127.1 + z * 311.7 + NSEED * 74.7) * 43758.5453; return (float)(nn - System.Math.Floor(nn)); }
        public static float VNoise(float x, float z)
        {
            float xi = Mathf.Floor(x), zi = Mathf.Floor(z), xf = x - xi, zf = z - zi, u = xf * xf * (3 - 2 * xf), w = zf * zf * (3 - 2 * zf);
            float a = Hash2(xi, zi), b = Hash2(xi + 1, zi), c = Hash2(xi, zi + 1), d = Hash2(xi + 1, zi + 1);
            return a + (b - a) * u + (c - a) * w + (a - b - c + d) * u * w;
        }
        public static float Fbm(float x, float z, int o = 4) { float s = 0, a = .5f, f = 1; for (int i = 0; i < o; i++) { s += VNoise(x * f, z * f) * a; f *= 2; a *= .5f; } return s; }
        // Hash2 above is the classic sin() hash: the phase only advances ~0.23 cycles per integer cell, so its "noise" is quasi-periodic
        // every ~4.4 cells and shows up as an axis-aligned checkerboard wherever it drives colour. Terrain SHAPE keeps using it (every
        // map was authored on those heights); everything cosmetic uses this integer hash instead, which has no such structure.
        static float HashQ(float x, float z)
        {
            uint a = (uint)(Mathf.FloorToInt(x) + 65536) * 374761393u, b = (uint)(Mathf.FloorToInt(z) + 65536) * 668265263u, h = a ^ b ^ ((uint)NSEED * 2246822519u);
            h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16; return (h & 0xFFFFFF) / 16777216f;
        }
        public static float VNoiseQ(float x, float z)
        {
            float xi = Mathf.Floor(x), zi = Mathf.Floor(z), xf = x - xi, zf = z - zi, u = xf * xf * (3 - 2 * xf), w = zf * zf * (3 - 2 * zf);
            float a = HashQ(xi, zi), b = HashQ(xi + 1, zi), c = HashQ(xi, zi + 1), d = HashQ(xi + 1, zi + 1);
            return a + (b - a) * u + (c - a) * w + (a - b - c + d) * u * w;
        }
        /// Colour noise: octaves are rotated against each other so no cell edge ever lines up with the terrain grid or with another octave.
        public static float FbmQ(float x, float z, int o = 4)
        {
            float s = 0, a = .5f, f = 1, tot = 0;
            for (int i = 0; i < o; i++) { float c = Mathf.Cos(i * .7f), sn = Mathf.Sin(i * .7f); s += VNoiseQ((x * c - z * sn) * f + i * 13.7f, (x * sn + z * c) * f) * a; tot += a; f *= 2.1f; a *= .5f; }
            return s / tot;
        }

        // ---------------- terrain shape ----------------
        /// Distance to the nearest trench, exact up to 24 m (1e9 beyond: every caller asks "closer than 3–20 m?"). Segments are
        /// bucketed in 16 m cells, rebuilt when the trench list changes (a new map, a dug foxhole): the terrain bake calls this twice
        /// per vertex (~300 k) and the planned field works roughly doubled the segment count.
        public static float TrenchD(float x, float z)
        {
            float d = 1e9f; if (M == null || M.trenches == null || M.trenches.Length == 0) return d;
            if (!ReferenceEquals(tGridSrc, M.trenches)) BuildTrenchGrid();
            int cx = Mathf.FloorToInt(x / TGC), cz = Mathf.FloorToInt(z / TGC);
            for (int i = -1; i <= 1; i++) for (int j = -1; j <= 1; j++)
                    if (tGrid.TryGetValue(((long)(cx + i) << 32) ^ (uint)(cz + j), out var l)) foreach (var s in l) d = Mathf.Min(d, FU.SegD(x, z, s.x, s.y, s.z, s.w));
            return d < 24 ? d : 1e9f;
        }
        const float TGC = 24; static FTrench[] tGridSrc; static readonly Dictionary<long, List<Vector4>> tGrid = new Dictionary<long, List<Vector4>>();
        static void BuildTrenchGrid()
        {
            tGrid.Clear(); tGridSrc = M.trenches;
            foreach (var t in M.trenches) for (int i = 0; i + 3 < t.pts.Length; i += 2)
                {
                    var s = new Vector4(t.pts[i], t.pts[i + 1], t.pts[i + 2], t.pts[i + 3]);
                    int x0 = Mathf.FloorToInt(Mathf.Min(s.x, s.z) / TGC), x1 = Mathf.FloorToInt(Mathf.Max(s.x, s.z) / TGC), z0 = Mathf.FloorToInt(Mathf.Min(s.y, s.w) / TGC), z1 = Mathf.FloorToInt(Mathf.Max(s.y, s.w) / TGC);
                    for (int a = x0; a <= x1; a++) for (int b = z0; b <= z1; b++) { long k = ((long)a << 32) ^ (uint)b; if (!tGrid.TryGetValue(k, out var l)) tGrid[k] = l = new List<Vector4>(); l.Add(s); }
                }
        }
        /// The trench cut (1.8 m deep, spoil on both lips). Applied after the objective / building pads are levelled — it used to be part
        /// of BaseHeight, so the pad blend flattened every trench inside or near an objective (Modlin's lost ~98 % of their depth).
        static float TrenchCut(float x, float z)
        {
            if (M.trenches == null || M.trenches.Length == 0) return 0; float dm = TrenchD(x, z); if (dm >= 4) return 0;
            return -1.8f * (1 - FU.Sm(.9f, 1.9f, dm)) + .5f * FU.Sm(1.9f, 2.6f, dm) * (1 - FU.Sm(2.6f, 3.8f, dm));
        }
        public static Vector2 KindSize(string k) { if (k == null || !KIND.TryGetValue(k, out var K)) K = KIND["house"]; return new Vector2(K.w, K.d); }
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
            else
            {
                float e = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - M.half;
                if (e > -25)
                {   // the rim of hills that closes the map off. Roads leave through cuttings: within ~12 m of a road the rim is a quarter as high
                    // (grades fell from 34–54 % to ~12 % at the Mokra / Westerplatte spawn roads), fading back to full height by 40 m.
                    float rim = 1; foreach (var r in M.roads) rim = Mathf.Min(rim, .25f + .75f * FU.Sm(12, 40, FU.SegD(x, z, r.x1, r.z1, r.x2, r.z2)));
                    h += FU.Sm(-25, 150, e) * (35 + Fbm(x * .015f + 9, z * .015f, 3) * 110) * rim;
                }
            }
            if (M.hasRiver)
            {
                h += M.rlevel + 4.5f; float d = Mathf.Abs(z - M.RiverC(x));
                h = Mathf.Lerp(h, M.rlevel + 1, 1 - FU.Sm(M.rw, M.rw * 3.4f, d)); h = Mathf.Lerp(h, M.rlevel - 1.8f, 1 - FU.Sm(M.rw * .45f, M.rw, d));
            }
            if (M.hasRiver) { float d = Mathf.Abs(z - M.RiverC(x)); if (d > M.rw * 3.6f) h = Mathf.Max(h, M.rlevel + .8f + (h - M.rlevel - .8f) * .25f * FU.Sm(0, 1, h - M.rlevel)); }   // fields stay above the river: no stray lakes
            if (M.hasSea && SeaEdge(x, z) > 12) h = Mathf.Max(h, .9f + Mathf.Max(0, h - .9f));
            foreach (var r in M.rails)
            {   // embankment, ramped down to a level crossing wherever a road crosses it
                float d = FU.SegD(x, z, r.x1, r.z1, r.x2, r.z2); if (d > 6.5f) continue; float cross = 1;
                foreach (var rd in M.roads) cross = Mathf.Min(cross, FU.Sm(5, 16, FU.SegD(x, z, rd.x1, rd.z1, rd.x2, rd.z2)));
                h += 1.3f * (1 - FU.Sm(2.5f, 6.5f, d)) * (M.rails.Length > 1 ? .4f : 1) * cross;
            }
            if (carving)
            {
                foreach (var p in ponds) { float d = FU.Hyp(x - p.x, z - p.z); if (d < p.r + 5) { float bed = p.level - p.depth * (1 - (d / p.r) * (d / p.r)); h = d < p.r ? Mathf.Min(h, bed) : Mathf.Min(h, Mathf.Lerp(p.level + .05f, h, (d - p.r) / 5)); } }
                foreach (var b in brooks)
                {
                    float best = 1e9f; int bi = 0; float bt = 0;
                    for (int i = 0; i < b.p.Length - 1; i++) { var a = b.p[i]; var c = b.p[i + 1]; var ab = c - a; float L2 = Mathf.Max(1e-4f, ab.sqrMagnitude), t = Mathf.Clamp01(((x - a.x) * ab.x + (z - a.y) * ab.y) / L2); float d = FU.Hyp(x - a.x - ab.x * t, z - a.y - ab.y * t); if (d < best) { best = d; bi = i; bt = t; } }
                    float lv = Mathf.Lerp(b.lv[bi], b.lv[bi + 1], bt), hw = b.w / 2, ford = 1; foreach (var rd in M.roads) ford = Mathf.Min(ford, FU.Sm(4, 14, FU.SegD(x, z, rd.x1, rd.z1, rd.x2, rd.z2)));
                    float bank = (3 + Mathf.Max(0, h - lv) * 1.2f) * Mathf.Lerp(4, 1, ford);   // where a road fords the brook the banks run out long and gentle
                    if (best < hw) h = Mathf.Min(h, lv - b.depth * (1 - (best / hw) * (best / hw) * .6f));
                    else if (best < hw + bank) h = Mathf.Min(h, Mathf.Lerp(lv + .05f, h, (best - hw) / bank));
                }
            }
            return h;
        }
        /// Map editor preview: a mission's terrain height without building the world (noise, hills, river, sea, rails,
        /// trenches). Swaps the static mission / seed for the call and restores them, so it is safe from the editor.
        public static float PreviewHeight(Mission m, float x, float z)
        {
            var om = M; var os = NSEED; M = m; NSEED = m.seed;
            try { return BaseHeight(x, z) + TrenchCut(x, z); } finally { M = om; NSEED = os; }
        }
        public static float? WaterLevel(Mission m) => m.hasRiver ? m.rlevel : m.hasSea ? 0f : (float?)null;
        static float FullHeight(float x, float z)
        {
            float h = BaseHeight(x, z);
            for (int i = 0; i < zones.Count; i++) { var q = zones[i]; float d = FU.Hyp(x - q.x, z - q.y); if (d < q.z + q.w) h = Mathf.Lerp(h, zoneH[i], 1 - FU.Sm(q.z, q.z + q.w, d)); }
            h += TrenchCut(x, z);
            if (M.hasRiver && BridgeAt(x, z) > 0) h = Mathf.Max(h, M.rlevel + 1.05f);
            return h;
        }
        /// A flat zone (building pad, objective, spawn) blended into the terrain. The blend band grows with the height difference to the
        /// surrounding ground so a pad on a hillside gets a ≤30 % ramp instead of a 3–5 m step (topo lint: Mokra farm, Wizna, Bzura mill),
        /// and the pad itself sits a third of the way toward the mean ring height so the step is smaller to begin with.
        static void AddZone(float x, float z, float r, float b)
        {
            float h0 = BaseHeight(x, z), ring = 0, dmax = 0;
            for (int i = 0; i < 8; i++) { float a = i * Mathf.PI / 4, hr = BaseHeight(x + Mathf.Cos(a) * (r + b), z + Mathf.Sin(a) * (r + b)); ring += hr / 8; dmax = Mathf.Max(dmax, Mathf.Abs(hr - h0)); }
            if (b <= 8) h0 = Mathf.Lerp(h0, ring, .33f);
            b = Mathf.Max(b, dmax / .3f);
            zones.Add(new Vector4(x, z, r, b)); zoneH.Add(h0);
        }
        /// Water surface height at (x, z), or -999 where there is no water.
        public static float WaterY(float x, float z)
        {
            float y = -999; if (WL.HasValue) y = WL.Value;
            foreach (var p in ponds) if (FU.Hyp(x - p.x, z - p.z) < p.r + 3) y = Mathf.Max(y, p.level);
            foreach (var b in brooks)
                for (int i = 0; i < b.p.Length - 1; i++)
                {
                    var a = b.p[i]; var c = b.p[i + 1]; var ab = c - a; float L2 = Mathf.Max(1e-4f, ab.sqrMagnitude), t = Mathf.Clamp01(((x - a.x) * ab.x + (z - a.y) * ab.y) / L2);
                    if (FU.Hyp(x - a.x - ab.x * t, z - a.y - ab.y * t) < b.w / 2 + 2) y = Mathf.Max(y, Mathf.Lerp(b.lv[i], b.lv[i + 1], t));
                }
            return y;
        }
        /// How deep the water is over the ground here (0 on dry land or on a bridge deck).
        public static float Depth(float x, float z) { float wy = WaterY(x, z); if (wy < -900) return 0; if (M.hasRiver && BridgeAt(x, z) > 0) return 0; return Mathf.Max(0, wy - H(x, z)); }
        /// Vehicles ford up to ~0.9 m; men wade and swim anywhere short of open sea.
        public static bool BlockedFor(Unit u, float x, float z)
        {
            float d = Depth(x, z); if (d <= 0) return false;
            return u.def.kind == "tank" || u.def.kind == "gun" ? d > .9f : d > 3f;
        }
        /// RO2-style wading: ankle / knee / waist / chest bands slow a man down, deeper than that he swims.
        public static float WadeMul(Unit u, float d)
        {
            if (d < .05f) return 1;
            if (u.def.kind == "tank" || u.def.kind == "gun") return 1 - Mathf.Min(.5f, d * .45f);
            if (u.def.kind == "cav") return d < 1.2f ? 1 - d * .35f : .4f;
            return d < .24f ? .9f : d < .5f ? .75f : d < .86f ? .65f : d < 1.35f ? .6f : .35f;
        }
        public const float SWIM = 1.35f;
        /// Natural water for the map: brooks from the mission data, ponds in the deepest hollows of open country.
        static void PlaceWater(Mission m, Rng R)
        {
            ponds.Clear(); brooks.Clear(); carving = false;
            if (m.streams != null)
                foreach (var s in m.streams)
                {
                    if (s.pts == null || s.pts.Length < 4) continue; var raw = new List<Vector2>(); for (int i = 0; i + 1 < s.pts.Length; i += 2) raw.Add(new Vector2(s.pts[i], s.pts[i + 1]));
                    var P = new List<Vector2>(); for (int i = 0; i < raw.Count - 1; i++) { int n = Mathf.Max(1, Mathf.CeilToInt((raw[i + 1] - raw[i]).magnitude / 6)); for (int k = 0; k < n; k++) P.Add(Vector2.Lerp(raw[i], raw[i + 1], k / (float)n)); } P.Add(raw[raw.Count - 1]);
                    var g = P.Select(q => BaseHeight(q.x, q.y)).ToArray(); if (g[0] < g[g.Length - 1]) { P.Reverse(); System.Array.Reverse(g); }   // water runs downhill: start at the high end
                    var lv = new float[P.Count]; float cur = 1e9f;
                    for (int i = 0; i < P.Count; i++) { cur = Mathf.Min(cur, g[i] - .3f); lv[i] = Mathf.Max(cur, g[i] - 1.8f); cur = Mathf.Max(cur, lv[i] - .01f); }   // never climbs; a rise becomes a shallow cutting   // never climbs; a rise becomes a short cutting, not a canyon
                    for (int it = 0; it < 3; it++) for (int i = 1; i < lv.Length - 1; i++) lv[i] = Mathf.Min(lv[i], (lv[i - 1] + lv[i] + lv[i + 1]) / 3 + .02f);
                    brooks.Add(new Brook { p = P.ToArray(), lv = lv, w = s.w > 0 ? s.w : 3, depth = s.depth > 0 ? s.depth : .45f });
                }
            if (m.ponds >= 0 && !m.city && !m.range)
            {
                int want = m.ponds > 0 ? m.ponds : Mathf.Min(5, Mathf.RoundToInt(AreaK * (m.hasSea ? .25f : .8f))); float Hh = m.half - 60, step = 20;
                var cand = new List<Vector3>();
                for (float x = -Hh; x <= Hh; x += step) for (float z = -Hh; z <= Hh; z += step)
                    {
                        float h = BaseHeight(x, z); bool min = true;
                        for (int i = 0; i < 12 && min; i++) { float a = i * .5236f; if (BaseHeight(x + Mathf.Sin(a) * 34, z + Mathf.Cos(a) * 34) < h + .6f) min = false; }
                        if (!min) continue;
                        bool bad = InRiver(x, z, 30) || (m.hasSea && SeaEdge(x, z) < 40) || NearRoad(x, z, 26) || NearRail(x, z, 26) || TrenchD(x, z) < 20;
                        foreach (var p in m.points) if (FU.Hyp(x - p.x, z - p.z) < Mathf.Max(p.r, Mathf.Max(p.w, p.d) / 2) + 30) bad = true;
                        foreach (var s in m.spawn) if (FU.Hyp(x - s.x, z - s.z) < 70) bad = true;
                        foreach (var b in m.build) if (FU.Hyp(x - b.x, z - b.z) < 32) bad = true;
                        foreach (var v in m.villages) if (FU.Hyp(x - v.x, z - v.z) < v.r + 30) bad = true;
                        if (!bad) cand.Add(new Vector3(x, h, z));
                    }
                foreach (var c in cand.OrderBy(c => c.y))
                {
                    if (ponds.Count >= want) break; if (ponds.Any(p => FU.Hyp(p.x - c.x, p.z - c.z) < 120)) continue;
                    float r = 10 + R.Next() * 12, rim = 1e9f; for (int i = 0; i < 16; i++) { float a = i * .3927f; rim = Mathf.Min(rim, BaseHeight(c.x + Mathf.Sin(a) * (r + 2), c.z + Mathf.Cos(a) * (r + 2))); }
                    float level = Mathf.Min(c.y + .5f, rim - .15f); if (level < c.y + .15f) continue;   // the hollow must actually hold water
                    ponds.Add(new Pond { x = c.x, z = c.z, r = r, level = level, depth = .7f + R.Next() * .5f });   // knee to waist deep: wadeable
                }
            }
            carving = true;
        }

        public static float H(float x, float z)
        {
            float fx = Mathf.Clamp((x - HO) / GS, 0, HN - 1.001f), fz = Mathf.Clamp((z - HO) / GS, 0, HN - 1.001f);
            int ix = (int)fx, iz = (int)fz; float tx = fx - ix, tz = fz - iz; int i = iz * HN + ix;
            return Mathf.Lerp(Mathf.Lerp(HG[i], HG[i + 1], tx), Mathf.Lerp(HG[i + HN], HG[i + HN + 1], tx), tz);
        }
        public static float Slope(float x, float z) => FU.Hyp(H(x + 1.5f, z) - H(x - 1.5f, z), H(x, z + 1.5f) - H(x, z - 1.5f)) / 3;
        public static bool InWater(float x, float z) => Depth(x, z) > .35f;
        public static bool Blocked(float x, float z) => Depth(x, z) > .9f;   // vehicle fording limit (men: see BlockedFor)

        static bool ClearOfKey(float x, float z, float m)
        {
            foreach (var p in M.points) if (FU.Hyp(x - p.x, z - p.z) < m) return false;
            foreach (var s in M.spawn) if (FU.Hyp(x - s.x, z - s.z) < m + 8) return false;
            return true;
        }
        static bool NearRoad(float x, float z, float m) { foreach (var r in M.roads) if (FU.SegD(x, z, r.x1, r.z1, r.x2, r.z2) < r.w / 2 + m) return true; return false; }
        public static bool NearRoadG(float x, float z, float m) => NearRoad(x, z, m);   // game-side access (AI deployment: AT guns cover roads)
        public static bool InRiverG(float x, float z, float m) => M.hasRiver && InRiver(x, z, m);
        public static bool NearRailG(float x, float z, float m) => M.rails != null && NearRail(x, z, m);
        public static float SeaEdgeG(float x, float z) => M.hasSea ? SeaEdge(x, z) : 1e9f;
        public static int dugCount;
        /// Entrenching tool (T32): cuts a short trench (a foxhole is a 2–3 m one) into the live terrain with the same profile the
        /// map's own trenches have (1.8 m deep, spoil thrown up on both lips), adds it to the mission's trench list so cover,
        /// blast shelter, trees and craters all treat it as a trench, and rebuilds cover. Refuses where a trench already is.
        public static bool DigTrench(float ax, float az, float bx, float bz)
        {
            if (terrainMesh == null || HG == null || dugCount > 60) return false;
            float mx = (ax + bx) / 2, mz = (az + bz) / 2;
            if (TrenchD(mx, mz) < 4.5f || InBuilding(mx, mz, 1.5f) || InWater(mx, mz) || NearRoad(mx, mz, 1) || BridgeAt(mx, mz) > 0) return false;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(ax, bx) - 4 - HO) / GS)), x1 = Mathf.Min(HN - 1, Mathf.CeilToInt((Mathf.Max(ax, bx) + 4 - HO) / GS));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(az, bz) - 4 - HO) / GS)), z1 = Mathf.Min(HN - 1, Mathf.CeilToInt((Mathf.Max(az, bz) + 4 - HO) / GS));
            for (int iz = z0; iz <= z1; iz++) for (int ix = x0; ix <= x1; ix++)
                {
                    float px = HO + ix * GS, pz = HO + iz * GS, dm = FU.SegD(px, pz, ax, az, bx, bz); if (dm >= 4) continue; int i = iz * HN + ix;
                    HG[i] += -1.6f * (1 - FU.Sm(.9f, 1.9f, dm)) + .45f * FU.Sm(1.9f, 2.6f, dm) * (1 - FU.Sm(2.6f, 3.8f, dm)); tVerts[i].y = HG[i];
                    if (dm < 3.2f) { float k = .85f * (1 - FU.Sm(1.6f, 3.2f, dm)); tCols[i] = Color.Lerp(tCols[i], FU.Hex(0x4a3a28).linear, k); }   // the terrain colours are linear
                }
            var l = new List<FTrench>(M.trenches ?? new FTrench[0]) { new FTrench { pts = new[] { ax, az, bx, bz } } }; M.trenches = l.ToArray();
            dugCount++; terrainDirty = true; colDirty = true; normT = 0; coverDirty = true; return true;
        }
        public static bool InBuilding(float x, float z, float m) { foreach (var b in obst) if (!b.dead && !b.low && !b.soft && x > b.x0 - m && x < b.x1 + m && z > b.z0 - m && z < b.z1 + m) return true; foreach (var bd in Interiors.bds) if (bd.alive && x > bd.x0 - m && x < bd.x1 + m && z > bd.z0 - m && z < bd.z1 + m) return true; return false; }
        static bool InRiver(float x, float z, float m)
        {
            if (M.hasRiver ? Mathf.Abs(z - M.RiverC(x)) < M.rw * 3.4f + m : M.hasSea && SeaEdge(x, z) < 5 + m) return true;
            foreach (var p in ponds) if (FU.Hyp(x - p.x, z - p.z) < p.r + 3 + m) return true;
            foreach (var b in brooks) for (int i = 0; i < b.p.Length - 1; i++) if (FU.SegD(x, z, b.p[i].x, b.p[i].y, b.p[i + 1].x, b.p[i + 1].y) < b.w / 2 + 2.5f + m) return true;
            return false;
        }
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
            if (x0 > x1) (x0, x1) = (x1, x0); if (z0 > z1) (z0, z1) = (z1, z0);   // an inverted box never blocked movement (Collide assumes x0<x1) but its BoxCollider still stopped rounds: an invisible wall (Unity warned "negative size" on Westerplatte)
            var b = new Obst { x0 = x0, x1 = x1, z0 = z0, z1 = z1, h = h, low = low, hp = hp, maxhp = hp }; obst.Add(b); return b;
        }

        // ---------------- build ----------------
        public static void Build(Mission m)
        {
            Clear(); M = m; NSEED = m.seed; WL = m.hasRiver ? m.rlevel : m.hasSea ? 0f : (float?)null;
            FieldWorks.Plan(m);   // T32: Polish rifle pits and fire trenches at defended objectives (this build only; missions.json is untouched)
            root = new GameObject("World — " + m.name).transform;
            var R = new Rng(m.seed); float Hh = m.half;
            int defend = 1 - m.attacker;

            foreach (var p in m.points) { points.Add(new CapPoint { n = p.n, x = p.x, z = p.z, r = p.w > 0 && p.d > 0 ? Mathf.Max(p.w, p.d) / 2 : p.r > 0 ? p.r : 14, owner = defend, inside = p.inside, rect = p.w > 0 && p.d > 0, hw = p.w / 2, hd = p.d / 2, rot = p.rot, key = p.key }); if (!(m.hasRiver && BridgeAt(p.x, p.z) > 0)) AddZone(p.x, p.z, 10, 26); }
            foreach (var s in m.spawn) AddZone(s.x, s.z, 24, 34);
            PlaceWater(m, R);

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
            int lone = Mathf.RoundToInt((m.city ? 30 : 100) * AreaK); for (int i = 0; i < lone; i++) AddT((R.Next() - .5f) * 2 * (Hh + 20), (R.Next() - .5f) * 2 * (Hh + 20));
            if (!m.city)
            {   // foliage pass (5 Nov): the Polish countryside is not a lawn — copses in the fields and tree-lined roads (every long road had its avenue)
                int copses = Mathf.RoundToInt(9 * AreaK);
                for (int i = 0; i < copses; i++) { float cx = (R.Next() - .5f) * 2 * (Hh - 30), cz = (R.Next() - .5f) * 2 * (Hh - 30); if (!ClearOfKey(cx, cz, 30) || NearRoad(cx, cz, 14)) continue; int n = 5 + (int)(R.Next() * 7); float cr = 6 + R.Next() * 7; for (int k = 0; k < n; k++) { float a = R.Next() * 6.28f, dd = Mathf.Sqrt(R.Next()) * cr; AddT(cx + Mathf.Cos(a) * dd, cz + Mathf.Sin(a) * dd); } }
                foreach (var r in m.roads)
                {
                    float dx = r.x2 - r.x1, dz = r.z2 - r.z1, L = FU.Hyp(dx, dz); if (L < 90 || R.Next() < .35f) continue; dx /= L; dz /= L; float off = r.w / 2 + 5.5f; bool both = R.Next() < .5f;
                    for (float s = 14 + R.Next() * 10; s < L - 14; s += 13 + R.Next() * 5)
                    {
                        if (R.Next() < .15f) continue;   // the odd gap where a tree has gone
                        float px = r.x1 + dx * s, pz = r.z1 + dz * s;
                        AddT(px - dz * off + FU.Rand(-.6f, .6f), pz + dx * off + FU.Rand(-.6f, .6f)); if (both) AddT(px + dz * off + FU.Rand(-.6f, .6f), pz - dx * off + FU.Rand(-.6f, .6f));
                    }
                }
            }
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
            FitZones();
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
                o.dead = false; o.kind = s.k;
            }
        }

        static void BakeHeights()
        {
            GS = M.half > 360 ? 2.5f : 2; float S = M.half * 2 + 400; HN = Mathf.RoundToInt(S / GS) + 1; HO = -S / 2; HG = new float[HN * HN];
            for (int iz = 0; iz < HN; iz++) for (int ix = 0; ix < HN; ix++) HG[iz * HN + ix] = FullHeight(HO + ix * GS, HO + iz * GS);
        }

        static Color GroundColor(float x, float z, float h, float s, Color grass, Color[] fields)
        {
            float n = FbmQ(x * .04f, z * .04f, 3) * .6f + FbmQ(x * .0075f + 31, z * .0075f, 2) * .4f;   // 25 m texture under 130 m soil / moisture patches
            if (M.city)
            {
                var cc = FU.Hex(0x74706a) * (1 + (n - .5f) * .25f); cc.a = 1;
                if (FbmQ(x * .1f + 5, z * .1f, 2) > .6f) cc = Color.Lerp(cc, FU.Hex(0x5a5446), .5f);
                foreach (var l in M.lanes) if (FU.SegD(x, z, l.x1, l.z1, l.x2, l.z2) < l.w / 2) cc = Color.Lerp(cc, FU.Hex(0x5e5a54), .6f);
                return cc;
            }
            Color c = grass * (1 + (n - .5f) * .3f + (h > 14 ? .05f : 0)); c.a = 1;
            if (M.name != "Westerplatte")
            {
                float a = .5f, fx = x * Mathf.Cos(a) + z * Mathf.Sin(a), fz = -x * Mathf.Sin(a) + z * Mathf.Cos(a);
                int ci = Mathf.FloorToInt(fx / 48), cj = Mathf.FloorToInt(fz / 62); float id = HashQ(ci, cj + 77);
                // furrows used to be a 3 m stripe sampled on the 2–2.5 m vertex grid (moiré); the shader now draws them per pixel for
                // vertices flagged by alpha < 1, along the same rotated axis (see VertexColorLit _Detail).
                if (id > .52f && s < .3f && h < 20) { c = fields[(int)(id * 40) % fields.Length] * (1 + (n - .5f) * .12f); c.a = .5f; }
            }
            if (s > .42f) c = Color.Lerp(c, FU.Hex(0x6e6a58), Mathf.Clamp((s - .42f) * 3, 0, .85f));
            if (WL.HasValue) { if (h < WL.Value + 1.4f) c = Color.Lerp(c, FU.Hex(M.hasSea ? 0xcdbf94 : 0x6a5a3c), M.hasSea ? .85f : .6f); else if (M.hasSea && h < WL.Value + 2.8f) c = Color.Lerp(c, FU.Hex(0xcdbf94), .3f); }
            if (ponds.Count + brooks.Count > 0) { float wy = WaterY(x, z); if (wy > -900 && h < wy + .9f) c = Color.Lerp(c, FU.Hex(0x5a5038), .65f * (1 - Mathf.Clamp01((h - wy) / .9f))); }
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
                mb.Gable(new Vector3(s.cx, y0 + s.h, s.cz), s.w, s.d, rh, 0, roof); mb.GableEnds(new Vector3(s.cx, y0 + s.h, s.cz), s.w, s.d, rh, 0, wall);
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
            if (treeChunks.TryGetValue(key, out var old) && old) { var omf = old.GetComponent<MeshFilter>(); if (omf && omf.sharedMesh) Object.Destroy(omf.sharedMesh); Object.Destroy(old); }   // the chunk mesh is its own: free it too (every crushed tree leaked one)
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
            coverDirty = true; Nav.dirty = true;
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
            if (terrainMesh == null || craterCount > 160 || InWater(x, z) || BridgeAt(x, z) > 0 || (M.hasRiver && InRiver(x, z, r)) || NearRoad(x, z, r + 1) || InBuilding(x, z, r + .5f) || TrenchD(x, z) < r + 2) return; craterCount++;   // not under a floor slab (tilted rooms, unreachable doors) and not through a trench line
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
                    for (int side = -1; side <= 1; side += 2)
                    {   // the parapets are solid: nothing drives or walks off the side of the deck any more (they are also knee-high cover)
                        var k = AddObst(bx + side * 5.6f - .2f, bx + side * 5.6f + .2f, c - len / 2, c + len / 2, 1.1f, true, 600); k.y0 = y + .2f; k.y1 = y + 1.25f; k.kind = "kerb";
                    }
                }
        }

        /// A rotated low obstacle (sandbag wall, barricade, wreck) as a chain of short boxes that hug the visible shape:
        /// one axis-aligned box around a diagonal wall blocks bullets and men well outside the wall itself.
        /// ang = direction of the long axis in game space, measured from +z (atan2(dx, dz)).
        public static List<Obst> OrientedObst(float cx, float cz, float len, float wid, float ang, float h, float hp, bool low, GameObject vis)
        {
            var list = new List<Obst>(); float sA = Mathf.Sin(ang), cA = Mathf.Cos(ang);
            int n = Mathf.Max(1, Mathf.CeilToInt(len / Mathf.Max(.45f, wid * .8f))); float sl = len / n;
            float hx = Mathf.Abs(sA) * sl / 2 + Mathf.Abs(cA) * wid / 2, hz = Mathf.Abs(cA) * sl / 2 + Mathf.Abs(sA) * wid / 2;
            for (int i = 0; i < n; i++)
            {
                float t = (i + .5f) / n - .5f, x = cx + sA * len * t, z = cz + cA * len * t;
                var o = AddObst(x - hx, x + hx, z - hz, z + hz, h, low, hp); o.y0 = H(x, z); o.y1 = o.y0 + h; o.vis = vis; list.Add(o);
            }
            if (list.Count > 1) foreach (var o in list) o.link = list;
            return list;
        }
        /// The real footprint of a placed model, measured from its meshes in the model's own (rotated) frame.
        static Bounds ModelExtent(GameObject go)
        {
            var inv = Quaternion.Inverse(go.transform.rotation); var o = go.transform.position; bool any = false; var b = new Bounds();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh) continue; var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = inv * (mf.transform.TransformPoint(c) - o); if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return any ? b : new Bounds(Vector3.zero, new Vector3(8, 1.1f, 1.1f));
        }
        /// Collision that matches a placed model: the long horizontal axis becomes a chain of boxes.
        static void ModelObst(GameObject go, float rot, float hp)
        {
            var e = ModelExtent(go); var ctr = go.transform.position + go.transform.rotation * e.center; bool alongX = e.size.x >= e.size.z;
            float len = alongX ? e.size.x : e.size.z, wid = alongX ? e.size.z : e.size.x, ang = rot + (alongX ? Mathf.PI / 2 : 0);
            OrientedObst(ctr.x, ctr.z, len, wid, ang, Mathf.Max(.6f, e.max.y), hp, true, go);
        }
        /// September 1939 on the ground: shell craters stitched across the attackers' approach, refugee carts and their
        /// bundles abandoned along the roads, burnt-out vehicles in the ditches, and towns burning on the skyline.
        static void InvasionDressing(MeshBatch mb, Rng R)
        {
            farSmoke.Clear(); if (M.range) return;
            int att = M.attacker; var aS = M.spawn[att]; var dS = M.spawn[1 - att]; float Hh = M.half;
            bool Free(float x, float z, float m) => Mathf.Abs(x) < Hh - 8 && Mathf.Abs(z) < Hh - 8 && ClearOfKey(x, z, 22) && !InBuilding(x, z, m) && !InRiver(x, z, 3) && !InWater(x, z) && TrenchD(x, z) > 4 && Slope(x, z) < .35f;
            // craters: denser toward the Polish line, along the axis of the attack
            int nc = M.city ? Mathf.RoundToInt(10 * AreaK) : Mathf.RoundToInt(22 * AreaK);
            for (int i = 0; i < nc; i++)
            {
                float t = Mathf.Pow(R.Next(), .7f), x = Mathf.Lerp(aS.x, dS.x, t) + (R.Next() - .5f) * Hh * 1.4f, z = Mathf.Lerp(aS.z, dS.z, t) + (R.Next() - .5f) * Hh * .5f;
                if (!Free(x, z, 2) || NearRoad(x, z, 2)) continue; float r = 1.4f + R.Next() * 2.4f, y = H(x, z);
                mb.Cyl(new Vector3(x, y - .02f, z), r * 1.25f, r, .12f, 14, FU.Hex(0x3a3024)); mb.Cyl(new Vector3(x, y + .03f, z), r * .6f, r * .45f, .1f, 10, FU.Hex(0x221d16));
            }
            if (!M.city)
            {
                foreach (var rd in M.roads)
                {
                    float L = FU.Hyp(rd.x2 - rd.x1, rd.z2 - rd.z1); if (L < 80) continue; float ux = (rd.x2 - rd.x1) / L, uz = (rd.z2 - rd.z1) / L; int n = Mathf.Max(1, Mathf.RoundToInt(L / 160));
                    for (int i = 0; i < n; i++)
                    {   // a refugee cart pitched into the verge, bundles and a trunk spilled around it
                        float s = (i + .3f + R.Next() * .4f) / n * L, side = R.Next() < .5f ? -1 : 1, off = rd.w / 2 + 2.2f, x = rd.x1 + ux * s - uz * off * side, z = rd.z1 + uz * s + ux * off * side;
                        if (!Free(x, z, 3)) continue; var c = Place("cart", x, z, Mathf.Atan2(ux, uz) + side * .5f); c.transform.rotation *= Quaternion.Euler(0, 0, side * 14);
                        for (int k = 0; k < 5; k++) { float bx = x + (R.Next() - .5f) * 5, bz = z + (R.Next() - .5f) * 5; int[] cs = { 0x6a5a44, 0x4a4e5a, 0x7a3a2a, 0x8a8070, 0x3a3a34 }; mb.Box(new Vector3(bx, H(bx, bz) + .2f, bz), new Vector3(.5f + R.Next() * .5f, .35f, .4f + R.Next() * .3f), R.Next() * 3, FU.Hex(cs[k])); }
                    }
                    for (int i = 0; i < Mathf.Max(1, n / 2); i++)
                    {   // a burnt-out vehicle shoved off the road, still smouldering
                        float s = R.Next() * L, side = R.Next() < .5f ? -1 : 1, off = rd.w / 2 + 3.5f, x = rd.x1 + ux * s - uz * off * side, z = rd.z1 + uz * s + ux * off * side;
                        if (!Free(x, z, 4)) continue; Place("wreck", x, z, Mathf.Atan2(ux, uz) + side * .3f); fires.Add(new Vector3(x, H(x, z) + 1.2f, z)); bigFire.Add(false); fireObst.Add(null);
                    }
                }
            }
            // towns burning on the skyline, mostly in the Polish rear and on the flanks
            float ax = dS.x - aS.x, az = dS.z - aS.z, al = Mathf.Max(1, FU.Hyp(ax, az)); ax /= al; az /= al;
            for (int i = 0; i < 4; i++)
            {
                float side = i < 3 ? 1 : -.4f, r = Hh + 160 + R.Next() * 260, sp = (R.Next() - .5f) * Hh * 2.2f;
                farSmoke.Add(new Vector3(ax * r * side - az * sp, 4, az * r * side + ax * sp));
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
            for (int i = 0; i < FieldWorks.fireSegs.Count; i++)
            {   // T32: a sandbag parapet on the forward lip of every Polish fire-trench section (the spoil is thrown forward and faced with bags)
                var s = FieldWorks.fireSegs[i]; var f = FieldWorks.fireFace[i]; float mx = (s.x + s.z) / 2 + f.x * 2.5f, mz = (s.y + s.w) / 2 + f.y * 2.5f, rr = Mathf.Atan2(f.x, f.y);
                if (InBuilding(mx, mz, 1.5f) || InWater(mx, mz) || NearRoad(mx, mz, 1)) continue;
                var sv = Place("sandbags", mx, mz, rr); ModelObst(sv, rr, 120);
            }
            foreach (var p in points)
            {
                float dx = aSp.x - p.x, dz = aSp.z - p.z, l = Mathf.Max(.01f, FU.Hyp(dx, dz)), ux = dx / l, uz = dz / l, rot = Mathf.Atan2(ux, uz), px = -uz, pz = ux;
                for (int j = -1; j <= 1; j++)
                {
                    float x = p.x + ux * 13 + px * j * 9.5f, z = p.z + uz * 13 + pz * j * 9.5f; if (Bad(x, z, 2)) continue;
                    var sv = Place("sandbags", x, z, rot); ModelObst(sv, rot, 120);
                }
                {   // T32: cover inside the zone too — sandbagged positions either side of the centre facing the approach, and one
                    // facing a flank, so the defenders have somewhere to fight from once the line in front has gone (and the attackers
                    // something to clear). In towns this is the sandbagged street corner / courtyard of 1939, not a trench.
                    float pr = Mathf.Max(8, p.r);
                    foreach (var (fx, fz, ang) in new[] { (-.45f, .15f, 0f), (.45f, .15f, 0f), (FU.Rand(-.2f, .2f), -.35f, 1.5708f) })
                    {
                        float x = p.x + px * fx * pr + ux * fz * pr, z = p.z + pz * fx * pr + uz * fz * pr, rr = rot + ang * (R.Next() < .5f ? 1 : -1);
                        if (Bad(x, z, 2.5f) || NearRoad(x, z, 1.5f) || Mathf.Abs(x) > M.half - 10 || Mathf.Abs(z) > M.half - 10) continue;
                        var sv = Place("sandbags", x, z, rr); ModelObst(sv, rr, 120);
                    }
                }
                for (int j = -2; j <= 2; j++)
                {
                    float x = p.x + ux * 25 + px * j * 8, z = p.z + uz * 25 + pz * j * 8 + FU.Rand(-2, 2); if (Bad(x, z, 2)) continue;
                    if (j % 2 == 0) { var wv = Place("wire", x, z, rot); var o = AddObst(x - 3.5f, x + 3.5f, z - 3.5f, z + 3.5f, 1.2f, true, 60); Shrink(o, x, z, ux, uz, 3.5f, .5f); o.y0 = H(x, z); o.y1 = o.y0 + 1.2f; o.wire = true; o.vis = wv; }
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
                for (int k = 0; k < 5; k++)
                {   // ammunition dump: crates of two sizes, some stacked, a tarpaulin on the biggest pile (was five identical cubes)
                    float x = sp.x + (k - 2) * 3.2f + FU.Rand(-.4f, .4f), z = sp.z + side + FU.Rand(-.8f, .8f), y = H(x, z), w = k % 2 == 0 ? 1.5f : 1.1f, hgt = k % 2 == 0 ? .9f : .7f, rot = FU.Rand(-.25f, .25f);
                    mb.Box(new Vector3(x, y + hgt / 2, z), new Vector3(w, hgt, w * .8f), rot, FU.Hex(k % 3 == 0 ? 0x6b5a3a : 0x5e4c30)); mb.Box(new Vector3(x, y + hgt + .02f, z), new Vector3(w + .02f, .05f, .22f), rot, FU.Hex(0xcaa83a));
                    if (k % 2 == 0) { mb.Box(new Vector3(x + .1f, y + hgt + .3f, z - .05f), new Vector3(w * .7f, .6f, w * .55f), rot + .3f, FU.Hex(0x6b5a3a)); if (k == 2) mb.Box(new Vector3(x, y + hgt + .66f, z), new Vector3(w + .5f, .1f, w * .9f), rot + .1f, FU.Hex(0x4f5a3c)); }
                }
            }
            float fwd = M.spawn[def].z > aSp.z ? 1 : -1;
            for (int i = 0; i < 4; i++) { float x = aSp.x + (i % 2 == 1 ? 18 : -18) + FU.Rand(-3, 3), z = aSp.z - fwd * (14 + (i >> 1) * 10); if (!InWater(x, z) && !InBuilding(x, z, 3)) Place("truck_" + att, x, z, fwd > 0 ? 0 : Mathf.PI); }
            for (int i = 0; i < 2; i++) { float x = dSp.x + FU.Rand(-20, 20), z = dSp.z + FU.Rand(-10, 10); if (!InWater(x, z) && !InBuilding(x, z, 3)) Place("cart", x, z, R.Next() * 6); }
            if (!M.city) for (int i = 0; i < 14 * AreaK; i++)
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
            InvasionDressing(mb, R);
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
            var wm = Mats.Lit(FU.Hex(0x26393a), .35f);   // still, peaty inland water: dark and only faintly reflective
            foreach (var p in ponds)
            {
                var v = new List<Vector3> { new Vector3(p.x, p.level, p.z) }; var t = new List<int>(); int n = 40; float rr = p.r + 3;
                for (int i = 0; i <= n; i++) { float a = i * 6.283f / n; v.Add(new Vector3(p.x + Mathf.Sin(a) * rr, p.level, p.z + Mathf.Cos(a) * rr)); if (i > 0) t.AddRange(new[] { 0, i, i + 1 }); }
                var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); var pg = new GameObject("Pond"); pg.transform.SetParent(root, false);
                pg.AddComponent<MeshFilter>().sharedMesh = m; pg.AddComponent<MeshRenderer>().sharedMaterial = wm;
            }
            foreach (var b in brooks)
            {
                var v = new List<Vector3>(); var t = new List<int>(); float hw = b.w / 2 + 1.5f;
                for (int i = 0; i < b.p.Length; i++)
                {
                    var d = (b.p[Mathf.Min(b.p.Length - 1, i + 1)] - b.p[Mathf.Max(0, i - 1)]).normalized; var nrm = new Vector2(-d.y, d.x) * hw;
                    v.Add(new Vector3(b.p[i].x - nrm.x, b.lv[i], b.p[i].y - nrm.y)); v.Add(new Vector3(b.p[i].x + nrm.x, b.lv[i], b.p[i].y + nrm.y));
                    if (i > 0) { int a = (i - 1) * 2; t.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3, a, a + 1, a + 2, a + 1, a + 3, a + 2 }); }
                }
                var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); var bg = new GameObject("Brook"); bg.transform.SetParent(root, false);
                bg.AddComponent<MeshFilter>().sharedMesh = m; bg.AddComponent<MeshRenderer>().sharedMaterial = wm;
            }
            if (!WL.HasValue) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane); go.name = "Water"; go.transform.SetParent(root, false);
            float S = (M.half * 2 + 400) / 10f; go.transform.localScale = new Vector3(S, 1, S); go.transform.position = new Vector3(0, WL.Value, 0);
            Object.Destroy(go.GetComponent<Collider>());
            // Half-gloss, not mirror (.9): at .9 the sun's reflection on the sea / river was a hard white highlight that bloomed into a blinding
            // glare whenever the player looked across the water from the pier or a bridge at a low sun. .5 keeps a soft glint.
            go.GetComponent<Renderer>().sharedMaterial = Mats.Lit(FU.Hex(M.hasSea ? 0x2f5f86 : 0x3d6f8f), .5f);
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

        /// Objectives inside a building (or keyed "building") take the building's whole footprint as their zone, so the entire
        /// interior has to be cleared and held, not a circle around a flag.
        static void FitZones()
        {
            foreach (var p in points)
            {
                if (p.rect || !(p.inside || p.key == "building")) continue;
                Bd best = null; float bd = 1e9f;
                foreach (var b in Interiors.bds) { if (!b.alive) continue; float dx = Mathf.Max(0, Mathf.Max(b.x0 - p.x, p.x - b.x1)), dz = Mathf.Max(0, Mathf.Max(b.z0 - p.z, p.z - b.z1)), d = FU.Hyp(dx, dz); if (d < bd) { bd = d; best = b; } }
                if (best == null || bd > 12) continue;
                float m = p.inside ? 1.2f : 4;   // a building keyed from outside includes its doorsteps and walls
                p.x = (best.x0 + best.x1) / 2; p.z = (best.z0 + best.z1) / 2; p.rect = true; p.rot = 0; p.inside = true;
                p.hw = (best.x1 - best.x0) / 2 + m; p.hd = (best.z1 - best.z0) / 2 + m; p.r = Mathf.Max(p.hw, p.hd);
            }
        }
        /// Zone outline draped over the terrain (a flat ring floats or sinks on hilltops and large zones).
        static Mesh ZoneOutline(CapPoint p)
        {
            var P = new List<Vector2>();
            if (p.rect)
            {
                Vector2[] c = { p.Local2World(-p.hw, -p.hd), p.Local2World(p.hw, -p.hd), p.Local2World(p.hw, p.hd), p.Local2World(-p.hw, p.hd) };
                for (int e = 0; e < 4; e++) { var a = c[e]; var b = c[(e + 1) % 4]; int n = Mathf.Max(2, Mathf.CeilToInt((b - a).magnitude / 1.5f)); for (int i = 0; i < n; i++) P.Add(Vector2.Lerp(a, b, i / (float)n)); }
            }
            else { int n = Mathf.Max(48, Mathf.CeilToInt(p.r * 6.283f / 1.5f)); for (int i = 0; i < n; i++) { float a = i * 6.283f / n; P.Add(new Vector2(p.x + Mathf.Sin(a) * p.r, p.z + Mathf.Cos(a) * p.r)); } }
            var v = new List<Vector3>(); var t = new List<int>(); int N = P.Count; float w = Mathf.Clamp(p.r * .012f, .3f, .8f);
            for (int i = 0; i < N; i++)
            {
                var a = P[(i + N - 1) % N]; var b = P[(i + 1) % N]; var dir = (b - a).normalized; var nrm = new Vector2(dir.y, -dir.x) * w; var q = P[i];
                float y = Mathf.Max(H(q.x, q.y), WL ?? -999) + .3f;
                v.Add(new Vector3(q.x - nrm.x, y, q.y - nrm.y)); v.Add(new Vector3(q.x + nrm.x, y, q.y + nrm.y));
            }
            for (int i = 0; i < N; i++) { int a = i * 2, b = ((i + 1) % N) * 2; t.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b, a, b, a + 1, a + 1, b, b + 1 }); }
            var m = new Mesh { indexFormat = IndexFormat.UInt32 }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
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
                var ring = new GameObject("Zone " + p.n); ring.transform.SetParent(root, false);
                ring.AddComponent<MeshFilter>().sharedMesh = ZoneOutline(p); var rr = ring.AddComponent<MeshRenderer>(); rr.shadowCastingMode = ShadowCastingMode.Off; p.ring = rr;
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
            if (root) { root.gameObject.SetActive(false); Object.Destroy(root.gameObject); }   // deactivate now: Destroy waits for the end of the frame, and the next map's AI deployment ran its sightlines against the old map's colliders
            ponds.Clear(); brooks.Clear(); carving = false;   // the next map's objective pads were flattened around the previous map's water
            Interiors.Clear(); Nav.Clear(); treeChunks.Clear(); chunkTrees.Clear(); dirtyChunks.Clear(); terrainMesh = null; coverDirty = true;
            obst.Clear(); trees.Clear(); treeGrid.Clear(); points.Clear(); zones.Clear(); zoneH.Clear(); blades.Clear(); fires.Clear(); bigFire.Clear(); fireObst.Clear(); shipGuns.Clear(); ship = null; smokes.Clear(); breakables.Clear();
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
        /// Line of sight between two world points, ignoring unit triggers. Smoke blocks it; so does a wood (RO2 / SD2: a forest is
        /// concealment) — a trunk on the line, or, past 40 m, two or more crowns between you and him.
        public static bool LOS(Vector3 a, Vector3 b)
        {
            if (Smoked(a, b) || Physics.Linecast(a, b, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (trees.Count == 0) return true;
            if (TreeTrunk(a, b, out _)) return false;
            return (b - a).sqrMagnitude < 40 * 40 || Canopies(a, b, 2) < 2;
        }

        // ---------------- trees along a line (the tree meshes are batched and have no colliders) ----------------
        // Trunk: cylinder r = .3·s from the ground to 3·s. Crown: conifers (k = 0) a cone 1.7·s..8.4·s, broadleaf (k = 1) a ball
        // 2.2·s..6.8·s; both taken as a cylinder r = 2·s between 1.8·s and 7·s. Walks the 10 m tree-grid cells the segment crosses.
        static readonly HashSet<long> cellSeen = new HashSet<long>();
        static void CellsAlong(Vector3 a, Vector3 b, System.Action<List<Tree>> f)
        {
            cellSeen.Clear(); float dx = b.x - a.x, dz = b.z - a.z, L = FU.Hyp(dx, dz); int n = Mathf.Max(1, Mathf.CeilToInt(L / 5f));
            for (int i = 0; i <= n; i++)
            {
                float k = i / (float)n, x = a.x + dx * k, z = a.z + dz * k; int cx = Mathf.FloorToInt(x / 10), cz = Mathf.FloorToInt(z / 10);
                for (int ox = -1; ox <= 1; ox++) for (int oz = -1; oz <= 1; oz++)
                    {
                        long key = ((long)(cx + ox) << 32) ^ (uint)(cz + oz); if (!cellSeen.Add(key)) continue;
                        if (treeGrid.TryGetValue(key, out var lst)) f(lst);
                    }
            }
        }
        /// Nearest point of the segment a-b to the tree's axis, in 2D: returns the parameter t and the horizontal distance.
        static float Near2D(Vector3 a, Vector3 b, Tree t, out float dist)
        {
            float dx = b.x - a.x, dz = b.z - a.z, l2 = Mathf.Max(1e-4f, dx * dx + dz * dz);
            float k = Mathf.Clamp01(((t.x - a.x) * dx + (t.z - a.z) * dz) / l2); dist = FU.Hyp(a.x + dx * k - t.x, a.z + dz * k - t.z); return k;
        }
        /// First trunk the segment runs into; `t` is the fraction along a-b. Bullets stop here.
        public static bool TreeTrunk(Vector3 a, Vector3 b, out float t)
        {
            float best = 2;
            CellsAlong(a, b, lst =>
            {
                foreach (var tr in lst)
                {
                    if (!tr.alive) continue; float k = Near2D(a, b, tr, out float d); if (d > .3f * tr.s || k >= best) continue;
                    float y = a.y + (b.y - a.y) * k, g = H(tr.x, tr.z); if (y < g - .5f || y > g + 3f * tr.s) continue; best = k;
                }
            });
            t = best; return best <= 1;
        }
        /// How many crowns the segment passes through (stops counting at `max`).
        public static int Canopies(Vector3 a, Vector3 b, int max)
        {
            int n = 0;
            CellsAlong(a, b, lst =>
            {
                if (n >= max) return;
                foreach (var tr in lst)
                {
                    if (!tr.alive) continue; float k = Near2D(a, b, tr, out float d); if (d > 2f * tr.s) continue;
                    float y = a.y + (b.y - a.y) * k, g = H(tr.x, tr.z); if (y < g + 1.8f * tr.s || y > g + 7f * tr.s) continue; if (++n >= max) return;
                }
            });
            return n;
        }
        /// Where along a-b a round first enters a crown (1 = never). Leaves and branches can turn a bullet.
        public static float FirstCanopy(Vector3 a, Vector3 b)
        {
            float best = 1;
            CellsAlong(a, b, lst =>
            {
                foreach (var tr in lst)
                {
                    if (!tr.alive) continue; float k = Near2D(a, b, tr, out float d); if (d > 2f * tr.s || k >= best) continue;
                    float y = a.y + (b.y - a.y) * k, g = H(tr.x, tr.z); if (y < g + 1.8f * tr.s || y > g + 7f * tr.s) continue; best = k;
                }
            });
            return best;
        }
    }
}
