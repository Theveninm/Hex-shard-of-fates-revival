using System;
using System.Collections.Generic;
using Game.Client.GameState;
using Game.Shared;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// "Quick Match" menu (mod v1): choose your deck, then an opponent among your own
    /// decks, the game's decks or the encounters, and start the match against the AI.
    /// Opens from the main menu's "Quick Match" button (or F1). Drawn with IMGUI using the game's
    /// sprites and fonts; the game's interface (NGUI) is disabled while it is open.
    /// </summary>
    public class OfflineMenu : MonoBehaviour
    {
        public static OfflineMenu Instance;

        private enum Tab { MyDecks, GameDecks, Encounters }

        private class Entry
        {
            public string Label;
            public string Detail;
            public string Search;
            public UID PlayerDeck = UID.Invalid;
            public DeckTemplate Template;
            public ResourceId Encounter = ResourceId.Invalid;
            public ChampionTemplate Champion;
        }

        private bool m_Open;
        private Tab m_Tab = Tab.Encounters;
        private string m_Search = "";
        private Vector2 m_ScrollMine, m_ScrollOpp;
        private List<Entry> m_MyDecks = new List<Entry>();
        private List<Entry> m_GameDecks, m_Encounters;
        private Entry m_SelMine, m_SelOpp;
        private string m_ListLang;
        private string m_Message = "";
        private readonly List<Behaviour> m_DisabledUi = new List<Behaviour>();


        public static void Open() { if (Instance != null) Instance.SetOpen(true); }

        public static bool IsOpen { get { return Instance != null && Instance.m_Open; } }

        /// <summary>Opens the deck editor on a deck (see AiDecksWindow).</summary>
        public static void EditDeck(UID deck) { if (Instance != null) Instance.OpenEditor(deck); }

        private void Awake() { Instance = this; }

        /// <summary>
        /// Test command (HexRevival.cmd file): "menu open|close", "menu list enc|game|mine [text]",
        /// "menu fight enc|game|mine &lt;n&gt; [text]" (n-th search result; my deck = the selected one).
        /// </summary>
        public static string Command(string[] parts)
        {
            OfflineMenu m = Instance;
            if (m == null) return "no menu";
            string sub = parts.Length > 1 ? parts[1] : "open";
            if (sub == "open") { m.SetOpen(true); return "opened"; }
            if (sub == "close") { m.SetOpen(false); return "closed"; }
            if (sub == "lang" && parts.Length > 2) { PlayerConfig.Language = parts[2]; m.Refresh(); return "langue " + Loc.Lang; }
            if (sub == "layouts")
            {
                m.Refresh();
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                foreach (Entry e in m.m_MyDecks)
                {
                    ChampionTemplate c = e.Champion;
                    if (c == null) continue;
                    sb.Append(e.Label).Append(" ").Append(c.m_HudPortrait);
                    CardTemplateLayout l = c.m_CircleHudLayout;
                    if (l != null) sb.Append(" circle=").Append(l.m_PortraitCenterX).Append(',').Append(l.m_PortraitCenterY).Append(" r=").Append(l.m_PortraitRadius);
                    foreach (CardTemplateLayout x in new[] { c.m_SelectionLayout, c.m_FullPortraitLayout, c.m_CutoutLayout })
                        if (x != null) sb.Append(" | l=").Append(x.m_PortraitLeft).Append(" t=").Append(x.m_PortraitTop).Append(" r=").Append(x.m_PortraitRight).Append(" b=").Append(x.m_PortraitBottom);
                    sb.Append('\n');
                }
                return sb.ToString();
            }
            m.Refresh();
            string tab = parts.Length > 2 ? parts[2] : "enc";
            List<Entry> list = tab == "game" ? m.m_GameDecks : tab == "mine" ? m.m_MyDecks : m.m_Encounters;
            int idx = 0;
            int argStart = 3;
            if (sub == "fight") { if (parts.Length > 3) int.TryParse(parts[3], out idx); argStart = 4; }
            string q = parts.Length > argStart ? string.Join(" ", parts, argStart, parts.Length - argStart).ToLowerInvariant() : "";
            List<Entry> found = list.FindAll(e => q.Length == 0 || e.Search.IndexOf(q, StringComparison.Ordinal) >= 0);
            if (sub == "list")
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append(found.Count).Append(" result(s), my deck = ").Append(m.m_SelMine != null ? m.m_SelMine.Label : "-").Append('\n');
                for (int i = 0; i < found.Count && i < 40; i++) sb.Append(i).Append(": ").Append(found[i].Label).Append(" | ").Append(found[i].Detail).Append('\n');
                return sb.ToString();
            }
            if (sub == "fight")
            {
                if (idx < 0 || idx >= found.Count) return "index out of range (" + found.Count + ")";
                m.m_SelOpp = found[idx];
                m.Fight();
                return "fight: " + (m.m_SelMine != null ? m.m_SelMine.Label : "-") + " vs " + found[idx].Label + " " + m.m_Message;
            }
            return "unknown subcommand";
        }

        private static bool OnLandingPage()
        {
            try
            {
                Main main = Singleton<Main>.Instance;
                return main != null && main.m_GameClient != null && main.m_GameClient.StateManager != null
                    && main.m_GameClient.StateManager.CurrentGameState == EGameState.LandingPage
                    && main.m_GameClient.PlayerProfile != null && main.m_GameClient.PlayerProfile.ReckoningID != UID.Invalid;
            }
            catch { return false; }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1) && (m_Open || OnLandingPage())) SetOpen(!m_Open);
            if (m_Open && Input.GetKeyDown(KeyCode.Escape)) SetOpen(false);
            if (m_Open && !OnLandingPage()) SetOpen(false);
            if (OnLandingPage()) { EnsureLogoutButton(); Tutorial.OnLandingPage(); }
            else m_LogoutButton = null;
        }

        private void SetOpen(bool open)
        {
            if (open == m_Open) return;
            m_Open = open;
            if (open)
            {
                Refresh();
                // Blocks clicks to the game interface under the menu.
                foreach (UICamera c in UICamera.list.ToArray())
                    if (c != null && c.enabled) { c.enabled = false; m_DisabledUi.Add(c); }
            }
            else
            {
                foreach (Behaviour b in m_DisabledUi) if (b != null) b.enabled = true;
                m_DisabledUi.Clear();
            }
        }

        // ------------------------------------------------------------------
        // Data
        // ------------------------------------------------------------------

        private void Refresh()
        {
            m_Message = "";
            m_MyDecks.Clear();
            foreach (Decks.DeckInfo d in Decks.List())
                m_MyDecks.Add(new Entry { Label = d.Name, Detail = Loc.Get("cards", d.Count), Search = d.Name.ToLowerInvariant(), PlayerDeck = d.Id, Champion = SafeChampion(d.Champion) });
            m_MyDecks.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
            if (m_SelMine != null) m_SelMine = m_MyDecks.Find(e => e.PlayerDeck == m_SelMine.PlayerDeck);
            if (m_SelMine == null && m_MyDecks.Count > 0) m_SelMine = m_MyDecks.Find(e => e.PlayerDeck == Decks.LastDeck) ?? m_MyDecks[0];
            if (m_SelOpp != null && m_SelOpp.PlayerDeck != UID.Invalid) m_SelOpp = null;
            if (m_ListLang != Loc.Lang) { m_GameDecks = null; m_ListLang = Loc.Lang; }
            if (m_GameDecks == null) BuildTemplateLists();
        }

        internal static bool IsBattle(EncounterScene es, TemplateManager tm)
        {
            if (es.m_EncounterPlayers == null) return false;
            foreach (EncounterPlayer p in es.m_EncounterPlayers)
            {
                if (p == null || !p.m_IsAI || !p.m_EncounterDeck.IsValid) continue;
                EncounterDeck ed;
                if (tm.EncounterDecks.TryGetValue(p.m_EncounterDeck, out ed) && ed != null && tm.Decks.ContainsKey(ed.m_DeckTemplateId)) return true;
                if (tm.Decks.ContainsKey(p.m_EncounterDeck)) return true;
            }
            return false;
        }

        private static ChampionTemplate SafeChampion(ResourceId id)
        {
            try { return id.IsValid ? Singleton<TemplateManager>.Instance.GetChampionTemplate(id) : null; }
            catch { return null; }
        }

        private void BuildTemplateLists()
        {
            m_GameDecks = new List<Entry>();
            m_Encounters = new List<Entry>();
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            HashSet<string> seen = new HashSet<string>();
            foreach (DeckTemplate t in tm.Decks.Values)
            {
                try
                {
                    if (t == null || !t.m_ChampionId.IsValid || string.IsNullOrEmpty(t.m_DeckName)) continue;
                    int n = 0;
                    foreach (TemplateCardStack s in t.m_DeckResources) n += s.m_Count;
                    if (n < 40) continue;
                    ChampionTemplate ch = tm.GetChampionTemplate(t.m_ChampionId);
                    string champ = ch != null ? ch.Name : "";
                    string key = t.m_DeckName + "|" + champ;
                    if (!seen.Add(key)) continue;
                    m_GameDecks.Add(new Entry { Label = t.m_DeckName, Detail = champ + " - " + Loc.Get("cards", n), Search = (t.m_DeckName + " " + champ).ToLowerInvariant(), Template = t, Champion = ch });
                }
                catch { }
            }
            m_GameDecks.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
            foreach (SceneData sd in tm.CampaignScenes.Values)
            {
                try
                {
                    EncounterScene es = sd as EncounterScene;
                    if (es == null) continue;
                    // Real battles only: an AI with a deck. The other "encounters" are campaign
                    // steps without combat (dialogs, rewards, panoramas, dungeon entrances).
                    if (!IsBattle(es, tm)) continue;
                    string title = es.Title;
                    if (string.IsNullOrEmpty(title)) title = es.Name;
                    if (string.IsNullOrEmpty(title)) title = es.m_Id.ToString();
                    ChampionTemplate ch = null;
                    try { ch = es.GetChampionTemplate(true); } catch { }
                    string detail = (ch != null ? Loc.Get("vs_champ", ch.Name) : Loc.Get("enc_opp")) + (es.IsBoss ? "  ·  " + Loc.Get("boss") : "") + (es.Mods != null && es.Mods.Count > 0 ? "  ·  " + Loc.Get("special_rules") : "");
                    m_Encounters.Add(new Entry { Label = title, Detail = detail, Search = (title + " " + detail).ToLowerInvariant(), Encounter = es.m_Id, Champion = ch });
                }
                catch { }
            }
            m_Encounters.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
            Debug.Log("[HexRevival] [Menu] " + m_GameDecks.Count + " game decks, " + m_Encounters.Count + " encounters");
        }

        // ------------------------------------------------------------------
        // Actions
        // ------------------------------------------------------------------

        private void OpenDeckEditor()
        {
            SetOpen(false);
            try { Singleton<Main>.Instance.m_GameClient.StateManager.TransitionToState(EGameState.CardCollection, null); }
            catch (Exception e) { Debug.LogError("[HexRevival] [Menu] editor: " + e); }
        }

        private void Fight()
        {
            if (m_SelMine == null) { m_Message = Loc.Get("msg_pick_deck"); return; }
            if (m_SelOpp == null) { m_Message = Loc.Get("msg_pick_opp"); return; }
            ResourceId scene = Patches.NeutralEncounterId;
            DeckTemplate aiDeck = null;
            if (m_SelOpp.Encounter.IsValid) scene = m_SelOpp.Encounter;
            else if (m_SelOpp.Template != null) aiDeck = m_SelOpp.Template;
            else if (m_SelOpp.PlayerDeck != UID.Invalid) aiDeck = Decks.ToTemplate(m_SelOpp.PlayerDeck);
            if (aiDeck == null && !m_SelOpp.Encounter.IsValid) { m_Message = Loc.Get("msg_no_opp_deck"); return; }
            SetOpen(false);
            Patches.StartMatch(m_SelMine.PlayerDeck, scene, aiDeck, m_SelOpp.Label);
        }

        /// <summary>
        /// Prepares a clone of a main menu button: keeps only its look (see UiClone), sets its text,
        /// in lower case like the game's own buttons, and tooltip, hides its padlock.
        /// </summary>
        private static void StripClone(GameObject b, string label, string tooltip)
        {
            UiClone.Strip(b);
            foreach (UILabel l in b.GetComponentsInChildren<UILabel>(true)) l.text = label.ToLower();
            foreach (UITooltipSetText t in b.GetComponentsInChildren<UITooltipSetText>(true))
            {
                t.TooltipLocalizationScript = null;
                t.TooltipText = tooltip; // Localization.Get returns an unknown key as is
            }
            foreach (Transform t in b.GetComponentsInChildren<Transform>(true))
                if (t != b.transform && t.name.IndexOf("Lock", StringComparison.OrdinalIgnoreCase) >= 0) t.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------
        // "Change account" button at the bottom of the main menu
        // ------------------------------------------------------------------

        private GameObject m_LogoutButton;

        private void EnsureLogoutButton()
        {
            UILandingPageViewModel vm = UnityEngine.Object.FindObjectOfType<UILandingPageViewModel>();
            // The column has no room for one more button: the Collection button goes (the
            // collection stays reachable from the deck editor of Quick Match and the game's menus).
            if (vm != null && vm.MenuUI.Collection != null && vm.MenuUI.Collection.gameObject.activeSelf)
            {
                vm.MenuUI.Collection.gameObject.SetActive(false);
                UIGrid g = vm.MenuUI.Collection.transform.parent != null ? vm.MenuUI.Collection.transform.parent.GetComponent<UIGrid>() : null;
                if (g != null) g.Reposition();
            }
            if (m_LogoutButton != null)
            {
                foreach (UILabel l in m_LogoutButton.GetComponentsInChildren<UILabel>())
                {
                    string t = Loc.Get("switch_account").ToLower();
                    if (l.text != t) l.text = t;
                }
                return;
            }
            if (vm == null || vm.MenuUI.Campaign == null) return;
            GameObject src = vm.MenuUI.Campaign.gameObject;
            Transform parent = src.transform.parent;
            if (parent == null) return;
            Transform existing = parent.Find("HexRevival_ChangeAccount");
            if (existing != null) { m_LogoutButton = existing.gameObject; return; }
            try
            {
                GameObject b = UiClone.Copy(src, "HexRevival_ChangeAccount");
                b.transform.SetAsLastSibling();
                StripClone(b, Loc.Get("switch_account"), Loc.Get("switch_account_tip"));
                // Red background (as cancel, opposite the green Quick Match), otherwise a classic button.
                Color red = new Color(1f, 0.55f, 0.5f);
                foreach (UISprite sp in b.GetComponentsInChildren<UISprite>(true))
                    if (sp.spriteName != null && sp.spriteName.StartsWith("Button_Landing", StringComparison.Ordinal)) sp.color = red;
                foreach (UIButton ub in b.GetComponentsInChildren<UIButton>(true))
                {
                    if (ub.tweenTarget != null && ub.tweenTarget.GetComponent<UILabel>() != null) continue;
                    ub.defaultColor = red;
                    ub.hover = new Color(1f, 0.72f, 0.68f);
                    ub.pressed = new Color(0.8f, 0.42f, 0.38f);
                    ub.disabledColor = red;
                }
                foreach (UIButton ub in b.GetComponentsInChildren<UIButton>(true))
                {
                    ub.onClick.Clear();
                    ub.onClick.Add(new EventDelegate(ChangeAccount));
                    ub.isEnabled = true;
                    RightClick.Add(ub.gameObject, ChangeAccount);
                }
                b.SetActive(true);
                UIGrid grid = parent.GetComponent<UIGrid>();
                if (grid != null) grid.Reposition();
                m_LogoutButton = b;
            }
            catch (Exception e)
            {
                Debug.LogError("[HexRevival] [Menu] change account button: " + e);
                m_LogoutButton = src; // no new attempt every frame
            }
        }

        /// <summary>
        /// NGUI buttons ignore the right mouse button; the game's menu buttons react to it like a
        /// left click (through components removed from our clones), so ours do the same.
        /// </summary>
        private class RightClick : MonoBehaviour
        {
            private Action m_Action;

            public static void Add(GameObject go, Action action)
            {
                RightClick rc = go.GetComponent<RightClick>() ?? go.AddComponent<RightClick>();
                rc.m_Action = action;
            }

            private void OnClick()
            {
                if (UICamera.currentTouchID == -2 && m_Action != null) m_Action();
            }
        }

        /// <summary>Logs out (the game's own logout) and goes back to the account menu.</summary>
        private static void ChangeAccount()
        {
            try { Singleton<Main>.Instance.RequestLogOut(); }
            catch (Exception e) { Debug.LogError("[HexRevival] [Menu] logout: " + e); }
        }

        // ------------------------------------------------------------------
        // Drawing (game sprites and fonts, see GameSkin)
        // ------------------------------------------------------------------

        private void OnGUI()
        {
            if (!m_Open) return;
            bool modal = m_ConfirmDelete != null;
            if (modal) GUI.enabled = false;
            float S = GameSkin.Scale;
            GUI.depth = -1000;
            GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float w = Mathf.Min(1180f * S, Screen.width - 24f), h = Mathf.Min(760f * S, Screen.height - 24f);
            Rect win = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f + 10f * S, w, h);

            // Background artwork, main frame, side ornaments
            GameSkin.Sprite(new Rect(win.x + 20 * S, win.y + 60 * S, 300 * S, 330 * S), "A_SplashArt", "ArtSplash_01", 0.25f, false);
            GameSkin.Sprite(new Rect(win.xMax - 320 * S, win.y + 60 * S, 300 * S, 330 * S), "A_SplashArt", "ArtSplash_02", 0.25f, false);
            if (!GameSkin.Sprite(win, "A_Frames", "Frame_With_Header_Hard_Corner"))
                GUI.DrawTexture(win, GameSkin.Solid(new Color(0.06f, 0.07f, 0.1f, 0.97f)));
            GameSkin.Sprite(new Rect(win.x - 40 * S, win.y + win.height / 2 - 175 * S, 65 * S, 349 * S), "A_Frames", "Frame_Side_Embellishment2_Left", 1f, false);
            GameSkin.Sprite(new Rect(win.xMax - 25 * S, win.y + win.height / 2 - 175 * S, 65 * S, 349 * S), "A_Frames", "Frame_Side_Embellishment2_Right", 1f, false);

            // Title
            Rect title = new Rect(win.center.x - 260 * S, win.y - 22 * S, 520 * S, 64 * S);
            GameSkin.Sprite(title, "A_Frames", "Frame_Title_Bar");
            GameSkin.Label(title, Loc.Get("quick_match"), GameSkin.Style("title", 30, GameSkin.Gold, TextAnchor.MiddleCenter));

            float pad = 40 * S;
            float top = win.y + 64 * S;
            float bottom = win.yMax - 92 * S;
            float colGap = 24 * S;
            float leftW = (win.width - 2 * pad - colGap) * 0.42f;
            Rect left = new Rect(win.x + pad, top, leftW, bottom - top);
            Rect right = new Rect(left.xMax + colGap, top, win.xMax - pad - left.xMax - colGap, bottom - top);

            // --- Left column: my deck
            GameSkin.Sprite(left, "A_Frames", "Frame_Generic_Inner");
            Rect lh = new Rect(left.x + 16 * S, left.y + 10 * S, left.width - 32 * S, 34 * S);
            GameSkin.Label(lh, Loc.Get("your_deck"), GameSkin.Style("title", 21, GameSkin.Gold));
            Rect listL = new Rect(left.x + 12 * S, lh.yMax + 8 * S, left.width - 24 * S, left.yMax - lh.yMax - 20 * S);
            m_ScrollMine = DrawMyDecks(listL, m_ScrollMine);

            // --- Right column: opponent
            GameSkin.Sprite(right, "A_Frames", "Frame_Generic_Inner");
            Rect rh = new Rect(right.x + 16 * S, right.y + 10 * S, right.width - 32 * S, 34 * S);
            GameSkin.Label(rh, Loc.Get("your_opponent"), GameSkin.Style("title", 21, GameSkin.Gold));
            float tabW = Mathf.Min(170 * S, (right.width - 40 * S) / 3f);
            float tabY = rh.yMax + 8 * S;
            if (GameSkin.Tab(new Rect(right.x + 16 * S, tabY, tabW, 32 * S), Loc.Get("tab_enc"), m_Tab == Tab.Encounters)) SwitchTab(Tab.Encounters);
            if (GameSkin.Tab(new Rect(right.x + 16 * S + tabW, tabY, tabW, 32 * S), Loc.Get("tab_game"), m_Tab == Tab.GameDecks)) SwitchTab(Tab.GameDecks);
            if (GameSkin.Tab(new Rect(right.x + 16 * S + 2 * tabW, tabY, tabW, 32 * S), Loc.Get("tab_mine"), m_Tab == Tab.MyDecks)) SwitchTab(Tab.MyDecks);

            Rect search = new Rect(right.x + 16 * S, tabY + 42 * S, right.width - 32 * S, 34 * S);
            GameSkin.Sprite(search, "A_Frames", "Frame_Inner");
            GUI.DrawTexture(new Rect(search.x + 2, search.y + 2, search.width - 4, search.height - 4), GameSkin.Solid(new Color(0, 0, 0, 0.45f)));
            GUI.SetNextControlName("hexrevival_search");
            m_Search = GUI.TextField(search, m_Search ?? "", GameSkin.Field);
            if (string.IsNullOrEmpty(m_Search) && GUI.GetNameOfFocusedControl() != "hexrevival_search")
                GUI.Label(search, "   " + Loc.Get("search"), GameSkin.Style("body", 14, GameSkin.Muted));

            string hint = m_Tab == Tab.Encounters ? Loc.Get("hint_enc")
                : m_Tab == Tab.GameDecks ? Loc.Get("hint_game") : Loc.Get("hint_mine");
            Rect hintR = new Rect(right.x + 18 * S, right.yMax - 34 * S, right.width - 36 * S, 26 * S);
            GUI.Label(hintR, hint, GameSkin.Style("body", 13, GameSkin.Muted));
            Rect listR = new Rect(right.x + 12 * S, search.yMax + 10 * S, right.width - 24 * S, hintR.y - search.yMax - 14 * S);
            List<Entry> list = m_Tab == Tab.Encounters ? m_Encounters : m_Tab == Tab.GameDecks ? m_GameDecks : m_MyDecks;
            m_ScrollOpp = DrawList(listR, list ?? new List<Entry>(), m_ScrollOpp, ref m_SelOpp, (m_Search ?? "").Trim().ToLowerInvariant());

            // --- Bottom: summary + actions
            float by = win.yMax - 74 * S;
            Rect summary = new Rect(win.x + pad, by, win.width - 2 * pad - 420 * S, 30 * S);
            GameSkin.Label(summary, (m_SelMine != null ? m_SelMine.Label : "—") + Loc.Get("versus") + (m_SelOpp != null ? m_SelOpp.Label : "—"), GameSkin.Style("title", 19, GameSkin.Text));
            if (!string.IsNullOrEmpty(m_Message))
                GUI.Label(new Rect(summary.x, summary.yMax, summary.width, 24 * S), m_Message, GameSkin.Style("body", 13, new Color(1f, 0.55f, 0.45f)));
            if (GameSkin.RedButton(new Rect(win.xMax - pad - 400 * S, by - 4 * S, 180 * S, 52 * S), Loc.Get("close"))) SetOpen(false);
            if (GameSkin.GreenButton(new Rect(win.xMax - pad - 210 * S, by - 4 * S, 210 * S, 52 * S), Loc.Get("fight"), m_SelMine != null && m_SelOpp != null)) Fight();
            GUI.enabled = true;
            if (modal) DrawConfirmDelete();
        }

        // ------------------------------------------------------------------
        // My decks: create, edit, delete
        // ------------------------------------------------------------------

        private Entry m_ConfirmDelete;

        private Vector2 DrawMyDecks(Rect area, Vector2 scroll)
        {
            float S = GameSkin.Scale;
            float rowH = 72 * S, gap = 4 * S, newH = 54 * S;
            float contentH = newH + gap + m_MyDecks.Count * (rowH + gap);
            bool needBar = contentH > area.height;
            Rect view = new Rect(0, 0, area.width - (needBar ? 20 * S : 0), contentH);
            scroll = GUI.BeginScrollView(area, scroll, view, false, false, GUIStyle.none, needBar ? GameSkin.ScrollBar : GUIStyle.none);

            // Create a new deck
            Rect nr = new Rect(0, 0, view.width, newH);
            bool over = nr.Contains(Event.current.mousePosition);
            GameSkin.Sprite(nr, "A_Buttons", over ? "Button_Bar_Main_hover" : "Button_Bar_Main_Mid");
            GameSkin.Sprite(new Rect(nr.x + 16 * S, nr.y + (newH - 34 * S) / 2f, 34 * S, 34 * S), "A_Buttons", over ? "Button_Add_Hover" : "Button_Add_Base", 1f, false);
            GameSkin.Label(new Rect(nr.x + 62 * S, nr.y, nr.width - 70 * S, newH), Loc.Get("new_deck"), GameSkin.Style("title", 18, over ? Color.white : GameSkin.Gold));
            if (GUI.Button(nr, GUIContent.none, GUIStyle.none)) OpenEditor(UID.Invalid);

            float icon = 38 * S, iconGap = 6 * S;
            float reserve = 2 * icon + iconGap + 14 * S;
            for (int i = 0; i < m_MyDecks.Count; i++)
            {
                Entry e = m_MyDecks[i];
                Rect r = new Rect(0, newH + gap + i * (rowH + gap), view.width, rowH);
                // Icons on the right (drawn before the row: they get the click first).
                Rect del = new Rect(r.xMax - 12 * S - icon, r.y + (rowH - icon) / 2f, icon, icon);
                Rect edit = new Rect(del.x - iconGap - icon, del.y, icon, icon);
                bool clickEdit = GameSkin.Icon(edit, "A_Shared_Atlas", "PvE_Nav_DeckEdit_01_Normal", "PvE_Nav_DeckEdit_02_Hover", "PvE_Nav_DeckEdit_03_Click");
                bool clickDel = GameSkin.Icon(del, "A_Icons", "Icon_DeleteDeck", "Icon_DeleteDeck_Hover", "Icon_DeleteDeck_Click");
                if (clickEdit) { OpenEditor(e.PlayerDeck); break; }
                if (clickDel) { m_ConfirmDelete = e; break; }
                if (GameSkin.Row(r, e.Label, e.Detail, m_SelMine == e, e.Champion, reserve)) { m_SelMine = e; m_Message = ""; }
                // The icons on top of the row background
                GameSkin.Sprite(edit, "A_Shared_Atlas", edit.Contains(Event.current.mousePosition) ? "PvE_Nav_DeckEdit_02_Hover" : "PvE_Nav_DeckEdit_01_Normal", 1f, false);
                GameSkin.Sprite(del, "A_Icons", del.Contains(Event.current.mousePosition) ? "Icon_DeleteDeck_Hover" : "Icon_DeleteDeck", 1f, false);
            }
            GUI.EndScrollView();
            return scroll;
        }

        private void DrawConfirmDelete()
        {
            float S = GameSkin.Scale;
            GUI.color = new Color(0, 0, 0, 0.5f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            Rect box = new Rect((Screen.width - 520 * S) / 2f, (Screen.height - 220 * S) / 2f, 520 * S, 220 * S);
            if (!GameSkin.Sprite(box, "A_Frames", "Frame_With_No_Header_Corners"))
                GUI.DrawTexture(box, GameSkin.Solid(new Color(0.06f, 0.07f, 0.1f, 0.97f)));
            GameSkin.Label(new Rect(box.x, box.y + 24 * S, box.width, 36 * S), Loc.Get("del_title"), GameSkin.Style("title", 24, GameSkin.Gold, TextAnchor.MiddleCenter));
            GUI.Label(new Rect(box.x + 30 * S, box.y + 66 * S, box.width - 60 * S, 56 * S), Loc.Get("del_text", m_ConfirmDelete.Label), GameSkin.Style("body", 15, GameSkin.Text, TextAnchor.MiddleCenter, true));
            if (GameSkin.GoldButton(new Rect(box.x + 50 * S, box.yMax - 78 * S, 190 * S, 52 * S), Loc.Get("cancel"))) m_ConfirmDelete = null;
            else if (GameSkin.RedButton(new Rect(box.xMax - 240 * S, box.yMax - 78 * S, 190 * S, 52 * S), Loc.Get("delete"))) DeleteDeck(m_ConfirmDelete);
        }

        private void DeleteDeck(Entry e)
        {
            m_ConfirmDelete = null;
            UID id = e.PlayerDeck;
            try
            {
                Game.Client.Network.Profile.ProfileService.RemoveDeck.Request(id, new Game.Client.Network.Profile.ProfileService.RemoveDeck.ResponseHandler((Game.Client.Network.Profile.RemoveDeckResponse res) =>
                {
                    try
                    {
                        Game.Client.PlayerProfile profile = Singleton<Main>.Instance.m_GameClient.PlayerProfile;
                        if (profile.DeckList.ContainsKey(id)) profile.RemoveDeck(id);
                    }
                    catch (Exception ex) { Debug.LogWarning("[HexRevival] [Menu] suppression (profil) : " + ex.Message); }
                    if (m_SelMine == e) m_SelMine = null;
                    if (m_SelOpp == e) m_SelOpp = null;
                    Refresh();
                }));
            }
            catch (Exception ex) { Debug.LogError("[HexRevival] [Menu] delete: " + ex); }
        }

        /// <summary>Opens the Collection editor on this deck (or on a new deck if the id is invalid).</summary>
        private void OpenEditor(UID deck)
        {
            SetOpen(false);
            try { Singleton<Main>.Instance.m_GameClient.StateManager.TransitionToState(EGameState.CardCollection, null); }
            catch (Exception ex) { Debug.LogError("[HexRevival] [Menu] editor: " + ex); return; }
            StartCoroutine(PrepareEditor(deck));
        }

        private System.Collections.IEnumerator PrepareEditor(UID deck)
        {
            float until = Time.realtimeSinceStartup + 20f;
            UIPlayerCardsViewModel vm = null;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                vm = UnityEngine.Object.FindObjectOfType<UIPlayerCardsViewModel>();
                if (vm != null && vm.DeckEditorViewModel != null && vm.DeckEditorViewModel.Context != null
                    && (deck == UID.Invalid || vm.DeckEditorViewModel.Context.Decks.Count > 0))
                    break;
                vm = null;
            }
            if (vm == null) { Debug.LogWarning("[HexRevival] [Menu] editor not found"); yield break; }
            yield return new WaitForSeconds(0.5f); // lets the editor finish its initialization
            try
            {
                if (deck == UID.Invalid) { vm.OnNewDeckClick(); yield break; }
                UIDeckEditorViewModel ed = vm.DeckEditorViewModel;
                for (int i = 0; i < ed.Context.Decks.Count; i++)
                {
                    UIDeckContext dc = ed.Context.Decks.GetItem(i);
                    if (dc != null && dc.DeckID == deck)
                    {
                        ed.Context.Decks.SelectItem(dc);
                        ed.OnLoadDeckPressed();
                        Debug.Log("[HexRevival] [Menu] deck opened in the editor: " + deck);
                        yield break;
                    }
                }
                Debug.LogWarning("[HexRevival] [Menu] deck missing from the editor: " + deck);
            }
            catch (Exception ex) { Debug.LogError("[HexRevival] [Menu] editor: " + ex); }
        }

        private void SwitchTab(Tab t)
        {
            if (m_Tab == t) return;
            m_Tab = t;
            m_ScrollOpp = Vector2.zero;
        }

        private Vector2 DrawList(Rect area, List<Entry> list, Vector2 scroll, ref Entry selected, string query)
        {
            float S = GameSkin.Scale;
            float rowH = 72 * S, gap = 4 * S;
            List<Entry> shown = query.Length == 0 ? list : list.FindAll(e => e.Search.IndexOf(query, StringComparison.Ordinal) >= 0);
            if (shown.Count == 0)
            {
                GUI.Label(new Rect(area.x + 8 * S, area.y + 6 * S, area.width, 30 * S), Loc.Get("no_match"), GameSkin.Style("body", 14, GameSkin.Muted));
                return Vector2.zero;
            }
            float contentH = shown.Count * (rowH + gap);
            bool needBar = contentH > area.height;
            Rect view = new Rect(0, 0, area.width - (needBar ? 20 * S : 0), contentH);
            scroll = GUI.BeginScrollView(area, scroll, view, false, false, GUIStyle.none, needBar ? GameSkin.ScrollBar : GUIStyle.none);
            int first = Mathf.Max(0, (int)(scroll.y / (rowH + gap)) - 1);
            int last = Mathf.Min(shown.Count - 1, (int)((scroll.y + area.height) / (rowH + gap)) + 1);
            for (int i = first; i <= last; i++)
            {
                Entry e = shown[i];
                if (GameSkin.Row(new Rect(0, i * (rowH + gap), view.width, rowH), e.Label, e.Detail, selected == e, e.Champion))
                {
                    selected = e;
                    m_Message = "";
                }
            }
            GUI.EndScrollView();
            return scroll;
        }
    }
}
