// Editor-only: "Fall Weiss / Fortify Objectives (RO2)" — turns every capture point into a Red Orchestra 2 style hotspot:
//  * a strongpoint to fight over (adds a fitting building when the zone has none) and a zone sized to its compound
//  * a defensive line facing the attackers' approach (trench arc; sandbagged walls at river crossings)
//  * close-quarters cover inside the zone (low walls, crates, rubble, barricades)
//  * covered flanking routes either side of the approach (broken hedgerows / stone walls, bushes, rocks) while the
//    frontal approach stays an open killing field
//  * a gentle rise under the objective when the defenders have no height advantage (never at river crossings)
// City streets get barricades / rubble / low walls only; objectives inside buildings get barricaded approaches.
// Every placement is checked against buildings, roads, water, other objectives and the map edge. Walls and props are
// written to both decor (visuals, three.js space) and segs / props (collision, game space). Missions are stamped with
// `fortified`, so re-running never stacks fortifications; a timestamped backup of missions.json goes to Logs/.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FallWeiss;
using UnityEditor;
using UnityEngine;

namespace FallWeissEditor
{
    public static class ObjectiveFortifier
    {
        const string JsonPath = "Assets/FallWeiss/Resources/missions.json";
        const int Version = 1;

        [MenuItem("Fall Weiss/Fortify Objectives (RO2)")]
        public static void Menu() { Debug.Log("[FallWeiss] " + Run(false)); }

        /// force: re-apply even to missions already stamped (only use on an un-fortified backup).
        public static string Run(bool force)
        {
            var data = JsonUtility.FromJson<MissionList>(File.ReadAllText(JsonPath)); var log = new List<string>();
            foreach (var m in data.missions)
            {
                if (m.range || m.points == null || m.points.Length == 0) continue;
                if (m.fortified >= Version && !force) { log.Add(m.name + ": already fortified"); continue; }
                Normalize(m); var f = new Fortify(m); f.Apply(); m.fortified = Version; log.Add(m.name + ": " + f.Summary);
            }
            Directory.CreateDirectory("Logs"); File.Copy(JsonPath, "Logs/missions_before_fortify_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json", true);
            File.WriteAllText(JsonPath, JsonUtility.ToJson(data, true)); AssetDatabase.ImportAsset(JsonPath);
            return string.Join("\n", log);
        }
        static void Normalize(Mission m)
        {
            m.roads ??= new FLine[0]; m.lanes ??= new FLine[0]; m.rails ??= new FLine[0]; m.forests ??= new FArea[0]; m.villages ??= new FArea[0]; m.orchards ??= new FRect[0];
            m.hills ??= new FHill[0]; m.trenches ??= new FTrench[0]; m.build ??= new FBuild[0]; m.landmarks ??= new FLandmark[0]; m.segs ??= new FSeg[0]; m.props ??= new FProp[0]; m.decor ??= new FDecor[0];
        }

        class Fortify
        {
            readonly Mission m; readonly System.Random R; readonly List<FBuild> build; readonly List<FSeg> segs; readonly List<FProp> props; readonly List<FDecor> decor; readonly List<FTrench> trenches; readonly List<FHill> hills;
            readonly float? wl; int nB, nT, nW, nP, nH, nA;
            public string Summary => $"{nA} zones re-anchored on buildings, {nB} strongpoints, {nT} trench lines, {nW} wall / hedge runs, {nP} cover props, {nH} rises";
            public Fortify(Mission mission)
            {
                m = mission; R = new System.Random(m.seed * 7919 + 17); wl = World.WaterLevel(m);
                build = m.build.ToList(); segs = m.segs.ToList(); props = m.props.ToList(); decor = m.decor.ToList(); trenches = m.trenches.ToList(); hills = m.hills.ToList();
            }
            float Rn(float a, float b) => a + (float)R.NextDouble() * (b - a);

