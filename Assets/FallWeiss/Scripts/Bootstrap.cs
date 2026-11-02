// Fall Weiss — auto-start: pressing Play in ANY scene spawns the game controller (no scene setup needed).
using UnityEngine;

namespace FallWeiss
{
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindAnyObjectByType<Game>() != null) return;
            var go = new GameObject("Fall Weiss — Game");
            go.AddComponent<Game>();
            go.AddComponent<RuntimeLog>();
            go.AddComponent<TestDriver>();
        }
    }

    /// Mirrors runtime errors to Logs/fallweiss_runtime.txt so problems can be read without the Console.
    public class RuntimeLog : MonoBehaviour
    {
        string path;
        void Awake()
        {
            var dir = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "Logs"); try { System.IO.Directory.CreateDirectory(dir); } catch { }
            path = System.IO.Path.Combine(dir, "fallweiss_runtime.txt");
            try { System.IO.File.WriteAllText(path, "Fall Weiss runtime log " + System.DateTime.Now + "\n"); } catch { }
            Application.logMessageReceived += OnLog;
        }
        void OnDestroy() { Application.logMessageReceived -= OnLog; }
        void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Log) return;
            try { System.IO.File.AppendAllText(path, $"[{type}] {msg}\n{stack}\n"); } catch { }
        }
    }
}
