// Fall Weiss — enterable buildings (port of makeBd / renderBd / breakPiece / collapseBd) and their navigation
// (bdCtx / roomIdx / bdPath / segRect / avoidBd / routeBd), plus floor surfaces (surfY).
// Houses: one room with door + windows. Offices: 1-2 storeys, central corridor, side rooms, outside stair.
// Halls: big open sheds with pillars, machinery and four doors. Every wall piece is a breakable obstacle.
using System.Collections.Generic;
using UnityEngine;

namespace FallWeiss
{
    public class Bd
    {
        public float cx, cz, w, d, x0, x1, z0, z1, baseY, hp, th, WH, FH, H;
        public string type, k; public int floors, wall, roofC, floorHex, ceilHex, wallCount, deadWalls; public bool gable, alive = true, roofGone;
        public readonly List<Obst> pieces = new List<Obst>();
        public readonly List<Deco> deco = new List<Deco>();
        public readonly List<Room> rooms = new List<Room>();
        public readonly List<NavNode> nodes = new List<NavNode>();
        public readonly List<Surf> surf = new List<Surf>();
        public GameObject go;
    }
    public class Deco { public float w, h, d, x, y, z; public int hex; public Obst owner; }
    public class Room { public float x0, x1, z0, z1; public int lv; }
    public class NavNode { public float x, z; public int lv, r; public bool outside; public readonly List<int> n = new List<int>(); }
    public class Surf { public float x0, x1, z0, z1, dy, dya, dyb, y, ya, yb; public bool ramp; public char ax; public Bd bd; }
    public class RoutePlan { public string key; public float t; public List<NavNode> wps; public int i; }

    public static class Interiors
    {
        public static readonly List<Bd> bds = new List<Bd>();
        public static readonly List<Surf> surfs = new List<Surf>();
        class Op { public float a, w, dh = 2.4f, sill, top; public bool door; }
        static Op Door(float a, float w, float dh) => new Op { a = a, w = w, door = true, dh = dh };
        static Op Win(float a, float w, float sill, float top) => new Op { a = a, w = w, sill = sill, top = top };

        /// Which original building kinds are enterable: kind -> (type, hp, floors override, thick, slits).
        public static readonly Dictionary<string, (string type, float hp, int floors, float thick, bool slits)> ENTER = new Dictionary<string, (string, float, int, float, bool)>
        {
            ["house"] = ("house", 170, 0, 0, false), ["guardhouse"] = ("house", 260, 0, 0, false), ["villa"] = ("office", 240, 0, 0, false), ["barracks"] = ("office", 260, 0, 0, false),
            ["admin"] = ("office", 320, 0, 0, false), ["warehouse"] = ("hall", 400, 0, 0, false), ["factory"] = ("hall", 480, 0, 0, false), ["barn"] = ("hall", 210, 0, 0, false), ["city"] = ("office", 300, 0, 0, false),
            ["tenement"] = ("office", 560, 0, 0, false), ["postoffice"] = ("office", 640, 2, 0, false), ["gate"] = ("office", 760, 2, 0, false), ["fortbarracks"] = ("office", 660, 2, 0, false),
            ["casemate"] = ("hall", 1500, 0, .9f, true), ["engineshed"] = ("hall", 520, 0, 0, false), ["sorting"] = ("hall", 560, 0, 0, false), ["garage"] = ("hall", 480, 0, 0, false), ["arsenal"] = ("hall", 700, 0, 0, false),
        };

        public static void Clear() { foreach (var b in bds) if (b.go) Object.Destroy(b.go); bds.Clear(); surfs.Clear(); }

        public static Bd At(float x, float z) { foreach (var b in bds) if (b.alive && x > b.x0 && x < b.x1 && z > b.z0 && z < b.z1) return b; return null; }

