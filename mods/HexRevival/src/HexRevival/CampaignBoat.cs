using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Client;
using Game.Shared;
using Game.Shared.Campaign.Messages;
using Game.Shared.Domain;
using Game.Shared.Eval.Data;
using Game.Shared.Mechanics;
using Reckoning.Game;
using Reckoning.Game.Conversations;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// AZ2 sea voyages (Src.Game unless noted). At Portsmeare Harbor (Node019B) the champion names a ship once,
    /// then sets sail from the boat's talent window: the ship is a sub-champion of class Boat (template "Boat",
    /// 40 health) with its own deck (a profile deck template, see DeckTemplates) and talents.
    /// The client moves the token between the harbor and the sea (Node00B) itself and draws it as a boat
    /// while "champ_override" names the ship.
    /// - Ship talent points (Src.Video): 3 when the ship is named, 1 for the first win over the Cloud Queen, the
    ///   Cloud King, the Storm of the Century and Bloodhorn. The game counts talent points from the level
    ///   (level - 1), so the ship's level carries them. Sea battles' XP goes to the champion (Src.Video).
    /// - Hull (40, "Heavy Hull" 60) kept across the battles of a voyage (Src.Game: Basic Hull "The Hull of your
    ///   ship persists across encounters", the Ship Port "When running low on hull, you can return here for
    ///   repairs", Water Town's shipwrights; the client reports the end health, +5 with Repair Crew; Src.Video: KVC
    ///   Jan 2017. MyHotStream late 2017 shows full hull at each battle, explained by no talent or ability). Full
    ///   again on each voyage and at Water Town. A battle lost sinks the ship: the game's "Your boat has sunk!"
    ///   message, back to the harbor.
    /// - The sea battles are fought again on each voyage, and the three sea quests must be done in one voyage:
    ///   their objectives are reset when a voyage begins or ends. The harbor master (Node00B) takes the turn-ins.
    /// - Zodiac Docks (NodeB0X): disembark at the Mouth of the Zodiac River (Node033), the new land.
    /// </summary>
    public static partial class Campaign
    {
        private const string Harbor = "Node019B", Port = "Node00B", Docks = "NodeB0X", NewLand = "Node033";
        private static readonly string[] SeaQuests = { "q_boat_pirates", "q_boat_elementals", "q_boat_lobsters" };
        /// <summary>The ship names offered by the harbor's conversation (events Boat_Name_1..4).</summary>
        private static readonly string[] ShipNames = { "Seas The Day", "Kraken's Gold", "The Reckoning", "The Windy Way" };
        private static readonly ResourceId HeavyHull = new ResourceId("{80f783f1-8ae3-4dfc-a232-d5d27be74111}");
        /// <summary>Champion entry of the ship in the campaign state (the client creates the sub-champion from it).</summary>
        private const string ShipEntry = "Boat";

        /// <summary>Sea nodes (the client's own list, UIDungeonZoneViewModel.m_BoatNodeList).</summary>
        private static bool IsSea(MapNode n) { return n.Z == AZ2 && (n.Id == Port || n.Id.StartsWith("NodeB")); }

        private static bool OnVoyage(ulong champ) { return Quests.Flag(champ, "boat:voyage") != null; }

        private static champion_bits Ship(ulong champ) { return Champions.SubChampion(champ, EChampionClass.Boat); }

        private static bool HasTalent(champion_bits ship, ResourceId talent) { return ship != null && ship.ChampionTalents != null && ship.ChampionTalents.Contains(talent); }

        private static int MaxHull(ulong champ) { return HasTalent(Ship(champ), HeavyHull) ? 60 : 40; }

        /// <summary>Ship talent points earned (naming, some sea battles): the ship's level is 1 + that.</summary>
        internal static void AddShipTalentPoints(ulong champ, int n)
        {
            int had; int.TryParse(Quests.Flag(champ, "boat:points"), out had);
            Quests.SetFlag(champ, "boat:points", (had + n).ToString());
            SyncShipLevel(champ);
        }

        private static void SyncShipLevel(ulong champ)
        {
            champion_bits ship = Ship(champ);
            int points; int.TryParse(Quests.Flag(champ, "boat:points"), out points);
            if (ship == null || ship.Level == 1 + points) return;
            Champions.SetLevel(ship.Id, 1 + points);
            try { GameClient.Instance.PlayerProfile.GetPvEChampionById(ship.Id).SourceBits.Level = 1 + points; } catch { }
        }

        /// <summary>Champion that fights at this node: the ship at sea, otherwise the champion.</summary>
        private static ulong Fighter(CampState st, MapNode n)
        {
            champion_bits ship = IsSea(n) && OnVoyage(st.ChampId) ? Ship(st.ChampId) : null;
            return ship != null ? ship.Id : st.ChampId;
        }

        /// <summary>Deck a sub-champion plays: its deck template, copied into a player deck the match can load (0 if none).</summary>
        private static ulong SubChampionDeck(ulong champId)
        {
            champion_bits c = Champions.Get(champId);
            if (c == null || c.OwnerChampionId == 0) return 0;
            Game.Shared.Profile.ProfileDeckTemplate t = DeckTemplates.Get(c.DeckTemplateId);
            return t != null ? Decks.FromTemplate(c.Id, t) : 0;
        }

        // ------------------------------------------------------------------
        // Campaign state sent to the client
        // ------------------------------------------------------------------

        /// <summary>The ship's champion entry, its name while it can sail, and the harbor master's turn-ins.</summary>
        private static void AddShipState(CampState st, GameplayState gs, Dictionary<string, object> pub)
        {
            if (st.Z != AZ2) return;
            SyncShipLevel(st.ChampId);   // the ship may have been created after its points were earned
            gs.Champions.Add(new Champion(ShipEntry, new Dictionary<string, object> { { "race", "None" }, { "class", "Boat" }, { "gender", "Female" } }));
            string name = Quests.Flag(st.ChampId, "boat:name");
            MapNode harbor = FindNode(AZ2, Harbor);
            if (name != null && (OnVoyage(st.ChampId) || (harbor != null && st.ALoc == harbor.Name)))
                pub["champ_override"] = new Dictionary<string, object> { { "id", ShipEntry }, { "name", name } };
            string warp = Quests.Flag(st.ChampId, "boat:warp");
            if (warp != null) pub["force_warp_dest"] = warp;
            if (Quests.Flag(st.ChampId, "boat:failed") != null)
            {
                // Shown once, with the state that follows the lost battle's outcome conversation.
                pub["show_boat_fail_msg"] = true;
                Quests.SetFlag(st.ChampId, "boat:failed", null);
            }
            foreach (string q in SeaQuests)
                pub["can_turnin_" + q.Substring("q_boat_".Length)] = Quests.IsActive(st.ChampId, q) && Quests.DoneLoc(st.ChampId, q, "Fight1")
                    && Quests.DoneLoc(st.ChampId, q, "Fight2") && Quests.DoneLoc(st.ChampId, q, "Fight3");
        }

        /// <summary>Location of a harbor or sea node (d: the location being built).</summary>
        private static void ShipLocation(CampState st, MapNode n, Dictionary<string, object> d)
        {
            if (n.Z != AZ2) return;
            if (n.Id == Harbor && Quests.Flag(st.ChampId, "boat:name") != null)
            {
                // Ship named: the harbor opens the boat's talent window ("Set Sail" = accept).
                d["type"] = "ChampionOverride";
                d["championId"] = ShipEntry;
                d.Remove("conversationId");
                d["completed"] = false;
                d["repeatable"] = true;
                d["autostart"] = st.ALoc == n.Name;
            }
            else if (n.Id == Port || n.Id == Docks)
            {
                // The harbor master and the docks: every time the ship is there.
                d["completed"] = false;
                d["repeatable"] = true;
            }
            else if (n.Id == "NodeB12" && Quests.Flag(st.ChampId, "boat:blessed") != null)
            {
                ConversationTemplate again = NodeConvs(n).Find(c => c.m_Name.ToUpperInvariant().Contains("ALREADY VISITED"));
                if (again != null) d["conversationId"] = again.m_Id.ToString();
            }
        }

        // ------------------------------------------------------------------
        // Events
        // ------------------------------------------------------------------

        /// <summary>Ship events (harbor, sea, docks). True when handled: SendEvent then only answers.</summary>
        private static bool ShipEvent(CampState st, string evt, string arg0, MapNode active)
        {
            if (st.Z != AZ2) return false;
            ulong champ = st.ChampId;
            if (evt.StartsWith("Boat_Name_"))
            {
                int i;
                if (int.TryParse(evt.Substring("Boat_Name_".Length), out i) && i >= 1 && i <= ShipNames.Length) Quests.SetFlag(champ, "boat:name", ShipNames[i - 1]);
                return true;
            }
            if (evt == "warp_move")
            {
                // The client moved the token where force_warp_dest said.
                if (arg0 != null) st.LastNode = arg0;
                Quests.SetFlag(champ, "boat:warp", null);
                return true;
            }
            if (evt == "boat_fail_shown") { Quests.SetFlag(champ, "boat:failed", null); return true; }
            if (active == null) return false;
            // Water Town: "The shipwrights of the town offer to repair the damage to your hull." (Src.Game)
            if (evt == "conv_done" && active.Id == WaterTown) Quests.SetFlag(champ, HullFlag, null);
            if (evt == "conv_done" && active.Id == Harbor && Quests.Flag(champ, "boat:name") != null && !st.Completed.Contains(Harbor))
            {
                // Naming conversation done: the talent window opens next (the location stays active).
                Complete(st, active);
                Quests.CompleteLoc(champ, "q_the_new_land", "Step1");
                // The harbor master gives the three sea quests here, all at once (Src.Video: KVC 002 1:28:09,
                // MyHotStream 2017-06-22 1:58: they appear in the tracker with the talent points window).
                foreach (string q in SeaQuests) Quests.Start(champ, q);
                Progression.OnShipNamed(champ);
                return true;
            }
            if (evt == "accept" && active.Id == Harbor) { SetSail(st); return true; }
            if (evt == "accept" && active.Id == Port) { EndVoyage(st, Harbor); return true; }
            if (evt == "accept" && active.Id == Docks)
            {
                Quests.CompleteLoc(champ, "q_the_new_land", "Step2");
                Quests.CompleteLoc(champ, "q_the_new_land", "Step3");
                EndVoyage(st, NewLand);
                return true;
            }
            if (evt.StartsWith("turnin_"))
            {
                // The harbor master's turn-in of a sea quest (its last two objectives: the voyage rule and the turn-in).
                string q = "q_boat_" + evt.Substring("turnin_".Length);
                Quests.CompleteLoc(champ, q, "Desc1");
                Quests.CompleteLoc(champ, q, "TurnIn");
                return true;
            }
            if (evt == "mermaidblessing")
            {
                // The witch's blessing (Src.Video: "Your starting hand size is increased by 1", 8 cards in the next battle; see ShipEffects).
                Quests.SetFlag(champ, "boat:blessed", "1");
                Quests.SetFlag(champ, "boat:blessing", "1");
                return false;
            }
            if (evt == "conv_done" && IsSea(active) && Quests.Flag(champ, "boat:sunk") != null && Quests.Flag(champ, "outcome:" + active.Key) != null)
            {
                // A sea battle lost: after its outcome conversation the voyage is over.
                Quests.SetFlag(champ, "outcome:" + active.Key, null);
                Quests.SetFlag(champ, "boat:sunk", null);
                Quests.SetFlag(champ, "boat:failed", "1");
                EndVoyage(st, Harbor);
                return true;
            }
            return false;
        }

        /// <summary>"Set Sail": a new voyage from the harbor master's quay (Node00B), the sea fought anew.</summary>
        private static void SetSail(CampState st)
        {
            ulong champ = st.ChampId;
            Quests.SetFlag(champ, "boat:voyage", "1");
            Quests.SetFlag(champ, HullFlag, null);
            foreach (MapNode n in st.Z.Nodes)
                if (IsSea(n) && n.Id != "NodeB02" && n.Id != "NodeB07" && n.Id != "NodeB12") st.Completed.Remove(n.Id);
            // "Must defeat all 3 in one voyage" (Src.Video): each voyage starts the sea quests over. Start: for a
            // save named before the quests were given at the naming.
            foreach (string q in SeaQuests) { Quests.Start(champ, q); Quests.ResetProgress(champ, q); }
            Reveal(st, Port);
            st.ALoc = null;
            st.LastNode = Port;
            Quests.SetFlag(champ, "boat:warp", Port);
            Debug.Log("[HexRevival] [Campaign] " + champ + " sets sail on " + Quests.Flag(champ, "boat:name"));
        }

        /// <summary>The voyage ends (back at the harbor, or ashore at the new land): the sea quests not turned in start over.</summary>
        private static void EndVoyage(CampState st, string to)
        {
            ulong champ = st.ChampId;
            Quests.SetFlag(champ, "boat:voyage", null);
            foreach (string q in SeaQuests) Quests.ResetProgress(champ, q);
            Reveal(st, to);
            st.ALoc = null;
            st.LastNode = to;
            Quests.SetFlag(champ, "boat:warp", to);
            Debug.Log("[HexRevival] [Campaign] " + champ + " voyage over, at " + to);
        }

        // ------------------------------------------------------------------
        // Sea battles
        // ------------------------------------------------------------------

        private static bool s_HullApplied;

        private const string HullFlag = "boat:hull";
        private const string WaterTown = "NodeB11";
        private static int s_EndHealth = -1;

        /// <summary>The ship's hull left (the voyage's last battle won), its maximum if none.</summary>
        private static int Hull(ulong champ)
        {
            int max = MaxHull(champ), h;
            string f = Quests.Flag(champ, HullFlag);
            return f != null && int.TryParse(f, out h) && h > 0 ? Math.Min(h, max) : max;
        }

        /// <summary>"player_end_health" of the battle (the client adds Repair Crew's 5).</summary>
        internal static void NoteEndHealth(object value)
        {
            try { s_EndHealth = Convert.ToInt32(value); } catch { s_EndHealth = -1; }
        }

        /// <summary>Start of a sea battle (after the mulligan): the hull left.</summary>
        private static void TickShip(StandaloneSession ss)
        {
            champion_bits ship = Champions.Get(s_BattleChamp);
            if (s_HullApplied || ship == null || ship.ChampionClass != EChampionClass.Boat) return;
            Player human = Cheats.FindPlayer(ss, "j");
            if (human == null || human.m_ChampionCard == null) return;
            s_HullApplied = true;
            ss.SetChampionHealth(human.m_ChampionCard, Hull(ship.OwnerChampionId), false);
            Server.MarkDirty();
        }

        /// <summary>The witch's blessing for the ship's next battle: the game's "Mermaid's Blessing" effect card (starting hand + 1).</summary>
        private static List<string> ShipEffects(CampState st, MapNode node)
        {
            List<string> res = new List<string>();
            if (IsSea(node) && Quests.Flag(st.ChampId, "boat:blessing") != null)
            {
                Quests.SetFlag(st.ChampId, "boat:blessing", null);
                res.Add("Mermaid's Blessing");
            }
            return res;
        }

        /// <summary>End of a sea battle: a defeat sinks the ship (after the outcome conversation).</summary>
        private static void ShipBattleEnded(ulong champ, bool won)
        {
            if (!OnVoyage(champ)) return;
            if (!won) { Quests.SetFlag(champ, "boat:sunk", "1"); Quests.SetFlag(champ, HullFlag, null); return; }
            if (s_EndHealth > 0) Quests.SetFlag(champ, HullFlag, Math.Min(s_EndHealth, MaxHull(champ)).ToString());
        }

        private static void ResetShipBattle() { s_HullApplied = false; s_EndHealth = -1; }
    }
}
