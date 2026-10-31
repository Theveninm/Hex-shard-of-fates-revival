using System;
using System.Collections.Generic;
using Game.Shared;
using Game.Shared.Mechanics;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// "Possible drops" of a store product (Zymmer 2026-10-31: "a list of all stuff that could drop when buying a pack
    /// from the store"), as the mod opens it (Progression.OpenPack, Packs, SpecialPacks, Zymmer). Not in the original game.
    /// - A bar on the item panel, for every product the window can describe (the game's card count only exists for the
    ///   booster packs): the cards owned of the product's pools and every item owned (cards, equipment, mercenaries),
    ///   with a "Possible drops" button. Under the game's card count when it has one, in its place otherwise.
    /// - The window: how many cards of each rarity, then every card of each pool with the copies owned, then the other
    ///   items (equipment, mercenaries) with their pictures. The odds shown are the mod's own (PLACEHOLDER where they are).
    /// </summary>
    public class PackContents : MonoBehaviour
    {
        /// <summary>What a product can give: cards by section, then other items by section.</summary>
        private class Contents
        {
            public string Summary;
            public readonly List<KeyValuePair<string, List<CardTemplate>>> Cards = new List<KeyValuePair<string, List<CardTemplate>>>();
            public readonly List<KeyValuePair<string, List<InventoryItemData>>> Items = new List<KeyValuePair<string, List<InventoryItemData>>>();
            public int CardsOwned, CardsTotal, ItemsOwned, ItemsTotal;
        }

        private class Row
        {
            public string Text, Detail;
            public bool Header;
            public List<InventoryItemData> Tiles;
            public float Height, Y;
        }

        private InventoryItemData m_Item;
        private Contents m_Contents;
        private List<Row> m_Rows;
        private float m_RowsWidth, m_RowsHeight;
        private Vector2 m_Scroll;
        private Rect m_BarRect, m_LastFrame;
        private readonly Controls.UiBlocker m_Block = new Controls.UiBlocker();
        private UIPremiumStoreViewModel m_Store;
        private float m_NextFind;
        private InventoryItemData m_Checked;
        private Contents m_CheckedContents;
        private bool m_Open;

        private static PackContents s_Instance;
        private void Awake() { s_Instance = this; }

        /// <summary>Dev "packlist": opens the window for the store's selected item.</summary>
        public static string DevOpen()
        {
            InventoryItemData item = s_Instance != null && s_Instance.m_Store != null ? s_Instance.Selected() : null;
            Contents c = item != null ? Describe(item) : null;
            if (c == null) return "no described item selected";
            s_Instance.Show(item, c);
            return item.m_Name + ": cards " + c.CardsOwned + "/" + c.CardsTotal + ", all " + (c.CardsOwned + c.ItemsOwned) + "/" + (c.CardsTotal + c.ItemsTotal);
        }

        private void Update()
        {
            if (m_Store == null && Time.realtimeSinceStartup >= m_NextFind)
            {
                m_NextFind = Time.realtimeSinceStartup + 1f;
                m_Store = FindObjectOfType<UIPremiumStoreViewModel>();
            }
            if (m_Store == null || !m_Store.isActiveAndEnabled) { m_Open = false; m_BarRect = new Rect(); m_Checked = null; m_Block.Release(); return; }
            m_Block.Update(m_Open, m_BarRect);
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
            if (m_Open) { DrawWindow(S); return; }
            InventoryItemData item = Selected();
            if (item != m_Checked) { m_Checked = item; m_CheckedContents = item != null ? Describe(item) : null; }
            if (m_CheckedContents == null) { m_BarRect = new Rect(); return; }
            DrawBar(S, item, m_CheckedContents);
        }

        // ------------------------------------------------------------------
        // The bar on the item panel
        // ------------------------------------------------------------------

        private void DrawBar(float S, InventoryItemData item, Contents c)
        {
            Transform frame = m_Store.transform.Find("Panel_R/Panel_Overlay/Frame_Details");
            bool gameBar = frame != null && frame.gameObject.activeInHierarchy;
            if (gameBar) m_LastFrame = ScreenRect(frame);
            Rect f = m_LastFrame;
            if (f.width <= 0)
            {
                // Never seen yet: where the game puts it (the top middle of the item panel).
                Transform panel = m_Store.transform.Find("Panel_R");
                Rect p = panel != null ? ScreenRect(panel) : new Rect(Screen.width * 0.5f, Screen.height * 0.12f, Screen.width * 0.48f, Screen.height * 0.7f);
                f = new Rect(p.center.x - 196 * S, p.y + 30 * S, 392 * S, 38 * S);
            }
            float h = 34 * S, w = 660 * S;
            // Under the game's card count when it shows one, in its place otherwise.
            Rect bar = new Rect(f.center.x - w / 2f, gameBar ? f.yMax + 6 * S : f.y, w, h);
            m_BarRect = bar;
            if (!GameSkin.Sprite(bar, "A_Shared_Atlas", "Dropdown_Panel")) GUI.DrawTexture(bar, GameSkin.Solid(new Color(0.05f, 0.06f, 0.09f, 0.9f)));
            string text = Loc.Get("pc_bar", Pct(c.CardsOwned, c.CardsTotal), Pct(c.CardsOwned + c.ItemsOwned, c.CardsTotal + c.ItemsTotal));
            Rect button = new Rect(bar.xMax - 170 * S, bar.y + 2 * S, 166 * S, h - 4 * S);
            GameSkin.Label(new Rect(bar.x + 12 * S, bar.y, button.x - bar.x - 16 * S, h), text, GameSkin.Style("bold", 15, GameSkin.Gold, TextAnchor.MiddleCenter));
            if (GameSkin.GoldButton(button, Loc.Get("pc_button"), true, 14)) Show(item, c);
        }

        private static string Pct(int owned, int total)
        {
            return total <= 0 ? "0/0" : owned + "/" + total + " (" + Mathf.RoundToInt(100f * owned / total) + "%)";
        }

        // ------------------------------------------------------------------
        // The window
        // ------------------------------------------------------------------

        private void Show(InventoryItemData item, Contents c)
        {
            m_Item = item;
            m_Contents = c;
            m_Rows = null;
            m_Scroll = Vector2.zero;
            m_Open = true;
        }

        private void DrawWindow(float S)
        {
            Rect win = Controls.Window(1000, 840, Loc.Get("pc_title", m_Item.m_Name));
            float pad = 34 * S;
            string summary = m_Contents.Summary + "\n" + Loc.Get("pc_bar", Pct(m_Contents.CardsOwned, m_Contents.CardsTotal),
                Pct(m_Contents.CardsOwned + m_Contents.ItemsOwned, m_Contents.CardsTotal + m_Contents.ItemsTotal));
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
            GUIStyle head = GameSkin.Style("title", 18, GameSkin.Gold);
            GUIStyle name = GameSkin.Style("body", 16, GameSkin.Text);
            GUIStyle det = GameSkin.Style("body", 15, GameSkin.Muted, TextAnchor.MiddleRight);
            GUIStyle tileName = GameSkin.Style("body", 13, GameSkin.Text, TextAnchor.UpperCenter, true);
            GUIStyle tileOwned = GameSkin.Style("body", 12, GameSkin.Muted, TextAnchor.UpperCenter);
            foreach (Row r in m_Rows)
            {
                // Only the rows in sight are drawn (lists of hundreds of cards).
                if (r.Y + r.Height < m_Scroll.y || r.Y > m_Scroll.y + inner.height) continue;
                Rect rr = new Rect(0, r.Y, width, r.Height);
                if (r.Header) GameSkin.Label(rr, r.Text, head);
                else if (r.Tiles != null) DrawTiles(rr, r.Tiles, S, tileName, tileOwned);
                else
                {
                    GUI.Label(new Rect(rr.x + 16 * S, rr.y, rr.width - 200 * S, rr.height), r.Text, name);
                    GUI.Label(new Rect(rr.xMax - 190 * S, rr.y, 190 * S, rr.height), r.Detail, det);
                }
            }
            GUI.EndScrollView();
            if (GameSkin.RedButton(new Rect(win.center.x - 90 * S, win.yMax - 70 * S, 180 * S, 52 * S), Loc.Get("close"))) { m_Open = false; m_Rows = null; }
        }

        private const float TileW = 150f, TileH = 196f;

        private void BuildRows(float width, float S)
        {
            m_RowsWidth = width;
            m_Rows = new List<Row>();
            float headH = 34 * S, rowH = 30 * S, gap = 4 * S;
            foreach (KeyValuePair<string, List<CardTemplate>> sec in m_Contents.Cards)
            {
                m_Rows.Add(new Row { Header = true, Text = sec.Key + "  (" + sec.Value.Count + ")", Height = headH });
                foreach (CardTemplate c in sec.Value)
                    m_Rows.Add(new Row { Text = c.m_Name, Detail = Loc.Get("pc_owned", Collection.Count(c.m_Id)), Height = rowH });
            }
            int perRow = Mathf.Max(1, (int)(width / ((TileW + 10) * S)));
            foreach (KeyValuePair<string, List<InventoryItemData>> sec in m_Contents.Items)
            {
                m_Rows.Add(new Row { Header = true, Text = sec.Key + "  (" + sec.Value.Count + ")", Height = headH });
                for (int i = 0; i < sec.Value.Count; i += perRow)
                    m_Rows.Add(new Row { Tiles = sec.Value.GetRange(i, Math.Min(perRow, sec.Value.Count - i)), Height = TileH * S });
            }
            float y = 0;
            foreach (Row r in m_Rows) { r.Y = y; y += r.Height + gap; }
            m_RowsHeight = y;
        }

        private static void DrawTiles(Rect row, List<InventoryItemData> items, float S, GUIStyle nameStyle, GUIStyle ownedStyle)
        {
            float w = TileW * S, step = (TileW + 10) * S, pic = 110 * S;
            for (int i = 0; i < items.Count; i++)
            {
                InventoryItemData it = items[i];
                Rect tile = new Rect(row.x + i * step, row.y, w, row.height);
                GameSkin.Sprite(tile, "A_Frames", "Frame_Inner");
                Rect pr = new Rect(tile.center.x - pic / 2f, tile.y + 8 * S, pic, pic);
                Texture2D t = Icon(it);
                bool owned = Inventory.Count(it.m_Id) > 0;
                if (t != null)
                {
                    Color old = GUI.color;
                    if (!owned) GUI.color = new Color(1f, 1f, 1f, 0.45f);   // greyed out as the collection shows what is missing
                    GUI.DrawTexture(pr, t, ScaleMode.ScaleToFit);
                    GUI.color = old;
                }
                else GUI.DrawTexture(pr, GameSkin.Solid(new Color(0.1f, 0.1f, 0.13f, 1f)));
                string kind = it is InventoryMercenaryData ? Loc.Get("pc_mercenary") : it is InventoryEquipmentData ? RarityName(((InventoryEquipmentData)it).m_Rarity) : "";
                GUI.Label(new Rect(tile.x + 4 * S, pr.yMax + 4 * S, w - 8 * S, 40 * S), it.m_Name.Trim(), nameStyle);
                GUI.Label(new Rect(tile.x + 4 * S, tile.yMax - 22 * S, w - 8 * S, 20 * S),
                    (kind.Length > 0 ? kind + " · " : "") + Loc.Get("pc_owned", Inventory.Count(it.m_Id)), ownedStyle);
            }
        }

        private static readonly Dictionary<ResourceId, Texture2D> s_Icons = new Dictionary<ResourceId, Texture2D>();

        /// <summary>An item's picture from the game's icon bundle (icons.ab: the item's texture, or its "_Thumb"); null if none.</summary>
        private static Texture2D Icon(InventoryItemData item)
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

        // ------------------------------------------------------------------
        // What a product can give
        // ------------------------------------------------------------------

        private static readonly ERarity[] Rarities = { ERarity.Legendary, ERarity.Epic, ERarity.Rare, ERarity.Uncommon, ERarity.Common };

        /// <summary>What the product can give; null if the window does not describe it.</summary>
        private static Contents Describe(InventoryItemData item)
        {
            Contents c = new Contents();
            c.Summary = Fill(item, c);
            if (c.Summary == null) return null;
            HashSet<ResourceId> cards = new HashSet<ResourceId>(), items = new HashSet<ResourceId>();
            foreach (KeyValuePair<string, List<CardTemplate>> s in c.Cards) foreach (CardTemplate t in s.Value) cards.Add(t.m_Id);
            foreach (KeyValuePair<string, List<InventoryItemData>> s in c.Items) foreach (InventoryItemData i in s.Value) items.Add(i.m_Id);
            c.CardsTotal = cards.Count;
            foreach (ResourceId id in cards) if (Collection.Count(id) > 0) c.CardsOwned++;
            c.ItemsTotal = items.Count;
            foreach (ResourceId id in items) if (Inventory.Count(id) > 0) c.ItemsOwned++;
            return c;
        }

        /// <summary>The product's pools added to c; returns its summary, null if not described.</summary>
        private static string Fill(InventoryItemData item, Contents c)
        {
            if (Zymmer.IsKismetPack(item))
            {
                List<Loot.Drop> bundle = Zymmer.KismetBundle(item.m_Name);
                if (bundle.Count == 0) return null;
                AddDrops(c, Loc.Get("pc_items"), bundle);
                return Loc.Get("pc_kismet");
            }
            string kind = SpecialPacks.KindOf(item);
            if (kind == "randomkismet")
            {
                foreach (InventoryItemData k in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                    if (Zymmer.IsKismetPack(k) && Zymmer.KismetBundle(k.m_Name).Count > 0) AddDrops(c, k.m_Name.Trim(), Zymmer.KismetBundle(k.m_Name));
                return c.Cards.Count + c.Items.Count > 0 ? Loc.Get("pc_randomkismet") : null;
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

        private static void AddCards(Contents c, ERarity rarity, List<CardTemplate> pool)
        {
            if (pool.Count > 0) c.Cards.Add(new KeyValuePair<string, List<CardTemplate>>(RarityName(rarity), Sorted(pool)));
        }

        /// <summary>The set's chest (Packs: one per booster, legendary for a primal pack), from Zymmer's "Set N Chest Loot".</summary>
        private static void AddChest(Contents c, CardSetTemplate set)
        {
            string source = "Set " + Zymmer.SetNumber(set) + " Chest Loot";
            List<Loot.Drop> drops = new List<Loot.Drop>();
            foreach (CardTemplate t in Zymmer.Cards(source)) drops.Add(new Loot.Drop { Card = t });
            foreach (InventoryItemData i in Zymmer.Equipment(source)) drops.Add(new Loot.Drop { Item = i.m_Id });
            foreach (InventoryMercenaryData m in Zymmer.MercenariesOf(source)) drops.Add(new Loot.Drop { Item = m.m_Id });
            if (drops.Count > 0) AddDrops(c, Loc.Get("pc_chest"), drops);
        }

        /// <summary>Drops of one source: its cards with the cards, its other items after every card section.</summary>
        private static void AddDrops(Contents c, string title, List<Loot.Drop> drops)
        {
            List<CardTemplate> cards = new List<CardTemplate>();
            List<InventoryItemData> items = new List<InventoryItemData>();
            foreach (Loot.Drop d in drops)
            {
                if (d.Card != null) { cards.Add(d.Card); continue; }
                InventoryItemData i = Singleton<TemplateManager>.Instance.GetInventoryItem(d.Item);
                if (i != null) items.Add(i);
            }
            if (cards.Count > 0) c.Cards.Add(new KeyValuePair<string, List<CardTemplate>>(title + " · " + Loc.Get("pc_cards"), Sorted(cards)));
            if (items.Count > 0)
            {
                // Mercenaries first, then equipment by rarity (the rarest first), then by name.
                items.Sort((a, b) =>
                {
                    int ka = a is InventoryMercenaryData ? 0 : 1, kb = b is InventoryMercenaryData ? 0 : 1;
                    if (ka != kb) return ka.CompareTo(kb);
                    InventoryEquipmentData ea = a as InventoryEquipmentData, eb = b as InventoryEquipmentData;
                    int ra = ea != null ? (int)ea.m_Rarity : 0, rb = eb != null ? (int)eb.m_Rarity : 0;
                    if (ra != rb) return rb.CompareTo(ra);
                    return string.Compare(a.m_Name, b.m_Name, StringComparison.OrdinalIgnoreCase);
                });
                c.Items.Add(new KeyValuePair<string, List<InventoryItemData>>(title + " · " + Loc.Get("pc_other_items"), items));
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
    }
}