        // ---------------- construction (port of makeBd) ----------------
        public static Bd Make(float ox, float oz, float ow, float od, float oh, int wall, int roof, bool gable, string k, string type, float hp, int floors, float thick, bool slits, System.Action<float, float, float, float> zone)
        {
            float th = thick > 0 ? thick : (type == "hall" ? .5f : .4f);
            var bd = new Bd { cx = ox, cz = oz, w = ow, d = od, x0 = ox - ow / 2, x1 = ox + ow / 2, z0 = oz - od / 2, z1 = oz + od / 2, type = type, floors = Mathf.Max(1, floors), wall = wall, roofC = roof != 0 ? roof : 0x5a3a2a, k = k, gable = gable, hp = hp, th = th, floorHex = 0x6a5638, ceilHex = 0xd8d0bc };
            bd.WH = type == "hall" ? Mathf.Max(4.6f, oh > 0 ? oh : 6.5f) : 3.1f; bd.FH = type == "hall" ? bd.WH + .3f : 3.4f; bd.H = bd.FH * bd.floors; bds.Add(bd);
            zone(ox, oz, bd.floors > 1 ? FU.Hyp(Mathf.Max(ow, od) / 2 + 3.5f, 9.2f) + 1 : Mathf.Max(ow, od) / 2 + 3, 8);
            int hex = wall;
            Obst P(float x0, float x1, float z0, float z1, float ry0, float ry1, string pk, int c, float php = 0)
            {
                if (x1 - x0 < .02f || z1 - z0 < .02f || ry1 - ry0 < .02f) return null;
                var b = World.AddObst(x0, x1, z0, z1, ry1 - ry0, false, php > 0 ? php : bd.hp); b.rel = true; b.bd = bd; b.ry0 = ry0; b.ry1 = ry1; b.pk = pk; b.hex = c;
                if (pk == "floor") { b.floor = true; b.nocover = true; } else if (pk == "roof") b.nocover = true; else if (pk == "lintel") { b.ceil = true; b.nocover = true; } else if (pk == "prop") b.low = true;
                bd.pieces.Add(b); if (pk == "wall" || pk == "sill" || pk == "lintel" || pk == "pillar") bd.wallCount++; return b;
            }
            void D(float w, float h, float d, float x, float y, float z, int c, Obst owner = null) => bd.deco.Add(new Deco { w = w, h = h, d = d, x = x, y = y, z = z, hex = c, owner = owner });
            int Node(float x, float z, int lv, bool outside, int r = -1) { bd.nodes.Add(new NavNode { x = x, z = z, lv = lv, outside = outside, r = r }); return bd.nodes.Count - 1; }
            void Link(int i, int j) { bd.nodes[i].n.Add(j); bd.nodes[j].n.Add(i); }
            int RoomA(float x0, float x1, float z0, float z1, int lv) { bd.rooms.Add(new Room { x0 = x0, x1 = x1, z0 = z0, z1 = z1, lv = lv }); return bd.rooms.Count - 1; }
            void Run(char axis, float c, float a0, float a1, int f, List<Op> ops, float t = 0)
            {   // wall along a world axis at fixed coordinate c, with door / window openings
                if (t <= 0) t = th; float yb = bd.FH * f, WH = bd.WH; ops.Sort((p, q) => p.a.CompareTo(q.a)); float cur = a0;
                Obst Seg(float s0, float s1, float r0, float r1, string pk) { if (s1 - s0 < .04f) return null; return axis == 'x' ? P(s0, s1, c - t / 2, c + t / 2, yb + r0, yb + r1, pk, hex) : P(c - t / 2, c + t / 2, s0, s1, yb + r0, yb + r1, pk, hex); }
                void Dec(float w, float h, float d, float a, float y, Obst owner, int c2) { if (axis == 'x') D(w, h, d, a, yb + y, c, c2, owner); else D(d, h, w, c, yb + y, a, c2, owner); }
                foreach (var q in ops)
                {
                    float h0 = q.a - q.w / 2, h1 = q.a + q.w / 2; Seg(cur, h0, 0, WH, "wall");
                    if (q.door) { var lp = Seg(h0, h1, q.dh, WH, "lintel"); Dec(.12f, q.dh, t + .08f, h0 + .06f, q.dh / 2, lp, 0x3a3028); Dec(.12f, q.dh, t + .08f, h1 - .06f, q.dh / 2, lp, 0x3a3028); Dec(q.w + .1f, .14f, t + .08f, q.a, q.dh + .05f, lp, 0x3a3028); }
                    else
                    {
                        var sp = Seg(h0, h1, 0, q.sill, "sill"); var lp = Seg(h0, h1, q.top, WH, "lintel");
                        Dec(q.w + .3f, .08f, t + .3f, q.a, q.sill + .04f, sp, 0xcfc6b2); Dec(.07f, q.top - q.sill, t + .06f, h0 + .035f, (q.sill + q.top) / 2, sp, 0x4a4036); Dec(.07f, q.top - q.sill, t + .06f, h1 - .035f, (q.sill + q.top) / 2, sp, 0x4a4036);
                        Dec(q.w, .06f, t + .04f, q.a, q.top - .03f, lp, 0x4a4036); Dec(.04f, q.top - q.sill, t + .02f, q.a, (q.sill + q.top) / 2, sp, 0x4a4036);
                    }
                    cur = h1;
                }
                Seg(cur, a1, 0, WH, "wall");
            }
            void Slab(int f, string pk, int c) => P(bd.x0, bd.x1, bd.z0, bd.z1, bd.FH * f - .3f, bd.FH * f, pk, c, 900);
            void Table(float x, float z, float w, float d) { var b = P(x - w / 2, x + w / 2, z - d / 2, z + d / 2, 0, .85f, "prop", 0x6a4e30, 70); if (b != null) D(w + .06f, .06f, d + .06f, x, .85f, z, 0x7a5e3c); }
            void FloorDeco(int f, int c) => D(ow - 2 * th, .07f, od - 2 * th, ox, bd.FH * f + .04f, oz, c);
            float R() => Random.value;

            if (type == "house")
            {
                float dx = ox + (R() - .5f) * Mathf.Max(0, ow - 3.6f);
                var front = new List<Op> { Door(dx, 1.3f, 2.3f) }; if (ow > 6.6f) front.Add(Win(dx + (dx > ox ? -2.6f : 2.6f), 1.2f, 1.0f, 2.2f));
                Run('x', bd.z1 - th / 2, bd.x0, bd.x1, 0, front);
                Run('x', bd.z0 + th / 2, bd.x0, bd.x1, 0, new List<Op> { Win(ox, 1.3f, 1.0f, 2.3f) });
                Run('z', bd.x0 + th / 2, bd.z0 + th, bd.z1 - th, 0, new List<Op> { Win(oz, 1.2f, 1.0f, 2.2f) }); Run('z', bd.x1 - th / 2, bd.z0 + th, bd.z1 - th, 0, new List<Op> { Win(oz, 1.2f, 1.0f, 2.2f) });
                P(bd.x0, bd.x1, bd.z0, bd.z1, bd.H - .3f, bd.H, "roof", hex, 700); FloorDeco(0, bd.floorHex);
                RoomA(bd.x0 + th, bd.x1 - th, bd.z0 + th, bd.z1 - th, 0); int ni = Node(dx, bd.z1 - th - 1, 0, false, 0), no = Node(dx, bd.z1 + 1.9f, 0, true), nc = Node(ox, oz, 0, false, 0); Link(ni, no); Link(ni, nc);
                Table(ox + (ow > 7 ? -1.4f : 0), oz - .6f, 1.3f, .8f); D(1.0f, .5f, 1.9f, bd.x0 + th + .6f, .27f, bd.z0 + th + 1.3f, 0x8a7a5a); D(.8f, 1.1f, .6f, bd.x1 - th - .5f, .6f, bd.z0 + th + .5f, 0x2a2a2a);
            }
            else if (type == "office")
            {
                bool alongX = ow >= od; float a = alongX ? ow : od, b = alongX ? od : ow, cw = 2.4f; int nb = Mathf.Max(2, Mathf.RoundToInt((a - 1) / 4.6f)); float bl = (a - 2 * th) / nb, ux0 = -a / 2 + th;
                float WU(float u) => (alongX ? ox : oz) + u; float WV(float v) => (alongX ? oz : ox) + v; char axU = alongX ? 'x' : 'z', axV = alongX ? 'z' : 'x';
                float WX(float u, float v) => alongX ? ox + u : ox + v; float WZ(float u, float v) => alongX ? oz + v : oz + u; float UC(int i) => ux0 + bl * (i + .5f);
                var cn = new List<(int eP, int eM)>();
                for (int f = 0; f < bd.floors; f++)
                {
                    var wins = new List<Op>(); for (int i = 0; i < nb; i++) wins.Add(Win(WU(UC(i)), 1.3f, 1.0f, 2.3f));
                    Run(axU, WV(b / 2 - th / 2), WU(-a / 2), WU(a / 2), f, new List<Op>(wins)); Run(axU, WV(-b / 2 + th / 2), WU(-a / 2), WU(a / 2), f, new List<Op>(wins));
                    Run(axV, WU(a / 2 - th / 2), WV(-b / 2 + th), WV(b / 2 - th), f, new List<Op> { Door(WV(0), 1.9f, 2.4f) });
                    Run(axV, WU(-a / 2 + th / 2), WV(-b / 2 + th), WV(b / 2 - th), f, f == 0 ? new List<Op> { Door(WV(0), 1.9f, 2.4f) } : new List<Op> { Win(WV(0), 1.3f, 1.0f, 2.3f) });
                    var doors = new List<Op>(); for (int i = 0; i < nb; i++) doors.Add(Door(WU(UC(i)), 1.6f, 2.3f));
                    Run(axU, WV(cw / 2), WU(-a / 2 + th), WU(a / 2 - th), f, new List<Op>(doors), .25f); Run(axU, WV(-cw / 2), WU(-a / 2 + th), WU(a / 2 - th), f, new List<Op>(doors), .25f);
                    for (int i = 1; i < nb; i++) { float u = ux0 + bl * i; Run(axV, WU(u), WV(cw / 2 + .125f), WV(b / 2 - th), f, new List<Op>(), .25f); Run(axV, WU(u), WV(-b / 2 + th), WV(-cw / 2 - .125f), f, new List<Op>(), .25f); }
                    if (f < bd.floors - 1) { Slab(f + 1, "floor", bd.ceilHex); bd.surf.Add(new Surf { x0 = bd.x0 + th, x1 = bd.x1 - th, z0 = bd.z0 + th, z1 = bd.z1 - th, dy = bd.FH * (f + 1), bd = bd }); }
                    FloorDeco(f, f > 0 ? 0x6a5638 : bd.floorHex);
                    int rcor = RoomA(alongX ? bd.x0 + th : ox - cw / 2 + .1f, alongX ? bd.x1 - th : ox + cw / 2 - .1f, alongX ? oz - cw / 2 + .1f : bd.z0 + th, alongX ? oz + cw / 2 - .1f : bd.z1 - th, f);
                    var row = new List<int>(); for (int i = 0; i < nb; i++) row.Add(Node(WX(UC(i), 0), WZ(UC(i), 0), f, false, rcor)); for (int i = 1; i < nb; i++) Link(row[i - 1], row[i]);
                    int eP = Node(WX(a / 2 - th - 1, 0), WZ(a / 2 - th - 1, 0), f, false, rcor), eM = Node(WX(-a / 2 + th + 1, 0), WZ(-a / 2 + th + 1, 0), f, false, rcor); Link(eP, row[nb - 1]); Link(eM, row[0]); cn.Add((eP, eM));
                    for (int i = 0; i < nb; i++) foreach (int sd in new[] { -1, 1 })
                        {
                            float u0 = ux0 + bl * i + .125f, u1 = ux0 + bl * (i + 1) - .125f, v0 = sd > 0 ? cw / 2 + .125f : -b / 2 + th, v1 = sd > 0 ? b / 2 - th : -cw / 2 - .125f;
                            int rr = RoomA(alongX ? WU(u0) : WV(v0), alongX ? WU(u1) : WV(v1), alongX ? WV(v0) : WU(u0), alongX ? WV(v1) : WU(u1), f);
                            int rn = Node(WX((u0 + u1) / 2, (v0 + v1) / 2), WZ((u0 + u1) / 2, (v0 + v1) / 2), f, false, rr); Link(rn, row[i]);
                            if (R() < .7f) { float tx = WU((u0 + u1) / 2 + (R() - .5f)), tz = WV((v0 + v1) / 2 + (R() - .5f) * 1.2f); Table(alongX ? tx : tz, alongX ? tz : tx, 1.3f, .8f); }
                        }
                }
                P(bd.x0, bd.x1, bd.z0, bd.z1, bd.H - .3f, bd.H, "roof", hex, 800);
                int o0 = Node(WX(-a / 2 - 2.6f, 0), WZ(-a / 2 - 2.6f, 0), 0, true), o1 = Node(WX(a / 2 + 2.6f, 0), WZ(a / 2 + 2.6f, 0), 0, true); Link(o0, cn[0].eM); Link(o1, cn[0].eP);
                if (bd.floors > 1)
                {   // external stair on the +u end wall up to the upper corridor door
                    float su0 = a / 2 + .5f, su1 = a / 2 + 2.1f, sv0 = -7.8f, sv1 = -1.0f;
                    (float, float) W2(float uu, float vv) => (alongX ? ox + uu : ox + vv, alongX ? oz + vv : oz + uu);
                    var (xa, za) = W2(su0, sv0); var (xb, zb) = W2(su1, sv1);
                    bd.surf.Add(new Surf { x0 = Mathf.Min(xa, xb), x1 = Mathf.Max(xa, xb), z0 = Mathf.Min(za, zb), z1 = Mathf.Max(za, zb), ramp = true, ax = alongX ? 'z' : 'x', dya = 0, dyb = bd.FH, bd = bd });
                    var (xc, zc) = W2(a / 2 + .2f, -1.0f); var (xd, zd) = W2(a / 2 + 2.1f, 1.2f);
                    bd.surf.Add(new Surf { x0 = Mathf.Min(xc, xd), x1 = Mathf.Max(xc, xd), z0 = Mathf.Min(zc, zd), z1 = Mathf.Max(zc, zd), dy = bd.FH, bd = bd });
                    void Dl(float us, float hs, float vs, float u, float y, float v, int c) { var (x, z) = W2(u, v); D(alongX ? us : vs, hs, alongX ? vs : us, x, y, z, c); }
                    int N = 14; float rise = bd.FH / N, run1 = (sv1 - sv0) / N; for (int kk = 0; kk < N; kk++) Dl(1.6f, (kk + 1) * rise, run1 + .02f, a / 2 + 1.3f, (kk + 1) * rise / 2, sv0 + (kk + .5f) * run1, 0x77736a);
                    Dl(1.9f, .2f, 2.3f, a / 2 + 1.15f, bd.FH - .1f, .1f, 0x6a665e); Dl(.08f, .9f, 6.9f, a / 2 + .5f, bd.FH * .5f + .45f, -4.4f, 0x3a342c); Dl(.08f, .9f, 6.9f, a / 2 + 2.1f, bd.FH * .5f + .45f, -4.4f, 0x3a342c);
                    var (sbx, sbz) = W2(a / 2 + 1.3f, -8.7f); var (slx, slz) = W2(a / 2 + 1.6f, 0);
                    int sb = Node(sbx, sbz, 0, true), sl = Node(slx, slz, 1, true); Link(sb, sl); Link(sl, cn[1].eP); Link(o0, o1); Link(o1, sb); Link(o0, sb);
                }
            }
            else
            {   // hall
                bool alongX = ow >= od; float a = alongX ? ow : od, b = alongX ? od : ow, WH = bd.WH;
                float WU(float u) => (alongX ? ox : oz) + u; float WV(float v) => (alongX ? oz : ox) + v; char axU = alongX ? 'x' : 'z', axV = alongX ? 'z' : 'x';
                var wins = new List<Op>(); for (float u = -a / 2 + 4.5f; u < a / 2 - 3; u += slits ? 5 : 6.5f) if (Mathf.Abs(u) > 3.2f) wins.Add(slits ? Win(WU(u), .7f, 1.2f, 1.9f) : Win(WU(u), 1.7f, 1.0f, 2.5f));
                var s1 = new List<Op>(wins) { Door(WU(0), 2.6f, 2.7f) }; var s2 = new List<Op>(wins) { Door(WU(0), 2.6f, 2.7f) };
                Run(axU, WV(b / 2 - th / 2), WU(-a / 2), WU(a / 2), 0, s1); Run(axU, WV(-b / 2 + th / 2), WU(-a / 2), WU(a / 2), 0, s2);
                Run(axV, WU(a / 2 - th / 2), WV(-b / 2 + th), WV(b / 2 - th), 0, new List<Op> { Door(WV(0), 3.8f, Mathf.Min(3.6f, WH - .6f)) }); Run(axV, WU(-a / 2 + th / 2), WV(-b / 2 + th), WV(b / 2 - th), 0, new List<Op> { Door(WV(0), 3.8f, Mathf.Min(3.6f, WH - .6f)) });
                P(bd.x0, bd.x1, bd.z0, bd.z1, bd.H - .3f, bd.H, "roof", roof != 0 ? roof : 0x4a4a4a, 1000); FloorDeco(0, 0x5e5b55);
                for (float u = -a / 2 + 7; u < a / 2 - 5; u += 8) foreach (int sv in new[] { -1, 1 }) { float cx = WU(u), cz = WV(sv * b / 4); P(alongX ? cx - .5f : cz - .5f, alongX ? cx + .5f : cz + .5f, alongX ? cz - .5f : cx - .5f, alongX ? cz + .5f : cx + .5f, 0, WH, "pillar", 0x8a867c, 700); }
                int nm = Mathf.FloorToInt(a * b / 75); int[] cols = { 0x6a6e6c, 0x7a5a3a, 0x4a5a4a, 0x7a6a30 };
                for (int kk = 0; kk < nm; kk++)
                {
                    float u = (R() - .5f) * (a - 10), v = (R() - .5f) * (b - 6); if (Mathf.Abs(v) < 2.2f) continue;
                    float cx = alongX ? WU(u) : WV(v), cz = alongX ? WV(v) : WU(u), w1 = 1.6f + R() * 1.8f, d1 = 1.2f + R() * 1.4f, hh = 1.1f + R() * .8f, ww = alongX ? w1 : d1, dd = alongX ? d1 : w1;
                    var pb = P(cx - ww / 2, cx + ww / 2, cz - dd / 2, cz + dd / 2, 0, hh, "prop", cols[kk % 4], 100); if (pb != null) D(ww * .6f, .25f, dd * .6f, cx, hh + .12f, cz, 0x2a2a2a);
                }
                RoomA(bd.x0 + th, bd.x1 - th, bd.z0 + th, bd.z1 - th, 0); int nc = Node(ox, oz, 0, false, 0);
                var ends = new[] { new[] { WU(a / 2 - th - 1.2f), WV(0), WU(a / 2 + 2.6f), WV(0) }, new[] { WU(-a / 2 + th + 1.2f), WV(0), WU(-a / 2 - 2.6f), WV(0) }, new[] { WU(0), WV(b / 2 - th - 1.2f), WU(0), WV(b / 2 + 2.6f) }, new[] { WU(0), WV(-b / 2 + th + 1.2f), WU(0), WV(-b / 2 - 2.6f) } };
                foreach (var e in ends) { int ni = Node(alongX ? e[0] : e[1], alongX ? e[1] : e[0], 0, false, 0), no = Node(alongX ? e[2] : e[3], alongX ? e[3] : e[2], 0, true); Link(ni, no); Link(ni, nc); }
                for (float u = -a / 2 + 3; u < a / 2 - 2; u += 6) { if (alongX) D(2.6f, .3f, b - 2 * th - .6f, WU(u), bd.WH + .15f, WV(0), 0x3a3c3e); else D(b - 2 * th - .6f, .3f, 2.6f, WV(0), bd.WH + .15f, WU(u), 0x3a3c3e); }
            }
            return bd;
        }

