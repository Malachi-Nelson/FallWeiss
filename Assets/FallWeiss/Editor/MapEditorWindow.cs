// Editor-only: visual map editor for Resources/missions.json (menu: Fall Weiss/Map Editor).
// Top-down view of a mission with its real terrain (baked from the game's own height function), every placed element
// drawn on top. Drag to move, drag handles to resize / reshape, drag pieces in from the palette, edit details in the
// side panel, then Save (a timestamped backup is kept in Logs/) and Play-test straight from the window.
// decor entries (the visual source for fences, walls, props...) stay linked to their segs / props twins (the collision
// source, z mirrored), so what you see and what blocks movement always move together.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FallWeiss;
using UnityEditor;
using UnityEngine;

namespace FallWeissEditor
{
    public class MapEditorWindow : EditorWindow
    {
        const string JsonPath = "Assets/FallWeiss/Resources/missions.json";
        static readonly HashSet<string> SegKinds = new HashSet<string> { "pier", "lamps", "fence", "hedge", "stonewall", "telegraph", "pipes", "deadrail" };
        static readonly string[] BuildKinds = { "house", "barn", "shed", "villa", "barracks", "warehouse", "bunker", "guardhouse", "ruin", "city", "factory", "boxcar", "stack", "tank", "admin", "engineshed", "postoffice", "sorting", "garage", "tenement", "gate", "fortbarracks", "arsenal", "casemate" };
        static readonly string[] PropKinds = { "crates", "barrels", "rocks", "barricade", "bushes", "flowers", "rubble", "shrine", "boat", "train", "tram", "buffer", "deadloco", "derailed", "signal", "watertower", "fountain" };
        static readonly string[] UnitTypes = { "rifle", "mg", "at", "tank", "sniper", "cav" };

        enum Cat { None, Point, Spawn, Build, Landmark, Forest, Village, Orchard, Hill, Road, Lane, Rail, Trench, Decor, Bridge }
        static readonly Cat[] Layers = { Cat.Point, Cat.Spawn, Cat.Build, Cat.Landmark, Cat.Forest, Cat.Village, Cat.Orchard, Cat.Hill, Cat.Road, Cat.Lane, Cat.Rail, Cat.Trench, Cat.Decor, Cat.Bridge };

        MissionList data; int mi; bool dirty;
        Mission M => data != null && data.missions != null && mi >= 0 && mi < data.missions.Length ? data.missions[mi] : null;
        Vector2 pan; float zoom = 1; Rect canvas; float ppm;
        Cat selCat; int selIdx = -1, selSub = -1;   // sub: -1 whole, -2 radius handle, 0/1 line ends, n trench vertex
        bool dragging, panning; Vector2 grabOffset;
        readonly List<string> undo = new List<string>(), redo = new List<string>();
        readonly Dictionary<Cat, bool> show = new Dictionary<Cat, bool>();
        bool snap = true, gridOn = true, terrainOn = true; float snapStep = 1;
        Vector2 sideScroll; int sideTab;
        int[] decorLink;   // decor i -> segs index, or -(props index + 2), or -1
        Texture2D terrain; bool terrainStale = true; string bakedFor;
        Dictionary<string, Vector2> kindSize;
        static GUIStyle labelStyle, smallStyle, paletteBtn; static int pendingPlay = -1, pendingSide;

        [MenuItem("Fall Weiss/Map Editor")]
        public static void Open() { var w = GetWindow<MapEditorWindow>("Map Editor"); w.minSize = new Vector2(900, 560); w.Show(); }

        void OnEnable() { foreach (var c in Layers) if (!show.ContainsKey(c)) show[c] = true; Load(); wantsMouseMove = true; }
        void OnDisable() { if (terrain) DestroyImmediate(terrain); }

        // ================= data =================
        void Load()
        {
            data = JsonUtility.FromJson<MissionList>(File.ReadAllText(JsonPath)); dirty = false; undo.Clear(); redo.Clear();
            mi = Mathf.Clamp(mi, 0, data.missions.Length - 1); foreach (var m in data.missions) Normalize(m); Relink(); terrainStale = true; Select(Cat.None, -1);
        }
        static void Normalize(Mission m)
        {
            m.roads ??= new FLine[0]; m.lanes ??= new FLine[0]; m.rails ??= new FLine[0]; m.forests ??= new FArea[0]; m.villages ??= new FArea[0];
            m.orchards ??= new FRect[0]; m.hills ??= new FHill[0]; m.trenches ??= new FTrench[0]; m.build ??= new FBuild[0]; m.landmarks ??= new FLandmark[0];
            m.segs ??= new FSeg[0]; m.props ??= new FProp[0]; m.decor ??= new FDecor[0]; m.points ??= new FPoint[0]; m.spawn ??= new FXZ[] { new FXZ(), new FXZ() };
            m.forces0 ??= new FForce[0]; m.forces1 ??= new FForce[0]; m.waves ??= new FWave[0]; m.bridges ??= new float[0];
        }
        void Save()
        {
            Directory.CreateDirectory("Logs"); File.Copy(JsonPath, "Logs/missions_backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json", true);
            File.WriteAllText(JsonPath, JsonUtility.ToJson(data, true)); AssetDatabase.ImportAsset(JsonPath); dirty = false; ShowNotification(new GUIContent("Saved " + M.name));
        }
        void Record() { undo.Add(JsonUtility.ToJson(M)); if (undo.Count > 80) undo.RemoveAt(0); redo.Clear(); dirty = true; }
        void Undo_() { if (undo.Count == 0) return; redo.Add(JsonUtility.ToJson(M)); JsonUtility.FromJsonOverwrite(undo[undo.Count - 1], M); undo.RemoveAt(undo.Count - 1); Normalize(M); Relink(); terrainStale = true; dirty = true; Select(Cat.None, -1); }
        void Redo_() { if (redo.Count == 0) return; undo.Add(JsonUtility.ToJson(M)); JsonUtility.FromJsonOverwrite(redo[redo.Count - 1], M); redo.RemoveAt(redo.Count - 1); Normalize(M); Relink(); terrainStale = true; dirty = true; Select(Cat.None, -1); }

        /// decor (three.js space, z mirrored) <-> segs / props (game space) twins, matched by kind and position.
        void Relink()
        {
            var m = M; if (m == null) return; decorLink = new int[m.decor.Length]; var usedS = new HashSet<int>(); var usedP = new HashSet<int>();
            for (int i = 0; i < m.decor.Length; i++)
            {
                var d = m.decor[i]; decorLink[i] = -1; if (d.v == null || d.v.Length < 2) continue;
                if (SegKinds.Contains(d.k) && d.v.Length >= 4)
                {
                    for (int j = 0; j < m.segs.Length; j++) { var s = m.segs[j]; if (usedS.Contains(j) || s.k != d.k) continue; if (Near(s.x1, d.v[0]) && Near(s.z1, -d.v[1]) && Near(s.x2, d.v[2]) && Near(s.z2, -d.v[3])) { decorLink[i] = j; usedS.Add(j); break; } }
                }
                else for (int j = 0; j < m.props.Length; j++) { var p = m.props[j]; if (usedP.Contains(j) || p.k != d.k) continue; if (Near(p.x, d.v[0]) && Near(p.z, -d.v[1])) { decorLink[i] = -(j + 2); usedP.Add(j); break; } }
            }
        }
        static bool Near(float a, float b) => Mathf.Abs(a - b) < .06f;
        bool IsSeg(FDecor d) => SegKinds.Contains(d.k) && d.v.Length >= 4;

        void SetDecorPos(int i, float x, float z, int end = -1)
        {   // end -1: move the whole piece to (x, z) centre; 0 / 1: move that end of a line piece
            var d = M.decor[i]; int link = decorLink[i];
            if (IsSeg(d))
            {
                if (end < 0) { float cx = (d.v[0] + d.v[2]) / 2, cz = -(d.v[1] + d.v[3]) / 2, dx = x - cx, dz = z - cz; d.v[0] += dx; d.v[2] += dx; d.v[1] -= dz; d.v[3] -= dz; }
                else { d.v[end * 2] = x; d.v[end * 2 + 1] = -z; }
                if (link >= 0) { var s = M.segs[link]; s.x1 = d.v[0]; s.z1 = -d.v[1]; s.x2 = d.v[2]; s.z2 = -d.v[3]; }
            }
            else { d.v[0] = x; d.v[1] = -z; if (link <= -2) { var p = M.props[-link - 2]; p.x = x; p.z = z; } }
        }

