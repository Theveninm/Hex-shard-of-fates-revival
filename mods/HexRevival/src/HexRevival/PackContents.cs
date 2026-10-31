using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Game.Shared;
using Game.Shared.Mechanics;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// What a store product can give (Zymmer 2026-10-31: "a list of all stuff that could drop when buying a pack from
    /// the store"), as the mod opens it (Progression.OpenPack, Packs, SpecialPacks, PrizePacks, Zymmer), grouped by loot
    /// kind with its rate ("Uncommons: 4 per pack", "Common chest: one item among these"), each pool with its chance and
    /// each card's or item's own chance. Not in the original game; the odds are the mod's (PLACEHOLDER where they are).
    /// - Products with the game's card count and Details window (boosters): a line under the card count with "All
    ///   items"; in the Details window, the same figure and a "Drops and rates" button that shows the grouped view in
    ///   place of its card grid (paginated: nothing can be added under it).
    /// - The other products: a bar in place of the card count ("Cards" as the game would count them, "All items") with
    ///   a Details button opening the grouped view.
    /// - Cards and items are miniatures; equipment shows its slot and the card it changes, mercenaries their shards; a
    ///   right click shows everything (effect, party bonus, deck rules, card text, drop chance).
    /// </summary>
    public class PackContents : MonoBehaviour
    {
        // ------------------------------------------------------------------
        // Model
        // ------------------------------------------------------------------

        /// <summary>A pool: drawn Draws times per pack, Chance (0-1) each time; Counted = in "Cards" (the game's count).</summary>
        private class Pool
        {
            public string Title;
            public float Chance = 1f;
            public int Draws = 1;
            public bool Counted, Once;
            public List<CardTemplate> Cards;
            public List<InventoryItemData> Items;
            public int Count { get { return Cards != null ? Cards.Count : Items != null ? Items.Count : 0; } }

            /// <summary>One card's or item's chance per pack (Once: everything is given).</summary>
            public float Each { get { return Once || Count == 0 ? 1f : 1f - Mathf.Pow(1f - Chance / Count, Draws); } }
        }

        private class Group
        {
            public string Title, Note;
            public readonly List<Pool> Pools = new List<Pool>();
        }

        private class Contents
        {
            public readonly List<Group> Groups = new List<Group>();
            public int CardsOwned, CardsTotal, AllOwned, AllTotal, ItemCount;
        }

        // ------------------------------------------------------------------
        // State
        // ------------------------------------------------------------------

        private UIPremiumStoreViewModel m_Store;
        private float m_NextFind;
        private InventoryItemData m_Checked;
        private Contents m_CheckedContents;
        private Rect m_BarRect, m_LastFrame;
        private readonly Controls.UiBlocker m_Block = new Controls.UiBlocker();

        // The mod's Details window.
        private bool m_Open;
        private InventoryItemData m_Item;
        private Contents m_Contents;
        private readonly Grouped m_View = new Grouped();

        // The game's Details window: the grouped view in place of its cards.
        private bool m_GameItems;
        private Transform m_GameDetails;
        private readonly Grouped m_GameView = new Grouped();
        private Rect m_GameButton, m_GameArea;

        // The right click's detail panel.
        private object m_Detail;
        private Pool m_DetailPool;

        private static PackContents s_Instance;
        private void Awake() { s_Instance = this; }

        /// <summary>Dev "packlist [scroll]": the Details view of the store's selected item (the game's when it has one).</summary>
        public static string DevOpen(float scroll)
        {
            if (s_Instance == null || s_Instance.m_Store == null) return "no store";
            if (scroll < 0)
            {
                // "packlist close": the mod's windows closed (the game's Details window back to its cards).
                s_Instance.m_Open = false; s_Instance.m_Detail = null;
                if (s_Instance.m_GameItems) s_Instance.ShowGame(s_Instance.m_GameDetails, false);
                return "closed";
            }
            InventoryItemData item = s_Instance.Selected();
            Contents c = item != null ? Describe(item) : null;
            if (c == null) return "no described item selected";
            Transform d = s_Instance.GameDetails();
            if (d != null) { s_Instance.ShowGame(d, true); s_Instance.m_GameView.Scroll.y = scroll; }
            else { s_Instance.Show(item, c); s_Instance.m_View.Scroll.y = scroll; }
            return item.m_Name.Trim() + ": cards " + c.CardsOwned + "/" + c.CardsTotal + ", all " + c.AllOwned + "/" + c.AllTotal + ", " + c.Groups.Count + " group(s)";
        }

        /// <summary>Dev "packdetail &lt;name&gt;": the right click's panel of a card or item of the shown product.</summary>
        public static string DevDetail(string name)
        {
            Contents c = s_Instance != null ? (s_Instance.m_Open ? s_Instance.m_Contents : s_Instance.m_CheckedContents) : null;
            if (c == null) return "nothing shown";
            foreach (Group g in c.Groups)
                foreach (Pool p in g.Pools)
                {
                    if (p.Cards != null) foreach (CardTemplate t in p.Cards) if (t.m_Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) { s_Instance.m_Detail = t; s_Instance.m_DetailPool = p; return "card " + t.m_Name + "\n" + t.m_GameText; }
                    if (p.Items != null) foreach (InventoryItemData i in p.Items) if (i.m_Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) { s_Instance.m_Detail = i; s_Instance.m_DetailPool = p; return "item " + i.m_Name + "\n" + Raw(i); }
                }
            return "not found";
        }

        /// <summary>The raw texts of an item (dev "packdetail"): to see the game's markup.</summary>
        private static string Raw(InventoryItemData i)
        {
            string s = i.m_Description;
            InventoryMercenaryData m = i as InventoryMercenaryData;
            MercenaryTemplate t = null;
            try { t = m != null ? m.MercenaryTemplate : null; } catch { }
            if (t != null) s += "\n--passive: " + t.m_PartyPassiveGameText + " | " + t.m_PartyPassiveDescription + "\n--text: " + t.m_GameText + "\n--deck: " + t.m_DeckRestrictions;
            return s;
        }

        private void Update()
        {
            if (m_Store == null && Time.realtimeSinceStartup >= m_NextFind)
            {
                m_NextFind = Time.realtimeSinceStartup + 1f;
                m_Store = FindObjectOfType<UIPremiumStoreViewModel>();
            }
            if (m_Store == null || !m_Store.isActiveAndEnabled)
            {
                // The store closed with the game's Details window on the drops: its cards shown again for the next time.
                if (m_GameItems) SetCardsVisible(m_GameDetails, true);
                m_GameItems = false; m_GameButton = new Rect(); m_GameArea = new Rect();
                m_Open = false; m_Detail = null; m_BarRect = new Rect(); m_Checked = null; m_Block.Release();
                return;
            }
            InventoryItemData item = Selected();
            if (item != m_Checked) { m_Checked = item; m_CheckedContents = item != null ? Describe(item) : null; }
            Transform d = GameDetails();
            if (d == null && m_GameItems) { SetCardsVisible(m_GameDetails, true); m_GameItems = false; m_Detail = null; }
            if (d == null) { m_GameButton = new Rect(); m_GameArea = new Rect(); }
            m_Block.Update(m_Open || m_Detail != null, m_BarRect, m_GameButton, m_GameArea);
        }

        private InventoryItemData Selected()
        {
            try
            {
                UIPremiumStoreItemContext it = m_Store.Context != null && m_Store.Context.Catalog != null ? m_Store.Context.Catalog.SelectedItem : null;
                return it != null ? it.ItemData : null;
            }
            catch { return null; }
        }

        private void OnGUI()
        {
            if (m_Store == null || !m_Store.isActiveAndEnabled) return;
            float S = GameSkin.Scale;
            GUI.depth = -850;
            if (m_Open) DrawWindow(S);
            else
            {
                DrawGameDetails(S);
                if (m_CheckedContents != null && !GamePopupOpen()) DrawBar(S, m_Checked, m_CheckedContents);
                else m_BarRect = new Rect();
            }
            if (m_Detail != null) DrawDetail(S);
        }

        /// <summary>One of the game's windows over the store (Details, Card Count, a confirmation): the bar is not drawn over it.</summary>
        private bool GamePopupOpen()
        {
            foreach (Transform t in m_Store.transform)
                if (t.name.StartsWith("Panel_Popup", StringComparison.Ordinal) && Shown(t)) return true;
            foreach (UIModalPopupViewModel m in FindObjectsOfType<UIModalPopupViewModel>())
                if (m != null && m.isActiveAndEnabled) return true;
            return false;
        }

        /// <summary>
        /// Whether one of the store's popups is on the screen: they stay active, closed ones have no active and enabled
        /// widget inside (the Details window's frame is deactivated, the Card Count's anchor disabled).
        /// </summary>
        private static bool Shown(Transform popup)
        {
            UIWidget w = popup.GetComponentInChildren<UIWidget>();
            return w != null && w.enabled && w.finalAlpha > 0.01f;
        }

        // ------------------------------------------------------------------
        // The bar on the item panel
        // ------------------------------------------------------------------

        private void DrawBar(float S, InventoryItemData item, Contents c)
        {
            Transform frame = m_Store.transform.Find("Panel_R/Panel_Overlay/Frame_Details");
            bool gameBar = frame != null && frame.gameObject.activeInHierarchy;
            // The game's card count frame, shown or hidden: its widget keeps its place on the screen.
            UIWidget fw = frame != null ? frame.GetComponent<UIWidget>() : null;
            Rect f = fw != null ? WidgetRect(fw) : new Rect();
            if (f.width > 0) m_LastFrame = f; else f = m_LastFrame;
            if (f.width <= 0) f = new Rect(Screen.width * 0.625f, Screen.height * 0.15f, 392 * S, 34 * S);
            float h = 34 * S;
            if (gameBar)
            {
                // The game has its card count and Details window: only the "All items" figure under them.
                float w = Mathf.Max(f.width, 300 * S);
                Rect line = new Rect(f.center.x - w / 2f, f.yMax + 4 * S, w, 28 * S);
                m_BarRect = new Rect();
                if (!GameSkin.Sprite(line, "A_Shared_Atlas", "Dropdown_Panel")) GUI.DrawTexture(line, GameSkin.Solid(new Color(0.05f, 0.06f, 0.09f, 0.9f)));
                GameSkin.Label(line, Loc.Get("pc_all_items", Pct(c.AllOwned, c.AllTotal)), GameSkin.Style("bold", 15, GameSkin.Gold, TextAnchor.MiddleCenter));
                return;
            }
            float bw = 150 * S, barW = 620 * S;
            Rect bar = new Rect(f.center.x - barW / 2f, f.y, barW, h);
            m_BarRect = bar;
            if (!GameSkin.Sprite(bar, "A_Shared_Atlas", "Dropdown_Panel")) GUI.DrawTexture(bar, GameSkin.Solid(new Color(0.05f, 0.06f, 0.09f, 0.9f)));
            Rect details = new Rect(bar.xMax - bw - 4 * S, bar.y + 2 * S, bw, h - 4 * S);
            GameSkin.Label(new Rect(bar.x + 12 * S, bar.y, details.x - bar.x - 16 * S, h), Stats(c), GameSkin.Style("bold", 15, GameSkin.Gold, TextAnchor.MiddleCenter));
            if (GameSkin.GoldButton(details, Loc.Get("pc_details"), true, 14)) Show(item, c);
        }

        private static string Stats(Contents c)
        {
            return c.CardsTotal > 0 ? Loc.Get("pc_bar", Pct(c.CardsOwned, c.CardsTotal), Pct(c.AllOwned, c.AllTotal)) : Loc.Get("pc_all_items", Pct(c.AllOwned, c.AllTotal));
        }

        private static string Pct(int owned, int total)
        {
            return total <= 0 ? "0/0" : owned + "/" + total + " (" + Mathf.FloorToInt(100f * owned / total) + "%)";
        }

        /// <summary>A chance as a percentage: "12.5%", "0.74%", "100%".</summary>
        private static string Rate(float p)
        {
            float v = p * 100f;
            string s = v >= 99.95f ? "100" : v >= 10f ? v.ToString("0.#") : v >= 1f ? v.ToString("0.##") : v.ToString("0.###");
            return s + "%";
        }

        // ------------------------------------------------------------------
        // The mod's Details window
        // ------------------------------------------------------------------

        private void Show(InventoryItemData item, Contents c)
        {
            m_Item = item;
            m_Contents = c;
            m_Open = true;
            m_View.Reset();
        }

        private void DrawWindow(float S)
        {
            Rect win = Controls.Window(1080, 880, Loc.Get("pc_details_title", m_Item.m_Name.Trim()));
            float pad = 34 * S;
            GameSkin.Label(new Rect(win.x + pad, win.y + 50 * S, win.width - 2 * pad, 28 * S), Stats(m_Contents), GameSkin.Style("bold", 17, GameSkin.Gold, TextAnchor.MiddleCenter));
            Rect area = new Rect(win.x + pad, win.y + 84 * S, win.width - 2 * pad, win.height - 168 * S);
            Controls.Frame(area, null);
            Rect inner = new Rect(area.x + 8 * S, area.y + 8 * S, area.width - 16 * S, area.height - 16 * S);
            m_View.Draw(inner, m_Contents, S, m_Detail == null, OpenDetail);
            if (GameSkin.RedButton(new Rect(win.center.x - 90 * S, win.yMax - 70 * S, 180 * S, 52 * S), Loc.Get("close")) && m_Detail == null) m_Open = false;
        }

        private void OpenDetail(object o, Pool p) { m_Detail = o; m_DetailPool = p; }

        // ------------------------------------------------------------------
        // The game's Details window
        // ------------------------------------------------------------------

        private Transform GameDetails()
        {
            Transform d = m_Store != null ? m_Store.transform.Find("Panel_Popup_Details") : null;
            return d != null && Shown(d) ? d.Find("Frame_Details") : null;
        }

        /// <summary>The card grid hidden while the grouped view is shown in its frame (its cards would show through).</summary>
        private static void SetCardsVisible(Transform details, bool visible)
        {
            if (details == null) return;
            Transform panel = details.Find("Frame_Scroll/Panel_Cards");
            UIPanel p = panel != null ? panel.GetComponent<UIPanel>() : null;
            if (p != null) p.alpha = visible ? 1f : 0f;
            Transform bar = details.Find("Frame_Scroll/ScrollBar_New");
            if (bar != null) bar.gameObject.SetActive(visible);
            Transform toggle = details.Find("Button_Toggle");   // "Show cards not owned": about the cards only
            if (toggle != null) toggle.gameObject.SetActive(visible);
        }

        private void ShowGame(Transform d, bool on)
        {
            m_GameItems = on;
            m_GameDetails = d;
            m_GameView.Reset();
            SetCardsVisible(d, !on);
        }

        private void DrawGameDetails(float S)
        {
            Transform d = GameDetails();
            if (d == null || m_CheckedContents == null) return;
            Transform close = d.Find("Button_Close"), frame = d.Find("Frame_Scroll"), inset = d.Find("Frame_Inset");
            UIWidget cw = close != null ? close.GetComponent<UIWidget>() : null, fw = frame != null ? frame.GetComponent<UIWidget>() : null;
            UIWidget iw = inset != null ? inset.GetComponent<UIWidget>() : null;
            if (cw == null || fw == null) return;
            // "All items" in the window's top frame, right of the game's own "collected" box.
            if (iw != null)
            {
                Rect ir = WidgetRect(iw);
                Rect line = new Rect(ir.xMax - 520 * S, ir.y + ir.height * 0.5f - 18 * S, 500 * S, 36 * S);
                GameSkin.Label(line, Loc.Get("pc_all_items", Pct(m_CheckedContents.AllOwned, m_CheckedContents.AllTotal)), GameSkin.Style("title", 22, GameSkin.Gold, TextAnchor.MiddleRight));
            }
            Rect cr = WidgetRect(cw);
            m_GameButton = new Rect(cr.x - 300 * S, cr.y, 290 * S, cr.height);
            if (GameSkin.GoldButton(m_GameButton, m_GameItems ? Loc.Get("pc_back_cards") : Loc.Get("pc_rates"), m_Detail == null, 17)) ShowGame(d, !m_GameItems);
            if (!m_GameItems) { m_GameArea = new Rect(); return; }
            Rect fr = WidgetRect(fw);
            m_GameArea = fr;
            GUI.DrawTexture(new Rect(fr.x + 6 * S, fr.y + 6 * S, fr.width - 12 * S, fr.height - 12 * S), GameSkin.Solid(new Color(0.03f, 0.035f, 0.05f, 1f)));
            m_GameView.Draw(new Rect(fr.x + 14 * S, fr.y + 12 * S, fr.width - 28 * S, fr.height - 24 * S), m_CheckedContents, S, m_Detail == null, OpenDetail);
        }

        // ------------------------------------------------------------------
        // The grouped view (both windows)
        // ------------------------------------------------------------------

        private const float TileW = 136f, CardH = 214f, ItemH = 236f, TileGap = 10f;

        private class Row
        {
            public Group Group;
            public Pool Pool;
            public int Start = -1, Length;   // tiles of Pool from Start
            public float Height, Y;
        }

        private class Grouped
        {
            public Vector2 Scroll;
            private List<Row> m_Rows;
            private Contents m_For;
            private float m_Width, m_Height;
            private int m_PerRow;

            public void Reset() { Scroll = Vector2.zero; m_Rows = null; m_For = null; }

            private void Build(Contents c, float width, float S)
            {
                m_For = c; m_Width = width;
                m_Rows = new List<Row>();
                m_PerRow = Mathf.Max(1, (int)((width + TileGap * S) / ((TileW + TileGap) * S)));
                foreach (Group g in c.Groups)
                {
                    m_Rows.Add(new Row { Group = g, Height = (g.Note != null ? 62 : 40) * S });
                    foreach (Pool p in g.Pools)
                    {
                        m_Rows.Add(new Row { Pool = p, Height = 30 * S });
                        float h = (p.Cards != null ? CardH : ItemH) * S;
                        for (int i = 0; i < p.Count; i += m_PerRow)
                            m_Rows.Add(new Row { Pool = p, Start = i, Length = Math.Min(m_PerRow, p.Count - i), Height = h });
                    }
                }
                float y = 0;
                foreach (Row r in m_Rows) { r.Y = y; y += r.Height + 6 * S; }
                m_Height = y;
            }

            public void Draw(Rect inner, Contents c, float S, bool interactive, Action<object, Pool> detail)
            {
                float width = inner.width - 24 * S;
                if (m_Rows == null || m_For != c || Mathf.Abs(m_Width - width) > 1f) Build(c, width, S);
                Rect view = new Rect(0, 0, inner.width - 20 * S, m_Height);
                Scroll = GUI.BeginScrollView(inner, Scroll, view, false, false, GUIStyle.none, m_Height > inner.height ? GameSkin.ScrollBar : GUIStyle.none);
                // In sight, in the scroll area's coordinates: the pictures drawn with the game's material are cut to it.
                Rect visible = new Rect(Scroll.x, Scroll.y, inner.width, inner.height);
                float x0 = (width - (m_PerRow * (TileW + TileGap) * S - TileGap * S)) / 2f;
                GUIStyle gTitle = GameSkin.Style("title", 21, GameSkin.Gold, TextAnchor.MiddleLeft);
                GUIStyle gNote = GameSkin.Style("body", 14, GameSkin.Text, TextAnchor.UpperLeft, true);
                GUIStyle pTitle = GameSkin.Style("bold", 16, GameSkin.Text, TextAnchor.MiddleLeft);
                GUIStyle pRate = GameSkin.Style("body", 15, GameSkin.GoldDim, TextAnchor.MiddleRight);
                foreach (Row r in m_Rows)
                {
                    if (r.Y + r.Height < visible.y || r.Y > visible.yMax) continue;   // only the rows in sight
                    if (r.Group != null)
                    {
                        GUI.DrawTexture(new Rect(0, r.Y + 2 * S, width, 2 * S), GameSkin.Solid(new Color(GameSkin.Gold.r, GameSkin.Gold.g, GameSkin.Gold.b, 0.5f)));
                        GameSkin.Label(new Rect(4 * S, r.Y + 6 * S, width, 30 * S), r.Group.Title, gTitle);
                        if (r.Group.Note != null) GUI.Label(new Rect(8 * S, r.Y + 36 * S, width - 16 * S, 26 * S), r.Group.Note, gNote);
                    }
                    else if (r.Start < 0)
                    {
                        Rect pr = new Rect(16 * S, r.Y, width - 16 * S, r.Height);
                        GUI.Label(pr, r.Pool.Title + "  (" + r.Pool.Count + ")", pTitle);
                        GUI.Label(pr, PoolRate(r.Pool), pRate);
                    }
                    else
                        for (int i = 0; i < r.Length; i++)
                        {
                            Rect tile = new Rect(x0 + i * (TileW + TileGap) * S, r.Y, TileW * S, r.Height);
                            object o = r.Pool.Cards != null ? (object)r.Pool.Cards[r.Start + i] : r.Pool.Items[r.Start + i];
                            if (o is CardTemplate) CardTile(tile, (CardTemplate)o, S);
                            else ItemTile(tile, (InventoryItemData)o, visible, S);
                            // A right click: the detail panel (a left click on a card does nothing either).
                            Event e = Event.current;
                            if (interactive && e.type == EventType.MouseDown && e.button == 1 && tile.Contains(e.mousePosition)) { detail(o, r.Pool); e.Use(); }
                        }
                }
                GUI.EndScrollView();
            }
        }

        /// <summary>"12.5% · each 0.74%", "4 per pack · each 2.5%", "each one".</summary>
        private static string PoolRate(Pool p)
        {
            if (p.Once) return Loc.Get("r_all");
            string head = p.Draws > 1 ? Loc.Get("r_draws", p.Draws) : Rate(p.Chance);
            return p.Count > 1 ? Loc.Get("r_each", head, Rate(p.Each)) : head;
        }

        private static void CardTile(Rect tile, CardTemplate c, float S)
        {
            Rect card = new Rect(tile.x, tile.y, tile.width, tile.height - 20 * S);
            CardArt.Tile(card, c);
            int n = Collection.Count(c.m_Id);
            GUI.Label(new Rect(tile.x, card.yMax, tile.width, 20 * S), Loc.Get("pc_owned", n),
                n > 0 ? GameSkin.Style("bold", 12, GameSkin.Gold, TextAnchor.MiddleCenter) : GameSkin.Style("body", 12, GameSkin.Muted, TextAnchor.MiddleCenter));
        }

        private static void ItemTile(Rect tile, InventoryItemData it, Rect clip, float S)
        {
            GameSkin.Sprite(tile, "A_Frames", "Frame_Inner");
            float pic = tile.width - 30 * S;
            Rect pr = new Rect(tile.center.x - pic / 2f, tile.y + 6 * S, pic, pic);
            Texture2D t = Icon(it);
            if (t != null) DrawIcon(pr, clip, t, it);
            else GUI.DrawTexture(pr, GameSkin.Solid(new Color(0.1f, 0.1f, 0.13f, 1f)));
            float y = pr.yMax + 2 * S;
            GUI.Label(new Rect(tile.x + 4 * S, y, tile.width - 8 * S, 34 * S), it.m_Name.Trim(), GameSkin.Style("bold", 13, GameSkin.Text, TextAnchor.UpperCenter, true));
            y += 34 * S;
            // Equipment: slot and rarity, then the card it changes; mercenaries: their shards.
            GUI.Label(new Rect(tile.x + 4 * S, y, tile.width - 8 * S, 18 * S), Kind(it), GameSkin.Style("body", 12, GameSkin.GoldDim, TextAnchor.UpperCenter));
            y += 18 * S;
            string second = Second(it);
            if (second != null) GUI.Label(new Rect(tile.x + 4 * S, y, tile.width - 8 * S, 30 * S), second, GameSkin.Style("body", 11, GameSkin.Text, TextAnchor.UpperCenter, true));
            int n = Inventory.Count(it.m_Id);
            GUI.Label(new Rect(tile.x, tile.yMax - 20 * S, tile.width, 18 * S), Loc.Get("pc_owned", n),
                n > 0 ? GameSkin.Style("bold", 12, GameSkin.Gold, TextAnchor.MiddleCenter) : GameSkin.Style("body", 12, GameSkin.Muted, TextAnchor.MiddleCenter));
        }

        private static string Kind(InventoryItemData it)
        {
            InventoryEquipmentData eq = it as InventoryEquipmentData;
            if (eq != null) return SlotName(eq.m_EquipmentType) + " · " + RarityName(eq.m_Rarity);
            if (it is InventoryMercenaryData) return Loc.Get("pc_mercenary");
            if (it is InventoryDeckSleeve) return Loc.Get("pc_sleeve");
            if (it is InventoryCardPack) return Loc.Get("pc_pack");
            return "";
        }

        /// <summary>The tile's second line: the card an equipment changes, a mercenary's shards.</summary>
        private static string Second(InventoryItemData it)
        {
            InventoryEquipmentData eq = it as InventoryEquipmentData;
            if (eq != null) { CardTemplate c = EquippedCard(eq); return c != null ? c.m_Name : null; }
            InventoryMercenaryData m = it as InventoryMercenaryData;
            if (m != null) { string s = MercShards(m); return s.Length > 0 ? s : null; }
            return null;
        }

        private static CardTemplate EquippedCard(InventoryEquipmentData eq)
        {
            try { return Singleton<TemplateManager>.Instance.GetPrimaryCardTemplateForEquipmentItem(eq); } catch { return null; }
        }

        /// <summary>A mercenary's shards (MercenaryTemplate.m_ShardAlignment: the shards its deck may use).</summary>
        private static string MercShards(InventoryMercenaryData m)
        {
            try
            {
                MercenaryTemplate t = m.MercenaryTemplate;
                if (t == null || t.m_ShardAlignment == null) return "";
                List<string> l = new List<string>();
                foreach (KeyValuePair<ECardShards, Dictionary<ERarity, int>> kv in t.m_ShardAlignment)
                {
                    bool any = false;
                    if (kv.Value != null) foreach (int n in kv.Value.Values) if (n > 0) any = true;
                    if (any || kv.Value == null || kv.Value.Count == 0) l.Add(ShardName(kv.Key));
                }
                return string.Join(", ", l.ToArray());
            }
            catch { return ""; }
        }

        private static string ShardName(ECardShards s)
        {
            switch (s)
            {
                case ECardShards.Blood: return Loc.Get("shard_blood");
                case ECardShards.Diamond: return Loc.Get("shard_diamond");
                case ECardShards.Ruby: return Loc.Get("shard_ruby");
                case ECardShards.Sapphire: return Loc.Get("shard_sapphire");
                case ECardShards.Wild: return Loc.Get("shard_wild");
                case ECardShards.Colorless: return Loc.Get("shard_colorless");
                default: return s.ToString();
            }
        }

        private static string SlotName(EEquipmentType t)
        {
            switch (t)
            {
                case EEquipmentType.Head: return Loc.Get("slot_head");
                case EEquipmentType.Chest: return Loc.Get("slot_chest");
                case EEquipmentType.Gloves: return Loc.Get("slot_gloves");
                case EEquipmentType.Feet: return Loc.Get("slot_feet");
                case EEquipmentType.Weapon: return Loc.Get("slot_weapon");
                default: return Loc.Get("slot_other");
            }
        }

        /// <summary>A game text for IMGUI's rich text: paragraphs, card links and symbols ("[ATK]", "[(1)]") made readable.</summary>
        private static string Plain(string s, string self)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("<p>", "\n").Replace("<P>", "\n").Replace("#SELF#", self ?? "");
            s = Regex.Replace(s, "</?a[^>]*>", "");
            s = Regex.Replace(s, @"\[\(([^)\]]*)\)\]", "($1)");
            // Champion powers: "[BASIC]: [1] [ARROWR] text" as "Basic (1): text" (the game draws these as symbols).
            s = Regex.Replace(s, @"\[([A-Z]+)\]:\s*\[(\d+)\]\s*\[ARROWR\]", m => m.Groups[1].Value.Substring(0, 1) + m.Groups[1].Value.Substring(1).ToLowerInvariant() + " (" + m.Groups[2].Value + "):");
            s = s.Replace("[ATK]", " ATK").Replace("[DEF]", " DEF").Replace("[ARROWR]", ":").Replace("[ACT]", "[" + Loc.Get("sym_exhaust") + "]");
            return s.Trim();
        }

        // ------------------------------------------------------------------
        // The right click's detail panel
        // ------------------------------------------------------------------

        private void DrawDetail(float S)
        {
            // Over everything, the rest darkened; Close or a click outside closes it.
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), GameSkin.Solid(new Color(0, 0, 0, 0.55f)));
            float w = Mathf.Min(900 * S, Screen.width - 40), h = Mathf.Min(560 * S, Screen.height - 40);
            Rect box = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            if (!GameSkin.Sprite(box, "A_Frames", "Frame_With_Header_Hard_Corner")) GUI.DrawTexture(box, GameSkin.Solid(new Color(0.06f, 0.07f, 0.1f, 0.98f)));
            float pad = 26 * S, pic = h - 2 * pad - 70 * S;
            Rect pr = new Rect(box.x + pad, box.y + pad + 20 * S, Mathf.Min(pic, w * 0.38f), pic);
            Rect text = new Rect(pr.xMax + pad, box.y + pad + 10 * S, box.xMax - pr.xMax - 2 * pad, h - 2 * pad - 70 * S);
            string title, body;
            CardTemplate card = m_Detail as CardTemplate;
            InventoryItemData item = m_Detail as InventoryItemData;
            if (card != null)
            {
                CardArt.Tile(new Rect(pr.x, pr.y, pr.width, pr.width * 1.45f > pr.height ? pr.height : pr.width * 1.45f), card);
                title = card.m_Name;
                body = Loc.Get("d_card", card.m_ResourceCost, card.m_CardType, RarityName(card.m_CardRarity)) + "\n\n" + Plain(card.m_GameText, card.m_Name)
                    + "\n\n" + Loc.Get("pc_owned", Collection.Count(card.m_Id));
            }
            else
            {
                Texture2D t = Icon(item);
                Rect sq = new Rect(pr.x, pr.y, pr.width, pr.width);
                if (t != null) DrawIcon(sq, new Rect(0, 0, Screen.width, Screen.height), t, item);
                title = item.m_Name.Trim();
                body = ItemText(item) + "\n\n" + Loc.Get("pc_owned", Inventory.Count(item.m_Id));
            }
            if (m_DetailPool != null)
                body += "\n" + Loc.Get("d_drop", m_DetailPool.Title, m_DetailPool.Once ? Loc.Get("r_all") : Rate(m_DetailPool.Each));
            GameSkin.Label(new Rect(text.x, text.y, text.width, 36 * S), title, GameSkin.Style("title", 24, GameSkin.Gold));
            // The game font has no bold face (IMGUI squeezes the letters): bold as gold.
            body = body.Replace("<b>", "<color=#E8C56A>").Replace("</b>", "</color>");
            GUIStyle bs = GameSkin.Style("body", 16, GameSkin.Text, TextAnchor.UpperLeft, true);
            bs.richText = true;
            GUI.Label(new Rect(text.x, text.y + 44 * S, text.width, text.height - 44 * S), body, bs);
            Event e = Event.current;
            bool outside = e.type == EventType.MouseDown && !box.Contains(e.mousePosition);
            if (GameSkin.RedButton(new Rect(box.center.x - 90 * S, box.yMax - 66 * S, 180 * S, 50 * S), Loc.Get("close")) || outside)
            {
                m_Detail = null;
                if (outside) e.Use();
            }
        }

        private static string ItemText(InventoryItemData item)
        {
            List<string> parts = new List<string>();
            InventoryEquipmentData eq = item as InventoryEquipmentData;
            if (eq != null)
            {
                parts.Add(Kind(eq));
                CardTemplate c = EquippedCard(eq);
                if (c != null) parts.Add(Loc.Get("d_changes", c.m_Name));
                parts.Add(Plain(item.m_Description, c != null ? c.m_Name : null));
                return string.Join("\n\n", parts.ToArray());
            }
            InventoryMercenaryData m = item as InventoryMercenaryData;
            if (m != null)
            {
                MercenaryTemplate t = null;
                try { t = m.MercenaryTemplate; } catch { }
                parts.Add(Loc.Get("pc_mercenary") + (t != null ? " · " + Loc.Get("d_health", t.m_StartingHealth) : ""));
                string shards = MercShards(m);
                if (shards.Length > 0) parts.Add(Loc.Get("d_shards", shards));
                if (t != null)
                {
                    string passive = !string.IsNullOrEmpty(t.m_PartyPassiveGameText) ? t.m_PartyPassiveGameText : t.m_PartyPassiveDescription;
                    if (!string.IsNullOrEmpty(passive)) parts.Add(Loc.Get("d_party", Plain(passive, m.m_Name)));
                    if (!string.IsNullOrEmpty(t.m_GameText)) parts.Add(Plain(t.m_GameText, m.m_Name));
                    if (!string.IsNullOrEmpty(t.m_DeckRestrictions)) parts.Add(Loc.Get("d_deck", Plain(t.m_DeckRestrictions, m.m_Name)));
                }
                return string.Join("\n\n", parts.ToArray());
            }
            parts.Add(Kind(item));
            if (!string.IsNullOrEmpty(item.m_Description)) parts.Add(Plain(item.m_Description, item.m_Name));
            return string.Join("\n\n", parts.ToArray());
        }

        // ------------------------------------------------------------------
        // Item pictures
        // ------------------------------------------------------------------

        private static Material s_IconMaterial;
        private static bool s_IconMaterialTried;

        /// <summary>
        /// An item's picture drawn as the game's inventory icons are (GoInventoryIcon.CreateTexture / SetEquipmentFromData):
        /// with the game's "M_Inventory_Icon" material and the item's m_HueSat as the color (the equipment pictures are
        /// silhouettes the material fills and colors). Graphics.DrawTexture ignores the GUI's clipping: the picture is cut
        /// to clip (the visible area, in the same coordinates) by hand.
        /// </summary>
        private static void DrawIcon(Rect r, Rect clip, Texture2D t, InventoryItemData it)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!s_IconMaterialTried)
            {
                s_IconMaterialTried = true;
                Material m = Resources.Load("M_Inventory_Icon", typeof(Material)) as Material;
                if (m != null) s_IconMaterial = new Material(m);
                else Debug.LogWarning("[HexRevival] [PackContents] M_Inventory_Icon not found");
            }
            float side = Mathf.Min(r.width, r.height);
            Rect sq = new Rect(r.center.x - side / 2f, r.center.y - side / 2f, side, side);
            Rect cut = Rect.MinMaxRect(Mathf.Max(sq.xMin, clip.xMin), Mathf.Max(sq.yMin, clip.yMin), Mathf.Min(sq.xMax, clip.xMax), Mathf.Min(sq.yMax, clip.yMax));
            if (cut.width <= 0 || cut.height <= 0) return;
            // The part of the texture left (texture coordinates count from the bottom).
            Rect src = new Rect((cut.xMin - sq.xMin) / side, (sq.yMax - cut.yMax) / side, cut.width / side, cut.height / side);
            if (s_IconMaterial == null) { GUI.DrawTextureWithTexCoords(cut, t, src); return; }
            Graphics.DrawTexture(cut, t, src, 0, 0, 0, 0, HueSat(it.m_HueSat), s_IconMaterial);
        }

        /// <summary>m_HueSat ("RRGGBB" or "RRGGBBAA", with or without "#") as the color passed to the material (0.5 = as is,
        /// as Graphics.DrawTexture's color); white if empty or unreadable.</summary>
        private static Color HueSat(string hex)
        {
            Color c = Color.white;
            if (!string.IsNullOrEmpty(hex))
            {
                string h = hex.Trim().TrimStart('#');
                try
                {
                    if (h.Length >= 6)
                    {
                        c.r = Convert.ToInt32(h.Substring(0, 2), 16) / 255f;
                        c.g = Convert.ToInt32(h.Substring(2, 2), 16) / 255f;
                        c.b = Convert.ToInt32(h.Substring(4, 2), 16) / 255f;
                        c.a = h.Length >= 8 ? Convert.ToInt32(h.Substring(6, 2), 16) / 255f : 1f;
                    }
                }
                catch { c = Color.white; }
            }
            return new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, c.a * 0.5f);
        }

        private static readonly Dictionary<ResourceId, Texture2D> s_Icons = new Dictionary<ResourceId, Texture2D>();

        /// <summary>An item's picture from the game's icon bundle (icons.ab: the item's texture, or its "_Thumb"); null if none.</summary>
        internal static Texture2D Icon(InventoryItemData item)
        {
            Texture2D t;
            if (s_Icons.TryGetValue(item.m_Id, out t)) return t;
            t = null;
            try
            {
                string tex = string.IsNullOrEmpty(item.m_TexturePath) ? null : FolderUtils.GetFileNameWithoutPathOrExtension(item.m_TexturePath);
                if (tex != null && AssetBundleManager.Instance != null)
                    t = (AssetBundleManager.Instance.GetAsset(AssetBundleFile.Icons, tex, AssetType.Texture2D) as Texture2D)
                        ?? (AssetBundleManager.Instance.GetAsset(AssetBundleFile.Icons, tex + "_Thumb", AssetType.Texture2D) as Texture2D);
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [PackContents] icon " + item.m_Name + ": " + e.Message); }
            s_Icons[item.m_Id] = t;
            return t;
        }

        /// <summary>
        /// Dev "iconcheck [all]": per item type, how many have a picture in the icon bundle; the missing ones listed
        /// (FutureRelease items, never given, left out unless "all").
        /// </summary>
        public static string IconCheck(bool all)
        {
            Dictionary<string, int[]> byType = new Dictionary<string, int[]>();
            List<string> missing = new List<string>();
            foreach (InventoryItemData i in Singleton<TemplateManager>.Instance.InventoryItems.Values)
            {
                if (i == null || (!all && i.m_BuildTag == "FutureRelease")) continue;
                string type = i.GetType().Name;
                int[] n;
                if (!byType.TryGetValue(type, out n)) byType[type] = n = new int[2];
                n[0]++;
                if (Icon(i) != null) { n[1]++; continue; }
                if (missing.Count < 60) missing.Add(type + ": " + (i.m_Name ?? "?").Trim() + " [" + (i.m_TexturePath ?? "-") + "] " + i.m_BuildTag);
            }
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, int[]> kv in byType) lines.Add(kv.Key + " " + kv.Value[1] + "/" + kv.Value[0]);
            return string.Join("\n", lines.ToArray()) + "\n--- missing:\n" + string.Join("\n", missing.ToArray());
        }

        // ------------------------------------------------------------------
        // What a product can give, by loot kind (the rates are the opening code's: Progression.OpenPack, Packs,
        // Zymmer.OpenChest, SpecialPacks, PrizePacks)
        // ------------------------------------------------------------------

        /// <summary>What the product can give; null if not described here.</summary>
        private static Contents Describe(InventoryItemData item)
        {
            Contents c = new Contents();
            try { if (!Fill(item, c)) return null; }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [PackContents] " + item.m_Name + ": " + e); return null; }
            c.Groups.RemoveAll(g => { g.Pools.RemoveAll(p => p.Count == 0); return g.Pools.Count == 0; });
            if (c.Groups.Count == 0) return null;
            HashSet<ResourceId> counted = new HashSet<ResourceId>(), cards = new HashSet<ResourceId>(), items = new HashSet<ResourceId>();
            foreach (Group g in c.Groups)
                foreach (Pool p in g.Pools)
                {
                    if (p.Cards != null) foreach (CardTemplate t in p.Cards) { cards.Add(t.m_Id); if (p.Counted) counted.Add(t.m_Id); }
                    if (p.Items != null) foreach (InventoryItemData i in p.Items) items.Add(i.m_Id);
                }
            c.CardsTotal = counted.Count;
            foreach (ResourceId id in counted) if (Collection.Count(id) > 0) c.CardsOwned++;
            c.AllTotal = cards.Count + items.Count;
            c.ItemCount = items.Count;
            foreach (ResourceId id in cards) if (Collection.Count(id) > 0) c.AllOwned++;
            foreach (ResourceId id in items) if (Inventory.Count(id) > 0) c.AllOwned++;
            return c;
        }

        private static Group G(Contents c, string title, string note = null)
        {
            Group g = new Group { Title = title, Note = note };
            c.Groups.Add(g);
            return g;
        }

        private static void Cards(Group g, string title, List<CardTemplate> pool, float chance, int draws, bool counted)
        {
            g.Pools.Add(new Pool { Title = title, Cards = Sorted(pool), Chance = chance, Draws = draws, Counted = counted });
        }

        private static void Cards(Group g, ERarity r, List<CardTemplate> pool, float chance, int draws = 1)
        {
            Cards(g, RarityName(r), pool, chance, draws, r != ERarity.Epic);   // the game's card count leaves the epics out
        }

        private static void Items(Group g, string title, List<InventoryItemData> items, float chance, int draws = 1)
        {
            g.Pools.Add(new Pool { Title = title, Items = SortedItems(items), Chance = chance, Draws = draws });
        }

        /// <summary>The product's loot groups; false if not described.</summary>
        private static bool Fill(InventoryItemData item, Contents c)
        {
            string name = (item.m_Name ?? "").Trim();
            if (name == "Siege Pack")
            {
                // PrizePacks.OpenSiegePack: one item.
                Group g = G(c, Loc.Get("g_one_item"), Loc.Get("n_never_twice"));
                Cards(g, Loc.Get("pc_pve_cards"), PrizePacks.PveCards(), 0.40f, 1, true);
                Cards(g, Loc.Get("pc_alt_shards"), PrizePacks.AltArtShards(), 0.20f, 1, true);
                Items(g, Loc.Get("pc_sleeves"), PrizePacks.SiegeSleeveItems(), 0.15f);
                Cards(g, Loc.Get("pc_alt_cards"), PrizePacks.AltArtCards(), 0.10f, 1, true);
                Items(g, Loc.Get("pc_equipment"), PrizePacks.SiegeEquipmentPool(), 0.10f);
                Items(g, Loc.Get("pc_mercenaries"), PrizePacks.SiegeMercenaryItems(), 0.05f);
                return true;
            }
            if (name == "Cosmic Coin Pack")
            {
                // PrizePacks.OpenCosmicPack: one item.
                Group g = G(c, Loc.Get("g_one_item"), Loc.Get("n_never_twice"));
                Cards(g, Loc.Get("pc_alt_cards"), PrizePacks.AltArtCards(), 0.65f, 1, true);
                InventoryItemData booster = PrizePacks.LatestBoosterItem();
                if (booster != null) Items(g, Loc.Get("pc_packs"), new List<InventoryItemData> { booster }, 0.30f);
                Items(g, Loc.Get("pc_sleeves"), PrizePacks.CosmicSleeveItems(), 0.05f);
                return true;
            }
            if (Zymmer.IsKismetPack(item))
            {
                List<Loot.Drop> bundle = Zymmer.KismetBundle(item.m_Name);
                if (bundle.Count == 0) return false;
                Bundle(G(c, Loc.Get("g_everything")), bundle);
                return true;
            }
            string kind = SpecialPacks.KindOf(item);
            if (kind == "randomkismet")
            {
                List<InventoryItemData> packs = new List<InventoryItemData>();
                foreach (InventoryItemData k in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                    if (Zymmer.IsKismetPack(k) && Zymmer.KismetBundle(k.m_Name).Count > 0) packs.Add(k);
                foreach (InventoryItemData k in packs)
                    Bundle(G(c, Loc.Get("g_one_pack", k.m_Name.Trim(), Rate(1f / packs.Count))), Zymmer.KismetBundle(k.m_Name));
                return packs.Count > 0;
            }
            if (kind == "giftbox")
            {
                string holiday = SpecialPacks.GiftBoxHoliday(item);
                Items(G(c, Loc.Get("g_gift_sleeve")), Loc.Get("pc_sleeves"), SpecialPacks.HolidaySleeves(holiday).ConvertAll(s => (InventoryItemData)s), 1f);
                Items(G(c, Loc.Get("g_gift_equipment")), Loc.Get("pc_equipment"), Sleeves.HolidayEquipmentPool(holiday), 1f);
                return true;
            }
            InventoryCardPack pack = item as InventoryCardPack;
            if (pack == null) return false;
            CardSetTemplate set;
            Singleton<TemplateManager>.Instance.CardSets.TryGetValue(pack.m_SetId, out set);
            switch (kind)
            {
                case "fullset":
                {
                    if (set == null) return false;
                    Group g = G(c, Loc.Get("g_fullset"));
                    foreach (ERarity r in new[] { ERarity.Legendary, ERarity.Epic, ERarity.Rare, ERarity.Uncommon, ERarity.Common })
                    {
                        List<CardTemplate> pool = Progression.Pool(set, r);
                        if (pool.Count > 0) g.Pools.Add(new Pool { Title = RarityName(r), Cards = Sorted(pool), Once = true, Counted = r != ERarity.Epic });
                    }
                    return true;
                }
                case "trials":
                {
                    if (set == null) return false;
                    // SpecialPacks: 6 commons, 2 uncommons, 1 rare of the pack's race, all different while enough.
                    string race = Array.Find(SpecialPacks.Races, x => item.m_Name.StartsWith(x));
                    Func<ERarity, List<CardTemplate>> of = r =>
                    {
                        List<CardTemplate> pool = Progression.Pool(set, r);
                        List<CardTemplate> l = race == null ? pool : pool.FindAll(t => t.HasSubtype(race == "Shin'hare" ? "Shin" : race));
                        return l.Count > 0 ? l : pool;
                    };
                    Cards(G(c, Loc.Get("g_per_pack", RarityName(ERarity.Rare), 1)), ERarity.Rare, of(ERarity.Rare), 1f);
                    Cards(G(c, Loc.Get("g_per_pack", RarityName(ERarity.Uncommon), 2)), ERarity.Uncommon, of(ERarity.Uncommon), 1f, 2);
                    Cards(G(c, Loc.Get("g_per_pack", RarityName(ERarity.Common), 6)), ERarity.Common, of(ERarity.Common), 1f, 6);
                    return true;
                }
                case "common":
                    Cards(G(c, Loc.Get("g_per_pack", RarityName(ERarity.Common), 10), Loc.Get("n_standard")), ERarity.Common, SpecialPacks.StandardPool(ERarity.Common), 1f, 10);
                    return true;
                case "gauntlet":
                    Cards(G(c, Loc.Get("g_per_pack", RarityName(ERarity.Uncommon), 4), Loc.Get("n_standard")), ERarity.Uncommon, SpecialPacks.StandardPool(ERarity.Uncommon), 1f, 4);
                    Cards(G(c, Loc.Get("g_per_pack", RarityName(ERarity.Common), 12)), ERarity.Common, SpecialPacks.StandardPool(ERarity.Common), 1f, 12);
                    return true;
                case "dualshard":
                {
                    if (set == null) return false;
                    Group g = G(c, Loc.Get("g_rare_slot"), Loc.Get("n_two_shards"));
                    Cards(g, ERarity.Legendary, Progression.Pool(set, ERarity.Legendary), 0.125f);
                    Cards(g, ERarity.Rare, Progression.Pool(set, ERarity.Rare), 0.875f);
                    Cards(G(c, Loc.Get("g_per_pack", RarityName(ERarity.Uncommon), 4)), ERarity.Uncommon, Progression.Pool(set, ERarity.Uncommon), 1f, 4);
                    Cards(G(c, Loc.Get("g_per_pack", RarityName(ERarity.Common), 12)), ERarity.Common, Progression.Pool(set, ERarity.Common), 1f, 12);
                    return true;
                }
                case "kismetdraft":
                {
                    Func<ERarity, List<CardTemplate>> all = r => { List<CardTemplate> l = new List<CardTemplate>(); foreach (CardSetTemplate s in SpecialPacks.BoosterSets()) l.AddRange(Progression.Pool(s, r)); return l; };
                    Group g = G(c, Loc.Get("g_rare_slot"), Loc.Get("n_all_sets"));
                    Cards(g, ERarity.Legendary, all(ERarity.Legendary), 0.125f);
                    Cards(g, ERarity.Rare, all(ERarity.Rare), 0.875f);
                    Cards(G(c, Loc.Get("g_per_pack", RarityName(ERarity.Uncommon), 4)), ERarity.Uncommon, all(ERarity.Uncommon), 1f, 4);
                    Cards(G(c, Loc.Get("g_per_pack", RarityName(ERarity.Common), 12)), ERarity.Common, all(ERarity.Common), 1f, 12);
                    return true;
                }
                case "gencon":
                    if (set == null) return false;
                    BoosterCards(c, set);
                    return true;
            }
            if (set == null) return false;
            if (pack.m_CardPackType == ECardPackType.PrimalPack)
            {
                // Packs.Primal: 2 legendaries and 13 rares; a legendary chest.
                Cards(G(c, Loc.Get("g_per_pack", RarityName(ERarity.Legendary), 2)), ERarity.Legendary, Progression.Pool(set, ERarity.Legendary), 1f, 2);
                Cards(G(c, Loc.Get("g_per_pack", RarityName(ERarity.Rare), 13)), ERarity.Rare, Progression.Pool(set, ERarity.Rare), 1f, 13);
                Chest(c, set, true);
                return true;
            }
            if (pack.m_CardPackType != ECardPackType.BoosterPack) return false;
            BoosterCards(c, set);
            Chest(c, set, false);
            // Packs: "Obtained by opening [set] Booster Packs" sleeves; Store: a bonus primal pack with a bought one.
            List<InventoryItemData> extra = Sleeves.Matching("^Obtained by opening " + Regex.Escape(set.m_Name.Trim()) + " Booster Packs").ConvertAll(s => (InventoryItemData)s);
            if (extra.Count > 0) Items(G(c, Loc.Get("g_sometimes"), Loc.Get("n_unowned")), Loc.Get("pc_sleeves"), extra, Packs.BoosterSleeveChance);
            ResourceId primal = Store.PrimalOf(set.m_Id);
            InventoryItemData primalItem = primal.IsValid ? Singleton<TemplateManager>.Instance.GetInventoryItem(primal) : null;
            if (primalItem != null) Items(G(c, Loc.Get("g_bonus")), Loc.Get("pc_packs"), new List<InventoryItemData> { primalItem }, Store.PrimalChance);
            return true;
        }

        /// <summary>Progression.OpenPack: the rare slot (a legendary 12.5%, else an epic 20%, else a rare), 4 uncommons, 12 commons.</summary>
        private static void BoosterCards(Contents c, CardSetTemplate set)
        {
            Group g = G(c, Loc.Get("g_rare_slot"));
            Cards(g, ERarity.Legendary, Progression.Pool(set, ERarity.Legendary), 0.125f);
            Cards(g, ERarity.Epic, Progression.Pool(set, ERarity.Epic), 0.875f * 0.2f);
            Cards(g, ERarity.Rare, Progression.Pool(set, ERarity.Rare), 0.875f * 0.8f);
            Cards(G(c, Loc.Get("g_per_pack", RarityName(ERarity.Uncommon), 4)), ERarity.Uncommon, Progression.Pool(set, ERarity.Uncommon), 1f, 4);
            Cards(G(c, Loc.Get("g_per_pack", RarityName(ERarity.Common), 12)), ERarity.Common, Progression.Pool(set, ERarity.Common), 1f, 12);
        }

        /// <summary>
        /// The set's chest (Packs: common with a booster, legendary with a primal pack), from Zymmer.OpenChest and the
        /// "Set N Chest Loot" list: per item, a mercenary not owned yet 5% (rare or better chests), a PvE card up to 20%,
        /// otherwise an equipment piece (common chest: common 70% / uncommon 30%; legendary: legendary). Its cards are not in
        /// the game's card count. The chest sleeves: 5% of the chests.
        /// </summary>
        private static void Chest(Contents c, CardSetTemplate set, bool legendary)
        {
            string source = "Set " + Zymmer.SetNumber(set) + " Chest Loot";
            List<InventoryItemData> equipment = Zymmer.Equipment(source);
            Group g = G(c, Loc.Get(legendary ? "g_chest_legendary" : "g_chest_common"), Loc.Get("n_wheel"));
            if (legendary)
            {
                Items(g, Loc.Get("pc_mercenaries"), Zymmer.MercenariesOf(source).ConvertAll(m => (InventoryItemData)m), 0.05f, 2);
                Cards(g, Loc.Get("pc_pve_cards"), Zymmer.Cards(source), 0.15f, 2, false);
                Items(g, RarityName(ERarity.Legendary) + " · " + Loc.Get("pc_equipment"), equipment.FindAll(i => Zymmer.RarityOf(i) == ERarity.Legendary), 0.80f, 2);
            }
            else
            {
                Cards(g, Loc.Get("pc_pve_cards"), Zymmer.Cards(source), 0.20f, 1, false);
                Items(g, RarityName(ERarity.Common) + " · " + Loc.Get("pc_equipment"), equipment.FindAll(i => Zymmer.RarityOf(i) == ERarity.Common), 0.80f * 0.7f);
                Items(g, RarityName(ERarity.Uncommon) + " · " + Loc.Get("pc_equipment"), equipment.FindAll(i => Zymmer.RarityOf(i) == ERarity.Uncommon), 0.80f * 0.3f);
            }
            List<InventoryItemData> sleeves = Sleeves.Matching("^Found in Chests for " + Regex.Escape(set.m_Name.Trim()) + @"\.$").ConvertAll(s => (InventoryItemData)s);
            if (sleeves.Count > 0) Items(g, Loc.Get("pc_sleeves") + " · " + Loc.Get("n_extra"), sleeves, Packs.ChestSleeveChance);
        }

        /// <summary>A Kismet's Reserve pack's theme: everything, once.</summary>
        private static void Bundle(Group g, List<Loot.Drop> drops)
        {
            List<CardTemplate> cards = new List<CardTemplate>();
            List<InventoryItemData> items = new List<InventoryItemData>();
            foreach (Loot.Drop d in drops)
            {
                if (d.Card != null) { cards.Add(d.Card); continue; }
                InventoryItemData i = Singleton<TemplateManager>.Instance.GetInventoryItem(d.Item);
                if (i != null) items.Add(i);
            }
            if (cards.Count > 0) g.Pools.Add(new Pool { Title = Loc.Get("pc_cards_title"), Cards = Sorted(cards), Once = true, Counted = true });
            if (items.Count > 0) g.Pools.Add(new Pool { Title = Loc.Get("pc_items_title"), Items = SortedItems(items), Once = true });
        }

        private static string RarityName(ERarity r)
        {
            switch (r)
            {
                case ERarity.Legendary: return Loc.Get("pc_legendary");
                case ERarity.Epic: return Loc.Get("pc_epic");
                case ERarity.Rare: return Loc.Get("pc_rare");
                case ERarity.Uncommon: return Loc.Get("pc_uncommon");
                default: return Loc.Get("pc_commonr");
            }
        }

        private static List<CardTemplate> Sorted(List<CardTemplate> pool)
        {
            List<CardTemplate> sorted = new List<CardTemplate>(pool.FindAll(t => t != null));
            sorted.Sort((a, b) => string.Compare(a.m_Name, b.m_Name, StringComparison.OrdinalIgnoreCase));
            return sorted;
        }

        /// <summary>Mercenaries first, then by rarity (the rarest first), then by name.</summary>
        private static List<InventoryItemData> SortedItems(List<InventoryItemData> items)
        {
            List<InventoryItemData> l = items.FindAll(i => i != null);
            l.Sort((a, b) =>
            {
                int ka = a is InventoryMercenaryData ? 0 : 1, kb = b is InventoryMercenaryData ? 0 : 1;
                if (ka != kb) return ka.CompareTo(kb);
                InventoryEquipmentData ea = a as InventoryEquipmentData, eb = b as InventoryEquipmentData;
                int ra = ea != null ? (int)ea.m_Rarity : 0, rb = eb != null ? (int)eb.m_Rarity : 0;
                if (ra != rb) return rb.CompareTo(ra);
                return string.Compare(a.m_Name, b.m_Name, StringComparison.OrdinalIgnoreCase);
            });
            return l;
        }

        // ------------------------------------------------------------------
        // Screen areas
        // ------------------------------------------------------------------

        /// <summary>An NGUI widget's area on the screen (GUI coordinates), from its own corners (also while hidden).</summary>
        private static Rect WidgetRect(UIWidget w)
        {
            Camera cam = NGUITools.FindCameraForLayer(w.gameObject.layer);
            Vector3[] c = w.worldCorners;
            if (cam == null || c == null || c.Length < 3) return new Rect();
            Vector3 a = cam.WorldToScreenPoint(c[0]), b = cam.WorldToScreenPoint(c[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Screen.height - Mathf.Max(a.y, b.y), Mathf.Max(a.x, b.x), Screen.height - Mathf.Min(a.y, b.y));
        }
    }
}
