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
        /// A card (preferably a campaign version, sets AZ1/AZ2) or an equipment item, by name.
        /// Exact names first (case, spaces and punctuation ignored), then the closest name when
        /// the spreadsheet misspells it (see Closest).
        /// </summary>
        public static Drop Resolve(string name, bool cardFirst)
        {
            Index();
            string key = Key(name);
            Drop d = new Drop();
            CardTemplate c;
            ResourceId it;
            bool hasCard = s_Cards.TryGetValue(key, out c), hasItem = s_Items.TryGetValue(key, out it);
            if (hasCard && (cardFirst || !hasItem)) { d.Card = c; return d; }
            if (hasItem) { d.Item = it; return d; }
            string ck = Closest(key, s_Cards.Keys), ik = Closest(key, s_Items.Keys);
            int cd = ck != null ? Distance(key, ck) : int.MaxValue, id = ik != null ? Distance(key, ik) : int.MaxValue;
            if (ck != null && (cd < id || (cd == id && cardFirst))) { d.Card = s_Cards[ck]; Inferred(name, d.Card.m_Name); return d; }
            if (ik != null) { d.Item = s_Items[ik]; Inferred(name, Singleton<TemplateManager>.Instance.GetInventoryItem(d.Item).m_Name); return d; }
            Debug.LogWarning("[HexRevival] [Loot] not found: " + name);
            return null;
        }

        private static Dictionary<string, CardTemplate> s_Cards;
        private static Dictionary<string, ResourceId> s_Items;
        private static readonly Dictionary<string, string> s_Inferred = new Dictionary<string, string>();

        private static void Inferred(string from, string to)
        {
            if (s_Inferred.ContainsKey(from)) return;
            s_Inferred[from] = to;
            Debug.Log("[HexRevival] [Loot] '" + from + "' read as '" + to + "'");
        }

        /// <summary>Lowercase letters and digits only: "Rotting away" = "Rotting Away", "LZ-37" = "LZ 37".</summary>
        private static string Key(string name)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char ch in name.ToLowerInvariant()) if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            return sb.ToString();
        }

        private static void Index()
        {
            if (s_Cards != null) return;
            s_Cards = new Dictionary<string, CardTemplate>();
            foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
            {
                if (t == null || string.IsNullOrEmpty(t.m_Name)) continue;
                bool own; try { own = t.IsOwnable(); } catch { own = false; }
                if (!own) continue;
                string k = Key(t.m_Name);
                CardTemplate cur;
                if (!s_Cards.TryGetValue(k, out cur) || (!IsCampaignSet(cur) && IsCampaignSet(t))) s_Cards[k] = t;
            }
            s_Items = new Dictionary<string, ResourceId>();
            foreach (InventoryItemData item in Singleton<TemplateManager>.Instance.InventoryItems.Values)
            {
                if (!(item is InventoryEquipmentData) || string.IsNullOrEmpty(item.m_Name)) continue;
                string k = Key(item.m_Name);
                if (!s_Items.ContainsKey(k)) s_Items[k] = item.m_Id;
            }
        }

        private static bool IsCampaignSet(CardTemplate t)
        {
            CardSetTemplate s;
            return Singleton<TemplateManager>.Instance.CardSets.TryGetValue(t.m_SetId, out s) && s != null && (s.m_Name == "AZ1" || s.m_Name == "AZ2");
        }

        /// <summary>
        /// Closest known name for a misspelt one: at most 1 edit per 5 letters (2 for a 10-letter
        /// name), and only if no other name is as close (otherwise the guess is ambiguous: null).
        /// </summary>
        private static string Closest(string key, IEnumerable<string> keys)
        {
            int max = Math.Max(1, key.Length / 5), best = int.MaxValue;
            string res = null;
            bool tie = false;
            foreach (string k in keys)
            {
                if (Math.Abs(k.Length - key.Length) > max) continue;
                int dist = Distance(key, k);
                if (dist < best) { best = dist; res = k; tie = false; }
                else if (dist == best) tie = true;
            }
            return best <= max && !tie ? res : null;
        }

        /// <summary>Levenshtein distance.</summary>
        private static int Distance(string a, string b)
        {
            int[] prev = new int[b.Length + 1], cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= b.Length; j++)
                    cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                int[] t = prev; prev = cur; cur = t;
            }
            return prev[b.Length];
        }

        /// <summary>Dev command "loot check": names that match nothing, and names read as another (misspellings).</summary>
        public static string Check()
        {
            List<string> names = new List<string>();
            foreach (KeyValuePair<string, PackTable> p in Packs())
                foreach (KeyValuePair<char, List<Entry>> kv in p.Value.Cat)
                    foreach (Entry e in kv.Value) names.Add(e.Name);
            names.AddRange(Progression.FixedRewardNames());
            List<string> missing = new List<string>();
            foreach (string n in names) if (Resolve(n, true) == null) missing.Add(n);
            List<string> guessed = new List<string>();
            foreach (KeyValuePair<string, string> kv in s_Inferred) guessed.Add(kv.Key + " -> " + kv.Value);
            return names.Count + " names, " + missing.Count + " not found, " + guessed.Count + " inferred"
                + (missing.Count > 0 ? "\nNOT FOUND:\n" + string.Join("\n", missing.ToArray()) : "")
                + (guessed.Count > 0 ? "\nINFERRED:\n" + string.Join("\n", guessed.ToArray()) : "");
        }

        /// <summary>Dev command "loot find text": card and equipment names containing text (letters only).</summary>
        public static string Find(string text)
        {
            Index();
            string k = Key(text);
            List<string> o = new List<string>();
            foreach (KeyValuePair<string, CardTemplate> kv in s_Cards) if (kv.Key.Contains(k)) o.Add("card " + kv.Value.m_Name);
            foreach (KeyValuePair<string, ResourceId> kv in s_Items) if (kv.Key.Contains(k)) o.Add("item " + Singleton<TemplateManager>.Instance.GetInventoryItem(kv.Value).m_Name);
            return o.Count + " match(es)" + (o.Count > 0 ? ": " + string.Join(", ", o.ToArray()) : "");
        }
    }
}