        /// After the heightmap is baked: absolute heights for pieces/surfaces, colliders, mesh.
        public static void Finish(Transform root)
        {
            foreach (var bd in bds)
            {
                bd.baseY = World.H(bd.cx, bd.cz);
                foreach (var b in bd.pieces) { b.y0 = bd.baseY + b.ry0; b.y1 = bd.baseY + b.ry1; }
                foreach (var s in bd.surf) { if (s.ramp) { s.ya = bd.baseY + s.dya; s.yb = bd.baseY + s.dyb; } else s.y = bd.baseY + s.dy; surfs.Add(s); }
                Render(bd, root);
            }
        }

        public static void Render(Bd bd, Transform root = null)
        {
            var parent = root ? root : (bd.go ? bd.go.transform.parent : World.root);
            if (bd.go) Object.Destroy(bd.go);
            var mb = new MeshBatch(); float by = bd.baseY;
            foreach (var b in bd.pieces) { if (b.dead || (b.pk == "roof" && bd.gable)) continue; mb.Box(new Vector3((b.x0 + b.x1) / 2, by + (b.ry0 + b.ry1) / 2, (b.z0 + b.z1) / 2), new Vector3(b.x1 - b.x0, b.ry1 - b.ry0, b.z1 - b.z0), 0, FU.Hex(b.hex)); }
            foreach (var dc in bd.deco) { if (dc.owner != null && dc.owner.dead) continue; mb.Box(new Vector3(dc.x, by + dc.y, dc.z), new Vector3(dc.w, dc.h, dc.d), 0, FU.Hex(dc.hex)); }
            if (bd.gable && !bd.roofGone)
            {
                float rh = Mathf.Clamp(Mathf.Min(bd.w, bd.d) * .4f, 1.4f, 3.8f), ry = by + bd.H - .3f; mb.Gable(new Vector3(bd.cx, ry, bd.cz), bd.w, bd.d, rh, 0, FU.Hex(bd.roofC)); mb.GableEnds(new Vector3(bd.cx, ry, bd.cz), bd.w, bd.d, rh, 0, FU.Hex(bd.wall));
                if (bd.type == "house") mb.Box(new Vector3(bd.cx + (bd.w >= bd.d ? bd.w * .22f : 0), ry + rh * .45f, bd.cz + (bd.w >= bd.d ? 0 : bd.d * .22f)), new Vector3(.6f, rh * .8f + .8f, .6f), 0, FU.Hex(0x6a5a4a));
            }
            bd.go = mb.Build("Building " + bd.k, parent, Mats.VC);
        }

