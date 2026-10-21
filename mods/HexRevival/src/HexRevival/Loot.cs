using System;
using System.Collections.Generic;
using Game.Shared;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Campaign and dungeon pack contents. What a pack holds comes from the reward windows of gameplay
    /// videos (Src.Video, 25+ packs opened, 2016-2017): 2 commons of the first PvP sets (Shards of
    /// Fate, Shattered Destiny, Armies of Myth) + 1 card of the pack's own list + 1 equipment + either
    /// a 2nd equipment or a stardust (about 1 time in 3). Which card and which equipment: the community
    /// "AZ1 Loot Tables" spreadsheet (2016, Src.LootTables2016; data/az1_pack_loot_tables.csv,
    /// generated into LootData.cs), each pick weighted by its % drop chance; its unique equipment
    /// (1% in total) is kept as a 1-in-100 extra.
    /// </summary>
    public static class Loot
    {
        public const string AZ1Pack = "Adventure Zone 1";

        /// <summary>One dropped thing: a card or an inventory item (equipment).</summary>
        public class Drop
        {
            public CardTemplate Card;
            public ResourceId Item = ResourceId.Invalid;
        }

        private class Entry { public string Name; public float Pct; }

        private class PackTable
        {
            public readonly Dictionary<char, List<Entry>> Cat = new Dictionary<char, List<Entry>>();
        }

        private static Dictionary<string, PackTable> s_Packs;

        private static Dictionary<string, PackTable> Packs()
        {
            if (s_Packs != null) return s_Packs;
            s_Packs = new Dictionary<string, PackTable>(StringComparer.OrdinalIgnoreCase);
            foreach (string[] p in LootData.Packs)
            {
                PackTable t = new PackTable();
                for (int i = 2; i + 3 < p.Length; i += 4)
                {
                    char c = p[i][0];
                    List<Entry> l;
                    if (!t.Cat.TryGetValue(c, out l)) t.Cat[c] = l = new List<Entry>();
                    float pct;
                    float.TryParse(p[i + 3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pct);
                    l.Add(new Entry { Name = p[i + 1], Pct = pct });
                }
                s_Packs[p[0]] = t;
            }
            return s_Packs;
        }

        /// <summary>PvP sets of the 2 commons of a campaign pack (Src.Video).</summary>
        private static readonly string[] CommonSets = { "Shards of Fate", "Shattered Destiny", "Armies of Myth" };

        /// <summary>Chance that the 5th slot is a stardust instead of a 2nd equipment (Src.Video: about 1 time in 3).</summary>
        private const float StardustChance = 1f / 3f;

        /// <summary>
        /// Stardust of the 5th slot. Seen: Common, Uncommon, Rare, and Legendary once (Src.Video);
        /// the weights are a PLACEHOLDER.
        /// </summary>
        private static readonly KeyValuePair<string, float>[] Stardusts =
        {
            new KeyValuePair<string, float>("Common Stardust", 35), new KeyValuePair<string, float>("Uncommon Stardust", 35),
            new KeyValuePair<string, float>("Rare Stardust", 25), new KeyValuePair<string, float>("Legendary Stardust", 5),
        };

        /// <summary>
        /// AZ2 packs (the zone's and its dungeons'): no drop table of them has been found, so their card and
        /// equipment are drawn from the game's own AZ2 set (cards and equipment items), by rarity (PLACEHOLDER weights:
        /// uncommon 10, rare 3, legendary 1), with the AZ1 pack layout.
        /// </summary>
        public const string AZ2Pack = "Adventure Zone 2";
        private static readonly HashSet<string> AZ2Packs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { AZ2Pack, "Brutecrown Bluff", "Great Machine Graveyard", "The Kraken", "Ruins of Kukatan", "Kraken's Cove" };

        /// <summary>Opens a pack of the table (e.g. AZ1Pack) or an AZ2 pack; unknown names are skipped (see the "loot check" command).</summary>
        public static List<Drop> Open(string pack)
        {
            List<Drop> res = new List<Drop>();
            PackTable t;
            if (!Packs().TryGetValue(pack, out t) && !AZ2Packs.Contains(pack)) return res;
            foreach (string setName in new[] { RandomOf(CommonSets), RandomOf(CommonSets) })
            {
                CardTemplate c = Progression.RandomCard(SetByName(setName), Game.Shared.Mechanics.ERarity.Common, null);
                if (c != null) res.Add(new Drop { Card = c });
            }
            AddTo(res, t != null ? Pick(t, 'c', true) : AZ2Card());
            AddTo(res, t != null ? Pick(t, 'e', false) : AZ2Equipment());
            if (UnityEngine.Random.value < StardustChance) AddTo(res, Resolve(PickStardust(), false));
            else AddTo(res, t != null ? Pick(t, 'e', false) : AZ2Equipment());
            if (t != null && UnityEngine.Random.value < 0.01f) AddTo(res, Pick(t, 'u', false));
            return res;
        }

        /// <summary>One random equipment of a pack's list (rewards of "2 / 3 random equipment", Src.Video).</summary>
        public static Drop RandomEquipment(string pack)
        {
            PackTable t;
            if (Packs().TryGetValue(pack, out t)) return Pick(t, 'e', false);
            return AZ2Packs.Contains(pack) ? AZ2Equipment() : null;
        }

        private static void AddTo(List<Drop> res, Drop d) { if (d != null) res.Add(d); }

        private static Game.Shared.Mechanics.ERarity AZ2Rarity()
        {
            float v = UnityEngine.Random.value * 14f;
            return v < 10f ? Game.Shared.Mechanics.ERarity.Uncommon : v < 13f ? Game.Shared.Mechanics.ERarity.Rare : Game.Shared.Mechanics.ERarity.Legendary;
        }

        private static Drop AZ2Card()
        {
            CardTemplate c = Progression.RandomCard(SetByName("AZ2"), AZ2Rarity(), null);
            return c != null ? new Drop { Card = c } : null;
        }

        private static Dictionary<Game.Shared.Mechanics.ERarity, List<ResourceId>> s_AZ2Equipment;

        /// <summary>An equipment item of the AZ2 set (InventoryEquipmentData.m_SetId; the "AZ2 Equipment" card set only holds their pictures).</summary>
        private static Drop AZ2Equipment()
        {
            if (s_AZ2Equipment == null)
            {
                s_AZ2Equipment = new Dictionary<Game.Shared.Mechanics.ERarity, List<ResourceId>>();
                CardSetTemplate set = SetByName("AZ2");
                foreach (InventoryItemData item in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                {
                    InventoryEquipmentData eq = item as InventoryEquipmentData;
                    if (eq == null || set == null || eq.m_SetId != set.m_Id) continue;
                    List<ResourceId> l;
                    if (!s_AZ2Equipment.TryGetValue(eq.m_Rarity, out l)) s_AZ2Equipment[eq.m_Rarity] = l = new List<ResourceId>();
                    l.Add(eq.m_Id);
                }
            }
            List<ResourceId> pool;
            if (!s_AZ2Equipment.TryGetValue(AZ2Rarity(), out pool) || pool.Count == 0) return null;
            return new Drop { Item = pool[UnityEngine.Random.Range(0, pool.Count)] };
        }

        private static Drop Pick(PackTable t, char cat, bool card)
        {
            List<Entry> l;
            if (!t.Cat.TryGetValue(cat, out l) || l.Count == 0) return null;
            float total = 0;
            foreach (Entry e in l) total += e.Pct;
            return Resolve(Pick(l, total), card);
        }

        private static string RandomOf(string[] l) { return l[UnityEngine.Random.Range(0, l.Length)]; }

        private static string PickStardust()
        {
            float total = 0;
            foreach (KeyValuePair<string, float> kv in Stardusts) total += kv.Value;
            float v = UnityEngine.Random.value * total;
            foreach (KeyValuePair<string, float> kv in Stardusts) { v -= kv.Value; if (v <= 0) return kv.Key; }
            return Stardusts[0].Key;
        }

        private static CardSetTemplate SetByName(string name)
        {
            foreach (CardSetTemplate s in Singleton<TemplateManager>.Instance.CardSets.Values)
                if (s != null && s.m_Name == name) return s;
            return null;
        }

        private static string Pick(List<Entry> l, float total)
        {
            float v = UnityEngine.Random.value * total;
            foreach (Entry e in l) { v -= e.Pct; if (v <= 0) return e.Name; }
            return l[l.Count - 1].Name;
        }

        /// <summary>
        /// A card (preferably a campaign version, sets AZ1/AZ2) or an equipment item, by its exact
        /// game name (case ignored). The spreadsheet's misspellings are corrected in
        /// data/loot_name_fixes.csv when LootData.cs is generated, not here.
        /// </summary>
        public static Drop Resolve(string name, bool cardFirst)
        {
            Index();
            // "<name> (mercenary)": the mercenary item, not the card of the same name.
            const string MercSuffix = " (mercenary)";
            if (name.EndsWith(MercSuffix))
            {
                string merc = name.Substring(0, name.Length - MercSuffix.Length);
                foreach (InventoryItemData item in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                    if (item is InventoryMercenaryData && item.m_Name == merc) return new Drop { Item = item.m_Id };
                Debug.LogWarning("[HexRevival] [Loot] no mercenary " + merc);
                return null;
            }
            Drop d = new Drop();
            CardTemplate c;
            ResourceId it;
            bool hasCard = s_Cards.TryGetValue(name, out c), hasItem = s_Items.TryGetValue(name, out it);
            if (hasCard && (cardFirst || !hasItem)) { d.Card = c; return d; }
            if (hasItem) { d.Item = it; return d; }
            Debug.LogWarning("[HexRevival] [Loot] not found: " + name);
            return null;
        }

        private static Dictionary<string, CardTemplate> s_Cards;
        private static Dictionary<string, ResourceId> s_Items;

        private static void Index()
        {
            if (s_Cards != null) return;
            s_Cards = new Dictionary<string, CardTemplate>(StringComparer.OrdinalIgnoreCase);
            foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
            {
                if (t == null || string.IsNullOrEmpty(t.m_Name)) continue;
                bool own; try { own = t.IsOwnable(); } catch { own = false; }
                if (!own) continue;
                string k = t.m_Name.Trim();   // some game names end with spaces ("Camouflage Vest ")
                CardTemplate cur;
                if (!s_Cards.TryGetValue(k, out cur) || (!IsCampaignSet(cur) && IsCampaignSet(t))) s_Cards[k] = t;
            }
            s_Items = new Dictionary<string, ResourceId>(StringComparer.OrdinalIgnoreCase);
            foreach (InventoryItemData item in Singleton<TemplateManager>.Instance.InventoryItems.Values)
            {
                // equipment, stardust and deck sleeves (campaign rewards, Src.Video)
                string type = item != null ? item.GetType().Name : "";
                bool other = type == "InventoryStardust" || type == "InventoryDeckSleeve";
                if (!(item is InventoryEquipmentData) && !other || string.IsNullOrEmpty(item.m_Name)) continue;
                string k = item.m_Name.Trim();
                if (!s_Items.ContainsKey(k)) s_Items[k] = item.m_Id;
            }
        }

        private static bool IsCampaignSet(CardTemplate t)
        {
            CardSetTemplate s;
            return Singleton<TemplateManager>.Instance.CardSets.TryGetValue(t.m_SetId, out s) && s != null && (s.m_Name == "AZ1" || s.m_Name == "AZ2");
        }

        /// <summary>Dev command "loot eqsets": equipment items counted by their set and their equipment set.</summary>
        public static string EquipmentSets()
        {
            Dictionary<string, int> n = new Dictionary<string, int>();
            foreach (InventoryItemData item in Singleton<TemplateManager>.Instance.InventoryItems.Values)
            {
                InventoryEquipmentData eq = item as InventoryEquipmentData;
                if (eq == null) continue;
                CardSetTemplate s;
                string k = (Singleton<TemplateManager>.Instance.CardSets.TryGetValue(eq.m_SetId, out s) && s != null ? s.m_Name : eq.m_SetId.ToString()) + " / " + eq.m_EquipmentSet + " / " + eq.m_BuildTag;
                int c; n.TryGetValue(k, out c); n[k] = c + 1;
            }
            List<string> l = new List<string>();
            foreach (KeyValuePair<string, int> kv in n) l.Add(kv.Value + "  " + kv.Key);
            return string.Join("\n", l.ToArray());
        }

        /// <summary>Dev command "loot check": reward names that match no card or equipment of the game.</summary>
        public static string Check()
        {
            List<string> names = new List<string>();
            foreach (KeyValuePair<string, PackTable> p in Packs())
                foreach (KeyValuePair<char, List<Entry>> kv in p.Value.Cat)
                    foreach (Entry e in kv.Value) if (!names.Contains(e.Name)) names.Add(e.Name);
            foreach (string n in Progression.FixedRewardNames()) if (!names.Contains(n)) names.Add(n);
            List<string> missing = names.FindAll(n => Resolve(n, true) == null);
            return names.Count + " names, " + missing.Count + " not found" + (missing.Count > 0 ? ":\n" + string.Join("\n", missing.ToArray()) : "");
        }

        /// <summary>Dev command "loot names": writes every collectible card and equipment name (with set / type) to HexRevivalNames.txt.</summary>
        public static string DumpNames()
        {
            Index();
            List<string> lines = new List<string>();
            foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
            {
                if (t == null || string.IsNullOrEmpty(t.m_Name)) continue;
                bool own; try { own = t.IsOwnable(); } catch { own = false; }
                CardSetTemplate set;
                Singleton<TemplateManager>.Instance.CardSets.TryGetValue(t.m_SetId, out set);
                lines.Add("card\t" + t.m_Name + "\t" + (set != null ? set.m_Name : "?") + "\t" + t.m_CardRarity + "\t" + (own ? "own" : "-"));
            }
            foreach (InventoryItemData item in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                if (item is InventoryEquipmentData) lines.Add("item\t" + item.m_Name + "\t" + item.Rarity + "\t" + item.m_Id);
            System.IO.File.WriteAllLines(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "HexRevivalNames.txt"), lines.ToArray());
            return lines.Count + " names written to HexRevivalNames.txt";
        }

        /// <summary>Dev command "loot find text": card and equipment names containing text (letters only).</summary>
        public static string Find(string text)
        {
            Index();
            string k = text.ToLowerInvariant();
            List<string> o = new List<string>();
            foreach (KeyValuePair<string, CardTemplate> kv in s_Cards) if (kv.Key.ToLowerInvariant().Contains(k)) o.Add("card " + kv.Value.m_Name);
            foreach (KeyValuePair<string, ResourceId> kv in s_Items) if (kv.Key.ToLowerInvariant().Contains(k)) o.Add("item " + Singleton<TemplateManager>.Instance.GetInventoryItem(kv.Value).m_Name);
            return o.Count + " match(es)" + (o.Count > 0 ? ": " + string.Join(", ", o.ToArray()) : "");
        }
    }
}
