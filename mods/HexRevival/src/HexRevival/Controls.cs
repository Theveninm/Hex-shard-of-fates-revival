using System;
using System.Collections.Generic;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// IMGUI controls of the mod's windows drawn over the game (AI settings of the deck editor, AI decks manager), in
    /// the look of the game (see GameSkin). Each returns the new value; Changed is set when a control changed one.
    /// </summary>
    public static class Controls
    {
        /// <summary>Set by any control that changed its value (reset by the caller).</summary>
        public static bool Changed;

        public static readonly string[] Skills = Enum.GetNames(typeof(Game.Shared.AI.EDifficulty));

        /// <summary>Shard colors of Opponents.Handicap.Shards, with their icon in the game's card filter.</summary>
        public static readonly string[] ShardSprites = { "filterbutton_blood_glow_da", "filterbutton_diamond_glow_da", "filterbutton_ruby_glow_da", "filterbutton_sapphire_glow_da", "filterbutton_wild_glow_da" };

        /// <summary>Darkened screen and a framed window with its title bar; returns the window.</summary>
        public static Rect Window(float width, float height, string title)
        {
            float S = GameSkin.Scale;
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            float w = Mathf.Min(width * S, Screen.width - 24f), h = Mathf.Min(height * S, Screen.height - 24f);
            Rect win = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f + 10f * S, w, h);
            if (!GameSkin.Sprite(win, "A_Frames", "Frame_With_Header_Hard_Corner"))
                GUI.DrawTexture(win, GameSkin.Solid(new Color(0.06f, 0.07f, 0.1f, 0.97f)));
            Rect t = new Rect(win.center.x - 260 * S, win.y - 22 * S, 520 * S, 64 * S);
            GameSkin.Sprite(t, "A_Frames", "Frame_Title_Bar");
            GameSkin.Label(t, title, GameSkin.Style("title", 28, GameSkin.Gold, TextAnchor.MiddleCenter));
            return win;
        }

        /// <summary>An inner frame with a title.</summary>
        public static void Frame(Rect r, string title)
        {
            float S = GameSkin.Scale;
            GameSkin.Sprite(r, "A_Frames", "Frame_Generic_Inner");
            if (!string.IsNullOrEmpty(title))
                GameSkin.Label(new Rect(r.x + 14 * S, r.y + 8 * S, r.width - 28 * S, 30 * S), title, GameSkin.Style("title", 19, GameSkin.Gold));
        }

        /// <summary>A scrolling list of rows (row i at y = i * (rowH + 4)); draw returns true when row i is clicked.</summary>
        public static Vector2 List(Rect area, Vector2 scroll, int count, float rowH, Func<int, bool> draw, Action<int> clicked)
        {
            float S = GameSkin.Scale;
            Rect view = new Rect(0, 0, area.width - 20 * S, count * (rowH + 4 * S));
            scroll = GUI.BeginScrollView(area, scroll, view, false, false, GUIStyle.none, view.height > area.height ? GameSkin.ScrollBar : GUIStyle.none);
            // Only the visible rows are drawn (lists of every card of the game).
            int first = Mathf.Max(0, (int)(scroll.y / (rowH + 4 * S)) - 1), last = Mathf.Min(count, first + (int)(area.height / (rowH + 4 * S)) + 3);
            for (int i = first; i < last; i++) if (draw(i)) clicked(i);
            GUI.EndScrollView();
            return scroll;
        }

        public static string TextField(Rect r, string value, int max = 60)
        {
            GameSkin.Sprite(r, "A_Frames", "Frame_Inner");
            GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), GameSkin.Solid(new Color(0, 0, 0, 0.45f)));
            string v = GUI.TextField(r, value ?? "", max, GameSkin.Field);
            if (v != value) Changed = true;
            return v;
        }

        /// <summary>Label, then "- value +" at the right.</summary>
        public static int Stepper(Rect r, string label, int value, int step, int min, int max, bool signed = false)
        {
            float S = GameSkin.Scale, bw = 34 * S;
            GUI.Label(new Rect(r.x, r.y, r.width - 2 * bw - 70 * S, r.height), label, GameSkin.Style("body", 14, GameSkin.Text));
            Rect minus = new Rect(r.xMax - 2 * bw - 64 * S, r.y, bw, r.height);
            Rect val = new Rect(minus.xMax, r.y, 64 * S, r.height);
            Rect plus = new Rect(val.xMax, r.y, bw, r.height);
            int v = value;
            if (Small(minus, "-")) v = Math.Max(min, value - step);
            GameSkin.Label(val, (signed && value > 0 ? "+" : "") + value, GameSkin.Style("bold", 16, GameSkin.Text, TextAnchor.MiddleCenter));
            if (Small(plus, "+")) v = Math.Min(max, value + step);
            if (v != value) Changed = true;
            return v;
        }

        /// <summary>Label, then "&lt; value &gt;" cycling through the options.</summary>
        public static string Cycle(Rect r, string label, string value, string[] options, Func<string, string> show = null)
        {
            float S = GameSkin.Scale, bw = 34 * S, lw = string.IsNullOrEmpty(label) ? 0 : Mathf.Min(r.width * 0.4f, 170 * S);
            if (lw > 0) GUI.Label(new Rect(r.x, r.y, lw, r.height), label, GameSkin.Style("body", 14, GameSkin.Text));
            int i = Array.IndexOf(options, value);
            Rect prev = new Rect(r.x + lw, r.y, bw, r.height), next = new Rect(r.xMax - bw, r.y, bw, r.height);
            Rect val = new Rect(prev.xMax, r.y, next.x - prev.xMax, r.height);
            string res = value;
            if (Small(prev, "<") && options.Length > 0) res = options[(i <= 0 ? options.Length : i) - 1];
            GameSkin.Label(val, show != null ? show(value ?? "") : value ?? "", GameSkin.Style("bold", 15, GameSkin.Text, TextAnchor.MiddleCenter));
            if (Small(next, ">") && options.Length > 0) res = options[(i + 1) % options.Length];
            if (res != value) Changed = true;
            return res;
        }

        /// <summary>Small square button (the title font has no "+", "-", "&lt;", "&gt;": the body font is used).</summary>
        public static bool Small(Rect r, string symbol)
        {
            bool enabled = GUI.enabled, hover = enabled && r.Contains(Event.current.mousePosition);
            GameSkin.Sprite(r, "A_Frames", "Frame_Inner");
            GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), GameSkin.Solid(hover ? new Color(0.35f, 0.28f, 0.15f, 0.9f) : new Color(0.12f, 0.1f, 0.07f, 0.9f)));
            GameSkin.Label(r, symbol, GameSkin.Style("bold", 20, enabled ? (hover ? Color.white : GameSkin.Gold) : GameSkin.Muted, TextAnchor.MiddleCenter));
            return enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        public static bool Toggle(Rect r, string label, bool value)
        {
            float S = GameSkin.Scale;
            Rect box = new Rect(r.x, r.y + (r.height - 26 * S) / 2f, 26 * S, 26 * S);
            GameSkin.Sprite(box, "A_Frames", "Frame_Inner");
            GUI.DrawTexture(new Rect(box.x + 2, box.y + 2, box.width - 4, box.height - 4), GameSkin.Solid(new Color(0, 0, 0, 0.5f)));
            if (value) GUI.DrawTexture(new Rect(box.x + 6 * S, box.y + 6 * S, box.width - 12 * S, box.height - 12 * S), GameSkin.Solid(GameSkin.Gold));
            GUI.Label(new Rect(box.xMax + 10 * S, r.y, r.width - box.width - 10 * S, r.height), label, GameSkin.Style("body", 14, GameSkin.Text));
            bool v = GUI.Button(r, GUIContent.none, GUIStyle.none) ? !value : value;
            if (v != value) Changed = true;
            return v;
        }

        /// <summary>
        /// Starting shards: one icon per color (Opponents.Handicap.Shards order), "+" and "-" above it, the count
        /// written over the icon.
        /// </summary>
        public static void Shards(Rect r, int[] shards)
        {
            float S = GameSkin.Scale, cell = r.width / 5f, icon = Mathf.Min(cell - 8 * S, r.height - 34 * S), bw = Mathf.Min(28 * S, cell / 2f - 2 * S);
            for (int i = 0; i < 5; i++)
            {
                float cx = r.x + cell * i + cell / 2f;
                Rect minus = new Rect(cx - bw - 1 * S, r.y, bw, 28 * S), plus = new Rect(cx + 1 * S, r.y, bw, 28 * S);
                if (Small(minus, "-") && shards[i] > 0) { shards[i]--; Changed = true; }
                if (Small(plus, "+") && shards[i] < 20) { shards[i]++; Changed = true; }
                Rect ic = new Rect(cx - icon / 2f, r.y + 32 * S, icon, icon);
                GameSkin.Sprite(ic, "A_Buttons", ShardSprites[i], shards[i] > 0 ? 1f : 0.35f, false);
                if (shards[i] > 0)
                    GameSkin.Label(ic, shards[i].ToString(), GameSkin.Style("bold", 22, Color.white, TextAnchor.MiddleCenter));
            }
        }

        /// <summary>
        /// Blocks the game's interface (its NGUI cameras) while the mouse is over one of the given areas, or always
        /// when modal: clicks on the mod's windows must not reach the game's buttons underneath.
        /// </summary>
        public class UiBlocker
        {
            private readonly List<Behaviour> m_Disabled = new List<Behaviour>();

            public void Update(bool modal, params Rect[] areas)
            {
                bool block = modal;
                Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                if (!block) foreach (Rect a in areas) if (a.width > 0 && a.Contains(mouse)) { block = true; break; }
                if (block && m_Disabled.Count == 0)
                {
                    foreach (UICamera c in UICamera.list.ToArray())
                        if (c != null && c.enabled) { c.enabled = false; m_Disabled.Add(c); }
                }
                else if (!block && m_Disabled.Count > 0) Release();
            }

            public void Release()
            {
                foreach (Behaviour b in m_Disabled) if (b != null) b.enabled = true;
                m_Disabled.Clear();
            }
        }
    }
}