            public void Apply()
            {
                for (int i = 0; i < m.points.Length; i++)
                {
                    var p = m.points[i];
                    // approach: from the previous objective (or the attackers' spawn) to this one
                    var from = i == 0 ? new Vector2(m.spawn[m.attacker].x, m.spawn[m.attacker].z) : new Vector2(m.points[i - 1].x, m.points[i - 1].z);
                    var c = new Vector2(p.x, p.z); var ap = (c - from); if (ap.sqrMagnitude < 1) ap = Vector2.up; ap.Normalize();   // direction the attack moves
                    var side = new Vector2(ap.y, -ap.x);
                    bool bridge = m.hasRiver && Mathf.Abs(p.z - m.RiverC(p.x)) < m.rw * 3;
                    if (p.inside) { BuildingApproach(p, c, ap, side); continue; }
                    if (m.city) { Streets(p, c, ap, side); continue; }
                    p.r = Mathf.Max(p.r, bridge ? 16 : 18);
                    if (!bridge) { Anchor(p); c = new Vector2(p.x, p.z); Strongpoint(p, c, ap, i); }
                    else { float sgn = Mathf.Sign(m.spawn[m.attacker].z - m.RiverC(p.x)); ap = new Vector2(0, -sgn); side = new Vector2(ap.y, -ap.x); }   // the attack crosses the river
                    if (bridge) Sandbags(p, c, ap, side); else TrenchArc(c, ap, p.r);
                    InnerCover(p, c, ap, side);
                    Flanks(c, ap, side, p.r);
                    if (!bridge) Rise(c, ap, from);
                }
                m.build = build.ToArray(); m.segs = segs.ToArray(); m.props = props.ToArray(); m.decor = decor.ToArray(); m.trenches = trenches.ToArray(); m.hills = hills.ToArray();
            }

            // ---------------- pieces ----------------
            /// RO2 zones take in their strongpoint: when a building or landmark stands just outside, shift the zone over it.
            void Anchor(FPoint p)
            {
                var c = new Vector2(p.x, p.z); Vector2? best = null; float bd = 1e9f;
                foreach (var b in build) { float d = Vector2.Distance(c, new Vector2(b.x, b.z)); if (d < bd) { bd = d; best = new Vector2(b.x, b.z); } }
                foreach (var l in m.landmarks) { float d = Vector2.Distance(c, new Vector2(l.x, l.z)); if (d < bd) { bd = d; best = new Vector2(l.x, l.z); } }
                if (!best.HasValue || bd < p.r - 4 || bd > p.r + 16) return;
                var q = Vector2.Lerp(c, best.Value, .55f); if (!Ground(q)) return;
                p.x = R1(q.x); p.z = R1(q.y); nA++;
            }
            void Strongpoint(FPoint p, Vector2 c, Vector2 ap, int i)
            {
                bool has = build.Any(b => Vector2.Distance(new Vector2(b.x, b.z), c) < p.r + 4) || m.landmarks.Any(l => Vector2.Distance(new Vector2(l.x, l.z), c) < p.r + 6);
                if (has) return;
                string n = p.n.ToLower();
                string k = n.Contains("farm") || n.Contains("village") || n.Contains("manor") || n.Contains("estate") ? (i % 2 == 0 ? "house" : "barn")
                         : n.Contains("mill") ? "barn" : n.Contains("rail") || n.Contains("yard") ? "warehouse" : n.Contains("depot") || n.Contains("store") ? "warehouse"
                         : n.Contains("block") || n.Contains("admin") ? "admin" : "bunker";
                // just behind the centre (defender side) so the yard in front is part of the fight
                for (int t = 0; t < 8; t++)
                {
                    var q = c + ap * Rn(3, 6) + new Vector2(ap.y, -ap.x) * Rn(-4, 4);
                    if (!FreeArea(q, 7, true)) continue;
                    build.Add(new FBuild { k = k, x = R1(q.x), z = R1(q.y) }); nB++; return;
                }
            }

            void TrenchArc(Vector2 c, Vector2 ap, float r)
            {
                if (trenches.Any(t => Enumerable.Range(0, t.pts.Length / 2).Any(k => { var v = new Vector2(t.pts[k * 2], t.pts[k * 2 + 1]); return Vector2.Distance(v, c) < r + 16 && Vector2.Dot(v - c, -ap) > 2; }))) return;
                float dist = r + Rn(8, 12), span = Rn(50, 65) * Mathf.Deg2Rad; var back = -ap; float baseA = Mathf.Atan2(back.y, back.x);
                var run = new List<float>(); bool any = false;
                void Flush() { if (run.Count >= 4) { trenches.Add(new FTrench { pts = run.ToArray() }); any = true; } run.Clear(); }
                for (int k = 0; k <= 8; k++)
                {   // a road or building through the arc splits it into separate runs with a crossing gap
                    float a = baseA + Mathf.Lerp(-span, span, k / 8f) + Rn(-.03f, .03f); var q = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (dist + Rn(-1f, 1f));
                    if (!Ground(q) || NearBuilding(q, 3) || OnRoad(q, 2.5f)) { Flush(); continue; }
                    run.Add(R1(q.x)); run.Add(R1(q.y));
                }
                Flush(); if (any) nT++;
            }

