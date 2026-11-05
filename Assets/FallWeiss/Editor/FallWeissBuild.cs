// Editor-only: builds the Windows standalone game.
// Menu: Fall Weiss > Build Windows .exe   — or drop Logs/fallweiss_build.request and the editor builds on next load.
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FallWeissEditor
{
    [InitializeOnLoad]
    public static class FallWeissBuild
    {
        static string Root => Directory.GetCurrentDirectory();
        static double nextPoll;
        /// Polls Logs/ for request files so the project can be driven while the editor is in the background:
        /// fallweiss_refresh.request (reimport + recompile), fallweiss_play.request / fallweiss_stop.request, fallweiss_build.request.
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return; nextPoll = EditorApplication.timeSinceStartup + 1.5;
            string R(string n) => Path.Combine(Root, "Logs", n);
            bool Take(string n) { var f = R(n); if (!File.Exists(f)) return false; try { File.Delete(f); } catch { } return true; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (Take("fallweiss_refresh.request")) { AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate); UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation(); }
            if (Take("fallweiss_play.request") && !EditorApplication.isPlaying) EditorApplication.EnterPlaymode();
            if (Take("fallweiss_stop.request") && EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            if (!EditorApplication.isPlaying && Take("fallweiss_build.request")) Build();   // direct call: delayCall never fires while the editor sits unfocused in the background
            if (Take("fallweiss_run.request")) Run();
            if (!EditorApplication.isPlaying && Take("fallweiss_pack.request")) FallWeissPack.Pack();
            if (!EditorApplication.isPlaying && Take("fallweiss_fortify.request")) { string r; try { r = ObjectiveFortifier.Run(false); } catch (System.Exception e) { r = "EXCEPTION: " + e; } File.WriteAllText(R("fallweiss_fortify.txt"), System.DateTime.Now + "\n" + r + "\n"); }
        }
        static FallWeissBuild()
        {
            EditorApplication.update += Poll;
            // Poll alone consumes every request: a second, deferred consumer of the build request let a pack request zip the previous build first
        }

        [MenuItem("Fall Weiss/Run built game (windowed)")]
        public static void Run()
        {
            var exe = Path.Combine(Root, "Build", "FallWeiss", "FallWeiss.exe");
            if (File.Exists(exe)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "-screen-fullscreen 0 -screen-width 1280 -screen-height 720") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
        }

        [MenuItem("Fall Weiss/Build Windows .exe")]
        public static void Build()
        {
            var log = Path.Combine(Root, "Logs", "fallweiss_build.txt");
            File.WriteAllText(log, "build started " + System.DateTime.Now + "\n");
            try
            {
                FallWeissSetup.Ensure(); AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
                PlayerSettings.productName = "Fall Weiss - Poland 1939";
                PlayerSettings.companyName = "Malachi Nelson";
                PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
                PlayerSettings.defaultIsNativeResolution = true;
                PlayerSettings.runInBackground = true;
                var scenes = EditorBuildSettings.scenes.Where(s => s.enabled && File.Exists(s.path)).Select(s => s.path).ToArray();
                if (scenes.Length == 0) scenes = new[] { "Assets/Scenes/SampleScene.unity" };
                var outDir = Path.Combine(Root, "Build", "FallWeiss");
                Directory.CreateDirectory(outDir);
                var opts = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = Path.Combine(outDir, "FallWeiss.exe"),
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None
                };
                var report = BuildPipeline.BuildPlayer(opts);
                var s = report.summary;
                File.AppendAllText(log, $"result: {s.result}\nscenes: {string.Join(", ", scenes)}\noutput: {s.outputPath}\nsize: {s.totalSize / (1024 * 1024)} MB\ntime: {s.totalTime}\nerrors: {s.totalErrors} warnings: {s.totalWarnings}\n");
                foreach (var step in report.steps) foreach (var m in step.messages) if (m.type == LogType.Error || m.type == LogType.Exception) File.AppendAllText(log, "ERROR: " + m.content + "\n");
                File.AppendAllText(log, "build finished " + System.DateTime.Now + "\n");
            }
            catch (System.Exception e) { File.AppendAllText(log, "EXCEPTION: " + e + "\n"); }
        }
    }
}
