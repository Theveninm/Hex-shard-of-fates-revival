using System;
using System.Collections.Generic;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// What the AI and the player start with when an AI plays a deck (Opponents.Handicap of Decks.AiSettings): health,
    /// resources, charges, extra cards, shards and cards in play, chosen in a grid of the game's cards (CardArt).
    /// Opened from the deck editor (DeckAiPanel) and for the sealed / draft AI players (AiDecksWindow); OK hands the
    /// edited settings back.
    /// </summary>
    public class ModifiersWindow : MonoBehaviour
    {
        private static ModifiersWindow s_Instance;
        private readonly Controls.UiBlocker m_Block = new Controls.UiBlocker();

        private Decks.AiSettings m_ModEdit;
        private Action<Decks.AiSettings> m_OnOk;
        private Vector2 m_ScrollAi, m_ScrollPlayer;

        private Opponents.Handicap m_PickFor;
        private string m_Search = "";
        private bool m_AiOnly;
        private List<CardTemplate> m_PickList;
        private Vector2 m_PickScroll;

        private void Awake() { s_Instance = this; }

        public static bool IsOpen { get { return s_Instance != null && s_Instance.m_ModEdit != null; } }

        /// <summary>Opens the window on a copy of the settings; ok receives them when the player confirms.</summary>
        public static void Open(Decks.AiSettings copy, Action<Decks.AiSettings> ok)
        {
            if (s_Instance == null) return;
            if (copy.AI == null) copy.AI = new Opponents.Handicap();
            if (copy.Player == null) copy.Player = new Opponents.Handicap();
            s_Instance.m_ModEdit = copy;
            s_Instance.m_OnOk = ok;
            s_Instance.m_PickFor = null;
            s_Instance.m_ScrollAi = s_Instance.m_ScrollPlayer = Vector2.zero;
        }

        public static void Close()
        {
            if (s_Instance == null) return;
            s_Instance.m_ModEdit = null;
            s_Instance.m_PickFor = null;
            s_Instance.m_OnOk = null;
            s_Instance.m_Block.Release();
        }

        private void Update()
        {
            if (m_ModEdit == null) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (m_PickFor != null) m_PickFor = null;
                else { Close(); return; }
            }
            m_Block.Update(true);
        }

        private void OnGUI()
        {
            if (m_ModEdit == null) return;
            GUI.depth = -1200;
            if (m_PickFor != null) { GUI.enabled = false; DrawModifiers(); GUI.enabled = true; DrawPicker(); }
            else DrawModifiers();
        }

        private void DrawModifiers()
        {
            float S = GameSkin.Scale;
            Rect win = Controls.Window(1180, 800, Loc.Get("modifiers_title"));
            float pad = 34 * S, gap = 24 * S, top = win.y + 64 * S, bottom = win.yMax - 86 * S;
            float colW = (win.width - 2 * pad - gap) / 2f;
            Rect left = new Rect(win.x + pad, top, colW, bottom - top), right = new Rect(left.xMax + gap, top, colW, bottom - top);
            Side(left, Loc.Get("opp_hc_ai"), m_ModEdit.AI, true, ref m_ScrollAi);
            Side(right, Loc.Get("opp_hc_player"), m_ModEdit.Player, false, ref m_ScrollPlayer);
            float by = win.yMax - 72 * S;
            if (GameSkin.RedButton(new Rect(win.xMax - pad - 400 * S, by, 190 * S, 52 * S), Loc.Get("cancel"))) Close();
            if (GameSkin.GreenButton(new Rect(win.xMax - pad - 190 * S, by, 190 * S, 52 * S), "OK"))
            {
                Action<Decks.AiSettings> ok = m_OnOk;
                Decks.AiSettings edited = m_ModEdit;
                Close();
                if (ok != null) ok(edited);
            }
        }

        private void Side(Rect r, string title, Opponents.Handicap h, bool ai, ref Vector2 scroll)
        {
            float S = GameSkin.Scale, lh = 32 * S, step = 37 * S;
            Controls.Frame(r, title);
            if (h.Shards == null || h.Shards.Length < 5) h.Shards = new int[5];
            if (h.InPlay == null) h.InPlay = new List<string>();
            float x = r.x + 16 * S, w = r.width - 32 * S, y = r.y + 48 * S;
            h.Health = Controls.Stepper(new Rect(x, y, w, lh), Loc.Get("opp_health"), h.Health, 1, -40, 100, true); y += step;
            h.Resources = Controls.Stepper(new Rect(x, y, w, lh), Loc.Get("opp_resources"), h.Resources, 1, 0, 10); y += step;
            h.Charges = Controls.Stepper(new Rect(x, y, w, lh), Loc.Get("opp_charges"), h.Charges, 1, 0, 10); y += step;
            h.Cards = Controls.Stepper(new Rect(x, y, w, lh), Loc.Get("opp_cards"), h.Cards, 1, 0, 10); y += step;
            y += 4 * S;

            GameSkin.Label(new Rect(x, y, w, 26 * S), Loc.Get("opp_shards"), GameSkin.Style("title", 16, GameSkin.Gold));
            y += 28 * S;
            Controls.Shards(new Rect(x, y, w, 96 * S), h.Shards);
            y += 104 * S;

            GameSkin.Label(new Rect(x, y, w - 200 * S, 34 * S), Loc.Get("opp_in_play"), GameSkin.Style("title", 16, GameSkin.Gold));
            if (GameSkin.GoldButton(new Rect(x + w - 190 * S, y, 190 * S, 34 * S), Loc.Get("add_card"), true, 15)) OpenPicker(h);
            y += 40 * S;
            Rect list = new Rect(x, y, w, r.yMax - 12 * S - y);
            GUI.DrawTexture(list, GameSkin.Solid(new Color(0, 0, 0, 0.35f)));
            int remove = -1;
            List<string> cards = h.InPlay;
            // The cards as tiles, a small "x" on each to remove it.
            scroll = CardGrid(new Rect(list.x + 4 * S, list.y + 4 * S, list.width - 8 * S, list.height - 8 * S), scroll, cards.Count, 92 * S, i => Opponents.CardByName(cards[i]), i => false, (i, tile, before) =>
            {
                Rect xr = new Rect(tile.xMax - 28 * S, tile.y + 4 * S, 24 * S, 24 * S);
                // Clicked before the tile is drawn (the tile's own button would take the click), shown over it after.
                if (before) { if (GUI.Button(xr, GUIContent.none, GUIStyle.none)) remove = i; }
                else Controls.Small(xr, "x");
            });
            if (remove >= 0) { cards.RemoveAt(remove); Controls.Changed = true; }
        }

        /// <summary>
        /// A scrolling grid of card tiles (see CardArt.Tile), only the visible rows drawn. clicked(i) is called when tile i is
        /// clicked; extra(i, tile, before) is called before the tile is drawn (to take clicks) and after it (to draw over it).
        /// </summary>
        private static Vector2 CardGrid(Rect area, Vector2 scroll, int count, float tileW, Func<int, CardTemplate> card, Func<int, bool> clicked, Action<int, Rect, bool> extra = null)
        {
            float S = GameSkin.Scale, gap = 6 * S, tileH = tileW * 1.36f;
            int cols = Mathf.Max(1, (int)((area.width - 20 * S + gap) / (tileW + gap)));
            int rows = (count + cols - 1) / cols;
            Rect view = new Rect(0, 0, area.width - 20 * S, rows * (tileH + gap));
            scroll = GUI.BeginScrollView(area, scroll, view, false, false, GUIStyle.none, view.height > area.height ? GameSkin.ScrollBar : GUIStyle.none);
            int first = Mathf.Max(0, (int)(scroll.y / (tileH + gap))), last = Mathf.Min(rows, first + (int)(area.height / (tileH + gap)) + 2);
            for (int row = first; row < last; row++)
                for (int col = 0; col < cols; col++)
                {
                    int i = row * cols + col;
                    if (i >= count) break;
                    Rect tile = new Rect(col * (tileW + gap), row * (tileH + gap), tileW, tileH);
                    CardTemplate c = card(i);
                    if (extra != null) extra(i, tile, true);
                    if (c == null) GUI.DrawTexture(tile, GameSkin.Solid(new Color(0.2f, 0.2f, 0.2f, 1f)));
                    else if (CardArt.Tile(tile, c)) clicked(i);
                    if (extra != null) extra(i, tile, false);
                }
            GUI.EndScrollView();
            return scroll;
        }

        // ------------------------------------------------------------------
        // Card picker (every card of the game, the AI's own cards included)
        // ------------------------------------------------------------------

        private void OpenPicker(Opponents.Handicap target)
        {
            m_PickFor = target;
            m_PickScroll = Vector2.zero;
            m_PickList = null;
        }

        private void DrawPicker()
        {
            float S = GameSkin.Scale;
            Rect win = Controls.Window(1180, 860, Loc.Get("pick_card"));
            float pad = 34 * S, y = win.y + 64 * S, w = win.width - 2 * pad;
            string search = Controls.TextField(new Rect(win.x + pad, y, w * 0.62f, 38 * S), m_Search, 40);
            bool aiOnly = Controls.Toggle(new Rect(win.x + pad + w * 0.66f, y, w * 0.34f, 38 * S), Loc.Get("ai_cards_only"), m_AiOnly);
            if (search != m_Search || aiOnly != m_AiOnly || m_PickList == null)
            {
                m_Search = search; m_AiOnly = aiOnly;
                m_PickList = new List<CardTemplate>();
                foreach (CardTemplate c in Opponents.CardsByName())
                {
                    if (!CardArt.IsShown(c)) continue;
                    if (m_AiOnly && c.m_SetId != Game.Shared.Resources.BuiltInResources.SetAIOnlyCards) continue;
                    if (m_Search.Length > 0 && c.m_Name.IndexOf(m_Search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    m_PickList.Add(c);
                }
                m_PickScroll = Vector2.zero;
            }
            y += 50 * S;
            Rect area = new Rect(win.x + pad, y, w, win.yMax - 92 * S - y);
            string chosen = null;
            List<CardTemplate> list = m_PickList;
            m_PickScroll = CardGrid(area, m_PickScroll, list.Count, 132 * S, i => list[i], i => { chosen = list[i].m_Name; return true; });
            if (chosen != null) { m_PickFor.InPlay.Add(chosen); Controls.Changed = true; m_PickFor = null; }
            GUI.Label(new Rect(win.x + pad, win.yMax - 72 * S, w - 220 * S, 52 * S), list.Count + " " + Loc.Get("cards_found"), GameSkin.Style("body", 14, GameSkin.Muted));
            if (GameSkin.RedButton(new Rect(win.xMax - pad - 190 * S, win.yMax - 72 * S, 190 * S, 52 * S), Loc.Get("cancel"))) m_PickFor = null;
        }
    

        /// <summary>Dev: "mods [state] | shard &lt;0-4&gt; | inplay &lt;card name&gt; | pick [search] | ok | cancel".</summary>
        public static string Command(string[] parts)
        {
            ModifiersWindow w = s_Instance;
            if (w == null || w.m_ModEdit == null) return "not open";
            string sub = parts.Length > 1 ? parts[1] : "state";
            switch (sub)
            {
                case "shard": w.m_ModEdit.AI.Shards[int.Parse(parts[2])]++; return "ok";
                case "inplay": w.m_ModEdit.AI.InPlay.Add(string.Join(" ", parts, 2, parts.Length - 2)); return "ok";
                case "pick": w.OpenPicker(w.m_ModEdit.AI); if (parts.Length > 2) w.m_Search = parts[2]; return "picker open";
                case "ok": { Action<Decks.AiSettings> ok = w.m_OnOk; Decks.AiSettings e = w.m_ModEdit; Close(); if (ok != null) ok(e); return "saved"; }
                case "cancel": Close(); return "cancelled";
            }
            Opponents.Handicap h = w.m_ModEdit.AI;
            return "AI: hp " + h.Health + " shards " + string.Join(",", Array.ConvertAll(h.Shards ?? new int[0], x => x.ToString())) + " inplay " + string.Join("/", (h.InPlay ?? new List<string>()).ToArray());
        }
    }
}