            void Sandbags(FPoint p, Vector2 c, Vector2 ap, Vector2 side)
            {   // river crossing: sandbagged stone walls on the defenders' bank, angled to cover the bridge exit
                for (int k = -1; k <= 1; k += 2)
                {
                    float bank = m.rw * 1.6f + Rn(3, 6); var mid = new Vector2(c.x, m.RiverC(c.x)) + ap * bank + side * k * Rn(7, 10); var dir = (side * k).normalized;
                    Wall("stonewall", mid - dir * 4, mid + dir * 4);
                }
                for (int k = 0; k < 3; k++) Prop(k == 0 ? "barricade" : "crates", new Vector2(c.x, m.RiverC(c.x)) + ap * (m.rw * 1.6f + Rn(6, 11)) + side * Rn(-6, 6), Rn(-1, 1));
                TrenchArc(new Vector2(c.x, m.RiverC(c.x)) + ap * (m.rw * 1.6f + 10), -ap, 2);   // a fire trench on the far bank, facing the crossing
            }

            void InnerCover(FPoint p, Vector2 c, Vector2 ap, Vector2 side)
            {
                // two short low walls on the approach edge, offset so they do not seal the zone
                for (int k = -1; k <= 1; k += 2)
                {
                    var mid = c - ap * (p.r * Rn(.55f, .8f)) + side * k * Rn(5, 9); var dir = (side + ap * Rn(-.3f, .3f)).normalized;
                    Wall(R.NextDouble() < .5 ? "stonewall" : "fence", mid - dir * Rn(3, 4.5f), mid + dir * Rn(3, 4.5f));
                }
                string[] kinds = { "crates", "barrels", "rubble", "barricade", "crates" };
                int n = 3 + R.Next(2);
                for (int k = 0; k < n; k++) { var q = c + Rot(ap, Rn(0, 6.28f)) * Rn(3, p.r * .8f); Prop(kinds[R.Next(kinds.Length)], q, Rn(-3, 3)); }
            }

            void Flanks(Vector2 c, Vector2 ap, Vector2 side, float r)
            {
                // broken cover lines either side of the approach: 3-4 segments from ~20 m to ~70 m out, with gaps to move through
                for (int k = -1; k <= 1; k += 2)
                {
                    float lat = r + Rn(18, 28); string kind = m.city ? "stonewall" : R.NextDouble() < .55 ? "hedge" : "stonewall";
                    float d = Rn(18, 24);
                    for (int s = 0; s < 4 && d < 75; s++)
                    {
                        float len = Rn(9, 14); var a = c - ap * d + side * k * (lat + Rn(-2, 2)); var b = c - ap * (d + len) + side * k * (lat + Rn(-3, 3));
                        Wall(kind, a, b); d += len + Rn(5, 9);
                        if (R.NextDouble() < .6) Prop(R.NextDouble() < .5 ? "bushes" : "rocks", c - ap * (d - 3) + side * k * (lat + Rn(3, 6)), Rn(-3, 3));
                    }
                }
            }

            void Rise(Vector2 c, Vector2 ap, Vector2 from)
            {
                float h = World.PreviewHeight(m, c.x, c.y), hf = World.PreviewHeight(m, c.x - ap.x * 40, c.y - ap.y * 40);
                if (h - hf > 1.2f) return;   // already holds the higher ground
                var q = c + ap * 6; if (wl.HasValue && World.PreviewHeight(m, q.x, q.y) < wl.Value + 1.5f) return;
                hills.Add(new FHill { x = R1(q.x), z = R1(q.y), r = 42, h = Mathf.Clamp(2.6f - (h - hf), 1.6f, 3.4f) }); nH++;
            }

