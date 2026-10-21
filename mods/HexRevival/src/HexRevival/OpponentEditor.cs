using System;
using System.Collections.Generic;
using Game.Shared;
using Game.Shared.AI;
using Game.Shared.Mechanics;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Editor of the AI opponent packs (see Opponents), opened from the Quick Match menu: the packs of the
    /// HexRevivalOpponents folder (the account's pack chosen here), their tiers, and each opponent's deck, AI skill,
    /// personality and handicaps. The default pack is read-only: copy it to change it. Changes are saved when
    /// another pack is chosen and when the editor is closed. Same look as the Quick Match menu (see GameSkin).
    /// </summary>
    public class OpponentEditor : MonoBehaviour
    {
        private static OpponentEditor s_Instance;
        private bool m_Open;
        private readonly List<Behaviour> m_DisabledUi = new List<Behaviour>();

        private List<string> m_Packs = new List<string>();
        private string m_PackName;
        private Opponents.Pack m_Pack;
        private bool m_Dirty;
        private int m_Tier = -1, m_Opp = -1;
        private Vector2 m_ScrollPacks, m_ScrollTiers, m_ScrollOpps;
        private int m_ImportDeck;
        private string m_Message;

        private static readonly string[] Modes = { Opponents.Ladder, Opponents.Gauntlet, Opponents.Limited, Opponents.Arena };
        private static readonly string[] Skills = Enum.GetNames(typeof(EDifficulty));
        private static readonly string[] Personalities = Enum.GetNames(typeof(EDeckPersonality));

        private void Awake() { s_Instance = this; }

        public static void Open() { if (s_Instance != null) s_Instance.SetOpen(true); }

        public static bool IsOpen { get { return s_Instance != null && s_Instance.m_Open; } }

        private void SetOpen(bool open)
        {
            if (open == m_Open) return;
            if (!open) SaveIfDirty();
            m_Open = open;
            if (open)
            {
                m_Packs = Opponents.List();
                string active = Saves.Current != null && !string.IsNullOrEmpty(Saves.Current.OpponentPack) ? Saves.Current.OpponentPack : Opponents.DefaultPackName;
                Select(m_Packs.Contains(active) ? active : Opponents.DefaultPackName);
                foreach (UICamera c in UICamera.list.ToArray())
                    if (c != null && c.enabled) { c.enabled = false; m_DisabledUi.Add(c); }
            }
            else
            {
                foreach (Behaviour b in m_DisabledUi) if (b != null) b.enabled = true;
                m_DisabledUi.Clear();
            }
        }

        private void Update()
        {
            if (m_Open && Input.GetKeyDown(KeyCode.Escape)) SetOpen(false);
        }

        private bool ReadOnly { get { return m_PackName == Opponents.DefaultPackName; } }

        private void Select(string name)
        {
            SaveIfDirty();
            m_PackName = name;
            m_Pack = Opponents.Load(name) ?? new Opponents.Pack { Name = name };
            m_Tier = m_Pack.Tiers.Count > 0 ? 0 : -1;
            m_Opp = m_Tier >= 0 && m_Pack.Tiers[0].Opponents.Count > 0 ? 0 : -1;
            m_Message = null;
        }

        private void SaveIfDirty()
        {
            if (!m_Dirty || m_Pack == null || ReadOnly) return;
            Opponents.Save(m_Pack, m_PackName);
            m_Dirty = false;
        }

        private void Use()
        {
            if (Saves.Current == null) return;
            SaveIfDirty();
            Saves.Current.OpponentPack = m_PackName == Opponents.DefaultPackName ? "" : m_PackName;
            Saves.SaveCurrent();
        }

        private void Copy()
        {
            SaveIfDirty();
            string baseName = (m_PackName == Opponents.DefaultPackName ? "My opponents" : m_PackName + " copy");
            string name = baseName;
            for (int i = 2; m_Packs.Contains(name); i++) name = baseName + " " + i;
            Opponents.Pack p = Opponents.Load(m_PackName) ?? m_Pack;
            p.Name = name;
            p.Author = Saves.Current != null ? Saves.Current.Name : "";
            Opponents.Save(p, name);
            m_Packs = Opponents.List();
            Select(name);
        }

        // ------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------

        private void OnGUI()
        {
            if (!m_Open || m_Pack == null) return;
            float S = GameSkin.Scale;
            GUI.depth = -1100;
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float w = Mathf.Min(1280f * S, Screen.width - 24f), h = Mathf.Min(800f * S, Screen.height - 24f);
            Rect win = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f + 10f * S, w, h);
            if (!GameSkin.Sprite(win, "A_Frames", "Frame_With_Header_Hard_Corner"))
                GUI.DrawTexture(win, GameSkin.Solid(new Color(0.06f, 0.07f, 0.1f, 0.97f)));
            Rect title = new Rect(win.center.x - 260 * S, win.y - 22 * S, 520 * S, 64 * S);
            GameSkin.Sprite(title, "A_Frames", "Frame_Title_Bar");
            GameSkin.Label(title, Loc.Get("opp_title"), GameSkin.Style("title", 28, GameSkin.Gold, TextAnchor.MiddleCenter));

            float pad = 30 * S, gap = 16 * S, top = win.y + 60 * S, bottom = win.yMax - 84 * S;
            float colW = (win.width - 2 * pad - 2 * gap);
            Rect packs = new Rect(win.x + pad, top, colW * 0.22f, bottom - top);
            Rect tiers = new Rect(packs.xMax + gap, top, colW * 0.26f, bottom - top);
            Rect opps = new Rect(tiers.xMax + gap, top, win.xMax - pad - tiers.xMax - gap, bottom - top);
            DrawPacks(packs);
            bool ro = ReadOnly;
            DrawTiers(tiers, ro);
            DrawOpponents(opps, ro);

            float by = win.yMax - 70 * S;
            string info = ro ? Loc.Get("opp_readonly") : m_Message ?? Loc.Get("opp_hint");
            GUI.Label(new Rect(win.x + pad, by, win.width - 2 * pad - 220 * S, 48 * S), info, GameSkin.Style("body", 14, ro ? GameSkin.Gold : GameSkin.Muted, TextAnchor.MiddleLeft, true));
            if (GameSkin.RedButton(new Rect(win.xMax - pad - 190 * S, by, 190 * S, 52 * S), Loc.Get("close"))) SetOpen(false);
        }

        private void Frame(Rect r, string title)
        {
            float S = GameSkin.Scale;
            GameSkin.Sprite(r, "A_Frames", "Frame_Generic_Inner");
            GameSkin.Label(new Rect(r.x + 14 * S, r.y + 8 * S, r.width - 28 * S, 30 * S), title, GameSkin.Style("title", 19, GameSkin.Gold));
        }

        private void DrawPacks(Rect r)
        {
            float S = GameSkin.Scale;
            Frame(r, Loc.Get("opp_packs"));
            string active = Saves.Current != null && !string.IsNullOrEmpty(Saves.Current.OpponentPack) ? Saves.Current.OpponentPack : Opponents.DefaultPackName;
            float rowH = 56 * S;
            Rect area = new Rect(r.x + 10 * S, r.y + 44 * S, r.width - 20 * S, r.height - 44 * S - 132 * S);
            m_ScrollPacks = List(area, m_ScrollPacks, m_Packs.Count, rowH, i =>
                GameSkin.Row(new Rect(0, i * (rowH + 4 * S), area.width - 20 * S, rowH), m_Packs[i], m_Packs[i] == active ? Loc.Get("opp_in_use") : "", m_Packs[i] == m_PackName),
                i => Select(m_Packs[i]));
            float bw = r.width - 20 * S, by = r.yMax - 128 * S;
            if (GameSkin.GreenButton(new Rect(r.x + 10 * S, by, bw, 38 * S), Loc.Get("opp_use"), m_PackName != active, 17)) Use();
            if (GameSkin.GoldButton(new Rect(r.x + 10 * S, by + 42 * S, bw, 38 * S), Loc.Get("opp_copy"), true, 17)) Copy();
            if (GameSkin.GoldButton(new Rect(r.x + 10 * S, by + 84 * S, bw, 38 * S), Loc.Get("opp_folder"), true, 17))
                Application.OpenURL("file:///" + Opponents.Folder.Replace('\\', '/'));
        }

        private void DrawTiers(Rect r, bool ro)
        {
            float S = GameSkin.Scale;
            Frame(r, Loc.Get("opp_tiers"));
            List<Opponents.Tier> tiers = m_Pack.Tiers;
            float rowH = 52 * S, editH = 200 * S;
            Rect area = new Rect(r.x + 10 * S, r.y + 44 * S, r.width - 20 * S, r.height - 44 * S - editH - 48 * S);
            m_ScrollTiers = List(area, m_ScrollTiers, tiers.Count, rowH, i =>
                GameSkin.Row(new Rect(0, i * (rowH + 4 * S), area.width - 20 * S, rowH), tiers[i].Name,
                    Loc.Get("mode_" + tiers[i].Mode) + " " + tiers[i].From + "-" + tiers[i].To + " · " + tiers[i].Opponents.Count, i == m_Tier),
                i => { m_Tier = i; m_Opp = tiers[i].Opponents.Count > 0 ? 0 : -1; });
            GUI.enabled = !ro;
            float by = area.yMax + 6 * S, half = (r.width - 26 * S) / 2f;
            if (GameSkin.GoldButton(new Rect(r.x + 10 * S, by, half, 36 * S), Loc.Get("opp_add"), !ro, 16))
            {
                tiers.Add(new Opponents.Tier { Name = "Tier " + (tiers.Count + 1), Mode = m_Tier >= 0 ? tiers[m_Tier].Mode : Opponents.Ladder, To = 99 });
                m_Tier = tiers.Count - 1; m_Opp = -1; m_Dirty = true;
            }
            if (GameSkin.RedButton(new Rect(r.x + 16 * S + half, by, half, 36 * S), Loc.Get("delete"), 16) && !ro && m_Tier >= 0)
            {
                tiers.RemoveAt(m_Tier); m_Tier = Mathf.Min(m_Tier, tiers.Count - 1); m_Opp = -1; m_Dirty = true;
            }
            if (m_Tier >= 0 && m_Tier < tiers.Count)
            {
                Opponents.Tier t = tiers[m_Tier];
                float y = r.yMax - editH;
                float lw = r.width - 20 * S;
                t.Name = TextField(new Rect(r.x + 10 * S, y, lw, 36 * S), t.Name);
                y += 42 * S;
                t.Mode = Cycle(new Rect(r.x + 10 * S, y, lw, 34 * S), Loc.Get("opp_mode"), t.Mode, Modes, m => Loc.Get("mode_" + m));
                y += 40 * S;
                t.From = Stepper(new Rect(r.x + 10 * S, y, lw, 34 * S), Loc.Get(t.Mode == Opponents.Ladder ? "opp_from_stars" : t.Mode == Opponents.Arena ? "opp_from_tier" : "opp_from_wins"), t.From, 1, 0, 9999);
                y += 40 * S;
                t.To = Stepper(new Rect(r.x + 10 * S, y, lw, 34 * S), Loc.Get("opp_to"), t.To, 1, t.From, 9999);
            }
            GUI.enabled = true;
        }

        private void DrawOpponents(Rect r, bool ro)
        {
            float S = GameSkin.Scale;
            Frame(r, Loc.Get("opp_opponents"));
            if (m_Tier < 0 || m_Tier >= m_Pack.Tiers.Count) return;
            List<Opponents.Opponent> list = m_Pack.Tiers[m_Tier].Opponents;
            float rowH = 46 * S;
            Rect area = new Rect(r.x + 10 * S, r.y + 44 * S, r.width - 20 * S, r.height * 0.30f);
            m_ScrollOpps = List(area, m_ScrollOpps, list.Count, rowH, i =>
                GameSkin.Row(new Rect(0, i * (rowH + 4 * S), area.width - 20 * S, rowH), list[i].Name, Summary(list[i]), i == m_Opp),
                i => m_Opp = i);
            GUI.enabled = !ro;
            float by = area.yMax + 6 * S, bw = (r.width - 32 * S) / 3f;
            if (GameSkin.GoldButton(new Rect(r.x + 10 * S, by, bw, 36 * S), Loc.Get("opp_add"), !ro, 16))
            {
                Opponents.Opponent o = new Opponents.Opponent { Name = "Opponent " + (list.Count + 1) };
                List<DeckTemplate> pre = Decks.PrebuiltPvp();
                if (pre.Count > 0) { o.Prebuilt = pre[0].m_DeckName; o.Name = pre[0].m_DeckName; }
                list.Add(o); m_Opp = list.Count - 1; m_Dirty = true;
            }
            if (GameSkin.GoldButton(new Rect(r.x + 16 * S + bw, by, bw, 36 * S), Loc.Get("opp_duplicate"), !ro && m_Opp >= 0, 16))
            {
                byte[] b = null;
                Game.Shared.Network.HConnect.Utils.SerializeJson(list[m_Opp], (byte[] x) => { b = x; });
                Game.Shared.Network.HConnect.Utils.DeserializeJson(b, (Opponents.Opponent x) => { x.Name += " (2)"; list.Insert(m_Opp + 1, x); });
                m_Opp++; m_Dirty = true;
            }
            if (GameSkin.RedButton(new Rect(r.x + 22 * S + 2 * bw, by, bw, 36 * S), Loc.Get("delete"), 16) && !ro && m_Opp >= 0)
            {
                list.RemoveAt(m_Opp); m_Opp = Mathf.Min(m_Opp, list.Count - 1); m_Dirty = true;
            }
            if (m_Opp >= 0 && m_Opp < list.Count) DrawOpponent(new Rect(r.x + 10 * S, by + 46 * S, r.width - 20 * S, r.yMax - by - 54 * S), list[m_Opp]);
            GUI.enabled = true;
        }

        private static string Summary(Opponents.Opponent o)
        {
            string deck = o.Cards != null && o.Cards.Count > 0 ? Loc.Get("opp_custom_deck") : o.Prebuilt;
            string res = deck + " · " + o.Skill;
            if (!o.AI.IsEmpty || !o.Player.IsEmpty) res += " · " + Loc.Get("opp_handicaps");
            if (o.Boss) res += " · " + Loc.Get("boss");
            return res;
        }

        private void DrawOpponent(Rect r, Opponents.Opponent o)
        {
            float S = GameSkin.Scale;
            float lh = 34 * S, step = 38 * S, colW = (r.width - 16 * S) / 2f;
            float y = r.y;
            o.Name = TextField(new Rect(r.x, y, r.width, 36 * S), o.Name);
            y += 42 * S;
            // Deck: a prebuilt deck of the game, or a deck imported from the player's decks.
            List<DeckTemplate> pre = Decks.PrebuiltPvp();
            string[] names = pre.ConvertAll(d => d.m_DeckName).ToArray();
            bool custom = o.Cards != null && o.Cards.Count > 0;
            if (custom)
            {
                GUI.Label(new Rect(r.x, y, colW, lh), Loc.Get("opp_deck") + ": " + Loc.Get("opp_custom_deck") + " (" + o.Champion + ", " + o.Cards.Count + ")", GameSkin.Style("body", 14, GameSkin.Text));
                if (GameSkin.GoldButton(new Rect(r.x + colW + 16 * S, y, colW, lh), Loc.Get("opp_use_prebuilt"), GUI.enabled, 15)) { o.Cards = new List<string>(); o.Champion = ""; m_Dirty = true; }
            }
            else if (names.Length > 0)
            {
                string before = o.Prebuilt;
                o.Prebuilt = Cycle(new Rect(r.x, y, r.width, lh), Loc.Get("opp_deck"), string.IsNullOrEmpty(o.Prebuilt) ? names[0] : o.Prebuilt, names, n => n);
                if (o.Prebuilt != before && (o.Name == before || o.Name.StartsWith("Opponent "))) o.Name = o.Prebuilt;
            }
            y += step;
            List<Decks.DeckInfo> mine = Decks.List();
            if (mine.Count > 0)
            {
                m_ImportDeck = Mathf.Clamp(m_ImportDeck, 0, mine.Count - 1);
                Rect cyc = new Rect(r.x, y, colW, lh);
                string chosen = Cycle(cyc, Loc.Get("opp_my_deck"), mine[m_ImportDeck].Name, mine.ConvertAll(d => d.Name).ToArray(), n => n, false);
                m_ImportDeck = Mathf.Max(0, mine.FindIndex(d => d.Name == chosen));
                if (GameSkin.GoldButton(new Rect(r.x + colW + 16 * S, y, colW, lh), Loc.Get("opp_import"), GUI.enabled, 15))
                {
                    DeckTemplate t = Decks.ToTemplate(mine[m_ImportDeck].Id);
                    if (t != null)
                    {
                        o.Cards = Opponents.CardLines(t);
                        ChampionTemplate ch = Singleton<TemplateManager>.Instance.GetChampionTemplate(t.m_ChampionId);
                        o.Champion = ch != null ? ch.m_Name : "";
                        o.Name = mine[m_ImportDeck].Name;
                        m_Dirty = true;
                    }
                }
            }
            y += step + 4 * S;
            o.Skill = Cycle(new Rect(r.x, y, colW, lh), Loc.Get("opp_skill"), o.Skill, Skills, n => n);
            o.Personality = Cycle(new Rect(r.x + colW + 16 * S, y, colW, lh), Loc.Get("opp_personality"), o.Personality, Personalities, n => n);
            y += step;
            o.Noise = Stepper(new Rect(r.x, y, colW, lh), Loc.Get("opp_noise"), o.Noise, 25, 0, 1000);
            o.Weight = Stepper(new Rect(r.x + colW + 16 * S, y, colW, lh), Loc.Get("opp_weight"), o.Weight, 1, 0, 100);
            y += step;
            o.Boss = Toggle(new Rect(r.x, y, colW, lh), Loc.Get("boss"), o.Boss);
            o.Elite = Toggle(new Rect(r.x + colW + 16 * S, y, colW, lh), Loc.Get("opp_elite"), o.Elite);
            y += step + 6 * S;
            GameSkin.Label(new Rect(r.x, y, colW, lh), Loc.Get("opp_hc_ai"), GameSkin.Style("title", 17, GameSkin.Gold));
            GameSkin.Label(new Rect(r.x + colW + 16 * S, y, colW, lh), Loc.Get("opp_hc_player"), GameSkin.Style("title", 17, GameSkin.Gold));
            y += lh;
            Handicap(new Rect(r.x, y, colW, r.yMax - y), o.AI);
            Handicap(new Rect(r.x + colW + 16 * S, y, colW, r.yMax - y), o.Player);
        }

        private void Handicap(Rect r, Opponents.Handicap h)
        {
            float S = GameSkin.Scale, lh = 32 * S, step = 35 * S;
            h.Health = Stepper(new Rect(r.x, r.y, r.width, lh), Loc.Get("opp_health"), h.Health, 1, -40, 100);
            h.Resources = Stepper(new Rect(r.x, r.y + step, r.width, lh), Loc.Get("opp_resources"), h.Resources, 1, 0, 10);
            h.Charges = Stepper(new Rect(r.x, r.y + 2 * step, r.width, lh), Loc.Get("opp_charges"), h.Charges, 1, 0, 10);
            h.Cards = Stepper(new Rect(r.x, r.y + 3 * step, r.width, lh), Loc.Get("opp_cards"), h.Cards, 1, 0, 10);
            // Cards in play from the start: names separated by commas.
            GUI.Label(new Rect(r.x, r.y + 4 * step, 90 * S, lh), Loc.Get("opp_in_play"), GameSkin.Style("body", 14, GameSkin.Text));
            string before = string.Join(", ", (h.InPlay ?? new List<string>()).ToArray());
            string after = TextField(new Rect(r.x + 90 * S, r.y + 4 * step, r.width - 90 * S, lh), before);
            if (after != before)
            {
                h.InPlay = new List<string>();
                if (after.Trim().Length > 0) foreach (string n in after.Split(',')) h.InPlay.Add(n.TrimStart());
            }
        }

        // ------------------------------------------------------------------
        // Controls
        // ------------------------------------------------------------------

        /// <summary>A scrolling list of rows; draw returns true when row i is clicked.</summary>
        private static Vector2 List(Rect area, Vector2 scroll, int count, float rowH, Func<int, bool> draw, Action<int> clicked)
        {
            float S = GameSkin.Scale;
            Rect view = new Rect(0, 0, area.width - 20 * S, count * (rowH + 4 * S));
            bool wasEnabled = GUI.enabled;
            GUI.enabled = true;   // selecting stays possible in a read-only pack
            scroll = GUI.BeginScrollView(area, scroll, view, false, false, GUIStyle.none, view.height > area.height ? GameSkin.ScrollBar : GUIStyle.none);
            for (int i = 0; i < count; i++) if (draw(i)) clicked(i);
            GUI.EndScrollView();
            GUI.enabled = wasEnabled;
            return scroll;
        }

        private string TextField(Rect r, string value)
        {
            GameSkin.Sprite(r, "A_Frames", "Frame_Inner");
            GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), GameSkin.Solid(new Color(0, 0, 0, 0.45f)));
            string v = GUI.TextField(r, value ?? "", 60, GameSkin.Field);
            if (v != value) m_Dirty = true;
            return v;
        }

        private int Stepper(Rect r, string label, int value, int step, int min, int max)
        {
            float S = GameSkin.Scale, bw = 34 * S;
            GUI.Label(new Rect(r.x, r.y, r.width - 2 * bw - 70 * S, r.height), label, GameSkin.Style("body", 14, GameSkin.Text));
            Rect minus = new Rect(r.xMax - 2 * bw - 64 * S, r.y, bw, r.height);
            Rect val = new Rect(minus.xMax, r.y, 64 * S, r.height);
            Rect plus = new Rect(val.xMax, r.y, bw, r.height);
            int v = value;
            if (Small(minus, "-")) v = Math.Max(min, value - step);
            GameSkin.Label(val, (value > 0 && min < 0 ? "+" : "") + value, GameSkin.Style("bold", 16, GameSkin.Text, TextAnchor.MiddleCenter));
            if (Small(plus, "+")) v = Math.Min(max, value + step);
            if (v != value) m_Dirty = true;
            return v;
        }

        private string Cycle(Rect r, string label, string value, string[] options, Func<string, string> show, bool dirty = true)
        {
            float S = GameSkin.Scale, bw = 34 * S, lw = Mathf.Min(r.width * 0.34f, 150 * S);
            GUI.Label(new Rect(r.x, r.y, lw, r.height), label, GameSkin.Style("body", 14, GameSkin.Text));
            int i = Array.IndexOf(options, value);
            Rect prev = new Rect(r.x + lw, r.y, bw, r.height), next = new Rect(r.xMax - bw, r.y, bw, r.height);
            Rect val = new Rect(prev.xMax, r.y, next.x - prev.xMax, r.height);
            string res = value;
            if (Small(prev, "<") && options.Length > 0) res = options[(i <= 0 ? options.Length : i) - 1];
            GameSkin.Label(val, show(value ?? ""), GameSkin.Style("bold", 15, GameSkin.Text, TextAnchor.MiddleCenter));
            if (Small(next, ">") && options.Length > 0) res = options[(i + 1) % options.Length];
            if (dirty && res != value) m_Dirty = true;
            return res;
        }

        /// <summary>Small square button (the title font has no "+", "-", "&lt;", "&gt;": the body font is used).</summary>
        private static bool Small(Rect r, string symbol)
        {
            bool enabled = GUI.enabled, hover = enabled && r.Contains(Event.current.mousePosition);
            GameSkin.Sprite(r, "A_Frames", "Frame_Inner");
            GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), GameSkin.Solid(hover ? new Color(0.35f, 0.28f, 0.15f, 0.9f) : new Color(0.12f, 0.1f, 0.07f, 0.9f)));
            GameSkin.Label(r, symbol, GameSkin.Style("bold", 20, enabled ? (hover ? Color.white : GameSkin.Gold) : GameSkin.Muted, TextAnchor.MiddleCenter));
            return enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        private bool Toggle(Rect r, string label, bool value)
        {
            float S = GameSkin.Scale;
            Rect box = new Rect(r.x, r.y + (r.height - 26 * S) / 2f, 26 * S, 26 * S);
            GameSkin.Sprite(box, "A_Frames", "Frame_Inner");
            GUI.DrawTexture(new Rect(box.x + 2, box.y + 2, box.width - 4, box.height - 4), GameSkin.Solid(new Color(0, 0, 0, 0.5f)));
            if (value) GUI.DrawTexture(new Rect(box.x + 6 * S, box.y + 6 * S, box.width - 12 * S, box.height - 12 * S), GameSkin.Solid(GameSkin.Gold));
            GUI.Label(new Rect(box.xMax + 10 * S, r.y, r.width - box.width - 10 * S, r.height), label, GameSkin.Style("body", 14, GameSkin.Text));
            bool v = GUI.Button(r, GUIContent.none, GUIStyle.none) ? !value : value;
            if (v != value) m_Dirty = true;
            return v;
        }

        /// <summary>Dev: "oppeditor open|close", "oppeditor use &lt;pack&gt;".</summary>
        public static string Command(string[] parts)
        {
            if (parts.Length > 2 && parts[1] == "use" && Saves.Current != null)
            {
                Saves.Current.OpponentPack = parts[2] == Opponents.DefaultPackName ? "" : parts[2];
                Saves.SaveCurrent();
                return "pack " + Opponents.Active.Name + " (" + Opponents.Active.Tiers.Count + " tiers)";
            }
            if (s_Instance == null) return "no editor";
            s_Instance.SetOpen(parts.Length < 2 || parts[1] != "close");
            return s_Instance.m_Open ? "opened" : "closed";
        }
    }
}
