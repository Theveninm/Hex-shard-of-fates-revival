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
    /// The notes say WHICH items a source held, never how often: every weight and count drawn from these pools is a
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
        // Set chests (account level rewards)
        // ------------------------------------------------------------------

        private static readonly string[] PvPSets =
            { "Shards of Fate", "Shattered Destiny", "Armies of Myth", "Primal Dawn", "Herofall", "Scars of War", "Frostheart", "Dead of Winter", "Doombringer" };

        /// <summary>The set's number (1-9) for a PvP set, 0 otherwise.</summary>
        /// <summary>An equipment of a set's Wheel of Fate (Src.Zymmer list; which one: PLACEHOLDER, any, evenly).</summary>
        public static Loot.Drop WheelEquipment(string source)
        {
            List<InventoryItemData> l = Equipment(source);
            return l.Count > 0 ? new Loot.Drop { Item = l[UnityEngine.Random.Range(0, l.Count)].m_Id } : null;
        }

        public static int SetNumber(CardSetTemplate set)
        {
            return set == null ? 0 : Array.IndexOf(PvPSets, set.m_Name) + 1;
        }

        /// <summary>
        /// A chest of a set opened: what it holds comes from the notes' "Set N Chest Loot" list (Src.Zymmer: the set's
        /// equipment, its PvE promo cards and its three chest mercenaries). How many items and which kind are PLACEHOLDER:
        /// 1 item (2 for a legendary chest); a mercenary 5% of the time from a rare or better chest (only one not owned),
        /// a PvE card 15% of the time, otherwise an equipment whose rarity follows the chest's (common chest: common 70% /
        /// uncommon 30%; uncommon: uncommon 70 / rare 30; rare: rare 75 / legendary 25; legendary: legendary).
        /// Booster chests can first be spun on the Wheel of Fate (see Packs).
        /// </summary>
        public static List<Loot.Drop> OpenChest(int setNumber, ERarity chest)
        {
            List<Loot.Drop> res = new List<Loot.Drop>();
            string source = "Set " + setNumber + " Chest Loot";
            if (Source(source).Count == 0) return res;
            int n = chest >= ERarity.Legendary ? 2 : 1;
            for (int k = 0; k < n; k++)
            {
                float x = UnityEngine.Random.value;
                Loot.Drop d = null;
                if (chest >= ERarity.Rare && x < 0.05f) d = NewMercenary(source);
                if (d == null && x < 0.20f) d = RandomCard(source);
                if (d == null) d = ChestEquipment(source, chest, res);
                if (d != null) res.Add(d);
            }
            return res;
        }

        private static Loot.Drop ChestEquipment(string source, ERarity chest, List<Loot.Drop> already)
        {
            float v = UnityEngine.Random.value;
            ERarity want;
            switch (chest)
            {
                case ERarity.Common: want = v < 0.7f ? ERarity.Common : ERarity.Uncommon; break;
                case ERarity.Uncommon: want = v < 0.7f ? ERarity.Uncommon : ERarity.Rare; break;
                case ERarity.Rare: want = v < 0.75f ? ERarity.Rare : ERarity.Legendary; break;
                default: want = ERarity.Legendary; break;
            }
            // Not twice the same piece in one chest.
            List<InventoryItemData> pool = Equipment(source).FindAll(i => !already.Exists(d => d.Card == null && d.Item == i.m_Id));
            List<InventoryItemData> of = pool.FindAll(i => RarityOf(i) == want);
            if (of.Count == 0) of = pool;
            return of.Count > 0 ? new Loot.Drop { Item = of[UnityEngine.Random.Range(0, of.Count)].m_Id } : null;
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
        /// drawn (not granted); "zymmer kismet Kismet's Vampire Pack" = a pack's theme; "zymmer pack Ruins of Kukatan" = a pack.
        /// </summary>
        public static string Command(string[] parts)
        {
            if (parts.Length > 3 && parts[1] == "chest")
            {
                int n; int.TryParse(parts[2], out n);
                ERarity r = (ERarity)Enum.Parse(typeof(ERarity), parts[3], true);
                return string.Join(", ", OpenChest(n, r).ConvertAll(Describe).ToArray());
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
