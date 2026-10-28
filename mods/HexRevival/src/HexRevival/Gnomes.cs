using System;
using System.Collections.Generic;
using Game.Shared;
using Game.Shared.Mechanics;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// "Gnomes Come Home" (AZ1 desert, Herbert's lost Cartographers), as the campaign server ran it:
    /// - Src.Video (Vandersveldt parts 41-54, MyHotStream Underworld battles 46-48): at the North Desert, after Herbert's
    ///   dialogue, a "Select Number of Gnomes" window ("Add any number of Lost Gnomes to your deck to help them cross the
    ///   desert. For every 3 Lost Gnomes you add to your deck, you will also automatically add 2 Primal Shards. If you add 0
    ///   Lost Gnomes to your deck, you can cross the desert without being attacked."), the client's CountPicker location.
    /// - With gnomes in tow, the desert's east and west routes appear and their wormoid nodes are battles (red, "blocked"),
    ///   fought again on every trip; the gnomes are cards of the deck. Without gnomes, those routes are not on the map:
    ///   only the middle path (Southern Stretch, Deep Desert, Northern Stretch) and its dialogues.
    /// - Bringing them to the south camp counts them ("Get all gnomes safely back to the south camp:0/20", "Current
    ///   gnomes:20" in the quest panel); Herbert's "Step 3" while some are still north, "Step 4" for the last ones.
    /// - Once all are home, the wormoid battles not won on the last trip stay as one-time battles (MyHotStream
    ///   Underworld fought the Wormoid Queen after the quest).
    /// Before 1.2.6 the mod had the battles from the start, as one-time battles, and hid nothing (mornox).
    /// </summary>
    public static class Gnomes
    {
        /// <summary>Herbert's team (Src.Video: "0/20").</summary>
        public const int Total = 20;

        /// <summary>The east and west routes (Northeast, East, Southeast; Northwest, West, Southwest Desert).</summary>
        private static readonly HashSet<string> Routes = new HashSet<string> { "Node058", "Node059", "Node060", "Node061", "Node062", "Node063" };

        /// <summary>The wormoid battles: the routes' and the Wormoid Queen's (Deep Desert).</summary>
        private static readonly HashSet<string> Wormoids = new HashSet<string> { "Node055", "Node058", "Node059", "Node060", "Node062", "Node063" };

        public const string LostGnome = "Lost Gnome";
        public const string PrimalShard = "Primal Shard";

        private static int Int(ulong champ, string key)
        {
            int n;
            return int.TryParse(Quests.Flag(champ, key), out n) ? n : 0;
        }

        private static void SetInt(ulong champ, string key, int v) { Quests.SetFlag(champ, key, v == 0 ? null : v.ToString()); }

        /// <summary>Gnomes travelling with the champion (in the deck).</summary>
        public static int Carried(ulong champ) { return Int(champ, "gnomes:carried"); }

        /// <summary>Gnomes brought back to the south camp.</summary>
        public static int Delivered(ulong champ) { return Int(champ, "gnomes:delivered"); }

        /// <summary>Gnomes still waiting at the North Desert.</summary>
        public static int Waiting(ulong champ) { return Math.Max(0, Total - Delivered(champ) - Carried(champ)); }

        public static bool Found(ulong champ) { return Quests.DoneLoc(champ, Quests.Gnomes, "Step1"); }

        public static bool AllHome(ulong champ) { return Quests.DoneLoc(champ, Quests.Gnomes, "Step2") || Quests.IsFinished(champ, Quests.Gnomes); }

        public static bool IsWormoid(string node) { return Wormoids.Contains(node); }

        /// <summary>A desert route the map does not show now (no gnomes in tow, quest not over).</summary>
        public static bool Hidden(ulong champ, string node)
        {
            return Routes.Contains(node) && Carried(champ) == 0 && !AllHome(champ);
        }

        /// <summary>Whether a wormoid node is a battle now (with gnomes in tow, or once all are home); not for other nodes.</summary>
        public static bool IsFight(ulong champ, string node)
        {
            if (!Wormoids.Contains(node)) return true;
            return Carried(champ) > 0 || AllHome(champ);
        }

        /// <summary>The North Desert, after Herbert's dialogue: the number picker opens (gnomes found, some waiting, none in tow).</summary>
        public static bool PickerDue(ulong champ)
        {
            return Found(champ) && !AllHome(champ) && Carried(champ) == 0 && Waiting(champ) > 0;
        }

        /// <summary>
        /// The number chosen: those gnomes travel with the champion, and the wormoid battles are armed again for this trip.
        /// Returns the map nodes to reopen.
        /// </summary>
        public static IEnumerable<string> Pick(ulong champ, int n)
        {
            n = Math.Max(0, Math.Min(n, Waiting(champ)));
            SetInt(champ, "gnomes:carried", n);
            Debug.Log("[HexRevival] [Gnomes] " + n + " gnome(s) in tow, " + Delivered(champ) + " home, " + Waiting(champ) + " waiting");
            Quests.Refresh(champ, Quests.Gnomes);
            if (n == 0) return new string[0];
            foreach (string w in Wormoids) Quests.SetFlag(champ, "prelude:" + w, null);
            return Wormoids;
        }

        /// <summary>At the south camp: the gnomes in tow are home. True when all of them are.</summary>
        public static bool Deliver(ulong champ)
        {
            int n = Carried(champ);
            if (n == 0) return AllHome(champ);
            SetInt(champ, "gnomes:delivered", Math.Min(Total, Delivered(champ) + n));
            SetInt(champ, "gnomes:carried", 0);
            bool all = Delivered(champ) >= Total;
            Debug.Log("[HexRevival] [Gnomes] " + n + " gnome(s) home (" + Delivered(champ) + "/" + Total + ")");
            if (all) { Quests.CompleteLoc(champ, Quests.Gnomes, "Step2_INFO"); Quests.CompleteLoc(champ, Quests.Gnomes, "Step2"); }
            else Quests.Refresh(champ, Quests.Gnomes);
            return all;
        }

        /// <summary>
        /// Cards added to the champion's deck for a battle at this node: the gnomes in tow and 2 Primal Shards for every 3
        /// of them (the picker's text), in the desert's wormoid battles only.
        /// </summary>
        public static List<string> DeckCards(ulong champ, string node)
        {
            List<string> res = new List<string>();
            int n = Carried(champ);
            if (n == 0 || !Wormoids.Contains(node)) return res;
            for (int i = 0; i < n; i++) res.Add(LostGnome);
            for (int i = 0; i < n / 3 * 2; i++) res.Add(PrimalShard);
            return res;
        }

        /// <summary>Quest panel counters ("Get all gnomes safely back to the south camp:N/20", "Current gnomes:N").</summary>
        public static int Counter(ulong champ, string locId)
        {
            if (locId == "Step2") return Delivered(champ);
            if (locId == "Step2_INFO") return Carried(champ);
            return 0;
        }
    }
}
