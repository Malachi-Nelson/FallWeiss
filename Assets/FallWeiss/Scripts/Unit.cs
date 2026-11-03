// Fall Weiss — units: procedural models, movement/collision (port of moveUnit/collide/route),
// AI (port of acquire/updateUnit/npcFire) and combat (damage/kill/explode/suppress).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FallWeiss
{
    /// Attached to unit hit colliders so raycasts can find the unit (and tell head from body).
    public enum HitZone { Body, Head, Chest, Stomach, Arm, Leg }
    public class UnitHit : MonoBehaviour { public Unit u; public bool head; public HitZone zone = HitZone.Body; public bool fitted; }   // fitted: sized from the model mesh, not the default table

    public class Order { public float x, z, cx = float.NaN, cz = float.NaN; public bool force, fast, cover; public Vector3 C => float.IsNaN(cx) ? new Vector3(x, 0, z) : new Vector3(cx, 0, cz); }

    public class Unit
    {
        public int id, team; public string type; public UnitDef def;
        public float x, y, z, hp, max, heading, turretH, cd, think, idle, supp, mor = 100, stam = 1, rout, leash = 22, stuck, lastHit = -99, aimP, anim;
        public bool alive = true, moving, los, chase, spr, sprinting, squad, follow, manual;
        public int burst, kills, vet, stance, slot;
        public Unit target, focus, lastAtt;
        public Order order; public List<Order> queue = new List<Order>();
        public Vector2 home; public Vector3? detour; float detourT; public RoutePlan rp; public Hide hide; public float hideCd, bandaging, cd2, heat; public int secure = -1; public float[] wounds = new float[6]; public int bandages = 2, smoke, cls = -1; public float barrelT; public bool overheated;   // cls: index into Defs.CLASSES when deployed by the player
        // player weapon state
        public string[] weps; public int wi; public Dictionary<string, Vector2Int> ws = new Dictionary<string, Vector2Int>();
        public int clip, reserve, gren; public float reload, gcd;
        // visuals
        public GameObject go; public Transform turret; public GameObject ring; public float deadT;

        public WeaponDef Weapon => weps != null && weps.Length > 0 ? Defs.WEAPONS[weps[wi]] : null;
        public float StM => stance == 2 ? .32f : stance == 1 ? .68f : 1;
        public Vector3 Eye => new Vector3(x, y + def.my * StM, z);
        public Vector3 Chest => new Vector3(x, y + def.ty * StM, z);
        public bool Foot => def.kind == "inf" || def.kind == "cav";

        // ---------------- weapons (port of initWeapons/syncW/saveW) ----------------
        public void InitWeapons(string[] list)
        {
            weps = list; wi = 0; ws.Clear();
            foreach (var w in weps) { var d = Defs.WEAPONS[w]; ws[w] = new Vector2Int(d.clip, d.reserve); }
            Sync();
        }
        public void Sync() { var w = Weapon; rkind = ReloadKind.None; singlesActive = false; if (w == null) { clip = reserve = 0; return; } var s = ws[w.id]; clip = s.x; reserve = s.y; reload = 0; }
        // reload bookkeeping shared by player and AI (the model reads it): kind, duration of the current step
        public ReloadKind rkind; public float reloadDur; public bool singlesActive, reloadStop; public int npcAmmo = -1;
        public static int Capacity(string key) => key == "K98" ? 5 : key == "MP40" ? 32 : key == "STG44" ? 30 : key == "MG42" ? 50 : key == "Luger" ? 8 : 1;
        public static float NpcReloadTime(string key) => key == "K98" ? 2.6f : key == "MG42" ? 5f : key == "Luger" ? 2f : key == "Panzerschreck" ? 4f : 2.4f;
        public void Save() { var w = Weapon; if (w != null) ws[w.id] = new Vector2Int(clip, reserve); }
        public void SwitchWeapon(int i) { if (weps == null || i < 0 || i >= weps.Length || i == wi) return; Save(); wi = i; Sync(); cd = .4f; }

        // ---------------- model (original procedural models via ModelLib) ----------------
        static Mesh ringMesh;
        Vector3 fallAx; float deadRoll;
        Transform lay, hip, upper, headT, gear, weaponT; Transform[] legs, knees, arms; float wpP, swA, proneK, crK, lpx, lpz; Quaternion turretBase = Quaternion.identity;
        readonly List<Quaternion> legBase = new List<Quaternion>();
        public float bleed, bandT; public bool officer, dummy; public int score, deaths; public Unit crushBy; public float crushUnder;   // dummy = Test Range target (no AI)   // wound bleeding (hp/s); AI bandage timer
        public float prot; public bool visible = true; public float seenT = -99; Renderer[] rends;
        // POLYGON soldier model (infantry). The procedural model stays underneath as the hit-box skeleton, renderers off.
        public SoldierVisual sv; public float lastShot = -99; float svShot = -99;
        public Transform muzzleT;   // v4 models: marker at the real muzzle of the carried weapon (flashes / tracers start here)
        public float tankSp, coaxCd, crushX = float.NaN, crushZ; public int coaxBurst;
        public string ModelWeapon() { var w = Weapon; if (w != null) return w.id; var l = officer ? Defs.OfficerLoadout(team) : Defs.Loadout(team, type); return l != null && l.Length > 0 ? l[0] : ""; }

        public string Variant()
        {
            string k = type == "mg" ? "mg" : type == "sniper" ? "sniper" : "rifle";
            if (weps != null && weps.Length > 0) { var w = weps[0]; if (w == "mp38" || w == "bar28") k = "assault"; else if (w == "mg34" || w == "ckm30") k = "mg"; else if (w == "kar98kz" || w == "wz29z") k = "sniper"; }
            return k;
        }

        public void BuildModel(Transform parent)
        {
            if (go) Object.Destroy(go);
            string name = def.kind == "tank" ? "tank_" + team : def.kind == "gun" ? "at_" + team : def.kind == "cav" ? "cav_" + team : "inf_" + team + "_" + Variant();
            go = ModelLib.Spawn(name, parent); go.name = Defs.NAMES[team][type] + " #" + id;
            var t = go.transform;
            if (def.kind == "inf")
            {
                lay = ModelLib.Find(t, "lay"); hip = ModelLib.Find(t, "hip"); upper = ModelLib.Find(t, "upper"); headT = ModelLib.Find(t, "head"); gear = ModelLib.Find(t, "gear"); weaponT = ModelLib.Find(t, "weapon");
                legs = new[] { ModelLib.Find(t, "leg0"), ModelLib.Find(t, "leg1") }; knees = new[] { ModelLib.Find(t, "knee0"), ModelLib.Find(t, "knee1") }; arms = new[] { ModelLib.Find(t, "arm0"), ModelLib.Find(t, "arm1") };
                string v = Variant(); wpP = v == "mg" ? -.04f : v == "assault" ? (team == 0 ? -.08f : -.06f) : -.1f;
                if (!AddModelHits(t)) { AddHitBox(upper, new Vector3(0, .5f, 0), new Vector3(.5f, .4f, .38f), HitZone.Chest); AddHitBox(upper, new Vector3(0, .14f, 0), new Vector3(.46f, .32f, .36f), HitZone.Stomach); AddHitBox(hip, new Vector3(0, -.45f, 0), new Vector3(.42f, .95f, .36f), HitZone.Leg); AddHead(headT, new Vector3(0, .12f, 0), .15f); }
                muzzleT = ModelLib.Find(t, "muzzle");
                if (muzzleT) PackGuns.MountThirdPerson(weaponT, muzzleT, ModelLib.Find(t, "breech"), ModelWeapon());   // POLYGON pack gun where the pack has this weapon
                if (muzzleT) wpP = 0;   // v4 models: the gun is modelled where the hands hold it, so it must not tilt away from them
            }
            else
            {
                var lst = new List<Transform>(); for (int i = 0; i < 8; i++) { var l = ModelLib.Find(t, "leg" + i); if (l) { lst.Add(l); legBase.Add(l.localRotation); } }
                legs = lst.ToArray(); turret = ModelLib.Find(t, "turret"); if (turret) turretBase = turret.localRotation;
                if (def.kind == "tank") AddHitBox(t, new Vector3(0, 1.2f, 0), new Vector3(2.8f, 2.4f, 4.8f));
                else if (def.kind == "gun") AddHitBox(t, new Vector3(0, .8f, -.2f), new Vector3(2.0f, 1.6f, 2.6f));
                else if (!AddModelHits(t)) { AddHitBox(t, new Vector3(0, 1.4f, 0), new Vector3(1f, 2.4f, 2.4f)); AddHead(t, new Vector3(0, 2.75f, -.1f), .2f); }
            }
            if (!ringMesh) ringMesh = World.RingMesh(.85f, 1, 32);
            ring = new GameObject("SelRing"); ring.transform.SetParent(t, false); ring.transform.localPosition = new Vector3(0, .15f, 0);
            float rs = def.r * 1.6f + .4f; ring.transform.localScale = new Vector3(rs, 1, rs);
            ring.AddComponent<MeshFilter>().sharedMesh = ringMesh; var rr = ring.AddComponent<MeshRenderer>(); rr.sharedMaterial = Mats.Unlit(new Color(.4f, 1, .4f)); rr.shadowCastingMode = ShadowCastingMode.Off;
            ring.SetActive(false);
            rends = go.GetComponentsInChildren<Renderer>(true);
            sv = null; var lib = SoldierLib.I;
            if (def.kind == "inf" && lib != null && lib.Use(team))
            {   // swap the visual only: legacy meshes stay (renderers off) because the hit boxes ride on their pivots
                foreach (var r in rends) if (r && r.gameObject != ring) r.enabled = false;
                var m = lib.models[lib.teamModels[Mathf.Abs(id) % lib.teamModels.Length]];
                sv = SoldierVisual.Create(lay ? lay : t, m, SoldierLib.WeaponKey(ModelWeapon(), type));
                rends = sv.rends;
                // hit zones ride the visible body (arms raised on the rifle, crouch, prone), not the hidden legacy skeleton
                foreach (var h in t.GetComponentsInChildren<UnitHit>(true)) Object.Destroy(h.gameObject);
                sv.AddHitZones(this);
            }
            lpx = x; lpz = z; SyncTransform(0);
        }
        void AddHitBox(Transform p, Vector3 c, Vector3 s, HitZone z = HitZone.Body)
        {
            if (!p) return; var g = new GameObject("Hit"); g.transform.SetParent(p, false); g.transform.localPosition = c;
            var bc = g.AddComponent<BoxCollider>(); bc.size = s; bc.isTrigger = true; var h = g.AddComponent<UnitHit>(); h.u = this; h.zone = z;
        }
        /// v4 models carry their own hit volumes: empty nodes named hit_* whose position / rotation / scale are the box
        /// (unit cube) or, for hit_head, the sphere (unit diameter). They follow the animated pivots they hang on.
        bool AddModelHits(Transform root)
        {
            var list = new List<Transform>();
            foreach (var tr in root.GetComponentsInChildren<Transform>(true)) if (tr.name.StartsWith("hit_")) list.Add(tr);
            foreach (var tr in list)
            {
                string n = tr.name;
                var zn = n.Contains("head") || n.Contains("helmet") ? HitZone.Head : n.Contains("chest") ? HitZone.Chest : n.Contains("stomach") ? HitZone.Stomach : n.Contains("arm") ? HitZone.Arm : n.Contains("leg") || n.Contains("knee") ? HitZone.Leg : HitZone.Body;
                if (n.StartsWith("hit_head")) { var sc = tr.gameObject.AddComponent<SphereCollider>(); sc.radius = .5f; sc.isTrigger = true; }
                else { var bc = tr.gameObject.AddComponent<BoxCollider>(); bc.size = Vector3.one; bc.isTrigger = true; }
                var h = tr.gameObject.AddComponent<UnitHit>(); h.u = this; h.zone = zn; h.head = zn == HitZone.Head; h.fitted = true;
            }
            return list.Count > 0;
        }
        void AddHead(Transform p, Vector3 c, float r)
        {
            if (!p) return; var g = new GameObject("Head"); g.transform.SetParent(p, false); g.transform.localPosition = c;
            var sc = g.AddComponent<SphereCollider>(); sc.radius = r; sc.isTrigger = true; var h = g.AddComponent<UnitHit>(); h.u = this; h.head = true; h.zone = HitZone.Head;
        }
        public void SetVisible(bool v, bool force = false)
        {
            if (v == visible && !force) return; visible = v;
            if (rends != null) foreach (var r in rends) if (r && (!ring || r.gameObject != ring)) r.enabled = v;
        }
        public void SetRenderers(bool v) { if (rends != null) foreach (var r in rends) if (r && (!ring || r.gameObject != ring)) r.enabled = v; }

        static Quaternion Qx(float r) => Quaternion.AngleAxis(r * Mathf.Rad2Deg, Vector3.right);
        static Quaternion Qy(float r) => Quaternion.AngleAxis(r * Mathf.Rad2Deg, Vector3.up);
        static Quaternion Qz(float r) => Quaternion.AngleAxis(r * Mathf.Rad2Deg, Vector3.forward);

        /// Port of soldiers.js pose(): sw = leg swing, cr = crouch, pr = prone, bob, spr = sprint, aim (three.js X-mirrored to Unity).
        void Pose(float sw, float cr, float pr, float bob, float spr, float aim)
        {
            float st = 1 - pr, swing = sw * (1 - cr * .45f) * (1 - pr * .65f);
            if (lay) { lay.localRotation = Qx(Mathf.PI / 2 * pr); lay.localPosition = new Vector3(0, .17f * pr, -.9f * pr); }
            if (hip) hip.localPosition = new Vector3(0, .92f - .32f * cr * st + bob * st * (1 - cr), -.09f * cr * st);
            if (upper) upper.localRotation = Qx((.3f * cr + .1f * spr) * st) * Qy(-swing * .12f * st);
            if (headT) headT.localRotation = Qx(-1.15f * pr - .08f * cr) * Qy(swing * .1f * st);
            if (gear) gear.localRotation = Qx(-Mathf.PI / 2 * pr - aim) * Qy(-(swing * .07f + (pr > .01f ? swing * .1f : 0)));
            if (weaponT) weaponT.localRotation = Qx(wpP * (1 - pr));
            for (int i = 0; i < 2; i++)
            {
                float sd = i == 1 ? -1 : 1, lead = sd * swing;
                if (legs != null && legs[i]) legs[i].localRotation = Qx((-1f * cr + lead) * st + sd * swing * .45f * pr) * Qz(sd * .03f * pr);
                if (knees != null && knees[i]) knees[i].localRotation = Qx((1.7f * cr + Mathf.Max(0, -lead) * 1.25f + spr * .25f) * st + Mathf.Max(0, -sd * swing) * .5f * pr);
                if (arms != null && arms[i]) arms[i].localRotation = Qx((i == 1 ? swing : -swing) * .05f - spr * .12f);
            }
        }

        public void SyncTransform(float dt)
        {
            if (!go) return;
            bool veh = def.kind == "tank" || def.kind == "gun" || def.kind == "cav"; float tilt = 0;
            if (veh) { float s = Mathf.Sin(heading) * 2.2f, c = Mathf.Cos(heading) * 2.2f; tilt = -Mathf.Atan2(World.H(x + s, z + c) - World.H(x - s, z - c), 4.4f); }
            float lift = 0;
            if (def.kind == "inf")
            {
                if (alive)
                {   // port of poseUnit() for infantry
                    float mv = FU.Hyp(x - lpx, z - lpz); lpx = x; lpz = z; float spd = dt > 0 ? Mathf.Min(mv / dt, 14) : 0;
                    swA = Mathf.Lerp(swA, Mathf.Clamp01(spd / 2.2f), Mathf.Min(1, dt * 10)); if (mv < 3) anim += mv * (sprinting ? 2.6f : 3.7f);
                    proneK = Mathf.Lerp(proneK, stance == 2 ? 1 : 0, Mathf.Min(1, dt * 5)); crK = Mathf.Lerp(crK, stance == 1 ? 1 : 0, Mathf.Min(1, dt * 6));
                    float sw = Mathf.Sin(anim) * (sprinting ? .9f : .6f) * swA;
                    Pose(sw, crK, proneK, Mathf.Abs(Mathf.Sin(anim)) * (sprinting ? .07f : .04f) * swA, sprinting ? 1 : 0, aimP);
                    if (!sv)
                    {   // v4 models: NPCs run their magazines dry and reload; the gun dips and rolls while reloading and kicks on each shot
                        bool me = Game.I && Game.I.player == this;
                        if (!me && reload > 0) { reload -= dt; if (reload <= 0) { reload = 0; rkind = ReloadKind.None; } }
                        float rl = reload > 0 && reloadDur > 0 ? Mathf.Sin(Mathf.Clamp01(1 - reload / reloadDur) * Mathf.PI) : 0;
                        float kick = Mathf.Clamp01(1 - (Time.time - lastShot) / .12f);
                        if (gear && (rl > 0 || kick > 0)) gear.localRotation = gear.localRotation * Qx(.55f * rl - .08f * kick) * Qz(.3f * rl);
                    }
                    if (sv)
                    {
                        sv.SetWeapon(SoldierLib.WeaponKey(ModelWeapon(), type));
                        bool isPlayer = Game.I && Game.I.player == this;
                        if (!isPlayer && reload > 0) { reload -= dt; if (reload <= 0) { reload = 0; rkind = ReloadKind.None; npcAmmo = Capacity(sv.weapon); } }
                        if (lastShot > svShot) { svShot = lastShot; sv.Fire(); }
                        sv.dead = false; sv.speed = dt > 0 ? spd : 0; sv.moving = moving; sv.sprint = sprinting; sv.crouch = stance == 1 ? 1 : 0;
                        sv.reload = reload > 0 ? rkind : ReloadKind.None; sv.reloadT = reloadDur > 0 ? Mathf.Clamp01(1 - reload / reloadDur) : 0; sv.singles = singlesActive;
                        if (!isPlayer)
                        {
                            var tg = target != null && target.alive ? target : focus != null && focus.alive ? focus : null;
                            if (tg != null && (los || Time.time - lastShot < 2f)) { sv.hasAim = true; sv.aimPoint = tg.Chest; sv.raise = true; }
                            else { sv.hasAim = Time.time - lastShot < 1.5f; sv.raise = sv.hasAim; }
                        }
                    }
                }
                else
                {
                    // death animation: knees buckle, then the body topples over in a random direction (gravity ease + small bounce)
                    deadT += dt; if (sv) { sv.dead = true; sv.speed = 0; sv.raise = sv.sprint = false; sv.reload = ReloadKind.None; }
                    if (fallAx == Vector3.zero) { float a = Random.value * Mathf.PI * 2; fallAx = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); }
                    if (proneK > .5f) Pose(0, 0, 1, 0, 0, 0);
                    else
                    {
                        float k = Mathf.Clamp01((deadT - .08f) / .7f); k *= k; float bounce = deadT > .78f && deadT < 1.05f ? Mathf.Sin((deadT - .78f) / .27f * Mathf.PI) * 6 : 0;
                        Pose(0, Mathf.Clamp01(deadT * 4) * .45f * (1 - k), 0, 0, 0, 0);
                        if (lay) { lay.localRotation = Quaternion.AngleAxis(86 * k - bounce, fallAx); lay.localPosition = new Vector3(0, .08f * k, 0); }
                    }
                }
            }
            else
            {
                if (!alive) { deadT += dt; if (def.kind == "cav") { float k = Mathf.Clamp01(deadT / .8f); k *= k; deadRoll = (fallAx.x >= 0 ? 1 : -1) * 82 * k; if (fallAx == Vector3.zero) fallAx = Random.value < .5f ? Vector3.right : Vector3.left; } }
                if (alive && legs != null && legs.Length > 0)
                {
                    swA = Mathf.Lerp(swA, moving ? 1 : 0, Mathf.Min(1, dt * 9)); anim += dt * (def.kind == "cav" ? 13 : 9.5f) * (sprinting ? 1.5f : 1) * (swA > .05f ? 1 : 0);
                    float sw = Mathf.Sin(anim) * (sprinting ? .8f : .55f) * swA;
                    for (int i = 0; i < legs.Length; i++) if (legs[i]) legs[i].localRotation = legBase[i] * Qx(i % 2 == 1 ? -sw : sw);
                    lift = Mathf.Abs(Mathf.Sin(anim)) * (sprinting ? .09f : .05f) * swA * .6f;
                }
                if (turret) turret.localRotation = turretBase * Qy(FU.AngDiff(heading, turretH));
            }
            float sink = !alive && Foot ? Mathf.Max(0, deadT - 6.4f) * .5f : 0;   // bodies sink into the ground before being removed
            if (def.kind == "tank" && alive && dt > 0 && Game.I) Combat.Crush(Game.I, this, dt);
            go.transform.position = new Vector3(x, y + lift - sink, z);
            go.transform.rotation = Quaternion.Euler(tilt * Mathf.Rad2Deg, heading * Mathf.Rad2Deg, deadRoll);
        }

        // ---------------- movement (port of route / collide / moveUnit) ----------------
        Vector2 Route(float tx, float tz)
        {
            var M = World.M; if (!M.hasRiver || (def.kind != "tank" && def.kind != "gun")) return new Vector2(tx, tz);   // men wade; vehicles still need a bridge
            float cu = M.RiverC(x), ct = M.RiverC(tx), su = Mathf.Sign(z - cu), st = Mathf.Sign(tz - ct), edge = M.rw * 1.5f + 2.5f;
            int onB = World.BridgeAt(x, z), tB = World.BridgeAt(tx, tz);
            if (su == st && !(tB > 0 && onB == 0)) return new Vector2(tx, tz);
            float best = M.bridges[0], bd = 1e9f; foreach (var b in M.bridges) { float d = Mathf.Abs(b - x) + Mathf.Abs(b - tx); if (d < bd) { bd = d; best = b; } }
            float bx = tB > 0 ? M.bridges[tB - 1] : best; var apU = new Vector2(bx, M.RiverC(bx) + su * edge); var far = new Vector2(bx, M.RiverC(bx) - su * edge);
            if (onB > 0) return su == st ? new Vector2(tx, tz) : far;
            if (FU.Hyp(x - apU.x, z - apU.y) > 3.5f) return apU;
            return tB > 0 ? new Vector2(tx, tz) : far;
        }

        public void Collide(List<Unit> units, Unit player)
        {   // port of collide / collideB: walls only block at their own height, tanks crush wire and trees
            float r = def.r; string kind = def.kind; float hh = kind == "tank" ? 3.4f : (kind == "cav" || kind == "gun") ? 3.2f : 2.3f;
            if (kind == "tank" && Game.I && (tankSp > .8f || moving)) { float fx = Mathf.Sin(heading) * 1.4f, fz = Mathf.Cos(heading) * 1.4f; World.BreakNear(Game.I, x + fx - r - .3f, x + fx + r + .3f, z + fz - r - .3f, z + fz + r + .3f); }
            var list = World.obst;
            for (int bi = 0; bi < list.Count; bi++)
            {
                var b = list[bi]; if (b.dead || b.soft || b.floor) continue;
                if (x <= b.x0 - r || x >= b.x1 + r || z <= b.z0 - r || z >= b.z1 + r) continue;
                if (b.y0 > y + hh || b.y1 < y + .25f) continue;
                if (b.wire && kind == "tank") { if (Game.I) Structures.Collapse(Game.I, b); continue; }
                float cx = Mathf.Clamp(x, b.x0, b.x1), cz = Mathf.Clamp(z, b.z0, b.z1), dx = x - cx, dz = z - cz, dd = FU.Hyp(dx, dz);
                if (dd > 1e-4f) { if (dd < r) { x = cx + dx / dd * r; z = cz + dz / dd * r; } }
                else
                {
                    float l = x - b.x0, rr = b.x1 - x, t = z - b.z0, bt = b.z1 - z, m = Mathf.Min(Mathf.Min(l, rr), Mathf.Min(t, bt));
                    if (m == l) x = b.x0 - r; else if (m == rr) x = b.x1 + r; else if (m == t) z = b.z0 - r; else z = b.z1 + r;
                }
            }
            int cx0 = Mathf.FloorToInt(x / 10), cz0 = Mathf.FloorToInt(z / 10);
            for (int i = -1; i <= 1; i++) for (int j = -1; j <= 1; j++)
                {
                    long key = ((long)(cx0 + i) << 32) ^ (uint)(cz0 + j);
                    if (!World.treeGrid.TryGetValue(key, out var lst)) continue;
                    for (int ti = lst.Count - 1; ti >= 0; ti--)
                    {
                        if (ti >= lst.Count) continue; var t = lst[ti]; float dx = x - t.x, dz = z - t.z, dd = FU.Hyp(dx, dz), rr = r + .4f;
                        if (dd < rr && dd > 1e-4f) { if (kind == "tank") { if (Game.I) World.KillTree(Game.I, t); } else { x = t.x + dx / dd * rr; z = t.z + dz / dd * rr; } }
                    }
                }
            foreach (var o in units)
            {
                if (o == this || !o.alive) continue; float dx = x - o.x, dz = z - o.z; if (Mathf.Abs(dx) > 4 || Mathf.Abs(dz) > 4 || Mathf.Abs(o.y - y) > 2) continue;
                float dd = FU.Hyp(dx, dz), rr = (r + o.def.r) * .8f;
                if (dd < rr && dd > 1e-4f) { float k = (rr - dd) / 2; x += dx / dd * k; z += dz / dd * k; if (o != player) { o.x -= dx / dd * k * .5f; o.z -= dz / dd * k * .5f; } }
            }
            float H = World.M.half; x = Mathf.Clamp(x, -H, H); z = Mathf.Clamp(z, -H, H);
        }

        /// Deep water: float with the head out instead of walking along the bottom; moving through water throws spray.
        public void Swim()
        {
            if (def.kind != "inf" && def.kind != "cav") return; float wy = World.WaterY(x, z); if (wy < -900) return; float d = wy - World.H(x, z);
            if (d > World.SWIM && Interiors.At(x, z) == null) y = Mathf.Max(y, wy - World.SWIM);
            if (d > .12f && moving && Random.value < Time.deltaTime * 4 && Game.I) Game.I.fx.Puff(new Vector3(x, wy + .05f, z), new Color(.82f, .88f, .9f, .7f), .25f, .9f, .5f, Vector3.up * .9f);
        }
        public bool Move(float tx, float tz, float dt, float tol, float spMul, List<Unit> units, Unit player)
        {   // port of moveUnit: river bridges -> building doors/stairs -> around buildings
            float gt = Game.I ? Game.I.t : 0;
            float fx = tx, fz = tz; var rt = Route(tx, tz); tx = rt.x; tz = rt.y;
            var rb = Interiors.RouteBd(this, tx, tz, gt); tx = rb.x; tz = rb.y; var av = Interiors.AvoidBd(this, tx, tz); tx = av.x; tz = av.y;
            if (detour.HasValue) { detourT -= dt; var dv = detour.Value; if (detourT <= 0 || FU.Hyp(dv.x - x, dv.z - z) < 1.5f) detour = null; else { tx = dv.x; tz = dv.z; } }
            float dx = tx - x, dz = tz - z, d = FU.Hyp(dx, dz);
            if (d < tol) return true;
            float sp = def.speed * spMul * (supp > .7f ? .55f : 1) * (1 - Mathf.Clamp(World.Slope(x, z) - .25f, 0, .4f)) * World.WadeMul(this, World.Depth(x, z));
            if (def.kind == "tank")
            {   // tracks: pivot toward the way before driving off, build speed gradually, brake harder than you accelerate
                float turn = Mathf.Abs(FU.AngDiff(heading, Mathf.Atan2(dx, dz))), want = sp * (turn > .7f ? .2f : turn > .3f ? .6f : 1);
                tankSp = Mathf.MoveTowards(tankSp, want, (want > tankSp ? 2.4f : 6f) * dt); sp = tankSp;
            }
            float step = Mathf.Min(d, sp * dt), ox = x, oz = z;
            x += dx / d * step; z += dz / d * step; Collide(units, player);
            if (World.BlockedFor(this, x, z)) { x = ox; z = oz; }
            y = Interiors.SurfY(x, z, y); Swim();
            float moved = FU.Hyp(x - ox, z - oz); stuck = moved < step * .3f ? stuck + dt : Mathf.Max(0, stuck - dt);
            if (stuck > 3 && FU.Hyp(fx - x, fz - z) < 8) { stuck = 0; return true; }
            float tr = def.kind == "tank" ? 1.1f : 5; heading += Mathf.Clamp(FU.AngDiff(heading, Mathf.Atan2(dx, dz)), -dt * tr, dt * tr); moving = true;
            if (stuck > .6f && !detour.HasValue)
            {
                bool inB = Interiors.At(x, z) != null; float s = Random.value < .5f ? 1 : -1, k = inB ? 2.4f : 14;
                detour = new Vector3(x - dz / d * s * k + dx / d * k * .4f, 0, z + dx / d * s * k + dz / d * k * .4f); detourT = inB ? 1.2f : 2.2f; stuck = 0;
            }
            return false;
        }

        // ---------------- AI ----------------
        void Acquire(Game g)
        {
            var cands = new List<(float sc, float d, Unit e)>();
            foreach (var e in g.units)
            {
                if (!e.alive || e.team == team || g.IsProt(e)) continue; float d = FU.Hyp(x - e.x, z - e.z); if (d > def.sight) continue;
                float sc = d; if (def.pen && e.def.armor) sc -= 30; if (def.mg && !e.def.armor) sc -= 10; if (def.kind == "tank" && e.def.kind == "gun") sc -= 15; if (!def.pen && e.def.armor) sc += 40;
                cands.Add((sc, d, e));
            }
            cands.Sort((a, b) => a.sc.CompareTo(b.sc)); target = null; los = false; chase = false;
            for (int i = 0; i < Mathf.Min(cands.Count, 4); i++)
            {
                var (_, d, e) = cands[i];
                if (d <= def.range) { if (World.LOS(Eye + Vector3.up * .1f, e.Chest)) { target = e; los = true; return; } }
                else { target = e; return; }
            }
            if (target == null && lastAtt != null && lastAtt.alive && g.t - lastHit < 6) { target = lastAtt; chase = true; return; }
            if (cands.Count > 0) target = cands[0].e;
        }
        /// Port of decide(): infantry under fire or closing in look for cover facing the enemy.
        void Decide(Game g)
        {
            if (!Foot || rout > 0) return; if (hide != null || hideCd > 0 || (order != null && order.force)) return;
            var t = target; if (t == null || !t.alive) return; float d = FU.Hyp(x - t.x, z - t.z); bool pinned = supp > .6f || hp < max * .4f;
            if (!(pinned || ((supp > .2f || d > 18) && d <= def.range * 1.15f && Random.value < .55f))) return;
            var c = Structures.Find(this, t.x, t.z, g.t); hideCd = pinned ? 5 : 7;
            if (c != null) hide = new Hide { x = c.x, z = c.z, t = FU.Rand(8, 16), fire = !pinned || supp < .6f, low = c.low, trench = c.trench };
        }

        public AudioSource eng; float snortT = 5;
        /// Tank engines (Panzer III/IV recordings for the Wehrmacht's Panzer II, Renault R35 for the Polish Army, which fielded R35s in 1939)
        /// and cavalry hoof-beats. Runs for the player's own tank or horse too, and fades the engine out when the vehicle is knocked out.
        void Sounds(Game g, float dt)
        {
            string kind = def.kind; if (kind != "tank" && kind != "cav") return;
            if (!alive) { if (eng) SoundBank.Fade(eng, dt, kind == "tank" ? 1.5f : .3f); return; }
            if (!go) return;
            if (!eng)
            {
                eng = kind == "tank" ? g.audio.Follow(team == 0 ? "tank_ger" : "tank_pol", go.transform, 1, true, 8, 600, 0) : g.audio.Follow("hooves", go.transform, 1, true, 4, 300, 0);
                if (!eng) return; eng.volume = 0;
            }
            if (kind == "tank") { eng.pitch = Mathf.Lerp(eng.pitch, moving ? 1.05f : .8f, dt * 1.5f); eng.volume = Mathf.Lerp(eng.volume, moving ? .75f : .4f, dt * 2); }
            else
            {
                eng.volume = Mathf.Lerp(eng.volume, moving ? (sprinting ? .75f : .55f) : 0, dt * 6); eng.pitch = Mathf.Lerp(eng.pitch, sprinting ? 1.15f : 1, dt * 3);
                snortT -= dt; if (snortT <= 0) { snortT = Random.Range(6f, 16f); g.audio.Shot("snort", new Vector3(x, y + 1.8f, z)); }
            }
        }
        public void Update(Game g, float dt)
        {
            Sounds(g, dt);
            if (!alive || this == g.player) return;
            if (dummy) { cd = 1; return; }
            Combat.Bleed(g, this, dt, true); if (!alive) return;
            y = Interiors.SurfY(x, z, y); cd -= dt; think -= dt; hideCd -= dt; moving = false; heat = Mathf.Max(0, heat - dt * .06f); supp = Mathf.Max(0, supp - dt * (hide != null ? .5f : .12f));
            if (Foot)
            {
                mor = Mathf.Clamp(mor + dt * (supp > .45f ? -5 * (1 - .12f * vet) : 1.6f) + (g.InSZ(this) ? dt * 15 : 0), 0, 100);
                if (mor < 18 && rout <= 0 && !g.InSZ(this)) { rout = FU.Rand(7, 11); order = null; focus = null; hide = null; follow = false; if (team == g.ME && Random.value < .5f) g.Msg(Defs.NAMES[team][type] + " is routing!"); }
            }
            if (rout > 0) { rout -= dt; if (rout <= 0) mor = 45; }
            if (focus != null && !focus.alive) focus = null;
            if (think <= 0) { think = .2f + Random.value * .15f; if (focus == null) Acquire(g); else los = FU.Hyp(x - focus.x, z - focus.z) <= def.range && World.LOS(Eye, focus.Chest); Decide(g); }
            var tgt = focus ?? target; if (tgt != null && !tgt.alive) tgt = target = null;
            float d = tgt != null ? FU.Hyp(x - tgt.x, z - tgt.z) : 1e9f; bool engaged = tgt != null && d <= def.range && los; var o = order; bool forced = o != null && o.force;
            Vector2? dest = null; float tol = .5f, spm = 1; bool rush = false; bool destIsOrder = false;
            if (rout > 0) { var sp = g.SpawnPos(team); dest = new Vector2(sp.x, sp.z); rush = true; }
            else if (hide != null) { hide.t -= dt; if (FU.Hyp(hide.x - x, hide.z - z) > .8f) { dest = new Vector2(hide.x, hide.z); rush = true; } if (hide.t <= 0) hide = null; }
            else if (o != null && (forced || !engaged || def.fireOnMove)) { dest = new Vector2(o.x, o.z); destIsOrder = true; }
            else if (o == null && follow && g.player != null && g.player.alive && !engaged) { var s = g.FollowSlot(this); float dd = FU.Hyp(s.x - x, s.y - z); if (dd > 2.5f) { dest = s; tol = 1.5f; rush = dd > 12; } }
            else if (o == null && tgt != null && !engaged && def.kind != "gun" && !(follow && g.player != null)) { if (focus != null || chase || FU.Hyp(x - home.x, z - home.y) < leash) { if (d > def.range * .8f) dest = new Vector2(tgt.x, tgt.z); } }
            if (dest.HasValue)
            {
                var dv = dest.Value; float far = FU.Hyp(dv.x - x, dv.y - z);
                bool want = (!engaged || forced) && (rush || far > 16 || (o != null && o.fast)) && (def.kind != "tank" || far > 40);
                if (want && stam > (spr ? .03f : .25f)) { spr = true; spm *= Defs.SprintMul(def.kind); stam = Mathf.Max(0, stam - dt * (def.kind == "tank" ? .05f : .13f)); } else { spr = false; stam = Mathf.Min(1, stam + dt * .2f); }
                bool arrived = Move(dv.x, dv.y, dt, tol, spm, g.units, g.player);
                if (arrived && destIsOrder && !detour.HasValue)
                {
                    bool cv = o.cover; order = null; home = new Vector2(x, z); if (queue.Count > 0) { order = queue[0]; queue.RemoveAt(0); }
                    if (cv) { var ne = g.NearestEnemy(this); var c = Structures.Find(this, ne != null ? ne.x : g.M.spawn[1 - team].x, ne != null ? ne.z : g.M.spawn[1 - team].z, g.t); if (c != null) hide = new Hide { x = c.x, z = c.z, t = 30, fire = true, low = c.low, trench = c.trench }; }
                }
            }
            else { idle += dt; spr = false; stam = Mathf.Min(1, stam + dt * .2f); }
            sprinting = spr && moving;
            if (def.kind == "inf")
            {
                bool atHide = hide != null && FU.Hyp(hide.x - x, hide.z - z) <= .8f; int st = 0;
                if (!moving)
                {
                    if (type == "sniper") st = 2;
                    else if (atHide) st = !hide.fire ? 2 : hide.low ? (((g.t + id * .61f) % 4.4f) < 2.4f ? 0 : 1) : 1;
                    else if (supp > .55f) st = 2; else if (engaged) st = 1;
                }
                float wd = World.Depth(x, z); if (wd > .5f && st == 2) st = 1; if (wd > .86f) st = 0;   // RO2: no going prone in water, no crouching above the waist
                stance = st;
            }
            bool hidingMove = hide != null && FU.Hyp(hide.x - x, hide.z - z) > .8f;
            bool noFire = World.Depth(x, z) > World.SWIM || hidingMove || (hide != null && !hide.fire) || sprinting || rout > 0 || (hide != null && hide.low && stance == 1);
            if (tgt != null && engaged && !(forced && !def.fireOnMove) && !noFire)
            {
                float want = Mathf.Atan2(tgt.x - x, tgt.z - z);
                aimP = Mathf.Lerp(aimP, Mathf.Clamp(Mathf.Atan2(tgt.y + tgt.def.ty * tgt.StM - (y + def.my * StM), d), -.5f, .5f), .25f);
                if (def.kind == "tank" || def.kind == "gun")
                {
                    float trv = def.kind == "tank" ? .6f : 2.2f; turretH += Mathf.Clamp(FU.AngDiff(turretH, want), -dt * trv, dt * trv); if (def.kind == "gun") heading += Mathf.Clamp(FU.AngDiff(heading, want), -dt * 2.4f, dt * 2.4f);
                    if (cd <= 0 && Mathf.Abs(FU.AngDiff(turretH, want)) < .12f) Combat.NpcFire(g, this, tgt);
                }
                else { if (!moving) heading += Mathf.Clamp(FU.AngDiff(heading, want), -dt * 6, dt * 6); if (cd <= 0 && Mathf.Abs(FU.AngDiff(heading, want)) < .4f) Combat.NpcFire(g, this, tgt); }
            }
            else { aimP = Mathf.Lerp(aimP, 0, .08f); if (def.kind == "tank") turretH += Mathf.Clamp(FU.AngDiff(turretH, heading), -dt * .5f, dt * .5f); else if (def.kind != "gun") turretH = heading; }
            if (def.kind == "tank") { if (!moving) tankSp = Mathf.MoveTowards(tankSp, 0, 6 * dt); Combat.NpcCoax(g, this, tgt, d, engaged && !noFire, dt); }
        }
    }
    public class Hide { public float x, z, t; public bool fire, low, trench; }

    // ---------------- combat ----------------
    public static class Combat
    {
        /// AI tank coaxial MG: bursts at soft targets the turret is laid on (main gun keeps its own reload).
        public static void NpcCoax(Game g, Unit u, Unit t, float d, bool engaged, float dt)
        {
            u.coaxCd -= dt; if (t == null || !t.alive || t.def.armor || !engaged || d > 120 || u.coaxCd > 0) return;
            if (Mathf.Abs(FU.AngDiff(u.turretH, Mathf.Atan2(t.x - u.x, t.z - u.z))) > .25f) return;
            u.coaxCd = .11f; if (++u.coaxBurst >= 10) { u.coaxBurst = 0; u.coaxCd = Random.Range(1f, 1.8f); }
            var fw = new Vector3(Mathf.Sin(u.turretH), 0, Mathf.Cos(u.turretH)); var s = new Vector3(u.x, u.y + 1.7f, u.z) + fw * 2f;
            var e = new Vector3(t.x, t.y + t.def.ty * t.StM, t.z);
            float p = Mathf.Clamp(.32f * (1 - .6f * d / 120f) * (u.moving ? .6f : 1) * (t.moving ? .8f : 1) * (t.stance == 2 ? .4f : 1) * (1 - t.supp * .3f), .03f, .6f);
            bool hit = Random.value < p;
            if (!hit) { e.x += FU.Rand(-1.5f, 1.5f) * (1 + d / 50); e.z += FU.Rand(-1.5f, 1.5f) * (1 + d / 50); e.y += FU.Rand(-.4f, .4f); }
            if (Physics.Linecast(s, e, out var rh, ~0, QueryTriggerInteraction.Ignore)) { e = rh.point; hit = false; }
            g.fx.Tracer(s, e); g.fx.Flash(s, .12f); g.audio.Shot(u.team == 0 ? "mg42" : "browning", s);
            if (hit) Bullet(g, t, u.def.mgdmg, RollZone(t), u); else { Suppress(g, e.x, e.z, u.team, .1f, 4); g.fx.Dust(e); }
        }

        /// Tracks: a soldier dies only when the tank actually runs him over — he has to be under the hull (inside the track
        /// width) while the tank keeps rolling about a metre over him. Brushing the track edges, or a tank that stops or
        /// creeps, just shoves him aside (friends are always shoved).
        public static void Crush(Game g, Unit u, float dt)
        {
            if (float.IsNaN(u.crushX)) { u.crushX = u.x; u.crushZ = u.z; return; }
            float step = FU.Hyp(u.x - u.crushX, u.z - u.crushZ), v = step / dt; u.crushX = u.x; u.crushZ = u.z; if (v > 40) return;
            float fx = Mathf.Sin(u.heading), fz = Mathf.Cos(u.heading);
            const float hw = 1.3f, hl = 2.4f;    // hull half width / half length (Panzer II / 7TP class)
            for (int i = g.units.Count - 1; i >= 0; i--)
            {
                var e = g.units[i]; if (!e.alive || e == u || !e.Foot || Mathf.Abs(e.y - u.y) > 2) continue;
                float dx = e.x - u.x, dz = e.z - u.z, along = dx * fx + dz * fz, side = dx * fz - dz * fx, er = e.def.r * .5f;
                if (Mathf.Abs(along) > hl + er || Mathf.Abs(side) > hw + er) { if (e.crushBy == u) { e.crushBy = null; e.crushUnder = 0; } continue; }
                bool underHull = Mathf.Abs(side) < hw - .2f && e.team != u.team && v > 1.2f;
                if (!underHull)
                {   // grazed by the hull or the tank isn't rolling: knocked aside, out of the way of the tracks
                    if (v < .3f) continue;
                    float sg = side >= 0 ? 1 : -1, push = Mathf.Min(hw + er - Mathf.Abs(side) + .05f, 4 * dt + .02f);
                    e.x += fz * sg * push; e.z -= fx * sg * push; if (e.crushBy == u) e.crushUnder = Mathf.Max(0, e.crushUnder - step); continue;
                }
                if (e.crushBy != u) { e.crushBy = u; e.crushUnder = 0; }
                e.crushUnder += step;
                if (e.crushUnder < .9f) continue;   // the tracks have to actually roll over him
                e.crushBy = null; e.crushUnder = 0;
                Damage(g, e, 999, true, false, u, true);
                if (!e.alive)
                {
                    var p = new Vector3(e.x, e.y + .3f, e.z); g.fx.Puff(p, new Color(.32f, .05f, .04f, .9f), .3f, 1f, .5f, Vector3.up * .6f); g.fx.Dust(p);
                    if (u == g.player) g.Msg("Ran over " + Defs.NAMES[e.team][e.type]);
                }
            }
        }

        // ---------------- bullets (RO2-style: hit zone decides, wounds bleed) ----------------
        public static System.Action<Unit, HitZone, Unit> onBullet;   // Test Range readout
        public static float ZoneMul(HitZone z) => z == HitZone.Head ? 4 : z == HitZone.Chest ? 1.3f : z == HitZone.Arm || z == HitZone.Leg ? .5f : 1;
        /// A small-arms hit on a zone. Soldiers take the zone multiplier and start bleeding if they survive; vehicles / horses take it flat.
        public static void Bullet(Game g, Unit t, float dmg, HitZone z, Unit by)
        {
            if (!t.alive) return;
            if (t.def.kind != "inf") { Damage(g, t, dmg, false, false, by); return; }
            Damage(g, t, dmg * ZoneMul(z), false, z == HitZone.Head, by); onBullet?.Invoke(t, z, by);
            if (t.alive) { t.bleed = Mathf.Min(8, t.bleed + (z == HitZone.Chest ? 5 : z == HitZone.Stomach ? 3.5f : z == HitZone.Head ? 4 : 1.1f)); t.bandT = 0; t.lastAtt = by ?? t.lastAtt; t.wounds[(int)z] = 1; }
        }
        /// Where an AI round lands: exposed zones depend on stance (prone shows head / shoulders / arms, crouched hides the legs).
        public static HitZone RollZone(Unit t)
        {
            float r = Random.value;
            if (t.stance == 2) return r < .2f ? HitZone.Head : r < .45f ? HitZone.Chest : r < .8f ? HitZone.Arm : r < .9f ? HitZone.Stomach : HitZone.Leg;
            if (t.stance == 1) return r < .1f ? HitZone.Head : r < .48f ? HitZone.Chest : r < .72f ? HitZone.Stomach : r < .9f ? HitZone.Arm : HitZone.Leg;
            return r < .08f ? HitZone.Head : r < .42f ? HitZone.Chest : r < .64f ? HitZone.Stomach : r < .8f ? HitZone.Arm : HitZone.Leg;
        }
        /// Damage of the shooter's carried weapon (falls back to the unit's own value for horsemen / crews).
        public static float WeaponDmg(Unit u)
        {
            if (u.def.kind != "inf") return u.def.dmg;
            return Defs.WEAPONS.TryGetValue(u.ModelWeapon(), out var w) && !w.melee && w.dmg > 0 ? w.dmg : u.def.dmg;
        }
        /// Bleeding wounds: lose hp until bandaged; AI soldiers patch themselves up after a few seconds out of the fight.
        public static void Bleed(Game g, Unit u, float dt, bool ai)
        {
            if (!u.alive || u.bleed <= 0) return;
            u.hp -= u.bleed * dt;
            if (ai) { u.bandT += dt * (u.moving || u.cd < .5f ? .4f : 1); if (u.bandT > 6) { u.bleed = 0; u.bandT = 0; } }
            if (u.hp <= 0) Kill(g, u, u.lastAtt, false);
        }

        public static void NpcFire(Game g, Unit u, Unit t)
        {
            var def = u.def; float ax = t.x - u.x, az = t.z - u.z, d = Mathf.Max(.01f, FU.Hyp(ax, az)), dx = ax / d, dz = az / d;
            var s = u.sv && u.sv.MuzzleWorld.HasValue && u.visible ? u.sv.MuzzleWorld.Value : !u.sv && u.muzzleT && u.visible ? u.muzzleT.position : new Vector3(u.x + dx * .9f, u.y + def.my * u.StM, u.z + dz * .9f);   // v4 models: the gun's muzzle marker
            float p = def.acc * (1 + .1f * u.vet) * (1 - .55f * d / def.range) * (1 - u.supp * .6f); if (t.moving) p *= .7f; if (u.moving) p *= .6f; if (t == g.player) p *= .75f; if (u.moving && def.kind == "tank") p *= .7f; if (!def.pen && t.Foot && u.Foot) p *= .75f;   // lethal zones: AI riflemen trade fewer hits
            p = Mathf.Clamp(p, .05f, .95f);
            bool hit = Random.value < p; var e = new Vector3(t.x, t.y + t.def.ty * t.StM, t.z);
            if (!hit) { e.x += FU.Rand(-2, 2) * (1 + d / 40); e.z += FU.Rand(-2, 2) * (1 + d / 40); e.y += FU.Rand(-.6f, .6f); }
            if (Physics.Linecast(s, e, out var rh, ~0, QueryTriggerInteraction.Ignore)) { e = rh.point; hit = false; }
            NearMiss(g, u, s, e, hit ? t : null);
            g.fx.Tracer(s, e); g.fx.MuzzleFlash(s, e - s, def.pen ? 1.2f : .3f);
            bool big = def.pen;
            if (hit)
            {
                if (big) { Damage(g, t, def.dmg, true, false, u); Explode(g, e, def.splash, def.dmg * .5f, u.team, t, u); }
                else Bullet(g, t, WeaponDmg(u) * Mathf.Lerp(1, .62f, Mathf.Clamp01((d - .45f * def.range) / (.55f * def.range))), RollZone(t), u);
            }
            else { Suppress(g, e.x, e.z, u.team, .16f, 4); if (big) Explode(g, new Vector3(e.x, Mathf.Max(World.H(e.x, e.z), e.y), e.z), def.splash, def.dmg * .5f, u.team, null, u); else g.fx.Dust(e); }
            u.lastShot = Time.time;
            u.cd = def.rof * (1 - .06f * u.vet); if (def.burst > 0) { if (++u.burst >= def.burst) { u.burst = 0; u.cd = def.pause; } }
            if (def.mg && u.Foot)
            {   // RO2: sustained fire overheats the barrel; the crew has to swap it before firing again
                u.heat = Mathf.Min(1, u.heat + .05f);
                if (u.heat >= 1) { u.heat = 0; u.cd = Mathf.Max(u.cd, 5.5f); if (u.team == g.ME && Random.value < .3f) g.Msg(Defs.NAMES[u.team][u.type] + " is changing a hot barrel."); }
            }
            if (!u.sv && u.Foot && u.def.kind == "inf" && Defs.WEAPONS.TryGetValue(u.ModelWeapon(), out var mw) && mw.clip > 0)
            {   // v4 models: same magazine / clip / belt sizes and reload times as the player's weapons
                if (u.npcAmmo < 0) u.npcAmmo = mw.clip;
                if (--u.npcAmmo <= 0)
                {
                    float rt = mw.reload * Random.Range(.9f, 1.15f); u.npcAmmo = mw.clip;
                    u.reload = u.reloadDur = rt; u.rkind = mw.vm == "rifle" || mw.vm == "sniper" ? ReloadKind.Stripper : ReloadKind.Mag; u.cd = Mathf.Max(u.cd, rt + .3f);
                }
            }
            if (u.sv)
            {   // infantry with a modelled weapon run dry and reload (stripper clip / magazine / belt), which costs them time
                if (u.npcAmmo < 0) u.npcAmmo = Unit.Capacity(u.sv.weapon);
                if (--u.npcAmmo <= 0)
                {
                    float rt = Unit.NpcReloadTime(u.sv.weapon) * Random.Range(.9f, 1.15f);
                    u.reload = u.reloadDur = rt; u.rkind = u.sv.weapon == "K98" ? ReloadKind.Stripper : ReloadKind.Mag; u.cd = Mathf.Max(u.cd, rt + .3f);
                }
            }
            g.audio.Shot(big ? "cannon" : NpcSnd(u), s);
        }
        /// A round passing within ~3 m of the player cracks past his head (supersonic snap), from the closest point of its path.
        static void NearMiss(Game g, Unit u, Vector3 s, Vector3 e, Unit struck)
        {
            var p = g.player; if (p == null || !p.alive || p.team == u.team || struck == p || u.def.pen) return;
            var se = e - s; float L2 = se.sqrMagnitude; if (L2 < 25) return;
            var pe = p.Eye; float tt = Vector3.Dot(pe - s, se) / L2; if (tt < .1f || tt > 1.02f) return;
            var cp = s + se * Mathf.Clamp01(tt); if ((cp - pe).sqrMagnitude < 9) g.audio.Shot("crack", cp);
        }
        /// Which recording an AI shooter uses: Germans K98k / MP 40 / MG 34 (MG 42 recordings) / P38, Poles wz.29 Mauser / ckm wz.30 (Browning) / Vis.
        static string NpcSnd(Unit u)
        {
            if (!u.sv && Defs.WEAPONS.TryGetValue(u.ModelWeapon(), out var wd) && !string.IsNullOrEmpty(wd.snd)) return wd.snd;   // v4 models: the carried weapon's own sound, same key the player's copy uses
            if (u.def.mg) return u.team == 0 ? "mg42" : "browning";
            string w = u.sv ? u.sv.weapon : null;
            if (w == "MP40" || w == "STG44") return "mp40";
            if (w == "Luger") return u.team == 0 ? "p38" : "pistol";
            return "k98";
        }

        public static void Suppress(Game g, float x, float z, int team, float amt, float r)
        {
            foreach (var u in g.units) { if (!u.alive || u.team == team) continue; if (Mathf.Abs(u.x - x) < r && Mathf.Abs(u.z - z) < r) u.supp = Mathf.Min(1, u.supp + amt); }
        }

        static float painT;
        public static void Damage(Game g, Unit t, float dmg, bool pen, bool head, Unit by, bool splash = false)
        {
            if (!t.alive || g.IsProt(t)) return;
            if (t.def.armor)
            {   // armour: small arms do nothing; penetrating hits / blasts scale with the face they strike (front plate is thickest)
                if (!pen) return;
                Vector3? from = splash ? g.lastBoom : by != null ? new Vector3(by.x, 0, by.z) : (Vector3?)null;
                float face = 1;
                if (from.HasValue)
                {
                    float ang = Mathf.Abs(FU.AngDiff(t.heading, Mathf.Atan2(from.Value.x - t.x, from.Value.z - t.z))) * Mathf.Rad2Deg;
                    face = ang < 45 ? .4f : ang > 135 ? 1.6f : 1f;
                    if (by == g.player && ang < 45 && !splash) g.Msg("Glancing hit on the front armour — flank it!");
                }
                dmg *= face * (splash ? .6f : 1);
            }
            else if (pen && !splash) dmg *= .7f;
            if (t.Foot) t.mor = Mathf.Max(0, t.mor - dmg * .18f * (1 - .12f * t.vet));
            t.hp -= dmg; t.supp = Mathf.Min(1, t.supp + .1f); t.lastHit = g.t; if (by != null && by.alive) t.lastAtt = by;
            if (t.def.kind == "inf" && !head && t != g.player && Time.time - painT > .6f && Random.value < .55f)
            {   // a wounded man close by cries out (wordless shouts only, no language); headshots kill silently, and a cooldown stops a chorus
                var cm = Camera.main; if (cm && (cm.transform.position - t.Eye).sqrMagnitude < 35 * 35) { painT = Time.time; g.audio.Shot("pain", t.Eye); }
            }
            if (by == g.player && t.team != g.ME) g.HitMarker(t.hp <= 0, head);
            if (t == g.player) g.DamageFlash(splash ? null : by, splash ? g.lastBoom : (Vector3?)null);
            if (t.hp <= 0) Kill(g, t, by, head);
        }

        public static void Kill(Game g, Unit t, Unit by, bool head)
        {
            t.alive = false; t.hp = 0; t.order = null; g.selected.Remove(t); if (t.ring) t.ring.SetActive(false); t.deaths++;
            if (by != null && by.team != t.team) { int pts = 10; var ap = g.ActivePoint(); if (ap != null && by.team != g.M.attacker && FU.Hyp(by.x - ap.x, by.z - ap.z) < ap.r + 25) pts = 15; by.score += pts; }   // defending the active objective counts extra
            foreach (var c in t.go.GetComponentsInChildren<Collider>()) c.enabled = false;
            if (t.team == g.ME) g.losses++; else { g.kills++; g.cp += t.def.kind == "tank" ? 15 : 3; }
            g.tix[t.team] -= t.def.kind == "tank" ? 4 : t.def.kind == "gun" ? 2 : 1;
            if (by != null && by.alive && by.team != t.team)
            {
                by.kills++; int v = Mathf.Min(3, by.kills / 3);
                if (v > by.vet) { by.vet = v; if (by.team == g.ME) g.Msg(new string('★', v) + " " + Defs.NAMES[by.team][by.type] + " promoted — veteran rank " + v); }
            }
            foreach (var f in g.units) if (f.alive && f.team == t.team && f.Foot && FU.Hyp(f.x - t.x, f.z - t.z) < 16) f.mor = Mathf.Max(0, f.mor - 10 * (1 - .12f * f.vet));
            int range = by != null ? Mathf.RoundToInt(FU.Hyp(by.x - t.x, by.z - t.z)) : 0;   // RO2: kill messages show the range
            if (by == g.player && t.team != g.ME) { g.Msg("✕ " + Defs.NAMES[t.team][t.type] + (head ? " (headshot, " : " (") + range + " m)"); g.OnPlayerKill(t, head); }
            if (t == g.player && by != null && by != t) g.Msg("Killed by " + Defs.NAMES[by.team][by.type] + (head ? " — headshot" : "") + " at " + range + " m");
            if (t.team != g.ME && t.def.kind == "inf" && Random.value < .2f) g.DropPickup(t.x, t.z);
            t.SetVisible(true, true);
            if (t.def.kind == "tank" || t.def.kind == "gun") Wreck(g, t);
            if (t == g.player) g.OnPlayerDeath();
        }

        /// RO2 vehicle death: the hull goes burnt-out black; if the ammunition cooks off the turret is blown off as a
        /// separate physics piece, otherwise it is left knocked askew. The wreck burns for minutes, not seconds.
        public static bool forceCookOff;   // test hook (dbg cookoff)
        static void Wreck(Game g, Unit t)
        {
            foreach (var r in t.go.GetComponentsInChildren<Renderer>()) if (r.gameObject != t.ring) r.sharedMaterial = Mats.Burnt;
            var p = new Vector3(t.x, t.y + 1.4f, t.z); bool cookOff = t.def.kind == "tank" && t.turret && (forceCookOff || Random.value < .4f); forceCookOff = false;
            g.fx.Explosion(new Vector3(t.x, t.y + 1, t.z), cookOff ? 7 : 4); g.audio.Shot("boom", p);
            if (cookOff)
            {
                var tr = t.turret; t.turret = null; tr.SetParent(World.root, true);
                var b = new Bounds(tr.position, Vector3.zero); bool has = false; foreach (var r in tr.GetComponentsInChildren<Renderer>()) { if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds); }
                var bc = tr.gameObject.AddComponent<BoxCollider>(); var ls = tr.lossyScale; bc.center = tr.InverseTransformPoint(b.center); bc.size = new Vector3(b.size.x / Mathf.Max(.01f, ls.x), b.size.y / Mathf.Max(.01f, ls.y), b.size.z / Mathf.Max(.01f, ls.z)) * .8f;
                var rb = tr.gameObject.AddComponent<Rigidbody>(); rb.mass = 800; rb.linearVelocity = Vector3.up * Random.Range(7f, 10f) + new Vector3(Random.Range(-3f, 3f), 0, Random.Range(-3f, 3f)); rb.angularVelocity = Random.insideUnitSphere * 2.5f;
                g.AddFire(p, 180); g.AddFire(p + Vector3.up * .6f, 180);
                if (g.player != null && g.player.alive && FU.Hyp(g.player.x - t.x, g.player.z - t.z) < 220) g.Msg("Ammunition cook-off — the turret is blown clean off!");
            }
            else { t.turretH = t.heading + Random.Range(-1.4f, 1.4f); g.AddFire(p, 120); }
        }

        public static void Explode(Game g, Vector3 p, float r, float dmg, int srcTeam, Unit skip, Unit by)
        {
            g.lastBoom = p; g.fx.Explosion(p, Mathf.Max(1.6f, r * .8f)); g.audio.Shot("boom", p); Suppress(g, p.x, p.z, srcTeam, .5f, r * 3);
            Structures.Damage(g, p, r, dmg * 5); World.BlastTrees(g, p.x, p.z, r); World.Crater(p.x, p.z, Mathf.Max(2, r * .65f), Mathf.Min(1.5f, r * .22f));
            if (g.player != null && g.player.alive) { float dd = (new Vector3(g.player.x, g.player.y, g.player.z) - p).magnitude; if (dd < r * 5) g.Shake((1 - dd / (r * 5)) * Mathf.Min(1.2f, r * .2f)); }
            var list = g.units.ToArray();
            foreach (var u in list)
            {
                if (!u.alive || u == skip || u.team == srcTeam) continue; float d = FU.Hyp(u.x - p.x, u.z - p.z); if (d > r + u.def.r) continue;
                Damage(g, u, dmg * (1 - d / (r + u.def.r) * .7f), true, false, by, true);
            }
        }
    }
}
