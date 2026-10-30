using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Game.Client;
using Game.Shared;
using Game.Shared.Domain;
using Game.Shared.Utils;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// The game's 425 deck sleeves and how each was obtained (Src.HexCodex: its sleeve list, bundled as
    /// assets/sleeve_sources.txt by tools/gen_sleeves.py; the game's own sleeve items carry only their names). The mod
    /// gives them where the original did and the mod has the same thing:
    /// - "Found in the Wheels of Fate for [set]": the Wheel of Fate's sleeves outcome (Packs);
    /// - "Found in Chests for [set]": sometimes in an opened chest of that set (PLACEHOLDER chance, Packs);
    /// - "Obtained by opening [set] Booster Packs": sometimes in an opened booster (PLACEHOLDER chance, Packs);
    /// - "Obtained in the [deck] Collector Deck": with that deck bought in the store (Store);
    /// - "Awarded through Account Leveling": the account level rewards "sleeve" (Progression; which one: PLACEHOLDER);
    /// - "Awarded by obtaining the [tier] Rank in the [year] Season [n] Ladder": the ladder's season rewards (Ladder);
    /// - "Awarded for getting 3 wins in Kismet's Draft events": a draft gauntlet run with 3 wins (Battlegrounds);
    /// - "Obtained through Gauntlet Tournaments or Steam": a constructed gauntlet run with 3 wins (PLACEHOLDER: the
    ///   number of wins, Battlegrounds);
    /// - "Awarded during the [year] [holiday] Holiday": playing during that holiday (Holidays, dates PLACEHOLDER), with one
    ///   of the holiday's equipment pieces;
    /// The others (Kickstarter, conventions, tournaments, VIP, staff...) cannot be earned offline. An account with
    /// everything unlocked owns every sleeve (Inventory).
    /// </summary>
    public static class Sleeves
    {
        private static Dictionary<string, string> s_Reasons;
        private static Dictionary<string, InventoryDeckSleeve> s_ByName;

        private static void Load()
        {
            if (s_Reasons != null) return;
            s_Reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            s_ByName = new Dictionary<string, InventoryDeckSleeve>(StringComparer.OrdinalIgnoreCase);
            foreach (InventoryItemData i in Singleton<TemplateManager>.Instance.InventoryItems.Values)
            {
                InventoryDeckSleeve s = i as InventoryDeckSleeve;
                if (s != null && !string.IsNullOrEmpty(s.m_Name) && HasPicture(s) && !s_ByName.ContainsKey(s.m_Name.Trim())) s_ByName[s.m_Name.Trim()] = s;
            }
            try
            {
                using (Stream st = typeof(Sleeves).Assembly.GetManifestResourceStream("HexRevival.sleeve_sources.txt"))
                using (StreamReader r = new StreamReader(st))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        if (line.Length == 0 || line[0] == '#') continue;
                        int tab = line.IndexOf('\t');
                        if (tab > 0) s_Reasons[line.Substring(0, tab).Trim()] = line.Substring(tab + 1).Trim();
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Sleeves] " + e.Message); }
        }

        private static HashSet<string> s_Pictures;

        /// <summary>
        /// Whether the game has the sleeve's picture: its texture in the sleeve archive (AssetBundles/general/
        /// DeckSleeves.harc, as "&lt;texture&gt;_s", "&lt;texture&gt;s" or "&lt;texture&gt;"; Src.Game). 18 sleeves have none
        /// (no texture, or none in the archive: Bounty of the Magus, the 2021 Season 6 ladder sleeves, the Courage, Dark,
        /// Dirge... deck sleeves): they showed blank (mornox, 2026-10-30), so they are never given.
        /// </summary>
        public static bool HasPicture(InventoryItemData sleeve)
        {
            if (sleeve == null) return false;
            if (s_Pictures == null)
            {
                s_Pictures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string path = Path.Combine(Path.Combine(Path.Combine(Application.dataPath, ".."), "AssetBundles"), "general/DeckSleeves.harc");
                    List<Harc.IndexItem> items = new List<Harc.IndexItem>();
                    Harc.Location loc;
                    if (new Harc(new Harc.FileSource(path)).ReadIndex(items, out loc) == Harc.ErrorCode.Ok)
                        foreach (Harc.IndexItem it in items) s_Pictures.Add(Path.GetFileNameWithoutExtension(it.Name));
                }
                catch (Exception e) { Debug.LogWarning("[HexRevival] [Sleeves] archive: " + e.Message); }
            }
            if (s_Pictures.Count == 0) return true;   // archive not readable: no sleeve left out
            string tex = FolderUtils.GetFileNameWithoutPathOrExtension(sleeve.m_TexturePath ?? "");
            return tex.Length > 0 && (s_Pictures.Contains(tex + "_s") || s_Pictures.Contains(tex + "s") || s_Pictures.Contains(tex));
        }

        /// <summary>Every sleeve of the game that has a picture (for the "everything unlocked" accounts).</summary>
        public static List<ResourceId> All()
        {
            Load();
            List<ResourceId> res = new List<ResourceId>();
            foreach (InventoryDeckSleeve s in s_ByName.Values) res.Add(s.m_Id);
            return res;
        }

        /// <summary>The sleeves whose "how obtained" text matches the pattern (case-insensitive regex).</summary>
        public static List<InventoryDeckSleeve> Matching(string pattern)
        {
            Load();
            List<InventoryDeckSleeve> res = new List<InventoryDeckSleeve>();
            Regex re = new Regex(pattern, RegexOptions.IgnoreCase);
            foreach (KeyValuePair<string, string> kv in s_Reasons)
            {
                InventoryDeckSleeve s;
                if (re.IsMatch(kv.Value) && s_ByName.TryGetValue(kv.Key, out s)) res.Add(s);
            }
            res.Sort((a, b) => string.CompareOrdinal(a.m_Name, b.m_Name));
            return res;
        }

        public static bool Owned(ResourceId id) { return Saves.Current != null && (Saves.Current.UnlockAll || Inventory.Count(id) > 0); }

        /// <summary>A random sleeve of the list the account does not own yet (null: it has them all).</summary>
        public static InventoryDeckSleeve Unowned(List<InventoryDeckSleeve> list)
        {
            List<InventoryDeckSleeve> left = list.FindAll(s => !Owned(s.m_Id));
            return left.Count > 0 ? left[UnityEngine.Random.Range(0, left.Count)] : null;
        }

        /// <summary>A sleeve of a set's Wheel of Fate / chests / boosters ("for Dead of Winter"), not owned yet; null if none.</summary>
        public static InventoryDeckSleeve OfSet(string kind, string setName)
        {
            if (string.IsNullOrEmpty(setName)) return null;
            string esc = Regex.Escape(setName.Trim());
            string pattern = kind == "wheel" ? "^Found in the Wheels of Fate for " + esc + @"\.$"
                : kind == "chest" ? "^Found in Chests for " + esc + @"\.$"
                : "^Obtained by opening " + esc + " Booster Packs";
            return Unowned(Matching(pattern));
        }

        /// <summary>Gives a sleeve now (saved, and to the client), with a notice; returns its name (null if nothing).</summary>
        public static string Grant(InventoryDeckSleeve s, bool notice = true)
        {
            if (s == null || Owned(s.m_Id)) return null;
            inventory_bits b = Inventory.GrantSilently(s.m_Id);
            if (b != null) try { GameClient.Instance.PlayerProfile.AddInventoryItem(b); } catch (Exception e) { Debug.LogWarning("[HexRevival] [Sleeves] " + e.Message); }
            Debug.Log("[HexRevival] [Sleeves] earned " + s.m_Name);
            if (notice) Notices.Show(Loc.Get("sleeve_earned", s.m_Name.Trim()));
            return s.m_Name.Trim();
        }

        // ------------------------------------------------------------------
        // Holidays
        // ------------------------------------------------------------------

        /// <summary>
        /// The game's holidays and when the mod holds them (PLACEHOLDER dates: the sleeves name the holidays, not their
        /// dates; these follow the real-world holidays they echo): Ardor Day (Feb 7-21), The Sea Festival (Jul 15 - Aug 15),
        /// Haunted Harvest (Oct 15 - Nov 5), Feast of Abundance (Nov 15-30), Night of Bells and Questmas (Dec 10 - Jan 5).
        /// </summary>
        private static readonly string[][] Holidays =
        {
            new[] { "Ardor Day", "2-7", "2-21" },
            new[] { "The Sea Festival", "7-15", "8-15" },
            new[] { "Haunted Harvest", "10-15", "11-5" },
            new[] { "Feast of Abundance", "11-15", "11-30" },
            new[] { "Night of Bells", "12-10", "1-5" },
            new[] { "Questmas", "12-10", "1-5" },
        };

        private static bool InPeriod(DateTime now, string from, string to)
        {
            Func<string, int> md = x => { string[] p = x.Split('-'); return int.Parse(p[0]) * 100 + int.Parse(p[1]); };
            int d = now.Month * 100 + now.Day, a = md(from), b = md(to);
            return a <= b ? d >= a && d <= b : d >= a || d <= b;
        }

        /// <summary>The holidays being held now.</summary>
        public static List<string> HolidaysNow(DateTime now)
        {
            List<string> res = new List<string>();
            foreach (string[] h in Holidays) if (InPeriod(now, h[1], h[2])) res.Add(h[0]);
            return res;
        }

        /// <summary>
        /// At login and after each game: during a holiday, one of its sleeves (any year's) not owned yet, once per holiday
        /// and year (the original gave the year's sleeve to everyone who played then).
        /// </summary>
        public static void CheckHolidays()
        {
            try
            {
                if (Saves.Current == null || Saves.Current.UnlockAll) return;
                DateTime now = DateTime.Now;
                foreach (string h in HolidaysNow(now))
                {
                    int year = now.Month == 1 ? now.Year - 1 : now.Year;   // a holiday spanning New Year counts for the year it began
                    string claim = "holiday:" + h + ":" + year;
                    if (Saves.Current.Claims.Contains(claim)) continue;
                    InventoryDeckSleeve s = Unowned(Matching("^Awarded during the [0-9]{4} " + Regex.Escape(h) + " Holiday"));
                    Saves.Current.Claims.Add(claim);
                    if (s != null) Grant(s);
                    GrantHolidayEquipment(h);
                    Saves.SaveCurrent();
                }
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Sleeves] holidays: " + e.Message); }
        }

        /// <summary>
        /// The holiday's equipment as well: one piece not owned yet of its events ("Events - Haunted Harvest 2016"...,
        /// Src.Zymmer, Src.HexCodex), any year's.
        /// </summary>
        private static void GrantHolidayEquipment(string holiday)
        {
            InventoryItemData e = HolidayEquipment(holiday);
            if (e == null) return;
            inventory_bits b = Inventory.GrantSilently(e.m_Id);
            if (b == null) return;
            try { GameClient.Instance.PlayerProfile.AddInventoryItem(b); } catch (Exception x) { Debug.LogWarning("[HexRevival] [Sleeves] " + x.Message); }
            Notices.Show(Loc.Get("holiday_item", e.m_Name.Trim()));
        }

        /// <summary>A piece of the holiday's equipment not owned yet (null: none left).</summary>
        public static InventoryItemData HolidayEquipment(string holiday)
        {
            string name = holiday.StartsWith("The ") ? holiday.Substring(4) : holiday;   // "Sea Festival" in the equipment lists
            List<InventoryItemData> pool = new List<InventoryItemData>();
            for (int y = 2014; y <= 2020; y++)
                foreach (InventoryItemData i in Zymmer.Equipment("Events - " + name + " " + y))
                    if (Inventory.Count(i.m_Id) == 0 && !pool.Contains(i)) pool.Add(i);
            return pool.Count == 0 ? null : pool[UnityEngine.Random.Range(0, pool.Count)];
        }

        // ------------------------------------------------------------------
        // Ladder seasons
        // ------------------------------------------------------------------

        public static readonly string[] LadderTiers = { "Bronze", "Silver", "Gold", "Platinum", "Cosmic" };

        private static List<string> s_Seasons;

        /// <summary>
        /// The ladder's seasons in order, as the sleeves name them ("Season 1" ... "2021 Season 6"): the mod runs them one
        /// after the other, a season every two months (6 a year, as the sleeves of 2017-2021; PLACEHOLDER: their dates),
        /// from the account's first ranked game, and starts over after the last one.
        /// </summary>
        public static List<string> Seasons()
        {
            if (s_Seasons != null) return s_Seasons;
            s_Seasons = new List<string>();
            Regex re = new Regex(@"in the (?:([0-9]{4}) )?Season\s+([0-9]+) Ladder", RegexOptions.IgnoreCase);
            List<KeyValuePair<int, string>> keys = new List<KeyValuePair<int, string>>();
            HashSet<string> seen = new HashSet<string>();
            foreach (InventoryDeckSleeve s in Matching(@"Rank in the .*Season\s+[0-9]+ Ladder"))
            {
                Match m = re.Match(s_Reasons[s.m_Name.Trim()]);
                if (!m.Success) continue;
                int year = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 2015, n = int.Parse(m.Groups[2].Value);
                string key = (m.Groups[1].Success ? year + " " : "") + "Season " + n;
                if (seen.Add(key)) keys.Add(new KeyValuePair<int, string>(year * 100 + n, key));
            }
            keys.Sort((a, b) => a.Key.CompareTo(b.Key));
            foreach (KeyValuePair<int, string> k in keys) s_Seasons.Add(k.Value);
            return s_Seasons;
        }

        /// <summary>A season's sleeve for a tier (null if the game has none).</summary>
        public static InventoryDeckSleeve SeasonSleeve(string season, string tier)
        {
            List<InventoryDeckSleeve> l = Matching("^Awarded by obtaining the " + tier + @"( 5)? Rank in the " + Regex.Escape(season).Replace(@"\ ", @"\s+") + " Ladder");
            return l.Count > 0 ? l[0] : null;
        }

        /// <summary>The current season (index in Seasons) for an account that started the ladder at that date.</summary>
        public static int SeasonIndex(DateTime start, DateTime now)
        {
            int n = Seasons().Count;
            if (n == 0) return -1;
            int months = (now.Year - start.Year) * 12 + now.Month - start.Month;
            return Math.Max(0, months / 2) % n;
        }

        // ------------------------------------------------------------------

        /// <summary>Dev: "sleeves [state|holiday|season|give &lt;pattern&gt;]".</summary>
        public static string Command(string[] parts)
        {
            Load();
            string sub = parts.Length > 1 ? parts[1] : "state";
            if (sub == "holiday") { CheckHolidays(); return "holidays now: " + string.Join(", ", HolidaysNow(DateTime.Now).ToArray()); }
            if (sub == "season") return Seasons().Count + " seasons: " + string.Join(", ", Seasons().ToArray());
            if (sub == "give" && parts.Length > 2) return Grant(Unowned(Matching(string.Join(" ", parts, 2, parts.Length - 2)))) ?? "none left";
            int owned = 0;
            foreach (InventoryDeckSleeve s in s_ByName.Values) if (Owned(s.m_Id)) owned++;
            return s_ByName.Count + " sleeves in the game, " + s_Reasons.Count + " with a source, " + owned + " owned; seasons " + Seasons().Count;
        }

        /// <summary>
        /// Dev "sleevecheck": the sleeves without a picture (see HasPicture).
        /// </summary>
        public static string DevCheck()
        {
            List<string> missing = new List<string>();
            int all = 0;
            foreach (InventoryItemData i in Singleton<TemplateManager>.Instance.InventoryItems.Values)
            {
                if (!(i is InventoryDeckSleeve)) continue;
                all++;
                if (!HasPicture(i)) missing.Add(i.m_Name.Trim() + " [" + (string.IsNullOrEmpty(i.m_TexturePath) ? "no texture" : FolderUtils.GetFileNameWithoutPathOrExtension(i.m_TexturePath)) + "]");
            }
            return all + " sleeves, " + missing.Count + " without a picture (never given):\n" + string.Join("\n", missing.ToArray());
        }
    }
}
