// Fall Weiss — IMGUI kit in the style of the original index.html (olive panels, #ffd96a gold, Courier New),
// with a Bahnschrift display face, 9-slice bordered panels/buttons, gradients and the two army emblems.
using UnityEngine;

namespace FallWeiss
{
    public static class UI
    {
        public static readonly Color Text = FU.Hex(0xe6e2c8), Gold = FU.Hex(0xffd96a), Olive = FU.Hex(0x7d8c57), Dim = FU.Hex(0x9aa57a), Red = FU.Hex(0xe05a4a), Green = FU.Hex(0x6bd36b);
        public static Font body, head;
        public static GUIStyle line, label, small, title, h1, h2, h3, button, bigButton, card, cardSel, panel, pill, center, ammo;
        // aligned / sized variants, built once: the HUD used to allocate ~30 GUIStyles per OnGUI call (two to four calls a frame)
        public static GUIStyle lineC, lineR, smallC, smallR, smallBoldC, h2R, h2R20, h2C, h2UC, h2s18, h2s19, h2s21, h2s24, h2s30, h2s34, h1s54, h1s72, h3s20, titleBig, label15, ammo22, tab, tabOn, cardBtn, trayBtn;
        public static Texture2D white, gradL, gradB, gradT, emblemG, emblemP;
        static bool ready;