        // ---------------- destruction ----------------
        public static void BreakPiece(Game g, Obst b)
        {
            if (b.dead) return; b.dead = true; if (b.col) Object.Destroy(b.col); var bd = b.bd;
            var c = new Vector3((b.x0 + b.x1) / 2, bd.baseY + (b.ry0 + b.ry1) / 2, (b.z0 + b.z1) / 2);
            for (int k = 0; k < 5; k++) g.fx.Puff(c + Random.insideUnitSphere * .6f, new Color(.66f, .61f, .53f, .8f), .8f, 2.6f, FU.Rand(1, 2), new Vector3(FU.Rand(-1.5f, 1.5f), FU.Rand(.5f, 2), 0));
            for (int k = 0; k < 4; k++) g.fx.Puff(c, new Color(.42f, .35f, .28f, 1), .25f, .2f, FU.Rand(.5f, .9f), new Vector3(FU.Rand(-4, 4), FU.Rand(2, 6), FU.Rand(-4, 4)), false, 22);
            if (b.pk == "wall" || b.pk == "sill" || b.pk == "lintel" || b.pk == "pillar") bd.deadWalls++; g.audio.Shot("boom", c, .6f); g.audio.Shot("debris", c, .8f);
            if (b.pk == "sill" || b.pk == "lintel") g.audio.Shot("glass", c, .9f);   // window frames: the panes go with them
            if (b.pk == "roof") bd.roofGone = true; Render(bd);
            if (bd.deadWalls > bd.wallCount * .55f && bd.alive) Collapse(g, bd);
        }
        public static void Collapse(Game g, Bd bd)
        {
            bd.alive = false; foreach (var b in bd.pieces) { if (b.dead) continue; b.dead = true; if (b.col) Object.Destroy(b.col); }
            surfs.RemoveAll(s => s.bd == bd); if (bd.go) Object.Destroy(bd.go); bd.go = null;
            Structures.Rubble(g, bd.x0, bd.x1, bd.z0, bd.z1, bd.baseY, bd.H, true);
            foreach (var u in g.units.ToArray()) if (u.alive && u.x > bd.x0 && u.x < bd.x1 && u.z > bd.z0 && u.z < bd.z1) Combat.Damage(g, u, 55, true, false, null, true);
        }

