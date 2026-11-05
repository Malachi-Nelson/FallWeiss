// Fall Weiss — the POLYGON pack's own guns (Kar98k, MP40, Luger P08) on the v4 soldiers and viewmodels.
// Each gun is baked once from the pack soldier prefab: the weapon handle bones are posed with the pack's aim-pose rig data
// (SoldierLib.WeaponRig, metres in the main-part frame: +Z barrel, +Y up), the skinned mesh is baked, and every triangle
// goes to the part (bone) that drives it, so the bolt, magazine and toggle can be animated on their own.
using System.Collections.Generic;
using UnityEngine;

namespace FallWeiss
{
    public static class PackGuns
    {
        public class Part { public string bone; public Mesh mesh; public Vector3 pivot; public Bounds b; }
        public class Gun { public string key; public WeaponRig rig; public Material mat; public readonly List<Part> parts = new List<Part>(); public int iMain = -1; }
        static readonly Dictionary<string, Gun> cache = new Dictionary<string, Gun>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() => cache.Clear();   // see ModelLib.ResetStatics

        /// Game weapon id -> pack gun, only where the pack has that exact weapon (MP 40 stands in for the near-identical MP 38).
        /// MG 34 (the pack only has the 1942 MG 42), the scoped rifle, the Polish guns and the bayonet / sabre stay v4.
        public static string KeyFor(string weaponId) => weaponId == "kar98k" ? "K98" : weaponId == "mp38" ? "MP40" : weaponId == "p08" ? "Luger" : null;

