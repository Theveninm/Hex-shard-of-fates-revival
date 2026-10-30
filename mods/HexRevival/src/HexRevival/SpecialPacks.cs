using System;
using System.Collections.Generic;
using Game.Shared;
using Game.Shared.Domain;
using Game.Shared.Mechanics;
using Game.Client.Network.Escrow;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// The store's other products (Src.HexCodex: its product list; Src.Game: the items, their pack types and texts),
    /// opened at purchase (the store's loot window shows what they held):
    /// - Full sets (FullSet): one of every card of the set (PLACEHOLDER: one copy);
    /// - Starter Trials packs (AITournamentPack: the Starter Trials were AI games that "earn free cards for your deck",
    ///   Src.Game): 6 commons, 2 uncommons and 1 rare of the pack's set, of its race (PLACEHOLDER contents);
    /// - Common Only Booster Pack (CommonPack): "10 common cards that are Standard Format legal" (Src.Game), sold in the
    ///   store's gold tab (Src.Game, the tutorial's objective);
    /// - Standard Format Gauntlet Pack (VoltronPack): "cards from all Standard Format legal sets. Does not include rares,
    ///   legendaries, or chests" (Src.Game): 12 commons and 4 uncommons (PLACEHOLDER counts, a booster's);
    /// - Doombringer Sharded Draft Pack (DualShardPack, "Only Generates 2 colors"): a Doombringer booster's cards in two
    ///   shards drawn at random (and shardless cards), no chest (PLACEHOLDER: a booster's counts);
    /// - Kismet's Draft Pack (KismetPack): a booster's counts drawn from every booster set (PLACEHOLDER);
    /// - GenCon 2016 Booster Pack (GenconPack, Herofall): a Herofall booster's cards (PLACEHOLDER);
    /// - Random Previous Kismet Pack: "A random previously released Kismet Pack" (Src.Game): one of the store's Kismet's
    ///   Reserve packs, delivered as that pack;
    /// - the holiday gift boxes (Harvest of Souls, Night of Bells, Feast of Abundance): one of the holiday's sleeves and
    ///   equipment pieces not owned yet (Src.HexCodex, Src.Zymmer; PLACEHOLDER contents).
    /// Prices: PLACEHOLDER (see GoldPrice).
    /// </summary>
    public static class SpecialPacks
    {
        /// <summary>The kinds of products made here, by item (null: not one of them).</summary>
        public static string KindOf(InventoryItemData item)
        {
            InventoryCardPack p = item as InventoryCardPack;
            string name = item != null && item.m_Name != null ? item.m_Name.Trim() : "";
            if (p != null)
                switch (p.m_CardPackType)
                {
                    case ECardPackType.FullSet: return "fullset";
                    case ECardPackType.AITournamentPack: return "trials";
                    case ECardPackType.CommonPack: return "common";
                    case ECardPackType.VoltronPack: return "gauntlet";
                    case ECardPackType.DualShardPack: return "dualshard";
                    case ECardPackType.KismetPack: return "kismetdraft";
                    case ECardPackType.GenconPack: return "gencon";
                }
            if (name == "Random Previous Kismet Pack") return "randomkismet";
            if (item is InventoryTreasureChest && name.EndsWith("Gift Box")) return "giftbox";
            return null;
        }

        /// <summary>PLACEHOLDER gold prices.</summary>
        public static int GoldPrice(string kind)
        {
            switch (kind)
            {
                case "fullset": return 200000;
                case "common": return 500;
                case "trials": case "gauntlet": case "dualshard": case "kismetdraft": case "gencon": return 2000;   // a booster's
                case "randomkismet": return 20000;   // a Kismet's Reserve pack's
                case "giftbox": return 5000;
                default: return 0;
            }
        }

        private static readonly string[] Races = { "Coyotle", "Dwarf", "Elf", "Human", "Necrotic", "Orc", "Shin'hare", "Vennen" };

        /// <summary>One bought product: its cards and items into the response. False if it is not one of these.</summary>
        public static bool Deliver(InventoryItemData item, PurchaseItemResponse r)
        {
            string kind = KindOf(item);
            if (kind == null) return false;
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            CardSetTemplate set = null;
            InventoryCardPack pack = item as InventoryCardPack;
            if (pack != null) tm.CardSets.TryGetValue(pack.m_SetId, out set);
            List<CardTemplate> cards = new List<CardTemplate>();
            switch (kind)
            {
                case "fullset":
                    foreach (ERarity rarity in new[] { ERarity.Common, ERarity.Uncommon, ERarity.Rare, ERarity.Epic, ERarity.Legendary })
                        cards.AddRange(Progression.Pool(set, rarity));
                    break;
                case "trials":
                {
                    string race = Array.Find(Races, x => item.m_Name.StartsWith(x));
                    Func<CardTemplate, bool> ofRace = c => race != null && c.HasSubtype(race == "Shin'hare" ? "Shin" : race);
                    Draw(cards, Progression.Pool(set, ERarity.Common), ofRace, 6);
                    Draw(cards, Progression.Pool(set, ERarity.Uncommon), ofRace, 2);
                    Draw(cards, Progression.Pool(set, ERarity.Rare), ofRace, 1);
                    break;
                }
                case "common": Draw(cards, StandardPool(ERarity.Common), null, 10); break;
                case "gauntlet":
                    Draw(cards, StandardPool(ERarity.Common), null, 12);
                    Draw(cards, StandardPool(ERarity.Uncommon), null, 4);
                    break;
                case "dualshard":
                {
                    ECardShards[] all = { ECardShards.Blood, ECardShards.Diamond, ECardShards.Ruby, ECardShards.Sapphire, ECardShards.Wild };
                    int a = UnityEngine.Random.Range(0, 5), b = (a + 1 + UnityEngine.Random.Range(0, 4)) % 5;
                    ECardShards two = all[a] | all[b];
                    Func<CardTemplate, bool> inTwo = c => (c.CardShards & ~(two | ECardShards.Colorless)) == 0;
                    Draw(cards, Progression.Pool(set, ERarity.Common), inTwo, 12);
                    Draw(cards, Progression.Pool(set, ERarity.Uncommon), inTwo, 4);
                    Draw(cards, Progression.Pool(set, UnityEngine.Random.value < 0.125f ? ERarity.Legendary : ERarity.Rare), inTwo, 1);
                    break;
                }
                case "kismetdraft":
                {
                    List<CardSetTemplate> sets = BoosterSets();
                    for (int i = 0; i < 12; i++) Draw(cards, Progression.Pool(Any(sets), ERarity.Common), null, 1);
                    for (int i = 0; i < 4; i++) Draw(cards, Progression.Pool(Any(sets), ERarity.Uncommon), null, 1);
                    Draw(cards, Progression.Pool(Any(sets), UnityEngine.Random.value < 0.125f ? ERarity.Legendary : ERarity.Rare), null, 1);
                    break;
                }
                case "gencon": cards.AddRange(Progression.OpenPack(set)); break;
                case "randomkismet":
                {
                    List<InventoryItemData> kismet = new List<InventoryItemData>();
                    foreach (InventoryItemData i in tm.InventoryItems.Values)
                        if (Zymmer.IsKismetPack(i) && Zymmer.KismetBundle(i.m_Name).Count > 0) kismet.Add(i);
                    if (kismet.Count == 0) return false;
                    Store.DeliverKismet(kismet[UnityEngine.Random.Range(0, kismet.Count)], r);
                    return true;
                }
                case "giftbox":
                {
                    // Harvest of Souls: the Haunted Harvest's (the game's autumn holiday).
                    string holiday = item.m_Name.Replace("Gift Box", "").Trim();
                    if (holiday == "Harvest of Souls") holiday = "Haunted Harvest";
                    InventoryDeckSleeve sl = Sleeves.Unowned(Sleeves.Matching("^Awarded during the [0-9]{4} " + System.Text.RegularExpressions.Regex.Escape(holiday) + " Holiday"));
                    inventory_bits sb = sl != null ? Inventory.GrantSilently(sl.m_Id) : null;
                    if (sb != null) r.GrantedInventory.Add(sb);
                    InventoryItemData eq = Sleeves.HolidayEquipment(holiday);
                    inventory_bits eb = eq != null ? Inventory.GrantSilently(eq.m_Id) : null;
                    if (eb != null) r.GrantedInventory.Add(eb);
                    if (sb == null && eb == null) { Saves.Current.Gold += GoldPrice(kind); Saves.SaveCurrent(); Notices.Show(Loc.Get("giftbox_empty")); }
                    return true;
                }
            }
            foreach (CardTemplate c in cards) if (c != null) r.GrantedCards.AddRange(Collection.GrantSilently(c.m_Id, 1));
            Debug.Log("[HexRevival] [Store] " + item.m_Name + ": " + cards.Count + " card(s)");
            return true;
        }

        /// <summary>
        /// n cards of the pool (those the filter keeps; all of it if none does), all different while the pool has enough
        /// (a race's cards of a set are few).
        /// </summary>
        private static void Draw(List<CardTemplate> into, List<CardTemplate> pool, Func<CardTemplate, bool> filter, int n)
        {
            List<CardTemplate> p = filter == null ? pool : pool.FindAll(c => filter(c));
            if (p.Count == 0) p = pool;
            List<CardTemplate> left = new List<CardTemplate>();
            for (int i = 0; i < n && p.Count > 0; i++)
            {
                if (left.Count == 0) left.AddRange(p);
                int k = UnityEngine.Random.Range(0, left.Count);
                into.Add(left[k]);
                left.RemoveAt(k);
            }
        }

        private static CardSetTemplate Any(List<CardSetTemplate> sets) { return sets.Count > 0 ? sets[UnityEngine.Random.Range(0, sets.Count)] : null; }

        /// <summary>The 9 booster sets (Shards of Fate ... Doombringer).</summary>
        private static List<CardSetTemplate> BoosterSets()
        {
            List<CardSetTemplate> res = new List<CardSetTemplate>();
            foreach (CardSetTemplate s in Singleton<TemplateManager>.Instance.CardSets.Values)
                if (s != null && Zymmer.SetNumber(s) > 0) res.Add(s);
            return res;
        }

        /// <summary>The cards of a rarity of the Standard format's sets (the game's format rules).</summary>
        private static List<CardTemplate> StandardPool(ERarity rarity)
        {
            List<CardTemplate> res = new List<CardTemplate>();
            Format standard = DeckValidator.DeckFormatToNormalFormat(EDeckFormat.Standard);
            foreach (CardSetTemplate s in BoosterSets())
                if (standard.IsSetValid(s.m_Id)) res.AddRange(Progression.Pool(s, rarity));
            return res;
        }
    }
}
