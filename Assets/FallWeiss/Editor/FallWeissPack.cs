// Editor-only: packs the built game into one shippable archive and checks it.
// Menu: Fall Weiss > Pack built game (zip)   — or Fall Weiss > Build + Pack.
// Why this exists: the single-file repacker that was used before extracts to %LOCALAPPDATA%\fallweiss_game\<hash>\ and
// lower-cases names; nothing in the project can see what it drops. This packer is boring on purpose: a plain zip of the
// whole Build/FallWeiss folder, a manifest of every file with its size, and a read-back check that the archive holds
// exactly those files. Unpack anywhere and run FallWeiss.exe; the FallWeiss_Data folder must stay next to it.
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FallWeissEditor
{
    public static class FallWeissPack
    {
        static string Root => Directory.GetCurrentDirectory();
        static string BuildDir => Path.Combine(Root, "Build", "FallWeiss");
        static string Log => Path.Combine(Root, "Logs", "fallweiss_pack.txt");

        [MenuItem("Fall Weiss/Build + Pack (zip)")]
        public static void BuildAndPack() { FallWeissBuild.Build(); Pack(); }

        [MenuItem("Fall Weiss/Pack built game (zip)")]
        public static void Pack()
        {
            File.WriteAllText(Log, "pack started " + System.DateTime.Now + "\n");
            try
            {
                var exe = Path.Combine(BuildDir, "FallWeiss.exe"); var data = Path.Combine(BuildDir, "FallWeiss_Data");
                if (!File.Exists(exe) || !Directory.Exists(data)) { Fail("no build at " + BuildDir + " — run Fall Weiss > Build Windows .exe first"); return; }
                // what must be there for the player to run and for every asset to load
                string[] must = { "FallWeiss.exe", "UnityPlayer.dll", "FallWeiss_Data/globalgamemanagers", "FallWeiss_Data/resources.assets", "FallWeiss_Data/resources.resource", "FallWeiss_Data/sharedassets0.assets", "FallWeiss_Data/level0", "FallWeiss_Data/Managed/FallWeiss.dll", "FallWeiss_Data/Resources/unity default resources", "FallWeiss_Data/Resources/unity_builtin_extra", "MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll" };
                var missing = must.Where(m => !File.Exists(Path.Combine(BuildDir, m.Replace('/', Path.DirectorySeparatorChar)))).ToList();
                if (missing.Count > 0) { Fail("build is incomplete, missing: " + string.Join(", ", missing)); return; }
                // everything except the debug-symbol folder Unity tells you not to ship
                var files = Directory.GetFiles(BuildDir, "*", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("_BackUpThisFolder_ButDontShipItWithYourGame") && !f.EndsWith(".pdb"))
                    .Select(f => f.Substring(BuildDir.Length + 1).Replace('\\', '/')).OrderBy(f => f).ToList();
                var stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmm"); var zipPath = Path.Combine(Root, "Build", "FallWeiss_win64_" + stamp + ".zip"); var tmp = zipPath + ".tmp";
                if (File.Exists(tmp)) File.Delete(tmp);
                long total = 0;
                using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
                {
                    foreach (var rel in files)
                    {
                        var full = Path.Combine(BuildDir, rel.Replace('/', Path.DirectorySeparatorChar)); total += new FileInfo(full).Length;
                        // the player exe and the big binary blobs do not compress usefully; store them so unpacking is fast
                        var lvl = rel.EndsWith(".resS") || rel.EndsWith(".resource") || rel.EndsWith(".dll") && rel.Contains("UnityPlayer") ? System.IO.Compression.CompressionLevel.NoCompression : System.IO.Compression.CompressionLevel.Optimal;
                        zip.CreateEntryFromFile(full, "FallWeiss/" + rel, lvl);   // one top-level folder, original names and case
                    }
                    var man = zip.CreateEntry("FallWeiss/MANIFEST.txt"); using (var w = new StreamWriter(man.Open())) { w.WriteLine("Fall Weiss build " + stamp); w.WriteLine("Unity " + Application.unityVersion); foreach (var rel in files) w.WriteLine(new FileInfo(Path.Combine(BuildDir, rel.Replace('/', Path.DirectorySeparatorChar))).Length.ToString().PadLeft(12) + "  " + rel); }
                }
                // read it back: every file, same size
                int bad = 0;
                using (var zip = ZipFile.OpenRead(tmp))
                {
                    var byName = zip.Entries.ToDictionary(e => e.FullName, e => e.Length);
                    foreach (var rel in files) { long want = new FileInfo(Path.Combine(BuildDir, rel.Replace('/', Path.DirectorySeparatorChar))).Length; if (!byName.TryGetValue("FallWeiss/" + rel, out var got) || got != want) { bad++; File.AppendAllText(Log, "MISMATCH " + rel + "\n"); } }
                }
                if (bad > 0) { File.Delete(tmp); Fail(bad + " files did not survive packing"); return; }
                if (File.Exists(zipPath)) File.Delete(zipPath); File.Move(tmp, zipPath);
                File.AppendAllText(Log, $"files: {files.Count}\nunpacked: {total / (1024 * 1024)} MB\narchive: {new FileInfo(zipPath).Length / (1024 * 1024)} MB\noutput: {zipPath}\nverified: all {files.Count} files present with matching sizes\npack finished {System.DateTime.Now}\n");
                Debug.Log("[FallWeiss] packed " + files.Count + " files -> " + zipPath);
                EditorUtility.RevealInFinder(zipPath);
            }
            catch (System.Exception e) { Fail("EXCEPTION: " + e); }
        }
        static void Fail(string why) { File.AppendAllText(Log, "FAILED: " + why + "\n"); Debug.LogError("[FallWeiss] pack failed: " + why); }
    }
}
