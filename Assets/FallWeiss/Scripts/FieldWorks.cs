// Fall Weiss — Polish field fortifications (roadmap T32) and the entrenching tool.
//
// History. In September 1939 the Polish Army fought its frontier and delaying battles from hastily dug field positions, not
// continuous WWI lines: squad rifle pits and short fire trenches ("gniazda") in an arc in front of a village or a railway
// embankment, with single foxholes for the section leaders and MGs behind them, dug in the hours or the night before contact.
// Mokra (1 Sept, Wołyńska Cavalry Brigade dug in along the embankment and in front of the village), the Westerplatte outposts
// and the Modlin field works all fit that picture; city fights (Wola, the Danzig Post Office) were barricades and sandbagged
// windows — no trenches there, and none where the Poles attack (Bzura). Every Polish soldier carried the small entrenching
// spade (saperka), so the men also dig in where they are told to hold.
//
// Plan(): before the terrain is built, every defended objective on a rural Polish-held map gets a broken fire trench of 3–5
// short sections across the approach 0.7–0.9 r in front of the centre (gaps between them, each angled a little), plus two
// foxholes inside the zone. They are written into the mission's trench list for this build only, so the terrain is cut, the
// AI gets trench cover points, blasts outside a trench are shielded, and no tree or house is placed in them. Existing
// hand-placed trenches win: nothing is dug within 6 m of one. The map's own data in missions.json is not modified.
//
// Dig: a Polish soldier who is crouched or prone and still can dig a foxhole (player: hold Y, 8 s; AI defenders holding a
// position with nobody in sight: 12 s). It becomes a 2.5 m slit trench facing the enemy, and real cover.
using System.Collections.Generic;
using UnityEngine;

namespace FallWeiss
{
    public static class FieldWorks
    {
        static readonly Dictionary<Mission, FTrench[]> original = new Dictionary<Mission, FTrench[]>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() { original.Clear(); }
        public static int planned;
        /// The fire-trench sections planned for this build, with the direction they face (World.DressMap puts a sandbag parapet on the forward lip).
        public static readonly List<Vector4> fireSegs = new List<Vector4>(); public static readonly List<Vector2> fireFace = new List<Vector2>();

        public static bool Applies(Mission m) => m != null && !m.range && !m.city && 1 - m.attacker == 1 && m.points != null && m.points.Length > 0;