        // ---------------- surfaces (port of surfY) ----------------
        public static float SurfY(float x, float z, float y)
        {
            float h = World.H(x, z);
            for (int i = 0; i < surfs.Count; i++)
            {
                var f = surfs[i]; if (x < f.x0 || x > f.x1 || z < f.z0 || z > f.z1) continue; float fy = f.y;
                if (f.ramp) { float t = Mathf.Clamp01(f.ax == 'x' ? (x - f.x0) / (f.x1 - f.x0) : (z - f.z0) / (f.z1 - f.z0)); fy = Mathf.Lerp(f.ya, f.yb, t); }
                if (fy <= y + .8f && fy > h) h = fy;
            }
            return h;
        }

        // ---------------- navigation (port of bdCtx / roomIdx / bdPath / segRect / avoidBd / routeBd) ----------------
        public struct Ctx { public Bd bd; public bool inside; public int lv; }
        static Ctx? BdCtx(float x, float z, float? y, int? lvHint)
        {
            foreach (var bd in bds) { if (!bd.alive) continue; if (x > bd.x0 && x < bd.x1 && z > bd.z0 && z < bd.z1) return new Ctx { bd = bd, inside = true, lv = lvHint ?? (y.HasValue && y.Value - bd.baseY > 2.4f ? 1 : 0) }; }
            if (y.HasValue) foreach (var bd in bds) { if (!bd.alive || bd.floors < 2 || y.Value - bd.baseY < 2.4f) continue; if (x > bd.x0 - 12 && x < bd.x1 + 12 && z > bd.z0 - 12 && z < bd.z1 + 12) return new Ctx { bd = bd, inside = false, lv = 1 }; }
            return null;
        }
        static int RoomIdx(Bd bd, float x, float z, int lv) { for (int i = 0; i < bd.rooms.Count; i++) { var r = bd.rooms[i]; if (r.lv == lv && x > r.x0 && x < r.x1 && z > r.z0 && z < r.z1) return i; } return -1; }
        static List<NavNode> BdPath(Bd bd, float sx, float sz, int sLv, bool sIn, float tx, float tz, int tLv, bool tIn)
        {
            var N = bd.nodes; int n = N.Count; var dist = new float[n]; var prev = new int[n]; var done = new bool[n]; int rs = sIn ? RoomIdx(bd, sx, sz, sLv) : -1, rt = tIn ? RoomIdx(bd, tx, tz, tLv) : -1;
            for (int i = 0; i < n; i++) { dist[i] = 1e9f; prev[i] = -2; var a = N[i]; if (a.lv != sLv) continue; if (sIn ? (a.r == rs && !a.outside) : a.outside) { dist[i] = FU.Hyp(a.x - sx, a.z - sz); prev[i] = -1; } }
            for (int it = 0; it < n; it++)
            {
                int u = -1; float du = 1e9f; for (int i = 0; i < n; i++) if (!done[i] && dist[i] < du) { du = dist[i]; u = i; } if (u < 0) break; done[u] = true;
                foreach (var v in N[u].n) { float w = du + FU.Hyp(N[u].x - N[v].x, N[u].z - N[v].z); if (w < dist[v]) { dist[v] = w; prev[v] = u; } }
            }
            float best = 1e9f; int bi = -1;
            for (int i = 0; i < n; i++) { var a = N[i]; if (a.lv != tLv || dist[i] > 1e8f) continue; if (tIn ? (a.r == rt && !a.outside) : a.outside) { float c = dist[i] + FU.Hyp(a.x - tx, a.z - tz); if (c < best) { best = c; bi = i; } } }
            if (bi < 0) return null; var outp = new List<NavNode>(); for (int i = bi; i >= 0; i = prev[i]) outp.Add(N[i]); outp.Reverse(); return outp;
        }
        public static bool SegRect(float ax, float az, float bx, float bz, float x0, float x1, float z0, float z1)
        {
            float t0 = 0, t1 = 1, dx = bx - ax, dz = bz - az; float[] P = { -dx, dx, -dz, dz }, Q = { ax - x0, x1 - ax, az - z0, z1 - az };
            for (int i = 0; i < 4; i++)
            {
                if (Mathf.Abs(P[i]) < 1e-9f) { if (Q[i] < 0) return false; }
                else { float r = Q[i] / P[i]; if (P[i] < 0) { if (r > t1) return false; if (r > t0) t0 = r; } else { if (r < t0) return false; if (r < t1) t1 = r; } }
            }
            return true;
        }
        public static Vector2 AvoidBd(Unit u, float tx, float tz)
        {
            if (bds.Count == 0) return new Vector2(tx, tz); float sx = u.x, sz = u.z; if (FU.Hyp(tx - sx, tz - sz) < 3) return new Vector2(tx, tz);
            Bd best = null; float bd0 = 1e9f;
            foreach (var bd in bds)
            {
                if (!bd.alive) continue; float x0 = bd.x0 - .8f, x1 = bd.x1 + .8f, z0 = bd.z0 - .8f, z1 = bd.z1 + .8f;
                if ((sx > x0 && sx < x1 && sz > z0 && sz < z1) || (tx > x0 && tx < x1 && tz > z0 && tz < z1)) continue;
                if (!SegRect(sx, sz, tx, tz, x0, x1, z0, z1)) continue; float dd = FU.Hyp(bd.cx - sx, bd.cz - sz); if (dd < bd0) { bd0 = dd; best = bd; }
            }
            if (best == null) return new Vector2(tx, tz); float m = 3.4f, X0 = best.x0 - .8f, X1 = best.x1 + .8f, Z0 = best.z0 - .8f, Z1 = best.z1 + .8f;
            var cs = new[] { new Vector2(best.x0 - m, best.z0 - m), new Vector2(best.x1 + m, best.z0 - m), new Vector2(best.x1 + m, best.z1 + m), new Vector2(best.x0 - m, best.z1 + m) }; Vector2? bc = null; float bs = 1e9f;
            foreach (var c in cs) { if (SegRect(sx, sz, c.x, c.y, X0, X1, Z0, Z1)) continue; float sc = FU.Hyp(c.x - sx, c.y - sz) + FU.Hyp(tx - c.x, tz - c.y); if (sc < bs) { bs = sc; bc = c; } }
            return bc ?? new Vector2(tx, tz);
        }
        public static Vector2 RouteBd(Unit u, float tx, float tz, float gameT)
        {
            if (bds.Count == 0) return new Vector2(tx, tz);
            var cu = BdCtx(u.x, u.z, u.y, null); var ct = BdCtx(tx, tz, null, 0);
            if (cu == null && ct == null) return new Vector2(tx, tz);
            var bdp = cu.HasValue ? cu.Value.bd : ct.Value.bd;
            string key = (cu.HasValue ? cu.Value.bd.cx.ToString("F1") : "o") + "|" + (ct.HasValue ? ct.Value.bd.cx.ToString("F1") + ":" + ct.Value.lv : "o") + "|" + Mathf.RoundToInt(tx / 3) + "," + Mathf.RoundToInt(tz / 3);
            int ul = (u.y - bdp.baseY) > 2.4f ? 1 : 0;
            if (u.rp == null || u.rp.key != key || gameT - u.rp.t > 5)
            {
                List<NavNode> wps = null;
                if (cu.HasValue && ct.HasValue && cu.Value.bd == ct.Value.bd)
                {
                    var c1 = cu.Value; var c2 = ct.Value;
                    bool sameRoom = c1.inside && RoomIdx(c1.bd, u.x, u.z, c1.lv) == RoomIdx(c2.bd, tx, tz, c2.lv) && c1.lv == c2.lv;
                    if (!sameRoom) wps = BdPath(c1.bd, u.x, u.z, c1.lv, c1.inside, tx, tz, c2.lv, true);
                }
                else if (cu.HasValue) wps = BdPath(cu.Value.bd, u.x, u.z, cu.Value.lv, cu.Value.inside, tx, tz, 0, false);
                else wps = BdPath(ct.Value.bd, u.x, u.z, 0, false, tx, tz, ct.Value.lv, ct.Value.inside);
                int k = 0; if (wps != null) while (k < wps.Count - 1 && wps[k + 1].lv <= ul && FU.Hyp(u.x - wps[k + 1].x, u.z - wps[k + 1].z) < FU.Hyp(wps[k].x - wps[k + 1].x, wps[k].z - wps[k + 1].z)) k++;
                u.rp = new RoutePlan { key = key, t = gameT, wps = wps, i = k };
            }
            var rp = u.rp; var w = rp.wps; if (w == null) return new Vector2(tx, tz);
            while (rp.i < w.Count && FU.Hyp(w[rp.i].x - u.x, w[rp.i].z - u.z) < 1.3f) rp.i++;
            if (rp.i >= w.Count) return new Vector2(tx, tz);
            return new Vector2(w[rp.i].x, w[rp.i].z);
        }
    }