        static Texture2D Tex(int w, int h, System.Func<int, int, Color> f, bool point = true)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = point ? FilterMode.Point : FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h]; for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = f(x, y); t.SetPixels(px); t.Apply(); return t;
        }
        static Texture2D Box(Color bg, Color border, int bw = 1) => Tex(8, 8, (x, y) => (x < bw || y < bw || x >= 8 - bw || y >= 8 - bw) ? border : bg);
        static GUIStyle St(GUIStyle b, Font f, int size, Color c, TextAnchor a = TextAnchor.UpperLeft, FontStyle fs = FontStyle.Normal)
        {
            var s = new GUIStyle(b) { font = f, fontSize = size, alignment = a, fontStyle = fs, wordWrap = true, richText = true };
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = c; return s;
        }

        public static void Ensure()
        {
            if (ready && gradT && emblemG && panel != null && panel.normal.background && body) return; ready = true;
            body = Font.CreateDynamicFontFromOSFont(new[] { "Courier New", "Consolas", "Lucida Console", "Arial" }, 16);
            head = Font.CreateDynamicFontFromOSFont(new[] { "Bahnschrift", "Bahnschrift SemiBold", "Segoe UI", "Arial" }, 32);
            white = Texture2D.whiteTexture;
            gradL = Tex(256, 1, (x, y) => new Color(.03f, .035f, .025f, Mathf.Lerp(.92f, 0, x / 255f)), false);
            gradB = Tex(1, 128, (x, y) => new Color(0, 0, 0, Mathf.Lerp(.75f, 0, y / 127f)), false);
            gradT = Tex(1, 128, (x, y) => new Color(.04f, .047f, .03f, Mathf.Lerp(0, .78f, y / 127f)), false);
            emblemG = Tex(64, 64, (x, y) =>
            {   // Balkenkreuz: white outline cross with black core
                int cx = Mathf.Abs(x - 32), cy = Mathf.Abs(y - 32); bool outer = (cx < 9 && cy < 28) || (cy < 9 && cx < 28); bool inner = (cx < 5 && cy < 24) || (cy < 5 && cx < 24);
                return inner ? new Color(.06f, .06f, .06f) : outer ? new Color(.93f, .93f, .9f) : Color.clear;
            });
            emblemP = Tex(64, 64, (x, y) =>
            {   // Polish air-force style checkerboard with red/white squares
                int cx = Mathf.Abs(x - 32), cy = Mathf.Abs(y - 32); if (cx > 26 || cy > 26) return Color.clear; if (cx > 24 || cy > 24) return new Color(.9f, .88f, .82f);
                bool w = ((x - 6) / 13 + (y - 6) / 13) % 2 == 0; return w ? new Color(.93f, .92f, .88f) : new Color(.78f, .12f, .14f);
            });
            label = St(GUI.skin.label, body, 16, Text); small = St(GUI.skin.label, body, 15, Text); center = St(label, body, 15, Text, TextAnchor.MiddleCenter);
            line = new GUIStyle(small) { wordWrap = false, clipping = TextClipping.Clip };
            title = St(GUI.skin.label, head, 64, Gold, TextAnchor.UpperLeft, FontStyle.Bold); title.wordWrap = false;
            h1 = St(GUI.skin.label, head, 40, Text, TextAnchor.MiddleCenter, FontStyle.Bold); h2 = St(GUI.skin.label, head, 24, Gold, TextAnchor.UpperLeft, FontStyle.Bold); h3 = St(GUI.skin.label, head, 17, Dim, TextAnchor.UpperLeft);
            ammo = St(GUI.skin.label, head, 34, Text, TextAnchor.LowerRight, FontStyle.Bold);
            panel = new GUIStyle { border = new RectOffset(2, 2, 2, 2), padding = new RectOffset(14, 14, 12, 12) }; panel.normal.background = Box(new Color(.086f, .102f, .07f, .86f), new Color(.42f, .48f, .29f, 1));
            button = St(GUI.skin.button, body, 15, Text, TextAnchor.MiddleCenter, FontStyle.Bold); button.border = new RectOffset(2, 2, 2, 2); button.padding = new RectOffset(10, 10, 6, 6);
            button.normal.background = Box(FU.Hex(0x2c3320), Olive); button.hover.background = Box(FU.Hex(0x455130), Gold); button.active.background = Box(FU.Hex(0x5a6a3a), Gold); button.focused.background = button.normal.background;
            button.hover.textColor = Gold;
            bigButton = new GUIStyle(button) { fontSize = 22, font = head }; bigButton.normal.background = Box(FU.Hex(0x3a4426), Gold, 2); bigButton.hover.background = Box(FU.Hex(0x56652f), Gold, 2); bigButton.normal.textColor = Gold;
            card = new GUIStyle(panel) { padding = new RectOffset(14, 12, 10, 10) }; card.hover.background = Box(new Color(.16f, .19f, .12f, .92f), Olive); card.normal.background = Box(new Color(.086f, .102f, .07f, .82f), new Color(.3f, .35f, .22f, 1));
            cardSel = new GUIStyle(card); cardSel.normal.background = cardSel.hover.background = Box(new Color(.235f, .26f, .15f, .94f), Gold, 2);
            pill = new GUIStyle { border = new RectOffset(1, 1, 1, 1) };
            lineC = new GUIStyle(line) { alignment = TextAnchor.UpperCenter }; lineR = new GUIStyle(line) { alignment = TextAnchor.UpperRight };
            smallC = new GUIStyle(small) { alignment = TextAnchor.UpperCenter }; smallR = new GUIStyle(small) { alignment = TextAnchor.UpperRight }; smallBoldC = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            h2R = new GUIStyle(h2) { alignment = TextAnchor.UpperRight }; h2R20 = new GUIStyle(h2) { alignment = TextAnchor.UpperRight, fontSize = 20 }; h2C = new GUIStyle(h2) { alignment = TextAnchor.MiddleCenter }; h2UC = new GUIStyle(h2) { alignment = TextAnchor.UpperCenter };
            h2s18 = new GUIStyle(h2) { fontSize = 18, wordWrap = false }; h2s19 = new GUIStyle(h2) { fontSize = 19, wordWrap = false }; h2s21 = new GUIStyle(h2) { fontSize = 21 }; h2s24 = new GUIStyle(h2) { fontSize = 24 }; h2s30 = new GUIStyle(h2) { fontSize = 30 }; h2s34 = new GUIStyle(h2) { fontSize = 34 };
            h1s54 = new GUIStyle(h1) { fontSize = 54 }; h1s72 = new GUIStyle(h1) { fontSize = 72 }; h3s20 = new GUIStyle(h3) { fontSize = 20 }; titleBig = new GUIStyle(title) { fontSize = 86 }; label15 = new GUIStyle(label) { fontSize = 15 }; ammo22 = new GUIStyle(ammo) { fontSize = 22 };
            tab = new GUIStyle(button) { fontSize = 15 }; tab.normal.background = Box(new Color(.1f, .12f, .08f, .8f), new Color(.3f, .35f, .22f, 1)); tab.normal.textColor = Dim;
            tabOn = new GUIStyle(tab); tabOn.normal.background = tabOn.hover.background = Box(FU.Hex(0x3a4426), Gold, 2); tabOn.normal.textColor = tabOn.hover.textColor = Gold;
            cardBtn = new GUIStyle(button) { fontSize = 14, padding = new RectOffset(4, 4, 4, 4), wordWrap = false };
            trayBtn = new GUIStyle(button) { fontSize = 13, padding = new RectOffset(5, 5, 3, 3), wordWrap = false, alignment = TextAnchor.MiddleLeft };
        }

        // ---------------- small widgets shared by the pause screen and the main menu ----------------
        /// A row of tabs; returns the selected index.
        public static int Tabs(Rect r, string[] labels, int sel, float gap = 4)
        {
            float w = (r.width - gap * (labels.Length - 1)) / labels.Length;
            for (int i = 0; i < labels.Length; i++) if (GUI.Button(new Rect(r.x + i * (w + gap), r.y, w, r.height), labels[i], i == sel ? tabOn : tab)) sel = i;
            return sel;
        }
        /// Label on the left, value on the right, slider between. Returns the new value.
        public static float Slider(float x, float y, float w, string label, float v, float lo, float hi, string fmt)
        {
            Shadow(new Rect(x, y, w * .5f, 20), label, line, Text); Shadow(new Rect(x + w - 64, y, 64, 20), v.ToString(fmt), smallR, Gold);
            return GUI.HorizontalSlider(new Rect(x + w * .52f, y + 6, w * .48f - 72, 14), v, lo, hi);
        }
        /// Label on the left, ON/OFF button on the right. Returns the new value.
        public static bool Toggle(float x, float y, float w, string label, bool v)
        {
            Shadow(new Rect(x, y + 2, w - 110, 22), label, line, Text);
            if (GUI.Button(new Rect(x + w - 100, y, 100, 26), v ? "ON" : "OFF", v ? tabOn : tab)) v = !v; return v;
        }
        /// Label on the left, a set of exclusive choices on the right. Returns the selected index.
        public static int Choice(float x, float y, float w, string label, string[] opts, int sel)
        {
            Shadow(new Rect(x, y + 2, w * .4f, 22), label, small, Text); float ow = (w * .6f - 4 * (opts.Length - 1)) / opts.Length;
            for (int i = 0; i < opts.Length; i++) if (GUI.Button(new Rect(x + w * .4f + i * (ow + 4), y, ow, 26), opts[i], i == sel ? tabOn : tab)) sel = i;
            return sel;
        }

        public static void Fill(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = o; }
        public static void Frame(Rect r, Color c, float w = 1) { Fill(new Rect(r.x, r.y, r.width, w), c); Fill(new Rect(r.x, r.yMax - w, r.width, w), c); Fill(new Rect(r.x, r.y, w, r.height), c); Fill(new Rect(r.xMax - w, r.y, w, r.height), c); }
        public static void Tex(Rect r, Texture t, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, t, ScaleMode.StretchToFill); GUI.color = o; }
        /// Text with a soft dark shadow (like the original's text-shadow).
        public static void Shadow(Rect r, string s, GUIStyle st, Color c, float alpha = 1)
        {
            var o = st.normal.textColor; st.normal.textColor = new Color(0, 0, 0, .75f * alpha);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), s, st); st.normal.textColor = new Color(c.r, c.g, c.b, c.a * alpha); GUI.Label(r, s, st); st.normal.textColor = o;
        }
        public static string Spaced(string s) { var sb = new System.Text.StringBuilder(); foreach (var ch in s) { sb.Append(ch); if (ch != ' ') sb.Append(' '); } return sb.ToString(); }
        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
        static readonly GUIContent gc = new GUIContent();
        /// Shortens `s` with an ellipsis until it fits `w` pixels in style `st` (so labels never spill or get cut mid-word).
        public static string Fit(string s, GUIStyle st, float w)
        {
            if (string.IsNullOrEmpty(s)) return s; gc.text = s; if (st.CalcSize(gc).x <= w) return s;
            for (int n = s.Length - 1; n > 1; n--) { gc.text = s.Substring(0, n).TrimEnd(' ', '·', '.') + "…"; if (st.CalcSize(gc).x <= w) return gc.text; }
            return "…";
        }
        /// Height of word-wrapped text at width `w`.
        public static float TextH(string s, GUIStyle st, float w) { gc.text = s; return st.CalcHeight(gc, w); }
    }
}
