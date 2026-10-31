using System;
using System.Collections.Generic;
using System.IO;
using Game.Client;
using Game.Client.Network.Profile;
using Game.Shared;
using Game.Shared.Domain;
using Game.Shared.Mechanics;
using Game.Shared.Network.Profile;
using Game.Shared.Resources;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Booster packs opened on the game's Pack Opening screen, their treasure chests and the Wheel of Fate, as in the
    /// original: a bought booster goes to the inventory; opening it (ProfileService.OpenCardPack) drops its cards in the
    /// fountain and gives a treasure chest; the chest can be spun on the Wheel of Fate for gold (SpinWheelOfFate) and is
    /// opened for its loot (OpenChest). Before 1.2.10 packs were opened at purchase, without chest (SirBeastmaster).
    ///
    /// Sources. Src.Game: the requests and their answers; the spin costs (ChestUtils.GetChestSpinCost: 1,200 / 3,100 /
    /// 8,500 / 30,000 gold, Primal chests and free spins 0; the same on the wiki's "Wheels of Fate"); the client's texts: a booster holds "1 Treasure Chest", a
    /// primal pack "1 Legendary Treasure Chest", "Most chests contain a single item. Legendary and Primal chests will have
    /// additional items", and the payout table: 1 / 2 / 3 eyes = paid spin / free spin / upgrade chest; three stars a
    /// mercenary, crowns a PvE card's equipment, hands an upgrade, moons a PvE card, mushrooms sleeves, skulls an alternate
    /// art card, hearts two upgrades, spiders a PvP rare card, each with a paid spin; "1 or 2 gold icons gift Gold. 3 gold
    /// icons gift exclusive rewards", "3 Red Icons gift exclusive rewards". Src.Zymmer: what each set's wheel and chests
    /// held. Src.Survey2014: how often each outcome came, by chest rarity (see Survey). Src.HexServer: 500 / 2,500 gold
    /// for 1 / 2 gold reels. Src.HexCodex: each set's Wheel of Fate and chest sleeves (see Sleeves). PLACEHOLDER: the
    /// reel colors' odds; the prizes the mod has no item for (alternate arts: a rare of the set instead); "exclusive rewards" = one of the set's wheel items; a
    /// legendary chest 2 items, a primal one 3.
    /// </summary>
    public static class Packs
    {
        private class Chest
        {
            public ulong Id;
            public ETreasureChestType Rarity;
            public EChestSpinStatus Spin;
            public ResourceId Set;   // the booster's card set
            public ResourceId Template = ResourceId.Invalid;   // a campaign pack's item (Howling Plains Pack...), else invalid
            public string LootPack;                             // a campaign pack's loot table (Loot.Open), else null
        }

        /// <summary>Chest instance ids: apart from the inventory's (numbered from 1 at each login).</summary>
        private const ulong ChestBase = 800000000UL;

        private static readonly Dictionary<ulong, Chest> s_Chests = new Dictionary<ulong, Chest>();
        private static ulong s_NextChest = ChestBase + 1;
        private static string FilePath { get { return Saves.File("chests.txt"); } }
        private static readonly System.Random s_Rng = new System.Random();

        // ------------------------------------------------------------------
        // Requests
        // ------------------------------------------------------------------

        /// <summary>Pack opening, wheel and chest requests (null: not one of them).</summary>
        public static object Handle(object args)
        {
            try
            {
                OpenCardPackRequestArgs open = args as OpenCardPackRequestArgs;
                if (open != null) return OpenPacks(open.ItemId, open.OpenAmount);
                SpinWheelOfFateRequestArgs spin = args as SpinWheelOfFateRequestArgs;
                if (spin != null) return Spin(spin.ChestID);
                OpenChestRequestArgs chest = args as OpenChestRequestArgs;
                if (chest != null) return OpenChests(chest.ChestIds);
            }
            catch (Exception e)
            {
                // An exception would leave the screen waiting forever: an error answer instead.
                Debug.LogError("[HexRevival] [Packs] " + args.GetType().Name + ": " + e);
                if (args is OpenCardPackRequestArgs) { OpenCardPackResponse r = Empty<OpenCardPackResponse>(); r.Error = EOpenCardPackError.InternalServerError; return r; }
                if (args is SpinWheelOfFateRequestArgs) { SpinWheelOfFateResponse r = Empty<SpinWheelOfFateResponse>(); r.Error = ESpinWheelOfFateError.InternalServerError; return r; }
                if (args is OpenChestRequestArgs) { OpenChestResponse r = Empty<OpenChestResponse>(); r.Error = EOpenChestError.InternalServerError; return r; }
            }
            return null;
        }

        private static T Empty<T>() where T : new()
        {
            T r = LocalServer.Ok<T>();
            OpenCardPackResponse p = r as OpenCardPackResponse;
            if (p != null) { p.NewCardInstances = new List<card_instance_bits>(); p.NewGemInstances = new List<inventory_bits>(); p.NewChestInstances = new List<chest_bits>(); }
            SpinWheelOfFateResponse s = r as SpinWheelOfFateResponse;
            if (s != null) { s.RewardedItems = new List<inventory_bits>(); s.RewardCards = new List<card_instance_bits>(); s.SpinEntryColors = new List<int>(); s.SpinEntrySymbols = new List<int>(); }
            OpenChestResponse c = r as OpenChestResponse;
            if (c != null) { c.validChestIds = new List<ulong>(); c.inventoryTemplateIDs = new List<ResourceId>(); c.cardTemplateIDs = new List<ResourceId>(); }
            return r;
        }

        /// <summary>Opens n packs of that item (-1: all): their cards, and a chest each.</summary>
        private static OpenCardPackResponse OpenPacks(ResourceId item, int amount)
        {
            OpenCardPackResponse r = Empty<OpenCardPackResponse>();
            InventoryCardPack pack = Singleton<TemplateManager>.Instance.GetInventoryItem(item) as InventoryCardPack;
            CardSetTemplate set;
            if (pack == null || !Singleton<TemplateManager>.Instance.CardSets.TryGetValue(pack.m_SetId, out set)) { r.Error = EOpenCardPackError.InvalidItemType; return r; }
            int owned = Inventory.Count(item);
            int n = amount < 0 ? owned : Math.Min(amount, owned);
            if (n <= 0) { r.Error = EOpenCardPackError.NotEnoughInventory; return r; }
            Load();
            bool primal = pack.m_CardPackType == ECardPackType.PrimalPack;
            for (int i = 0; i < n; i++)
            {
                List<CardTemplate> cards = primal ? Primal(set) : Progression.OpenPack(set);
                foreach (CardTemplate t in cards) r.NewCardInstances.AddRange(Collection.GrantSilently(t.m_Id, 1));
                // The chest of the pack (a primal pack's is legendary, Src.HexServer).
                Chest c = NewChest(primal ? ETreasureChestType.Legendary : RollRarity(), set.m_Id);
                r.NewChestInstances.Add(Bits(c));
                // "Obtained by opening [set] Booster Packs" (Src.HexCodex: Doombringer's): sometimes one of them
                // (PLACEHOLDER chance), given when the opening is over.
                if (!primal && UnityEngine.Random.value < BoosterSleeveChance)
                {
                    InventoryDeckSleeve sl = Sleeves.OfSet("booster", set.m_Name);
                    if (sl != null) s_AfterOpening.Add(sl);
                }
            }
            Inventory.Remove(item, n);
            Save();
            // The client takes the cards and chests from the answer (PlayerProfile.HandleOpenCardPack); the packs are
            // ours to take, once the (delayed) answer is delivered: earlier, the screen's pack list is rebuilt before
            // the opening animation (issue #25).
            s_ToRemove.Add(new KeyValuePair<ResourceId, int>(item, n));
            Debug.Log("[HexRevival] [Packs] opened " + n + " x " + pack.m_Name + ": " + r.NewCardInstances.Count + " card(s), " + n + " chest(s)");
            return r;
        }

        private static readonly List<KeyValuePair<ResourceId, int>> s_ToRemove = new List<KeyValuePair<ResourceId, int>>();

        /// <summary>Sleeves found in the packs being opened, given once the opening's answer was delivered.</summary>
        private static readonly List<InventoryDeckSleeve> s_AfterOpening = new List<InventoryDeckSleeve>();

        /// <summary>After an OpenCardPack answer was delivered (LocalServer.Pump): the opened packs leave the client's inventory.</summary>
        public static void OnPackAnswerDelivered()
        {
            foreach (InventoryDeckSleeve sl in s_AfterOpening) Sleeves.Grant(sl);
            s_AfterOpening.Clear();
            foreach (KeyValuePair<ResourceId, int> kv in s_ToRemove)
                try { GameClient.Instance.PlayerProfile.RemoveInventoryItem(kv.Key, kv.Value); }
                catch (Exception e) { Debug.LogWarning("[HexRevival] [Packs] " + e.Message); }
            s_ToRemove.Clear();
        }

        /// <summary>A primal pack: 2 legendaries and 13 rares of the set (Src.Forum2017).</summary>
        public static List<CardTemplate> Primal(CardSetTemplate set)
        {
            List<CardTemplate> res = new List<CardTemplate>();
            for (int k = 0; k < 2; k++) res.Add(Progression.RandomCard(set, ERarity.Legendary, null));
            for (int k = 0; k < 13; k++) res.Add(Progression.RandomCard(set, ERarity.Rare, null));
            res.RemoveAll(c => c == null);
            return res;
        }

        /// <summary>
        /// A booster's chest: common. Src.Survey2015, the players' chest survey (Google sheet
        /// 193MgoYZ5OJc5F7UMXq39-GBmzBUhCT7s9ltcKGWtIVs): all 93 chests recorded from boosters were common, 1 item each;
        /// better chests came from the wheel's upgrades. PLACEHOLDER: no rarer chest at all (a small sample). The wiki
        /// (Src.Wiki, "Treasure Chest", 2014) says "of a random rarity (common, uncommon, rare or legendary)", without
        /// odds; the later survey is kept.
        /// </summary>
        private static ETreasureChestType RollRarity() { return ETreasureChestType.Common; }

        // ------------------------------------------------------------------
        // The Wheel of Fate
        // ------------------------------------------------------------------

        private enum Outcome { Mercenary, Equipment, Sleeves, PveCard, AltArt, PvpRare, Upgrade, DoubleUpgrade, FreeSpin, PaidSpin, Failed }

        /// <summary>
        /// Src.Survey2014: the players' Wheels of Fate survey (2,167 spins, Google sheet 1CAfwv-xqUZN-pJXx4r7Kpbz0PCtcmOFuzh0eSFAQ6Gw):
        /// how many spins of each chest rarity gave each outcome, in Outcome's order. Legendary chests (4 spins in the
        /// survey) use the rare chests' counts.
        /// </summary>
        private static readonly int[][] Survey =
        {
            new[] { 53, 69, 50, 62, 67, 81, 213, 64, 109, 241, 798 },   // common chest (1,807 spins)
            new[] { 12, 9, 12, 10, 9, 15, 35, 8, 17, 28, 132 },          // uncommon (287)
            new[] { 1, 0, 1, 6, 5, 6, 8, 3, 3, 10, 26 },                 // rare (69)
        };

        private static Outcome Draw(ETreasureChestType rarity)
        {
            int[] w = Survey[Math.Min(2, Math.Max(0, (int)rarity))];
            int total = 0;
            foreach (int x in w) total += x;
            int v = s_Rng.Next(total);
            for (int i = 0; i < w.Length; i++) { if (v < w[i]) return (Outcome)i; v -= w[i]; }
            return Outcome.Failed;
        }

        /// <summary>Three reel symbols showing the outcome (the client's payout table).</summary>
        private static void ShowReels(Outcome o, List<int> reels)
        {
            int triple = -1;
            switch (o)
            {
                case Outcome.Mercenary: triple = 0; break;
                case Outcome.Equipment: triple = 1; break;
                case Outcome.PveCard: triple = 3; break;
                case Outcome.Sleeves: triple = 5; break;
                case Outcome.AltArt: triple = 6; break;
                case Outcome.DoubleUpgrade: triple = 7; break;
                case Outcome.PvpRare: triple = 9; break;
                case Outcome.Upgrade: triple = s_Rng.Next(2) == 0 ? Eye : 2; break;   // three eyes or three hands
            }
            if (triple >= 0) { reels.Add(triple); reels.Add(triple); reels.Add(triple); return; }
            int eyes = o == Outcome.PaidSpin ? 1 : o == Outcome.FreeSpin ? 2 : 0;
            List<int> others = new List<int>(Symbols);
            others.Remove(Eye);
            for (int i = 0; i < 3 - eyes; i++) reels.Add(others[s_Rng.Next(others.Count)]);
            // A failed spin is not a triple.
            if (eyes == 0 && reels[0] == reels[1] && reels[1] == reels[2]) reels[2] = others[(others.IndexOf(reels[2]) + 1) % others.Count];
            for (int i = 0; i < eyes; i++) reels.Insert(s_Rng.Next(reels.Count + 1), Eye);
        }

        // Reel symbols of the client (UIPackListViewModel.SetPayoutHighlights): 0 star, 1 crown, 2 hand, 3 moon, 4 eye,
        // 5 mushroom, 6 skull, 7 heart, 9 spider; colors: 0 plain, 1 red, 2 gold.
        private static readonly int[] Symbols = { 0, 1, 2, 3, 4, 5, 6, 7, 9 };
        private const int Eye = 4;

        private static SpinWheelOfFateResponse Spin(ulong chestId)
        {
            SpinWheelOfFateResponse r = Empty<SpinWheelOfFateResponse>();
            Load();
            Chest c;
            if (!s_Chests.TryGetValue(chestId, out c)) { r.Error = ESpinWheelOfFateError.ChestDoesNotBelongToPlayer; return r; }
            if (c.Spin == EChestSpinStatus.NoSpin) { r.Error = ESpinWheelOfFateError.NoSpinLeftOnChest; return r; }
            int cost = SpinCost(c);
            if (Saves.Current.Gold < cost) { r.Error = ESpinWheelOfFateError.NotEnoughGoldForSpin; r.Chest = Bits(c); return r; }
            Saves.Current.Gold -= cost;

            // The outcome, with the community survey's frequencies for the chest's rarity (Src.Survey2014), then reels
            // that show it. The reel colors are independent: PLACEHOLDER red 20%, gold 5%.
            Outcome o = Draw(c.Rarity);
            ShowReels(o, r.SpinEntrySymbols);
            for (int i = 0; i < 3; i++)
            {
                double v = s_Rng.NextDouble();
                r.SpinEntryColors.Add(v < 0.05 ? 2 : v < 0.25 ? 1 : 0);
            }
            List<int> sym = r.SpinEntrySymbols, col = r.SpinEntryColors;
            int golds = col.FindAll(x => x == 2).Count, reds = col.FindAll(x => x == 1).Count;
            CardSetTemplate set;
            Singleton<TemplateManager>.Instance.CardSets.TryGetValue(c.Set, out set);
            string wheel = "Set " + Zymmer.SetNumber(set) + " Wheels of Fate";
            List<string> won = new List<string>();

            // Every triple also gives a paid spin (the client's payout table).
            c.Spin = o == Outcome.Failed ? EChestSpinStatus.NoSpin : o == Outcome.FreeSpin ? EChestSpinStatus.FreeSpin : EChestSpinStatus.PaidSpin;
            switch (o)
            {
                case Outcome.PaidSpin: won.Add("paid re-spin"); break;
                case Outcome.FreeSpin: won.Add("free re-spin"); break;
                case Outcome.Upgrade: Upgrade(c, 1); won.Add("upgrade"); if (sym[0] == Eye) c.Spin = EChestSpinStatus.NoSpin; break;   // 3 eyes: the upgrade only
                case Outcome.DoubleUpgrade: Upgrade(c, 2); won.Add("double upgrade"); break;
                case Outcome.Mercenary: Prize(Zymmer.NewMercenary(wheel) ?? Zymmer.WheelEquipment(wheel), r, won); break;
                case Outcome.Equipment: Prize(Zymmer.WheelEquipment(wheel), r, won); break;
                case Outcome.PveCard: Prize(Zymmer.RandomCard(wheel), r, won); break;
                case Outcome.Sleeves:
                {
                    // One of the set's Wheel of Fate sleeves not owned yet (Src.HexCodex, see Sleeves); none left: a rare.
                    InventoryDeckSleeve sl = Sleeves.OfSet("wheel", set != null ? set.m_Name : null);
                    Prize(sl != null ? new Loot.Drop { Item = sl.m_Id } : RareOf(set), r, won);
                    break;
                }
                case Outcome.AltArt:
                case Outcome.PvpRare: Prize(RareOf(set), r, won); break;   // alternate arts: PLACEHOLDER, see the class
            }
            if (golds == 1) r.GoldAward += 500;
            else if (golds == 2) r.GoldAward += 2500;
            if (golds == 3 || reds == 3)
            {
                // "Exclusive rewards": one of the set's wheel items (PLACEHOLDER, see the class).
                Loot.Drop ex = s_Rng.Next(2) == 0 ? Zymmer.RandomCard(wheel) : Zymmer.WheelEquipment(wheel);
                Prize(ex ?? RareOf(set), r, won);
            }
            if (r.GoldAward > 0) { Saves.Current.Gold += r.GoldAward; won.Add(r.GoldAward + " gold"); }
            Saves.SaveCurrent();
            Save();
            r.Chest = Bits(c);
            LocalServer.Later(() => { try { GameClient.Instance.PlayerProfile.UpdateBalance((int)Saves.Current.Gold, (int)Saves.Current.Platinum); } catch { } });
            Debug.Log("[HexRevival] [Packs] wheel " + string.Join(",", sym.ConvertAll(x => x.ToString()).ToArray()) + " / " + string.Join(",", col.ConvertAll(x => x.ToString()).ToArray())
                + " for " + cost + " gold: " + (won.Count > 0 ? string.Join(", ", won.ToArray()) : "nothing") + "; chest " + c.Rarity + " " + c.Spin);
            return r;
        }

        private static int SpinCost(Chest c)
        {
            if (c.Rarity == ETreasureChestType.Primal || c.Spin == EChestSpinStatus.FreeSpin) return 0;
            switch (c.Rarity)
            {
                case ETreasureChestType.Common: return 1200;
                case ETreasureChestType.Uncommon: return 3100;
                case ETreasureChestType.Rare: return 8500;
                default: return 30000;
            }
        }

        /// <summary>
        /// A chest's rarity goes up; an upgraded legendary chest "becomes a primal chest", the only way to get one
        /// (Src.Wiki: hextcg.fandom.com "Treasure Chest"; the client knows primal chests and opens them).
        /// </summary>
        private static void Upgrade(Chest c, int steps)
        {
            int v = Math.Min((int)ETreasureChestType.Primal, (int)c.Rarity + steps);
            if (c.Rarity < ETreasureChestType.Primal) c.Rarity = (ETreasureChestType)v;
        }

        /// <summary>A prize of the wheel: silently granted, the answer gives it to the client (PlayerProfile.HandleChestUpdate).</summary>
        private static void Prize(Loot.Drop d, SpinWheelOfFateResponse r, List<string> won)
        {
            if (d == null) return;
            if (d.Card != null) { r.RewardCards.AddRange(Collection.GrantSilently(d.Card.m_Id, 1)); won.Add(d.Card.m_Name); return; }
            InventoryItemData data = Singleton<TemplateManager>.Instance.GetInventoryItem(d.Item);
            InventoryMercenaryData merc = data as InventoryMercenaryData;
            if (merc != null && Inventory.Count(merc.m_Id) > 0) return;
            inventory_bits it = Inventory.GrantSilently(d.Item);
            if (it == null) return;
            r.RewardedItems.Add(it);
            if (merc != null) { if (merc.DeckTemplate != null) Collection.GrantDeck(merc.DeckTemplate); Mercenaries.OnEarned(merc.m_Id); }
            if (data != null) won.Add(data.m_Name.Trim());
        }

        private static Loot.Drop RareOf(CardSetTemplate set)
        {
            CardTemplate t = Progression.RandomCard(set, ERarity.Rare, null);
            return t != null ? new Loot.Drop { Card = t } : null;
        }

        // ------------------------------------------------------------------
        // Opening a chest
        // ------------------------------------------------------------------

        /// <summary>PLACEHOLDER: how often a chest also holds one of its set's sleeves, and a booster one of its own.</summary>
        internal const float ChestSleeveChance = 0.05f, BoosterSleeveChance = 0.02f;

        private static OpenChestResponse OpenChests(List<ulong> ids)
        {
            OpenChestResponse r = Empty<OpenChestResponse>();
            Load();
            foreach (ulong id in ids ?? new List<ulong>())
            {
                Chest c;
                if (!s_Chests.TryGetValue(id, out c)) { r.Error = EOpenChestError.InvalidChestID; continue; }
                if (c.LootPack != null)
                {
                    // A campaign pack (Howling Plains Pack, Tranquil Dream Pack...): its table's cards and equipment.
                    List<Loot.Drop> loot = Loot.Open(c.LootPack);
                    foreach (Loot.Drop d in loot)
                    {
                        Progression.GiveDrop(d);
                        if (d.Card != null) r.cardTemplateIDs.Add(d.Card.m_Id); else r.inventoryTemplateIDs.Add(d.Item);
                    }
                    s_Chests.Remove(id);
                    r.validChestIds.Add(id);
                    ulong taken = id;
                    LocalServer.Later(() => { try { GameClient.Instance.PlayerProfile.RemoveInventoryItem(new UID(UID.Type.InventoryItem, taken)); } catch { } });
                    Debug.Log("[HexRevival] [Packs] campaign pack " + c.LootPack + " opened: " + loot.Count + " item(s)");
                    continue;
                }
                CardSetTemplate set;
                Singleton<TemplateManager>.Instance.CardSets.TryGetValue(c.Set, out set);
                // The set's chest loot (Src.Zymmer pools, Zymmer.OpenChest); a primal chest: one item more (PLACEHOLDER).
                ERarity rarity = c.Rarity == ETreasureChestType.Common ? ERarity.Common : c.Rarity == ETreasureChestType.Uncommon ? ERarity.Uncommon
                    : c.Rarity == ETreasureChestType.Rare ? ERarity.Rare : ERarity.Legendary;
                int setNumber = Zymmer.SetNumber(set);
                List<Loot.Drop> drops = Zymmer.OpenChest(setNumber, rarity);
                if (c.Rarity == ETreasureChestType.Primal) drops.AddRange(Zymmer.OpenChest(setNumber, ERarity.Rare));
                // "Found in Chests for [set]" (Src.HexCodex): one of the set's chest sleeves not owned yet, 5% of the
                // chests (PLACEHOLDER chance).
                if (UnityEngine.Random.value < ChestSleeveChance)
                {
                    InventoryDeckSleeve sl = Sleeves.OfSet("chest", set != null ? set.m_Name : null);
                    if (sl != null) drops.Add(new Loot.Drop { Item = sl.m_Id });
                }
                if (drops.Count == 0)
                {
                    // A set without a chest list: gold (the account chests' old PLACEHOLDER).
                    r.goldAcquired += rarity >= ERarity.Rare ? 1000 : 500;
                    Saves.Current.Gold += rarity >= ERarity.Rare ? 1000 : 500;
                }
                foreach (Loot.Drop d in drops)
                {
                    Progression.GiveDrop(d);
                    if (d.Card != null) r.cardTemplateIDs.Add(d.Card.m_Id); else r.inventoryTemplateIDs.Add(d.Item);
                }
                s_Chests.Remove(id);
                r.validChestIds.Add(id);
                ulong gone = id;
                LocalServer.Later(() => { try { GameClient.Instance.PlayerProfile.RemoveInventoryItem(new UID(UID.Type.InventoryItem, gone)); } catch { } });
                Debug.Log("[HexRevival] [Packs] chest " + c.Rarity + " (set " + setNumber + ") opened: " + drops.Count + " item(s)");
            }
            Saves.SaveCurrent();
            Save();
            LocalServer.Later(() => { try { GameClient.Instance.PlayerProfile.UpdateBalance((int)Saves.Current.Gold, (int)Saves.Current.Platinum); } catch { } });
            return r;
        }

        // ------------------------------------------------------------------
        // Storage
        // ------------------------------------------------------------------

        private static chest_bits Bits(Chest c)
        {
            return new chest_bits { InventoryId = c.Id, ChestRarity = (int)c.Rarity, WOFSpinStatus = (int)c.Spin, BoosterPackType = c.Set, TempateID = c.Template.IsValid ? c.Template : BuiltInResources.CommonTreasureChest };
        }

        private static Chest NewChest(ETreasureChestType rarity, ResourceId set)
        {
            Chest c = new Chest { Id = s_NextChest++, Rarity = rarity, Spin = EChestSpinStatus.PaidSpin, Set = set };
            s_Chests[c.Id] = c;
            return c;
        }

        /// <summary>
        /// A campaign pack won (Src.Video: the reward window read "You received 1 Howling Plains Pack(s)" with an Open
        /// button, MHS Ardent 2017-01-15 [D1m-I19keFw] 2:14; Zymmer 2026-10-31: packs were opened automatically): a chest of
        /// the pack's item, given to the client now, opened later (the window's Open button, or the Pack Opening screen)
        /// for the loot table's cards. Returns the inventory line for the reward window, null if the game has no such item.
        /// </summary>
        public static inventory_bits GiveCampaignPack(string lootPack)
        {
            InventoryItemData item = null;
            string name = Loot.PackItemName(lootPack);
            foreach (InventoryItemData i in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                if (i is InventoryTreasureChest && i.m_Name != null && i.m_Name.Trim() == name) { item = i; break; }
            if (item == null) return null;
            Load();
            Chest c = new Chest { Id = s_NextChest++, Rarity = ETreasureChestType.Common, Spin = EChestSpinStatus.NoSpin, Set = ResourceId.Invalid, Template = item.m_Id, LootPack = lootPack };
            s_Chests[c.Id] = c;
            Save();
            // The client registers the chest itself from the reward's inventory line (CacheRewards: adding it here too
            // threw "same key").
            inventory_bits inv = new inventory_bits();
            inv.Id = c.Id;
            inv.TemplateID = item.m_Id;
            inv.ItemQuantity = 1;
            inv.ClaimDate = DateTime.MinValue;
            inv.EscrowStatus = "";
            inv.OwnerReckId = GameClient.Instance.PlayerProfile.ReckoningID.GetInstanceId();
            return inv;
        }

        private static string s_Loaded;

        private static void Load()
        {
            if (s_Loaded == FilePath) return;
            s_Loaded = FilePath;
            s_Chests.Clear();
            s_NextChest = ChestBase + 1;
            if (!File.Exists(FilePath)) return;
            try
            {
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] p = line.Split(' ');
                    if (p.Length < 4) continue;
                    Chest c = new Chest { Id = ulong.Parse(p[0]), Rarity = (ETreasureChestType)int.Parse(p[1]), Spin = (EChestSpinStatus)int.Parse(p[2]), Set = new ResourceId(p[3]) };
                    // A campaign pack: its item, then its loot table (the rest of the line, it may hold spaces).
                    if (p.Length >= 6) { c.Template = new ResourceId(p[4]); c.LootPack = string.Join(" ", p, 5, p.Length - 5); }
                    s_Chests[c.Id] = c;
                    if (c.Id >= s_NextChest) s_NextChest = c.Id + 1;
                }
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Packs] read: " + e); }
        }

        private static void Save()
        {
            try
            {
                List<string> lines = new List<string>();
                foreach (Chest c in s_Chests.Values)
                    lines.Add(c.Id + " " + (int)c.Rarity + " " + (int)c.Spin + " " + c.Set + (c.LootPack != null ? " " + c.Template + " " + c.LootPack : ""));
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Packs] save: " + e); }
        }

        /// <summary>At login: the save's unopened chests to the client's profile (its chest list and inventory).</summary>
        public static void LoadClient(PlayerProfile profile)
        {
            s_Loaded = null;
            Load();
            foreach (Chest c in s_Chests.Values)
            {
                profile.AddChestItem(c.Id, Bits(c));
                profile.AddInventoryItem(new UID(UID.Type.InventoryItem, c.Id), c.Template.IsValid ? c.Template : BuiltInResources.CommonTreasureChest, 1);
            }
            if (s_Chests.Count > 0) Debug.Log("[HexRevival] [Packs] " + s_Chests.Count + " chest(s) waiting");
        }

        /// <summary>Dev: "packs [state] | give &lt;pack_name&gt; [n] | open | flip | gather".</summary>
        public static string Command(string[] parts)
        {
            Load();
            string sub = parts.Length > 1 ? parts[1] : "state";
            if (sub == "open")
            {
                // dev: "packs open" = drops the first pack of the Pack Opening screen into the well (as the drag does)
                UIPackListViewModel vm = UnityEngine.Object.FindObjectOfType<UIPackListViewModel>();
                if (vm == null) return "not on the Pack Opening screen";
                foreach (PlayerInventoryItem i in GameClient.Instance.PlayerProfile.InventoryItems)
                    if (i.Template is InventoryCardPack)
                    {
                        typeof(UIPackListViewModel).GetField("m_CurrentlySelectedPack", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(vm, i);
                        vm.OnOpenPackClicked(i);
                        return "opening " + i.Template.m_Name;
                    }
                return "no pack";
            }
            if (sub == "flip" || sub == "gather")
            {
                // dev: "packs flip" = turns the pack's rare over (a click on it), "packs gather" = closes the cards (Continue)
                int k = 0;
                foreach (UIPackContentViewModel vm in UnityEngine.Object.FindObjectsOfType<UIPackContentViewModel>())
                    if (vm.IsVisible()) { if (sub == "flip") vm.FlipRare(); else vm.OnClose(); k++; }
                return sub + " on " + k + " view(s)";
            }
            if (sub == "give" && parts.Length > 2)
            {
                string name = parts[2].Replace('_', ' ');
                int n = parts.Length > 3 ? int.Parse(parts[3]) : 1;
                foreach (InventoryItemData i in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                    if (i is InventoryCardPack && i.m_Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        for (int k = 0; k < n; k++) { inventory_bits b = Inventory.GrantSilently(i.m_Id); if (b != null) GameClient.Instance.PlayerProfile.AddInventoryItem(b); }
                        return n + " x " + i.m_Name;
                    }
                return "no pack " + name;
            }
            List<string> l = new List<string>();
            foreach (Chest c in s_Chests.Values) l.Add(c.Id + " " + c.Rarity + " " + c.Spin);
            return s_Chests.Count + " chest(s)" + (l.Count > 0 ? ": " + string.Join(", ", l.ToArray()) : "");
        }
    }
}