    /// Generic structure damage (port of damageStructures / collapse / crater / killTree / blastTrees) and AI cover points.
    public static class Structures
    {
        public static void Damage(Game g, Vector3 p, float r, float dmg)
        {
            var list = World.obst;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var b = list[i]; if (b.dead || b.hp <= 0 || b.soft) continue; float cx = Mathf.Clamp(p.x, b.x0, b.x1), cz = Mathf.Clamp(p.z, b.z0, b.z1), d = FU.Hyp(p.x - cx, p.z - cz);
                if (d > r + 1 || p.y > b.y1 + 8) continue;
                b.hp -= dmg * (1 - d / (r + 2)); g.fx.Puff(new Vector3(cx, Mathf.Min(Mathf.Max(p.y, b.y0 + 1), b.y1), cz), new Color(.72f, .67f, .56f, .7f), 1, 3.2f, 1.3f, Vector3.up * 1.5f);
                if (!b.smoking && b.hp < b.maxhp * .5f && !b.low && !b.rel) { b.smoking = true; g.AddBurning(b); }
                if (b.hp <= 0) Collapse(g, b);
            }
        }
        public static void Collapse(Game g, Obst b)
        {
            if (b.bd != null) { Interiors.BreakPiece(g, b); return; }
            if (b.dead) return; b.dead = true; Nav.dirty = true; if (b.col) Object.Destroy(b.col); if (b.vis) Object.Destroy(b.vis); g.RemoveBurning(b);
            if (b.link != null) foreach (var o in b.link) if (!o.dead) { o.dead = true; if (o.col) Object.Destroy(o.col); }
            if (b.kind == "fence" || b.kind == "hedge") { World.BreakNear(g, b.x0, b.x1, b.z0, b.z1); return; }
            float cx = (b.x0 + b.x1) / 2, cz = (b.z0 + b.z1) / 2;
            if (b.low) { for (int k = 0; k < 4; k++) g.fx.Puff(new Vector3(cx + FU.Rand(-2, 2), b.y0 + .6f, cz), new Color(.54f, .48f, .33f, .8f), .5f, 1.4f, 1, new Vector3(FU.Rand(-2, 2), 2, 0)); return; }
            Rubble(g, b.x0, b.x1, b.z0, b.z1, b.y0, b.h, b.kind != "ruin");
            foreach (var u in g.units.ToArray()) if (u.alive && FU.Hyp(u.x - cx, u.z - cz) < 5 && u.def.kind == "inf") Combat.Damage(g, u, 40, true, false, null, true);
        }
        public static void Rubble(Game g, float x0, float x1, float z0, float z1, float y0, float h, bool announce)
        {
            float cx = (x0 + x1) / 2, cz = (z0 + z1) / 2, w = x1 - x0, d = z1 - z0; int[] cols = { 0x5a5248, 0x6b6256, 0x3a3631, 0x7a6a58 };
            var mb = new MeshBatch(); mb.Box(new Vector3(cx, y0 + .25f, cz), new Vector3(w * .85f, .5f, d * .85f), 0, FU.Hex(0x4a443c));
            int n = Mathf.Min(26, 6 + Mathf.FloorToInt(w * d / 16)); for (int k = 0; k < n; k++) mb.Box(new Vector3(cx + FU.Rand(-w / 2, w / 2), y0 + FU.Rand(.3f, .9f), cz + FU.Rand(-d / 2, d / 2)), new Vector3(FU.Rand(.8f, 2.8f), FU.Rand(.5f, 1.6f), FU.Rand(.8f, 2.8f)), FU.Rand(0, 3), FU.Hex(cols[k % 4]));
            mb.Build("Rubble", World.root, Mats.VC);
            var rb = World.AddObst(x0, x1, z0, z1, 1.3f, true, 0); rb.soft = true; rb.y0 = y0; rb.y1 = y0 + 1.3f; World.MakeColliderPublic(rb);
            g.fx.Explosion(new Vector3(cx, y0 + 2, cz), Mathf.Max(5, Mathf.Min(w, d) * .6f));
            for (int k = 0; k < 14; k++) g.fx.Puff(new Vector3(cx + FU.Rand(-w / 2, w / 2), y0 + FU.Rand(1, h), cz + FU.Rand(-d / 2, d / 2)), new Color(.6f, .56f, .5f, .75f), 2, 7, FU.Rand(2, 4), new Vector3(FU.Rand(-2, 2), FU.Rand(1, 4), 0));
            g.audio.Shot("boom", new Vector3(cx, y0, cz)); g.audio.Shot("debris", new Vector3(cx, y0 + 2, cz)); g.audio.Shot("glass", new Vector3(cx, y0 + 2, cz), .8f); g.Shake(1.7f); if (announce) g.Msg("A building collapses!");
            World.coverDirty = true; Nav.dirty = true;
        }

