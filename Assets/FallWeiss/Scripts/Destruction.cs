// Fall Weiss — deeper building destruction and fire (roadmap T33).
//
// Before: a wall section broke on its own, and at 55 % of walls down the whole house vanished into a flat rubble box with one
// dust burst. Nothing burned except a few scripted fires, and a collapsed house was cold rubble a second later.
//
// Now:
//  * Support: a ground-floor wall that goes takes what stands on it — the wall above falls a moment later, the walls beside
//    it crack (lose half their strength). Past 35 % of the walls the roof comes in.
//  * Fire: a high-explosive burst against a house can set it alight (more likely the bigger the burst). A fire grows over
//    ~40 s from a smoulder at the eaves to flames out of the roof, takes the building apart piece by piece (roof first), can
//    jump to a house within 8 m, and burns anyone who stays inside. The AI leaves a burning house and stops using it as cover.
//  * Collapse: walls and roof come down as tumbling debris (physics), the corners stay standing as broken wall stubs (real
//    cover with cover points), roof beams lie across the pile, a dust cloud hangs over the site for ~14 s (it blocks sight like
//    smoke), and the ruins burn for two to four minutes. A house that was already burning burns harder in its rubble.
using System.Collections.Generic;
using UnityEngine;

namespace FallWeiss
{
    public static class Destruction
    {
        class Site
        {
            public Bd bd; public Vector3 c; public float x0, x1, z0, z1, y0, heat, life = -1, tick, chewT, spreadT; public bool rubble;
            public readonly List<Vector3> pts = new List<Vector3>(); public AudioSource snd; public GameObject sndGo;
        }
        static readonly List<Site> sites = new List<Site>();
        static readonly List<GameObject> debris = new List<GameObject>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() { sites.Clear(); debris.Clear(); }

        public static void Clear()
        {
            foreach (var s in sites) if (s.sndGo) Object.Destroy(s.sndGo);
            foreach (var d in debris) if (d) Object.Destroy(d);
            sites.Clear(); debris.Clear();
        }
        public static bool Burning(Bd bd) { if (bd == null) return false; foreach (var s in sites) if (s.bd == bd && !s.rubble && s.heat > .2f) return true; return false; }

        // ---------------- fire ----------------
        public static void Ignite(Game g, Bd bd, float heat = .2f)
        {
            if (bd == null || !bd.alive) return;
            foreach (var s0 in sites) if (s0.bd == bd && !s0.rubble) { s0.heat = Mathf.Max(s0.heat, heat); return; }
            var s = new Site { bd = bd, c = new Vector3(bd.cx, bd.baseY + bd.H * .5f, bd.cz), x0 = bd.x0, x1 = bd.x1, z0 = bd.z0, z1 = bd.z1, y0 = bd.baseY, heat = heat };
            int n = Mathf.Clamp(Mathf.RoundToInt(bd.w * bd.d / 30), 3, 7);
            for (int i = 0; i < n; i++) s.pts.Add(new Vector3(FU.Rand(bd.x0 + 1, bd.x1 - 1), bd.baseY + bd.H * FU.Rand(.7f, .95f), FU.Rand(bd.z0 + 1, bd.z1 - 1)));
            Sound(g, s); sites.Add(s);
            if (g.player != null && g.player.alive && FU.Hyp(g.player.x - bd.cx, g.player.z - bd.cz) < 120) g.Msg("A house is on fire!");
        }
        static void RubbleFire(Game g, float x0, float x1, float z0, float z1, float y0, float heat)
        {
            var s = new Site { c = new Vector3((x0 + x1) / 2, y0 + 1, (z0 + z1) / 2), x0 = x0, x1 = x1, z0 = z0, z1 = z1, y0 = y0, heat = heat, rubble = true, life = FU.Rand(120, 240) * (.6f + heat * .6f) };
            int n = Mathf.Clamp(Mathf.RoundToInt((x1 - x0) * (z1 - z0) / 40), 2, 5);
            for (int i = 0; i < n; i++) s.pts.Add(new Vector3(FU.Rand(x0 + 1, x1 - 1), y0 + FU.Rand(.4f, 1.2f), FU.Rand(z0 + 1, z1 - 1)));
            Sound(g, s); sites.Add(s);
        }
        static void Sound(Game g, Site s)
        {
            s.sndGo = new GameObject("FireSound"); s.sndGo.transform.SetParent(World.root, false); s.sndGo.transform.position = s.c;
            s.snd = g.audio.Follow("fire_loop", s.sndGo.transform, .4f, true, 6, 140, 0);
        }

