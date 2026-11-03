// Fall Weiss: Poland 1939 — Unity port. Core data types, unit/weapon tables and math helpers.
// Ported from game.js (BASE / MOD / NAMES / WEAPONS / CLASSES / BUY) — values kept identical.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FallWeiss
{
    // ---------------- mission data (Resources/missions.json, exported from the JS MISSIONS array) ----------------
    [Serializable] public class FLine { public float x1, z1, x2, z2, w; }
    [Serializable] public class FArea { public float x, z, n, r; }
    [Serializable] public class FRect { public float x, z, w, d; }
    [Serializable] public class FHill { public float x, z, r, h; }
    [Serializable] public class FTrench { public float[] pts; }
    [Serializable] public class FBuild { public string k; public float x, z, w, d, h, rot; }
    [Serializable] public class FLandmark { public string k; public float x, z; }
    [Serializable] public class FSeg { public string k; public float x1, z1, x2, z2; }
    [Serializable] public class FProp { public string k; public float x, z, a, b; }
    [Serializable] public class FDecor { public string k; public float[] v; }
    [Serializable] public class FStream { public float[] pts; public float w, depth; }   // a brook / ditch, first point upstream
    [Serializable] public class FPoint { public string n; public float x, z, r; public bool inside; public float w, d, rot; public string key; }   // w,d>0: rectangular zone (rot radians); key: hill / bridgehead / building / area
    [Serializable] public class FXZ { public float x, z; }
    [Serializable] public class FForce { public string type; public int n; public int loc; public float sp; }
    [Serializable] public class FComp { public string type; public int n; }
    [Serializable] public class FWave { public float t0, every; public FComp[] comp; }
    [Serializable] public class FTod { public string top, hor, sun; public float dirY, fog; }

    [Serializable]
    public class Mission
    {
        public string name, date, brief;
        public int attacker, tickets, seed, barrage;
        public float time, half, vis, relief;
        public string ground;
        public bool city; public bool range;   // range = the weapons Test Range (no objectives / waves / AI director)
        public int fortified; public float north; public FStream[] streams; public int ponds;   // ponds: 0 = place natural ponds automatically, -1 = none   // north: real compass bearing of the map's +z axis, so the HUD compass matches the historical ground   // version of the RO2 objective fortification pass applied (Fall Weiss / Fortify Objectives)
        public FDecor[] decor;
        public bool hasSea; public float seaHx, seaHz;
        public bool hasRiver; public float rz0, ramp, rk, rw, rlevel; public float[] bridges;
        public string skyTop, skyHor, sun; public float[] sunDir; public float fogNear, fogFar;
        public FLine[] roads, lanes, rails;
        public FArea[] forests, villages;
        public FRect[] orchards;
        public FHill[] hills;
        public FTrench[] trenches;
        public FBuild[] build;
        public FLandmark[] landmarks;
        public FSeg[] segs;
        public FProp[] props;
        public FPoint[] points;
        public FXZ[] spawn;
        public FForce[] forces0, forces1;
        public FWave[] waves;
        public int wrecks, fires;
        public bool hasShip; public float shipX, shipZ;
        public bool hasTod; public FTod todFrom, todTo;
        public bool hasRain; public float rainStart, rainEnd, rainPeak;

        public FForce[] Forces(int team) => team == 0 ? forces0 : forces1;
        public float RiverC(float x) => rz0 + ramp * Mathf.Sin(x * rk);
    }

    [Serializable] public class MissionList { public Mission[] missions; }

    // ---------------- unit + weapon definitions ----------------
    public class UnitDef
    {
        public string type, kind;
        public float hp, speed, range, sight, dmg, rof, acc, r, my = 1.4f, ty = 1f, bh = 2.3f, splash, pause, mgdmg;
        public int burst;
        public bool armor, mg, pen, fireOnMove;
        public UnitDef Clone() => (UnitDef)MemberwiseClone();
    }

    public class WeaponDef
    {
        public string id, name, vm, snd = "rifle";
        public int clip, reserve;
        public float reload, rof, dmg, spread, ads = 40, recoil = .5f, range, reach;
        public bool auto, melee, scope, mg;
        public float fallNear = 160, fallFar = 340, fallMul = .8f;
    }

    public class ClassDef { public string key, label, type; public string[][] weps; public int gren, smoke; }
    public class BuyDef { public string key, label; public int cost, n; }

    public static class Defs
    {
        public static readonly Dictionary<string, UnitDef> BASE = new Dictionary<string, UnitDef>
        {
            ["rifle"] = new UnitDef { kind = "inf", hp = 100, speed = 5.3f, range = 75, sight = 115, dmg = 55, rof = 3.1f, acc = .46f, r = .5f, my = 1.4f, ty = 1f, bh = 2.3f },
            ["mg"] = new UnitDef { kind = "inf", hp = 100, speed = 4f, range = 100, sight = 120, dmg = 15, rof = .12f, burst = 5, pause = 2.6f, acc = .34f, r = .55f, my = 1f, ty = 1f, mg = true, bh = 2.3f },
            ["at"] = new UnitDef { kind = "gun", hp = 60, speed = 2.2f, range = 110, sight = 120, dmg = 62, rof = 3.4f, acc = .85f, r = 1.1f, my = 1f, ty = .8f, pen = true, splash = 2, bh = 2.2f },
            ["tank"] = new UnitDef { kind = "tank", hp = 420, speed = 7f, range = 110, sight = 125, dmg = 36, rof = 3.6f, acc = .8f, r = 1.8f, armor = true, my = 2f, ty = 1.1f, pen = true, splash = 4.6f, fireOnMove = true, mgdmg = 52, bh = 3.4f },
            ["sniper"] = new UnitDef { kind = "inf", hp = 100, speed = 4.8f, range = 190, sight = 210, dmg = 100, rof = 4.6f, acc = .85f, r = .5f, my = 1.4f, ty = 1f, bh = 2.3f },
            ["cav"] = new UnitDef { kind = "cav", hp = 100, speed = 10, range = 55, sight = 100, dmg = 50, rof = 3.2f, acc = .5f, r = .9f, my = 2.2f, ty = 1.3f, bh = 3.6f },
        };

        static readonly Dictionary<string, UnitDef> cache = new Dictionary<string, UnitDef>();
        public static UnitDef For(int team, string type)
        {
            string k = team + type;
            if (cache.TryGetValue(k, out var d)) return d;
            d = BASE[type].Clone(); d.type = type;
            if (team == 0 && type == "tank") d.hp = 440;
            if (team == 1 && type == "tank") { d.hp = 320; d.dmg = 38; d.speed = 6.4f; }
            if (team == 1 && type == "at") d.acc = .9f;
            cache[k] = d; return d;
        }

        public static readonly Dictionary<string, string>[] NAMES =
        {
            new Dictionary<string, string> { ["rifle"] = "Schütze", ["mg"] = "MG 34 Team", ["at"] = "3.7 cm PaK 36", ["tank"] = "Panzer II", ["sniper"] = "Scharfschütze", ["cav"] = "Kavallerie" },
            new Dictionary<string, string> { ["rifle"] = "Strzelec", ["mg"] = "CKM wz.30", ["at"] = "Bofors 37 mm", ["tank"] = "7TP Light Tank", ["cav"] = "Ułan Cavalry", ["sniper"] = "Strzelec wyborowy" },
        };
        public static readonly string[] SIDE = { "Wehrmacht", "Polish Army" };

        public static readonly Dictionary<string, WeaponDef> WEAPONS = new Dictionary<string, WeaponDef>
        {
            ["kar98k"] = new WeaponDef { name = "Karabiner 98k", vm = "rifle", clip = 5, reserve = 40, reload = 2.3f, rof = 1.1f, dmg = 100, spread = .0005f, ads = 40, recoil = .7f, fallMul = 1, snd = "k98" },
            ["wz29"] = new WeaponDef { name = "Karabin wz.29 Mauser", vm = "rifle", clip = 5, reserve = 40, reload = 2.3f, rof = 1.1f, dmg = 100, spread = .00055f, ads = 40, recoil = .7f, fallMul = 1, snd = "k98" },
            ["mp38"] = new WeaponDef { name = "MP 38", vm = "smg", clip = 32, reserve = 128, reload = 2.2f, rof = .12f, dmg = 45, spread = .0065f, auto = true, ads = 46, recoil = .2f, range = 90, fallNear = 40, fallFar = 120, fallMul = .6f, snd = "mp40" },
            ["bar28"] = new WeaponDef { name = "Rkm wz.28 (BAR)", vm = "bar", clip = 20, reserve = 100, reload = 2.7f, rof = .1f, dmg = 100, spread = .0022f, auto = true, ads = 45, recoil = .3f, range = 140, fallNear = 55, fallFar = 150, fallMul = .7f, snd = "bar" },
            ["mg34"] = new WeaponDef { name = "MG 34", vm = "mg", clip = 75, reserve = 225, reload = 4.6f, rof = .075f, dmg = 100, spread = .0026f, auto = true, ads = 46, recoil = .17f, mg = true, snd = "mg42" },
            ["ckm30"] = new WeaponDef { name = "Ckm wz.30", vm = "ckm", clip = 100, reserve = 300, reload = 4.2f, rof = .1f, dmg = 100, spread = .0026f, auto = true, ads = 46, recoil = .17f, mg = true, snd = "browning" },
            ["p38"] = new WeaponDef { name = "Walther P38", vm = "pistol", clip = 8, reserve = 24, reload = 2f, rof = .3f, dmg = 42, spread = .005f, ads = 60, recoil = .3f, range = 70, fallNear = 25, fallFar = 80, fallMul = .65f, snd = "p38" },
            ["vis"] = new WeaponDef { name = "Vis wz.35", vm = "pistol", clip = 8, reserve = 24, reload = 2f, rof = .3f, dmg = 42, spread = .005f, ads = 60, recoil = .3f, range = 70, fallNear = 25, fallFar = 80, fallMul = .65f, snd = "pistol" },
            ["p08"] = new WeaponDef { name = "Luger P08", vm = "pistol", clip = 8, reserve = 24, reload = 2.1f, rof = .28f, dmg = 42, spread = .0048f, ads = 60, recoil = .32f, range = 70, fallNear = 25, fallFar = 80, fallMul = .65f, snd = "pistol" },
            ["kar98kz"] = new WeaponDef { name = "Kar98k ZF39 (scoped)", vm = "sniper", clip = 5, reserve = 30, reload = 2.6f, rof = 1.5f, dmg = 110, spread = .00025f, ads = 9, scope = true, recoil = .8f, range = 420, fallNear = 999, fallFar = 1000, fallMul = 1, snd = "k98" },
            ["wz29z"] = new WeaponDef { name = "Mauser wz.29 (scoped)", vm = "sniper", clip = 5, reserve = 30, reload = 2.6f, rof = 1.5f, dmg = 110, spread = .00025f, ads = 9, scope = true, recoil = .8f, range = 420, fallNear = 999, fallFar = 1000, fallMul = 1, snd = "k98" },
            ["bayonet"] = new WeaponDef { name = "Bayonet", vm = "knife", melee = true, rof = .75f, dmg = 90, reach = 2.8f },
            ["sabre"] = new WeaponDef { name = "Szabla wz.34", vm = "sabre", melee = true, rof = .85f, dmg = 100, reach = 3.5f },
            // crew weapons (player driving a gun / tank)
            ["at_gun"] = new WeaponDef { name = "37 mm gun", vm = "none", clip = 0, spread = .002f, ads = 24 },
            ["tank_gun"] = new WeaponDef { name = "Main gun", vm = "none", clip = 0, spread = .003f, ads = 40 },
        };
        static Defs() { foreach (var kv in WEAPONS) kv.Value.id = kv.Key; }

        // default AI loadouts (for possessed AI units)
        /// Squad leaders: MP 38 + Luger P08 (Wehrmacht), wz.29 + Vis (Polish Army).
        public static string[] OfficerLoadout(int team) => team == 0 ? new[] { "mp38", "p08" } : new[] { "wz29", "vis" };
        public static string[] Loadout(int team, string type)
        {
            switch (type)
            {
                case "rifle": return team == 0 ? new[] { "kar98k", "p08", "bayonet" } : new[] { "wz29", "vis", "bayonet" };
                case "mg": return team == 0 ? new[] { "mg34", "p08" } : new[] { "ckm30", "vis" };
                case "sniper": return team == 0 ? new[] { "kar98kz", "p08", "bayonet" } : new[] { "wz29z", "vis", "bayonet" };
                case "cav": return team == 0 ? new[] { "kar98k", "sabre", "p08" } : new[] { "wz29", "sabre", "vis" };   // 1939: each side its own carbine and pistol
                case "at": return new[] { "at_gun" };
                case "tank": return new[] { "tank_gun" };
            }
            return new string[0];
        }

        public static readonly ClassDef[] CLASSES =
        {
            new ClassDef { key = "rifleman", label = "Rifleman", type = "rifle", gren = 2, weps = new[] { new[] { "kar98k", "p08", "bayonet" }, new[] { "wz29", "vis", "bayonet" } } },
            new ClassDef { key = "assault", label = "Assault", type = "rifle", gren = 3, smoke = 1, weps = new[] { new[] { "mp38", "p08", "bayonet" }, new[] { "bar28", "vis", "bayonet" } } },
            new ClassDef { key = "gunner", label = "Machine Gunner", type = "mg", gren = 1, weps = new[] { new[] { "mg34", "p08" }, new[] { "ckm30", "vis" } } },
            new ClassDef { key = "officer", label = "Officer", type = "rifle", gren = 1, smoke = 2, weps = new[] { new[] { "mp38", "p08" }, new[] { "wz29", "vis" } } },
            new ClassDef { key = "sniper", label = "Sniper", type = "sniper", gren = 1, weps = new[] { new[] { "kar98kz", "p08" }, new[] { "wz29z", "vis" } } },
        };

        public static readonly BuyDef[] BUY =
        {
            new BuyDef { key = "rifle", label = "Rifle Squad", cost = 30, n = 4 },
            new BuyDef { key = "mg", label = "MG Team", cost = 25, n = 1 },
            new BuyDef { key = "at", label = "AT Gun", cost = 40, n = 1 },
            new BuyDef { key = "tank", label = "Tank", cost = 80, n = 1 },
            new BuyDef { key = "cav", label = "Cavalry", cost = 35, n = 3 },
            new BuyDef { key = "sniper", label = "Sniper", cost = 45, n = 1 },
        };

        public static float SprintMul(string kind) => kind == "inf" ? 1.6f : kind == "cav" ? 1.25f : kind == "tank" ? 1.3f : 1.4f;
    }

    // ---------------- helpers ----------------
    public static class FU
    {
        public static float Sm(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }
        public static float AngDiff(float a, float b) { float d = b - a; while (d > Mathf.PI) d -= 2 * Mathf.PI; while (d < -Mathf.PI) d += 2 * Mathf.PI; return d; }
        public static float SegD(float x, float z, float x1, float z1, float x2, float z2)
        {
            float dx = x2 - x1, dz = z2 - z1, l2 = dx * dx + dz * dz; if (l2 == 0) l2 = 1;
            float t = Mathf.Clamp01(((x - x1) * dx + (z - z1) * dz) / l2);
            float px = x1 + dx * t - x, pz = z1 + dz * t - z; return Mathf.Sqrt(px * px + pz * pz);
        }
        public static float Hyp(float a, float b) => Mathf.Sqrt(a * a + b * b);
        public static float Rand(float a, float b) => UnityEngine.Random.Range(a, b);
        public static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
        public static Color Hex(int h) => new Color(((h >> 16) & 255) / 255f, ((h >> 8) & 255) / 255f, (h & 255) / 255f);
    }

    /// mulberry32, same as the JS rng() so map layouts are deterministic per seed.
    public class Rng
    {
        int s;
        public Rng(int seed) { s = seed; }
        public float Next()
        {
            unchecked
            {
                s += 0x6D2B79F5;
                uint us = (uint)s;
                int t = (int)(us ^ (us >> 15)) * (1 | s);
                uint ut = (uint)t;
                t = (t + (int)(ut ^ (ut >> 7)) * (61 | t)) ^ t;
                ut = (uint)t;
                return (ut ^ (ut >> 14)) / 4294967296f;
            }
        }
        public T Pick<T>(T[] a) => a[Mathf.Min(a.Length - 1, (int)(Next() * a.Length))];
    }

    /// Material cache — URP Lit coloured materials, plus an unlit variant for rings/tracers.
    public static class Mats
    {
        static readonly Dictionary<int, Material> lit = new Dictionary<int, Material>();
        static readonly Dictionary<int, Material> unlit = new Dictionary<int, Material>();
        static Shader litShader, unlitShader;
        static int Key(Color c) => ((int)(c.r * 255) << 16) | ((int)(c.g * 255) << 8) | (int)(c.b * 255) | ((int)(c.a * 15) << 24);

        public static Material Lit(Color c, float smooth = .1f)
        {
            int k = Key(c) ^ (smooth > .5f ? 0x40000000 : 0);
            if (lit.TryGetValue(k, out var m) && m) return m;
            if (!litShader) litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(litShader); m.SetColor("_BaseColor", c); m.color = c; m.SetFloat("_Smoothness", smooth);
            lit[k] = m; return m;
        }
        public static Material Lit(int hex) => Lit(FU.Hex(hex));

        public static Material Unlit(Color c)
        {
            int k = Key(c);
            if (unlit.TryGetValue(k, out var m) && m) return m;
            if (!unlitShader) unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            m = new Material(unlitShader); m.SetColor("_BaseColor", c); m.color = c;
            unlit[k] = m; return m;
        }

        static Material vc, burnt;
        /// Shared vertex-colour material (terrain, buildings, all exported models).
        public static Material VC
        {
            get
            {
                if (vc) return vc;
                var res = Resources.Load<Material>("FallWeissTerrain"); // created by the editor helper so builds include the shader
                // build from the shader, never by cloning the saved .mat: a stale keyword state on the asset makes
                // Unity 6.6 crash natively inside Material(Material) while it formats the keyword-mismatch warning
                var sh = res ? res.shader : null; if (!sh) sh = Shader.Find("FallWeiss/VertexColorLit");
                vc = sh ? new Material(sh) { name = "FallWeiss VC (runtime)" } : Lit(FU.Hex(0x6d7f45));
                return vc;
            }
        }
        public static Material VertexColor() => VC;
        /// Charred look for destroyed vehicles (tints the vertex colours).
        public static Material Burnt { get { if (!burnt) { burnt = new Material(VC); burnt.SetColor("_BaseColor", new Color(.16f, .15f, .14f)); } return burnt; } }

        static readonly Dictionary<int, Material> trans = new Dictionary<int, Material>();
        static Shader transShader;
        /// Alpha-blended unlit material (clouds, smoke, rings, beams, rain). `additive` for fire / flashes.
        public static Material Transparent(Color c, Texture tex = null, bool additive = false, bool noFog = false)
        {
            int k = Key(c) ^ (additive ? 0x10000000 : 0) ^ (noFog ? 0x20000000 : 0) ^ (tex ? tex.GetHashCode() * 7919 : 0);
            if (trans.TryGetValue(k, out var m) && m) return m;
            if (!transShader) { transShader = Shader.Find("FallWeiss/Transparent"); if (!transShader) { var r = Resources.Load<Material>("FallWeissTransparent"); if (r) transShader = r.shader; } }
            if (!transShader) return Unlit(c);
            m = new Material(transShader); m.SetColor("_Color", c); if (tex) m.SetTexture("_MainTex", tex);
            m.SetFloat("_DstBlend", additive ? 1 : 10); m.SetFloat("_FogOn", noFog ? 0 : 1); m.renderQueue = 3000; trans[k] = m; return m;
        }
        public static void Clear() { lit.Clear(); unlit.Clear(); }
    }
}
