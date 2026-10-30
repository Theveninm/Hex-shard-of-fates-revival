using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Decks played by real players in HEX constructed tournaments (Src.HexCodex: its tournament database, era "Sets
    /// 9+", 2018-2020), bundled with the mod (assets/tournament_decks.txt, built by tools/gen_tournament_decks.py).
    /// They are the default AI opponents of the modes whose original opponents were players: the ladder (weaker
    /// players' decks in the low tiers, the strongest players' in the high ones), the constructed gauntlet and Siege.
    /// </summary>
    public static class TournamentDecks
    {
        public class Deck
        {
            public string Key, Champion, Shards, Record, Date, Series;
            /// <summary>"count name" lines (Opponents.Opponent.Cards).</summary>
            public List<string> Cards = new List<string>();
            /// <summary>Name shown for the deck: its champion, shards and tournament record.</summary>
            public string Name { get { return Champion + " (" + Shards + ", " + Record + ")"; } }
            public string Detail { get { return Date + " - " + Series; } }
        }

        private static Dictionary<string, Deck> s_Decks;
        private static List<Deck> s_List;
        private static Dictionary<string, List<KeyValuePair<string, int>>> s_Tiers;

        private static void Load()
        {
            if (s_Decks != null) return;
            s_Decks = new Dictionary<string, Deck>();
            s_List = new List<Deck>();
            s_Tiers = new Dictionary<string, List<KeyValuePair<string, int>>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (Stream st = typeof(TournamentDecks).Assembly.GetManifestResourceStream("HexRevival.tournament_decks.txt"))
                using (StreamReader r = new StreamReader(st))
                {
                    Deck cur = null;
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        if (line.Length == 0 || line[0] == '#') continue;
                        if (line.StartsWith("tier "))
                        {
                            string[] p = line.Split(' ');
                            List<KeyValuePair<string, int>> refs = new List<KeyValuePair<string, int>>();
                            for (int i = 2; i < p.Length; i++)
                            {
                                int c = p[i].LastIndexOf(':'), w;
                                if (c > 0 && int.TryParse(p[i].Substring(c + 1), out w)) refs.Add(new KeyValuePair<string, int>(p[i].Substring(0, c), w));
                            }
                            s_Tiers[p[1]] = refs;
                        }
                        else if (line.StartsWith("deck "))
                        {
                            string[] p = line.Substring(5).Split('|');
                            if (p.Length < 6) { cur = null; continue; }
                            cur = new Deck { Key = p[0], Champion = p[1], Shards = p[2], Record = p[3], Date = p[4], Series = p[5] };
                            s_Decks[cur.Key] = cur;
                            s_List.Add(cur);
                        }
                        else if (cur != null) cur.Cards.Add(line);
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [TournamentDecks] " + e.Message); }
            Debug.Log("[HexRevival] " + s_List.Count + " tournament decks");
        }

        public static Deck Get(string key)
        {
            Load();
            Deck d;
            return key != null && s_Decks.TryGetValue(key, out d) ? d : null;
        }

        /// <summary>Every bundled deck, in the file's order (by champion).</summary>
        public static List<Deck> All() { Load(); return s_List; }

        /// <summary>A tier's decks and frequencies ("Bronze" ... "Cosmic", "Gauntlet"); empty if unknown.</summary>
        public static List<KeyValuePair<string, int>> Tier(string name)
        {
            Load();
            List<KeyValuePair<string, int>> t;
            return s_Tiers.TryGetValue(name, out t) ? t : new List<KeyValuePair<string, int>>();
        }

        /// <summary>The opponent playing a bundled deck (null if unknown).</summary>
        public static Opponents.Opponent Opponent(string key)
        {
            Deck d = Get(key);
            return d == null ? null : new Opponents.Opponent { Name = d.Name, Champion = d.Champion, Cards = new List<string>(d.Cards) };
        }

        /// <summary>Dev: "tdecks [check]": how many decks, and (check) the cards and champions the game does not know.</summary>
        public static string Command(string[] parts)
        {
            Load();
            if (parts.Length < 2 || parts[1] != "check") return s_List.Count + " decks, tiers: " + string.Join(", ", new List<string>(s_Tiers.Keys).ToArray());
            HashSet<string> cards = new HashSet<string>(), champions = new HashSet<string>();
            foreach (Deck d in s_List)
            {
                if (!Opponents.IsChampion(d.Champion)) champions.Add(d.Champion);
                foreach (string l in d.Cards)
                {
                    int sp = l.IndexOf(' ');
                    string n = sp > 0 ? l.Substring(sp + 1).Trim() : l.Trim();
                    if (Opponents.CardByName(n) == null) cards.Add(n);
                }
            }
            return s_List.Count + " decks; unknown champions " + champions.Count + ": " + string.Join(", ", new List<string>(champions).ToArray())
                + "; unknown cards " + cards.Count + ": " + string.Join(", ", new List<string>(cards).ToArray());
        }
    }
}
