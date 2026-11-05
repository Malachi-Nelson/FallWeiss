// Fall Weiss — vehicle and gun crews (roadmap T27), tank repair (T28) and the sounds a crew hears inside a hull (T29).
//
// Until now the gun layers, the tank commander and the lorry driver were part of the vehicle mesh: the same low-poly figures
// as the 2026-10 soldier model, baked in, with no uniform of their own, no hit zones and no way out. models.bytes now ships the
// vehicles bare, and each tank / AT gun gets real soldier units as crew: the same model, kit and hit zones as the infantry
// (RO2: a gun crew can be shot off its gun, a commander can be picked out of his hatch, a knocked-out crew bails out and
// fights on with pistols). The player can leave a vehicle (E) and climb back into any friendly one.
//
// Seats are in the vehicle's own model space (+Z = forward). Crew ride along in Unit.Update (Crew.Mounted) and never think,
// path or fire for themselves while mounted; the vehicle does the fighting, the crew is what dies when it is penetrated.
using System.Collections.Generic;
using UnityEngine;

namespace FallWeiss
{
    public static class Crew
    {
        public class Seat { public Vector3 p; public int stance; public bool inside, turret; public float face; }

        /// Who crews what. Tank: commander in the hatch (visible when unbuttoned), gunner and driver inside. AT gun: layer and
        /// loader kneeling by the shield, the gun commander crouched at the trails.
        public static Seat[] Seats(Unit v)
        {
            if (v.def.kind == "tank")
            {
                var hatch = v.team == 0 ? new Vector3(0, -.55f, -.25f) : new Vector3(-.15f, -.55f, -.17f);   // Pz II / 7TP commander's hatch, turret space
                return new[] { new Seat { p = hatch, stance = 0, turret = true }, new Seat { p = new Vector3(.35f, 0, .3f), stance = 1, inside = true }, new Seat { p = new Vector3(-.35f, 0, .9f), stance = 1, inside = true } };
            }
            if (v.def.kind == "gun")
            {
                bool g = v.team == 0;
                return new[] { new Seat { p = new Vector3(g ? -.6f : .6f, 0, -.85f), stance = 1 }, new Seat { p = new Vector3(g ? .8f : -.82f, 0, -1.15f), stance = 1 }, new Seat { p = new Vector3(0, 0, -2.3f), stance = 1 } };
            }
            return new Seat[0];
        }
        public static int CrewSize(Unit v) => Seats(v).Length;
        public static string CrewType(Unit v) => v.def.kind == "tank" ? "tcrew" : "crew";

        /// Spawns the crew of a freshly built vehicle. Called from Game.SpawnUnit after the vehicle itself exists.
        public static void Spawn(Game g, Unit v)
        {
            var seats = Seats(v); if (seats.Length == 0) return; v.crew = new List<Unit>();
            for (int i = 0; i < seats.Length; i++) { var c = g.SpawnUnit(v.team, CrewType(v), v.x, v.z, v.heading); c.sect = v.sect; Mount(g, c, v, i); }
        }

        public static void Mount(Game g, Unit c, Unit v, int seat = -1)
        {
            if (v.crew == null) v.crew = new List<Unit>();
            if (seat < 0) { var used = new HashSet<int>(); foreach (var o in v.crew) if (o.alive) used.Add(o.seat); for (int i = 0; i < Seats(v).Length; i++) if (!used.Contains(i)) { seat = i; break; } }
            if (seat < 0) return;
            if (!v.crew.Contains(c)) v.crew.Add(c);
            c.mount = v; c.seat = seat; c.order = null; c.queue.Clear(); c.hide = null; c.focus = c.target = null; c.follow = c.squad = false; c.path = null; c.moving = c.sprinting = false; c.rout = 0;
            c.manual = true; c.SetStance(Seats(v)[seat].stance); c.stanceT = 0;
            Place(g, c, 0);
        }

