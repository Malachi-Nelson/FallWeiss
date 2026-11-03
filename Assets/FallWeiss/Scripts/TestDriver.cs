// Fall Weiss — scripted input driver for automated play-testing.
// Drop a text file at Logs/fallweiss_input.txt (next to Assets/ in the editor, next to the .exe in a build) and it is
// executed line by line, injecting real Input System events, so every key binding is exercised exactly as a player would.
// Commands:  wait s | tap KEY | hold KEY s | down KEY | up KEY | look dx dy s | lmb s | rmb s | click x y [right]
//            mission idx side | range | shot name | log text | timescale v | lmbdown | lmbup | rmbdown | rmbup
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace FallWeiss
{
    public class TestDriver : MonoBehaviour
    {
        string dir, inPath, logPath; float poll; bool busy;
        readonly HashSet<Key> down = new HashSet<Key>();
        Vector2 pos; bool lmb, rmb;

        void Awake()
        {
            dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs");
            inPath = Path.Combine(dir, "fallweiss_input.txt"); logPath = Path.Combine(dir, "fallweiss_test.txt");
        }

        void Update()
        {
            if (busy) return; poll -= Time.unscaledDeltaTime; if (poll > 0) return; poll = .5f;
            if (!File.Exists(inPath)) return;
            string[] lines; try { lines = File.ReadAllLines(inPath); File.Delete(inPath); } catch { return; }
            StartCoroutine(Run(lines));
        }

        void Log(string s) { try { File.AppendAllText(logPath, $"[{Time.unscaledTime:0.0}] {s}\n"); } catch { } }

        void SendKeys()
        {
            if (Keyboard.current == null) return;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(new List<Key>(down).ToArray()));
        }
        void SendMouse(Vector2 delta)
        {
            if (Mouse.current == null) return;
            var st = new MouseState { position = pos, delta = delta }.WithButton(MouseButton.Left, lmb).WithButton(MouseButton.Right, rmb);
            InputSystem.QueueStateEvent(Mouse.current, st);
        }
        static bool ParseKey(string s, out Key k) { if (s.Length == 1 && char.IsDigit(s[0])) s = "Digit" + s; return System.Enum.TryParse(s, true, out k) && !int.TryParse(s, out _); }

        IEnumerator Run(string[] lines)
        {
            busy = true;
#if UNITY_EDITOR
            var set = InputSystem.settings; var bb = set.backgroundBehavior; var eb = set.editorInputBehaviorInPlayMode;
            set.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            set.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            try { File.WriteAllText(logPath, $"test run {System.DateTime.Now}\n"); } catch { }
            pos = new Vector2(Screen.width / 2f, Screen.height / 2f);
            var game = FindAnyObjectByType<Game>();
            foreach (var raw in lines)
            {
                var ln = raw.Trim(); if (ln.Length == 0 || ln.StartsWith("#")) continue;
                var a = ln.Split(' '); var cmd = a[0].ToLowerInvariant(); Key k;
                float F(int i, float d = 0) => a.Length > i && float.TryParse(a[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : d;
                switch (cmd)
                {
                    case "wait": { float e = Time.realtimeSinceStartup + F(1, 1); while (Time.realtimeSinceStartup < e) yield return null; break; }
                    case "tap":
                        if (ParseKey(a[1], out k)) { down.Add(k); SendKeys(); yield return null; yield return null; down.Remove(k); SendKeys(); yield return null; yield return null; }
                        else Log("bad key " + a[1]); break;
                    case "down": if (ParseKey(a[1], out k)) { down.Add(k); SendKeys(); yield return null; } break;
                    case "up": if (ParseKey(a[1], out k)) { down.Remove(k); SendKeys(); yield return null; } break;
                    case "hold":
                        if (ParseKey(a[1], out k)) { down.Add(k); SendKeys(); float e = Time.realtimeSinceStartup + F(2, 1); while (Time.realtimeSinceStartup < e) yield return null; down.Remove(k); SendKeys(); yield return null; }
                        break;
                    case "look":
                        {
                            float dx = F(1), dy = F(2), s = Mathf.Max(.05f, F(3, .5f)), st = Time.realtimeSinceStartup, e = st + s;
                            while (Time.realtimeSinceStartup < e) { float f = Time.unscaledDeltaTime / s; SendMouse(new Vector2(dx * f, dy * f)); yield return null; }
                            SendMouse(Vector2.zero); yield return null; break;
                        }
                    case "lmb": case "rmb":
                        {
                            if (cmd == "lmb") lmb = true; else rmb = true; SendMouse(Vector2.zero);
                            float e = Time.realtimeSinceStartup + F(1, .1f); while (Time.realtimeSinceStartup < e) yield return null;
                            lmb = rmb = false; SendMouse(Vector2.zero); yield return null; break;
                        }
                    case "lmbdown": lmb = true; SendMouse(Vector2.zero); yield return null; break;   // hold a button across other commands (ADS + shot)
                    case "lmbup": lmb = false; SendMouse(Vector2.zero); yield return null; break;
                    case "rmbdown": rmb = true; SendMouse(Vector2.zero); yield return null; break;
                    case "rmbup": rmb = false; SendMouse(Vector2.zero); yield return null; break;
                    case "click":
                        {   // x/y in 1920x1080 HUD space, origin top-left
                            float sx = Screen.width / 1920f, sy = Screen.height / 1080f; pos = new Vector2(F(1) * sx, Screen.height - F(2) * sy);
                            bool right = a.Length > 3 && a[3] == "right"; SendMouse(Vector2.zero); yield return null;
                            if (right) rmb = true; else lmb = true; SendMouse(Vector2.zero); yield return null; yield return null;
                            lmb = rmb = false; SendMouse(Vector2.zero); yield return null; break;
                        }
                    case "mission":
                        if (game) { game.StartMission((int)F(1), (int)F(2)); } yield return null; yield return null; break;
                    case "range": if (game) { game.StartRange(); } yield return null; yield return null; break;
                    case "timescale": Time.timeScale = F(1, 1); break;
                    case "shot":
                        {
                            yield return new WaitForEndOfFrame();
                            var p = Path.Combine(dir, "shot_" + a[1] + ".png");
                            var tex = ScreenCapture.CaptureScreenshotAsTexture();
                            try { File.WriteAllBytes(p, tex.EncodeToPNG()); } catch (System.Exception ex) { Log("shot failed " + ex.Message); }
                            Destroy(tex); Log("shot " + p); break;
                        }
                    case "log": Log(ln.Substring(4)); break;
                    case "dbg": if (game) Log("dbg " + ln.Substring(4) + " => " + game.DebugCmd(ln.Substring(4))); break;
                    default: Log("unknown: " + ln); break;
                }
                if (game) Log($"{ln}  -> state={game.state} rts={game.rts} hp={(game.player != null ? game.player.hp : -1):0} pos=({(game.player != null ? game.player.x : 0):0},{(game.player != null ? game.player.z : 0):0}) tp={game.tp} cam={(Camera.main ? Camera.main.transform.position.ToString("F1") : "-")}");
            }
            down.Clear(); SendKeys(); lmb = rmb = false; SendMouse(Vector2.zero);
#if UNITY_EDITOR
            set.backgroundBehavior = bb; set.editorInputBehaviorInPlayMode = eb;
#endif
            Log("done"); busy = false;
        }
    }
}