        public static void Tick(Game g, float dt)
        {
            if (sites.Count == 0) return; var cam = Camera.main ? Camera.main.transform.position : Vector3.zero;
            for (int i = sites.Count - 1; i >= 0; i--)
            {
                var s = sites[i]; s.tick -= dt; if (s.tick > 0) continue; float step = .25f; s.tick = step;
                if (!s.rubble && (s.bd == null || !s.bd.alive)) { Drop(i); continue; }   // the house fell: Collapse has started the rubble fire
                if (s.rubble) { s.life -= step; s.heat = Mathf.Min(s.heat, Mathf.Clamp01(s.life / 60f) + .15f); if (s.life <= 0) { Drop(i); continue; } }
                else s.heat = Mathf.Min(1, s.heat + step / 40f);   // ~40 s from a smoulder to a fully involved house
                if (s.snd) s.snd.volume = (.15f + .55f * s.heat) * SoundBank.WorldVol;
                if ((s.c - cam).sqrMagnitude < 520 * 520)
                    foreach (var p in s.pts) if (Random.value < .35f + .65f * s.heat) g.fx.Fire(p, s.heat > .55f);
                // heat: anyone inside or right against it is burned; the AI gets out
                if (s.heat > .35f)
                    foreach (var u in g.units)
                    {
                        if (!u.alive || u.mount != null || !u.Foot) continue;
                        bool inside = u.x > s.x0 && u.x < s.x1 && u.z > s.z0 && u.z < s.z1; if (!inside) continue;
                        if (s.rubble && u.y > s.y0 + 2.5f) continue;
                        Combat.Damage(g, u, (s.rubble ? 2.5f : 4f) * s.heat * step, false, false, null, true, true);
                        if (u != g.player && u.alive && (u.order == null || !u.order.force)) { u.hide = null; u.supp = Mathf.Min(1, u.supp + .2f); var ex = Exit(s, u.x, u.z); u.order = new Order { x = ex.x, z = ex.y, force = true, fast = true }; }
                        else if (u == g.player && Random.value < .05f) g.Msg("You're burning — get out of the building!");
                    }
                if (s.rubble) continue;
                // the fire takes the house apart: roof first, then the upper storey, then the walls (Interiors.BreakPiece → Collapse at 55 %)
                s.chewT -= step;
                if (s.heat > .5f && s.chewT <= 0)
                {
                    s.chewT = FU.Rand(2.5f, 5f) / s.heat; Obst pick = null; int best = -1;
                    foreach (var b in s.bd.pieces) { if (b.dead || b.floor) continue; int pr = b.pk == "roof" ? 3 : b.ry0 > 2.4f ? 2 : b.pk == "wall" || b.pk == "sill" || b.pk == "lintel" ? 1 : 0; if (pr > best || (pr == best && Random.value < .3f)) { best = pr; pick = b; } }
                    if (pick != null) Interiors.BreakPiece(g, pick);
                }
                // and jumps across a narrow gap
                s.spreadT -= step;
                if (s.heat > .75f && s.spreadT <= 0)
                {
                    s.spreadT = 6;
                    foreach (var o in Interiors.bds)
                    {
                        if (o == s.bd || !o.alive || Burning(o)) continue;
                        float gx = Mathf.Max(0, Mathf.Max(o.x0 - s.x1, s.x0 - o.x1)), gz = Mathf.Max(0, Mathf.Max(o.z0 - s.z1, s.z0 - o.z1));
                        if (FU.Hyp(gx, gz) < 8 && Random.value < .25f) { Ignite(g, o, .15f); break; }
                    }
                }
            }
        }
        static Vector2 Exit(Site s, float x, float z)
        {   // the nearest wall, then 4 m beyond it
            float dl = x - s.x0, dr = s.x1 - x, db = z - s.z0, df = s.z1 - z, m = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(db, df));
            return m == dl ? new Vector2(s.x0 - 4, z) : m == dr ? new Vector2(s.x1 + 4, z) : m == db ? new Vector2(x, s.z0 - 4) : new Vector2(x, s.z1 + 4);
        }
        static void Drop(int i) { var s = sites[i]; if (s.sndGo) Object.Destroy(s.sndGo); sites.RemoveAt(i); }

        /// A high-explosive burst against a house: the bigger the burst, the likelier the fire (a 37 mm shell rarely, a bomb often).
        public static void BlastAt(Game g, Bd bd, float r)
        {
            if (bd == null || !bd.alive || r < 3.5f) return;
            if (Random.value < Mathf.Clamp01((r - 3) * .07f)) Ignite(g, bd, .2f + (r >= 6.5f ? .3f : 0));
        }

