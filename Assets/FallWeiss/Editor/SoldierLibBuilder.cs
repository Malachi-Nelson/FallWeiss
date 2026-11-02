// Editor-only: builds Resources/SoldierLib.asset and the soldier locomotion controller from the POLYGON WW2 German
// soldier pack, and bakes per-weapon rig data (grips, part layout, sight / muzzle / stock points) from the pack's aim
// poses. Re-run via "Fall Weiss/Rebuild Soldier Library" after changing the pack.
using System.Collections.Generic;
using System.Linq;
using FallWeiss;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace FallWeissEditor
{
    public static class SoldierLibBuilder
    {
        const string Pack = "Assets/WW2_German_soilders/";
        const string Out = "Assets/FallWeiss/Animation/";
        static readonly (string name, string prefab)[] Models =
        {
            ("SS_A", "SchutzStaffel_A/SchutzStaffel_A_Prefab.prefab"), ("SS_B", "Schutzstaffel_b/SchutzStaffel_B_Prefab.prefab"),
            ("Wehrmacht_A", "Wehrmacht_A/Wehrmacht_A_prefab.prefab"), ("Wehrmacht_B", "Wehrmacht_B/wehrmacht_b_prefab.prefab"),
            ("Wehrmacht_C", "Wehrmacht_C/Wehrmacht_C_Prefab.prefab"),
        };
        // weapon key, embedded mesh, main part bone, reference pose clip
        static readonly (string key, string mesh, string main, string clip)[] Weapons =
        {
            ("K98", "K98", "Weapon_handle.002", "K98_aim"), ("MP40", "MP40", "Weapon_handle", "MP40_aim"),
            ("MG42", "MG42", "Weapon_handle", "MG42_aim"), ("Luger", "Luger_P08", "Weapon_handle.003", "Luger_aim"),
            ("Panzerschreck", "PanzerSchreck", "Weapon_handle", "Panzerschreck_idle"), ("STG44", "STG44", "Weapon_handle", "STG44_Aim"),
        };

        [MenuItem("Fall Weiss/Rebuild Soldier Library")]
        public static string Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/FallWeiss/Animation")) AssetDatabase.CreateFolder("Assets/FallWeiss", "Animation");
            var models = new List<SoldierModel>();
            foreach (var (name, rel) in Models)
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(Pack + "Soilders/FBX_soilders/" + rel); if (!pf) { Debug.LogError("[FallWeiss] missing " + rel); continue; }
                var an = pf.GetComponentInChildren<Animator>(); var fbx = AssetDatabase.GetAssetPath(an.avatar);
                var clips = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
                models.Add(new SoldierModel { name = name, prefab = pf, clips = clips, bareSkin = BareHands(name, pf) });
            }
            AssetDatabase.DeleteAsset(Out + "SoldierUpperBody.mask");

            // locomotion-only controller (Wehrmacht_A clips, overridden per model at runtime); the upper body is procedural
            var a = models.First(m => m.name == "Wehrmacht_A");
            AnimationClip C(string k) { var c = a.Clip(k); if (!c) Debug.LogError("[FallWeiss] clip missing: " + k); return c; }
            AssetDatabase.DeleteAsset(Out + "Soldier.controller");
            var ctl = AnimatorController.CreateAnimatorControllerAtPath(Out + "Soldier.controller");
            ctl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var loco = ctl.CreateBlendTreeInController("Locomotion", out var tree, 0); tree.blendParameter = "Speed"; tree.useAutomaticThresholds = false;
            tree.children = new[]
            {
                new ChildMotion { motion = C("Idle"), threshold = 0, timeScale = 1 },
                new ChildMotion { motion = C("Walk"), threshold = 1.4f, timeScale = 1 },
                new ChildMotion { motion = C("Walk"), threshold = 5.5f, timeScale = 2.3f },   // no run clip in the pack: fast walk cycle
            };
            ctl.layers[0].stateMachine.defaultState = loco;
            EditorUtility.SetDirty(ctl);

            var rigs = Weapons.Select(w => BakeRig(a, w.key, w.mesh, w.main, w.clip)).Where(r => r != null).ToArray();

            const string libPath = "Assets/FallWeiss/Resources/SoldierLib.asset";
            var lib = AssetDatabase.LoadAssetAtPath<SoldierLib>(libPath);
            if (!lib) { lib = ScriptableObject.CreateInstance<SoldierLib>(); AssetDatabase.CreateAsset(lib, libPath); }
            lib.models = models.ToArray(); lib.controller = ctl; lib.rigs = rigs;
            lib.teamModels = new[] { models.FindIndex(m => m.name == "Wehrmacht_A"), models.FindIndex(m => m.name == "Wehrmacht_B"), models.FindIndex(m => m.name == "Wehrmacht_C") };
            GameObject P(string p) => AssetDatabase.LoadAssetAtPath<GameObject>(Pack + "Bullet_prefabs/" + p + ".prefab");
            lib.roundPrefab = P("K98_bullet_prefab"); lib.caseK98 = P("K98_bullet_prefab"); lib.casePistol = P("MP40_prefab"); lib.caseMG = P("MG42_prefab"); lib.rocketPrefab = P("PanzerSchreck_Rocket_prefab");
            EditorUtility.SetDirty(lib); AssetDatabase.SaveAssets();
            return $"SoldierLib: {models.Count} models, {rigs.Length} weapon rigs ({string.Join(", ", rigs.Select(r => r.key))}), controller {AssetDatabase.GetAssetPath(ctl)}";
        }

        static readonly HashSet<string> HandBones = new HashSet<string> { "Bone.008", "Bone.009", "Bone.010", "Bone.011", "Bone.012", "Bone.013" };
        /// The pack's soldiers wear gloves (grey / black) that read as grey blocks in first person: copy the skin texture
        /// and repaint every UV triangle skinned to the hands in the model's own face tone, keeping the texture's shading.
        static Material BareHands(string name, GameObject pf)
        {
            var skin = pf.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.sharedMaterial && r.sharedMaterial.name.ToLower().Contains("skin")); if (!skin) return null;
            var src = skin.sharedMaterial; var tex = src.GetTexture("_BaseMap") as Texture2D; if (!tex) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false); t.LoadImage(System.IO.File.ReadAllBytes(AssetDatabase.GetAssetPath(tex)));
            var mesh = skin.sharedMesh; var bw = mesh.boneWeights; var uv = mesh.uv; var tris = mesh.triangles; int W = t.width, H = t.height;
            Color face = Color.clear; int nf = 0; float handLum = 0; int nh = 0;
            for (int i = 0; i < bw.Length; i++)
            {
                var b = skin.bones[bw[i].boneIndex0].name; var c = t.GetPixelBilinear(uv[i].x, uv[i].y);
                if (b == "Bone.025") { face += c; nf++; } else if (HandBones.Contains(b)) { handLum += c.grayscale; nh++; }
            }
            if (nf == 0 || nh == 0) return null; face /= nf; handLum = Mathf.Max(.05f, handLum / nh);
            var px = t.GetPixels(); var done = new bool[px.Length];
            for (int k = 0; k < tris.Length; k += 3)
            {
                int a = tris[k], b = tris[k + 1], c = tris[k + 2];
                if (!HandBones.Contains(skin.bones[bw[a].boneIndex0].name) || !HandBones.Contains(skin.bones[bw[b].boneIndex0].name) || !HandBones.Contains(skin.bones[bw[c].boneIndex0].name)) continue;
                Vector2 A = new Vector2(uv[a].x * W, uv[a].y * H), B = new Vector2(uv[b].x * W, uv[b].y * H), C = new Vector2(uv[c].x * W, uv[c].y * H);
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.x, Mathf.Min(B.x, C.x))) - 1), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(Mathf.Max(A.x, Mathf.Max(B.x, C.x))) + 1);
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.y, Mathf.Min(B.y, C.y))) - 1), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(Mathf.Max(A.y, Mathf.Max(B.y, C.y))) + 1);
                float area = (B.x - A.x) * (C.y - A.y) - (C.x - A.x) * (B.y - A.y); if (Mathf.Abs(area) < 1e-6f) { var i0 = Mathf.Clamp((int)A.y, 0, H - 1) * W + Mathf.Clamp((int)A.x, 0, W - 1); if (!done[i0]) { done[i0] = true; px[i0] = face; } continue; }
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                {   // barycentric test with a small margin so island edges do not keep a glove-coloured seam
                    float px0 = x + .5f, py0 = y + .5f;
                    float w0 = ((B.x - px0) * (C.y - py0) - (C.x - px0) * (B.y - py0)) / area, w1 = ((C.x - px0) * (A.y - py0) - (A.x - px0) * (C.y - py0)) / area, w2 = 1 - w0 - w1;
                    if (w0 < -.08f || w1 < -.08f || w2 < -.08f) continue;
                    int i = y * W + x; if (done[i]) continue; done[i] = true;
                    float shade = Mathf.Clamp(px[i].grayscale / handLum, .7f, 1.2f); var o = face * shade; o.a = 1; px[i] = o;
                }
            }
            t.SetPixels(px); t.Apply();
            const string dir = "Assets/FallWeiss/Animation/BareHands"; if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/FallWeiss/Animation", "BareHands");
            string png = dir + "/" + name + "_skin.png"; System.IO.File.WriteAllBytes(png, t.EncodeToPNG()); AssetDatabase.ImportAsset(png);
            var ti = (TextureImporter)AssetImporter.GetAtPath(png); var srcImp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex)) as TextureImporter;
            if (ti && srcImp) { ti.filterMode = srcImp.filterMode; ti.mipmapEnabled = srcImp.mipmapEnabled; ti.SaveAndReimport(); }
            string matPath = dir + "/" + name + "_skin.mat"; AssetDatabase.DeleteAsset(matPath);
            var m = new Material(src) { name = name + "_skin_bare" }; m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(png)); AssetDatabase.CreateAsset(m, matPath);
            return m;
        }

        static Transform Find(GameObject g, string n) { foreach (var t in g.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t; return null; }

        static WeaponRig BakeRig(SoldierModel m, string key, string mesh, string main, string clipKey)
        {
            var clip = m.Clip(clipKey); if (!clip) { Debug.LogError("[FallWeiss] no pose clip " + clipKey); return null; }
            var g = Object.Instantiate(m.prefab); g.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            try
            {
                var an = g.GetComponentInChildren<Animator>(); clip.SampleAnimation(an.gameObject, 0);
                var mb = Find(g, main); var smr = g.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(s => s.name == mesh);
                var inv = Quaternion.Inverse(mb.rotation);
                Vector3 L(Vector3 w) => inv * (w - mb.position); Quaternion LR(Quaternion q) => inv * q;   // metres (the rig is scaled x29)
                var rig = new WeaponRig { key = key, main = main };
                var rh = Find(g, "Bone.011"); var lh = Find(g, "Bone.010");
                rig.rh = L(rh.position); rig.rhRot = LR(rh.rotation); rig.lh = L(lh.position); rig.lhRot = LR(lh.rotation); rig.head = L(Find(g, "Bone.025").position);

                // parts = handle bones this mesh is skinned to, with their assembled pose relative to the main part
                var baked = new Mesh(); smr.BakeMesh(baked, true); var v = baked.vertices; var bw = smr.sharedMesh.boneWeights;
                var used = new List<string>(); var bounds = new Dictionary<string, Bounds>(); var mainVerts = new List<Vector3>(); var allVerts = new List<Vector3>();
                for (int i = 0; i < v.Length; i++)
                {
                    var bn = smr.bones[bw[i].boneIndex0].name; var lp = L(smr.transform.TransformPoint(v[i]));
                    if (!bounds.ContainsKey(bn)) { bounds[bn] = new Bounds(lp, Vector3.zero); used.Add(bn); } else { var b = bounds[bn]; b.Encapsulate(lp); bounds[bn] = b; }
                    if (bn == main) mainVerts.Add(lp); allVerts.Add(lp);
                }
                rig.parts = used.ToArray(); rig.partPos = new Vector3[used.Count]; rig.partRot = new Quaternion[used.Count]; rig.partBounds = new Bounds[used.Count];
                for (int i = 0; i < used.Count; i++) { var t = Find(g, used[i]); rig.partPos[i] = L(t.position); rig.partRot[i] = LR(t.rotation); rig.partBounds[i] = bounds[used[i]]; }
                rig.body = bounds[main];

                // reference points from the main part's vertices
                var bd = rig.body; float zr = rig.rh.z; bool pistol = key == "Luger";
                // sight line: highest centre-line point behind the grip area (rear sight) and near the muzzle (front sight)
                var rear = allVerts.Where(p => Mathf.Abs(p.x - bd.center.x) < .025f && p.z > zr - (pistol ? .06f : 0) && p.z < zr + (pistol ? .08f : .45f)).ToList(); if (rear.Count == 0) rear = allVerts;
                var rTop = rear.OrderByDescending(p => p.y).First(); rig.sight = new Vector3(bd.center.x, rTop.y, rTop.z);
                var fr = allVerts.Where(p => Mathf.Abs(p.x - bd.center.x) < .025f && p.z > bd.max.z - (pistol ? .04f : .10f)).ToList(); if (fr.Count == 0) fr = allVerts;
                var fTop = fr.OrderByDescending(p => p.y).First(); rig.sightFront = new Vector3(bd.center.x, fTop.y, fTop.z);
                var front = mainVerts.Where(p => p.z > bd.max.z - .04f).ToList(); rig.muzzle = new Vector3(front.Average(p => p.x), front.Average(p => p.y), bd.max.z);
                var back = mainVerts.Where(p => p.z < bd.min.z + .04f).ToList(); rig.stock = new Vector3(back.Average(p => p.x), back.Average(p => p.y), bd.min.z);
                rig.port = new Vector3(bd.center.x, rig.sight.y, zr + .16f);
                return rig;
            }
            finally { Object.DestroyImmediate(g); }
        }
    }
}