        // ================= GUI =================
        void Styles()
        {
            if (labelStyle != null) return;
            labelStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.white }, fontSize = 11 };
            smallStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1, 1, 1, .85f) } };
            paletteBtn = new GUIStyle(EditorStyles.miniButton) { alignment = TextAnchor.MiddleLeft };
        }

        void OnGUI()
        {
            Styles(); if (data == null) Load(); var m = M; if (m == null) { EditorGUILayout.HelpBox("No missions found in " + JsonPath, MessageType.Error); return; }
            Toolbar();
            const float side = 300; var area = new Rect(0, 22, position.width, position.height - 22);
            canvas = new Rect(area.x, area.y, area.width - side, area.height);
            DrawCanvas(m);
            GUILayout.BeginArea(new Rect(canvas.xMax, area.y, side, area.height), EditorStyles.helpBox);
            sideTab = GUILayout.Toolbar(sideTab, new[] { "Selection", "Palette", "Mission", "Layers" });
            sideScroll = EditorGUILayout.BeginScrollView(sideScroll);
            switch (sideTab) { case 0: SelectionPanel(m); break; case 1: Palette(); break; case 2: MissionPanel(m); break; default: LayerPanel(); break; }
            EditorGUILayout.EndScrollView(); GUILayout.EndArea();
            Keys();
        }

        void Toolbar()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            var names = data.missions.Select((x, i) => (i + 1) + ". " + x.name).ToArray();
            int nm = EditorGUILayout.Popup(mi, names, EditorStyles.toolbarPopup, GUILayout.Width(220));
            if (nm != mi) { if (!dirty || EditorUtility.DisplayDialog("Unsaved changes", "Switch mission without saving? Changes stay in memory until you Save or Revert.", "Switch", "Stay")) { mi = nm; undo.Clear(); redo.Clear(); Relink(); terrainStale = true; Select(Cat.None, -1); pan = Vector2.zero; zoom = 1; } }
            if (GUILayout.Button(new GUIContent("Save" + (dirty ? " *" : ""), "Write missions.json (backup kept in Logs/)"), EditorStyles.toolbarButton, GUILayout.Width(60))) Save();
            if (GUILayout.Button("Revert", EditorStyles.toolbarButton, GUILayout.Width(55)) && (!dirty || EditorUtility.DisplayDialog("Revert", "Discard all unsaved changes?", "Revert", "Cancel"))) Load();
            GUI.enabled = undo.Count > 0; if (GUILayout.Button("Undo", EditorStyles.toolbarButton, GUILayout.Width(45))) Undo_(); GUI.enabled = redo.Count > 0; if (GUILayout.Button("Redo", EditorStyles.toolbarButton, GUILayout.Width(45))) Redo_(); GUI.enabled = true;
            GUILayout.Space(8);
            snap = GUILayout.Toggle(snap, "Snap", EditorStyles.toolbarButton, GUILayout.Width(42)); snapStep = EditorGUILayout.FloatField(snapStep, EditorStyles.toolbarTextField, GUILayout.Width(30));
            gridOn = GUILayout.Toggle(gridOn, "Grid", EditorStyles.toolbarButton, GUILayout.Width(38)); terrainOn = GUILayout.Toggle(terrainOn, "Terrain", EditorStyles.toolbarButton, GUILayout.Width(55));
            if (GUILayout.Button("Fit", EditorStyles.toolbarButton, GUILayout.Width(32))) { pan = Vector2.zero; zoom = 1; }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("▶ Play as attacker", "Save, enter Play mode and start this mission"), EditorStyles.toolbarButton, GUILayout.Width(120))) PlayTest(M.attacker);
            if (GUILayout.Button("▶ Play as defender", EditorStyles.toolbarButton, GUILayout.Width(120))) PlayTest(1 - M.attacker);
            GUILayout.EndHorizontal();
        }

        // ---------------- canvas ----------------
        Vector2 W2S(float x, float z) => canvas.center + pan + new Vector2(x * ppm, -z * ppm);
        Vector2 S2W(Vector2 s) { var v = (s - canvas.center - pan) / ppm; return new Vector2(v.x, -v.y); }
        float Snap(float v) => snap && snapStep > 0 ? Mathf.Round(v / snapStep) * snapStep : Mathf.Round(v * 10) / 10;

        void DrawCanvas(Mission m)
        {
            ppm = Mathf.Min(canvas.width, canvas.height) / (2 * m.half * 1.08f) * zoom;
            EditorGUI.DrawRect(canvas, new Color(.12f, .13f, .11f));
            if (Event.current.type == EventType.Repaint)
            {
                GUI.BeginClip(canvas); var off = -canvas.position;
                Vector2 C(float x, float z) => W2S(x, z) + off;
                // terrain
                var tl = C(-m.half, m.half); var br = C(m.half, -m.half); var mapR = Rect.MinMaxRect(tl.x, tl.y, br.x, br.y);
                if (terrainOn) { if (terrainStale || bakedFor != m.name) BakeTerrain(m); if (terrain) GUI.DrawTexture(mapR, terrain, ScaleMode.StretchToFill); }
                else EditorGUI.DrawRect(mapR, Hex(m.ground, new Color(.35f, .45f, .25f)));
                if (gridOn) Grid(m, C);
                Handles.BeginGUI();
                var hc = Handles.color;
                // areas
                if (show[Cat.Hill]) for (int i = 0; i < m.hills.Length; i++) { var h = m.hills[i]; Ring(C(h.x, h.z), h.r * ppm, new Color(.75f, .65f, .4f, .9f), Sel(Cat.Hill, i), "hill " + h.h.ToString("0") + "m"); }
                if (show[Cat.Forest]) for (int i = 0; i < m.forests.Length; i++) { var a = m.forests[i]; Disc(C(a.x, a.z), a.r * ppm, new Color(.1f, .35f, .12f, .55f), Sel(Cat.Forest, i)); Lbl(C(a.x, a.z), "forest (" + a.n + ")"); }
                if (show[Cat.Village]) for (int i = 0; i < m.villages.Length; i++) { var a = m.villages[i]; Disc(C(a.x, a.z), a.r * ppm, new Color(.6f, .45f, .3f, .4f), Sel(Cat.Village, i)); Lbl(C(a.x, a.z), "village (" + a.n + ")"); }
                if (show[Cat.Orchard]) for (int i = 0; i < m.orchards.Length; i++) { var o = m.orchards[i]; var r = RectC(C(o.x, o.z), o.w * ppm, o.d * ppm); Box(r, new Color(.45f, .6f, .2f, .45f), Sel(Cat.Orchard, i)); Lbl(r.center, "orchard"); }
                // lines
                if (show[Cat.Road]) for (int i = 0; i < m.roads.Length; i++) Line(m.roads[i], C, new Color(.6f, .53f, .38f), Sel(Cat.Road, i));
                if (show[Cat.Lane]) for (int i = 0; i < m.lanes.Length; i++) Line(m.lanes[i], C, new Color(.45f, .44f, .42f), Sel(Cat.Lane, i));
                if (show[Cat.Rail]) for (int i = 0; i < m.rails.Length; i++) Line(m.rails[i], C, new Color(.25f, .22f, .2f), Sel(Cat.Rail, i), true);
                if (show[Cat.Trench]) for (int i = 0; i < m.trenches.Length; i++) Trench(m.trenches[i], C, Sel(Cat.Trench, i));
                if (show[Cat.Bridge] && m.hasRiver) for (int i = 0; i < m.bridges.Length; i++) { float bx = m.bridges[i], bz = m.RiverC(bx); var p = C(bx, bz); var r = RectC(p, 9 * ppm, (m.rw * 3.2f) * ppm); Box(r, new Color(.55f, .45f, .32f, .95f), Sel(Cat.Bridge, i)); }
                // buildings / landmarks / decor
                if (show[Cat.Build]) for (int i = 0; i < m.build.Length; i++) { var b = m.build[i]; var sz = Size(b); var r = RectC(C(b.x, b.z), sz.x * ppm, sz.y * ppm); Box(r, new Color(.82f, .74f, .62f, .95f), Sel(Cat.Build, i)); if (ppm > 1.3f) Lbl(r.center, b.k, true); }
                if (show[Cat.Landmark]) for (int i = 0; i < m.landmarks.Length; i++) { var l = m.landmarks[i]; var r = RectC(C(l.x, l.z), (l.k == "church" ? 10 : 5) * ppm, (l.k == "church" ? 16 : 5) * ppm); Box(r, new Color(.95f, .9f, .75f, .95f), Sel(Cat.Landmark, i)); Lbl(r.center + new Vector2(0, -r.height / 2 - 8), "✝ " + l.k); }
                if (show[Cat.Decor]) for (int i = 0; i < m.decor.Length; i++) DecorShape(i, C);
                // gameplay layer on top
                if (show[Cat.Spawn]) for (int i = 0; i < m.spawn.Length && i < 2; i++) { var s = m.spawn[i]; var p = C(s.x, s.z); Disc(p, 9, i == 0 ? new Color(.3f, .55f, 1, .95f) : new Color(1, .35f, .3f, .95f), Sel(Cat.Spawn, i)); Lbl(p + new Vector2(0, -16), (i == 0 ? "Wehrmacht" : "Polish Army") + " spawn" + (i == m.attacker ? " (attack)" : " (defend)")); ForcesBadge(m, i, -1, p + new Vector2(0, 16)); }
                if (show[Cat.Point])
                {
                    for (int i = 0; i + 1 < m.points.Length; i++) { Handles.color = new Color(1, .85f, .3f, .6f); Handles.DrawDottedLine(C(m.points[i].x, m.points[i].z), C(m.points[i + 1].x, m.points[i + 1].z), 4); }
                    for (int i = 0; i < m.points.Length; i++)
                    {
                        var pt = m.points[i]; var p = C(pt.x, pt.z); Disc(p, pt.r * ppm, new Color(1, .82f, .25f, .28f), Sel(Cat.Point, i)); Ring(p, pt.r * ppm, new Color(1, .85f, .3f, 1), Sel(Cat.Point, i), null);
                        Lbl(p, (i + 1).ToString(), false, 16); Lbl(p + new Vector2(0, -pt.r * ppm - 10), pt.n); ForcesBadge(m, 0, i, p + new Vector2(0, pt.r * ppm + 10)); ForcesBadge(m, 1, i, p + new Vector2(0, pt.r * ppm + 24));
                        if (Sel(Cat.Point, i)) Knob(p + new Vector2(pt.r * ppm, 0));
                    }
                }
                SelectionHandles(m, C);
                Handles.color = hc; Handles.EndGUI();
                // compass + scale
                GUI.Label(new Rect(8, 6, 300, 18), "N ↑   " + m.name + "  ·  " + (2 * m.half).ToString("0") + " m across  ·  grid 50 m", labelStyle);
                var mw = S2W(Event.current.mousePosition - off + canvas.position);
                if (canvas.Contains(Event.current.mousePosition)) GUI.Label(new Rect(8, canvas.height - 22, 260, 18), $"x {mw.x:0.0}   z {mw.y:0.0}", smallStyle);
                GUI.EndClip();
            }
            CanvasInput(m);
        }

        void Grid(Mission m, Func<float, float, Vector2> C)
        {
            Handles.BeginGUI(); Handles.color = new Color(1, 1, 1, .08f);
            for (float v = -Mathf.Floor(m.half / 50) * 50; v <= m.half; v += 50) { Handles.DrawLine(C(v, -m.half), C(v, m.half)); Handles.DrawLine(C(-m.half, v), C(m.half, v)); }
            Handles.color = new Color(1, 1, 1, .5f); var a = C(-m.half, -m.half); var b = C(m.half, m.half);
            Handles.DrawPolyLine(a, new Vector2(b.x, a.y), b, new Vector2(a.x, b.y), a); Handles.EndGUI();
        }

        void BakeTerrain(Mission m)
        {
            const int N = 192; if (!terrain) terrain = new Texture2D(N, N, TextureFormat.RGB24, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            float H = m.half, st = 2 * H / N; var wl = World.WaterLevel(m); var g = Hex(m.ground, new Color(.35f, .45f, .25f)); var px = new Color[N * N];
            var h = new float[N * N]; for (int j = 0; j < N; j++) for (int i = 0; i < N; i++) h[j * N + i] = World.PreviewHeight(m, -H + (i + .5f) * st, -H + (j + .5f) * st);
            for (int j = 0; j < N; j++) for (int i = 0; i < N; i++)
                {
                    float y = h[j * N + i], dx = h[j * N + Mathf.Min(N - 1, i + 1)] - h[j * N + Mathf.Max(0, i - 1)], dz = h[Mathf.Min(N - 1, j + 1) * N + i] - h[Mathf.Max(0, j - 1) * N + i];
                    float shade = Mathf.Clamp(1 - dx * .09f + dz * .06f, .55f, 1.35f);
                    Color c = m.city ? new Color(.42f, .42f, .4f) : g; c = Color.Lerp(c, new Color(.55f, .5f, .38f), Mathf.Clamp01((y - 6) / 18)) * shade;
                    if (wl.HasValue && y < wl.Value - .35f) c = Color.Lerp(new Color(.18f, .32f, .46f), new Color(.1f, .2f, .32f), Mathf.Clamp01((wl.Value - y) / 6));
                    px[j * N + i] = c;
                }
            terrain.SetPixels(px); terrain.Apply(); terrainStale = false; bakedFor = m.name;
        }

        // ---------------- shapes ----------------
        bool Sel(Cat c, int i) => selCat == c && selIdx == i;
        static Rect RectC(Vector2 c, float w, float h) => new Rect(c.x - w / 2, c.y - h / 2, Mathf.Max(3, w), Mathf.Max(3, h));
        static void Disc(Vector2 p, float r, Color c, bool sel) { Handles.color = c; Handles.DrawSolidDisc(p, Vector3.forward, Mathf.Max(2, r)); if (sel) { Handles.color = Color.white; Handles.DrawWireDisc(p, Vector3.forward, Mathf.Max(3, r) + 2, 2); } }
        static void Ring(Vector2 p, float r, Color c, bool sel, string label) { Handles.color = sel ? Color.white : c; Handles.DrawWireDisc(p, Vector3.forward, Mathf.Max(2, r), sel ? 3 : 1.5f); if (label != null) Lbl(p + new Vector2(0, -r - 8), label); }
        static void Box(Rect r, Color c, bool sel) { EditorGUI.DrawRect(r, c); if (sel) { Handles.color = Color.white; Handles.DrawPolyLine(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin)); } }
        static void Knob(Vector2 p) { Handles.color = Color.black; Handles.DrawSolidDisc(p, Vector3.forward, 6); Handles.color = Color.white; Handles.DrawSolidDisc(p, Vector3.forward, 4.5f); }
        static void Lbl(Vector2 p, string s, bool small = false, int size = 0)
        {
            var st = small ? smallStyle : labelStyle; if (size > 0) st = new GUIStyle(labelStyle) { fontSize = size };
            var c = new GUIContent(s); var sz = st.CalcSize(c); var r = new Rect(p.x - sz.x / 2, p.y - sz.y / 2, sz.x, sz.y);
            var sh = r; sh.position += Vector2.one; var col = st.normal.textColor; st.normal.textColor = new Color(0, 0, 0, .8f); GUI.Label(sh, c, st); st.normal.textColor = col; GUI.Label(r, c, st);
        }
        void Line(FLine l, Func<float, float, Vector2> C, Color c, bool sel, bool dashed = false)
        {
            Vector2 a = C(l.x1, l.z1), b = C(l.x2, l.z2); Handles.color = c; float w = Mathf.Max(2, l.w * ppm);
            if (dashed) { Handles.DrawAAPolyLine(w, a, b); Handles.color = new Color(.6f, .55f, .45f); Handles.DrawDottedLine(a, b, 3); } else Handles.DrawAAPolyLine(w, a, b);
            if (sel) { Handles.color = Color.white; Handles.DrawAAPolyLine(2, a, b); Knob(a); Knob(b); }
        }
        void Trench(FTrench t, Func<float, float, Vector2> C, bool sel)
        {
            if (t.pts == null || t.pts.Length < 4) return; var pts = new List<Vector3>(); for (int k = 0; k + 1 < t.pts.Length; k += 2) pts.Add(C(t.pts[k], t.pts[k + 1]));
            Handles.color = new Color(.3f, .22f, .14f); Handles.DrawAAPolyLine(Mathf.Max(3, 3.4f * ppm), pts.ToArray());
            Handles.color = new Color(.55f, .42f, .26f); Handles.DrawAAPolyLine(1.5f, pts.ToArray());
            if (sel) foreach (var p in pts) Knob(p);
        }
        void DecorShape(int i, Func<float, float, Vector2> C)
        {
            var d = M.decor[i]; if (d.v == null || d.v.Length < 2) return; bool sel = Sel(Cat.Decor, i);
            if (IsSeg(d))
            {
                Vector2 a = C(d.v[0], -d.v[1]), b = C(d.v[2], -d.v[3]);
                Color c = d.k == "stonewall" ? new Color(.62f, .6f, .56f) : d.k == "hedge" ? new Color(.2f, .42f, .16f) : d.k == "fence" ? new Color(.55f, .42f, .28f) : d.k == "pier" ? new Color(.5f, .38f, .25f) : new Color(.85f, .85f, .7f, .7f);
                Handles.color = c; if (d.k == "lamps" || d.k == "telegraph") Handles.DrawDottedLine(a, b, 4); else Handles.DrawAAPolyLine(d.k == "pier" ? Mathf.Max(3, 5 * ppm) : 3, a, b);
                if (sel) { Handles.color = Color.white; Handles.DrawAAPolyLine(1.5f, a, b); Knob(a); Knob(b); }
            }
            else { var p = C(d.v[0], -d.v[1]); Disc(p, Mathf.Max(3, 1.6f * ppm), new Color(.9f, .7f, .35f, .9f), sel); if (ppm > 1.6f || sel) Lbl(p + new Vector2(0, -10), d.k, true); }
        }
        void ForcesBadge(Mission m, int team, int loc, Vector2 p)
        {
            var f = m.Forces(team).Where(x => x.loc == loc).ToList(); if (f.Count == 0) return;
            string s = string.Join(" ", f.Select(x => x.n + "×" + x.type)); var col = team == 0 ? new Color(.55f, .75f, 1) : new Color(1, .6f, .55f);
            var st = new GUIStyle(smallStyle) { normal = { textColor = col } }; var sz = st.CalcSize(new GUIContent(s)); EditorGUI.DrawRect(new Rect(p.x - sz.x / 2 - 3, p.y - sz.y / 2, sz.x + 6, sz.y), new Color(0, 0, 0, .55f)); GUI.Label(new Rect(p.x - sz.x / 2, p.y - sz.y / 2, sz.x, sz.y), s, st);
        }
        void SelectionHandles(Mission m, Func<float, float, Vector2> C)
        {
            switch (selCat)
            {
                case Cat.Forest: case Cat.Village: { var a = (selCat == Cat.Forest ? m.forests : m.villages)[selIdx]; Knob(C(a.x + a.r, a.z)); break; }
                case Cat.Hill: { var h = m.hills[selIdx]; Knob(C(h.x + h.r, h.z)); break; }
                case Cat.Orchard: { var o = m.orchards[selIdx]; Knob(C(o.x + o.w / 2, o.z - o.d / 2)); break; }
                case Cat.Build: { var b = m.build[selIdx]; var s = Size(b); Knob(C(b.x + s.x / 2, b.z - s.y / 2)); break; }
            }
        }
        Vector2 Size(FBuild b)
        {
            if (kindSize == null)
            {   // default footprints straight from World.KIND, so the map shows what the game will build
                kindSize = new Dictionary<string, Vector2>();
                var f = typeof(World).GetField("KIND", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                if (f?.GetValue(null) is IDictionary dict) foreach (DictionaryEntry e in dict) { var t = e.Value.GetType(); kindSize[(string)e.Key] = new Vector2((float)t.GetField("w").GetValue(e.Value), (float)t.GetField("d").GetValue(e.Value)); }
            }
            var ks = kindSize.TryGetValue(b.k, out var v) ? v : new Vector2(8, 7);
            return new Vector2(b.w > 0 ? b.w : ks.x, b.d > 0 ? b.d : ks.y);
        }
        static Color Hex(string h, Color fb) => !string.IsNullOrEmpty(h) && ColorUtility.TryParseHtmlString(h, out var c) ? c : fb;

        // ---------------- input ----------------
        void CanvasInput(Mission m)
        {
            var e = Event.current; var mp = e.mousePosition; bool over = canvas.Contains(mp);
            if (e.type == EventType.ScrollWheel && over) { var before = S2W(mp); zoom = Mathf.Clamp(zoom * (e.delta.y > 0 ? .88f : 1.14f), .3f, 14); ppm = Mathf.Min(canvas.width, canvas.height) / (2 * m.half * 1.08f) * zoom; var after = W2S(before.x, before.y); pan += mp - after; e.Use(); Repaint(); }
            if (e.type == EventType.MouseDown && over && (e.button == 1 || e.button == 2 || (e.button == 0 && e.alt))) { panning = true; e.Use(); }
            if (e.type == EventType.MouseDrag && panning) { pan += e.delta; e.Use(); Repaint(); }
            if (e.type == EventType.MouseUp && panning) { panning = false; e.Use(); }
            if (e.type == EventType.MouseDown && over && e.button == 0 && !e.alt)
            {
                var w = S2W(mp); Pick(m, mp); if (selCat != Cat.None) { Record(); dragging = true; grabOffset = GrabOffset(m, w); } e.Use(); Repaint();
            }
            if (e.type == EventType.MouseDrag && dragging) { var w = S2W(mp) - grabOffset; Move(m, Snap(w.x), Snap(w.y)); e.Use(); Repaint(); }
            if (e.type == EventType.MouseUp && dragging) { dragging = false; if (TerrainCat(selCat)) terrainStale = true; e.Use(); Repaint(); }
            // drag & drop from the palette
            if ((e.type == EventType.DragUpdated || e.type == EventType.DragPerform) && over && DragAndDrop.GetGenericData("fwmap") is string kind)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (e.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); var w = S2W(mp); Add(m, kind, Snap(w.x), Snap(w.y)); }
                e.Use();
            }
            if (e.type == EventType.MouseMove && over) Repaint();
        }
        static bool TerrainCat(Cat c) => c == Cat.Hill || c == Cat.Rail || c == Cat.Trench;

        void Pick(Mission m, Vector2 mp)
        {
            bool Hit(Vector2 p, float r) => (mp - p).sqrMagnitude <= r * r;
            Vector2 C(float x, float z) => W2S(x, z);
            // handles of the current selection first
            if (selCat != Cat.None && selIdx >= 0)
            {
                int k = HandleUnder(m, mp); if (k != int.MinValue) { selSub = k; return; }
            }
            selSub = -1;
            if (show[Cat.Spawn]) for (int i = 0; i < m.spawn.Length && i < 2; i++) if (Hit(C(m.spawn[i].x, m.spawn[i].z), 11)) { Select(Cat.Spawn, i); return; }
            if (show[Cat.Point]) for (int i = 0; i < m.points.Length; i++) if (Hit(C(m.points[i].x, m.points[i].z), Mathf.Max(10, Mathf.Min(m.points[i].r * ppm, 26)))) { Select(Cat.Point, i); return; }
            if (show[Cat.Decor]) for (int i = m.decor.Length - 1; i >= 0; i--) { var d = m.decor[i]; if (d.v == null || d.v.Length < 2) continue; if (IsSeg(d) ? SegDist(mp, C(d.v[0], -d.v[1]), C(d.v[2], -d.v[3])) < 6 : Hit(C(d.v[0], -d.v[1]), Mathf.Max(6, 1.6f * ppm))) { Select(Cat.Decor, i); return; } }
            if (show[Cat.Landmark]) for (int i = 0; i < m.landmarks.Length; i++) if (RectC(C(m.landmarks[i].x, m.landmarks[i].z), 10 * ppm + 6, 16 * ppm + 6).Contains(mp)) { Select(Cat.Landmark, i); return; }
            if (show[Cat.Build]) for (int i = m.build.Length - 1; i >= 0; i--) { var s = Size(m.build[i]); if (RectC(C(m.build[i].x, m.build[i].z), s.x * ppm + 4, s.y * ppm + 4).Contains(mp)) { Select(Cat.Build, i); return; } }
            if (show[Cat.Bridge] && m.hasRiver) for (int i = 0; i < m.bridges.Length; i++) if (Hit(C(m.bridges[i], m.RiverC(m.bridges[i])), Mathf.Max(8, 6 * ppm))) { Select(Cat.Bridge, i); return; }
            if (show[Cat.Trench]) for (int i = 0; i < m.trenches.Length; i++) { var p = m.trenches[i].pts; for (int k = 0; k + 3 < p.Length; k += 2) if (SegDist(mp, C(p[k], p[k + 1]), C(p[k + 2], p[k + 3])) < 6) { Select(Cat.Trench, i); return; } }
            foreach (var (cat, arr) in new (Cat, FLine[])[] { (Cat.Road, m.roads), (Cat.Lane, m.lanes), (Cat.Rail, m.rails) })
                if (show[cat]) for (int i = 0; i < arr.Length; i++) if (SegDist(mp, C(arr[i].x1, arr[i].z1), C(arr[i].x2, arr[i].z2)) < Mathf.Max(5, arr[i].w * ppm / 2)) { Select(cat, i); return; }
            if (show[Cat.Orchard]) for (int i = 0; i < m.orchards.Length; i++) if (RectC(C(m.orchards[i].x, m.orchards[i].z), m.orchards[i].w * ppm, m.orchards[i].d * ppm).Contains(mp)) { Select(Cat.Orchard, i); return; }
            if (show[Cat.Village]) for (int i = 0; i < m.villages.Length; i++) if (Hit(C(m.villages[i].x, m.villages[i].z), m.villages[i].r * ppm)) { Select(Cat.Village, i); return; }
            if (show[Cat.Forest]) for (int i = 0; i < m.forests.Length; i++) if (Hit(C(m.forests[i].x, m.forests[i].z), m.forests[i].r * ppm)) { Select(Cat.Forest, i); return; }
            if (show[Cat.Hill]) for (int i = 0; i < m.hills.Length; i++) if (Hit(C(m.hills[i].x, m.hills[i].z), m.hills[i].r * ppm)) { Select(Cat.Hill, i); return; }
            Select(Cat.None, -1);
        }
        int HandleUnder(Mission m, Vector2 mp)
        {
            bool Hit(Vector2 p) => (mp - p).sqrMagnitude <= 64; Vector2 C(float x, float z) => W2S(x, z);
            switch (selCat)
            {
                case Cat.Point: { var p = m.points[selIdx]; if (Hit(C(p.x, p.z) + new Vector2(p.r * ppm, 0))) return -2; break; }
                case Cat.Forest: case Cat.Village: { var a = (selCat == Cat.Forest ? m.forests : m.villages)[selIdx]; if (Hit(C(a.x + a.r, a.z))) return -2; break; }
                case Cat.Hill: { var h = m.hills[selIdx]; if (Hit(C(h.x + h.r, h.z))) return -2; break; }
                case Cat.Orchard: { var o = m.orchards[selIdx]; if (Hit(C(o.x + o.w / 2, o.z - o.d / 2))) return -2; break; }
                case Cat.Build: { var b = m.build[selIdx]; var s = Size(b); if (Hit(C(b.x + s.x / 2, b.z - s.y / 2))) return -2; break; }
                case Cat.Road: case Cat.Lane: case Cat.Rail: { var l = Lines(selCat)[selIdx]; if (Hit(C(l.x1, l.z1))) return 0; if (Hit(C(l.x2, l.z2))) return 1; break; }
                case Cat.Trench: { var p = m.trenches[selIdx].pts; for (int k = 0; k + 1 < p.Length; k += 2) if (Hit(C(p[k], p[k + 1]))) return k / 2; break; }
                case Cat.Decor: { var d = m.decor[selIdx]; if (IsSeg(d)) { if (Hit(C(d.v[0], -d.v[1]))) return 0; if (Hit(C(d.v[2], -d.v[3]))) return 1; } break; }
            }
            return int.MinValue;
        }
        static float SegDist(Vector2 p, Vector2 a, Vector2 b) { var ab = b - a; float t = ab.sqrMagnitude < 1e-4f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude); return (p - (a + ab * t)).magnitude; }
        FLine[] Lines(Cat c) => c == Cat.Road ? M.roads : c == Cat.Lane ? M.lanes : M.rails;

        Vector2 Anchor(Mission m)
        {   // the world point that follows the mouse while dragging the current selection
            switch (selCat)
            {
                case Cat.Point: { var p = m.points[selIdx]; return selSub == -2 ? new Vector2(p.x + p.r, p.z) : new Vector2(p.x, p.z); }
                case Cat.Spawn: return new Vector2(m.spawn[selIdx].x, m.spawn[selIdx].z);
                case Cat.Build: { var b = m.build[selIdx]; var s = Size(b); return selSub == -2 ? new Vector2(b.x + s.x / 2, b.z - s.y / 2) : new Vector2(b.x, b.z); }
                case Cat.Landmark: return new Vector2(m.landmarks[selIdx].x, m.landmarks[selIdx].z);
                case Cat.Forest: case Cat.Village: { var a = (selCat == Cat.Forest ? m.forests : m.villages)[selIdx]; return selSub == -2 ? new Vector2(a.x + a.r, a.z) : new Vector2(a.x, a.z); }
                case Cat.Hill: { var h = m.hills[selIdx]; return selSub == -2 ? new Vector2(h.x + h.r, h.z) : new Vector2(h.x, h.z); }
                case Cat.Orchard: { var o = m.orchards[selIdx]; return selSub == -2 ? new Vector2(o.x + o.w / 2, o.z - o.d / 2) : new Vector2(o.x, o.z); }
                case Cat.Road: case Cat.Lane: case Cat.Rail: { var l = Lines(selCat)[selIdx]; return selSub == 0 ? new Vector2(l.x1, l.z1) : selSub == 1 ? new Vector2(l.x2, l.z2) : new Vector2((l.x1 + l.x2) / 2, (l.z1 + l.z2) / 2); }
                case Cat.Trench: { var p = m.trenches[selIdx].pts; if (selSub >= 0) return new Vector2(p[selSub * 2], p[selSub * 2 + 1]); float sx = 0, sz = 0; int n = p.Length / 2; for (int k = 0; k + 1 < p.Length; k += 2) { sx += p[k]; sz += p[k + 1]; } return new Vector2(sx / n, sz / n); }
                case Cat.Decor: { var d = m.decor[selIdx]; if (IsSeg(d)) return selSub == 0 ? new Vector2(d.v[0], -d.v[1]) : selSub == 1 ? new Vector2(d.v[2], -d.v[3]) : new Vector2((d.v[0] + d.v[2]) / 2, -(d.v[1] + d.v[3]) / 2); return new Vector2(d.v[0], -d.v[1]); }
                case Cat.Bridge: return new Vector2(m.bridges[selIdx], m.RiverC(m.bridges[selIdx]));
            }
            return Vector2.zero;
        }
        Vector2 GrabOffset(Mission m, Vector2 w) => w - Anchor(m);

        void Move(Mission m, float x, float z)
        {
            var a = Anchor(m); float dx = x - a.x, dz = z - a.y;
            switch (selCat)
            {
                case Cat.Point: { var p = m.points[selIdx]; if (selSub == -2) p.r = Mathf.Max(4, Snap(Mathf.Abs(x - p.x))); else { p.x = x; p.z = z; } break; }
                case Cat.Spawn: m.spawn[selIdx].x = x; m.spawn[selIdx].z = z; break;
                case Cat.Build: { var b = m.build[selIdx]; if (selSub == -2) { b.w = Mathf.Max(2, Snap(Mathf.Abs(x - b.x) * 2)); b.d = Mathf.Max(2, Snap(Mathf.Abs(z - b.z) * 2)); } else { b.x = x; b.z = z; } break; }
                case Cat.Landmark: m.landmarks[selIdx].x = x; m.landmarks[selIdx].z = z; break;
                case Cat.Forest: case Cat.Village: { var ar = (selCat == Cat.Forest ? m.forests : m.villages)[selIdx]; if (selSub == -2) ar.r = Mathf.Max(4, Snap(Mathf.Abs(x - ar.x))); else { ar.x = x; ar.z = z; } break; }
                case Cat.Hill: { var h = m.hills[selIdx]; if (selSub == -2) h.r = Mathf.Max(8, Snap(Mathf.Abs(x - h.x))); else { h.x = x; h.z = z; } break; }
                case Cat.Orchard: { var o = m.orchards[selIdx]; if (selSub == -2) { o.w = Mathf.Max(6, Snap(Mathf.Abs(x - o.x) * 2)); o.d = Mathf.Max(6, Snap(Mathf.Abs(z - o.z) * 2)); } else { o.x = x; o.z = z; } break; }
                case Cat.Road: case Cat.Lane: case Cat.Rail: { var l = Lines(selCat)[selIdx]; if (selSub != 1) { l.x1 += dx; l.z1 += dz; } if (selSub != 0) { l.x2 += dx; l.z2 += dz; } break; }
                case Cat.Trench: { var p = m.trenches[selIdx].pts; if (selSub >= 0) { p[selSub * 2] = x; p[selSub * 2 + 1] = z; } else for (int k = 0; k + 1 < p.Length; k += 2) { p[k] += dx; p[k + 1] += dz; } break; }
                case Cat.Decor: if (IsSeg(m.decor[selIdx]) && selSub >= 0) SetDecorPos(selIdx, x, z, selSub); else SetDecorPos(selIdx, x, z); break;
                case Cat.Bridge: m.bridges[selIdx] = x; break;
            }
        }

        void Select(Cat c, int i) { selCat = c; selIdx = i; selSub = -1; GUI.FocusControl(null); if (c != Cat.None) sideTab = 0; }

        void Keys()
        {
            var e = Event.current; if (e.type != EventType.KeyDown) return;
            if ((e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && selCat != Cat.None && GUIUtility.keyboardControl == 0) { Record(); Delete(M); e.Use(); }
            if (e.control && e.keyCode == KeyCode.Z) { Undo_(); e.Use(); }
            if (e.control && e.keyCode == KeyCode.Y) { Redo_(); e.Use(); }
            if (e.control && e.keyCode == KeyCode.S) { Save(); e.Use(); }
            if (e.control && e.keyCode == KeyCode.D && selCat != Cat.None) { Record(); Duplicate(M); e.Use(); }
        }

        // ---------------- add / delete / duplicate ----------------
        static T[] Append<T>(T[] a, T v) { var l = (a ?? new T[0]).ToList(); l.Add(v); return l.ToArray(); }
        static T[] RemoveAt<T>(T[] a, int i) { var l = a.ToList(); l.RemoveAt(i); return l.ToArray(); }

        void Add(Mission m, string kind, float x, float z)
        {
            Record(); var parts = kind.Split(':'); string cat = parts[0], k = parts.Length > 1 ? parts[1] : "";
            switch (cat)
            {
                case "point": m.points = Append(m.points, new FPoint { n = "Objective " + (m.points.Length + 1), x = x, z = z, r = 14 }); Select(Cat.Point, m.points.Length - 1); break;
                case "build": m.build = Append(m.build, new FBuild { k = k, x = x, z = z }); Select(Cat.Build, m.build.Length - 1); break;
                case "landmark": m.landmarks = Append(m.landmarks, new FLandmark { k = k, x = x, z = z }); Select(Cat.Landmark, m.landmarks.Length - 1); break;
                case "forest": m.forests = Append(m.forests, new FArea { x = x, z = z, r = 30, n = 60 }); Select(Cat.Forest, m.forests.Length - 1); terrainStale = true; break;
                case "village": m.villages = Append(m.villages, new FArea { x = x, z = z, r = 22, n = 4 }); Select(Cat.Village, m.villages.Length - 1); break;
                case "orchard": m.orchards = Append(m.orchards, new FRect { x = x, z = z, w = 40, d = 28 }); Select(Cat.Orchard, m.orchards.Length - 1); break;
                case "hill": m.hills = Append(m.hills, new FHill { x = x, z = z, r = 50, h = 8 }); Select(Cat.Hill, m.hills.Length - 1); terrainStale = true; break;
                case "road": m.roads = Append(m.roads, new FLine { x1 = x - 30, z1 = z, x2 = x + 30, z2 = z, w = 6 }); Select(Cat.Road, m.roads.Length - 1); break;
                case "lane": m.lanes = Append(m.lanes, new FLine { x1 = x - 25, z1 = z, x2 = x + 25, z2 = z, w = 10 }); Select(Cat.Lane, m.lanes.Length - 1); break;
                case "rail": m.rails = Append(m.rails, new FLine { x1 = x - 60, z1 = z, x2 = x + 60, z2 = z, w = 3 }); Select(Cat.Rail, m.rails.Length - 1); terrainStale = true; break;
                case "trench": m.trenches = Append(m.trenches, new FTrench { pts = new[] { x - 20, z, x, z + 4, x + 20, z } }); Select(Cat.Trench, m.trenches.Length - 1); terrainStale = true; break;
                case "bridge": if (m.hasRiver) { m.bridges = Append(m.bridges, x); Select(Cat.Bridge, m.bridges.Length - 1); } else ShowNotification(new GUIContent("This map has no river")); break;
                case "seg":
                    m.decor = Append(m.decor, new FDecor { k = k, v = new[] { x - 12, -z, x + 12, -z } }); m.segs = Append(m.segs, new FSeg { k = k, x1 = x - 12, z1 = z, x2 = x + 12, z2 = z });
                    Relink(); Select(Cat.Decor, m.decor.Length - 1); break;
                case "prop":
                    m.decor = Append(m.decor, new FDecor { k = k, v = new[] { x, -z, 0f } }); m.props = Append(m.props, new FProp { k = k, x = x, z = z });
                    Relink(); Select(Cat.Decor, m.decor.Length - 1); break;
            }
            Repaint();
        }

        void Delete(Mission m)
        {
            int i = selIdx; if (i < 0) return;
            switch (selCat)
            {
                case Cat.Point:
                    m.points = RemoveAt(m.points, i);   // forces placed there fall back to their spawn; later ones shift down
                    foreach (var f in m.forces0.Concat(m.forces1)) { if (f.loc == i) f.loc = -1; else if (f.loc > i) f.loc--; }
                    break;
                case Cat.Spawn: ShowNotification(new GUIContent("Spawns can be moved, not deleted")); return;
                case Cat.Build: m.build = RemoveAt(m.build, i); break;
                case Cat.Landmark: m.landmarks = RemoveAt(m.landmarks, i); break;
                case Cat.Forest: m.forests = RemoveAt(m.forests, i); break;
                case Cat.Village: m.villages = RemoveAt(m.villages, i); break;
                case Cat.Orchard: m.orchards = RemoveAt(m.orchards, i); break;
                case Cat.Hill: m.hills = RemoveAt(m.hills, i); terrainStale = true; break;
                case Cat.Road: m.roads = RemoveAt(m.roads, i); break;
                case Cat.Lane: m.lanes = RemoveAt(m.lanes, i); break;
                case Cat.Rail: m.rails = RemoveAt(m.rails, i); terrainStale = true; break;
                case Cat.Trench: m.trenches = RemoveAt(m.trenches, i); terrainStale = true; break;
                case Cat.Bridge: m.bridges = RemoveAt(m.bridges, i); break;
                case Cat.Decor:
                    { int link = decorLink[i]; m.decor = RemoveAt(m.decor, i); if (link >= 0) m.segs = RemoveAt(m.segs, link); else if (link <= -2) m.props = RemoveAt(m.props, -link - 2); Relink(); break; }
            }
            Select(Cat.None, -1); Repaint();
        }

        void Duplicate(Mission m)
        {
            int i = selIdx; const float o = 6;
            switch (selCat)
            {
                case Cat.Build: { var b = m.build[i]; m.build = Append(m.build, new FBuild { k = b.k, x = b.x + o, z = b.z - o, w = b.w, d = b.d, h = b.h }); Select(Cat.Build, m.build.Length - 1); break; }
                case Cat.Landmark: { var l = m.landmarks[i]; m.landmarks = Append(m.landmarks, new FLandmark { k = l.k, x = l.x + o, z = l.z - o }); Select(Cat.Landmark, m.landmarks.Length - 1); break; }
                case Cat.Decor:
                    {
                        var d = m.decor[i]; var v = (float[])d.v.Clone(); v[0] += o; v[1] += o; if (IsSeg(d)) { v[2] += o; v[3] += o; m.segs = Append(m.segs, new FSeg { k = d.k, x1 = v[0], z1 = -v[1], x2 = v[2], z2 = -v[3] }); }
                        else { var src = decorLink[i] <= -2 ? m.props[-decorLink[i] - 2] : null; m.props = Append(m.props, new FProp { k = d.k, x = v[0], z = -v[1], a = src?.a ?? 0, b = src?.b ?? 0 }); }
                        m.decor = Append(m.decor, new FDecor { k = d.k, v = v }); Relink(); Select(Cat.Decor, m.decor.Length - 1); break;
                    }
                default: ShowNotification(new GUIContent("Duplicate works on buildings, landmarks and props")); break;
            }
        }

        // ================= side panels =================
        void SelectionPanel(Mission m)
        {
            if (selCat == Cat.None || selIdx < 0) { EditorGUILayout.HelpBox("Click anything on the map to select it.\n\n• Drag to move · drag the white knobs to resize / reshape\n• Right- or middle-drag (or Alt+drag) to pan · wheel to zoom\n• Palette tab: drag pieces onto the map\n• Delete removes · Ctrl+D duplicates · Ctrl+Z / Ctrl+Y undo / redo · Ctrl+S saves", MessageType.Info); return; }
            EditorGUILayout.LabelField(selCat + " #" + (selIdx + 1), EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck(); string before = JsonUtility.ToJson(m);
            switch (selCat)
            {
                case Cat.Point:
                    {
                        var p = m.points[selIdx]; p.n = EditorGUILayout.TextField("Name", p.n); XZ(ref p.x, ref p.z); p.r = EditorGUILayout.Slider("Capture radius", p.r, 4, 40); p.inside = EditorGUILayout.Toggle("Inside a building", p.inside);
                        EditorGUILayout.LabelField("Capture order: " + (selIdx + 1) + " of " + m.points.Length);
                        GUILayout.BeginHorizontal(); GUI.enabled = selIdx > 0; if (GUILayout.Button("◀ Earlier")) SwapPoints(m, selIdx, selIdx - 1); GUI.enabled = selIdx < m.points.Length - 1; if (GUILayout.Button("Later ▶")) SwapPoints(m, selIdx, selIdx + 1); GUI.enabled = true; GUILayout.EndHorizontal();
                        break;
                    }
                case Cat.Spawn: { var s = m.spawn[selIdx]; EditorGUILayout.LabelField(selIdx == 0 ? "Wehrmacht" : "Polish Army"); XZ(ref s.x, ref s.z); break; }
                case Cat.Build:
                    {
                        var b = m.build[selIdx]; int k = Array.IndexOf(BuildKinds, b.k); int nk = EditorGUILayout.Popup("Kind", Mathf.Max(0, k), BuildKinds); if (nk != k && nk >= 0) b.k = BuildKinds[nk]; XZ(ref b.x, ref b.z);
                        b.w = EditorGUILayout.FloatField(new GUIContent("Width (0 = default)"), b.w); b.d = EditorGUILayout.FloatField(new GUIContent("Depth (0 = default)"), b.d); b.h = EditorGUILayout.FloatField(new GUIContent("Height (0 = default)"), b.h);
                        break;
                    }
                case Cat.Landmark: { var l = m.landmarks[selIdx]; int k = l.k == "windmill" ? 1 : 0; l.k = new[] { "church", "windmill" }[EditorGUILayout.Popup("Kind", k, new[] { "church", "windmill" })]; XZ(ref l.x, ref l.z); break; }
                case Cat.Forest: case Cat.Village: { var a = (selCat == Cat.Forest ? m.forests : m.villages)[selIdx]; XZ(ref a.x, ref a.z); a.r = EditorGUILayout.FloatField("Radius", a.r); a.n = EditorGUILayout.IntField(selCat == Cat.Forest ? "Trees" : "Houses", (int)a.n); break; }
                case Cat.Orchard: { var o = m.orchards[selIdx]; XZ(ref o.x, ref o.z); o.w = EditorGUILayout.FloatField("Width", o.w); o.d = EditorGUILayout.FloatField("Depth", o.d); break; }
                case Cat.Hill: { var h = m.hills[selIdx]; XZ(ref h.x, ref h.z); h.r = EditorGUILayout.FloatField("Radius", h.r); h.h = EditorGUILayout.FloatField("Height", h.h); break; }
                case Cat.Road: case Cat.Lane: case Cat.Rail:
                    { var l = Lines(selCat)[selIdx]; EditorGUILayout.LabelField("Start"); XZ(ref l.x1, ref l.z1); EditorGUILayout.LabelField("End"); XZ(ref l.x2, ref l.z2); l.w = EditorGUILayout.FloatField("Width", l.w); break; }
                case Cat.Trench:
                    {
                        var t = m.trenches[selIdx]; EditorGUILayout.LabelField(t.pts.Length / 2 + " points (drag the knobs)");
                        GUILayout.BeginHorizontal(); if (GUILayout.Button("+ point at end")) { float lx = t.pts[t.pts.Length - 2], lz = t.pts[t.pts.Length - 1]; t.pts = t.pts.Concat(new[] { lx + 12, lz }).ToArray(); }
                        GUI.enabled = t.pts.Length > 4; if (GUILayout.Button("− last point")) t.pts = t.pts.Take(t.pts.Length - 2).ToArray(); GUI.enabled = true; GUILayout.EndHorizontal(); break;
                    }
                case Cat.Bridge: m.bridges[selIdx] = EditorGUILayout.FloatField("x along the river", m.bridges[selIdx]); break;
                case Cat.Decor:
                    {
                        var d = m.decor[selIdx]; EditorGUILayout.LabelField("Kind", d.k); int link = decorLink[selIdx];
                        EditorGUILayout.LabelField("Collision twin", link >= 0 ? "segs #" + (link + 1) : link <= -2 ? "props #" + (-link - 1) : "none (visual only)");
                        if (IsSeg(d)) { float x1 = d.v[0], z1 = -d.v[1], x2 = d.v[2], z2 = -d.v[3]; EditorGUILayout.LabelField("Start"); XZ(ref x1, ref z1); EditorGUILayout.LabelField("End"); XZ(ref x2, ref z2); SetDecorPos(selIdx, x1, z1, 0); SetDecorPos(selIdx, x2, z2, 1); }
                        else
                        {
                            float x = d.v[0], z = -d.v[1]; XZ(ref x, ref z); SetDecorPos(selIdx, x, z);
                            if (d.v.Length >= 3) { d.v[2] = EditorGUILayout.FloatField("Angle / size (a)", d.v[2]); if (link <= -2) m.props[-link - 2].a = d.v[2]; }
                        }
                        break;
                    }
            }
            if (EditorGUI.EndChangeCheck()) { undo.Add(before); redo.Clear(); dirty = true; if (TerrainCat(selCat)) terrainStale = true; Repaint(); }
            EditorGUILayout.Space(8);
            if (GUILayout.Button("Delete")) { Record(); Delete(m); }
        }
        static void XZ(ref float x, ref float z) { var v = EditorGUILayout.Vector2Field("Position (x, z)", new Vector2(x, z)); x = v.x; z = v.y; }
        void SwapPoints(Mission m, int a, int b)
        {
            Record(); (m.points[a], m.points[b]) = (m.points[b], m.points[a]);
            foreach (var f in m.forces0.Concat(m.forces1)) { if (f.loc == a) f.loc = b; else if (f.loc == b) f.loc = a; }
            selIdx = b;
        }

        void Palette()
        {
            EditorGUILayout.HelpBox("Drag a piece onto the map (or click it to drop it at the map centre).", MessageType.None);
            Group("Gameplay", new[] { ("point", "Objective") });
            Group("Terrain", new[] { ("hill", "Hill"), ("forest", "Forest"), ("village", "Village (auto houses)"), ("orchard", "Orchard"), ("trench", "Trench line"), ("bridge", "Bridge (on the river)") });
            Group("Routes", new[] { ("road", "Road"), ("lane", "Paved lane / street"), ("rail", "Railway") });
            Group("Buildings", BuildKinds.Select(k => ("build:" + k, k)).Concat(new[] { ("landmark:church", "church (landmark)"), ("landmark:windmill", "windmill (landmark)") }).ToArray());
            Group("Walls & lines", SegKinds.Select(k => ("seg:" + k, k)).ToArray());
            Group("Props", PropKinds.Select(k => ("prop:" + k, k)).ToArray());
        }
        readonly Dictionary<string, bool> fold = new Dictionary<string, bool>();
        void Group(string title, (string id, string label)[] items)
        {
            if (!fold.ContainsKey(title)) fold[title] = title != "Props" && title != "Walls & lines";
            fold[title] = EditorGUILayout.Foldout(fold[title], title, true); if (!fold[title]) return;
            foreach (var (id, label) in items)
            {
                var r = GUILayoutUtility.GetRect(new GUIContent(label), paletteBtn); var e = Event.current;
                if (e.type == EventType.MouseDrag && r.Contains(e.mousePosition - e.delta)) { DragAndDrop.PrepareStartDrag(); DragAndDrop.SetGenericData("fwmap", id); DragAndDrop.objectReferences = new UnityEngine.Object[0]; DragAndDrop.StartDrag(label); e.Use(); }
                if (GUI.Button(r, "  ⠿  " + label, paletteBtn)) { var c = S2W(canvas.center); Add(M, id, Snap(c.x), Snap(c.y)); }
            }
        }

        void MissionPanel(Mission m)
        {
            EditorGUI.BeginChangeCheck(); string before = JsonUtility.ToJson(m);
            m.name = EditorGUILayout.TextField("Name", m.name); m.date = EditorGUILayout.TextField("Date", m.date);
            EditorGUILayout.LabelField("Briefing"); m.brief = EditorGUILayout.TextArea(m.brief, GUILayout.MinHeight(60));
            m.attacker = EditorGUILayout.Popup("Attacker", m.attacker, new[] { "Wehrmacht", "Polish Army" });
            m.tickets = EditorGUILayout.IntField("Reinforcements (attacker)", m.tickets); m.time = EditorGUILayout.FloatField("Time limit (s)", m.time);
            m.half = EditorGUILayout.FloatField("Map half-size (m)", m.half); m.relief = EditorGUILayout.Slider("Terrain relief", m.relief, 0, 30); m.seed = EditorGUILayout.IntField("Terrain seed", m.seed);
            m.city = EditorGUILayout.Toggle("City (street grid)", m.city);
            EditorGUILayout.Space(4); EditorGUILayout.LabelField("Water", EditorStyles.boldLabel);
            m.hasRiver = EditorGUILayout.Toggle("River", m.hasRiver);
            if (m.hasRiver) { m.rz0 = EditorGUILayout.FloatField("  centre z", m.rz0); m.ramp = EditorGUILayout.FloatField("  meander amplitude", m.ramp); m.rk = EditorGUILayout.FloatField("  meander frequency", m.rk); m.rw = EditorGUILayout.FloatField("  width", m.rw); m.rlevel = EditorGUILayout.FloatField("  water level", m.rlevel); }
            m.hasSea = EditorGUILayout.Toggle("Sea", m.hasSea); if (m.hasSea) { m.seaHx = EditorGUILayout.FloatField("  shore x", m.seaHx); m.seaHz = EditorGUILayout.FloatField("  shore z", m.seaHz); }
            if (EditorGUI.EndChangeCheck()) { undo.Add(before); redo.Clear(); dirty = true; terrainStale = true; }
            EditorGUILayout.Space(6); Forces(m, 0); Forces(m, 1);
            EditorGUILayout.Space(6); Waves(m);
        }
        void Forces(Mission m, int team)
        {
            EditorGUILayout.LabelField((team == 0 ? "Wehrmacht" : "Polish Army") + " starting forces", EditorStyles.boldLabel);
            var locs = new[] { "spawn" }.Concat(m.points.Select((p, i) => (i + 1) + ". " + p.n)).ToArray(); var list = m.Forces(team).ToList(); int del = -1;
            EditorGUI.BeginChangeCheck(); string before = JsonUtility.ToJson(m);
            for (int i = 0; i < list.Count; i++)
            {
                var f = list[i]; GUILayout.BeginHorizontal();
                int ti = Mathf.Max(0, Array.IndexOf(UnitTypes, f.type)); f.type = UnitTypes[EditorGUILayout.Popup(ti, UnitTypes, GUILayout.Width(60))];
                f.n = EditorGUILayout.IntField(f.n, GUILayout.Width(32)); f.loc = EditorGUILayout.Popup(f.loc + 1, locs) - 1; if (GUILayout.Button("×", GUILayout.Width(20))) del = i;
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("+ add force")) list.Add(new FForce { type = "rifle", n = 4, loc = -1, sp = 8 });
            if (del >= 0) list.RemoveAt(del);
            if (EditorGUI.EndChangeCheck() || del >= 0 || list.Count != m.Forces(team).Length) { undo.Add(before); redo.Clear(); dirty = true; if (team == 0) m.forces0 = list.ToArray(); else m.forces1 = list.ToArray(); }
        }
        void Waves(Mission m)
        {
            EditorGUILayout.LabelField("Reinforcement waves (AI)", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck(); string before = JsonUtility.ToJson(m);
            for (int i = 0; i < m.waves.Length; i++)
            {
                var w = m.waves[i]; EditorGUILayout.LabelField(i == 0 ? "Wehrmacht" : "Polish Army", EditorStyles.miniBoldLabel);
                GUILayout.BeginHorizontal(); w.t0 = EditorGUILayout.FloatField("first (s)", w.t0); w.every = EditorGUILayout.FloatField("every (s)", w.every); GUILayout.EndHorizontal();
                foreach (var c in w.comp ?? new FComp[0]) { GUILayout.BeginHorizontal(); GUILayout.Space(12); int ti = Mathf.Max(0, Array.IndexOf(UnitTypes, c.type)); c.type = UnitTypes[EditorGUILayout.Popup(ti, UnitTypes, GUILayout.Width(70))]; c.n = EditorGUILayout.IntField(c.n, GUILayout.Width(40)); GUILayout.EndHorizontal(); }
            }
            if (EditorGUI.EndChangeCheck()) { undo.Add(before); redo.Clear(); dirty = true; }
        }
        void LayerPanel()
        {
            foreach (var c in Layers) show[c] = EditorGUILayout.ToggleLeft(c.ToString(), show[c]);
            EditorGUILayout.Space(8); EditorGUILayout.HelpBox("The terrain image is baked from the game's own height function (hills, relief, river, sea, rails, trenches), so it matches what you'll play on.", MessageType.None);
            if (GUILayout.Button("Re-bake terrain")) terrainStale = true;
        }

        // ================= play-test =================
        void PlayTest(int side)
        {
            if (dirty) Save();
            pendingPlay = mi; pendingSide = side;
            if (!EditorApplication.isPlaying) EditorApplication.isPlaying = true;
            EditorApplication.update -= PollPlay; EditorApplication.update += PollPlay;
        }
        static void PollPlay()
        {
            if (pendingPlay < 0) { EditorApplication.update -= PollPlay; return; }
            if (!EditorApplication.isPlaying) return; var g = UnityEngine.Object.FindAnyObjectByType<Game>(); if (!g) return;
            int id = pendingPlay; pendingPlay = -1; EditorApplication.update -= PollPlay;
            EditorApplication.delayCall += () => { if (g) g.StartMission(id, pendingSide); };
        }
    }
}
