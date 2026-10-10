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
    /// A save is created with two options: "cheats available" (in-game cheat menu) and
    /// "unlock everything" (full collection, as in Quick Match before saves existed). Without the
    /// latter the save is in progression mode: cards are earned by playing.
    /// </summary>
    public static class Saves
    {
        public class Info
        {
            public string Name;
            public bool Cheats;
            public bool UnlockAll;
            public long Gold;
            public DateTime LastPlayed;
        }

        public const long UnlockedGold = 100000;
        public const long StartingGold = 500;

        private static Info s_Current;

        /// <summary>The save in use (null before an account is chosen).</summary>
        public static Info Current { get { return s_Current; } }

        private static string RootDir { get { return Path.Combine(LocalServer.Root, "HexRevivalSaves"); } }

        /// <summary>Path of a file of the current save.</summary>
        public static string File(string name)
        {
            if (s_Current == null) throw new InvalidOperationException("no save selected");
            return Path.Combine(Path.Combine(RootDir, s_Current.Name), name);
        }

        /// <summary>Saves, most recently played first.</summary>
        public static List<Info> List()
        {
            MigrateLegacyFiles();
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

        public static Info Create(string name, bool cheats, bool unlockAll)
        {
            Directory.CreateDirectory(Path.Combine(RootDir, name));
            Info i = new Info { Name = name, Cheats = cheats, UnlockAll = unlockAll, Gold = unlockAll ? UnlockedGold : StartingGold, LastPlayed = DateTime.Now };
            Write(i);
            return i;
        }

        /// <summary>The save with this name, created (everything unlocked, cheats) if missing.</summary>
        public static Info FindOrCreate(string name)
        {
            foreach (Info i in List()) if (string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            return Create(CheckName(name) == null ? name : "Offline", true, true);
        }

        /// <summary>Selects the save used by the next login.</summary>
        public static void Select(Info save)
        {
            s_Current = save;
            save.LastPlayed = DateTime.Now;
            Write(save);
            Campaign.Reset();
            Debug.Log("[HexRevival] [Saves] account '" + save.Name + "'" + (save.UnlockAll ? " (unlock all)" : "") + (save.Cheats ? " (cheats)" : ""));
        }

        /// <summary>Saves the current account settings (gold...).</summary>
        public static void SaveCurrent()
        {
            if (s_Current != null) Write(s_Current);
        }

        private static Info Read(string name)
        {
            string path = Path.Combine(Path.Combine(RootDir, name), "account.txt");
            if (!System.IO.File.Exists(path)) return null;
            Info i = new Info { Name = name };
            try
            {
                foreach (string line in System.IO.File.ReadAllLines(path))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                    if (k == "cheats") i.Cheats = v == "1";
                    else if (k == "unlock") i.UnlockAll = v == "1";
                    else if (k == "gold") long.TryParse(v, out i.Gold);
                    else if (k == "lastplayed") { long t; if (long.TryParse(v, out t)) i.LastPlayed = new DateTime(t); }
                }
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Saves] read " + name + ": " + e.Message); return null; }
            return i;
        }

        private static void Write(Info i)
        {
            try
            {
                string dir = Path.Combine(RootDir, i.Name);
                Directory.CreateDirectory(dir);
                System.IO.File.WriteAllLines(Path.Combine(dir, "account.txt"), new[] {
                    "cheats=" + (i.Cheats ? "1" : "0"),
                    "unlock=" + (i.UnlockAll ? "1" : "0"),
                    "gold=" + i.Gold,
                    "lastplayed=" + i.LastPlayed.Ticks });
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Saves] write " + i.Name + ": " + e.Message); }
        }

        /// <summary>
        /// Before saves existed, decks, champions and campaign were stored next to Hex.exe:
        /// they become a first save, "Offline", with everything unlocked and cheats available.
        /// </summary>
        private static void MigrateLegacyFiles()
        {
            if (Directory.Exists(RootDir)) return;
            string root = LocalServer.Root;
            string[,] files = { { "HexRevival.decks.txt", "decks.txt" }, { "HexRevival.champions.txt", "champions.txt" }, { "HexRevival.campaign.txt", "campaign.txt" } };
            bool any = false;
            for (int k = 0; k < files.GetLength(0); k++) any |= System.IO.File.Exists(Path.Combine(root, files[k, 0]));
            if (!any) return;
            Info i = Create("Offline", true, true);
            string dir = Path.Combine(RootDir, i.Name);
            for (int k = 0; k < files.GetLength(0); k++)
            {
                string src = Path.Combine(root, files[k, 0]);
                try { if (System.IO.File.Exists(src)) System.IO.File.Move(src, Path.Combine(dir, files[k, 1])); }
                catch (Exception e) { Debug.LogError("[HexRevival] [Saves] migrate " + files[k, 0] + ": " + e.Message); }
            }
            Debug.Log("[HexRevival] [Saves] previous decks and champions moved to the account 'Offline'");
        }
    }
}