        /// Puts a crewman on his seat for this frame (position, height, heading, visibility, hit boxes).
        static void Place(Game g, Unit c, float dt)
        {
            var v = c.mount; var seats = Seats(v); if (c.seat < 0 || c.seat >= seats.Length || !v.go) return; var s = seats[c.seat];
            Transform frame = s.turret && v.turret ? v.turret : v.go.transform;
            // the vehicle's transform is written in SyncTransform after Update, so build the frame from the simulation state instead of last frame's transform
            var rot = Quaternion.Euler(0, (s.turret ? v.turretH : v.heading) * Mathf.Rad2Deg, 0);
            Vector3 local = s.p; if (s.turret && v.turret) local = v.turret.localPosition + Quaternion.Euler(0, FU.AngDiff(v.heading, v.turretH) * Mathf.Rad2Deg, 0) * s.p;
            if (s.turret) rot = Quaternion.Euler(0, v.heading * Mathf.Rad2Deg, 0);
            var w = new Vector3(v.x, v.y, v.z) + rot * local;
            c.x = w.x; c.z = w.z; c.y = v.def.kind == "gun" ? World.H(w.x, w.z) : w.y;
            c.heading = s.turret ? v.turretH : v.heading + s.face; c.turretH = c.heading;
            bool repairing = v.repairT > 0 && c.seat == 1 && v.def.kind == "tank";
            if (repairing)
            {   // the man out on the track with a sledge: left side, crouched, facing the running gear
                var side = rot * new Vector3(-2.1f, 0, -.6f); c.x = v.x + side.x; c.z = v.z + side.z; c.y = World.H(c.x, c.z); c.heading = v.heading + Mathf.PI / 2; c.SetStance(1); c.stanceT = 0;
            }
            bool show = !s.inside || repairing;
            if (s.turret) show = !v.buttoned && !(v == g.player && !g.tp);   // the player is the commander: no second head in front of the camera
            SetShown(c, show);
        }
        static void SetShown(Unit c, bool show)
        {
            if (c.crewShown == show) return; c.crewShown = show;
            c.Show(show, true); foreach (var col in c.go.GetComponentsInChildren<Collider>(true)) col.enabled = show && c.alive;
        }

        /// Per-frame tick for a mounted crewman (replaces the whole of Unit.Update for him).
        public static void Mounted(Game g, Unit c, float dt)
        {
            var v = c.mount;
            if (!v.alive) { Dismount(g, c, bail: true); return; }
            c.cd -= dt; c.think -= dt; c.supp = Mathf.Max(0, c.supp - dt * .2f); c.moving = c.sprinting = false; c.stam = Mathf.Min(1, c.stam + dt * .1f);
            if (c.Foot) c.mor = Mathf.Clamp(c.mor + dt * 1.2f, 0, 100);
            Combat.Bleed(g, c, dt, true); if (!c.alive) return;
            // gun crews flinch under fire: prone behind the shield, which is why a suppressed gun loads slowly (Combat.NpcFire reads crewSupp)
            if (v.def.kind == "gun") { var s = Seats(v)[c.seat]; int want = v.supp > .7f ? 2 : s.stance; if (want != c.stance) c.SetStance(want); }
            Place(g, c, dt);
        }

