// Fall Weiss — ground pathfinding. A 2 m grid over the map with A* (8-way, no corner cutting), separate passability for
// men and vehicles, and a per-frame search budget. Terrain (water depth, slope) is sampled once per map; walls, buildings,
// wire and trees are rasterised into an obstacle layer that is rebuilt whenever something is destroyed. Interiors keep
// their own door / stair graph (Interiors.RouteBd); Nav only routes outside buildings and hands over at the door node.
using System.Collections.Generic;
using UnityEngine;

namespace FallWeiss
{
    public static class Nav
    {
        public const float CELL = 2f;
        static int N; static float O;                 // cells per side, world coordinate of cell 0
        static byte[] terr;                           // terrain cost 1..9 per cell, 0 = impassable for everyone (deep water, map edge)
        static byte[] terrVeh;                        // vehicle terrain cost (fording limit, steep slopes)
        static byte[] blk;                            // obstacle bits: 1 = blocks men, 2 = blocks vehicles, 4 = tree (cost for men, crushed by tanks)
        static int version = -1; public static bool dirty = true;
        static int budget; static int frame = -1; const int PerFrame = 6, MaxExpand = 24000;
        public static int searches, fails;            // dbg stats

        public class Path { public List<Vector2> pts = new List<Vector2>(); public int i; public float t; public Vector2 goal; public bool straight; public float retry; }

        static int Ix(float x) => Mathf.Clamp(Mathf.FloorToInt((x - O) / CELL), 0, N - 1);
        static float Wx(int i) => O + (i + .5f) * CELL;

        public static void Clear() { terr = null; blk = null; N = 0; dirty = true; }

        /// Terrain layer: once per map (World.Build). Everything else is a rebuild of the obstacle layer.
        public static void Build()
        {
            var M = World.M; N = Mathf.CeilToInt(2 * M.half / CELL) + 1; O = -M.half - CELL * .5f;
            terr = new byte[N * N]; terrVeh = new byte[N * N]; blk = new byte[N * N];
            for (int j = 0; j < N; j++) for (int i = 0; i < N; i++)
                {
                    float x = Wx(i), z = Wx(j); int k = j * N + i;
                    if (Mathf.Abs(x) > M.half - 1 || Mathf.Abs(z) > M.half - 1) { terr[k] = 0; terrVeh[k] = 0; continue; }
                    float d = World.Depth(x, z), sl = World.Slope(x, z);
                    terr[k] = (byte)(d > 3f ? 0 : d > .05f ? Mathf.Clamp(2 + Mathf.RoundToInt(d * 3), 2, 9) : sl > .8f ? 3 : 1);
                    terrVeh[k] = (byte)(d > .9f || sl > 1.1f ? 0 : d > .05f ? 3 : sl > .5f ? 4 : 1);
                    if (terrVeh[k] == 0 && d < .05f && World.TrenchD(x, z) < 3) terrVeh[k] = 5;   // a tank straddles a trench (Unit.GroundY); the cut's walls are not a cliff to it
                }
            Rasterise(); version = 0; dirty = false;
        }

        /// Obstacle layer: walls and solid buildings (with clearance), wire (men only), trees (cost for men, nothing for tanks).
        static void Rasterise()
        {
            System.Array.Clear(blk, 0, blk.Length);
            void Fill(float x0, float x1, float z0, float z1, byte bits)
            {
                int i0 = Ix(x0), i1 = Ix(x1), j0 = Ix(z0), j1 = Ix(z1);
                for (int j = j0; j <= j1; j++) for (int i = i0; i <= i1; i++) blk[j * N + i] |= bits;
            }
            foreach (var b in World.obst)
            {
                if (b.dead || b.soft || b.floor || b.ceil) continue;
                if (b.rel && (b.pk == "roof" || b.pk == "lintel" || b.ry0 > 1.2f)) continue;   // things above head height don't block the ground
                if (b.wire) { Fill(b.x0 - .5f, b.x1 + .5f, b.z0 - .5f, b.z1 + .5f, 1); continue; }
                Fill(b.x0 - .6f, b.x1 + .6f, b.z0 - .6f, b.z1 + .6f, 1);
                Fill(b.x0 - 1.9f, b.x1 + 1.9f, b.z0 - 1.9f, b.z1 + 1.9f, 2);
            }
            foreach (var bd in Interiors.bds) if (bd.alive) { Fill(bd.x0 - .4f, bd.x1 + .4f, bd.z0 - .4f, bd.z1 + .4f, 1); Fill(bd.x0 - 1.9f, bd.x1 + 1.9f, bd.z0 - 1.9f, bd.z1 + 1.9f, 2); }
            foreach (var t in World.trees) if (t.alive) blk[Ix(t.z) * N + Ix(t.x)] |= 4;
        }
        public static void Tick() { if (dirty && terr != null) { Rasterise(); version++; dirty = false; } if (frame != Time.frameCount) { frame = Time.frameCount; budget = PerFrame; } }

