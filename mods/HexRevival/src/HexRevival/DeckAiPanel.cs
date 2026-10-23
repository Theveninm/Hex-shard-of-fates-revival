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
    /// - a "Modifiers" button under it opens the modifiers of both sides (see ModifiersWindow): health, resources, charges, extra cards,
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



        private void Update()
        {
            if (m_Vm == null)
            {
                Decks.EditorDeck = 0;
                ModifiersWindow.Close(); m_PopupVisible = false;
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

                m_Block.Update(false, m_PopupVisible && !ModifiersWindow.IsOpen ? m_PanelRect : new Rect());
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
            ulong deck = m_DeckId;
            ModifiersWindow.Open(Current().Clone(), a =>
            {
                // The deck may have changed meanwhile: the settings go to the deck they were opened for.
                if (deck != 0) Decks.SetAi(deck, a);
                else Decks.PendingAi = a;
            });
        }

        // ------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------

        private void OnGUI()
        {
            if (m_Vm == null) return;
            GUI.depth = -1100;
            if (m_PopupVisible && m_PopupEdit != null && !ModifiersWindow.IsOpen) DrawPopupRow();
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
            float w = Mathf.Min(f.width - 80 * S, 1100 * S), h = 64 * S;
            Rect r = new Rect(f.center.x - w / 2f, bottom - h, w, h);
            m_PanelRect = r;
            GUI.DrawTexture(r, GameSkin.Solid(new Color(0f, 0f, 0f, 0.55f)));
            Decks.AiSettings a = m_PopupEdit;
            float pad = 14 * S, lh = 28 * S, y = r.y + 3 * S, x = r.x + pad, col = (r.width - 2 * pad) / 3f;
            // A deck saved as made for the AI stays one (it is shared and has no owner any more); it can be copied as a
            // player deck if the account owns its cards.
            bool locked = m_DeckId != 0 && Decks.IsAiDeck(m_DeckId);
            if (locked)
            {
                GUI.enabled = false;
                Controls.Toggle(new Rect(x, y, col - 10 * S, lh), Loc.Get("ai_deck"), true);
                GUI.enabled = true;
                a.ForAI = true;
            }
            else a.ForAI = Controls.Toggle(new Rect(x, y, col - 10 * S, lh), Loc.Get("ai_deck"), a.ForAI);
            a.Skill = Controls.Skill(new Rect(x + col, y, 2 * col - 10 * S, lh), Loc.Get("ai_level"), a.Skill);
            y += lh + 2 * S;
            if (locked && GameSkin.GoldButton(new Rect(x, y, col - 10 * S, lh), Loc.Get("ai_copy_to_player"), true, 13)) CopyToPlayer();
            else if (!locked) GUI.Label(new Rect(x, y, col - 10 * S, lh), Loc.Get("ai_skill_note"), GameSkin.Style("body", 11, GameSkin.Muted, TextAnchor.MiddleLeft, true));
            a.Noise = Controls.Stepper(new Rect(x + col, y, 2 * col - 10 * S, lh), Loc.Get("ai_noise"), a.Noise, 10, 0, 200);
        }

        private void CopyToPlayer()
        {
            List<string> missing = new List<string>();
            ulong id = Decks.CopyToAccount(m_DeckId, missing);
            if (id != 0) Notices.Show(Loc.Get("ai_copy_done", Current() != null ? Decks.NameOf(id) : ""));
            else if (missing.Count > 0) Notices.Show(Loc.Get("ai_copy_missing", string.Join(", ", missing.GetRange(0, Math.Min(6, missing.Count)).ToArray()) + (missing.Count > 6 ? "..." : "")));
        }

        /// <summary>Dev: "deckai [state] | mods | reset" (the Modifiers window: "mods ..." commands, see ModifiersWindow).</summary>
        public static string Command(string[] parts)
        {
            DeckAiPanel p = s_Instance;
            if (p == null || p.m_Vm == null) return "not in the deck editor";
            string sub = parts.Length > 1 ? parts[1] : "state";
            switch (sub)
            {
                case "mods": p.OpenModifiers(); return "modifiers open";
                case "reset": if (p.m_DeckId != 0) Decks.SetAi(p.m_DeckId, new Decks.AiSettings()); return "reset";
                case "copy": p.CopyToPlayer(); return "copy";
            }
            Decks.AiSettings a = p.Current();
            return "deck " + p.m_DeckId + " forAI=" + a.ForAI + " skill=" + a.Skill + " noise=" + a.Noise
                + " AI: hp " + a.AI.Health + " shards " + string.Join(",", Array.ConvertAll(a.AI.Shards ?? new int[0], x => x.ToString())) + " inplay " + string.Join("/", (a.AI.InPlay ?? new List<string>()).ToArray())
                + " pending=" + (Decks.PendingAi != null);
        }
}
}
