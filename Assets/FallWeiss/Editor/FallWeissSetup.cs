// Editor-only: makes sure Resources/ holds one material per shader the game creates at runtime,
// so standalone builds include them (Shader.Find only finds shaders that are referenced by an included asset).
using UnityEditor;
using UnityEngine;

namespace FallWeissEditor
{
    [InitializeOnLoad]
    public static class FallWeissSetup
    {
        static FallWeissSetup() { EditorApplication.delayCall += Ensure; }
        const int MatVersion = 2;   // bump to force the Resources materials to be rebuilt from their shaders
        public static void Ensure()
        {
            if (EditorPrefs.GetInt("FallWeiss.MatVersion", 0) < MatVersion)
            {   // the saved materials picked up a keyword state that no longer matches the shaders (crashes Material(Material))
                foreach (var n in new[] { "Terrain", "Sky", "Transparent", "Lit", "Unlit" }) AssetDatabase.DeleteAsset("Assets/FallWeiss/Resources/FallWeiss" + n + ".mat");
                EditorPrefs.SetInt("FallWeiss.MatVersion", MatVersion);
            }
            Make("Assets/FallWeiss/Resources/FallWeissTerrain.mat", "FallWeiss/VertexColorLit");
            Make("Assets/FallWeiss/Resources/FallWeissSky.mat", "FallWeiss/Sky");
            Make("Assets/FallWeiss/Resources/FallWeissTransparent.mat", "FallWeiss/Transparent");
            Make("Assets/FallWeiss/Resources/FallWeissLit.mat", "Universal Render Pipeline/Lit");
            Make("Assets/FallWeiss/Resources/FallWeissUnlit.mat", "Universal Render Pipeline/Unlit");
        }
        static void Make(string path, string shader)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
            var sh = Shader.Find(shader); if (sh == null) { Debug.LogWarning("[FallWeiss] shader not found: " + shader); return; }
            var m = new Material(sh); if (shader.Contains("Lit") && !shader.Contains("Vertex")) m.enableInstancing = true;
            AssetDatabase.CreateAsset(m, path); AssetDatabase.SaveAssets();
            Debug.Log("[FallWeiss] Created " + path);
        }
    }
}
