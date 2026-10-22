using System;
using System.Collections.Generic;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// AI settings of a deck in the game's deck editor (Collection screen), for when the AI plays it (see AiConfig):
    /// - the game's "Set Personality" button (Siege only in the game) is shown for every deck, and its window gets
    ///   a row below: deck made for the AI, default AI level, frequency, boss / elite / rare;
    /// - a "Modifiers" button under it opens the modifiers of both sides: health, resources, charges, extra cards,
    ///   imprecision (AI), cards in play (chosen in a card list) and shards at the start.
    /// Changes apply when the game's window is confirmed (Select) or the modifiers' OK; a deck not saved yet gets them
    /// at its first save (Decks.PendingAi).
    /// </summary>
    public class DeckAiPanel : MonoBehaviour
    {
        private static DeckAiPanel s_Instance;
        private void Awake() { s_Instance = this; }

        private UIPlayerCardsViewModel m_Vm;
        private float m_NextFind;
        private readonly Controls.UiBlocker m_Block = new Controls.UiBlocker();

        // The game's personality button, moved up, and our copy in its place.
        private Transform m_PersonalityButton;
        private GameObject m_ModButton;

        private ulong m_DeckId;
        private bool m_PopupVisible, m_PopupConfirmed;
        private Decks.AiSettings m_PopupEdit;
        private Rect m_PanelRect;

        private Decks.AiSettings m_ModEdit;
        private Vector2 m_ScrollAi, m_ScrollPlayer;

        private Opponents.Handicap m_PickFor;
        private string m_Search = "";
        private bool m_AiOnly;
        private List<CardTemplate> m_PickList;
        private Vector2 m_PickScroll;

        private void Update()
        {
            if (m_Vm == null)
            {
                Decks.EditorDeck = 0;
                m_ModEdit = null; m_PickFor = null; m_PopupVisible = false;
                m_Block.Release();
                if (Time.realtimeSinceStartup < m_NextFind) return;
                m_NextFind = Time.realtimeSinceStartup + 1f;
                m_Vm = FindObjectOfType<UIPlayerCardsViewModel>();
                if (m_Vm == null) return;
                m_PersonalityButton = null; m_ModButton = null;
                try { m_Vm.m_PersonalityVM.PersonalitySelected += p => m_PopupConfirmed = true; }
                catch (Exception e) { Debug.LogWarning("[HexRevival] [DeckAi] personality window: " + e.Message); }
            }
            try
            {
                // The personality matters to every deck the AI plays, not only to Siege.
                if (!m_Vm.m_ChampionVM.Context.ShowPersonalityButton) m_Vm.m_ChampionVM.Context.ShowPersonalityButton = true;
                EnsureButton();

                Game.Shared.Mechanics.DeckCache working = m_Vm.DeckEditorViewModel != null ? m_Vm.DeckEditorViewModel.GetWorkingDeck() : null;
                ulong id = working != null && working.m_DeckId.IsValid() ? working.m_DeckId.GetInstanceId() : 0;
                if (id != m_DeckId)
                {
                    if (id != 0) Decks.PendingAi = null;   // another deck: what was set for an unsaved one is dropped
                    m_DeckId = id;
                }
                Decks.EditorDeck = id;

                global::Siege.UIPersonalitySelectContext pc = m_Vm.m_PersonalityVM != null ? m_Vm.m_PersonalityVM.IContext as global::Siege.UIPersonalitySelectContext : null;
                bool visible = pc != null && pc.IsVisible;
                if (visible && !m_PopupVisible) { m_PopupEdit = Current().Clone(); m_PopupConfirmed = false; }
                if (!visible && m_PopupVisible && m_PopupConfirmed && m_PopupEdit != null) Commit(m_PopupEdit);
                m_PopupVisible = visible;

                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    if (m_PickFor != null) m_PickFor = null;
                    else m_ModEdit = null;
                }
                m_Block.Update(m_ModEdit != null, m_PopupVisible ? m_PanelRect : new Rect());
            }
            catch (Exception e)
            {
                Debug.LogError("[HexRevival] [DeckAi] " + e);
                m_Vm = null;
            }
        }

        /// <summary>The settings of the deck being edited (a copy is edited, see Commit).</summary>
        private Decks.AiSettings Current()
        {
            if (m_DeckId != 0) return Decks.Ai(m_DeckId);
            return Decks.PendingAi ?? new Decks.AiSettings();
        }

        private void Commit(Decks.AiSettings a)
        {
            if (m_DeckId != 0) Decks.SetAi(m_DeckId, a);
            else Decks.PendingAi = a;
        }

        // ------------------------------------------------------------------
        // The "Modifiers" button, a copy of the game's "Set Personality" button placed under it
        // ------------------------------------------------------------------

        private void EnsureButton()
        {
            if (m_PersonalityButton == null)
            {
                Transform src = Find(m_Vm.m_ChampionVM.transform, "Button_PersonalitySelect");
                if (src == null) return;
                if (src.parent.Find("HexRevival_Modifiers") != null) { m_PersonalityButton = src; m_ModButton = src.parent.Find("HexRevival_Modifiers").gameObject; return; }
                // Our copy keeps the game's place (and anchors); the game's button moves one row up (the champion's
                // name is right below).
                GameObject b = UiClone.Copy(src.gameObject, "HexRevival_Modifiers");
                UiClone.Strip(b);
                foreach (UILabel l in b.GetComponentsInChildren<UILabel>(true)) l.text = Loc.Get("modifiers");
                Transform icon = b.transform.Find("Icon_Frame");
                if (icon != null) icon.gameObject.SetActive(false);
                foreach (UIButton ub in b.GetComponentsInChildren<UIButton>(true))
                {
                    ub.onClick.Clear();
                    ub.onClick.Add(new EventDelegate(OpenModifiers));
                    ub.isEnabled = true;
                }
                UIWidget w = src.GetComponent<UIWidget>();
                int h = w != null ? w.height + 6 : 38;
                if (w != null && w.isAnchored)
                {
                    w.topAnchor.absolute += h;
                    w.bottomAnchor.absolute += h;
                    w.ResetAnchors();
                    w.UpdateAnchors();
                }
                else src.localPosition += new Vector3(0, h, 0);
                m_PersonalityButton = src;
                m_ModButton = b;
            }
            bool active = m_PersonalityButton.gameObject.activeSelf;
            if (m_ModButton != null && m_ModButton.activeSelf != active) m_ModButton.SetActive(active);
        }

        private static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                Transform r = Find(c, name);
                if (r != null) return r;
            }
            return null;
        }

        private void OpenModifiers()
        {
            m_ModEdit = Current().Clone();
            m_ScrollAi = m_ScrollPlayer = Vector2.zero;
        }

        // ------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------

        private void OnGUI()
        {
            if (m_Vm == null) return;
            GUI.depth = -1100;
            if (m_PopupVisible && m_PopupEdit != null && m_ModEdit == null) DrawPopupRow();
            if (m_ModEdit != null)
            {
                if (m_PickFor != null) { GUI.enabled = false; DrawModifiers(); GUI.enabled = true; DrawPicker(); }
                else DrawModifiers();
            }
        }

        /// <summary>Screen rectangle (GUI coordinates) of an NGUI object.</summary>
        private static Rect ScreenRect(Transform t)
        {
            Bounds b = NGUIMath.CalculateAbsoluteWidgetBounds(t);
            Camera cam = NGUITools.FindCameraForLayer(t.gameObject.layer);
            if (cam == null) return new Rect();
            Vector3 a = cam.WorldToScreenPoint(b.min), c = cam.WorldToScreenPoint(b.max);
            return new Rect(Mathf.Min(a.x, c.x), Screen.height - Mathf.Max(a.y, c.y), Mathf.Abs(c.x - a.x), Mathf.Abs(c.y - a.y));
        }

        /// <summary>The row added under the game's personality window.</summary>
        private void DrawPopupRow()
        {
            float S = GameSkin.Scale;
            // Inside the game's window, between the personality's description and the Cancel / Select buttons.
            Transform frame = Find(m_Vm.m_PersonalityVM.transform, "Frame_Main");
            Rect f = frame != null ? ScreenRect(frame) : new Rect(Screen.width * 0.1f, Screen.height * 0.1f, Screen.width * 0.8f, Screen.height * 0.8f);
            UIButton select = m_Vm.m_PersonalityVM.MainUI.Btn_Select;
            float bottom = select != null ? ScreenRect(select.transform).y - 6 * S : f.yMax - 80 * S;
            float w = Mathf.Min(f.width - 80 * S, 1100 * S), h = 90 * S;
            Rect r = new Rect(f.center.x - w / 2f, bottom - h, w, h);
            m_PanelRect = r;
            GUI.DrawTexture(r, GameSkin.Solid(new Color(0f, 0f, 0f, 0.45f)));
            Decks.AiSettings a = m_PopupEdit;
            float pad = 18 * S, lh = 28 * S, y = r.y + 4 * S, x = r.x + pad, col = (r.width - 2 * pad) / 3f;
            a.ForAI = Controls.Toggle(new Rect(x, y, col - 10 * S, lh), Loc.Get("ai_deck"), a.ForAI);
            a.Skill = Controls.Cycle(new Rect(x + col, y, 2 * col - 10 * S, lh), Loc.Get("ai_default_skill"), a.Skill, Controls.Skills);
            y += lh + 2 * S;
            a.Frequency = Controls.Stepper(new Rect(x, y, col - 20 * S, lh), Loc.Get("opp_weight"), a.Frequency, 1, 0, 20);
            float tw = (2 * col) / 3f;
            a.Boss = Controls.Toggle(new Rect(x + col, y, tw - 10 * S, lh), Cap(Loc.Get("boss")), a.Boss);
            a.Elite = Controls.Toggle(new Rect(x + col + tw, y, tw - 10 * S, lh), Loc.Get("opp_elite"), a.Elite);
            a.Rare = Controls.Toggle(new Rect(x + col + 2 * tw, y, tw - 10 * S, lh), Loc.Get("opp_rare"), a.Rare);
            GUI.Label(new Rect(x, r.yMax - 20 * S, r.width - 2 * pad, 18 * S), Loc.Get("ai_skill_note"), GameSkin.Style("body", 12, GameSkin.Muted));
        }

        private static string Cap(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1); }

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
            if (GameSkin.RedButton(new Rect(win.xMax - pad - 400 * S, by, 190 * S, 52 * S), Loc.Get("cancel"))) m_ModEdit = null;
            if (GameSkin.GreenButton(new Rect(win.xMax - pad - 190 * S, by, 190 * S, 52 * S), "OK"))
            {
                Commit(m_ModEdit);
                m_ModEdit = null;
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
            if (ai) { m_ModEdit.Noise = Controls.Stepper(new Rect(x, y, w, lh), Loc.Get("opp_noise"), m_ModEdit.Noise, 10, 0, 200); }
            y += step + 4 * S;   // the same height on both sides

            GameSkin.Label(new Rect(x, y, w, 26 * S), Loc.Get("opp_shards"), GameSkin.Style("title", 16, GameSkin.Gold));
            y += 28 * S;
            Controls.Shards(new Rect(x, y, w, 96 * S), h.Shards);
            y += 104 * S;

            GameSkin.Label(new Rect(x, y, w - 200 * S, 34 * S), Loc.Get("opp_in_play"), GameSkin.Style("title", 16, GameSkin.Gold));
            if (GameSkin.GoldButton(new Rect(x + w - 190 * S, y, 190 * S, 34 * S), Loc.Get("add_card"), true, 15)) OpenPicker(h);
            y += 40 * S;
            Rect list = new Rect(x, y, w, r.yMax - 12 * S - y);
            GUI.DrawTexture(list, GameSkin.Solid(new Color(0, 0, 0, 0.35f)));
            float rowH = 30 * S;
            int remove = -1;
            List<string> cards = h.InPlay;
            scroll = Controls.List(new Rect(list.x + 4 * S, list.y + 4 * S, list.width - 8 * S, list.height - 8 * S), scroll, cards.Count, rowH, i =>
            {
                Rect row = new Rect(0, i * (rowH + 4 * S), list.width - 32 * S, rowH);
                GUI.Label(new Rect(row.x + 8 * S, row.y, row.width - 50 * S, row.height), cards[i], GameSkin.Style("bold", 15, GameSkin.Text));
                return Controls.Small(new Rect(row.xMax - 34 * S, row.y, 32 * S, rowH), "x");
            }, i => remove = i);
            if (remove >= 0) { cards.RemoveAt(remove); Controls.Changed = true; }
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
            Rect win = Controls.Window(820, 760, Loc.Get("pick_card"));
            float pad = 34 * S, y = win.y + 64 * S, w = win.width - 2 * pad;
            string search = Controls.TextField(new Rect(win.x + pad, y, w * 0.62f, 38 * S), m_Search, 40);
            bool aiOnly = Controls.Toggle(new Rect(win.x + pad + w * 0.66f, y, w * 0.34f, 38 * S), Loc.Get("ai_cards_only"), m_AiOnly);
            if (search != m_Search || aiOnly != m_AiOnly || m_PickList == null)
            {
                m_Search = search; m_AiOnly = aiOnly;
                m_PickList = new List<CardTemplate>();
                foreach (CardTemplate c in Opponents.CardsByName())
                {
                    if (m_AiOnly && c.m_SetId != Game.Shared.Resources.BuiltInResources.SetAIOnlyCards) continue;
                    if (m_Search.Length > 0 && c.m_Name.IndexOf(m_Search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    m_PickList.Add(c);
                }
                m_PickScroll = Vector2.zero;
            }
            y += 50 * S;
            Rect area = new Rect(win.x + pad, y, w, win.yMax - 92 * S - y);
            float rowH = 42 * S;
            string chosen = null;
            List<CardTemplate> list = m_PickList;
            m_PickScroll = Controls.List(area, m_PickScroll, list.Count, rowH, i =>
                GameSkin.Row(new Rect(0, i * (rowH + 4 * S), area.width - 20 * S, rowH), list[i].m_Name,
                    list[i].m_SetId == Game.Shared.Resources.BuiltInResources.SetAIOnlyCards ? Loc.Get("ai_card") : "", false),
                i => chosen = list[i].m_Name);
            if (chosen != null) { m_PickFor.InPlay.Add(chosen); Controls.Changed = true; m_PickFor = null; }
            GUI.Label(new Rect(win.x + pad, win.yMax - 72 * S, w - 220 * S, 52 * S), list.Count + " " + Loc.Get("cards_found"), GameSkin.Style("body", 14, GameSkin.Muted));
            if (GameSkin.RedButton(new Rect(win.xMax - pad - 190 * S, win.yMax - 72 * S, 190 * S, 52 * S), Loc.Get("cancel"))) m_PickFor = null;
        }
    
        /// <summary>Dev: "deckai [state] | mods | pick [search] | shard &lt;0-4&gt; | inplay &lt;card name&gt; | ok | cancel".</summary>
        public static string Command(string[] parts)
        {
            DeckAiPanel p = s_Instance;
            if (p == null || p.m_Vm == null) return "not in the deck editor";
            string sub = parts.Length > 1 ? parts[1] : "state";
            switch (sub)
            {
                case "mods": p.OpenModifiers(); return "modifiers open";
                case "shard": if (p.m_ModEdit == null) return "open mods first"; p.m_ModEdit.AI.Shards[int.Parse(parts[2])]++; return "ok";
                case "inplay": if (p.m_ModEdit == null) return "open mods first"; p.m_ModEdit.AI.InPlay.Add(string.Join(" ", parts, 2, parts.Length - 2)); return "ok";
                case "pick": if (p.m_ModEdit == null) p.OpenModifiers(); p.OpenPicker(p.m_ModEdit.AI); if (parts.Length > 2) p.m_Search = parts[2]; return "picker open";
                case "ok": if (p.m_ModEdit != null) p.Commit(p.m_ModEdit); p.m_ModEdit = null; return "saved";
                case "cancel": p.m_ModEdit = null; return "cancelled";
                case "reset": if (p.m_DeckId != 0) Decks.SetAi(p.m_DeckId, new Decks.AiSettings()); return "reset";
            }
            Decks.AiSettings a = p.Current();
            return "deck " + p.m_DeckId + " forAI=" + a.ForAI + " skill=" + a.Skill + " freq=" + a.Frequency + " boss=" + a.Boss + " elite=" + a.Elite
                + " AI: hp " + a.AI.Health + " shards " + string.Join(",", Array.ConvertAll(a.AI.Shards ?? new int[0], x => x.ToString())) + " inplay " + string.Join("/", (a.AI.InPlay ?? new List<string>()).ToArray())
                + " pending=" + (Decks.PendingAi != null);
        }
}
}
