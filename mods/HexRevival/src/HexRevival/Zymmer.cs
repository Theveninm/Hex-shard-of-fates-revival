using System;
using System.Collections.Generic;
using Game.Shared;
using Game.Shared.Mechanics;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// What "Zymmer's notes" say each card, equipment and mercenary came from (Progression.Src.Zymmer): a player's
    /// hand-made catalogue of the game, every entry with the pack, chest, Wheel of Fate, arena or quest that gave it.
    /// Generated into ZymmerData.cs by tools/gen_zymmer.py (data/zymmer_sources.tsv, names matched to the game's ids).
    /// Hex Codex's equipment list adds the pieces the notes miss (see AddCodexEquipment). The notes say WHICH items a source held, never how often: every weight and count drawn from these pools is a
    /// PLACEHOLDER unless noted.
    ///
    /// Used for: the AZ2 pack and dungeon pack contents (Loot), the AZ1 dungeon packs' own equipment missing from the
    /// 2016 loot tables (Loot), the Frost Ring Arena chests (Arena), the set chests of the account level rewards
    /// (Progression), the Siege Pack equipment (PrizePacks) and the Kismet's Reserve packs (Store).
    /// The notes agree with the mod's earlier sources on the AZ1 race rewards (all eight races, both items), the
    /// Crayburn Castle packs and the set chest / Wheel of Fate mercenaries (Src.Game item descriptions).
    /// </summary>
    public static class Zymmer
    {
        public class Entry
        {
            public char Kind;          // 'c' card, 'e' equipment, 'm' mercenary
            public ResourceId Id;
            public string Name;
            public string Parent;      // equipment: the card it equips (as the notes name it)
        }

        private static Dictionary<string, List<Entry>> s_Sources;

        /// <summary>
        /// The equipment Hex Codex lists for a source and the notes do not (Src.HexCodex: its equipment list with where each
        /// piece was obtained; assets/codex_equipment.txt, built by tools/gen_codex_equipment.py): added to the same pools,
        /// matched to the game's items by name.
        /// </summary>
        private static void AddCodexEquipment()
        {
            try
            {
                Dictionary<string, InventoryEquipmentData> byName = new Dictionary<string, InventoryEquipmentData>(StringComparer.OrdinalIgnoreCase);
                foreach (InventoryItemData i in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                {
                    InventoryEquipmentData e = i as InventoryEquipmentData;
                    if (e != null && !string.IsNullOrEmpty(e.m_Name) && !byName.ContainsKey(e.m_Name.Trim())) byName[e.m_Name.Trim()] = e;
                }
                int added = 0;
                using (System.IO.Stream st = typeof(Zymmer).Assembly.GetManifestResourceStream("HexRevival.codex_equipment.txt"))
                using (System.IO.StreamReader r = new System.IO.StreamReader(st))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        if (line.Length == 0 || line[0] == '#') continue;
                        string[] p = line.Split('\t');
                        InventoryEquipmentData e;
                        if (p.Length < 2 || !byName.TryGetValue(p[1].Trim(), out e)) continue;
                        List<Entry> l;
                        if (!s_Sources.TryGetValue(p[0], out l)) s_Sources[p[0]] = l = new List<Entry>();
                        if (l.Exists(x => x.Id == e.m_Id)) continue;
                        l.Add(new Entry { Kind = 'e', Id = e.m_Id, Name = e.m_Name.Trim(), Parent = "" });
                        added++;
                    }
                }
                Debug.Log("[HexRevival] [Zymmer] " + added + " equipment pieces added from Hex Codex");
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Zymmer] Hex Codex equipment: " + e.Message); }
        }

        /// <summary>Every entry of a source, as named in the notes ("Alachian Sea Pack", "Set 6 Chest Loot"...).</summary>
        public static List<Entry> Source(string source)
        {
            if (s_Sources == null)
            {
                s_Sources = new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);
                foreach (string[] s in ZymmerData.Sources)
                {
                    List<Entry> l = new List<Entry>();
                    for (int i = 1; i + 3 < s.Length; i += 4)
                    {
                        ResourceId id;
                        try { id = new ResourceId(new Guid(s[i + 1])); } catch { continue; }
                        l.Add(new Entry { Kind = s[i][0], Id = id, Name = s[i + 2], Parent = s[i + 3] });
                    }
                    s_Sources[s[0]] = l;
                }
                AddCodexEquipment();
            }
            List<Entry> res;
            return s_Sources.TryGetValue(source, out res) ? res : new List<Entry>();
        }

        public static List<CardTemplate> Cards(string source)
        {
            List<CardTemplate> res = new List<CardTemplate>();
            foreach (Entry e in Source(source))
            {
                CardTemplate t;
                if (e.Kind == 'c' && Singleton<TemplateManager>.Instance.Cards.TryGetValue(e.Id, out t) && t != null) res.Add(t);
            }
            return res;
        }

        public static List<InventoryItemData> Equipment(string source)
        {
            List<InventoryItemData> res = new List<InventoryItemData>();
            foreach (Entry e in Source(source))
            {
                if (e.Kind != 'e') continue;
                InventoryItemData i = Singleton<TemplateManager>.Instance.GetInventoryItem(e.Id);
                if (i is InventoryEquipmentData) res.Add(i);
            }
            return res;
        }

        public static List<InventoryMercenaryData> MercenariesOf(string source)
        {
            List<InventoryMercenaryData> res = new List<InventoryMercenaryData>();
            foreach (Entry e in Source(source))
            {
                if (e.Kind != 'm') continue;
                InventoryMercenaryData m = Singleton<TemplateManager>.Instance.GetInventoryItem(e.Id) as InventoryMercenaryData;
                if (m != null && m.MercenaryTemplate != null) res.Add(m);
            }
            return res;
        }

        // ------------------------------------------------------------------
        // Weighted picks
        // ------------------------------------------------------------------

        /// <summary>
        /// Weight of one card / one equipment by rarity: the mean drop % of one item of that rarity in the Adventure Zone 1
        /// pack (Src.LootTables2016, 1,162 packs). Used for packs the loot tables do not cover (AZ2): PLACEHOLDER by analogy.
        /// </summary>
        public static float CardWeight(ERarity r)
        {
            switch (r)
            {
                case ERarity.Common: case ERarity.Uncommon: return 1.70f;
                case ERarity.Rare: return 2.81f;
                default: return 0.51f;   // legendary and above
            }
        }

        public static float EquipmentWeight(ERarity r)
        {
            switch (r)
            {
                case ERarity.Common: return 1.56f;
                case ERarity.Uncommon: return 1.54f;
                case ERarity.Rare: return 0.97f;
                default: return 0.45f;
            }
        }

        public static T Weighted<T>(List<T> l, Func<T, float> weight) where T : class
        {
            if (l == null || l.Count == 0) return null;
            float total = 0;
            foreach (T x in l) total += Math.Max(0f, weight(x));
            if (total <= 0) return l[UnityEngine.Random.Range(0, l.Count)];
            float v = UnityEngine.Random.value * total;
            foreach (T x in l) { v -= Math.Max(0f, weight(x)); if (v <= 0) return x; }
            return l[l.Count - 1];
        }

        public static ERarity RarityOf(InventoryItemData i)
        {
            InventoryEquipmentData eq = i as InventoryEquipmentData;
            return eq != null ? eq.m_Rarity : ERarity.Common;
        }

        /// <summary>A random card of a source (weighted by rarity), or null.</summary>
        public static Loot.Drop RandomCard(string source)
        {
            CardTemplate c = Weighted(Cards(source), t => CardWeight(t.m_CardRarity));
            return c != null ? new Loot.Drop { Card = c } : null;
        }

        /// <summary>A random equipment of a source (weighted by rarity), or null.</summary>
        public static Loot.Drop RandomEquipment(string source)
        {
            InventoryItemData i = Weighted(Equipment(source), x => EquipmentWeight(RarityOf(x)));
            return i != null ? new Loot.Drop { Item = i.m_Id } : null;
        }

        /// <summary>A mercenary of a source the player does not own yet, or null.</summary>
        public static Loot.Drop NewMercenary(string source)
        {
            List<InventoryMercenaryData> l = MercenariesOf(source).FindAll(m => Inventory.Count(m.m_Id) == 0);
            return l.Count > 0 ? new Loot.Drop { Item = l[UnityEngine.Random.Range(0, l.Count)].m_Id } : null;
        }

        // ------------------------------------------------------------------
        // Set chests and the Wheel of Fate
        // ------------------------------------------------------------------

        private static readonly string[] PvPSets =
            { "Shards of Fate", "Shattered Destiny", "Armies of Myth", "Primal Dawn", "Herofall", "Scars of War", "Frostheart", "Dead of Winter", "Doombringer" };

        /// <summary>The set's number (1-9) for a PvP set, 0 otherwise.</summary>
        public static int SetNumber(CardSetTemplate set)
        {
            return set == null ? 0 : Array.IndexOf(PvPSets, set.m_Name) + 1;
        }

        /// <summary>The PvP set of that number (1-9), null if none.</summary>
        public static CardSetTemplate SetOfNumber(int n)
        {
            if (n < 1 || n > PvPSets.Length) return null;
            foreach (CardSetTemplate s in Singleton<TemplateManager>.Instance.CardSets.Values)
                if (s != null && s.m_Name == PvPSets[n - 1]) return s;
            return null;
        }

        // ------------------------------------------------------------------
        // The Wheel of Fate's prizes: which version (Src.Survey2014)
        // ------------------------------------------------------------------

        // Src.Survey2014, the players' Wheels of Fate survey (2,167 spins, Google sheet 1CAfwv-xqUZN-pJXx4r7Kpbz0PCtcmOFuzh0eSFAQ6Gw),
        // as tabulated by Src.HexServer (MTGQuarters/hex-server, branch pve-equipment-and-mercenaries,
        // services/wheel_of_fate.py PRIZE_TIER_WEIGHTS, 2026-10-01): how many of each prize won were the set's common /
        // uncommon / rare version. Their totals match the survey's outcome counts used in Packs (mercenaries 66,
        // equipment 78, PvE cards 78, alternate arts 81). The versions are the items ranked by their rarity in the game
        // data (the Set 1-3 wheel PvE cards are Uncommon, Rare and Legendary, Src.Zymmer).
        private static readonly int[] WheelMercenaryTiers = { 59, 5, 2 };
        private static readonly int[] WheelEquipmentTiers = { 10, 8, 60 };
        private static readonly int[] WheelPveCardTiers = { 64, 9, 5 };

        /// <summary>
        /// Items grouped by rarity rank (the lowest is the common version), a group drawn with the survey's counts (the
        /// last count for further groups), then one item of it, evenly; only the allowed ones (e.g. not owned yet).
        /// </summary>
        private static T Tiered<T>(List<T> all, Func<T, int> rank, Func<T, bool> allowed, int[] counts) where T : class
        {
            List<int> ranks = new List<int>();
            foreach (T x in all) { int r = rank(x); if (!ranks.Contains(r)) ranks.Add(r); }
            ranks.Sort();
            List<List<T>> tiers = new List<List<T>>();
            List<int> w = new List<int>();
            for (int i = 0; i < ranks.Count; i++)
            {
                int r = ranks[i];
                List<T> t = all.FindAll(x => rank(x) == r && allowed(x));
                if (t.Count == 0) continue;
                tiers.Add(t);
                w.Add(Math.Max(1, counts[Math.Min(i, counts.Length - 1)]));
            }
            if (tiers.Count == 0) return null;
            int total = 0;
            foreach (int x in w) total += x;
            int v = UnityEngine.Random.Range(0, total);
            for (int i = 0; i < tiers.Count; i++)
            {
                if (v < w[i]) return tiers[i][UnityEngine.Random.Range(0, tiers[i].Count)];
                v -= w[i];
            }
            return tiers[tiers.Count - 1][0];
        }

        /// <summary>An equipment of a set's Wheel of Fate (Src.Zymmer list), its version drawn with Src.Survey2014's counts.</summary>
        public static Loot.Drop WheelEquipment(string source)
        {
            InventoryItemData i = Tiered(Equipment(source), x => (int)RarityOf(x), x => true, WheelEquipmentTiers);
            return i != null ? new Loot.Drop { Item = i.m_Id } : null;
        }

        /// <summary>A mercenary of a set's Wheel of Fate not owned yet (Src.Zymmer list), its version drawn with Src.Survey2014's counts; null if none.</summary>
        public static Loot.Drop WheelMercenary(string source)
        {
            InventoryMercenaryData m = Tiered(MercenariesOf(source), x => (int)x.Rarity, x => Inventory.Count(x.m_Id) == 0, WheelMercenaryTiers);
            return m != null ? new Loot.Drop { Item = m.m_Id } : null;
        }

        /// <summary>One of a set's three Wheel of Fate PvE cards (Src.Zymmer list), its version drawn with Src.Survey2014's counts.</summary>
        public static Loot.Drop WheelPveCard(string source)
        {
            CardTemplate c = Tiered(Cards(source), x => (int)x.m_CardRarity, x => true, WheelPveCardTiers);
            return c != null ? new Loot.Drop { Card = c } : null;
        }

        /// <summary>
        /// The wheel's "alternate art card" (Src.Game: the client's payout table, three skulls): an alternate art (epic)
        /// card of the set. Which one: PLACEHOLDER, any, evenly (only Set 1's three are reported, and second hand).
        /// </summary>
        public static Loot.Drop WheelAltArt(CardSetTemplate set)
        {
            List<CardTemplate> l = Progression.Pool(set, ERarity.Epic);
            return l.Count > 0 ? new Loot.Drop { Card = l[UnityEngine.Random.Range(0, l.Count)] } : null;
        }

        // ------------------------------------------------------------------
        // Set chests: what each chest rarity holds (Src.Survey2015)
        // ------------------------------------------------------------------

        public enum ChestPrize { Equipment, Stardust, AltArt, PveCard, Mercenary, Sleeve, Booster }

        /// <summary>One line of a chest rarity's table: a kind of prize (its rarity or tier) and how often it came.</summary>
        public class ChestOdds
        {
            public ChestPrize Kind;
            public ERarity Rarity;   // equipment: its rarity; stardust: its kind (Promo = promo stardust)
            public string Tier;      // alternate arts: "low", "high", "any"; PvE cards: "rare" (uncommon or rare), "any"
            public int Weight;
        }

        private static ChestOdds O(ChestPrize k, ERarity r, int w) { return new ChestOdds { Kind = k, Rarity = r, Weight = w }; }
        private static ChestOdds O(ChestPrize k, string tier, int w) { return new ChestOdds { Kind = k, Tier = tier, Weight = w }; }

        /// <summary>
        /// Src.Survey2015, the players' treasure chest survey (July 2015, about 1,700 drops of Set 1-3 chests whose kind
        /// was recorded, Google sheet 193MgoYZ5OJc5F7UMXq39-GBmzBUhCT7s9ltcKGWtIVs), as tabulated by Src.HexServer
        /// (MTGQuarters/hex-server, branch pve-equipment-and-mercenaries, services/chest_loot.py CHEST_CONTENT_WEIGHTS,
        /// 2026-10-01): per chest rarity (common, uncommon, rare, legendary, primal), how often (per mille) each kind of
        /// prize came. A kind with nothing left to give (no item of that rarity, every mercenary or sleeve owned) is
        /// left out and the others share its part. Used for every set: the survey only covers Sets 1-3 (extrapolation).
        /// </summary>
        public static readonly ChestOdds[][] ChestTable =
        {
            new[] {   // common
                O(ChestPrize.Equipment, ERarity.Common, 674), O(ChestPrize.Equipment, ERarity.Uncommon, 136), O(ChestPrize.Equipment, ERarity.Rare, 48),
                O(ChestPrize.Stardust, ERarity.Common, 45), O(ChestPrize.Stardust, ERarity.Uncommon, 25), O(ChestPrize.AltArt, "low", 67) },
            new[] {   // uncommon
                O(ChestPrize.Equipment, ERarity.Common, 214), O(ChestPrize.Equipment, ERarity.Uncommon, 580), O(ChestPrize.Equipment, ERarity.Rare, 76),
                O(ChestPrize.Stardust, ERarity.Uncommon, 22), O(ChestPrize.Stardust, ERarity.Rare, 14), O(ChestPrize.AltArt, "low", 69),
                O(ChestPrize.PveCard, "rare", 25) },
            new[] {   // rare
                O(ChestPrize.Equipment, ERarity.Common, 103), O(ChestPrize.Equipment, ERarity.Uncommon, 194), O(ChestPrize.Equipment, ERarity.Rare, 455),
                O(ChestPrize.Stardust, ERarity.Rare, 20), O(ChestPrize.Stardust, ERarity.Legendary, 12), O(ChestPrize.AltArt, "low", 59),
                O(ChestPrize.PveCard, "rare", 55), O(ChestPrize.Mercenary, "", 59) },
            new[] {   // legendary
                O(ChestPrize.Equipment, ERarity.Uncommon, 172), O(ChestPrize.Equipment, ERarity.Rare, 178), O(ChestPrize.Equipment, ERarity.Legendary, 249),
                O(ChestPrize.Stardust, ERarity.Promo, 18), O(ChestPrize.AltArt, "any", 65), O(ChestPrize.PveCard, "any", 154),
                O(ChestPrize.Mercenary, "", 136), O(ChestPrize.Booster, "", 12) },
            new[] {   // primal
                O(ChestPrize.Equipment, ERarity.Rare, 85), O(ChestPrize.Equipment, ERarity.Legendary, 237), O(ChestPrize.Stardust, ERarity.Promo, 34),
                O(ChestPrize.AltArt, "high", 212), O(ChestPrize.PveCard, "any", 204), O(ChestPrize.Mercenary, "", 110),
                O(ChestPrize.Sleeve, "", 102), O(ChestPrize.Booster, "", 8) },
        };

        /// <summary>
        /// The alternate arts (epic cards) the survey saw in chests (Src.Survey2015 as tabulated by Src.HexServer,
        /// CHEST_AA_CARDS): "low" from common to rare chests, "high" from legendary and primal ones. Other sets: any
        /// alternate art of the set (PLACEHOLDER).
        /// </summary>
        private static readonly Dictionary<string, string[][]> ChestAltArts = new Dictionary<string, string[][]>
        {
            { "Shards of Fate", new[] { new[] { "Savage Raider", "Hex Engine", "Wall of Corpses", "Rot Caster" }, new[] { "Mastery of Time", "Crash of Beasts" } } },
            { "Shattered Destiny", new[] { new[] { "Constantina", "Royal Valkyr", "Paladin of the Necropolis", "Psychotic Anarchist" }, new[] { "Darkspire Tyrant", "Arborean Rootfather" } } },
        };

        /// <summary>Items per chest: 1, 2 for a legendary chest, 3 for a primal one (the client: "Legendary and Primal chests will have additional items"; how many: PLACEHOLDER).</summary>
        public static int ChestItems(int tier) { return tier >= 4 ? 3 : tier == 3 ? 2 : 1; }

        /// <summary>The table's line for a chest rarity (0 common .. 4 primal).</summary>
        public static int ChestTier(ERarity r) { return r == ERarity.Common ? 0 : r == ERarity.Uncommon ? 1 : r == ERarity.Rare ? 2 : 3; }

        /// <summary>The set's chest alternate arts for a tier (see ChestAltArts).</summary>
        public static List<CardTemplate> ChestAltArtPool(CardSetTemplate set, string tier)
        {
            List<CardTemplate> epics = Progression.Pool(set, ERarity.Epic);
            string[][] names;
            if (set == null || !ChestAltArts.TryGetValue(set.m_Name, out names)) return epics;
            List<string> want = new List<string>();
            if (tier != "high") want.AddRange(names[0]);
            if (tier != "low") want.AddRange(names[1]);
            List<CardTemplate> l = epics.FindAll(t => want.Contains(t.m_Name.Trim()));
            return l.Count > 0 ? l : epics;
        }

        /// <summary>The set's chest PvE cards for a tier: "rare" = its uncommon and rare ones, "any" = legendary too.</summary>
        public static List<CardTemplate> ChestPveCards(string source, string tier)
        {
            return Cards(source).FindAll(t => t.m_CardRarity == ERarity.Uncommon || t.m_CardRarity == ERarity.Rare
                || (tier == "any" && t.m_CardRarity == ERarity.Legendary));
        }

        /// <summary>The set's booster pack item, invalid if none.</summary>
        public static ResourceId BoosterOf(CardSetTemplate set)
        {
            if (set == null) return ResourceId.Invalid;
            foreach (InventoryItemData i in Singleton<TemplateManager>.Instance.InventoryItems.Values)
            {
                InventoryCardPack p = i as InventoryCardPack;
                if (p != null && p.m_SetId == set.m_Id && p.m_CardPackType == ECardPackType.BoosterPack) return p.m_Id;
            }
            return ResourceId.Invalid;
        }

        /// <summary>The stardust item of a kind (Promo: promo stardust).</summary>
        public static ResourceId StardustItem(ERarity r)
        {
            switch (r)
            {
                case ERarity.Common: return BuiltInResources.CommonStardust;
                case ERarity.Uncommon: return BuiltInResources.UncommonStardust;
                case ERarity.Rare: return BuiltInResources.RareStardust;
                case ERarity.Legendary: return BuiltInResources.LegendaryStardust;
                default: return BuiltInResources.PromoStardust;
            }
        }

        /// <summary>A chest of a set number opened (account level rewards, dev): see OpenChest(set, tier).</summary>
        public static List<Loot.Drop> OpenChest(int setNumber, ERarity chest)
        {
            return OpenChest(SetOfNumber(setNumber), ChestTier(chest));
        }

        /// <summary>
        /// A chest of a set opened (tier 0 common .. 4 primal): ChestItems(tier) prizes, each of a kind drawn from
        /// ChestTable (Src.Survey2015), then one of the set's items of that kind: equipment, PvE cards and mercenaries of
        /// the notes' "Set N Chest Loot" list (Src.Zymmer), the set's chest sleeves (Src.HexCodex), its alternate arts,
        /// stardust, its booster. Which item of a kind: evenly. Empty if the set has no chest list.
        /// </summary>
        public static List<Loot.Drop> OpenChest(CardSetTemplate set, int tier)
        {
            List<Loot.Drop> res = new List<Loot.Drop>();
            string source = "Set " + SetNumber(set) + " Chest Loot";
            if (set == null || Source(source).Count == 0) return res;
            tier = Math.Max(0, Math.Min(4, tier));
            for (int k = 0; k < ChestItems(tier); k++)
            {
                List<ChestOdds> left = new List<ChestOdds>(ChestTable[tier]);
                while (left.Count > 0)
                {
                    ChestOdds o = Weighted(left, x => x.Weight);
                    Loot.Drop d = ChestDrop(set, source, o, res);
                    if (d != null) { res.Add(d); break; }
                    left.Remove(o);
                }
            }
            return res;
        }

        /// <summary>One prize of a kind, or null if the set has nothing of it left (not twice the same item in a chest).</summary>
        private static Loot.Drop ChestDrop(CardSetTemplate set, string source, ChestOdds o, List<Loot.Drop> already)
        {
            switch (o.Kind)
            {
                case ChestPrize.Equipment:
                {
                    List<InventoryItemData> l = Equipment(source).FindAll(i => RarityOf(i) == o.Rarity && !already.Exists(d => d.Card == null && d.Item == i.m_Id));
                    return l.Count > 0 ? new Loot.Drop { Item = l[UnityEngine.Random.Range(0, l.Count)].m_Id } : null;
                }
                case ChestPrize.Stardust:
                {
                    ResourceId id = StardustItem(o.Rarity);
                    return id.IsValid ? new Loot.Drop { Item = id } : null;
                }
                case ChestPrize.AltArt:
                {
                    List<CardTemplate> l = ChestAltArtPool(set, o.Tier);
                    return l.Count > 0 ? new Loot.Drop { Card = l[UnityEngine.Random.Range(0, l.Count)] } : null;
                }
                case ChestPrize.PveCard:
                {
                    List<CardTemplate> l = ChestPveCards(source, o.Tier);
                    return l.Count > 0 ? new Loot.Drop { Card = l[UnityEngine.Random.Range(0, l.Count)] } : null;
                }
                case ChestPrize.Mercenary:
                {
                    Loot.Drop d = NewMercenary(source);
                    return d != null && !already.Exists(x => x.Card == null && x.Item == d.Item) ? d : null;
                }
                case ChestPrize.Sleeve:
                {
                    InventoryDeckSleeve sl = Sleeves.OfSet("chest", set.m_Name);
                    return sl != null && !already.Exists(x => x.Card == null && x.Item == sl.m_Id) ? new Loot.Drop { Item = sl.m_Id } : null;
                }
                case ChestPrize.Booster:
                {
                    ResourceId b = BoosterOf(set);
                    return b.IsValid ? new Loot.Drop { Item = b } : null;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Kismet's Reserve packs (store)
        // ------------------------------------------------------------------

        /// <summary>
        /// The Kismet's Reserve packs of the game data and their theme: the notes list all their items under
        /// "Kismet's Reserve Pack" (Src.Zymmer: 8 PvE cards, 16 equipment, 4 mercenaries); the game data splits them in
        /// four themes, by the equipment's design notes and the mercenaries' descriptions (Src.Game): Oni, Boonberries,
        /// Vampires and the 2017 Convocation one. Each theme is 2 cards, the 4 equipment that equip them and 1 mercenary.
        /// The plain "Kismet's Reserve Pack" (a Scars of War promo item) taken as the 2017 Convocation theme: inferred.
        /// What one pack gave (here: the whole theme, once) is PLACEHOLDER.
        /// </summary>
        private static readonly string[][] KismetThemes =
        {
            new[] { "Kismet's Reserve Oni Pack", "KR Oni", "Oni - Kismet's Reserve" },
            new[] { "Kismet's Boonberries Pack", "KR - Boonberries", "Boonberry - Kismet's Reserve" },
            new[] { "Kismet's Vampire Pack", "KR - Vampires", "Vampires - Kismet's Reserve" },
            new[] { "Kismet's Reserve Pack", "Scars 2017 Conv", "2017 Convocation" },
        };

        public static bool IsKismetPack(InventoryItemData item)
        {
            return item != null && item.m_Name != null && Array.Exists(KismetThemes, t => t[0] == item.m_Name.Trim());
        }

        /// <summary>The theme of a Kismet's Reserve pack: its cards, equipment and mercenary.</summary>
        public static List<Loot.Drop> KismetBundle(string packName)
        {
            List<Loot.Drop> res = new List<Loot.Drop>();
            string[] theme = Array.Find(KismetThemes, t => t[0] == (packName ?? "").Trim());
            if (theme == null) return res;
            List<Entry> all = Source("Kismet's Reserve Pack");
            HashSet<string> cards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Entry e in all)
            {
                if (e.Kind != 'e') continue;
                InventoryItemData i = Singleton<TemplateManager>.Instance.GetInventoryItem(e.Id);
                if (i == null || (i.m_DesignNotes ?? "").Trim() != theme[1]) continue;
                res.Add(new Loot.Drop { Item = i.m_Id });
                if (!string.IsNullOrEmpty(e.Parent)) cards.Add(e.Parent);
            }
            foreach (Entry e in all)
            {
                if (e.Kind == 'c' && cards.Contains(e.Name))
                {
                    CardTemplate t;
                    if (Singleton<TemplateManager>.Instance.Cards.TryGetValue(e.Id, out t) && t != null) res.Add(new Loot.Drop { Card = t });
                }
                if (e.Kind == 'm')
                {
                    InventoryMercenaryData m = Singleton<TemplateManager>.Instance.GetInventoryItem(e.Id) as InventoryMercenaryData;
                    if (m != null && (m.m_Description ?? "").Trim() == theme[2]) res.Add(new Loot.Drop { Item = m.m_Id });
                }
            }
            return res;
        }

        // ------------------------------------------------------------------
        // Dev command
        // ------------------------------------------------------------------

        private static string Describe(Loot.Drop d)
        {
            if (d == null) return "-";
            if (d.Card != null) return "card " + d.Card.m_Name + " (" + d.Card.m_CardRarity + ")";
            InventoryItemData i = Singleton<TemplateManager>.Instance.GetInventoryItem(d.Item);
            if (i is InventoryMercenaryData) return "mercenary " + i.m_Name;
            return "item " + (i != null ? i.m_Name + " (" + RarityOf(i) + ")" : d.Item.ToString());
        }

        /// <summary>
        /// Dev: "zymmer" = every source with its resolved cards / equipment / mercenaries; "zymmer chest 9 Rare" = a chest
        /// drawn (not granted; "primal" too); "zymmer kismet Kismet's Vampire Pack" = a pack's theme; "zymmer pack Ruins of Kukatan" = a pack.
        /// </summary>
        public static string Command(string[] parts)
        {
            if (parts.Length > 3 && parts[1] == "chest")
            {
                int n; int.TryParse(parts[2], out n);
                int tier = parts[3].Equals("primal", StringComparison.OrdinalIgnoreCase) ? 4 : ChestTier((ERarity)Enum.Parse(typeof(ERarity), parts[3], true));
                return string.Join(", ", OpenChest(SetOfNumber(n), tier).ConvertAll(Describe).ToArray());
            }
            if (parts.Length > 2 && parts[1] == "kismet")
                return string.Join(", ", KismetBundle(string.Join(" ", parts, 2, parts.Length - 2)).ConvertAll(Describe).ToArray());
            if (parts.Length > 2 && parts[1] == "pack")
                return string.Join(", ", Loot.Open(string.Join(" ", parts, 2, parts.Length - 2)).ConvertAll(Describe).ToArray());
            List<string> o = new List<string>();
            foreach (string[] s in ZymmerData.Sources)
                o.Add(s[0] + ": " + Cards(s[0]).Count + " cards, " + Equipment(s[0]).Count + " equipment, " + MercenariesOf(s[0]).Count + " mercenaries"
                      + " (" + (s.Length - 1) / 4 + " listed)");
            return string.Join("\n", o.ToArray());
        }
    }
}