        // ---------------- AI cover points (port of buildCoverPts / findCover) ----------------
        public class Cover { public float x, z, taken; public Vector2? n; public bool low, trench; public Obst b; public Unit by; }
        static readonly Dictionary<long, List<Cover>> grid = new Dictionary<long, List<Cover>>();
        static long Key(int i, int j) => ((long)i << 32) ^ (uint)j;
        public static void BuildCover()
        {
            grid.Clear();
            void Push(Cover p) { long k = Key(Mathf.FloorToInt(p.x / 20), Mathf.FloorToInt(p.z / 20)); if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<Cover>(); l.Add(p); }
            foreach (var b in World.obst)
            {
                if (b.dead || b.wire || b.nocover || b.floor) continue; bool low = b.low || b.h < 1.7f; float w = b.x1 - b.x0, d = b.z1 - b.z0, o = .95f;
                // low cover faces along its long axis (of the whole chain for sandbag walls built from short boxes); a box under 2 m gets one point at its middle.
                // Before, a box narrower than 2 m on x had no points at all, and low cover never had x-faces: every sandbag wall and north-south hedge or fence gave no cover.
                float W = w, D = d; if (b.link != null && b.link.Count > 1) { float lx0 = 1e9f, lx1 = -1e9f, lz0 = 1e9f, lz1 = -1e9f; foreach (var o2 in b.link) { lx0 = Mathf.Min(lx0, o2.x0); lx1 = Mathf.Max(lx1, o2.x1); lz0 = Mathf.Min(lz0, o2.z0); lz1 = Mathf.Max(lz1, o2.z1); } W = lx1 - lx0; D = lz1 - lz0; }
                bool zFaces = !low || W >= D, xFaces = !low || D > W;
                if (zFaces) for (float x = w >= 2 ? b.x0 + 1 : (b.x0 + b.x1) / 2; x <= Mathf.Max(b.x1 - 1, (b.x0 + b.x1) / 2) + .01f; x += Mathf.Max(4, w / 3)) { Push(new Cover { x = x, z = b.z1 + o, n = new Vector2(0, 1), low = low, b = b }); Push(new Cover { x = x, z = b.z0 - o, n = new Vector2(0, -1), low = low, b = b }); }
                if (xFaces) for (float z = d >= 2 ? b.z0 + 1 : (b.z0 + b.z1) / 2; z <= Mathf.Max(b.z1 - 1, (b.z0 + b.z1) / 2) + .01f; z += Mathf.Max(4, d / 3)) { Push(new Cover { x = b.x0 - o, z = z, n = new Vector2(-1, 0), low = low, b = b }); Push(new Cover { x = b.x1 + o, z = z, n = new Vector2(1, 0), low = low, b = b }); }
            }
            if (World.M.trenches != null) foreach (var t in World.M.trenches) for (int i = 0; i + 3 < t.pts.Length; i += 2)
                    {
                        float ax = t.pts[i], az = t.pts[i + 1], bx = t.pts[i + 2], bz = t.pts[i + 3], L = FU.Hyp(bx - ax, bz - az); int n = Mathf.Max(1, Mathf.RoundToInt(L / 5));
                        for (int k = 0; k < n; k++) Push(new Cover { x = Mathf.Lerp(ax, bx, k / (float)n), z = Mathf.Lerp(az, bz, k / (float)n), trench = true });
                    }
            World.coverDirty = false;
        }
        /// Every cover point within r of (x, z) — for the AI deployment pass (defenders pick positions before the battle starts).
        public static List<Cover> Near(float x, float z, float r)
        {
            if (World.coverDirty) BuildCover();
            var o = new List<Cover>(); int n = Mathf.CeilToInt(r / 20), cx = Mathf.FloorToInt(x / 20), cz = Mathf.FloorToInt(z / 20);
            for (int i = -n; i <= n; i++) for (int j = -n; j <= n; j++) if (grid.TryGetValue(Key(cx + i, cz + j), out var a)) foreach (var p in a) if (FU.Hyp(p.x - x, p.z - z) <= r) o.Add(p);
            return o;
        }
        public static Cover Find(Unit u, float fx, float fz, float t)
        {
            if (World.coverDirty) BuildCover();
            float ux = u.x, uz = u.z, de = FU.Hyp(fx - ux, fz - uz); int cx = Mathf.FloorToInt(ux / 20), cz = Mathf.FloorToInt(uz / 20); Cover best = null; float bs = 1e9f;
            for (int i = -1; i <= 1; i++) for (int j = -1; j <= 1; j++)
                {
                    if (!grid.TryGetValue(Key(cx + i, cz + j), out var a)) continue;
                    foreach (var p in a)
                    {
                        if (p.b != null && p.b.dead) continue; float d = FU.Hyp(p.x - ux, p.z - uz); if (d > 22) continue;
                        if (p.n.HasValue) { float ex = fx - p.x, ez = fz - p.z, l = Mathf.Max(.01f, FU.Hyp(ex, ez)); if ((p.n.Value.x * ex + p.n.Value.y * ez) / l > -.25f) continue; }
                        if (p.by != null && p.by != u && p.by.alive && t - p.taken < 14) continue;
                        if (World.InBuildingG(p.x, p.z, .3f) || World.InWater(p.x, p.z)) continue;
                        float dEn = FU.Hyp(fx - p.x, fz - p.z), sc = d + Mathf.Max(0, (de - dEn) * .6f) - (p.low ? 3 : 0) - (p.trench ? 4 : 0);
                        if (sc < bs) { bs = sc; best = p; }
                    }
                }
            if (best != null) { best.taken = t; best.by = u; }
            return best;
        }
    }
}