        static int Cost(int k, bool veh)
        {
            if (veh) { int c = terrVeh[k]; if (c == 0 || (blk[k] & 2) != 0) return 0; return c; }
            else { int c = terr[k]; if (c == 0 || (blk[k] & 1) != 0) return 0; return (blk[k] & 4) != 0 ? c + 3 : c; }
        }
        public static bool Passable(float x, float z, bool veh) { if (terr == null) return true; return Cost(Ix(z) * N + Ix(x), veh) > 0; }

        /// Straight line clear on the grid (Bresenham over cell passability) — used to smooth paths and to skip the search entirely.
        static bool Clear(int i0, int j0, int i1, int j1, bool veh)
        {
            int di = Mathf.Abs(i1 - i0), dj = -Mathf.Abs(j1 - j0), si = i0 < i1 ? 1 : -1, sj = j0 < j1 ? 1 : -1, e = di + dj;
            for (int n = 0; n < 4096; n++)
            {
                if (n > 0 && Cost(j0 * N + i0, veh) == 0) return false; if (i0 == i1 && j0 == j1) return true;   // the start cell is where he already stands: a man in cover against a wall is in a "blocked" cell
                int e2 = 2 * e; if (e2 >= dj) { e += dj; i0 += si; } if (e2 <= di) { e += di; j0 += sj; }
            }
            return false;
        }

        // ---------------- A* ----------------
        static float[] g; static int[] prev, heap; static int[] stamp; static int curStamp; static float[] f; static byte[] closed;
        static int Nearest(int i, int j, bool veh)
        {   // nearest passable cell to a blocked goal / start (rings outward to 24 cells = 48 m: an order dropped in the middle of a factory
            // yard, a wire belt or a flooded meadow still resolves to the nearest place a man can stand instead of a null path and a frozen unit)
            if (Cost(j * N + i, veh) > 0) return j * N + i;
            for (int r = 1; r <= 24; r++) for (int dj = -r; dj <= r; dj++) for (int di = -r; di <= r; di++)
                    { if (Mathf.Max(Mathf.Abs(di), Mathf.Abs(dj)) != r) continue; int a = i + di, b = j + dj; if (a < 0 || b < 0 || a >= N || b >= N) continue; if (Cost(b * N + a, veh) > 0) return b * N + a; }
            return -1;
        }
        static readonly int[] DI = { 1, -1, 0, 0, 1, 1, -1, -1 }, DJ = { 0, 0, 1, -1, 1, -1, 1, -1 };
        /// Finds a path (world points, smoothed) from a to b for a man or a vehicle. Null when the budget is spent or nothing is found.
        static bool overBudget;   // set by Find: null because the frame's budget was spent (keep the old path), not because there is no way
        public static Path Find(Vector2 a, Vector2 b, bool veh)
        {
            if (terr == null) return null;
            int si = Ix(a.x), sj = Ix(a.y), ti = Ix(b.x), tj = Ix(b.y);
            var p = new Path { goal = b, t = Time.time };
            if (Clear(si, sj, ti, tj, veh)) { p.pts.Add(b); p.straight = true; return p; }
            if (budget <= 0) { overBudget = true; return null; } overBudget = false; budget--; searches++;
            int S = Nearest(si, sj, veh), T = Nearest(ti, tj, veh); if (S < 0 || T < 0) { fails++; return null; }
            int total = N * N;
            if (g == null || g.Length != total) { g = new float[total]; f = new float[total]; prev = new int[total]; stamp = new int[total]; closed = new byte[total]; heap = new int[total + 1]; }
            curStamp++; if (curStamp == int.MaxValue) { System.Array.Clear(stamp, 0, total); curStamp = 1; }
            int hn = 0;
            void Push(int k) { heap[++hn] = k; int c = hn; while (c > 1 && f[heap[c >> 1]] > f[heap[c]]) { (heap[c >> 1], heap[c]) = (heap[c], heap[c >> 1]); c >>= 1; } }
            int Pop() { int top = heap[1]; heap[1] = heap[hn--]; int c = 1; while (true) { int l = c * 2, r = l + 1, m = c; if (l <= hn && f[heap[l]] < f[heap[m]]) m = l; if (r <= hn && f[heap[r]] < f[heap[m]]) m = r; if (m == c) break; (heap[m], heap[c]) = (heap[c], heap[m]); c = m; } return top; }
            float H(int k) { int i = k % N, j = k / N, di = Mathf.Abs(i - ti), dj = Mathf.Abs(j - tj); return Mathf.Max(di, dj) + .4142f * Mathf.Min(di, dj); }
            stamp[S] = curStamp; closed[S] = 0; g[S] = 0; f[S] = H(S); prev[S] = -1; Push(S);
            int found = -1, expanded = 0;
            while (hn > 0 && expanded < MaxExpand)
            {
                int k = Pop(); if (closed[k] == 1 && stamp[k] == curStamp) continue; closed[k] = 1; expanded++;
                if (k == T) { found = k; break; }
                int i = k % N, j = k / N;
                for (int n = 0; n < 8; n++)
                {
                    int a2 = i + DI[n], b2 = j + DJ[n]; if (a2 < 0 || b2 < 0 || a2 >= N || b2 >= N) continue; int q = b2 * N + a2;
                    int c = Cost(q, veh); if (c == 0) continue;
                    if (n >= 4 && (Cost(j * N + a2, veh) == 0 || Cost(b2 * N + i, veh) == 0)) continue;   // no cutting corners through a wall
                    float ng = g[k] + c * (n >= 4 ? 1.4142f : 1);
                    if (stamp[q] != curStamp) { stamp[q] = curStamp; closed[q] = 0; g[q] = ng; f[q] = ng + H(q); prev[q] = k; Push(q); }
                    else if (ng < g[q] && closed[q] == 0) { g[q] = ng; f[q] = ng + H(q); prev[q] = k; Push(q); }
                }
            }
            if (found < 0) { fails++; return null; }
            var cells = new List<int>(); for (int k = found; k >= 0; k = prev[k]) cells.Add(k); cells.Reverse();
            // string-pull: keep only the corners that are needed, so men walk diagonals instead of grid staircases
            int at = 0;
            while (at < cells.Count - 1)
            {
                int far = at + 1;
                for (int k = cells.Count - 1; k > at + 1; k--) if (Clear(cells[at] % N, cells[at] / N, cells[k] % N, cells[k] / N, veh)) { far = k; break; }
                p.pts.Add(new Vector2(Wx(cells[far] % N), Wx(cells[far] / N))); at = far;
            }
            if (p.pts.Count == 0 || (p.pts[p.pts.Count - 1] - b).sqrMagnitude > CELL * CELL) p.pts.Add(b);
            return p;
        }