        /// Leaves the vehicle: beside it on the open side, crouched. A bail-out (vehicle lost) routs the man for a few seconds.
        public static void Dismount(Game g, Unit c, bool bail, bool dead = false)
        {
            var v = c.mount; if (v == null) return; bool inside = v.def.kind == "tank" && c.seat > 0 && !(v.repairT > 0 && c.seat == 1);
            v.crew?.Remove(c); c.mount = null; c.seat = -1;
            if (dead && inside) { c.crewShown = false; c.Show(false, true); c.deadT = 60; return; }   // died inside the hull: no body to show, gone with the next sweep
            c.crewShown = false; SetShown(c, true);   // force the renderers / colliders back on
            if (!dead)
            {
                var r = Quaternion.Euler(0, v.heading * Mathf.Rad2Deg, 0); Vector3 best = Vector3.zero; bool got = false;
                foreach (var o in new[] { new Vector3(-2.4f, 0, 0), new Vector3(2.4f, 0, 0), new Vector3(0, 0, -3.2f), new Vector3(-2.4f, 0, -2.4f), new Vector3(2.4f, 0, -2.4f) })
                {
                    var p = new Vector3(v.x, 0, v.z) + r * o; if (World.InBuildingG(p.x, p.z, .6f) || World.InWater(p.x, p.z) || !Nav.Passable(p.x, p.z, false)) continue; best = p; got = true; break;
                }
                if (!got) { var fs = g.FreeSpot(v.x, v.z, 4, .5f); best = new Vector3(fs.x, 0, fs.y); }
                c.x = best.x; c.z = best.z; c.y = World.H(c.x, c.z); c.heading = v.heading; c.SetStance(1); c.stanceT = 0;
                c.home = new Vector2(c.x, c.z); c.manual = false; c.squad = c.Foot; c.prot = g.t + 1.5f;
                if (bail) { c.rout = FU.Rand(5, 9); c.mor = 30; c.supp = .6f; }
                if (c.weps == null) c.InitWeapons(Defs.Loadout(c.team, c.type));
            }
            c.SyncTransform(0);
        }

        /// A penetration that "hits the crew" kills one man inside (the commander last — he is the one in the hatch and the one the
        /// player is). With nobody left the vehicle is dead whatever its hull points say.
        public static void KillOne(Game g, Unit v, Unit by)
        {
            if (v.crew == null) return; Unit pick = null;
            for (int i = v.crew.Count - 1; i >= 0; i--) { var c = v.crew[i]; if (c.alive && c.seat > 0) { pick = c; break; } }
            if (pick == null) foreach (var c in v.crew) if (c.alive) { pick = c; break; }
            if (pick == null) return;
            Combat.Kill(g, pick, by, false);
        }
        public static int Alive(Unit v) { int n = 0; if (v.crew != null) foreach (var c in v.crew) if (c.alive && c.mount == v) n++; return n; }

        /// After the vehicle dies: a cook-off kills everyone inside, otherwise the survivors bail out and run.
        public static void OnVehicleDead(Game g, Unit v, Unit by, bool cookOff)
        {
            if (v.crew == null) return;
            foreach (var c in v.crew.ToArray())
            {
                if (!c.alive || c.mount != v) continue;
                if (cookOff && v.def.kind == "tank") { Dismount(g, c, bail: true); Combat.Kill(g, c, by, false); c.deadT = 2; }
                else Dismount(g, c, bail: true);
            }
            if (v.team == g.ME && v.def.kind == "tank" && !cookOff && Alive(v) == 0 && v.crew.Count > 0) g.Msg("Crew bailing out of the " + Defs.NAMES[v.team][v.type] + "!");
        }

