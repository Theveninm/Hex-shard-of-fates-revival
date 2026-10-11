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

        /// <summary>A card (preferably a campaign version, sets AZ1/AZ2) or an equipment item, by name.</summary>
        public static Drop Resolve(string name, bool cardFirst)
        {
            Drop d = new Drop();
            if (cardFirst) { d.Card = FindCard(name); if (d.Card != null) return d; }
            d.Item = FindItem(name);
            if (d.Item.IsValid) return d;
            if (!cardFirst) { d.Card = FindCard(name); if (d.Card != null) return d; }
            Debug.LogWarning("[HexRevival] [Loot] not found: " + name);
            return null;
        }

        private static Dictionary<string, CardTemplate> s_Cards;
        private static Dictionary<string, ResourceId> s_Items;

        private static CardTemplate FindCard(string name)
        {
            if (s_Cards == null)
            {
                s_Cards = new Dictionary<string, CardTemplate>(StringComparer.OrdinalIgnoreCase);
                foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
                {
                    if (t == null || string.IsNullOrEmpty(t.m_Name)) continue;
                    bool own; try { own = t.IsOwnable(); } catch { own = false; }
                    if (!own) continue;
                    CardTemplate cur;
                    if (!s_Cards.TryGetValue(t.m_Name, out cur) || (!IsCampaignSet(cur) && IsCampaignSet(t))) s_Cards[t.m_Name] = t;
                }
            }
            CardTemplate r;
            return s_Cards.TryGetValue(Normalize(name), out r) || s_Cards.TryGetValue(name, out r) ? r : null;
        }

        private static bool IsCampaignSet(CardTemplate t)
        {
            CardSetTemplate s;
            return Singleton<TemplateManager>.Instance.CardSets.TryGetValue(t.m_SetId, out s) && s != null && (s.m_Name == "AZ1" || s.m_Name == "AZ2");
        }

        private static ResourceId FindItem(string name)
        {
            if (s_Items == null)
            {
                s_Items = new Dictionary<string, ResourceId>(StringComparer.OrdinalIgnoreCase);
                foreach (InventoryItemData item in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                    if (item is InventoryEquipmentData && !string.IsNullOrEmpty(item.m_Name) && !s_Items.ContainsKey(item.m_Name)) s_Items[item.m_Name] = item.m_Id;
            }
            ResourceId r;
            return s_Items.TryGetValue(Normalize(name), out r) || s_Items.TryGetValue(name, out r) ? r : ResourceId.Invalid;
        }

        /// <summary>Spelling fixes for names in the spreadsheet.</summary>
        private static string Normalize(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "lightening medallion": return "Lightning Medallion";
                case "etheral healer": return "Ethereal Healer";
                default: return name;
            }
        }

        /// <summary>Dev command "loot check": names of the tables that match no card or item.</summary>
        public static string Check()
        {
            List<string> missing = new List<string>();
            int n = 0;
            foreach (KeyValuePair<string, PackTable> p in Packs())
                foreach (KeyValuePair<char, List<Entry>> kv in p.Value.Cat)
                    foreach (Entry e in kv.Value)
                    {
                        n++;
                        if (FindCard(e.Name) == null && !FindItem(e.Name).IsValid) missing.Add(p.Key + ": " + e.Name);
                    }
            foreach (string name in Progression.FixedRewardNames())
            {
                n++;
                if (FindCard(name) == null && !FindItem(name).IsValid) missing.Add("fixed: " + name);
            }
            return n + " names, " + missing.Count + " not found" + (missing.Count > 0 ? ":\n" + string.Join("\n", missing.ToArray()) : "");
        }
    }
}