        // ---------------- support: what a broken wall takes with it ----------------
        public static void OnPieceBroken(Game g, Obst b)
        {
            var bd = b.bd; if (bd == null || !bd.alive) return;
            bool wall = b.pk == "wall" || b.pk == "sill" || b.pk == "lintel" || b.pk == "pillar";
            if (wall)
            {
                foreach (var o in bd.pieces)
                {
                    if (o.dead || o == b || o.floor) continue;
                    bool overlap = o.x0 < b.x1 - .1f && o.x1 > b.x0 + .1f && o.z0 < b.z1 - .1f && o.z1 > b.z0 + .1f;
                    if (overlap && o.pk != "roof" && o.ry0 >= b.ry1 - .15f && Random.value < .75f) { var oo = o; g.After(FU.Rand(.3f, 1.1f), () => { if (!oo.dead && oo.bd != null && oo.bd.alive) Interiors.BreakPiece(g, oo); }); }   // nothing under it any more
                    else if (!overlap && Mathf.Abs(o.ry0 - b.ry0) < .2f && (o.pk == "wall" || o.pk == "pillar"))
                    {   // the wall either side cracks
                        float gx = Mathf.Max(0, Mathf.Max(o.x0 - b.x1, b.x0 - o.x1)), gz = Mathf.Max(0, Mathf.Max(o.z0 - b.z1, b.z0 - o.z1));
                        if (gx + gz < .15f) o.hp = Mathf.Min(o.hp, o.maxhp * .5f);
                    }
                }
                if (bd.wallCount > 0 && bd.deadWalls > bd.wallCount * .35f && !bd.roofGone)
                {   // the roof has lost its bearing walls: it comes in over a second or two
                    float k = 0; foreach (var o in bd.pieces) if (!o.dead && o.pk == "roof") { var oo = o; k += FU.Rand(.2f, .5f); g.After(k, () => { if (!oo.dead && oo.bd != null && oo.bd.alive) Interiors.BreakPiece(g, oo); }); }
                }
            }
            // a little falling masonry for every piece
            var c = new Vector3((b.x0 + b.x1) / 2, bd.baseY + (b.ry0 + b.ry1) / 2, (b.z0 + b.z1) / 2);
            Chunks(g, c, new Vector3(b.x1 - b.x0, b.ry1 - b.ry0, b.z1 - b.z0), b.pk == "roof" ? bd.roofC : b.hex != 0 ? b.hex : bd.wall, b.pk == "roof" ? 3 : 2, 4);
        }

        // ---------------- collapse ----------------
        public static void OnCollapse(Game g, Bd bd, bool wasBurning)
        {
            float w = bd.x1 - bd.x0, d = bd.z1 - bd.z0, y0 = bd.baseY, H = bd.H; var c = new Vector3(bd.cx, y0 + H * .5f, bd.cz);
            Chunks(g, c, new Vector3(w * .8f, H * .8f, d * .8f), bd.wall, Mathf.Clamp(Mathf.RoundToInt(w * d / 10), 8, 18), 7);
            Chunks(g, c + Vector3.up * H * .35f, new Vector3(w * .7f, .6f, d * .7f), bd.roofC, 5, 4);
            int[] dark = { 0x4a3a2a, 0x3b2e22, 0x2a2420 };
            // the corners stand: L-shaped wall stubs, broken off at different heights (real cover)
            float t = Mathf.Max(.35f, bd.th > 0 ? bd.th : .4f);
            foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            {
                if (Random.value < .25f) continue;
                float cx = sx < 0 ? bd.x0 : bd.x1, cz = sz < 0 ? bd.z0 : bd.z1, h = Mathf.Min(H * .7f, FU.Rand(1.1f, 2.8f)), lx = Mathf.Min(w * .45f, FU.Rand(1.6f, 3.6f)), lz = Mathf.Min(d * .45f, FU.Rand(1.6f, 3.6f));
                var col = FU.Hex(bd.wall) * FU.Rand(.55f, .75f); col.a = 1;
                float ax0 = sx < 0 ? cx : cx - lx, ax1 = sx < 0 ? cx + lx : cx, az0 = sz < 0 ? cz : cz - t, az1 = sz < 0 ? cz + t : cz;
                float bx0 = sx < 0 ? cx : cx - t, bx1 = sx < 0 ? cx + t : cx, bz0 = sz < 0 ? cz : cz - lz, bz1 = sz < 0 ? cz + lz : cz;
                float h2 = h * FU.Rand(.6f, 1);
                foreach (var (x0, x1, z0, z1, hh) in new[] { (ax0, ax1, az0, az1, h), (bx0, bx1, bz0, bz1, h2) })
                {   // each stub its own mesh, linked as the obstacle's visual: a shell that knocks it down must take the picture with it
                    var smb = new MeshBatch(); smb.Box(new Vector3((x0 + x1) / 2, y0 + hh / 2, (z0 + z1) / 2), new Vector3(x1 - x0, hh, z1 - z0), 0, col);
                    var o = World.AddObst(x0, x1, z0, z1, hh, hh < 1.7f, 300); o.y0 = y0; o.y1 = y0 + hh; o.kind = "ruin"; o.vis = smb.Build("RuinWall", World.root, Mats.VC); World.MakeColliderPublic(o);
                }
            }
            // roof beams across the pile
            for (int k = 0; k < Mathf.Clamp(Mathf.RoundToInt(w / 2.5f), 2, 6); k++)
            {
                float bx = FU.Rand(bd.x0 + 1, bd.x1 - 1), bz = FU.Rand(bd.z0 + 1, bd.z1 - 1), L = Mathf.Min(w, d) * FU.Rand(.5f, .9f);
                var bm = new GameObject("beam"); bm.transform.SetParent(World.root, false); bm.transform.position = new Vector3(bx, y0 + FU.Rand(.7f, 1.6f), bz);
                bm.transform.rotation = Quaternion.Euler(FU.Rand(-25, 25), FU.Rand(0, 180), FU.Rand(-20, 20));
                var bmb = new MeshBatch(); bmb.Box(Vector3.zero, new Vector3(.22f, .22f, L), 0, FU.Hex(dark[k % 3])); bmb.Build("beam", bm.transform, Mats.VC);
            }
            // a hanging dust cloud that blocks sight like smoke, and the ruins burn
            g.AddDust(new Vector3(bd.cx, y0, bd.cz), 14, Mathf.Clamp(Mathf.Max(w, d) * .7f, 6, 14));
            RubbleFire(g, bd.x0, bd.x1, bd.z0, bd.z1, y0, wasBurning ? 1 : .55f);
        }

