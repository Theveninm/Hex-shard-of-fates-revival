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
    /// from the store"): a button next to the store's card count opens a window with what the selected pack can give,
    /// as the mod opens it (Progression.OpenPack, Packs, SpecialPacks, Zymmer): how many cards of each rarity, then every
    /// card and item of each pool with the copies owned. The odds shown are the mod's own (PLACEHOLDER where they are).
    /// </summary>
    public class PackContents : MonoBehaviour
    {
        private class Row
        {
            public string Text;
            public string Detail;
            public bool Header;
        }

        private InventoryItemData m_Item;
        private string m_Summary;
        private List<Row> m_Rows;
        private Vector2 m_Scroll;
        private Rect m_ButtonRect;
        private readonly Controls.UiBlocker m_Block = new Controls.UiBlocker();
        private UIPremiumStoreViewModel m_Store;
        private float m_NextFind;
        private InventoryItemData m_Checked;
        private bool m_CheckedHas;

        private bool Open { get { return m_Rows != null; } }

        private static PackContents s_Instance;
        private void Awake() { s_Instance = this; }

        /// <summary>Dev "packlist": opens the window for the store's selected item.</summary>
        public static string DevOpen()
        {
            InventoryItemData item = s_Instance != null && s_Instance.m_Store != null ? s_Instance.Selected() : null;
            if (item == null || !Has(item)) return "no described item selected";
            s_Instance.Show(item);
            return item.m_Name + ": " + s_Instance.m_Rows.Count + " rows";
        }

        private void Update()
        {
            if (m_Store == null && Time.realtimeSinceStartup >= m_NextFind)
            {
                m_NextFind = Time.realtimeSinceStartup + 1f;
                m_Store = FindObjectOfType<UIPremiumStoreViewModel>();
            }
            if (m_Store == null || !m_Store.isActiveAndEnabled) { m_Rows = null; m_ButtonRect = new Rect(); m_Block.Release(); return; }
            m_Block.Update(Open, m_ButtonRect);
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
            if (Open) { DrawWindow(S); return; }
            InventoryItemData item = Selected();
            if (item != m_Checked) { m_Checked = item; m_CheckedHas = item != null && Has(item); }
            if (!m_CheckedHas) { m_ButtonRect = new Rect(); return; }
            Transform frame = m_Store.transform.Find("Panel_R/Panel_Overlay/Frame_Details");
            Rect f = frame != null && frame.gameObject.activeInHierarchy ? ScreenRect(frame) : new Rect();
            Transform panel = m_Store.transform.Find("Panel_R");
            Rect p = panel != null ? ScreenRect(panel) : new Rect();
            // Left of the card count when there is one, else the top left corner of the item's panel.
            m_ButtonRect = f.width > 0 ? new Rect(f.x - 196 * S, f.y, 186 * S, f.height)
                : new Rect(p.x + 14 * S, p.y + 30 * S, 186 * S, 34 * S);
            if (GameSkin.GoldButton(m_ButtonRect, Loc.Get("pc_button"), true, 15)) Show(item);
        }

        private void Show(InventoryItemData item)
        {
            m_Item = item;
            m_Rows = new List<Row>();
            m_Summary = Describe(item, m_Rows);
            m_Scroll = Vector2.zero;
        }

        private void DrawWindow(float S)
        {
            Rect win = Controls.Window(980, 820, Loc.Get("pc_title", m_Item.m_Name));
            float pad = 34 * S;
            GUIStyle sum = GameSkin.Style("body", 16, GameSkin.Text, TextAnchor.UpperLeft, true);
            float sh = sum.CalcHeight(new GUIContent(m_Summary), win.width - 2 * pad);
            GUI.Label(new Rect(win.x + pad, win.y + 54 * S, win.width - 2 * pad, sh), m_Summary, sum);
            float top = win.y + 64 * S + sh;
            Rect area = new Rect(win.x + pad, top, win.width - 2 * pad, win.yMax - 84 * S - top);
            Controls.Frame(area, null);
            Rect inner = new Rect(area.x + 8 * S, area.y + 8 * S, area.width - 16 * S, area.height - 16 * S);
            float rowH = 30 * S;
            GUIStyle head = GameSkin.Style("title", 18, GameSkin.Gold);
            GUIStyle name = GameSkin.Style("body", 16, GameSkin.Text);
            GUIStyle det = GameSkin.Style("body", 15, GameSkin.Muted, TextAnchor.MiddleRight);
            m_Scroll = Controls.List(inner, m_Scroll, m_Rows.Count, rowH, i =>
            {
                Row r = m_Rows[i];
                Rect rr = new Rect(0, i * (rowH + 4 * S), inner.width - 24 * S, rowH);
                if (r.Header) GameSkin.Label(rr, r.Text, head);
                else
                {
                    GUI.Label(new Rect(rr.x + 16 * S, rr.y, rr.width - 200 * S, rr.height), r.Text, name);
                    GUI.Label(new Rect(rr.xMax - 190 * S, rr.y, 190 * S, rr.height), r.Detail, det);
                }
                return false;
            }, i => { });
            if (GameSkin.RedButton(new Rect(win.center.x - 90 * S, win.yMax - 70 * S, 180 * S, 52 * S), Loc.Get("close"))) m_Rows = null;
        }

        // ------------------------------------------------------------------
        // What a product can give
        // ------------------------------------------------------------------

        private static readonly ERarity[] Rarities = { ERarity.Legendary, ERarity.Epic, ERarity.Rare, ERarity.Uncommon, ERarity.Common };

        /// <summary>Whether the window has something to show for this item.</summary>
        public static bool Has(InventoryItemData item)
        {
            return Describe(item, null) != null;
        }

        /// <summary>The product's summary, its pools added to rows (rows null: only checks); null if not described.</summary>
        private static string Describe(InventoryItemData item, List<Row> rows)
        {
            if (item == null) return null;
            if (Zymmer.IsKismetPack(item))
            {
                List<Loot.Drop> bundle = Zymmer.KismetBundle(item.m_Name);
                if (bundle.Count == 0) return null;
                if (rows != null) AddDrops(rows, Loc.Get("pc_items"), bundle);
                return Loc.Get("pc_kismet");
            }
            string kind = SpecialPacks.KindOf(item);
            if (kind == "randomkismet")
            {
                if (rows != null)
                    foreach (InventoryItemData k in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                        if (Zymmer.IsKismetPack(k) && Zymmer.KismetBundle(k.m_Name).Count > 0) AddDrops(rows, k.m_Name, Zymmer.KismetBundle(k.m_Name));
                return Loc.Get("pc_randomkismet");
            }
            InventoryCardPack pack = item as InventoryCardPack;
            if (pack == null) return null;
            CardSetTemplate set;
            Singleton<TemplateManager>.Instance.CardSets.TryGetValue(pack.m_SetId, out set);
            switch (kind)
            {
                case "fullset":
                    if (set == null) return null;
                    if (rows != null) foreach (ERarity r in Rarities) AddCards(rows, r, Progression.Pool(set, r));
                    return Loc.Get("pc_fullset");
                case "trials":
                {
                    if (set == null) return null;
                    string race = Array.Find(SpecialPacks.Races, x => item.m_Name.StartsWith(x));
                    if (rows != null)
                        foreach (ERarity r in new[] { ERarity.Rare, ERarity.Uncommon, ERarity.Common })
                        {
                            List<CardTemplate> pool = Progression.Pool(set, r);
                            List<CardTemplate> of = race == null ? pool : pool.FindAll(c => c.HasSubtype(race == "Shin'hare" ? "Shin" : race));
                            AddCards(rows, r, of.Count > 0 ? of : pool);
                        }
                    return Loc.Get("pc_trials");
                }
                case "common":
                    if (rows != null) AddCards(rows, ERarity.Common, SpecialPacks.StandardPool(ERarity.Common));
                    return Loc.Get("pc_common");
                case "gauntlet":
                    if (rows != null) { AddCards(rows, ERarity.Uncommon, SpecialPacks.StandardPool(ERarity.Uncommon)); AddCards(rows, ERarity.Common, SpecialPacks.StandardPool(ERarity.Common)); }
                    return Loc.Get("pc_gauntlet");
                case "dualshard":
                    if (set == null) return null;
                    if (rows != null) foreach (ERarity r in new[] { ERarity.Legendary, ERarity.Rare, ERarity.Uncommon, ERarity.Common }) AddCards(rows, r, Progression.Pool(set, r));
                    return Loc.Get("pc_dualshard");
                case "kismetdraft":
                    if (rows != null)
                        foreach (ERarity r in new[] { ERarity.Legendary, ERarity.Rare, ERarity.Uncommon, ERarity.Common })
                        {
                            List<CardTemplate> all = new List<CardTemplate>();
                            foreach (CardSetTemplate s in SpecialPacks.BoosterSets()) all.AddRange(Progression.Pool(s, r));
                            AddCards(rows, r, all);
                        }
                    return Loc.Get("pc_kismetdraft");
                case "gencon":
                    if (set == null) return null;
                    if (rows != null) foreach (ERarity r in Rarities) AddCards(rows, r, Progression.Pool(set, r));
                    return Loc.Get("pc_gencon");
            }
            if (set == null) return null;
            if (pack.m_CardPackType == ECardPackType.PrimalPack)
            {
                if (rows != null) { AddCards(rows, ERarity.Legendary, Progression.Pool(set, ERarity.Legendary)); AddCards(rows, ERarity.Rare, Progression.Pool(set, ERarity.Rare)); AddChest(rows, set); }
                return Loc.Get("pc_primal");
            }
            if (pack.m_CardPackType != ECardPackType.BoosterPack) return null;
            if (rows != null)
            {
                foreach (ERarity r in Rarities) AddCards(rows, r, Progression.Pool(set, r));
                AddChest(rows, set);
            }
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

        private static void AddCards(List<Row> rows, ERarity rarity, List<CardTemplate> pool)
        {
            if (pool.Count == 0) return;
            List<CardTemplate> sorted = new List<CardTemplate>(pool);
            sorted.Sort((a, b) => string.Compare(a.m_Name, b.m_Name, StringComparison.OrdinalIgnoreCase));
            rows.Add(new Row { Header = true, Text = RarityName(rarity) + "  (" + sorted.Count + ")" });
            foreach (CardTemplate c in sorted) rows.Add(new Row { Text = c.m_Name, Detail = Loc.Get("pc_owned", Collection.Count(c.m_Id)) });
        }

        /// <summary>The set's chest (Packs: one per booster, legendary for a primal pack), from Zymmer's "Set N Chest Loot".</summary>
        private static void AddChest(List<Row> rows, CardSetTemplate set)
        {
            string source = "Set " + Zymmer.SetNumber(set) + " Chest Loot";
            List<Loot.Drop> drops = new List<Loot.Drop>();
            foreach (InventoryItemData i in Zymmer.Equipment(source)) drops.Add(new Loot.Drop { Item = i.m_Id });
            foreach (CardTemplate c in Zymmer.Cards(source)) drops.Add(new Loot.Drop { Card = c });
            foreach (InventoryMercenaryData m in Zymmer.MercenariesOf(source)) drops.Add(new Loot.Drop { Item = m.m_Id });
            if (drops.Count > 0) AddDrops(rows, Loc.Get("pc_chest"), drops);
        }

        private static void AddDrops(List<Row> rows, string title, List<Loot.Drop> drops)
        {
            rows.Add(new Row { Header = true, Text = title + "  (" + drops.Count + ")" });
            foreach (Loot.Drop d in drops)
            {
                if (d.Card != null) { rows.Add(new Row { Text = d.Card.m_Name, Detail = Loc.Get("pc_owned", Collection.Count(d.Card.m_Id)) }); continue; }
                InventoryItemData i = Singleton<TemplateManager>.Instance.GetInventoryItem(d.Item);
                if (i == null) continue;
                string what = i is InventoryMercenaryData ? Loc.Get("pc_mercenary") : i is InventoryEquipmentData ? RarityName(((InventoryEquipmentData)i).m_Rarity) : "";
                rows.Add(new Row { Text = i.m_Name + (what.Length > 0 ? "  · " + what : ""), Detail = Loc.Get("pc_owned", Inventory.Count(i.m_Id)) });
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
