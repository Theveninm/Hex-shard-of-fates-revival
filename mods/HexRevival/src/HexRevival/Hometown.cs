using System;
using System.Collections.Generic;
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
    /// The champion's hometown (AZ0), where a new champion starts, as in the original game:
    /// 1. its elder tells the race's story ("AZ0 - Intro Story Sequence - &lt;race&gt;", plays by itself at the first visit);
    /// 2. its sparring partner trains it in a first card battle ("AZ0 - First Card Battle - &lt;race&gt;", the AZ0_&lt;race&gt;
    ///    encounter, then "... Success" or "... Fail", whose "retry_battle" answer fights again);
    /// 3. its quest giver sends it to Castle Crayburn ("Crayburn Castle - &lt;race&gt; - Quest Start") and takes the report
    ///    when it comes back ("... Quest End": the castle's reward, see Progression.OnCrayburnDone);
    /// 4. the elder sends it to the Feralroot Woods ("AZ0 - Transition to Adventure Zone 1 - &lt;race&gt;", choice_go).
    /// Src.Game: the conversations, the encounters, the hometown panoramas (adventurezone01/p_ncrtc_necropolis...) and their
    /// characters, the client's own hometown hints (UIPanoramaZoneViewModel: the sparring partner of each race, the
    /// "&lt;giver&gt;_Crayburn_QuestEnd" locations). Src.Video: Amaz, Let's Play part 2 (the latest client, after the tutorial):
    /// the Hatchery, Orzh's story, "Training with Zilth" (50 gold, 100 XP), Xarlot's quest, then the castle; MyHotStream
    /// Underworld and Ardent runs: Iddi and Nerissa before the castle. Before 1.2.10 a new champion started in the castle.
    /// PLACEHOLDER: the quest giver is shown once the sparring match is won (the client's quest hint follows the sparring
    /// partner's success line, the order seen on video), and the dwarves' elder stands at the Quarry's NodeA (the
    /// client names the dwarf nodes NodeA..NodeD: NodeB is the sparring partner, NodeC the quest giver).
    /// </summary>
    public static class Hometown
    {
        private class Home
        {
            public string Bundle, Elder, Partner, Giver;   // panorama bundle, its characters (node names of the scene)
            public string Race, BattleRace, Scene;          // the race in the conversation names, in the battle's, the encounter
        }

        private static readonly Dictionary<ERace, Home> Homes = new Dictionary<ERace, Home>
        {
            { ERace.Necrotic, new Home { Bundle = "p_ncrtc_necropolis", Elder = "Drokkord", Partner = "Iddi", Giver = "Margugram", Race = "Necrotic", Scene = "AZ0_Necrotic" } },
            { ERace.Vennen, new Home { Bundle = "p_vnnn_thehatchery", Elder = "Orzh", Partner = "Zilth", Giver = "Xarlot", Race = "Vennen", Scene = "AZ0_Vennen" } },
            { ERace.Elf, new Home { Bundle = "p_elf_satyrsroost", Elder = "Emilia", Partner = "Nerissa", Giver = "Balthasar", Race = "Elf", Scene = "AZ0_Elf" } },
            { ERace.Human, new Home { Bundle = "p_hmm_wrenscastle", Elder = "CaptainCedric", Partner = "GarethKay", Giver = "ColonelSterling", Race = "Human", Scene = "AZ0_Human" } },
            { ERace.Orc, new Home { Bundle = "p_orc_xamahuac", Elder = "Xolotl", Partner = "Moqui", Giver = "Montecuma", Race = "Orc", Scene = "AZ0_Orc" } },
            { ERace.ShinHare, new Home { Bundle = "p_shnhr_jinguru", Elder = "Mitsuo", Partner = "Sora", Giver = "Uyuki", Race = "Shin'hare", BattleRace = "Shinhare", Scene = "AZ0_Shinhare" } },
            { ERace.Dwarf, new Home { Bundle = "p_dwrf_thequarry", Elder = "NodeA", Partner = "NodeB", Giver = "NodeC", Race = "Dwarf", Scene = "AZ0_Dwarf" } },
            { ERace.Coyotle, new Home { Bundle = "p_cytl_thunderfield", Elder = "ShortBuffalo", Partner = "WhisperingBreeze", Giver = "DuskDaughter", Race = "Coyotle", Scene = "AZ0_Coyotle" } },
        };

        // Champion flags (Quests): "home" = 1 while in the hometown, "left" once gone; "home:intro" the story was told;
        // "home:fight" the sparring battle is next; "home:outcome" its success / fail line to show; "home:spar" won;
        // "home:quest" started / done; "home:bye" the farewell was offered once.
        private static string F(ulong c, string k) { return Quests.Flag(c, k); }
        private static void Set(ulong c, string k, string v) { Quests.SetFlag(c, k, v); }

        /// <summary>Answer events of the conversation being shown (choice_battle_yes, retry_battle, choice_go...).</summary>
        private static readonly Dictionary<ulong, string> s_Choice = new Dictionary<ulong, string>();

        /// <summary>Champion of the sparring battle in progress (0: none).</summary>
        private static ulong s_BattleChamp;

        public static bool Active(ulong champ) { return F(champ, "home") == "1"; }

        /// <summary>The champion has been to its hometown (the castle is entered from there, not by itself).</summary>
        public static bool Begun(ulong champ) { return F(champ, "home") != null; }

        /// <summary>A new champion (no progress, castle not started) begins in its hometown.</summary>
        public static void MaybeStart(ulong champ, bool newChampion)
        {
            if (!newChampion || Begun(champ) || QuestSim.Active || Info(champ) == null) return;
            if (F(champ, "dungeon:" + Dungeons.CrayburnId) != null || Dungeons.InRun(champ)) return;
            Set(champ, "home", "1");
            Debug.Log("[HexRevival] [Hometown] champion " + champ + " starts in its hometown");
        }

        private static Home Info(ulong champ)
        {
            champion_bits c = Champions.Get(champ);
            Home h;
            return c != null && Homes.TryGetValue(c.Race, out h) ? h : null;
        }

        /// <summary>Panorama bundle and level of the champion's hometown.</summary>
        public static string[] Bundle(ulong champ)
        {
            Home h = Info(champ);
            return h != null ? new[] { "adventurezone01/" + h.Bundle, h.Bundle } : new[] { "", "" };
        }

        // ------------------------------------------------------------------
        // Game data
        // ------------------------------------------------------------------

        private static Dictionary<string, ConversationTemplate> s_Convs;

        private static string Norm(string s) { return s.Replace("'", "").Trim().ToLowerInvariant(); }

        private static ConversationTemplate Conv(string name)
        {
            if (s_Convs == null)
            {
                s_Convs = new Dictionary<string, ConversationTemplate>();
                foreach (ConversationTemplate c in Singleton<TemplateManager>.Instance.ConversationTemplates.Values)
                    if (c != null && !string.IsNullOrEmpty(c.m_Name) && !s_Convs.ContainsKey(Norm(c.m_Name))) s_Convs[Norm(c.m_Name)] = c;
            }
            ConversationTemplate t;
            if (!s_Convs.TryGetValue(Norm(name), out t)) Debug.LogWarning("[HexRevival] [Hometown] no conversation " + name);
            return t;
        }

        private static string Intro(Home h) { return "AZ0 - Intro Story Sequence - " + h.Race; }
        private static string Battle(Home h) { return "AZ0 - First Card Battle - " + (h.BattleRace ?? h.Race); }
        private static string Farewell(Home h) { return "AZ0 - Transition to Adventure Zone 1 - " + h.Race; }
        private static string QuestStart(Home h) { return "Crayburn Castle - " + h.Race + " - Quest Start"; }
        private static string QuestEnd(Home h) { return "Crayburn Castle - " + h.Race + " - Quest End"; }

        private static string Encounter(Home h)
        {
            foreach (SceneData s in Singleton<TemplateManager>.Instance.CampaignScenes.Values)
                if (s is EncounterScene && s.m_Name == h.Scene) return s.m_Id.ToString();
            Debug.LogWarning("[HexRevival] [Hometown] no encounter " + h.Scene);
            return null;
        }

        private static bool CastleDone(ulong champ) { return F(champ, "dungeon:" + Dungeons.CrayburnId) != null; }

        // ------------------------------------------------------------------
        // The panorama's characters
        // ------------------------------------------------------------------

        /// <summary>The hometown's locations (one per character) into the panorama state.</summary>
        public static void Fill(ulong champ, GameplayState gs)
        {
            Home h = Info(champ);
            if (h == null) return;
            bool won = F(champ, "home:spar") != null;
            string quest = F(champ, "home:quest");

            // The elder: the story (by itself the first time), then, the castle's report made, the farewell.
            if (quest == "done") Add(gs, h.Elder, "Farewell", Conv(Farewell(h)), F(champ, "home:bye") == null, give: true);
            else Add(gs, h.Elder, "Intro", Conv(Intro(h)), F(champ, "home:intro") == null);

            // The sparring partner: the challenge, the battle, its outcome; afterwards the success line.
            string outcome = F(champ, "home:outcome");
            if (outcome != null) Add(gs, h.Partner, "BattleEnd", Singleton<TemplateManager>.Instance.GetConversationTemplate(new ResourceId(new Guid(outcome))), true);
            else if (F(champ, "home:fight") != null)
            {
                string enc = Encounter(h);
                if (enc != null)
                {
                    Add(gs, h.Partner, "Battle", Conv(Battle(h)), true, encounter: enc);
                    gs.Encounters.Add(new Game.Shared.Eval.Data.Encounter(enc, new Dictionary<string, object> { { "encscene", enc } }));
                }
            }
            else if (won) Add(gs, h.Partner, "Battle", Conv(Battle(h) + " Success"), false);
            else Add(gs, h.Partner, "Battle", Conv(Battle(h)), false);

            // The quest giver: Castle Crayburn, then the report.
            if (!won) return;
            if (quest == null) Add(gs, h.Giver, "Crayburn_QuestStart", Conv(QuestStart(h)), false, give: true);
            else if (quest == "started" && CastleDone(champ)) Add(gs, h.Giver, "Crayburn_QuestEnd", Conv(QuestEnd(h)), false, turnIn: true);
        }

        private static void Add(GameplayState gs, string node, string role, ConversationTemplate conv, bool autostart,
            bool give = false, bool turnIn = false, string encounter = null)
        {
            if (conv == null && encounter == null) return;
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["name"] = node + "_" + role;
            d["node"] = node;
            d["type"] = encounter != null ? "Encounter" : "Convo";
            if (encounter != null) d["encounter"] = encounter;
            else d["conversationId"] = conv.m_Id.ToString();
            d["visible"] = true; d["enabled"] = true; d["completed"] = false; d["repeatable"] = !autostart && !give && !turnIn;
            d["givequest"] = give; d["turninquest"] = turnIn; d["autostart"] = autostart; d["autopan"] = autostart;
            d["allow_cancel"] = true;
            gs.VisLocs.Add(new Location(d));
            Dictionary<string, object> nd = new Dictionary<string, object> { { "id", node }, { "type", "DEFAULT" } };
            if (conv != null && conv.m_ChampionId.IsValid) nd["championId"] = conv.m_ChampionId.ToString();
            gs.LocNodes.Add(new Node(node, nd));
        }

        // ------------------------------------------------------------------
        // Events
        // ------------------------------------------------------------------

        /// <summary>A panorama event in the hometown (location = the one in progress). True if the client was sent elsewhere.</summary>
        public static bool Event(ulong champ, string evt, string location, object[] args)
        {
            Home h = Info(champ);
            if (h == null) return false;
            Debug.Log("[HexRevival] [Hometown] event " + evt + (location != null ? " at " + location : ""));
            if (evt.StartsWith("choice_") || evt == "retry_battle") { s_Choice[champ] = evt; return false; }
            if (evt == "start" && F(champ, "home:fight") != null)
            {
                // The battle panel's "Battle" button.
                string enc = Encounter(h);
                if (enc == null) return false;
                Campaign.NoteStartArgs(args);
                s_BattleChamp = champ;
                Campaign.LaunchBattle(champ, enc, "hometown sparring");
                return true;
            }
            if (evt == "enc_cancel" || evt == "cancel") { if (location != null && location.EndsWith("_Battle")) Set(champ, "home:fight", null); return false; }
            if ((evt != "conv_done" && evt != "accept") || location == null) return false;

            string choice;
            s_Choice.TryGetValue(champ, out choice);
            s_Choice.Remove(champ);
            if (location.EndsWith("_Intro")) Set(champ, "home:intro", "1");
            else if (location.EndsWith("_Battle"))
            {
                if (choice == "choice_battle_yes" && F(champ, "home:spar") == null) Set(champ, "home:fight", "1");
            }
            else if (location.EndsWith("_BattleEnd"))
            {
                Set(champ, "home:outcome", null);
                if (choice == "retry_battle") Set(champ, "home:fight", "1");
            }
            else if (location.EndsWith("_Crayburn_QuestStart"))
            {
                Set(champ, "home:quest", "started");
                Debug.Log("[HexRevival] [Hometown] champion " + champ + " leaves for Castle Crayburn");
                Dungeons.Enter(champ, "crayburn");
                return true;
            }
            else if (location.EndsWith("_Crayburn_QuestEnd"))
            {
                Set(champ, "home:quest", "done");
                Progression.OnCrayburnDone(champ);
            }
            else if (location.EndsWith("_Farewell"))
            {
                Set(champ, "home:bye", "1");
                if (choice == "choice_go")
                {
                    Set(champ, "home", "left");
                    Debug.Log("[HexRevival] [Hometown] champion " + champ + " leaves for the Feralroot Woods");
                    Campaign.ReturnToArea(champ, "AZ1", null);
                    return true;
                }
            }
            return false;
        }

        /// <summary>End of a match: true if it was the sparring battle (its success / fail line comes next).</summary>
        public static bool OnGameEnded(bool won)
        {
            ulong champ = s_BattleChamp;
            if (champ == 0) return false;
            s_BattleChamp = 0;
            Home h = Info(champ);
            Set(champ, "home:fight", null);
            if (h != null)
            {
                ConversationTemplate line = Conv(Battle(h) + (won ? " Success" : " Fail"));
                if (line != null) Set(champ, "home:outcome", line.m_Id.ToString());
            }
            Debug.Log("[HexRevival] [Hometown] sparring battle " + (won ? "won" : "lost"));
            if (won)
            {
                Set(champ, "home:spar", "won");
                Progression.OnCampaignBattleWon(champ, "home/spar");
            }
            return true;
        }

        /// <summary>Dev: "home [state] | reset | skip | new &lt;race&gt;".</summary>
        public static string Command(string[] parts, ulong champ)
        {
            string sub = parts.Length > 1 ? parts[1] : "state";
            if (sub == "reset")
            {
                foreach (string k in new[] { "home", "home:intro", "home:fight", "home:outcome", "home:spar", "home:quest", "home:bye" }) Set(champ, k, null);
                return "hometown flags cleared for " + champ;
            }
            if (sub == "skip") { Set(champ, "home", "left"); return "hometown left"; }
            if (sub == "talk" && parts.Length > 2)
            {
                // dev: "home talk <character>" = double-clicks that character of the panorama shown (any hub too)
                foreach (GoPanoramaInteractable go in UnityEngine.Object.FindObjectsOfType<GoPanoramaInteractable>())
                    if (go.name == parts[2])
                    {
                        UIPanoramaZoneViewModel vm = UnityEngine.Object.FindObjectOfType<UIPanoramaZoneViewModel>();
                        if (vm == null || go.Context == null || go.Context.Location == null) return "not available";
                        vm.OnNodeDoubleClicked(go.Context);
                        return "talking to " + parts[2];
                    }
                return "no character " + parts[2];
            }
            if (sub == "new" && parts.Length > 2)
            {
                // dev: "home new <race>" = a new warrior of that race on the profile (then "dun go <id>" to play it)
                ERace race = (ERace)Enum.Parse(typeof(ERace), parts[2], true);
                GameClient.Instance.PlayerProfile.CreateChampion("Home" + race, race, EChampionClass.Warrior, EGender.Male);
                return "champion requested";
            }
            string s = "champion " + champ + " home=" + F(champ, "home");
            foreach (string k in new[] { "home:intro", "home:fight", "home:outcome", "home:spar", "home:quest", "home:bye" }) s += " " + k + "=" + F(champ, k);
            return s + " castle=" + CastleDone(champ);
        }
    }
}
