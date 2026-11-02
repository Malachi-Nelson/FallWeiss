// Fall Weiss — registry of the POLYGON WW2 soldier models (Assets/WW2_German_soilders), loaded from Resources so
// standalone builds include the prefabs, clips and Animator controller. Built by "Fall Weiss/Rebuild Soldier Library".
using System.Collections.Generic;
using UnityEngine;

namespace FallWeiss
{
    [System.Serializable]
    public class SoldierModel
    {
        public string name;
        public GameObject prefab;
        public AnimationClip[] clips;   // this model's own clips (each rig has its own root bone name)
        public Material bareSkin;       // skin material with the pack's gloves recoloured to bare hands (built by the editor)
        public AnimationClip Clip(string key)
        {
            foreach (var c in clips) if (c && c.name.EndsWith("|" + key, System.StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }
    }

    /// Per-weapon rig data, all in the weapon's main-part bone space (+Z = barrel, +Y = up), taken from the pack's aim pose.
    [System.Serializable]
    public class WeaponRig
    {
        public string key, main;                       // pack weapon key, main part bone name
        public Vector3 rh, lh, head; public Quaternion rhRot, lhRot;
        public string[] parts; public Vector3[] partPos; public Quaternion[] partRot; public Bounds[] partBounds;
        public Bounds body;                            // main part mesh bounds
        public Vector3 sight, sightFront, muzzle, stock, port;   // rear / front sight tops, muzzle, butt, ejection port
        public int Part(string bone) { for (int i = 0; i < parts.Length; i++) if (parts[i] == bone) return i; return -1; }
    }

    public class SoldierLib : ScriptableObject
    {
        public WeaponRig[] rigs;
        public WeaponRig Rig(string key) { if (rigs != null) foreach (var r in rigs) if (r.key == key) return r; return null; }
        public GameObject roundPrefab, caseK98, casePistol, caseMG, rocketPrefab;
        [Tooltip("Master switch: off = every unit keeps the original procedural model.")]
        public bool useModels = true;
        [Tooltip("Per team (0 = Wehrmacht, 1 = Polish Army). The pack only has German uniforms, so Polish units keep the procedural model by default.")]
        public bool[] teams = { true, false };
        public SoldierModel[] models;                  // Wehrmacht A/B/C (+ SS A/B, unused by default)
        public int[] teamModels = { 2, 3, 4 };         // indices into models used for team 0
        public RuntimeAnimatorController controller;  // template: built from Wehrmacht_A's K98 clips

        static SoldierLib inst; static bool tried;
        public static SoldierLib I { get { if (!inst && !tried) { tried = true; inst = Resources.Load<SoldierLib>("SoldierLib"); } return inst; } }
        public static bool forceAll;   // Test Range: POLYGON models for both sides
        public bool Use(int team) => (forceAll && controller && models != null && models.Length > 0) || useModels && controller && models != null && models.Length > 0 && team >= 0 && team < teams.Length && teams[team];

        /// Game weapon id -> pack weapon key (mesh + clip prefix).
        public static string WeaponKey(string gameWeapon, string unitType)
        {
            if (unitType == "at") return "Panzerschreck";
            switch (gameWeapon)
            {
                case "mp38": case "bar28": return "MP40";
                case "mg34": case "ckm30": return "MG42";
                case "p38": case "vis": case "p08": return "Luger";   // the pack only has a Luger: it stands in for the P38 / Vis
                case "panzerschreck": return "Panzerschreck";
                case "panzerfaust": return "Panzerfaust";
                case "stg44": return "STG44";
                default: return unitType == "mg" ? "MG42" : "K98";   // kar98k, wz29, scoped rifles, bayonet
            }
        }
        public static readonly Dictionary<string, string> MESH = new Dictionary<string, string>
        {
            ["K98"] = "K98", ["MP40"] = "MP40", ["MG42"] = "MG42", ["Luger"] = "Luger_P08", ["Panzerschreck"] = "PanzerSchreck",
            ["Panzerfaust"] = "Panzerfaust_launcher", ["STG44"] = "STG44", ["Flammenwerfer"] = "Flammenwerfer",
        };

        // one override controller per (model, weapon), shared by every unit using it
        readonly Dictionary<string, AnimatorOverrideController> overrides = new Dictionary<string, AnimatorOverrideController>();
        public RuntimeAnimatorController Controller(SoldierModel m, string wep)
        {
            string k = m.name + "/" + wep; if (overrides.TryGetValue(k, out var o)) return o;
            o = new AnimatorOverrideController(controller) { name = k };
            var list = new List<KeyValuePair<AnimationClip, AnimationClip>>(); o.GetOverrides(list);
            for (int i = 0; i < list.Count; i++)
            {   // only locomotion lives in the controller now; the upper body is procedural (SoldierVisual)
                var src = list[i].Key; string n = src.name.Substring(src.name.IndexOf('|') + 1);
                var rep = m.Clip(n); list[i] = new KeyValuePair<AnimationClip, AnimationClip>(src, rep ? rep : src);
            }
            o.ApplyOverrides(list); overrides[k] = o; return o;
        }
    }
}
