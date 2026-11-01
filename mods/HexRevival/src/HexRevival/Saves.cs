using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Local saves, which play the part of player accounts. Each save is a folder
    /// HexRevivalSaves/&lt;name&gt;/ holding account.txt (settings, gold), collection.txt (cards owned,
    /// progression mode only), decks.txt, champions.txt and campaign.txt.
    ///
    /// All saves work the same way (cards are earned by playing); a save created with "unlock
    /// everything" simply owns every card and every equipment from the start.
    /// </summary>
    public static class Saves
    {
        public class Info
        {
            public string Name;
            /// <summary>Folder outside the account list (quest simulation), null for accounts.</summary>
            public string Folder;
            public bool UnlockAll;
            public long Gold;
            public long Platinum;
            public DateTime LastPlayed;
            // Account level (see Progression).
            public int AccountLevel = 1;
            public int AccountXp;          // total account XP
            public int XpInPeriod;         // XP earned in the current daily period
            public DateTime PeriodStart;   // first win of the current daily period
            /// <summary>The game's tutorial is to be played at the next login (chosen when the account was created).</summary>
            public bool TutorialPending;
            public int CosmicCoins;        // Cosmic Coins (earned at the Cosmic tier of the ladders)
            /// <summary>
            /// What sold for platinum sells for gold (platinum cannot be earned offline, only by cheats or chosen at
            /// creation). Chosen when the account is created; always on for "unlock everything" accounts.
            /// </summary>
            public bool GoldPrices = true;
            /// <summary>
            /// What a platinum is worth in gold (Criesener, 2026-11-01: chosen per account, Store.DefaultGoldPerPlatinum by
            /// default): what sold for platinum (boosters, decks, entries) costs that many gold per platinum.
            /// </summary>
            public int GoldPerPlatinum = Store.DefaultGoldPerPlatinum;
            /// <summary>
            /// Every price that was gold in the original (stardust, Wheel of Fate spins) is divided by it
            /// (Criesener, 2026-11-01: chosen per account; 1 = the original prices).
            /// </summary>
            public int GoldDivisor = 1;
            /// <summary>
            /// Every gem in PvE decks (campaign, dungeons), not only the Standard and class gems the PvE format allows
            /// (Src.Game: GemHelper.IsGemLegalInFormat); the arena already allows every gem (mornox, 2026-10-30). Off by
            /// default, as in the original game.
            /// </summary>
            public bool AllGems;
            /// <summary>
            /// Breaking down and crafting (see Crafting): off by default, also for the accounts made before it existed
            /// (Criesener, 2026-10-31); never on "unlock everything" accounts.
            /// </summary>
            public bool Crafting;
            /// <summary>One-time rewards already received ("champion:reward" keys).</summary>
            public HashSet<string> Claims = new HashSet<string>();
        }

        public const long UnlockedGold = 100000;

        private static Info s_Current;

        /// <summary>The save in use (null before an account is chosen).</summary>
        public static Info Current { get { return s_Current; } }

        private static string RootDir { get { return Path.Combine(LocalServer.Root, "HexRevivalSaves"); } }

        /// <summary>The folder of a save.</summary>
        public static string FolderOf(Info i) { return i.Folder ?? Path.Combine(RootDir, i.Name); }

        /// <summary>Path of a file of the current save.</summary>
        public static string File(string name)
        {
            if (s_Current == null) throw new InvalidOperationException("no save selected");
            return Path.Combine(s_Current.Folder ?? Path.Combine(RootDir, s_Current.Name), name);
        }

        /// <summary>Replaces the save in use without logging in (quest simulation); returns the previous one.</summary>
        internal static Info Swap(Info save)
        {
            Info old = s_Current;
            s_Current = save;
            return old;
        }

        /// <summary>Saves, most recently played first.</summary>
        public static List<Info> List()
        {
            List<Info> res = new List<Info>();
            if (Directory.Exists(RootDir))
                foreach (string dir in Directory.GetDirectories(RootDir))
                {
                    Info i = Read(Path.GetFileName(dir));
                    if (i != null) res.Add(i);
                }
            res.Sort((a, b) => b.LastPlayed.CompareTo(a.LastPlayed));
            return res;
        }

        /// <summary>Why a name cannot be used (null if it can).</summary>
        public static string CheckName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Trim().Length == 0) return "account_name_empty";
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Trim() != name || name.Length > 32) return "account_name_invalid";
            if (Directory.Exists(Path.Combine(RootDir, name))) return "account_name_taken";
            return null;
        }

        /// <summary>
        /// A new account. On progression accounts, the player chooses the gold and platinum to start with (0 by
        /// default), whether platinum prices are converted to gold, the gold a platinum is worth and the divisor of
        /// the gold prices (0: the defaults).
        /// </summary>
        public static Info Create(string name, bool unlockAll, bool tutorial = false, bool goldPrices = true, long gold = 0, long platinum = 0, bool allGems = false, bool crafting = false,
            int goldPerPlatinum = 0, int goldDivisor = 0)
        {
            Directory.CreateDirectory(Path.Combine(RootDir, name));
            Info i = new Info { Name = name, UnlockAll = unlockAll, LastPlayed = DateTime.Now, TutorialPending = tutorial };
            i.Gold = unlockAll ? UnlockedGold : Math.Max(0, gold);
            i.Platinum = unlockAll ? 0 : Math.Max(0, platinum);
            i.GoldPrices = unlockAll || goldPrices;
            if (!unlockAll && goldPerPlatinum > 0) i.GoldPerPlatinum = goldPerPlatinum;
            if (!unlockAll && goldDivisor > 0) i.GoldDivisor = goldDivisor;
            i.AllGems = allGems;
            i.Crafting = !unlockAll && crafting;
            Write(i);
            return i;
        }

        /// <summary>The save with this name, created (everything unlocked) if missing.</summary>
        public static Info FindOrCreate(string name)
        {
            foreach (Info i in List()) if (string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            return Create(CheckName(name) == null ? name : "Offline", true);
        }

        /// <summary>Selects the save used by the next login.</summary>
        public static void Select(Info save)
        {
            s_Current = save;
            save.LastPlayed = DateTime.Now;
            Write(save);
            Campaign.Reset();
            Debug.Log("[HexRevival] [Saves] account '" + save.Name + "'" + (save.UnlockAll ? " (unlock all)" : ""));
        }

        /// <summary>Saves the current account settings (gold...).</summary>
        public static void SaveCurrent()
        {
            if (s_Current != null) Write(s_Current);
        }

        // ------------------------------------------------------------------
        // The online profile's account
        // ------------------------------------------------------------------

        /// <summary>
        /// The account the online profile plays with: not one of the offline accounts (its own folder, outside the
        /// account list), everything unlocked. Decks outside the campaign are shared by every account anyway.
        /// </summary>
        private static string OnlineDir { get { return Path.Combine(LocalServer.Root, "HexRevivalOnline" + Instance.Suffix); } }

        public static Info Online
        {
            get
            {
                Info i = ReadFrom(OnlineDir, "account.txt") ?? new Info { UnlockAll = true, Gold = UnlockedGold, LastPlayed = DateTime.Now };
                i.Folder = OnlineDir;
                i.Name = string.IsNullOrEmpty(OnlineProfile.Current.Name) ? "Online" : OnlineProfile.Current.Name;
                i.UnlockAll = true;
                i.GoldPrices = true;
                i.GoldPerPlatinum = Store.DefaultGoldPerPlatinum;
                i.GoldDivisor = 1;
                return i;
            }
        }

        /// <summary>Logged in with the online profile: the solo features (cheats...) are hidden.</summary>
        public static bool IsOnline { get { return s_Current != null && s_Current.Folder == OnlineDir; } }

        private static Info Read(string name)
        {
            Info i = ReadFrom(Path.Combine(RootDir, name), "account.txt");
            if (i != null) i.Name = name;
            return i;
        }

        private static Info ReadFrom(string dir, string file)
        {
            string path = Path.Combine(dir, file);
            string name = Path.GetFileName(dir);
            if (!System.IO.File.Exists(path)) return null;
            Info i = new Info { Name = name };
            try
            {
                foreach (string line in System.IO.File.ReadAllLines(path))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                    if (k == "unlock") i.UnlockAll = v == "1";
                    else if (k == "gold") long.TryParse(v, out i.Gold);
                    else if (k == "platinum") long.TryParse(v, out i.Platinum);
                    else if (k == "lastplayed") { long t; if (long.TryParse(v, out t)) i.LastPlayed = new DateTime(t); }
                    else if (k == "level") int.TryParse(v, out i.AccountLevel);
                    else if (k == "xp") int.TryParse(v, out i.AccountXp);
                    else if (k == "xpperiod") int.TryParse(v, out i.XpInPeriod);
                    else if (k == "periodstart") { long t; if (long.TryParse(v, out t)) i.PeriodStart = new DateTime(t); }
                    else if (k == "tutorial") i.TutorialPending = v == "1";
                    else if (k == "cosmic") int.TryParse(v, out i.CosmicCoins);
                    else if (k == "goldprices") i.GoldPrices = v != "0";
                    else if (k == "goldperplat") { int n; if (int.TryParse(v, out n) && n > 0) i.GoldPerPlatinum = n; }
                    else if (k == "golddivisor") { int n; if (int.TryParse(v, out n) && n > 0) i.GoldDivisor = n; }
                    else if (k == "allgems") i.AllGems = v == "1";
                    else if (k == "crafting") i.Crafting = v == "1";
                    else if (k == "claims") i.Claims.UnionWith(v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                }
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Saves] read " + name + ": " + e.Message); return null; }
            return i;
        }

        private static void Write(Info i)
        {
            try
            {
                string dir = i.Folder ?? Path.Combine(RootDir, i.Name);
                Directory.CreateDirectory(dir);
                System.IO.File.WriteAllLines(Path.Combine(dir, "account.txt"), new[] {
                    "unlock=" + (i.UnlockAll ? "1" : "0"),
                    "gold=" + i.Gold,
                    "platinum=" + i.Platinum,
                    "lastplayed=" + i.LastPlayed.Ticks,
                    "level=" + i.AccountLevel,
                    "xp=" + i.AccountXp,
                    "xpperiod=" + i.XpInPeriod,
                    "periodstart=" + i.PeriodStart.Ticks,
                    "tutorial=" + (i.TutorialPending ? "1" : "0"),
                    "cosmic=" + i.CosmicCoins,
                    "goldprices=" + (i.GoldPrices ? "1" : "0"),
                    "goldperplat=" + i.GoldPerPlatinum,
                    "golddivisor=" + i.GoldDivisor,
                    "allgems=" + (i.AllGems ? "1" : "0"),
                    "crafting=" + (i.Crafting ? "1" : "0"),
                    "claims=" + string.Join(",", new List<string>(i.Claims).ToArray()) });
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Saves] write " + i.Name + ": " + e.Message); }
        }
    }
}
