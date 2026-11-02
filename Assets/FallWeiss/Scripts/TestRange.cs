// Fall Weiss — weapons Test Range (mission "Test Range" in missions.json, menu or TestDriver "range").
// Static targets at 10/25/50/100/200 m, a walking target, an enemy tank parked side-on (armour faces) and a friendly
// tank to drive (F) for crushing / coaxial MG. Every infantry weapon on F1–F11 or the wheel, infinite ammunition.
// The readout reports hit zones and outcomes (no damage numbers) and the hits each weapon needed per kill;
// O toggles translucent hit-zone overlays on every unit.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FallWeiss
{
    public class TestRange
    {
        public static readonly string[] Weps = { "kar98k", "kar98kz", "mp38", "mg34", "p38", "p08", "bayonet", "wz29", "bar28", "ckm30", "vis" };
        static readonly float[] Dist = { 10, 25, 50, 100, 200 };

        class Slot { public float x, z, heading; public string type = "rifle"; public int team = 1; public bool walker; public Unit u; public float respawnT; public int hits; public string wep; public bool logged; }
        readonly Game g; readonly List<Slot> slots = new List<Slot>();
        readonly Dictionary<string, List<int>> stk = new Dictionary<string, List<int>>();
        string lastHit = "—"; float walkT;
        bool overlay; readonly Dictionary<Collider, GameObject> ov = new Dictionary<Collider, GameObject>(); float ovT;
        Slot enemyTank, ownTank; bool placed;
        static readonly Key[] FKeys = { Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8, Key.F9, Key.F10, Key.F11 };

        public TestRange(Game game) { g = game; Combat.onBullet += OnBullet; }
        public void Dispose() { Combat.onBullet -= OnBullet; ClearOverlay(); SoldierLib.forceAll = false; }

        public void Setup()
        {
            var sp = g.M.spawn[0]; sp.z += 14;   // firing point: clear of the spawn crates
            for (int i = 0; i < Dist.Length; i++) slots.Add(new Slot { x = sp.x + (i % 2 == 0 ? -2.5f : 2.5f), z = sp.z + Dist[i], heading = Mathf.PI });
            slots.Add(new Slot { x = sp.x, z = sp.z + 40, heading = Mathf.PI / 2, walker = true });
            enemyTank = new Slot { x = sp.x + 24, z = sp.z + 45, heading = -Mathf.PI / 2, type = "tank" }; slots.Add(enemyTank);   // side-on to the firing line
            ownTank = new Slot { x = sp.x - 12, z = sp.z + 4, heading = 0, type = "tank", team = 0 }; slots.Add(ownTank);
            foreach (var s in slots) Spawn(s);
            g.Msg("TEST RANGE: F1–F11 or wheel = weapons (infinite ammo) · O = hit-zone overlay · F on the tank = drive it · K = check ammo");
        }

        void Spawn(Slot s)
        {
            if (s.u != null && s.u.go) Object.Destroy(s.u.go); if (s.u != null) g.units.Remove(s.u);
            s.u = g.SpawnUnit(s.team, s.type, s.x, s.z, s.heading); s.u.dummy = true; s.u.prot = -99; s.hits = 0; s.wep = null; s.logged = false; s.respawnT = 0;
        }

        public void Update(float dt, Keyboard kb)
        {
            var p = g.player;
            if (!placed && p != null) { placed = true; p.x = g.M.spawn[0].x; p.z = g.M.spawn[0].z + 14; p.y = World.H(p.x, p.z); }
            if (p != null && p.alive && p.def.kind == "inf")
            {
                if (p.weps == null || p.weps.Length != Weps.Length) { p.InitWeapons(Weps); g.BuildViewModel(); }
                for (int i = 0; i < FKeys.Length && i < Weps.Length; i++) if (kb[FKeys[i]].wasPressedThisFrame && i != p.wi) { p.SwitchWeapon(i); g.BuildViewModel(); g.Msg("▸ " + p.Weapon.name); }
                var w = p.Weapon; if (w != null && w.clip > 0) p.reserve = Mathf.Max(p.reserve, w.clip * 4); p.gren = 4; p.bandages = 3;
            }
            if (kb.oKey.wasPressedThisFrame) { overlay = !overlay; if (!overlay) ClearOverlay(); g.Msg(overlay ? "Hit-zone overlay on" : "Hit-zone overlay off"); }
            walkT += dt;
            foreach (var s in slots)
            {
                var u = s.u; if (u == null) continue;
                if (u.alive && s.walker)
                {   // walks back and forth across the lane at a steady pace
                    float k = Mathf.Sin(walkT * .35f); u.x = s.x + k * 14; u.heading = Mathf.Cos(walkT * .35f) >= 0 ? Mathf.PI / 2 : -Mathf.PI / 2; u.moving = true; u.y = World.H(u.x, u.z);
                }
                if (u.alive && u != p) { u.stance = 0; if (u.def.kind == "tank") { u.order = null; u.cd = 1; } }
                if (!u.alive)
                {
                    if (!s.logged) { s.logged = true; if (s.wep != null && s.hits > 0) { if (!stk.TryGetValue(s.wep, out var l)) stk[s.wep] = l = new List<int>(); l.Add(s.hits); } }
                    s.respawnT += dt; if (s.respawnT > (u.def.kind == "tank" ? 5 : 2.5f) && u != p) Spawn(s);
                }
            }
            if (overlay) { ovT -= dt; if (ovT <= 0) { ovT = .5f; SyncOverlay(); } }
        }

        void OnBullet(Unit t, HitZone z, Unit by)
        {
            if (by == null || by != g.player) return;
            var s = slots.FirstOrDefault(q => q.u == t); string wid = by.Weapon != null ? by.Weapon.id : "?";
            if (s != null) { if (s.wep != wid) { s.wep = wid; s.hits = 0; } s.hits++; }
            float d = FU.Hyp(t.x - by.x, t.z - by.z);
            lastHit = Zone(z) + "  ·  " + Mathf.RoundToInt(d) + " m  ·  " + (!t.alive ? "<color=#ff5a4a>KILLED</color>" : t.bleed > 4 ? "<color=#ffb04a>badly wounded, bleeding heavily</color>" : t.bleed > 0 ? "<color=#ffd96a>wounded, bleeding</color>" : "wounded");
        }
        static string Zone(HitZone z) => z == HitZone.Head ? "HEAD" : z == HitZone.Chest ? "CHEST" : z == HitZone.Stomach ? "STOMACH" : z == HitZone.Arm ? "ARM" : z == HitZone.Leg ? "LEG" : "BODY";

        public void Draw(float W, float H)
        {
            var r = new Rect(W - 470, 92, 450, 300); UI.Fill(r, new Color(.03f, .04f, .025f, .62f)); UI.Frame(r, new Color(.42f, .48f, .29f, .8f));
            float y = r.y + 8;
            UI.Shadow(new Rect(r.x + 12, y, r.width - 24, 22), "TEST RANGE", UI.small, UI.Gold); y += 22;
            UI.Shadow(new Rect(r.x + 12, y, r.width - 24, 22), "Last hit:  " + lastHit, UI.small, UI.Text); y += 26;
            UI.Shadow(new Rect(r.x + 12, y, r.width - 24, 22), "Hits needed per kill (last · average):", UI.small, UI.Dim); y += 22;
            foreach (var id in Weps)
            {
                if (!stk.TryGetValue(id, out var l) || l.Count == 0) continue;
                UI.Shadow(new Rect(r.x + 18, y, r.width - 30, 20), Defs.WEAPONS[id].name + ":  " + l[l.Count - 1] + "  ·  " + l.Average().ToString("0.0") + "  (" + l.Count + " kills)", UI.small, UI.Text); y += 19;
                if (y > r.yMax - 44) break;
            }
            if (enemyTank != null && enemyTank.u != null)
            {
                var t = enemyTank.u; float f = t.hp / t.max; string st = !t.alive ? "destroyed" : f > .85f ? "intact" : f > .5f ? "damaged" : f > .2f ? "heavily damaged" : "crippled";
                UI.Shadow(new Rect(r.x + 12, r.yMax - 44, r.width - 24, 20), "Enemy tank (side-on): " + st, UI.small, UI.Dim);
            }
            UI.Shadow(new Rect(r.x + 12, r.yMax - 24, r.width - 24, 20), "F1–F11 weapons · O overlay " + (overlay ? "ON" : "off") + " · K ammo", UI.small, UI.Dim);
        }

        // ---------------- hit-zone overlay ----------------
        void SyncOverlay()
        {
            foreach (var kv in ov.Where(kv => kv.Key == null || !kv.Key.enabled || !kv.Key.gameObject.activeInHierarchy).ToList()) { if (kv.Value) Object.Destroy(kv.Value); ov.Remove(kv.Key); }
            foreach (var h in Object.FindObjectsByType<UnitHit>())
            {
                var c = h.GetComponent<Collider>(); if (!c || !c.enabled || ov.ContainsKey(c)) continue;
                var prim = GameObject.CreatePrimitive(c is SphereCollider ? PrimitiveType.Sphere : PrimitiveType.Cube); Object.Destroy(prim.GetComponent<Collider>());
                prim.name = "HitOverlay"; prim.transform.SetParent(c.transform, false);
                if (c is BoxCollider b) { prim.transform.localPosition = b.center; prim.transform.localScale = b.size; }
                else if (c is SphereCollider sc) { prim.transform.localPosition = sc.center; prim.transform.localScale = Vector3.one * sc.radius * 2; }
                var col = h.zone == HitZone.Head ? new Color(1, .15f, .1f, .45f) : h.zone == HitZone.Chest ? new Color(1, .55f, .1f, .38f) : h.zone == HitZone.Stomach ? new Color(1, .95f, .2f, .38f) : h.zone == HitZone.Arm ? new Color(.2f, .9f, 1, .38f) : h.zone == HitZone.Leg ? new Color(.3f, .45f, 1, .38f) : new Color(1, 1, 1, .18f);
                var mr = prim.GetComponent<Renderer>(); mr.sharedMaterial = Mats.Transparent(col, null, false, true); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ov[c] = prim;
            }
        }
        void ClearOverlay() { foreach (var kv in ov) if (kv.Value) Object.Destroy(kv.Value); ov.Clear(); }
    }
}
