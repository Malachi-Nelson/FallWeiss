// Fall Weiss — loads Resources/models.bytes: the ORIGINAL procedural models (soldiers.js / models_stable.js /
// optimize.js / buildIronVM) exported headless from three.js, rebuilt here as shared vertex-coloured meshes.
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace FallWeiss
{
    public static class ModelLib
    {
        class Node { public string name; public Vector3 p; public Quaternion q; public Vector3 s; public Mesh mesh; public Node[] kids; }
        static Dictionary<string, Node> models;

        static void Load()
        {
            models = new Dictionary<string, Node>();
            var ta = Resources.Load<TextAsset>("models"); if (!ta) { Debug.LogError("[FallWeiss] Resources/models.bytes missing"); return; }
            using var br = new BinaryReader(new MemoryStream(ta.bytes));
            if (Encoding.ASCII.GetString(br.ReadBytes(4)) != "FWM1") { Debug.LogError("[FallWeiss] bad models.bytes"); return; }
            int n = br.ReadInt32();
            for (int i = 0; i < n; i++) { string name = Str(br); models[name] = ReadNode(br, name); }
        }
        static string Str(BinaryReader br) { int l = br.ReadInt32(); return Encoding.UTF8.GetString(br.ReadBytes(l)); }
        static Node ReadNode(BinaryReader br, string model)
        {
            var nd = new Node { name = Str(br) };
            nd.p = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            nd.q = new Quaternion(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            nd.s = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            int vc = br.ReadInt32();
            if (vc > 0)
            {
                var pos = new Vector3[vc]; var nor = new Vector3[vc]; var col = new Color[vc];
                for (int i = 0; i < vc; i++) pos[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                for (int i = 0; i < vc; i++) nor[i] = new Vector3(br.ReadSByte() / 127f, br.ReadSByte() / 127f, br.ReadSByte() / 127f).normalized;
                for (int i = 0; i < vc; i++) col[i] = new Color(br.ReadByte() / 255f, br.ReadByte() / 255f, br.ReadByte() / 255f).linear;
                int ic = br.ReadInt32(); var idx = new int[ic]; for (int i = 0; i < ic; i++) idx[i] = br.ReadInt32();
                var m = new Mesh { name = model + "/" + nd.name, indexFormat = vc > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                m.vertices = pos; m.normals = Smooth(pos, nor, 50); m.colors = col; m.triangles = idx; m.RecalculateBounds(); nd.mesh = m;
            }
            int kc = br.ReadInt32(); nd.kids = new Node[kc];
            for (int i = 0; i < kc; i++) nd.kids[i] = ReadNode(br, model);
            return nd;
        }

        /// Angle-limited normal smoothing: vertices at the same spot share a normal when their faces are within `deg` of each
        /// other, so curved parts (helmets, wheels, barrels, hulls) shade smoothly while box edges stay crisp.
        static Vector3[] Smooth(Vector3[] pos, Vector3[] nor, float deg)
        {
            float cos = Mathf.Cos(deg * Mathf.Deg2Rad); var outN = new Vector3[nor.Length];
            var groups = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < pos.Length; i++) { var k = new Vector3Int(Mathf.RoundToInt(pos[i].x * 2000), Mathf.RoundToInt(pos[i].y * 2000), Mathf.RoundToInt(pos[i].z * 2000)); if (!groups.TryGetValue(k, out var l)) groups[k] = l = new List<int>(); l.Add(i); }
            foreach (var l in groups.Values)
                foreach (int i in l)
                {
                    var sum = Vector3.zero; foreach (int j in l) if (Vector3.Dot(nor[i], nor[j]) >= cos) sum += nor[j];
                    outN[i] = sum.sqrMagnitude > 1e-8f ? sum.normalized : nor[i];
                }
            return outN;
        }

        public static bool Has(string name) { if (models == null) Load(); return models.ContainsKey(name); }

        /// Instantiates a model hierarchy. Named pivots (hip, upper, leg0, turret, blades...) keep their names.
        public static GameObject Spawn(string name, Transform parent, bool shadows = true, Material mat = null)
        {
            if (models == null) Load();
            if (!models.TryGetValue(name, out var nd)) { Debug.LogWarning("[FallWeiss] no model " + name); return new GameObject(name); }
            var go = Build(nd, parent, shadows, mat ?? Mats.VC); go.name = name; return go;
        }
        static GameObject Build(Node nd, Transform parent, bool shadows, Material mat)
        {
            var go = new GameObject(string.IsNullOrEmpty(nd.name) ? "n" : nd.name);
            go.transform.SetParent(parent, false); go.transform.localPosition = nd.p; go.transform.localRotation = nd.q; go.transform.localScale = nd.s;
            if (nd.mesh)
            {
                go.AddComponent<MeshFilter>().sharedMesh = nd.mesh; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
                mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
            foreach (var k in nd.kids) Build(k, go.transform, shadows, mat);
            return go;
        }

        public static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++) { var r = Find(root.GetChild(i), name); if (r) return r; }
            return null;
        }
    }
}
