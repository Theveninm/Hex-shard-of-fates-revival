using System;
using System.Collections.Generic;
using Game.Shared;
using Game.Shared.Mechanics;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// What a store product can give (Zymmer 2026-10-31: "a list of all stuff that could drop when buying a pack from
    /// the store"), as the mod opens it (Progression.OpenPack, Packs, SpecialPacks, PrizePacks, Zymmer). Not in the
    /// original game.
    /// - A bar on the item panel of every product described here: "Cards" counts the cards the game's own card count
    ///   counts (the set's commons, uncommons, rares and legendaries, the product's card pools for the others), "All
    ///   items" everything the product can give (also the epics, the chest's cards, equipment, mercenaries, sleeves).
    ///   It sits under the game's card count, or in its place for products without one, and is hidden while one of the
    ///   game's windows is open.
    /// - "Possible drops": a list window, the cards of each pool then the other items, with the copies owned.
    /// - "Details" (products without the game's own Details window): the same as miniatures. In the game's Details
    ///   window (boosters), the other items' miniatures are added under the cards, in its scroll area.
    /// The odds shown are the mod's own (PLACEHOLDER where they are).
    /// </summary>
    public class PackContents : MonoBehaviour
    {
        private class CardSection
        {
            public string Title;
            public List<CardTemplate> Cards;
            public bool Counted;   // in "Cards" (as the game's count): false for the epics and the chest's cards
        }

        /// <summary>What a product can give: cards by section, then other items by section.</summary>
        private class Contents
        {
            public string Summary;
            public readonly List<CardSection> Cards = new List<CardSection>();
            public readonly List<KeyValuePair<string, List<InventoryItemData>>> Items = new List<KeyValuePair<string, List<InventoryItemData>>>();
            public int CardsOwned, CardsTotal, AllOwned, AllTotal;
            public bool HasItems { get { return Items.Count > 0; } }
        }

        private class Row
        {
            public string Text, Detail;
            public bool Header;
            public List<CardTemplate> CardTiles;
            public List<InventoryItemData> ItemTiles;
            public float Height, Y;
        }

        private enum View { None, List, Tiles }

        private InventoryItemData m_Item;
        private Contents m_Contents;
        private View m_View;
        private List<Row> m_Rows;
        private float m_RowsWidth, m_RowsHeight;
        private Vector2 m_Scroll;
        private Rect m_BarRect, m_LastFrame;
        private readonly Controls.UiBlocker m_Block = new Controls.UiBlocker();
        private UIPremiumStoreViewModel m_Store;
        private float m_NextFind;
        private InventoryItemData m_Checked;
        private Contents m_CheckedContents;

        private static PackContents s_Instance;
        private void Awake() { s_Instance = this; }

        /// <summary>Dev "packlist [scroll] [tiles]": opens the window for the store's selected item.</summary>
        public static string DevOpen(float scroll, bool tiles)
        {
            InventoryItemData item = s_Instance != null && s_Instance.m_Store != null ? s_Instance.Selected() : null;
            Contents c = item != null ? Describe(item) : null;
            if (c == null) return "no described item selected";
            s_Instance.Show(item, c, tiles ? View.Tiles : View.List);
            s_Instance.m_Scroll.y = scroll;   // dev: "packlist 99999" = the end of the list
            return item.m_Name.Trim() + ": cards " + c.CardsOwned + "/" + c.CardsTotal + ", all " + c.AllOwned + "/" + c.AllTotal;
        }

        /// <summary>Dev "detailsitems": in the game's Details window, shows the other items (as its button does).</summary>
        public static string DevGameItems()
        {
            Transform d = s_Instance != null ? s_Instance.GameDetails() : null;
            if (d == null) return "no Details window";
            s_Instance.m_GameItems = true;
            s_Instance.m_GameScroll = Vector2.zero;
            SetCardsVisible(d, false);
            s_Instance.m_GameDetails = d;
            return "items shown";
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
                // The store closed with the Details window on the items: its cards shown again for the next time.
                if (m_GameItems) SetCardsVisible(m_GameDetails, true);
                m_GameItems = false; m_GameButton = new Rect(); m_GameArea = new Rect();
                m_View = View.None; m_BarRect = new Rect(); m_Checked = null; m_Block.Release(); return;
            }
            InventoryItemData item = Selected();
            if (item != m_Checked) { m_Checked = item; m_CheckedContents = item != null ? Describe(item) : null; }
            UpdateGameDetails();
            m_Block.Update(m_View != View.None, m_BarRect, m_GameButton, m_GameArea);
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
            if (m_View != View.None) { DrawWindow(S); return; }
            DrawGameDetailsItems(S);
            if (m_CheckedContents == null || GamePopupOpen()) { m_BarRect = new Rect(); return; }
            DrawBar(S, m_Checked, m_CheckedContents);
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

        // ------------------------------------------------------------------
        // The bar on the item panel
        // ------------------------------------------------------------------

        private Transform DetailsFrame() { return m_Store.transform.Find("Panel_R/Panel_Overlay/Frame_Details"); }

        private void DrawBar(float S, InventoryItemData item, Contents c)
        {
            Transform frame = DetailsFrame();
            bool gameBar = frame != null && frame.gameObject.activeInHierarchy;
            // The game's card count frame, shown or hidden: its widget keeps its place on the screen.
            UIWidget fw = frame != null ? frame.GetComponent<UIWidget>() : null;
            Rect f = fw != null ? WidgetRect(fw) : new Rect();
            if (f.width > 0) m_LastFrame = f; else f = m_LastFrame;
            if (f.width <= 0) f = new Rect(Screen.width * 0.625f, Screen.height * 0.15f, 392 * S, 34 * S);
            float h = 34 * S, bw = 166 * S, w = gameBar ? 660 * S : 760 * S;
            // Under the game's card count when it shows one, in its place otherwise.
            Rect bar = new Rect(f.center.x - w / 2f, gameBar ? f.yMax + 6 * S : f.y, w, h);
            m_BarRect = bar;
            if (!GameSkin.Sprite(bar, "A_Shared_Atlas", "Dropdown_Panel")) GUI.DrawTexture(bar, GameSkin.Solid(new Color(0.05f, 0.06f, 0.09f, 0.9f)));
            Rect drops = new Rect(bar.xMax - bw - 4 * S, bar.y + 2 * S, bw, h - 4 * S);
            // A Details window of the mod's own for the products the game gives none.
            Rect details = gameBar ? new Rect() : new Rect(drops.x - bw - 4 * S, drops.y, bw, drops.height);
            float textEnd = gameBar ? drops.x : details.x;
            GameSkin.Label(new Rect(bar.x + 12 * S, bar.y, textEnd - bar.x - 16 * S, h), Stats(c), GameSkin.Style("bold", 15, GameSkin.Gold, TextAnchor.MiddleCenter));
            if (!gameBar && GameSkin.GoldButton(details, Loc.Get("pc_details"), true, 14)) Show(item, c, View.Tiles);
            if (GameSkin.GoldButton(drops, Loc.Get("pc_button"), true, 14)) Show(item, c, View.List);
        }

        private static string Stats(Contents c)
        {
            return Loc.Get("pc_bar", Pct(c.CardsOwned, c.CardsTotal), Pct(c.AllOwned, c.AllTotal));
        }

        private static string Pct(int owned, int total)
        {
            return total <= 0 ? "0/0" : owned + "/" + total + " (" + Mathf.FloorToInt(100f * owned / total) + "%)";
        }

        // ------------------------------------------------------------------
        // The mod's windows: the list (Possible drops) and the miniatures (Details)
        // ------------------------------------------------------------------

        private void Show(InventoryItemData item, Contents c, View view)
        {
            m_Item = item;
            m_Contents = c;
            m_View = view;
            m_Rows = null;
            m_Scroll = Vector2.zero;
        }

        private void DrawWindow(float S)
        {
            string title = Loc.Get(m_View == View.Tiles ? "pc_details_title" : "pc_title", m_Item.m_Name.Trim());
            Rect win = Controls.Window(1060, 860, title);
            float pad = 34 * S;
            string summary = m_Contents.Summary + "\n" + Stats(m_Contents);
            GUIStyle sum = GameSkin.Style("body", 16, GameSkin.Text, TextAnchor.UpperLeft, true);
            float sh = sum.CalcHeight(new GUIContent(summary), win.width - 2 * pad);
            GUI.Label(new Rect(win.x + pad, win.y + 54 * S, win.width - 2 * pad, sh), summary, sum);
            float top = win.y + 64 * S + sh;
            Rect area = new Rect(win.x + pad, top, win.width - 2 * pad, win.yMax - 84 * S - top);
            Controls.Frame(area, null);
            Rect inner = new Rect(area.x + 8 * S, area.y + 8 * S, area.width - 16 * S, area.height - 16 * S);
            float width = inner.width - 24 * S;
            if (m_Rows == null || Mathf.Abs(m_RowsWidth - width) > 1f) BuildRows(width, S);

            Rect view = new Rect(0, 0, inner.width - 20 * S, m_RowsHeight);
            m_Scroll = GUI.BeginScrollView(inner, m_Scroll, view, false, false, GUIStyle.none, view.height > inner.height ? GameSkin.ScrollBar : GUIStyle.none);
            // What is in sight, in the scroll area's coordinates: the pictures drawn with the game's material are cut to it.
            Rect visible = new Rect(m_Scroll.x, m_Scroll.y, inner.width, inner.height);
            GUIStyle head = GameSkin.Style("title", 18, GameSkin.Gold);
            GUIStyle name = GameSkin.Style("body", 16, GameSkin.Text);
            GUIStyle det = GameSkin.Style("body", 15, GameSkin.Muted, TextAnchor.MiddleRight);
            foreach (Row r in m_Rows)
            {
                // Only the rows in sight are drawn (lists of hundreds of cards).
                if (r.Y + r.Height < visible.y || r.Y > visible.yMax) continue;
                Rect rr = new Rect(0, r.Y, width, r.Height);
                if (r.Header) GameSkin.Label(rr, r.Text, head);
                else if (r.CardTiles != null) DrawCardTiles(rr, r.CardTiles, S);
                else if (r.ItemTiles != null) DrawItemTiles(rr.x, rr.y, r.ItemTiles, TileW * S, TileH * S, (TileW + TileGap) * S, visible, S);
                else
                {
                    GUI.Label(new Rect(rr.x + 16 * S, rr.y, rr.width - 220 * S, rr.height), r.Text, name);
                    GUI.Label(new Rect(rr.xMax - 210 * S, rr.y, 210 * S, rr.height), r.Detail, det);
                }
            }
            GUI.EndScrollView();
            if (GameSkin.RedButton(new Rect(win.center.x - 90 * S, win.yMax - 70 * S, 180 * S, 52 * S), Loc.Get("close"))) { m_View = View.None; m_Rows = null; }
        }

        private const float TileW = 136f, TileH = 196f, TileGap = 10f;

        private void BuildRows(float width, float S)
        {
            m_RowsWidth = width;
            m_Rows = new List<Row>();
            float headH = 34 * S, rowH = 30 * S, gap = 4 * S;
            int perRow = Mathf.Max(1, (int)(width / ((TileW + TileGap) * S)));
            foreach (CardSection sec in m_Contents.Cards)
            {
                m_Rows.Add(new Row { Header = true, Text = sec.Title + "  (" + sec.Cards.Count + ")", Height = headH });
                if (m_View == View.Tiles)
                    for (int i = 0; i < sec.Cards.Count; i += perRow)
                        m_Rows.Add(new Row { CardTiles = sec.Cards.GetRange(i, Math.Min(perRow, sec.Cards.Count - i)), Height = TileH * S });
                else
                    foreach (CardTemplate c in sec.Cards)
                        m_Rows.Add(new Row { Text = c.m_Name, Detail = Loc.Get("pc_owned", Collection.Count(c.m_Id)), Height = rowH });
            }
            foreach (KeyValuePair<string, List<InventoryItemData>> sec in m_Contents.Items)
            {
                m_Rows.Add(new Row { Header = true, Text = sec.Key + "  (" + sec.Value.Count + ")", Height = headH });
                if (m_View == View.Tiles)
                    for (int i = 0; i < sec.Value.Count; i += perRow)
                        m_Rows.Add(new Row { ItemTiles = sec.Value.GetRange(i, Math.Min(perRow, sec.Value.Count - i)), Height = TileH * S });
                else
                    foreach (InventoryItemData it in sec.Value)
                        m_Rows.Add(new Row { Text = it.m_Name.Trim() + "  ·  " + Kind(it), Detail = Loc.Get("pc_owned", Inventory.Count(it.m_Id)), Height = rowH });
            }
            float y = 0;
            foreach (Row r in m_Rows) { r.Y = y; y += r.Height + gap; }
            m_RowsHeight = y;
        }

        private static void DrawCardTiles(Rect row, List<CardTemplate> cards, float S)
        {
            float w = TileW * S, step = (TileW + TileGap) * S;
            GUIStyle owned = GameSkin.Style("body", 12, GameSkin.Muted, TextAnchor.MiddleCenter);
            for (int i = 0; i < cards.Count; i++)
            {
                Rect tile = new Rect(row.x + i * step, row.y, w, row.height - 20 * S);
                CardArt.Tile(tile, cards[i]);
                int n = Collection.Count(cards[i].m_Id);
                GUI.Label(new Rect(tile.x, tile.yMax, w, 20 * S), Loc.Get("pc_owned", n), n > 0 ? GameSkin.Style("bold", 12, GameSkin.Gold, TextAnchor.MiddleCenter) : owned);
            }
        }

        /// <summary>A row of item miniatures from (x, y); clip: the visible area in the same coordinates (the pictures are cut to it).</summary>
        private static void DrawItemTiles(float x, float y, List<InventoryItemData> items, float w, float h, float step, Rect clip, float S)
        {
            float pic = Mathf.Min(w - 16 * S, h * 0.6f);
            GUIStyle nameStyle = GameSkin.Style("body", 13, GameSkin.Text, TextAnchor.UpperCenter, true);
            GUIStyle ownedStyle = GameSkin.Style("body", 12, GameSkin.Muted, TextAnchor.UpperCenter);
            GUIStyle ownedGold = GameSkin.Style("bold", 12, GameSkin.Gold, TextAnchor.UpperCenter);
            for (int i = 0; i < items.Count; i++)
            {
                InventoryItemData it = items[i];
                Rect tile = new Rect(x + i * step, y, w, h);
                GameSkin.Sprite(tile, "A_Frames", "Frame_Inner");
                Rect pr = new Rect(tile.center.x - pic / 2f, tile.y + 8 * S, pic, pic);
                Texture2D t = Icon(it);
                if (t != null) DrawIcon(pr, clip, t, it);
                else GUI.DrawTexture(pr, GameSkin.Solid(new Color(0.1f, 0.1f, 0.13f, 1f)));
                int n = Inventory.Count(it.m_Id);
                GUI.Label(new Rect(tile.x + 4 * S, pr.yMax + 4 * S, w - 8 * S, 40 * S), it.m_Name.Trim(), nameStyle);
                GUI.Label(new Rect(tile.x + 4 * S, tile.yMax - 22 * S, w - 8 * S, 20 * S), Kind(it) + " · " + Loc.Get("pc_owned", n), n > 0 ? ownedGold : ownedStyle);
            }
        }

        private static string Kind(InventoryItemData it)
        {
            if (it is InventoryMercenaryData) return Loc.Get("pc_mercenary");
            if (it is InventoryEquipmentData) return RarityName(((InventoryEquipmentData)it).m_Rarity);
            if (it is InventoryDeckSleeve) return Loc.Get("pc_sleeve");
            if (it is InventoryCardPack) return Loc.Get("pc_pack");
            return "";
        }

        // ------------------------------------------------------------------
        // The game's Details window: the other items, in place of the cards
        // ------------------------------------------------------------------

        // The window's card grid is paginated (NguiPaginatedItemsSourceBinding: a fixed set of slots bound to other
        // cards as its scroll bar moves), so nothing can be added under its last card: a button next to Close shows
        // the product's other items in the grid's frame instead, and back.
        private bool m_GameItems;
        private Transform m_GameDetails;   // the window whose cards were hidden (shown again when it closes)
        private Vector2 m_GameScroll;
        private Rect m_GameButton, m_GameArea;

        private Transform GameDetails()
        {
            Transform d = m_Store != null ? m_Store.transform.Find("Panel_Popup_Details") : null;
            return d != null && Shown(d) ? d.Find("Frame_Details") : null;
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

        private void UpdateGameDetails()
        {
            Transform d = GameDetails();
            if (d == null || m_CheckedContents == null || !m_CheckedContents.HasItems)
            {
                if (m_GameItems) SetCardsVisible(m_GameDetails, true);
                m_GameItems = false; m_GameButton = new Rect(); m_GameArea = new Rect();
            }
        }

        /// <summary>The card grid's panel hidden while the items are shown (its cards would show through).</summary>
        private static void SetCardsVisible(Transform details, bool visible)
        {
            Transform panel = details != null ? details.Find("Frame_Scroll/Panel_Cards") : null;
            UIPanel p = panel != null ? panel.GetComponent<UIPanel>() : null;
            if (p != null) p.alpha = visible ? 1f : 0f;
            Transform bar = details != null ? details.Find("Frame_Scroll/ScrollBar_New") : null;
            if (bar != null) bar.gameObject.SetActive(visible);
        }

        private void DrawGameDetailsItems(float S)
        {
            Transform d = GameDetails();
            if (d == null || m_CheckedContents == null || !m_CheckedContents.HasItems) return;
            Transform close = d.Find("Button_Close"), frame = d.Find("Frame_Scroll");
            UIWidget cw = close != null ? close.GetComponent<UIWidget>() : null, fw = frame != null ? frame.GetComponent<UIWidget>() : null;
            if (cw == null || fw == null) return;
            Rect cr = WidgetRect(cw);
            int items = 0;
            foreach (KeyValuePair<string, List<InventoryItemData>> sec in m_CheckedContents.Items) items += sec.Value.Count;
            // Left of Close, as wide as the window's other button.
            m_GameButton = new Rect(cr.x - 300 * S, cr.y, 290 * S, cr.height);
            string label = m_GameItems ? Loc.Get("pc_back_cards") : Loc.Get("pc_show_items", items);
            if (GameSkin.GoldButton(m_GameButton, label, true, 17))
            {
                m_GameItems = !m_GameItems;
                m_GameScroll = Vector2.zero;
                SetCardsVisible(d, !m_GameItems);
                m_GameDetails = d;
            }
            if (!m_GameItems) { m_GameArea = new Rect(); return; }
            Rect fr = WidgetRect(fw);
            m_GameArea = fr;
            Rect inner = new Rect(fr.x + 14 * S, fr.y + 12 * S, fr.width - 28 * S, fr.height - 24 * S);
            float width = inner.width - 24 * S, tw = TileW * S * 1.15f, th = TileH * S * 1.1f, step = tw + TileGap * S;
            int perRow = Mathf.Max(1, (int)(width / step));
            float x0 = (width - (perRow * step - TileGap * S)) / 2f;
            float headH = 40 * S, total = 0;
            foreach (KeyValuePair<string, List<InventoryItemData>> sec in m_CheckedContents.Items)
                total += headH + ((sec.Value.Count + perRow - 1) / perRow) * (th + 6 * S);
            Rect view = new Rect(0, 0, inner.width - 20 * S, total);
            m_GameScroll = GUI.BeginScrollView(inner, m_GameScroll, view, false, false, GUIStyle.none, total > inner.height ? GameSkin.ScrollBar : GUIStyle.none);
            Rect visible = new Rect(m_GameScroll.x, m_GameScroll.y, inner.width, inner.height);
            GUIStyle head = GameSkin.Style("title", 20, GameSkin.Gold, TextAnchor.MiddleCenter);
            float y = 0;
            foreach (KeyValuePair<string, List<InventoryItemData>> sec in m_CheckedContents.Items)
            {
                GameSkin.Label(new Rect(0, y, width, headH), sec.Key + "  (" + sec.Value.Count + ")", head);
                y += headH;
                for (int i = 0; i < sec.Value.Count; i += perRow)
                {
                    if (y + th >= visible.y && y <= visible.yMax)
                        DrawItemTiles(x0, y, sec.Value.GetRange(i, Math.Min(perRow, sec.Value.Count - i)), tw, th, step, visible, S);
                    y += th + 6 * S;
                }
            }
            GUI.EndScrollView();
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
        // What a product can give
        // ------------------------------------------------------------------

        private static readonly ERarity[] Rarities = { ERarity.Legendary, ERarity.Epic, ERarity.Rare, ERarity.Uncommon, ERarity.Common };

        /// <summary>What the product can give; null if not described here.</summary>
        private static Contents Describe(InventoryItemData item)
        {
            Contents c = new Contents();
            try { c.Summary = Fill(item, c); }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [PackContents] " + item.m_Name + ": " + e.Message); return null; }
            if (c.Summary == null || (c.Cards.Count == 0 && c.Items.Count == 0)) return null;
            HashSet<ResourceId> counted = new HashSet<ResourceId>(), cards = new HashSet<ResourceId>(), items = new HashSet<ResourceId>();
            foreach (CardSection s in c.Cards)
                foreach (CardTemplate t in s.Cards) { cards.Add(t.m_Id); if (s.Counted) counted.Add(t.m_Id); }
            foreach (KeyValuePair<string, List<InventoryItemData>> s in c.Items) foreach (InventoryItemData i in s.Value) items.Add(i.m_Id);
            c.CardsTotal = counted.Count;
            foreach (ResourceId id in counted) if (Collection.Count(id) > 0) c.CardsOwned++;
            c.AllTotal = cards.Count + items.Count;
            foreach (ResourceId id in cards) if (Collection.Count(id) > 0) c.AllOwned++;
            foreach (ResourceId id in items) if (Inventory.Count(id) > 0) c.AllOwned++;
            return c;
        }

        /// <summary>The product's pools added to c; returns its summary, null if not described.</summary>
        private static string Fill(InventoryItemData item, Contents c)
        {
            string name = (item.m_Name ?? "").Trim();
            if (name == "Siege Pack")
            {
                AddCards(c, Loc.Get("pc_pve_cards"), PrizePacks.PveCards(), true);
                AddCards(c, Loc.Get("pc_alt_cards"), PrizePacks.AltArtCards(), true);
                AddCards(c, Loc.Get("pc_alt_shards"), PrizePacks.AltArtShards(), true);
                AddItems(c, Loc.Get("pc_mercenaries"), PrizePacks.SiegeMercenaryItems());
                AddItems(c, Loc.Get("pc_equipment"), PrizePacks.SiegeEquipmentPool());
                AddItems(c, Loc.Get("pc_sleeves"), PrizePacks.SiegeSleeveItems());
                return Loc.Get("pc_siege");
            }
            if (name == "Cosmic Coin Pack")
            {
                AddCards(c, Loc.Get("pc_alt_cards"), PrizePacks.AltArtCards(), true);
                InventoryItemData booster = PrizePacks.LatestBoosterItem();
                if (booster != null) AddItems(c, Loc.Get("pc_packs"), new List<InventoryItemData> { booster });
                AddItems(c, Loc.Get("pc_sleeves"), PrizePacks.CosmicSleeveItems());
                return Loc.Get("pc_cosmic");
            }
            if (Zymmer.IsKismetPack(item))
            {
                List<Loot.Drop> bundle = Zymmer.KismetBundle(item.m_Name);
                if (bundle.Count == 0) return null;
                AddDrops(c, Loc.Get("pc_items"), bundle, true);
                return Loc.Get("pc_kismet");
            }
            string kind = SpecialPacks.KindOf(item);
            if (kind == "randomkismet")
            {
                foreach (InventoryItemData k in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                    if (Zymmer.IsKismetPack(k) && Zymmer.KismetBundle(k.m_Name).Count > 0) AddDrops(c, k.m_Name.Trim(), Zymmer.KismetBundle(k.m_Name), true);
                return Loc.Get("pc_randomkismet");
            }
            if (kind == "giftbox")
            {
                string holiday = SpecialPacks.GiftBoxHoliday(item);
                AddItems(c, Loc.Get("pc_sleeves"), SpecialPacks.HolidaySleeves(holiday).ConvertAll(s => (InventoryItemData)s));
                AddItems(c, Loc.Get("pc_equipment"), Sleeves.HolidayEquipmentPool(holiday));
                return Loc.Get("pc_giftbox");
            }
            InventoryCardPack pack = item as InventoryCardPack;
            if (pack == null) return null;
            CardSetTemplate set;
            Singleton<TemplateManager>.Instance.CardSets.TryGetValue(pack.m_SetId, out set);
            switch (kind)
            {
                case "fullset":
                    if (set == null) return null;
                    foreach (ERarity r in Rarities) AddCards(c, r, Progression.Pool(set, r));
                    return Loc.Get("pc_fullset");
                case "trials":
                {
                    if (set == null) return null;
                    string race = Array.Find(SpecialPacks.Races, x => item.m_Name.StartsWith(x));
                    foreach (ERarity r in new[] { ERarity.Rare, ERarity.Uncommon, ERarity.Common })
                    {
                        List<CardTemplate> pool = Progression.Pool(set, r);
                        List<CardTemplate> of = race == null ? pool : pool.FindAll(t => t.HasSubtype(race == "Shin'hare" ? "Shin" : race));
                        AddCards(c, r, of.Count > 0 ? of : pool);
                    }
                    return Loc.Get("pc_trials");
                }
                case "common":
                    AddCards(c, ERarity.Common, SpecialPacks.StandardPool(ERarity.Common));
                    return Loc.Get("pc_common");
                case "gauntlet":
                    AddCards(c, ERarity.Uncommon, SpecialPacks.StandardPool(ERarity.Uncommon));
                    AddCards(c, ERarity.Common, SpecialPacks.StandardPool(ERarity.Common));
                    return Loc.Get("pc_gauntlet");
                case "dualshard":
                    if (set == null) return null;
                    foreach (ERarity r in new[] { ERarity.Legendary, ERarity.Rare, ERarity.Uncommon, ERarity.Common }) AddCards(c, r, Progression.Pool(set, r));
                    return Loc.Get("pc_dualshard");
                case "kismetdraft":
                    foreach (ERarity r in new[] { ERarity.Legendary, ERarity.Rare, ERarity.Uncommon, ERarity.Common })
                    {
                        List<CardTemplate> all = new List<CardTemplate>();
                        foreach (CardSetTemplate s in SpecialPacks.BoosterSets()) all.AddRange(Progression.Pool(s, r));
                        AddCards(c, r, all);
                    }
                    return Loc.Get("pc_kismetdraft");
                case "gencon":
                    if (set == null) return null;
                    foreach (ERarity r in Rarities) AddCards(c, r, Progression.Pool(set, r));
                    return Loc.Get("pc_gencon");
            }
            if (set == null) return null;
            if (pack.m_CardPackType == ECardPackType.PrimalPack)
            {
                AddCards(c, ERarity.Legendary, Progression.Pool(set, ERarity.Legendary));
                AddCards(c, ERarity.Rare, Progression.Pool(set, ERarity.Rare));
                AddChest(c, set);
                return Loc.Get("pc_primal");
            }
            if (pack.m_CardPackType != ECardPackType.BoosterPack) return null;
            foreach (ERarity r in Rarities) AddCards(c, r, Progression.Pool(set, r));
            AddChest(c, set);
            return Loc.Get(Store.PrimalOf(set.m_Id).IsValid ? "pc_booster_primal" : "pc_booster");
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
            List<CardTemplate> sorted = new List<CardTemplate>(pool);
            sorted.Sort((a, b) => string.Compare(a.m_Name, b.m_Name, StringComparison.OrdinalIgnoreCase));
            return sorted;
        }

        /// <summary>A rarity's pool of a set; the epics are not in the game's card count (it counts common to legendary).</summary>
        private static void AddCards(Contents c, ERarity rarity, List<CardTemplate> pool)
        {
            AddCards(c, RarityName(rarity), pool, rarity != ERarity.Epic);
        }

        private static void AddCards(Contents c, string title, List<CardTemplate> pool, bool counted)
        {
            if (pool.Count > 0) c.Cards.Add(new CardSection { Title = title, Cards = Sorted(pool), Counted = counted });
        }

        private static void AddItems(Contents c, string title, List<InventoryItemData> items)
        {
            items = items.FindAll(i => i != null);
            if (items.Count == 0) return;
            // Mercenaries first, then by rarity (the rarest first), then by name.
            items.Sort((a, b) =>
            {
                int ka = a is InventoryMercenaryData ? 0 : 1, kb = b is InventoryMercenaryData ? 0 : 1;
                if (ka != kb) return ka.CompareTo(kb);
                InventoryEquipmentData ea = a as InventoryEquipmentData, eb = b as InventoryEquipmentData;
                int ra = ea != null ? (int)ea.m_Rarity : 0, rb = eb != null ? (int)eb.m_Rarity : 0;
                if (ra != rb) return rb.CompareTo(ra);
                return string.Compare(a.m_Name, b.m_Name, StringComparison.OrdinalIgnoreCase);
            });
            c.Items.Add(new KeyValuePair<string, List<InventoryItemData>>(title, items));
        }

        /// <summary>The set's chest (Packs: one per booster, legendary for a primal pack), from Zymmer's "Set N Chest Loot":
        /// its cards are not in the game's card count.</summary>
        private static void AddChest(Contents c, CardSetTemplate set)
        {
            string source = "Set " + Zymmer.SetNumber(set) + " Chest Loot";
            List<Loot.Drop> drops = new List<Loot.Drop>();
            foreach (CardTemplate t in Zymmer.Cards(source)) drops.Add(new Loot.Drop { Card = t });
            foreach (InventoryItemData i in Zymmer.Equipment(source)) drops.Add(new Loot.Drop { Item = i.m_Id });
            foreach (InventoryMercenaryData m in Zymmer.MercenariesOf(source)) drops.Add(new Loot.Drop { Item = m.m_Id });
            if (drops.Count > 0) AddDrops(c, Loc.Get("pc_chest"), drops, false);
        }

        /// <summary>Drops of one source: its cards with the cards, its other items after every card section.</summary>
        private static void AddDrops(Contents c, string title, List<Loot.Drop> drops, bool counted)
        {
            List<CardTemplate> cards = new List<CardTemplate>();
            List<InventoryItemData> items = new List<InventoryItemData>();
            foreach (Loot.Drop d in drops)
            {
                if (d.Card != null) { cards.Add(d.Card); continue; }
                InventoryItemData i = Singleton<TemplateManager>.Instance.GetInventoryItem(d.Item);
                if (i != null) items.Add(i);
            }
            AddCards(c, title + " · " + Loc.Get("pc_cards"), cards, counted);
            AddItems(c, title + " · " + Loc.Get("pc_other_items"), items);
        }

        // ------------------------------------------------------------------
        // Screen areas
        // ------------------------------------------------------------------

        /// <summary>An NGUI widget's area on the screen (GUI coordinates), from its own corners (also while hidden).</summary>
        private static Rect WidgetRect(UIWidget w)
        {
            return CornersRect(w.worldCorners, w.gameObject.layer);
        }

        private static Rect CornersRect(Vector3[] c, int layer)
        {
            Camera cam = NGUITools.FindCameraForLayer(layer);
            if (cam == null || c == null || c.Length < 3) return new Rect();
            Vector3 a = cam.WorldToScreenPoint(c[0]), b = cam.WorldToScreenPoint(c[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Screen.height - Mathf.Max(a.y, b.y), Mathf.Max(a.x, b.x), Screen.height - Mathf.Min(a.y, b.y));
        }
    }
}