            void Streets(FPoint p, Vector2 c, Vector2 ap, Vector2 side)
            {   // city: barricade the street mouth facing the attack, rubble and low walls for house-to-house cover
                p.r = Mathf.Max(p.r, 16);
                for (int k = -1; k <= 1; k++) Prop("barricade", c - ap * Rn(8, 12) + side * k * Rn(4, 6), Rn(-.4f, .4f));
                for (int k = 0; k < 4; k++) Prop(R.NextDouble() < .5 ? "rubble" : "crates", c + Rot(ap, Rn(0, 6.28f)) * Rn(3, 12), Rn(-3, 3));
                for (int k = -1; k <= 1; k += 2) { var mid = c - ap * Rn(18, 26) + side * k * Rn(5, 8); Wall("stonewall", mid - side * 3, mid + side * 3); }
                for (int k = -1; k <= 1; k += 2) for (int s = 0; s < 3; s++) Prop("rubble", c - ap * Rn(25, 60) + side * k * Rn(6, 10), Rn(-3, 3));
            }

            void BuildingApproach(FPoint p, Vector2 c, Vector2 ap, Vector2 side)
            {   // objective inside a building: barricades and debris on the approaches, nothing that blocks the doors
                for (int k = -1; k <= 1; k += 2) Prop("barricade", c - ap * (p.r + Rn(8, 12)) + side * k * Rn(5, 9), Rn(-.4f, .4f));
                for (int k = 0; k < 3; k++) Prop(R.NextDouble() < .5 ? "rubble" : "crates", c - ap * (p.r + Rn(14, 30)) + side * Rn(-14, 14), Rn(-3, 3));
            }

            // ---------------- placement with checks ----------------
            void Wall(string k, Vector2 a, Vector2 b)
            {
                for (int s = 0; s <= 4; s++) { var q = Vector2.Lerp(a, b, s / 4f); if (!Ground(q) || NearBuilding(q, 2) || OnRoad(q, 1.5f) || NearObjectiveCore(q)) return; }
                float x1 = R1(a.x), z1 = R1(a.y), x2 = R1(b.x), z2 = R1(b.y);
                segs.Add(new FSeg { k = k, x1 = x1, z1 = z1, x2 = x2, z2 = z2 }); decor.Add(new FDecor { k = k, v = new[] { x1, -z1, x2, -z2 } }); nW++;
            }
            void Prop(string k, Vector2 q, float a)
            {
                if (!Ground(q) || NearBuilding(q, 1.5f) || OnRoad(q, 1) || NearObjectiveCore(q, 2)) return;
                float x = R1(q.x), z = R1(q.y), aa = Mathf.Round(a * 100) / 100; props.Add(new FProp { k = k, x = x, z = z, a = aa }); decor.Add(new FDecor { k = k, v = new[] { x, -z, aa } }); nP++;
            }
            bool Ground(Vector2 q) => Mathf.Abs(q.x) < m.half - 15 && Mathf.Abs(q.y) < m.half - 15 && !(wl.HasValue && World.PreviewHeight(m, q.x, q.y) < wl.Value + .2f) && (!m.hasRiver || Mathf.Abs(q.y - m.RiverC(q.x)) > m.rw * 1.3f);
            bool NearBuilding(Vector2 q, float pad) => build.Any(b => Mathf.Abs(q.x - b.x) < (b.w > 0 ? b.w : 9) / 2 + pad && Mathf.Abs(q.y - b.z) < (b.d > 0 ? b.d : 8) / 2 + pad)
                                                       || m.landmarks.Any(l => Vector2.Distance(q, new Vector2(l.x, l.z)) < (l.k == "church" ? 10 : 5) + pad);
            bool OnRoad(Vector2 q, float pad) => m.roads.Concat(m.lanes).Concat(m.rails).Any(r => SegD(q, new Vector2(r.x1, r.z1), new Vector2(r.x2, r.z2)) < r.w / 2 + pad);
            bool NearObjectiveCore(Vector2 q, float pad = 3) => m.points.Any(o => Vector2.Distance(q, new Vector2(o.x, o.z)) < pad) || m.spawn.Any(s => Vector2.Distance(q, new Vector2(s.x, s.z)) < 14);
            bool FreeArea(Vector2 q, float r, bool keepRoads) => Ground(q) && !NearBuilding(q, r * .6f) && !(keepRoads && OnRoad(q, r * .6f));
            static float SegD(Vector2 p, Vector2 a, Vector2 b) { var ab = b - a; float t = ab.sqrMagnitude < 1e-4f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude); return (p - (a + ab * t)).magnitude; }
            static Vector2 Rot(Vector2 v, float a) => new Vector2(v.x * Mathf.Cos(a) - v.y * Mathf.Sin(a), v.x * Mathf.Sin(a) + v.y * Mathf.Cos(a));
            static float R1(float v) => Mathf.Round(v * 10) / 10;
        }
    }
}