        /// Plain (non-interior) buildings that fall: the same burning rubble, smaller.
        public static void OnCollapseSimple(Game g, Obst b)
        {
            if (b.low || b.kind == "ruin" || b.kind == "fence" || b.kind == "hedge") return;
            float w = b.x1 - b.x0, d = b.z1 - b.z0; if (w * d < 12) return;
            Chunks(g, new Vector3((b.x0 + b.x1) / 2, b.y0 + b.h * .5f, (b.z0 + b.z1) / 2), new Vector3(w * .8f, b.h * .8f, d * .8f), b.hex != 0 ? b.hex : 0x8a7f70, Mathf.Clamp(Mathf.RoundToInt(w * d / 12), 6, 14), 6);
            g.AddDust(new Vector3((b.x0 + b.x1) / 2, b.y0, (b.z0 + b.z1) / 2), 10, Mathf.Clamp(Mathf.Max(w, d) * .6f, 5, 11));
            RubbleFire(g, b.x0, b.x1, b.z0, b.z1, b.y0, b.smoking ? 1 : .5f);
        }

        /// Tumbling masonry: physics boxes thrown from a volume, gone after ~20 s. Capped so a street fight cannot fill the scene.
        static void Chunks(Game g, Vector3 c, Vector3 size, int hex, int n, float speed)
        {
            debris.RemoveAll(x => !x); if (debris.Count > 160) n = Mathf.Min(n, 2);
            var col = FU.Hex(hex == 0 ? 0x8a7f70 : hex);
            for (int i = 0; i < n; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = "debris"; go.transform.SetParent(World.root, false);
                go.transform.position = c + new Vector3(FU.Rand(-.5f, .5f) * size.x, FU.Rand(-.5f, .5f) * size.y, FU.Rand(-.5f, .5f) * size.z);
                float s = FU.Rand(.25f, .9f); go.transform.localScale = new Vector3(s * FU.Rand(.8f, 1.8f), s * FU.Rand(.4f, 1), s * FU.Rand(.8f, 1.8f)); go.transform.rotation = Random.rotation;
                var mr = go.GetComponent<MeshRenderer>(); float sh = i % 3 == 0 ? .7f : i % 3 == 1 ? .85f : 1; var cc = col * sh; cc.a = 1; mr.sharedMaterial = Mats.Lit(cc);   // three shades per colour: Mats caches by colour, a random shade per chunk would mint a material each
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var rb = go.AddComponent<Rigidbody>(); rb.mass = 40 * s; rb.linearVelocity = new Vector3(FU.Rand(-1, 1) * speed, FU.Rand(.5f, 1.5f) * speed * .5f, FU.Rand(-1, 1) * speed); rb.angularVelocity = Random.insideUnitSphere * 6;
                // once it has landed it is scenery: every gameplay ray uses ~0 (all layers), so a lying chunk would be bulletproof and block sight
                float settle = FU.Rand(2.6f, 3.4f); Object.Destroy(rb, settle); Object.Destroy(go.GetComponent<Collider>(), settle + .05f);
                Object.Destroy(go, FU.Rand(16, 24)); debris.Add(go);
            }
        }
    }
}