        /// Called at the start of World.Build (after World.M is set, before the terrain).
        public static void Plan(Mission m)
        {
            if (!original.ContainsKey(m)) original[m] = m.trenches ?? new FTrench[0];
            m.trenches = original[m]; planned = 0; World.dugCount = 0; perPoint.Clear(); fireSegs.Clear(); fireFace.Clear();
            if (!Applies(m)) return;
            var R = new System.Random(m.seed * 31 + 7); float F(float a, float b) => a + (float)R.NextDouble() * (b - a);
            var outp = new List<FTrench>(m.trenches); var att = m.spawn[m.attacker];
            bool Ok(float x, float z)
            {
                if (Mathf.Abs(x) > m.half - 20 || Mathf.Abs(z) > m.half - 20) return false;
                foreach (var t in outp) for (int i = 0; i + 3 < t.pts.Length; i += 2) if (FU.SegD(x, z, t.pts[i], t.pts[i + 1], t.pts[i + 2], t.pts[i + 3]) < 6) return false;
                foreach (var r in m.roads) if (FU.SegD(x, z, r.x1, r.z1, r.x2, r.z2) < r.w / 2 + 3) return false;
                if (World.InRiverG(x, z, 4) || World.SeaEdgeG(x, z) < 16 || World.NearRailG(x, z, 5)) return false;
                foreach (var b in m.build)
                {   // the house footprint (rotated) plus 3 m. Most map buildings carry w = d = 0 and take their size from the building table
                    // (×1.25 for the size variation World.Build applies), so the raw w/d let trenches run under barns and warehouses
                    var ks = World.KindSize(b.k); float bw = (b.w > 0 ? b.w : ks.x * 1.25f), bdp = (b.d > 0 ? b.d : ks.y * 1.25f);
                    float c = Mathf.Cos(-b.rot), s = Mathf.Sin(-b.rot), dx = x - b.x, dz = z - b.z, lx = dx * c - dz * s, lz = dx * s + dz * c;
                    if (Mathf.Abs(lx) < bw / 2 + 3 && Mathf.Abs(lz) < bdp / 2 + 3) return false;
                }
                if (m.landmarks != null) foreach (var lm in m.landmarks) if (FU.Hyp(x - lm.x, z - lm.z) < 14) return false;   // churches, windmills, water towers
                foreach (var sp in m.spawn) if (FU.Hyp(x - sp.x, z - sp.z) < 40) return false;
                return true;
            }
            bool Seg(float ax, float az, float bx, float bz)
            {
                if (!Ok(ax, az) || !Ok(bx, bz) || !Ok((ax + bx) / 2, (az + bz) / 2)) return false;
                outp.Add(new FTrench { pts = new[] { ax, az, bx, bz } }); planned++; return true;
            }
            foreach (var p in m.points)
            {
                float dx = att.x - p.x, dz = att.z - p.z, l = Mathf.Max(.01f, FU.Hyp(dx, dz)), ux = dx / l, uz = dz / l, px = -uz, pz = ux;
                // depth of the zone along the attack axis: a 150 × 28 m embankment zone is 75 m "radius" but only ~14 m deep toward the enemy
                float r = p.r > 0 ? p.r : 14;
                if (p.w > 0 && p.d > 0) { float ca = Mathf.Cos(p.rot), sa = Mathf.Sin(p.rot); r = Mathf.Abs(ux * ca - uz * sa) * p.w / 2 + Mathf.Abs(ux * sa + uz * ca) * p.d / 2; r = Mathf.Max(r, 10); }
                // the fire trench: 3–5 sections across the approach with gaps between them
                // between the sandbag line (13 m out) and the wire (25 m out) that World.DressMap lays in front of every objective — a trench
                // dug beyond its own wire was the first test's mistake
                float wide = p.w > 0 && p.d > 0 ? Mathf.Max(p.w, p.d) / 2 : r;   // the line spans the zone's width, not its depth
                int n = wide > 35 ? 5 : wide > 22 ? 4 : 3; float span = Mathf.Min(wide * 1.6f, 60), fwd = Mathf.Clamp(r * .45f, 16, 20);
                for (int k = 0; k < n; k++)
                {
                    float lat = (k - (n - 1) / 2f) * span / Mathf.Max(1, n - 1) + F(-2, 2), f = fwd + F(-3, 3), half = F(2.5f, 3.6f), tw = F(-.25f, .25f);
                    float cx = p.x + ux * f + px * lat, cz = p.z + uz * f + pz * lat, ex = px * Mathf.Cos(tw) + ux * Mathf.Sin(tw), ez = pz * Mathf.Cos(tw) + uz * Mathf.Sin(tw);
                    for (int tries = 0; tries < 3; tries++)
                    {
                        if (Seg(cx - ex * half, cz - ez * half, cx + ex * half, cz + ez * half)) { fireSegs.Add(new Vector4(cx - ex * half, cz - ez * half, cx + ex * half, cz + ez * half)); fireFace.Add(new Vector2(ux, uz)); break; }
                        cx += ux * -2.5f; cz += uz * -2.5f;   // blocked: try a little further back
                    }
                }
                // two foxholes inside the zone (the section leader's and the LMG's)
                for (int k = 0; k < 2; k++)
                {
                    float a = F(0, 6.28f), d = r * F(.2f, .5f), cx = p.x + Mathf.Cos(a) * d, cz = p.z + Mathf.Sin(a) * d;
                    Seg(cx - px * 1.2f, cz - pz * 1.2f, cx + px * 1.2f, cz + pz * 1.2f);
                }
            }
            m.trenches = outp.ToArray();
        }

        // ---------------- entrenching tool ----------------
        public const float PLAYER_DIG = 8, AI_DIG = 12;
        public static bool CanDig(Unit u) => u != null && u.alive && u.team == 1 && u.def.kind == "inf" && u.mount == null;
        static float Face(Unit u)
        {   // facing the nearest known enemy, else the enemy's spawn
            var g = Game.I; var e = u.target ?? (g != null ? g.NearestEnemy(u) : null); var sp = World.M.spawn[1 - u.team];
            float tx = e != null ? e.x : sp.x, tz = e != null ? e.z : sp.z; return Mathf.Atan2(tx - u.x, tz - u.z);
        }
        public static string WhyNot(Unit u)
        {
            if (!CanDig(u)) return "Only Polish infantry carry the saperka.";
            if (u.moving) return "Stand still to dig."; if (u.stance == 0) return "Crouch or go prone to dig.";
            return Ground(u);
        }
        static string Ground(Unit u)
        {
            if (World.TrenchD(u.x, u.z) < 4.5f) return "There's already a trench here.";
            if (World.InBuildingG(u.x, u.z, 1.5f) || World.InWater(u.x, u.z) || World.NearRoadG(u.x, u.z, 1)) return "You can't dig here.";
            if (World.dugCount > 60) return "The ground here is already turned over enough.";
            return null;
        }
        /// Finishes a dig: a 2.5 m slit trench across the facing direction, half a metre in front of the man, who drops into it.
        public static bool Finish(Unit u)
        {
            float f = Face(u), ex = Mathf.Cos(f), ez = -Mathf.Sin(f), cx = u.x + Mathf.Sin(f) * .5f, cz = u.z + Mathf.Cos(f) * .5f;
            if (!World.DigTrench(cx - ex * 1.25f, cz - ez * 1.25f, cx + ex * 1.25f, cz + ez * 1.25f)) return false;
            u.x = cx; u.z = cz; u.y = World.H(u.x, u.z); return true;
        }