        public static Gun Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null; if (cache.TryGetValue(key, out var have) && (have == null || have.parts.Count == 0 || have.parts[0].mesh)) return have; cache[key] = null;   // a cached gun whose meshes were destroyed is rebuilt
            var lib = SoldierLib.I; if (lib == null || lib.models == null || lib.models.Length == 0) return null;
            var rig = lib.Rig(key); if (rig == null || rig.parts == null || !SoldierLib.MESH.TryGetValue(key, out var meshName)) return null;
            int mi = lib.teamModels != null && lib.teamModels.Length > 0 ? lib.teamModels[0] : 0; if (mi < 0 || mi >= lib.models.Length || !lib.models[mi].prefab) return null;
            var tmp = Object.Instantiate(lib.models[mi].prefab); tmp.name = "PackGunBake"; tmp.transform.SetPositionAndRotation(new Vector3(0, -5000, 0), Quaternion.identity); tmp.transform.localScale = Vector3.one;   // out of sight for the one frame it lives (the scale reset used to sit inside this comment)
            try
            {
                SkinnedMeshRenderer smr = null; var bones = new Dictionary<string, Transform>();
                foreach (var r in tmp.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (r.name == meshName) smr = r;
                foreach (var t in tmp.GetComponentsInChildren<Transform>(true)) bones[t.name] = t;
                if (!smr || !smr.sharedMesh || !bones.TryGetValue(rig.main, out var main)) return null;
                foreach (var a in tmp.GetComponentsInChildren<Animator>(true)) a.enabled = false;
                // gun frame = the main part; pose every part where the pack's aim pose puts it (as SoldierVisual does)
                var P = new GameObject("PackGunFrame").transform; P.SetPositionAndRotation(main.position, main.rotation);
                var keep = new Dictionary<Transform, Vector3>();
                for (int i = 0; i < rig.parts.Length; i++) if (bones.TryGetValue(rig.parts[i], out var bt)) keep[bt] = bt.lossyScale;
                foreach (var kv in keep) kv.Key.SetParent(P, true);
                for (int i = 0; i < rig.parts.Length; i++)
                    if (bones.TryGetValue(rig.parts[i], out var bt)) { bt.localPosition = rig.partPos[i]; bt.localRotation = rig.partRot[i]; bt.localScale = keep[bt]; }
                var baked = new Mesh(); smr.BakeMesh(baked);   // legacy bake: vertices sit at the renderer's position / rotation with no scale applied
                var sv = baked.vertices; var sn = baked.normals; var uv = smr.sharedMesh.uv; var tri = smr.sharedMesh.triangles; var bw = smr.sharedMesh.boneWeights; var sb = smr.bones;
                var st = smr.transform; var W = new Vector3[sv.Length]; var N = new Vector3[sv.Length];
                for (int i = 0; i < sv.Length; i++)
                {   // rotate + translate into world, then into the gun frame (P has unit scale)
                    W[i] = P.InverseTransformPoint(st.position + st.rotation * sv[i]);
                    N[i] = P.InverseTransformDirection(st.rotation * (i < sn.Length ? sn[i] : Vector3.up));
                }
                // triangle -> part: the bone with the largest weight on its first vertex (handle bones only; everything else is the body)
                var partOf = new Dictionary<string, int>(); var g = new Gun { key = key, rig = rig, mat = smr.sharedMaterial };
                System.Func<string, int> PartIndex = bone =>
                {
                    if (System.Array.IndexOf(rig.parts, bone) < 0) bone = rig.main;
                    int k; if (!partOf.TryGetValue(bone, out k)) { k = g.parts.Count; partOf[bone] = k; g.parts.Add(new Part { bone = bone }); if (bone == rig.main) g.iMain = k; }
                    return k;
                };
                var lists = new List<List<int>>();
                for (int t = 0; t < tri.Length; t += 3)
                {
                    int v0 = tri[t]; string bone = v0 < bw.Length && bw[v0].boneIndex0 < sb.Length && sb[bw[v0].boneIndex0] ? sb[bw[v0].boneIndex0].name : rig.main;
                    int k = PartIndex(bone); while (lists.Count <= k) lists.Add(new List<int>()); lists[k].Add(tri[t]); lists[k].Add(tri[t + 1]); lists[k].Add(tri[t + 2]);
                }
                for (int k = 0; k < g.parts.Count; k++)
                {   // compact each part into its own mesh, vertices relative to the part's pivot (bounds centre)
                    var map = new Dictionary<int, int>(); var pv = new List<Vector3>(); var pn = new List<Vector3>(); var pu = new List<Vector2>(); var pt = new List<int>();
                    foreach (int vi in lists[k]) { if (!map.TryGetValue(vi, out var ni)) { ni = pv.Count; map[vi] = ni; pv.Add(W[vi]); pn.Add(N[vi]); pu.Add(vi < uv.Length ? uv[vi] : Vector2.zero); } pt.Add(ni); }
                    var b = new Bounds(pv.Count > 0 ? pv[0] : Vector3.zero, Vector3.zero); foreach (var p in pv) b.Encapsulate(p);
                    var part = g.parts[k]; part.b = b; part.pivot = b.center;
                    for (int i = 0; i < pv.Count; i++) pv[i] -= part.pivot;
                    var m = new Mesh { name = key + "/" + part.bone }; if (pv.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    m.SetVertices(pv); m.SetNormals(pn); m.SetUVs(0, pu); m.SetTriangles(pt, 0); m.RecalculateBounds(); part.mesh = m;
                }
                Object.Destroy(P.gameObject); Object.Destroy(baked);
                cache[key] = g; return g;
            }
            catch (System.Exception e) { Debug.LogWarning("[FallWeiss] pack gun " + key + ": " + e.Message); return null; }
            finally { Object.Destroy(tmp); }
        }

        /// The gun under `parent`, root in the gun frame. Returns the root; parts are named after their bones.
        public static Transform Spawn(Gun g, Transform parent, bool shadows)
        {
            var root = new GameObject("Pack_" + g.key).transform; root.SetParent(parent, false); root.gameObject.layer = parent.gameObject.layer;
            foreach (var p in g.parts)
            {
                var go = new GameObject(p.bone); go.layer = root.gameObject.layer; go.transform.SetParent(root, false); go.transform.localPosition = p.pivot;
                go.AddComponent<MeshFilter>().sharedMesh = p.mesh; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = g.mat;
                mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return root;
        }

        /// Hides a v4 mesh node without disabling its renderer (Unit.SetRenderers re-enables renderers): drop the mesh.
        public static void Blank(Transform t) { if (!t) return; var mf = t.GetComponent<MeshFilter>(); if (mf) mf.sharedMesh = null; }

        /// Third person: swap the v4 soldier's modelled gun for the pack gun, muzzle on the v4 muzzle, barrel along the v4 barrel.
        public static bool MountThirdPerson(Transform weaponT, Transform muzzleT, Transform breechT, string weaponId)
        {
            var g = Get(KeyFor(weaponId)); if (g == null || !weaponT || !muzzleT || !breechT) return false;
            Vector3 mz = weaponT.InverseTransformPoint(muzzleT.position), br = weaponT.InverseTransformPoint(breechT.position);
            var rig = g.rig; var rigDir = (rig.sightFront - rig.sight).normalized; var myDir = (mz - br).normalized;
            var R = Quaternion.LookRotation(myDir, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(rigDir, Vector3.up));
            for (int i = 0; i < weaponT.childCount; i++) { var c = weaponT.GetChild(i); if (c.GetComponent<MeshFilter>()) Blank(c); }
            var root = Spawn(g, weaponT, true); root.localRotation = R; root.localPosition = mz - R * rig.muzzle; return true;
        }
    }
}
