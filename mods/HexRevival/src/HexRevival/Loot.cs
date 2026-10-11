using System;
using System.Collections.Generic;
using Game.Shared;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Campaign and dungeon pack contents, from the community "AZ1 Loot Tables" spreadsheet (2016,
    /// Src.LootTables2016; data/az1_pack_loot_tables.csv, generated into LootData.cs): for each pack,
    /// the cards, equipment and unique equipment seen, with their % drop chance over the packs opened
    /// (1162 Adventure Zone 1 packs).
    ///
    /// The per-category totals are round numbers (AZ1: cards 50%, equipment 200%, unique 1%), so a
    /// pack is modelled as that many expected drops per category: the whole part always drops, the
    /// rest is a chance (AZ1: 2 equipment, a card 1 time in 2). Each drop is picked weighted by its %.
    /// This reading of the percentages is an assumption (PLACEHOLDER): the sheet does not say.
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

        /// <summary>Opens a pack of the table (e.g. AZ1Pack); unknown names are skipped (see the "loot check" command).</summary>
        public static List<Drop> Open(string pack)
        {
            List<Drop> res = new List<Drop>();
            PackTable t;
            if (!Packs().TryGetValue(pack, out t)) return res;
            foreach (KeyValuePair<char, List<Entry>> kv in t.Cat)
            {
                float total = 0;
                foreach (Entry e in kv.Value) total += e.Pct;
                float expected = total / 100f;
                int n = (int)expected;
                if (UnityEngine.Random.value < expected - n) n++;
                for (int i = 0; i < n; i++)
                {
                    Drop d = Resolve(Pick(kv.Value, total), kv.Key == 'c');
                    if (d != null) res.Add(d);
                }
            }
            return res;
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
                if (!(item is InventoryEquipmentData) || string.IsNullOrEmpty(item.m_Name)) continue;
                string k = item.m_Name.Trim();
                if (!s_Items.ContainsKey(k)) s_Items[k] = item.m_Id;
            }
        }

        private static bool IsCampaignSet(CardTemplate t)
        {
            CardSetTemplate s;
            return Singleton<TemplateManager>.Instance.CardSets.TryGetValue(t.m_SetId, out s) && s != null && (s.m_Name == "AZ1" || s.m_Name == "AZ2");
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