        /// AI: Polish defenders holding a position with nobody in sight dig in. A few men per objective, not the whole army.
        static readonly Dictionary<int, int> perPoint = new Dictionary<int, int>();
        /// The first condition that keeps a man from digging (dbg works).
        public static string AiBlock(Game g, Unit u)
        {
            if (u.digT > 0) return "digging"; if (!CanDig(u)) return "notInf"; if (u.team == g.M.attacker) return "attacker";
            if (u.type == "crew" || u.type == "tcrew" || u.type == "sniper") return "type"; if (u.moving) return "moving"; if (u.target != null && (u.los || FU.Hyp(u.x - u.target.x, u.z - u.target.z) < 130)) return "contact";
            if (u.order != null && u.order.force) return "order"; if (u.idle < 3) return "idle<3"; if (u.supp > .2f) return "supp"; if (u.rout > 0) return "rout"; if (u.hide != null && u.hide.trench) return "inTrench";
            int pi = -1; var pts = World.points; for (int i = 0; i < pts.Count; i++) { if (pts[i].locked) continue; if (FU.Hyp(u.x - pts[i].x, u.z - pts[i].z) < pts[i].r + 30) { pi = i; break; } }
            if (pi < 0) return "farFromPoint"; perPoint.TryGetValue(pi, out int n); if (n >= 6) return "pointFull";
            var gr = Ground(u); return gr == null ? "ready" : gr.StartsWith("There's") ? "nearTrench" : "badGround";
        }
        public static bool AiTick(Game g, Unit u, float dt)
        {
            if (u.digT > 0)
            {
                bool interrupted = u.target != null && u.los && FU.Hyp(u.x - u.target.x, u.z - u.target.z) < 130 || u.supp > .4f || u.order != null && u.order.force || u.rout > 0;
                if (interrupted) { u.digT = 0; return false; }
                u.digT += dt; u.moving = false; u.sprinting = false; u.SetStance(1);
                if (Time.time - u.digSnd > .9f && g.audio.listener && (g.audio.listener.position - new Vector3(u.x, u.y, u.z)).sqrMagnitude < 40 * 40) { u.digSnd = Time.time; g.audio.Shot("dig", new Vector3(u.x, u.y + .3f, u.z), .7f); }
                if (u.digT >= AI_DIG) { u.digT = 0; if (Finish(u)) { u.hide = new Hide { x = u.x, z = u.z, t = 60, fire = true, trench = true }; u.home = new Vector2(u.x, u.z); } }
                return true;
            }
            if (!CanDig(u) || u.team == g.M.attacker || u == g.player || u.type == "crew" || u.type == "tcrew" || u.type == "sniper") return false;
            bool contact = u.target != null && (u.los || FU.Hyp(u.x - u.target.x, u.z - u.target.z) < 130);   // a man seen far off over open ground is no reason to stop digging
            if (u.moving || contact || (u.order != null && u.order.force) || u.idle < 3 || u.supp > .2f || u.rout > 0 || (u.hide != null && u.hide.trench)) return false;
            if (Random.value > dt * .15f) return false;   // not everyone at once
            int pi = -1; float bd = 1e9f; var pts = World.points;
            for (int i = 0; i < pts.Count; i++) { if (pts[i].locked) continue; float d = FU.Hyp(u.x - pts[i].x, u.z - pts[i].z); if (d < pts[i].r + 30 && d < bd) { bd = d; pi = i; } }
            if (pi < 0) return false; perPoint.TryGetValue(pi, out int n); if (n >= 6) return false;
            if (Ground(u) != null) return false;
            perPoint[pi] = n + 1; u.digT = .001f; return true;
        }
    }
}