        /// The next point a unit should head for on its way to (tx, tz). Reuses the unit's path while the goal stays put and the
        /// path is fresh; otherwise searches again (within the frame budget — when that's spent the unit keeps its old heading).
        public static Vector2 Next(Unit u, float tx, float tz)
        {
            if (terr == null) return new Vector2(tx, tz);
            bool veh = u.def.kind == "tank" || u.def.kind == "gun"; var goal = new Vector2(tx, tz); var here = new Vector2(u.x, u.z);
            var p = u.path;
            bool stale = (p == null || (p.goal - goal).sqrMagnitude > 9 || Time.time - p.t > 6 || (u.stuck > .8f && Time.time - p.t > 1)) && (p == null || (p.goal - goal).sqrMagnitude > 9 || Time.time >= p.retry);   // a failed search waits 3 s before the next try
            if (stale)
            {
                var np = Find(here, goal, veh);
                if (np != null) { u.path = p = np; if (u.stuck > .8f) u.stuck = 0; }
                else if (overBudget && p != null) { }   // over budget this frame: keep following the old path
                else { u.path = p = new Path { goal = goal, t = Time.time - 3, retry = Time.time + 3, straight = true }; p.pts.Add(goal); }   // (a unit stuck against a wall with no path used to re-run a full A* every frame and starve everyone else's)   // nothing found (or no path yet): go straight for now, try again in 3 s — not every frame
            }
            while (p.i < p.pts.Count - 1 && (p.pts[p.i] - here).sqrMagnitude < 1.6f * 1.6f) p.i++;
            // look past the next corner when the way to the one after it is already clear
            if (p.i < p.pts.Count - 1 && Clear(Ix(here.x), Ix(here.y), Ix(p.pts[p.i + 1].x), Ix(p.pts[p.i + 1].y), veh)) p.i++;
            return p.pts[Mathf.Min(p.i, p.pts.Count - 1)];
        }
    }
}