        // ---------------- repairs (T28) ----------------
        // A tank that has lost its tracks / engine or its gun can be put right by its own crew, if it has two men left and nobody is
        // shooting at it: 45 s for the running gear (one man climbs out with a sledge and spare links — he can be shot), 28 s for
        // the gun (inside). Being hit pauses the work; the man outside tucks in behind the hull.
        public const float TRACK_T = 45, GUN_T = 28;
        public static void Repair(Game g, Unit v, float dt)
        {
            if (!v.alive || v.def.kind != "tank" || (!v.immobile && !v.gunDead)) { v.repairT = 0; return; }
            bool safe = Alive(v) >= 2 && !v.moving && g.t - v.lastHit > 6 && Time.time - v.lastShot > 5 && v.supp < .4f && (v == g.player || v.target == null);
            if (v.repairT <= 0)
            {
                if (!safe) return;
                v.repairT = .001f; v.repairWhat = v.immobile ? "tracks" : "gun"; v.repairDur = v.immobile ? TRACK_T : GUN_T; v.repairTick = 0;
                if (v == g.player) g.Msg(v.repairWhat == "tracks" ? "Driver's out on the track — repairing the running gear. Stay still." : "Loader is clearing the gun — hold here.");
                return;
            }
            bool hit = g.t - v.lastHit < 2;   // paused while rounds are coming in
            if (!hit && Alive(v) >= 2) v.repairT += dt;
            v.repairTick -= dt;
            if (v.repairTick <= 0 && !hit) { v.repairTick = v.repairWhat == "tracks" ? FU.Rand(.55f, .9f) : FU.Rand(.9f, 1.6f); g.audio.Shot("repair_hammer", new Vector3(v.x, v.y + 1, v.z), v.repairWhat == "tracks" ? 1 : .5f, v == g.player && v.repairWhat == "gun"); }
            if (v.repairT >= v.repairDur)
            {
                if (v.repairWhat == "tracks") v.immobile = false; else v.gunDead = false;
                v.repairT = 0; g.audio.Shot("repair_done", new Vector3(v.x, v.y + 1, v.z), 1, v == g.player);
                if (v == g.player) g.Msg(v.repairWhat == "tracks" ? "Tracks repaired — we can move." : "Main gun back in action.");
                else if (v.team == g.ME) g.Msg(Defs.NAMES[v.team][v.type] + " repaired its " + v.repairWhat + ".");
            }
        }

        /// Leaderless guns: a rifleman with nothing to do within 30 m goes and mans an abandoned friendly gun.
        public static void TryRecrew(Game g, Unit u)
        {
            if (u.mount != null || !u.alive || u.team < 0 || u == g.player || u.rout > 0 || u.target != null || (u.type != "rifle" && u.type != "crew")) return;
            foreach (var v in g.units)
            {
                if (!v.alive || v.team != u.team || v.def.kind != "gun" || v.crew == null || Alive(v) > 0) continue;
                float d = FU.Hyp(u.x - v.x, u.z - v.z); if (d > 30) continue;
                if (d < 2.6f) { Mount(g, u, v); if (u.team == g.ME) g.Msg(Defs.NAMES[u.team][u.type] + " has taken over the " + Defs.NAMES[v.team][v.type] + "."); return; }
                if (u.order == null) { u.order = new Order { x = v.x, z = v.z, force = true }; u.leash = 40; }
                return;
            }
        }

        // ---------------- lorry drivers ----------------
        /// The convoy lorries are scenery, not units: give each a man at the wheel from the soldier model (seated: legs bent, hands forward).
        public static void AddDriver(Transform truck, int team)
        {
            if (!ModelLib.Has("inf_" + team + "_rifle")) return;
            var m = ModelLib.Spawn("inf_" + team + "_rifle", truck, true).transform; m.name = "driver"; m.localPosition = new Vector3(-.42f, .12f, 1.0f); m.localRotation = Quaternion.identity;
            var hip = ModelLib.Find(m, "hip"); var wep = ModelLib.Find(m, "weapon"); if (wep) wep.gameObject.SetActive(false);
            foreach (var n in new[] { "leg0", "leg1" }) { var l = ModelLib.Find(m, n); if (l) l.localRotation = Quaternion.AngleAxis(-82, Vector3.right); }
            foreach (var n in new[] { "knee0", "knee1" }) { var k = ModelLib.Find(m, n); if (k) k.localRotation = Quaternion.AngleAxis(84, Vector3.right); }
            foreach (var n in new[] { "arm0", "arm1" }) { var a = ModelLib.Find(m, n); if (a) a.localRotation = Quaternion.AngleAxis(-55, Vector3.right); }
            foreach (var h in m.GetComponentsInChildren<Transform>(true)) if (h.name.StartsWith("hit_")) Object.Destroy(h.gameObject);
        }
    }
}
