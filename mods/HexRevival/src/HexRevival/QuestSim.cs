using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Game.Client;
using Game.Shared.Mechanics;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Quest simulation, to test quests without playing them (dev command "sim").
    ///
    /// A script of steps is run for a throwaway champion in a save folder outside the account list
    /// (HexRevivalSim next to the game): the campaign logic runs as for a real champion, but the
    /// client is not told anything (no map update, no journal, no loot window) and rewards are only
    /// described. The account in use is put back afterwards.
    ///
    /// Steps (separated by ';'):
    ///   new &lt;Race&gt;           fresh champion of that race (whole map visible)
    ///   visit &lt;Node&gt;         go to a map node; shows what the client would display
    ///   talk [event ...]      finishes the node's conversation, after the dialogue events given (e.g. choice_Help)
    ///   fight win|lose [tamed] battle at the current node
    ///   hub &lt;Node&gt;           enters a hub (Cave-In Node017, Ambling Mesa Node034): lists the NPCs
    ///   npc &lt;Name&gt;           talks to a hub NPC
    ///   leave                 leaves the hub
    ///   quests                quest progress of the champion
    /// The report is written to HexRevivalSim.txt.
    /// </summary>
    public static class QuestSim
    {
        public static bool Active { get; private set; }
        /// <summary>The simulated battle captured a creature with the taming item.</summary>
        public static bool Tamed { get; private set; }

        private const ulong Champ = 900;
        private static StringBuilder s_Log;

        public static void Note(string line) { if (s_Log != null) s_Log.AppendLine(line); }

        private static string Folder { get { return Path.Combine(LocalServer.Root, "HexRevivalSim"); } }

        public static string Run(string script)
        {
            s_Log = new StringBuilder();
            Saves.Info account = Saves.Current;
            if (account == null) return "log in first";
            FieldInfo currentChamp = typeof(PlayerProfile).GetField("m_CurrentChampionId", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            object champBefore = currentChamp.GetValue(GameClient.Instance.PlayerProfile);
            Active = true;
            Directory.CreateDirectory(Folder);
            Saves.Swap(new Saves.Info { Name = "simulation", Folder = Folder, UnlockAll = true });
            Campaign.Reset();
            Quests.Reset();
            Champions.Reload();
            try
            {
                foreach (string raw in script.Split(';'))
                {
                    string step = raw.Trim();
                    if (step.Length == 0) continue;
                    s_Log.AppendLine("> " + step);
                    try { Step(step.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)); }
                    catch (Exception e) { s_Log.AppendLine("  ERROR " + e); }
                }
            }
            finally
            {
                Campaign.SimLeaveHub(Champ);
                Saves.Swap(account);
                Campaign.Reset();
                Quests.Reset();
                Champions.Reload();
                currentChamp.SetValue(GameClient.Instance.PlayerProfile, champBefore);
                Active = false;
                Tamed = false;
            }
            File.WriteAllText(Path.Combine(LocalServer.Root, "HexRevivalSim.txt"), s_Log.ToString());
            string report = s_Log.ToString();
            s_Log = null;
            return report;
        }

        private static void Step(string[] p)
        {
            switch (p[0])
            {
                case "new":
                {
                    ERace race = (ERace)Enum.Parse(typeof(ERace), p.Length > 1 ? p[1] : "Human", true);
                    foreach (string f in Directory.GetFiles(Folder)) File.Delete(f);
                    // id|name|level|class|race|gender|owner|deck|deck template|pet (deck: any id, the simulation plays no match)
                    File.WriteAllText(Saves.File("champions.txt"), Champ + "|Sim|1|Warrior|" + race + "|Male|0|999999|0|\n");
                    Campaign.Reset();
                    Quests.Reset();
                    Champions.Reload();
                    Campaign.SimStart(Champ);
                    Note("  champion " + Champ + " " + race + " (" + race.GetFaction() + ")");
                    break;
                }
                case "visit":
                    Campaign.StartLocation(Champ, p[1], null);
                    Note("  " + Campaign.SimDescribe(Champ, p[1]));
                    break;
                case "talk":
                    for (int i = 1; i < p.Length; i++) Campaign.SendEvent(Champ, p[i], new object[0], null);
                    Campaign.SendEvent(Champ, "conv_done", new object[0], null);
                    break;
                case "fight":
                    Tamed = p.Length > 2 && p[2] == "tamed";
                    Campaign.SimFight(Champ, p.Length > 1 && p[1] == "win");
                    Tamed = false;
                    break;
                case "show":
                    Note("  " + Campaign.SimDescribe(Champ, p[1]));
                    break;
                case "hub":
                    Note("    " + Campaign.SimHub(Champ, p[1]));
                    break;
                case "npc":
                    Note("  " + Campaign.SimNpc(Champ, string.Join(" ", p, 1, p.Length - 1)));
                    Note("    " + Campaign.SimNpcs(Champ));
                    break;
                case "leave":
                    Campaign.SimLeaveHub(Champ);
                    break;
                case "quests":
                    Note("  " + Quests.Command(new[] { "quest", "of", Champ.ToString() }));
                    break;
                default:
                    Note("  unknown step");
                    break;
            }
        }
    }
}
