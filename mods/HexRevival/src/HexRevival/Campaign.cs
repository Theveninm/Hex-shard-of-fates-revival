using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Game.Client;
using Game.Client.Campaign;
using Game.Client.GameState;
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
    /// Local campaign service, replacing the campaign server (CampSysGeneral).
    ///
    /// The client talks to the campaign only through the static ClientCampaignManager methods
    /// (QueryCurrentCampaign, GetSummary, QueryState, StartLocation, SendEvent...). They are
    /// intercepted (see Plugin.cs) and answered here with a GameplayState built locally, which
    /// the client caches and renders exactly as it did with the server's answers.
    ///
    /// Scope (milestone 1): the AZ1 world map (Howling Plains). Map nodes come from the game's
    /// own AZ1 DungeonTemplate, the paths between nodes from the client's map prefab, and each
    /// node's encounter or conversation from the game's templates. Completing a node reveals its
    /// neighbours. One campaign per champion, saved in HexRevival.campaign.txt.
    /// </summary>
    public static partial class Campaign
    {
        /// <summary>
        /// An adventure zone: its map template (the DungeonTemplate listing the nodes), the client's map
        /// prefab (paths), the prefix of its scene and conversation names, its hubs. The campaign id of a
        /// champion in a zone is Base + champion.
        /// </summary>
        internal class Zone
        {
            public string Key;              // "AZ1": quest tracker group, name of the campaign
            public string Prefix;           // template names, spaces removed and upper case: "AZ1-NODE"
            public ResourceId TemplateId;
            public string NodesPrefab;
            public string BackgroundPrefab = "campaign/azmap/azmap";  // the world map, shared by the zones
            public string StartNode;
            public ulong Base;
            public Dictionary<string, string[]> Panoramas;             // hub node -> asset bundle, level
            public List<MapNode> Nodes;
            public Dictionary<string, HashSet<string>> Edges;
            public HashSet<string> NodeNumbers;                        // numbers of the template's nodes ("19", "19B")
            public Dictionary<string, string> NumberAlias;             // node -> number in its names
            public Dictionary<string, EFactions> HubFaction;           // hubs whose quest givers serve one faction
            public Dictionary<string, string> TitleNode;               // node title (upper case, no spaces) -> node
            /// <summary>
            /// AZ2 names a node's faction variants with a letter ("NODE 6A ... (vs Ardent)", "Node 6B ... -
            /// Underworld") on a single map node: they belong to the node unless the letter is a node of its own.
            /// </summary>
            public bool Matches(string nameNumber, string nodeNumber)
            {
                if (nameNumber == null || nodeNumber == null) return false;
                if (nameNumber == nodeNumber) return true;
                if (this != AZ2 || nameNumber.Length != nodeNumber.Length + 1 || !nameNumber.StartsWith(nodeNumber)) return false;
                char c = nameNumber[nameNumber.Length - 1];
                return (c == 'A' || c == 'B') && !NodeNumbers.Contains(nameNumber) && char.IsDigit(nodeNumber[nodeNumber.Length - 1]);
            }
        }

        /// <summary>AZ1, Howling Plains.</summary>
        internal static readonly Zone AZ1 = new Zone
        {
            Key = "AZ1", Prefix = "AZ1-NODE", TemplateId = new ResourceId(new Guid("742c6fc1-77e5-4029-bf9e-4e8af387857a")),
            NodesPrefab = "campaign/az01/nodes", StartNode = "Node001", Base = 0,
            // Panorama level of each hub (Src.HexServer, read from the client's asset bundles).
            Panoramas = new Dictionary<string, string[]>
            {
                { "Node017", new[] { "adventurezone01/p_dwrf_cavein", "p_dwrf_cavein" } },
                { "Node034", new[] { "adventurezone01/p_cytl_amblingmesa", "p_cytl_amblingmesa" } },
                { "Node049", new[] { "adventurezone01/p_cytl_thunderfield", "p_cytl_thunderfield" } },
            },
        };

        /// <summary>AZ2, the Alachian Sea (entered from AZ1's River of Whispers, Node00Y).</summary>
        internal static readonly Zone AZ2 = new Zone
        {
            Key = "AZ2", Prefix = "AZ2-NODE", TemplateId = new ResourceId(new Guid("a2f267fa-fd2e-43d2-bf95-9fd7367ba5de")),
            NodesPrefab = "campaign/az02/nodes", StartNode = "Node099", Base = 500000,
            // Src.Game names: "Node 99B - Transfer To AZ1" (the River of Whispers, back to AZ1), "Node 00B A -
            // Return To Port" (the Harbor Master), "Node B0X A - Disembark to 33".
            NumberAlias = new Dictionary<string, string> { { "Node00Z", "99B" }, { "Node00B", "BA" }, { "NodeB0X", "B0XA" } },
            // Wakuna's givers are the Ardent's (Belarius, Blue Sparrow...), Naagaan's the Underworld's (Takumi, Renner...).
            HubFaction = new Dictionary<string, EFactions> { { "Node007", EFactions.Aria }, { "Node027", EFactions.Underworld } },
            // Hubs: Wakuna (coyotle) and Naagaan (necrotic), the client's AZ2 panorama bundles.
            Panoramas = new Dictionary<string, string[]>
            {
                { "Node007", new[] { "adventurezone02/p_cytl_wakuna", "p_cytl_wakuna" } },
                { "Node027", new[] { "adventurezone02/p_ncrtc_naagaan", "p_ncrtc_naagaan" } },
            },
        };

        private static readonly Zone[] Zones = { AZ1, AZ2 };

        /// <summary>Zone of an AREA campaign id (null if not one).</summary>
        private static Zone ZoneOf(ulong campId)
        {
            if (campId >= PanoBase) return null;
            return campId >= AZ2.Base ? AZ2 : AZ1;
        }

        internal static Zone ZoneByKey(string key) { return Array.Find(Zones, z => z.Key == key) ?? AZ1; }

        /// <summary>AZ1 map template id (other code refers to it).</summary>
        public static ResourceId AZ1TemplateId { get { return AZ1.TemplateId; } }

        // ------------------------------------------------------------------
        // Map model (built once from the game data)
        // ------------------------------------------------------------------

        internal class MapNode
        {
            public Zone Z;
            public string Id;            // "Node001", the map prefab's node name
            /// <summary>Id unique across the zones (flags, rewards): "Node019" in AZ1, "AZ2/Node019" in AZ2.</summary>
            public string Key { get { return Z == AZ1 ? Id : Z.Key + "/" + Id; } }
            public string Name;          // location name sent back by the client (unique)
            public string Type = "Empty"; // LocationType: Encounter, Convo or Empty
            public string Encounter;     // EncounterScene GUID
            public string Conversation;  // ConversationTemplate GUID
            public string Scenes = "";   // names and titles of every scene of the node (reward lookups, e.g. "Shroom Haus")
            public string Panorama;      // hub panorama scene (Cave-In, Ambling Mesa, Thunderfield): NPCs and quest givers
            public bool IsHaus { get { return Scenes.ToUpperInvariant().Contains("SHROOM HAUS"); } }
            public string RepeatConversation; // "... - Repeat Encounters" conversation, offered once the node is completed (Army of Myth)
            public string FightEncounter; // battle started by a dialogue choice "choice_fight" (Brink Ridge blockade)
            public string Dungeon;        // dungeon entered from this node (Dungeons key)
            public Dictionary<string, string> Variants; // battle by faction and dialogue choice ("UW:help", "AR"...), see FactionNodes
            public string FactionOnly;    // "AR" / "UW": the battle is that faction's only (the other one just talks)
        }

        private static MapNode FindNode(Zone z, string idOrName)
        {
            BuildMap(z);
            return z.Nodes.Find(n => n.Id == idOrName || n.Name == idOrName);
        }

        private static void BuildMap(Zone z)
        {
            if (z.Nodes != null) return;
            z.Nodes = new List<MapNode>();
            z.Edges = new Dictionary<string, HashSet<string>>();
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            SceneData sd;
            DungeonTemplate dt = tm.CampaignScenes.TryGetValue(z.TemplateId, out sd) ? sd as DungeonTemplate : null;
            if (dt == null) { Debug.LogError("[HexRevival] [Campaign] " + z.Key + " template not found"); return; }

            z.NodeNumbers = new HashSet<string>();
            z.TitleNode = new Dictionary<string, string>();
            foreach (DungeonItemData it in dt.m_ItemData)
            {
                if (string.IsNullOrEmpty(it.m_MapNodeId)) continue;
                if (NodeNumber(it.m_MapNodeId) != null) z.NodeNumbers.Add(NodeNumber(it.m_MapNodeId));
                string t = (it.m_Title ?? "").Replace(" ", "").ToUpperInvariant();
                if (t.Length > 0 && !z.TitleNode.ContainsKey(t)) z.TitleNode[t] = it.m_MapNodeId;
            }
            HashSet<string> names = new HashSet<string>();
            foreach (DungeonItemData it in dt.m_ItemData)
            {
                if (string.IsNullOrEmpty(it.m_MapNodeId)) continue;
                MapNode n = new MapNode { Z = z, Id = it.m_MapNodeId };
                string title = !string.IsNullOrEmpty(it.m_Title) ? it.m_Title : it.m_Name;
                n.Name = string.IsNullOrEmpty(title) || names.Contains(title) ? n.Id : title;
                names.Add(n.Name);
                ResolveContent(n, tm);
                n.Dungeon = Dungeons.AtNode(z.Key, n.Id);
                if (n.Dungeon != null) { n.Type = "Dungeon"; n.Encounter = null; n.Conversation = null; n.RepeatConversation = null; }
                z.Nodes.Add(n);
            }
            if (z == AZ1) AddFactionNodes(z, tm);

            // Paths: the map prefab holds one object per path, named "Path_NodeA_NodeB".
            try
            {
                GameObject prefab = Resources.Load(z.NodesPrefab) as GameObject;
                if (prefab != null)
                    foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
                    {
                        string[] p = t.name.Split('_');
                        if (p.Length != 3 || p[0] != "Path") continue;
                        AddEdge(z, p[1], p[2]);
                        AddEdge(z, p[2], p[1]);
                    }
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Campaign] map paths: " + e); }
            Debug.Log("[HexRevival] [Campaign] " + z.Key + ": " + z.Nodes.Count + " nodes, " + z.Edges.Count + " connected");
        }

        /// <summary>
        /// Map nodes whose battle depends on the faction (Src.Game): the template has "Node023A/B" and
        /// "Node026A/B", the map prefab one "Node023" / "Node026"; their conversations ("Node 23 -
        /// Zombies - ARDENT", "Node 26 - Bog - Underworld player"...) are per faction. Node 23: the
        /// soldiers of the other faction surrounded by zombies, helped or not (choice_Help / choice_NoHelp).
        /// </summary>
        private static readonly Dictionary<string, string[]> FactionNodes = new Dictionary<string, string[]>
        {
            { "Node023", new[] { "Node023A", "UW:help", "AZ 1 - NODE 23A - HUMANS CHOICE HELP", "UW:nohelp", "AZ 1 - NODE 23A_2 - HUMANS NO HELP",
                "AR:help", "AZ 1 - NODE 23B - NECROTIC CHOICE HELP", "AR:nohelp", "AZ 1 - NODE 23B_2 - NECROTIC NO HELP" } },
            { "Node026", new[] { "Node026A", "UW", "AZ 1 - NODE 26 - WAYWARD ORCS", "AR", "AZ 1 - NODE 26B - WAYWARD VENNEN" } },
        };

        private static void AddFactionNodes(Zone z, TemplateManager tm)
        {
            Dictionary<string, string> scenes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (SceneData sc in tm.CampaignScenes.Values)
                if (sc is EncounterScene && sc.m_Name != null && !scenes.ContainsKey(sc.m_Name.Trim())) scenes[sc.m_Name.Trim()] = sc.m_Id.ToString();
            foreach (KeyValuePair<string, string[]> kv in FactionNodes)
            {
                if (z.Nodes.Exists(x => x.Id == kv.Key)) continue;
                MapNode a = z.Nodes.Find(x => x.Id == kv.Value[0]);
                // The template's variants are not on the map: one node replaces them.
                z.Nodes.RemoveAll(x => x.Id.StartsWith(kv.Key) && x.Id.Length == kv.Key.Length + 1);
                MapNode n = new MapNode { Z = z, Id = kv.Key, Name = a != null ? a.Name : kv.Key, Variants = new Dictionary<string, string>() };
                ResolveContent(n, tm);
                for (int i = 1; i + 1 < kv.Value.Length; i += 2)
                {
                    string id;
                    if (scenes.TryGetValue(kv.Value[i + 1], out id)) n.Variants[kv.Value[i]] = id;
                    else Debug.LogError("[HexRevival] [Campaign] " + kv.Key + ": no scene " + kv.Value[i + 1]);
                    if (id != null && n.Encounter == null) n.Encounter = id;
                }
                if (n.Encounter != null) { n.Type = "Encounter"; n.Conversation = null; }
                z.Nodes.Add(n);
            }
        }

        /// <summary>
        /// Zombie blockade at Brink Ridge (Src.Video: the Ardent champion fought a Barricade Zombie there after
        /// the Smoldering Dead; Src.Game: scenes "NODE 19B - ZOMBIE BLOCKADE", variants B1-B3 unused:
        /// PLACEHOLDER). Its encounter while it is to be fought, else null.
        /// </summary>
        private static string ZombieBlockade(ulong champ)
        {
            if (Quests.Flag(champ, "dungeon:a8d2ad0e-306e-4d07-9cd1-e4d7c82cdf1c") == null || Quests.Flag(champ, "won:Node019B") != null) return null;
            MapNode b = FindNode(AZ1, "Node019B");
            return b != null ? b.Encounter : null;
        }

        /// <summary>The node's battle for this champion (faction, dialogue choice).</summary>
        private static string EncOf(ulong champ, MapNode n)
        {
            champion_bits c = Champions.Get(champ);
            string f = c != null && c.Race.GetFaction() == Game.Shared.Mechanics.EFactions.Underworld ? "UW" : "AR";
            if (n.FactionOnly != null && n.FactionOnly != f) return null;
            if (n.Variants == null) return n.Encounter;
            string v, evt = Quests.Flag(champ, "choice:" + n.Key);
            if (evt != null && n.Variants.TryGetValue(evt, out v)) return v;   // a dialogue event (Mancubus / Succubus)
            string choice = evt == "choice_NoHelp" ? "nohelp" : "help";
            return n.Variants.TryGetValue(f + ":" + choice, out v) || n.Variants.TryGetValue(f, out v) ? v : n.Encounter;
        }

        private static void AddEdge(Zone z, string a, string b)
        {
            HashSet<string> set;
            if (!z.Edges.TryGetValue(a, out set)) z.Edges[a] = set = new HashSet<string>();
            set.Add(b);
        }

        private static string Txt(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            try { string l = Localization.TryGet(s); if (!string.IsNullOrEmpty(l)) s = l; } catch { }
            return s.Replace("\n", " ").Replace("\r", " ");
        }

        /// <summary>Type and public fields of an object (one level; lists and template ids shown by name when possible).</summary>
        private static string Describe(object o, int depth = 0)
        {
            if (o == null) return "null";
            if (depth > 3) return o.GetType().Name;
            StringBuilder sb = new StringBuilder(o.GetType().Name + " {");
            foreach (FieldInfo f in o.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object v; try { v = f.GetValue(o); } catch { continue; }
                sb.Append(" " + f.Name + "=" + Value(v, depth + 1));
            }
            return sb.Append(" }").ToString();
        }

        private static string Value(object v, int depth = 0)
        {
            if (v == null) return "null";
            if (v is string) return "\"" + v + "\"";
            if (v is ResourceId)
            {
                ResourceId id = (ResourceId)v;
                TemplateManager tm = Singleton<TemplateManager>.Instance;
                CardTemplate ct = tm.GetCardTemplate(id);
                if (ct != null) return id + "(card " + ct.m_Name + ")";
                InventoryItemData it = tm.GetInventoryItem(id);
                if (it != null) return id + "(item " + it.m_Name + ")";
                return id.ToString();
            }
            System.Collections.IEnumerable e = v as System.Collections.IEnumerable;
            if (e != null)
            {
                List<string> l = new List<string>();
                foreach (object x in e) { l.Add(x != null && x.GetType().IsClass && !(x is string) && !(x is ResourceId) ? Describe(x, depth + 1) : Value(x, depth + 1)); if (l.Count > 20) break; }
                return "[" + string.Join(", ", l.ToArray()) + "]";
            }
            return v.ToString();
        }

        /// <summary>End-of-battle events published by the game's rules (Src.Game: HandleGameplayChallenges), e.g. "savedprincess", "tamed".</summary>
        public static readonly HashSet<string> BattleEvents = new HashSet<string>();
        private static bool s_FortuneApplied;

        /// <summary>StandaloneSession.PublishEvent (Harmony prefix): records the event of the battle in progress.</summary>
        public static void OnPublishEvent(string key)
        {
            if (key == null) return;
            BattleEvents.Add(key);
            Debug.Log("[HexRevival] [Campaign] battle event " + key);
        }

        /// <summary>Resources the AI starts with in play in the next campaign battle (Army of Myth difficulty).</summary>
        private static int s_Difficulty;

        /// <summary>Called on every server update (Server.Flush): applies the difficulty once the mulligan is over.</summary>
        public static void OnSessionTick(StandaloneSession ss)
        {
            if (ss == null || (int)ss.CurrentTurnPhase <= (int)Game.Shared.Mechanics.ETurnPhases.Mulligan) return;
            if (s_BattleChamp != 0 && !s_FortuneApplied) { s_FortuneApplied = true; Fortunes.Apply(ss, s_BattleChamp); }
            if (s_BattleChamp != 0) TickShip(ss);
            if (s_Difficulty <= 0) return;
            Player ai = Cheats.FindPlayer(ss, "ai");
            if (ai != null)
            {
                // "starts the game with N resources in play": resources only, no shard thresholds (checked in game by the user)
                Cheats.AddResourcesOnly(ss, ai, s_Difficulty);
                Debug.Log("[HexRevival] [Campaign] difficulty: AI starts with " + s_Difficulty + " resource(s)");
            }
            s_Difficulty = 0;
        }

        /// <summary>Last campaign event sent by the client (e.g. the conversation choice "Diff_6" of the Army of Myth).</summary>
        public static string LastEvent;

        /// <summary>True when the node is a battle (its rewards come with the win, not with a conversation).</summary>
        // ------------------------------------------------------------------
        // Conversation shown by a node (quest steps, faction variants, battle outcomes)
        // ------------------------------------------------------------------

        private static readonly Dictionary<string, List<ConversationTemplate>> s_NodeConvs = new Dictionary<string, List<ConversationTemplate>>();

        /// <summary>Every conversation of a node ("AZ1 - Node 012 - ...").</summary>
        private static List<ConversationTemplate> NodeConvs(MapNode n)
        {
            List<ConversationTemplate> l;
            if (s_NodeConvs.TryGetValue(n.Key, out l)) return l;
            l = new List<ConversationTemplate>();
            string number = NumberOf(n);
            if (number != null)
                foreach (ConversationTemplate c in Singleton<TemplateManager>.Instance.ConversationTemplates.Values)
                    if (c != null && n.Z.Matches(NameNumber(c.m_Name, n.Z.Prefix), number)) l.Add(c);
            s_NodeConvs[n.Key] = l;
            return l;
        }

        private static string ConvName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            ConversationTemplate c = Singleton<TemplateManager>.Instance.GetConversationTemplate(new ResourceId(id));
            return c == null ? "?" : c.m_Name;
        }

        /// <summary>Conversations shown after a battle (success / fail) or for repeats.</summary>
        private static bool IsOutcome(string name)
        {
            string u = name.ToUpperInvariant();
            return u.Contains("SUCCESS") || u.Contains("FAIL") || u.Contains("REPEAT ENCOUNTERS");
        }

        /// <summary>Conversation of a non-battle node for this champion: quest step rules first, else the best matching one.</summary>
        private static string NodeConversation(ulong champ, MapNode n)
        {
            List<KeyValuePair<string, string>> convs = new List<KeyValuePair<string, string>>();
            foreach (ConversationTemplate c in NodeConvs(n))
                if (Quests.MatchesChampion(c.m_Name, champ)) convs.Add(new KeyValuePair<string, string>(c.m_Name, c.m_Id.ToString()));
            string picked = NodeScripts.Pick(champ, n.Key, convs);
            if (picked != null) return picked;
            ConversationTemplate best = null;
            foreach (ConversationTemplate c in NodeConvs(n))
                if (Quests.MatchesChampion(c.m_Name, champ) && !IsOutcome(c.m_Name) && (best == null || ConversationRank(c.m_Name) < ConversationRank(best.m_Name))) best = c;
            return best == null ? n.Conversation : best.m_Id.ToString();
        }

        /// <summary>
        /// Conversations that leave the way closed (Src.Game dialogues): bridges whose keeper waits for
        /// the quest, the Blood Gate without its key, the Masked Army without a faction quest, the Brink
        /// Ridge blockade unless the player sneaked past or paid.
        /// </summary>
        private static bool IsGate(ulong champ, MapNode n, string convId)
        {
            string u = ConvName(convId).ToUpperInvariant();
            if (u.Contains("BLOCKADE - STEP"))
            {
                string choice = Quests.Flag(champ, "choice:" + n.Key);
                return choice != "choice_sneak" && choice != "choice_Bribe";
            }
            return (u.Contains("STEP 1B") && u.Contains("NOT BEATEN")) || u.Contains("STEP 1 - LOCKED") || u.Contains("MASKED ARMY - STEP 1");
        }

        /// <summary>Conversation shown before a node's battle (null if none).</summary>
        private static string Prelude(ulong champ, MapNode n)
        {
            if (NodeScripts.HasRules(n.Key)) return NodeConversation(champ, n);
            ConversationTemplate best = null;
            foreach (ConversationTemplate c in NodeConvs(n))
                if (Quests.MatchesChampion(c.m_Name, champ) && !IsOutcome(c.m_Name) && Quests.RoleOf(c.m_Name) == Quests.Role.None
                    && (best == null || ConversationRank(c.m_Name) < ConversationRank(best.m_Name))) best = c;
            return best != null ? best.m_Id.ToString() : null;
        }

        /// <summary>Conversation shown after a node's battle ("... Success" / "... Fail", by the choice made before it).</summary>
        private static string Outcome(ulong champ, MapNode n, bool won)
        {
            string choice = Quests.Flag(champ, "choice:" + n.Key);
            ConversationTemplate best = null;
            int bestScore = -1;
            foreach (ConversationTemplate c in NodeConvs(n))
            {
                string u = c.m_Name.ToUpperInvariant();
                if (!Quests.MatchesChampion(c.m_Name, champ) || !(won ? u.Contains("SUCCESS") : u.Contains("FAIL"))) continue;
                int score = 1;
                // Harp Rock: the outcome of the demon fought (dialogue event "swaptosuccubus" / "swaptomancubus")
                string fought = choice != null && choice.StartsWith("swapto") ? choice.Substring(6).ToUpperInvariant() : n.Variants != null && n.Variants.ContainsKey("swaptomancubus") ? "MANCUBUS" : null;
                if (fought != null) score = u.Contains(fought) ? 3 : 0;
                if (u.Contains("CHOSE NOT")) score = choice == "choice_NoHelp" ? 3 : 0;
                else if (u.Contains("CHOSE TO")) score = choice == "choice_Help" ? 3 : choice == null ? 2 : 0;
                if (score > bestScore) { bestScore = score; best = c; }
            }
            return best != null ? best.m_Id.ToString() : null;
        }

        /// <summary>The map node of a key ("Node019" = AZ1, "AZ2/Node019").</summary>
        private static MapNode NodeOfKey(string key)
        {
            int i = key.IndexOf('/');
            return i < 0 ? FindNode(AZ1, key) : FindNode(ZoneByKey(key.Substring(0, i)), key.Substring(i + 1));
        }

        public static bool NodeHasBattle(string node)
        {
            MapNode n = NodeOfKey(node);
            return n != null && n.Encounter != null;
        }

        /// <summary>Names and titles of the scenes of a map node ("" if unknown).</summary>
        public static string NodeScenes(string node)
        {
            MapNode n = NodeOfKey(node);
            return n != null ? n.Scenes : "";
        }

        private static IEnumerable<string> Neighbours(Zone z, string node)
        {
            HashSet<string> set;
            BuildMap(z);
            return z.Edges.TryGetValue(node, out set) ? (IEnumerable<string>)set : new string[0];
        }

        /// <summary>
        /// Content of a map node, from the game's templates named after the node: encounter scenes
        /// "AZ 1 - NODE 03 - WILD CUB" and conversations "AZ1 - Node 3 - Feralroot Bear".
        /// A scene with an AI deck is a battle; otherwise the node's first conversation is used
        /// (dialog, panorama, dungeon and reward scenes are not handled yet: the node is empty).
        /// </summary>
        private static void ResolveContent(MapNode n, TemplateManager tm)
        {
            string number = NumberOf(n);
            if (number == null) return;
            foreach (SceneData s in tm.CampaignScenes.Values)
            {
                EncounterScene es = s as EncounterScene;
                if (es != null && SceneBelongs(n, es, number)) n.Scenes += es.m_Name + " / " + es.m_Title + " | ";
            }
            // Brink Ridge: the blockade's battle scene is "NODE 19A" (Src.Game).
            if (n.Z == AZ1 && n.Id == "Node019")
                foreach (SceneData s in tm.CampaignScenes.Values)
                    if (s is EncounterScene && s.m_Name != null && s.m_Name.Replace(" ", "").ToUpperInvariant().StartsWith("AZ1-NODE19A-")) n.FightEncounter = s.m_Id.ToString();
            foreach (ConversationTemplate c in tm.ConversationTemplates.Values)
                if (c != null && n.Z.Matches(NameNumber(c.m_Name, n.Z.Prefix), number) && c.m_Name.ToUpperInvariant().Contains("REPEAT ENCOUNTERS") && !NodeScripts.HasRules(n.Key)) n.RepeatConversation = c.m_Id.ToString();
            foreach (SceneData s in tm.CampaignScenes.Values)
            {
                EncounterScene es = s as EncounterScene;
                if (es != null && SceneBelongs(n, es, number) && es.m_Name.ToUpperInvariant().Contains("PANORAMA") && n.Z.Panoramas.ContainsKey(n.Id))
                {
                    n.Panorama = es.m_Id.ToString();
                    n.Type = "Panorama";
                    return;
                }
            }
            if (n.Z != AZ1 && n.Z.Panoramas.ContainsKey(n.Id))
            {
                // AZ2 hubs have no panorama scene: the location only needs its type.
                n.Panorama = Guid.Empty.ToString();
                n.Type = "Panorama";
                return;
            }
            List<EncounterScene> battles = new List<EncounterScene>();
            foreach (SceneData s in tm.CampaignScenes.Values)
            {
                EncounterScene es = s as EncounterScene;
                if (es == null || !SceneBelongs(n, es, number) || !OfflineMenu.IsBattle(es, tm)) continue;
                battles.Add(es);
                if (n.Z == AZ1) break;
            }
            if (battles.Count > 0)
            {
                n.Type = "Encounter";
                n.Encounter = battles[0].m_Id.ToString();
                if (battles.Count == 1 && n.Z != AZ1)
                {
                    string u = battles[0].m_Name.ToUpperInvariant();
                    if (u.Contains("(VS") && u.Contains("ARDENT")) n.FactionOnly = "AR";
                    else if (u.Contains("(VS") && u.Contains("UNDERWORLD")) n.FactionOnly = "UW";
                }
                if (battles.Count > 1)
                {
                    // AZ2 variants (Src.Game names): "(vs Ardent)" is the Ardent champion's battle, "(vs. Underworld)" or
                    // "- U (Secret)" the Underworld's; Harp Rock's Mancubus / Succubus swap by a dialogue event.
                    n.Variants = new Dictionary<string, string>();
                    foreach (EncounterScene es in battles)
                    {
                        string u = es.m_Name.ToUpperInvariant();
                        string key = u.Contains("ARDENT") ? "AR" : u.Contains("UNDERWORLD") || u.Contains("- U (") ? "UW"
                            : u.Contains("SUCCUBUS") ? "swaptosuccubus" : u.Contains("MANCUBUS") ? "swaptomancubus" : null;
                        if (key != null && !n.Variants.ContainsKey(key)) n.Variants[key] = es.m_Id.ToString();
                    }
                    // the faction-less one (e.g. "NODE 56 ... (Secret)") is the other faction's
                    foreach (EncounterScene es in battles)
                    {
                        string u = es.m_Name.ToUpperInvariant();
                        if (u.Contains("ARDENT") || u.Contains("UNDERWORLD") || u.Contains("- U (") || u.Contains("CUBUS")) continue;
                        if (n.Variants.ContainsKey("UW") && !n.Variants.ContainsKey("AR")) n.Variants["AR"] = es.m_Id.ToString();
                        else if (n.Variants.ContainsKey("AR") && !n.Variants.ContainsKey("UW")) n.Variants["UW"] = es.m_Id.ToString();
                    }
                    if (n.Variants.ContainsKey("swaptomancubus")) n.Encounter = n.Variants["swaptomancubus"];
                }
                return;
            }
            ConversationTemplate best = null;
            foreach (ConversationTemplate c in tm.ConversationTemplates.Values)
            {
                if (c == null || !n.Z.Matches(NameNumber(c.m_Name, n.Z.Prefix), number)) continue;
                if (best == null || ConversationRank(c.m_Name) < ConversationRank(best.m_Name)) best = c;
            }
            if (best != null)
            {
                n.Type = "Convo";
                n.Conversation = best.m_Id.ToString();
            }
        }

        /// <summary>
        /// Whether an encounter scene is the node's: by the number in its name, except in AZ2 where the
        /// location named in it decides ("AZ 2 - NODE 8 - George Gorge - Flock of Seagulls" is George
        /// Gorge's, Node004: Src.Game numbering slip).
        /// </summary>
        private static bool SceneBelongs(MapNode n, EncounterScene es, string number)
        {
            if (n.Z != AZ1 && es.m_Name != null)
            {
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(es.m_Name, @"NODE\s*\w+\s*-\s*([^-(]+?)\s+-");
                string owner;
                if (m.Success && n.Z.TitleNode.TryGetValue(m.Groups[1].Value.Replace(" ", "").ToUpperInvariant(), out owner)) return owner == n.Id;
            }
            return n.Z.Matches(NameNumber(es.m_Name, n.Z.Prefix), number);
        }

        /// <summary>Opening conversation first: not a success/fail/repeat variant, step 1 before the others.</summary>
        private static int ConversationRank(string name)
        {
            string s = name.ToUpperInvariant();
            if (s.Contains("SUCCESS") || s.Contains("FAIL") || s.Contains("REPEAT") || s.Contains("NOT COMPLETE") || s.Contains("END")) return 2;
            if (s.Contains("STEP") && !s.Contains("STEP 1")) return 1;
            return 0;
        }

        /// <summary>"Node003" -> "3", "Node00R" -> "R", "Node019B" -> "19B"; null if not a "Node" id.</summary>
        private static string NodeNumber(string id)
        {
            if (!id.StartsWith("Node") || id.Length <= 4) return null;
            string s = NormNumber(id.Substring(4));
            return s.Length > 0 ? s : null;
        }

        /// <summary>Leading zeros removed, also inside ("019B" -> "19B", "B01" -> "B1", "B0X" kept).</summary>
        private static string NormNumber(string s)
        {
            return System.Text.RegularExpressions.Regex.Replace(s.TrimStart('0'), "(?<![0-9])0+(?=[0-9])", "");
        }

        /// <summary>Number of a node in the names of its scenes and conversations (some AZ2 nodes are named otherwise).</summary>
        private static string NumberOf(MapNode n)
        {
            string alias;
            return n.Z.NumberAlias != null && n.Z.NumberAlias.TryGetValue(n.Id, out alias) ? alias : NodeNumber(n.Id);
        }

        /// <summary>Node number in a template name: "AZ 1 - NODE 03 - X" -> "3", "AZ 1 - NODE - 18 - X" -> "18".</summary>
        private static string NameNumber(string name, string prefix)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string s = name.Replace(" ", "").ToUpperInvariant();
            if (!s.StartsWith(prefix)) return null;
            s = s.Substring(prefix.Length).TrimStart('-');
            int end = s.IndexOf('-');
            if (end >= 0) s = s.Substring(0, end);
            s = NormNumber(s);
            return s.Length > 0 ? s : null;
        }

        // ------------------------------------------------------------------
        // Campaign state (one per champion)
        // ------------------------------------------------------------------

        private class CampState
        {
            public Zone Z = AZ1;
            public ulong ChampId;
            public bool Started;
            public string ALoc;                 // active location (name), null when exploring
            public string LastNode;
            public HashSet<string> Completed = new HashSet<string>();
            public HashSet<string> Visible = new HashSet<string>();
            public List<string> VisitedNodes = new List<string>();
            public List<string> VisitedPaths = new List<string>();
            public HashSet<string> HausReady = new HashSet<string>(); // Shroom Hauses whose card choice window is open (after the Shroomkin's conversation)
            public ulong CampId { get { return Z.Base + ChampId; } } // one campaign per champion and zone
        }

        private static Dictionary<ulong, CampState> s_States;
        private static CampState s_Battle; // campaign whose encounter is being played

        /// <summary>Champion playing the current campaign battle (0 outside campaign battles).</summary>
        public static ulong BattleChampion { get { return s_BattleChamp; } }
        private static ulong s_BattleChamp;

        private static string FilePath { get { return Saves.File("campaign.txt"); } }

        /// <summary>Forgets the loaded campaigns (another save was selected).</summary>
        public static void Reset()
        {
            s_Pano.Clear();
            Quests.Reset();
            Dungeons.Reset();
            s_States = null;
            s_Battle = null;
        }

        /// <summary>Cheat: every node of the map becomes visible and selectable.</summary>
        public static bool RevealAll(ulong champId)
        {
            CampState st = GetChamp(champId, false);
            if (st == null) return false;
            BuildMap(st.Z);
            foreach (MapNode n in st.Z.Nodes) st.Visible.Add(n.Id);
            Save();
            return true;
        }

        /// <summary>True once the champion has done something on the AZ1 map (beyond its first node).</summary>
        public static bool HasProgress(ulong champId)
        {
            LoadAll();
            CampState st;
            return s_States.TryGetValue(champId, out st) && (st.Completed.Count > 1 || st.VisitedNodes.Count > 1);
        }

        /// <summary>True if this champion has a saved campaign (used for LastCampaignID).</summary>
        public static bool HasSave(ulong champId)
        {
            LoadAll();
            return s_States.ContainsKey(champId);
        }

        /// <summary>State of an AREA campaign (id = zone base + champion).</summary>
        private static CampState Get(ulong campId, bool create)
        {
            LoadAll();
            CampState st;
            Zone z = ZoneOf(campId);
            if (z == null) return null;
            if (s_States.TryGetValue(campId, out st) || !create) return st;
            BuildMap(z);
            st = new CampState { Z = z, ChampId = campId - z.Base, LastNode = z.StartNode };
            st.Completed.Add(z.StartNode);
            st.VisitedNodes.Add(z.StartNode);
            Reveal(st, z.StartNode);
            s_States[campId] = st;
            Save();
            return st;
        }

        /// <summary>Zone the champion is in (AZ1 until it sails to AZ2).</summary>
        internal static Zone CurrentZone(ulong champ) { return ZoneByKey(Quests.Flag(champ, "zone") ?? AZ1.Key); }

        /// <summary>The champion's campaign in its current zone.</summary>
        private static CampState GetChamp(ulong champ, bool create) { return Get(CurrentZone(champ).Base + champ, create); }

        /// <summary>A node and its neighbours become visible (and selectable).</summary>
        private static void Reveal(CampState st, string node)
        {
            st.Visible.Add(node);
            foreach (string n in Neighbours(st.Z, node)) st.Visible.Add(n);
        }

        private static void Complete(CampState st, MapNode node)
        {
            st.Completed.Add(node.Id);
            Reveal(st, node.Id);
        }

        // File: one line per champion:
        // champId|started|lastNode|completed,..|visible,..|visitedNodes,..|visitedPaths,..|hausReady,..
        private static void LoadAll()
        {
            if (s_States != null) return;
            s_States = new Dictionary<ulong, CampState>();
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] p = line.Split('|');
                    if (p.Length < 7) continue;
                    ulong campId = ulong.Parse(p[0]);
                    Zone z = ZoneOf(campId) ?? AZ1;
                    CampState st = new CampState { Z = z, ChampId = campId - z.Base, Started = p[1] == "1", LastNode = p[2] };
                    st.Completed.UnionWith(SplitList(p[3]));
                    st.Visible.UnionWith(SplitList(p[4]));
                    st.VisitedNodes.AddRange(SplitList(p[5]));
                    st.VisitedPaths.AddRange(SplitList(p[6]));
                    if (p.Length > 7) st.HausReady.UnionWith(SplitList(p[7]));
                    s_States[st.CampId] = st;
                }
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Campaign] load: " + e); }
        }

        private static string[] SplitList(string s) { return s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries); }

        private static void Save()
        {
            try
            {
                List<string> lines = new List<string>();
                foreach (CampState st in s_States.Values)
                    lines.Add(string.Join("|", new[] { st.CampId.ToString(), st.Started ? "1" : "0", st.LastNode,
                        string.Join(",", new List<string>(st.Completed).ToArray()), string.Join(",", new List<string>(st.Visible).ToArray()),
                        string.Join(",", st.VisitedNodes.ToArray()), string.Join(",", st.VisitedPaths.ToArray()),
                        string.Join(",", new List<string>(st.HausReady).ToArray()) }));
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Campaign] save: " + e); }
        }

        // ------------------------------------------------------------------
        // GameplayState sent to the client
        // ------------------------------------------------------------------

        private static GameplayState BuildState(CampState st)
        {
            BuildMap(st.Z);
            GameplayState gs = new GameplayState();
            gs.CampID = st.CampId;
            gs.ChampID = st.ChampId;
            gs.TempType = "AREA";
            gs.PayGroups = new PayableGroup[0];
            gs.ALoc = st.ALoc;
            gs.LastNode = st.LastNode;
            gs.CurState = "EXPLORE";
            gs.Started = st.Started ? "2020-01-01T00:00:00Z" : null;
            gs.Champions = new List<Champion>();
            gs.Flags = new Dictionary<string, object>();
            gs.VisLocs = new List<Location>();
            gs.LocNodes = new List<Node>();
            gs.Encounters = new List<Game.Shared.Eval.Data.Encounter>();
            foreach (MapNode n in st.Z.Nodes)
            {
                bool visible = st.Visible.Contains(n.Id);
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["name"] = n.Name;
                d["node"] = n.Id;
                bool repeat = n.RepeatConversation != null && st.Completed.Contains(n.Id);
                string battle = EncOf(st.ChampId, n);
                d["type"] = repeat || (n.Type == "Encounter" && battle == null) ? "Convo" : n.Type;
                d["visible"] = visible;
                d["enabled"] = visible;
                // A completed node cannot be started again by the client: repeatable nodes are sent as not completed (blue marker).
                d["completed"] = st.Completed.Contains(n.Id) && !repeat;
                d["repeatable"] = repeat;
                d["allow_cancel"] = true;
                if (battle != null) d["encounter"] = battle;
                if (repeat) d["conversationId"] = n.RepeatConversation;
                else if (n.Conversation != null) d["conversationId"] = n.Conversation;
                string outcome = n.Panorama == null && !n.IsHaus ? Quests.Flag(st.ChampId, "outcome:" + n.Key) : null;
                if (outcome != null || (!repeat && n.Panorama == null && !n.IsHaus))
                {
                    if (outcome != null)
                    {
                        // Battle just ended: its success / fail conversation opens by itself.
                        d["type"] = "Convo";
                        d["conversationId"] = outcome;
                        d["completed"] = false;
                        d["autostart"] = true;
                    }
                    else if (battle != null)
                    {
                        // Battle node: its conversation first, then (prelude seen) the battle starts by itself.
                        string prelude = !st.Completed.Contains(n.Id) ? Prelude(st.ChampId, n) : null;
                        if (prelude != null && Quests.Flag(st.ChampId, "prelude:" + n.Key) == null)
                        {
                            d["type"] = "Convo";
                            d["conversationId"] = prelude;
                        }
                        else if (prelude != null) d["autostart"] = true;
                    }
                    else
                    {
                        string conv = NodeConversation(st.ChampId, n);
                        if (conv != null) d["conversationId"] = conv;
                        if (NodeScripts.HasRules(n.Key) && st.Completed.Contains(n.Id))
                        {
                            d["completed"] = false;
                            d["repeatable"] = true;
                        }
                    }
                }
                if (n.Panorama != null)
                {
                    // Hub: a reusable portal to its panorama (never completed, Src.Game location type Panorama).
                    d["type"] = "Panorama";
                    d["encounter"] = n.Panorama;
                    d.Remove("conversationId");
                    d["completed"] = false;
                    d["repeatable"] = true;
                }
                if (n.Dungeon != null)
                {
                    // Dungeon entrance: "start" enters the dungeon's own map (see Dungeons), as often as wanted.
                    d["type"] = "Dungeon";
                    d.Remove("conversationId");
                    d.Remove("autostart");
                    d["completed"] = false;
                    d["repeatable"] = st.Completed.Contains(n.Id);
                    d["event"] = "start";
                }
                string zombies = n.Z == AZ1 && n.Id == "Node019" ? ZombieBlockade(st.ChampId) : null;
                if (zombies != null)
                {
                    // Brink Ridge after the Smoldering Dead: zombies hold the barricade (one battle).
                    d["type"] = "Encounter";
                    d["encounter"] = zombies;
                    d.Remove("conversationId");
                    d.Remove("autostart");
                    d["completed"] = false;
                    d["repeatable"] = false;
                    gs.Encounters.Add(new Game.Shared.Eval.Data.Encounter(zombies, new Dictionary<string, object> { { "encscene", zombies } }));
                }
                if (n.IsHaus && st.HausReady.Contains(n.Id) && !st.Completed.Contains(n.Id))
                {
                    // The game's card choice window (Src.Game, as hex-server serves it): 3 card templates to pick from.
                    d["type"] = "ShroomHaus";
                    d.Remove("conversationId");
                    d["autostart"] = true;
                    d["choices"] = new List<object>(Progression.ShroomHausChoices(n.Key).ToArray());
                    d["selection-type"] = "Shroomkin_Haus";
                    d["selection-desc"] = "Shroomkin_Instructions";
                }
                ShipLocation(st, n, d);
                gs.VisLocs.Add(new Location(d));
                Dictionary<string, object> nd = new Dictionary<string, object>();
                nd["id"] = n.Id;
                nd["type"] = "DEFAULT";
                gs.LocNodes.Add(new Node(n.Id, nd));
                if (battle != null)
                {
                    Dictionary<string, object> ed = new Dictionary<string, object>();
                    ed["encscene"] = battle;
                    gs.Encounters.Add(new Game.Shared.Eval.Data.Encounter(battle, ed));
                }
            }
            Dictionary<string, object> pub = new Dictionary<string, object>();
            pub["CampaignGroup"] = st.Z.Key;
            pub["visited_nodes"] = new List<object>(st.VisitedNodes.ToArray());
            pub["visited_paths"] = new List<object>(st.VisitedPaths.ToArray());
            pub["quest_nodes"] = new List<object>();
            AddShipState(st, gs, pub);
            gs.PublicState = new QueryState(pub);
            return gs;
        }

        private static CampSummary Summary(CampState st)
        {
            TemplateTypeInfo info = new TemplateTypeInfo(st.Z.Key, "AREA", null, false, false, "", "",
                st.Z.BackgroundPrefab, st.Z.NodesPrefab, st.Z.TemplateId.ToString());
            return new CampSummary(st.CampId, info, CampaignType.AREA, GameClient.Instance.PlayerProfile.ReckoningID, st.Z.Key, true, 1, null);
        }

        // ------------------------------------------------------------------
        // Answers (delivered on the next frame, like a network answer)
        // ------------------------------------------------------------------

        // Answers are delivered a few frames later, like network answers: the map screen asks for
        // the state in the same frame it creates the map nodes, which are only initialized by
        // Unity on the next frame (UIDungeonMapNode.Start).
        private const int AnswerDelayFrames = 5;
        private static readonly List<KeyValuePair<int, Action>> s_Answers = new List<KeyValuePair<int, Action>>();

        internal static void Enqueue(Action a) { if (QuestSim.Active) return; s_Answers.Add(new KeyValuePair<int, Action>(Time.frameCount + AnswerDelayFrames, a)); }

        /// <summary>Called every frame (main thread).</summary>
        public static void Pump()
        {
            while (s_Answers.Count > 0 && s_Answers[0].Key <= Time.frameCount)
            {
                Action a = s_Answers[0].Value;
                s_Answers.RemoveAt(0);
                try { a(); }
                catch (Exception e) { Debug.LogError("[HexRevival] [Campaign] answer: " + e); }
            }
        }

        /// <summary>Same handling as ClientCampaignManager.HandleInputResponse for a successful answer.</summary>
        private static void Answer(CampState st, ClientCampaignManager.StateCallback callback)
        {
            if (st == null) return;
            GameplayState gs = BuildState(st);
            Enqueue(() =>
            {
                MethodInfo cache = typeof(ClientCampaignManager).GetMethod("CacheCampaignState", BindingFlags.Static | BindingFlags.NonPublic);
                cache.Invoke(null, new object[] { st.CampId, gs });
                if (callback != null) callback(gs);
            });
        }

        // ------------------------------------------------------------------
        // Hub panoramas (Cave-In, Ambling Mesa, Thunderfield): the game's panorama scenes,
        // whose NPCs are quest givers. One PANORAMA campaign per champion (id PanoBase + champion).
        // ------------------------------------------------------------------

        private const ulong PanoBase = 1000000;
        private static bool IsPanoCamp(ulong id) { return id >= PanoBase && id < Dungeons.DunBase; }

        private class PanoState { public Zone Z; public string Node; public string ALoc; }
        private static readonly Dictionary<ulong, PanoState> s_Pano = new Dictionary<ulong, PanoState>();

        private class Npc { public string Name; public string ConvId; public string ConvName; public bool Give, TurnIn, Repeat; public string Champion; }

        private static void EnterPanorama(CampState st, MapNode node)
        {
            s_Pano[st.ChampId] = new PanoState { Z = st.Z, Node = node.Id };
            st.ALoc = null;
            st.LastNode = node.Id;
            Save();
            Debug.Log("[HexRevival] [Campaign] enter panorama " + node.Name);
            GameplayState gs = BuildPanoState(st.ChampId);
            Enqueue(() => Quests.Invoke("HandleOnNotifyCampUpdate", new CampSysGeneral.Any.CampUpdateNotify(0, PanoBase + st.ChampId, st.ChampId, "area_panorama", "PANORAMA", true, gs, null)));
        }

        private static void LeavePanorama(ulong champ)
        {
            s_Pano.Remove(champ);
            CampState st = GetChamp(champ, false);
            if (st == null) return;
            GameplayState gs = BuildState(st);
            Enqueue(() => Quests.Invoke("HandleOnNotifyCampUpdate", new CampSysGeneral.Any.CampUpdateNotify(0, st.CampId, champ, "return_parent", "AREA", true, gs, null)));
        }

        /// <summary>Conversation each NPC of the hub offers now, from the quest progress.</summary>
        private static List<Npc> HubNpcs(ulong champ, Zone z, string node)
        {
            string number = NodeNumber(node);
            Dictionary<string, List<ConversationTemplate>> byNpc = new Dictionary<string, List<ConversationTemplate>>();
            EFactions hubFaction;
            champion_bits cb = Champions.Get(champ);
            if (z.HubFaction != null && z.HubFaction.TryGetValue(node, out hubFaction) && cb != null && cb.Race.GetFaction() != hubFaction) return new List<Npc>();
            foreach (ConversationTemplate c in Singleton<TemplateManager>.Instance.ConversationTemplates.Values)
            {
                if (c == null || string.IsNullOrEmpty(c.m_OwnerName) || !z.Matches(NameNumber(c.m_Name, z.Prefix), number)) continue;
                if (!Quests.MatchesChampion(c.m_Name, champ)) continue;
                List<ConversationTemplate> l;
                if (!byNpc.TryGetValue(c.m_OwnerName, out l)) byNpc[c.m_OwnerName] = l = new List<ConversationTemplate>();
                l.Add(c);
            }
            List<Npc> res = new List<Npc>();
            foreach (KeyValuePair<string, List<ConversationTemplate>> kv in byNpc)
            {
                ConversationTemplate pick = null; int rank = 99; bool give = false, turnIn = false;
                bool anyFinished = false;
                foreach (ConversationTemplate c in kv.Value)
                {
                    string q = Quests.QuestOf(c.m_Name);
                    if (q != null && Quests.IsFinished(champ, q)) anyFinished = true;
                }
                // Order: turn-in, new quest, quest in progress, finished quest, NPC's generic repeat.
                foreach (ConversationTemplate c in kv.Value)
                {
                    string q = Quests.QuestOf(c.m_Name);
                    Quests.Role r = Quests.RoleOf(c.m_Name);
                    int cr = 99; bool g = false, t = false;
                    if (q != null)
                    {
                        bool started = Quests.IsStarted(champ, q), finished = Quests.IsFinished(champ, q);
                        if (r == Quests.Role.End && Quests.TurnInReady(champ, q, c.m_Id.ToString())) { cr = 0; t = true; }
                        else if (r == Quests.Role.Start && !started && Quests.CanStart(champ, q)) { cr = 1; g = true; }
                        else if (r == Quests.Role.NotComplete && started && !finished) cr = 2;
                        else if (r == Quests.Role.Repeat && finished) cr = 3;
                    }
                    else if (r == Quests.Role.Repeat && anyFinished) cr = 4;
                    if (cr < rank) { rank = cr; pick = c; give = g; turnIn = t; }
                }
                if (pick == null) continue;
                res.Add(new Npc { Name = kv.Key, ConvId = pick.m_Id.ToString(), ConvName = pick.m_Name, Give = give, TurnIn = turnIn, Repeat = rank >= 2, Champion = pick.m_ChampionId.IsValid ? pick.m_ChampionId.ToString() : null });
            }
            return res;
        }

        private static GameplayState BuildPanoState(ulong champ)
        {
            PanoState ps;
            s_Pano.TryGetValue(champ, out ps);
            GameplayState gs = new GameplayState();
            gs.CampID = PanoBase + champ;
            gs.ChampID = champ;
            gs.TempType = "PANORAMA";
            gs.PayGroups = new PayableGroup[0];
            gs.ALoc = ps != null ? ps.ALoc : null;
            gs.CurState = "EXPLORE";
            gs.Started = "2020-01-01T00:00:00Z";
            gs.Champions = new List<Champion>();
            gs.Flags = new Dictionary<string, object>();
            gs.VisLocs = new List<Location>();
            gs.LocNodes = new List<Node>();
            gs.Encounters = new List<Game.Shared.Eval.Data.Encounter>();
            if (ps != null)
                foreach (Npc n in HubNpcs(champ, ps.Z, ps.Node))
                {
                    Dictionary<string, object> d = new Dictionary<string, object>();
                    d["name"] = n.Name; d["node"] = n.Name; d["type"] = "Convo"; d["conversationId"] = n.ConvId;
                    d["visible"] = true; d["enabled"] = true; d["completed"] = false; d["repeatable"] = n.Repeat;
                    d["givequest"] = n.Give; d["turninquest"] = n.TurnIn; d["autostart"] = false; d["autopan"] = false;
                    gs.VisLocs.Add(new Location(d));
                    Dictionary<string, object> nd = new Dictionary<string, object> { { "id", n.Name }, { "type", "DEFAULT" } };
                    if (n.Champion != null) nd["championId"] = n.Champion;
                    gs.LocNodes.Add(new Node(n.Name, nd));
                }
            gs.PublicState = new QueryState(new Dictionary<string, object> { { "CampaignGroup", ps != null ? ps.Z.Key : AZ1.Key }, { "HideQuickNavigation", false } });
            return gs;
        }

        private static CampSummary PanoSummary(ulong champ)
        {
            PanoState ps;
            string[] a = s_Pano.TryGetValue(champ, out ps) && ps.Z.Panoramas.ContainsKey(ps.Node) ? ps.Z.Panoramas[ps.Node] : new[] { "", "" };
            string zone = ps != null ? ps.Z.Key : AZ1.Key;
            TemplateTypeInfo info = new TemplateTypeInfo(zone, "PANORAMA", null, false, false, a[0], a[1],
                "campaign/tutorial/prefabs/background1", "", "2f59b729-7cdf-4fb4-abc3-5654a644df65");
            return new CampSummary(PanoBase + champ, info, CampaignType.PANORAMA, GameClient.Instance.PlayerProfile.ReckoningID, zone, true, 1, null);
        }

        private static void AnswerPano(ulong champ, ClientCampaignManager.StateCallback callback)
        {
            GameplayState gs = BuildPanoState(champ);
            Enqueue(() =>
            {
                MethodInfo cache = typeof(ClientCampaignManager).GetMethod("CacheCampaignState", BindingFlags.Static | BindingFlags.NonPublic);
                cache.Invoke(null, new object[] { gs.CampID, gs });
                if (callback != null) callback(gs);
            });
        }

        private static void AnswerQuest(ulong campId, ClientCampaignManager.StateCallback callback)
        {
            GameplayState gs = Quests.State(campId);
            if (gs == null) return;
            Enqueue(() =>
            {
                MethodInfo cache = typeof(ClientCampaignManager).GetMethod("CacheCampaignState", BindingFlags.Static | BindingFlags.NonPublic);
                cache.Invoke(null, new object[] { campId, gs });
                if (callback != null) callback(gs);
            });
        }

        /// <summary>Caches a state and calls back (a few frames later, like a network answer).</summary>
        internal static void Deliver(ulong campId, GameplayState gs, ClientCampaignManager.StateCallback callback)
        {
            Enqueue(() =>
            {
                MethodInfo cache = typeof(ClientCampaignManager).GetMethod("CacheCampaignState", BindingFlags.Static | BindingFlags.NonPublic);
                cache.Invoke(null, new object[] { campId, gs });
                if (callback != null) callback(gs);
            });
        }

        /// <summary>Server notification moving the client to another campaign (AREA, PANORAMA, DUNGEON).</summary>
        internal static void Notify(ulong campId, ulong champ, string reason, string type, GameplayState gs)
        {
            Enqueue(() => Quests.Invoke("HandleOnNotifyCampUpdate", new CampSysGeneral.Any.CampUpdateNotify(0, campId, champ, reason, type, true, gs, null)));
        }

        /// <summary>Back to a zone's map from a dungeon, on the dungeon's node (or the map's start).</summary>
        internal static void ReturnToArea(ulong champ, string zone, string node)
        {
            Zone z = zone != null ? ZoneByKey(zone) : CurrentZone(champ);
            CampState st = Get(z.Base + champ, true);
            MapNode n = node != null ? FindNode(z, node) : null;
            if (n != null) { st.LastNode = n.Id; st.Visible.Add(n.Id); }
            st.ALoc = null;
            Save();
            Notify(st.CampId, champ, "return_parent", "AREA", BuildState(st));
        }

        /// <summary>A dungeon was finished: its AZ1 node is completed (what lies beyond is revealed).</summary>
        internal static void OnDungeonDone(ulong champ, string zone, string node)
        {
            Zone z = ZoneByKey(zone);
            MapNode n = node != null ? FindNode(z, node) : null;
            if (n == null) return;
            CampState st = Get(z.Base + champ, true);
            Complete(st, n);
            Save();
        }

        /// <summary>
        /// The River of Whispers (Src.Game: "AZ1 - Node Y - To AZ2" / "AZ2 - Node 99B - Transfer To AZ1",
        /// event do_transfer): the champion goes to the other zone's map, at its end of the river.
        /// </summary>
        private static void Transfer(CampState from)
        {
            Zone to = from.Z == AZ1 ? AZ2 : AZ1;
            ulong champ = from.ChampId;
            from.ALoc = null;
            Quests.SetFlag(champ, "zone", to.Key);
            if (to == AZ2) Quests.CompleteLoc(champ, "q_go_to_az2", "Step1");
            CampState st = Get(to.Base + champ, true);
            st.LastNode = to == AZ2 ? AZ2.StartNode : "Node00Y";
            st.Visible.Add(st.LastNode);
            st.ALoc = null;
            Save();
            Debug.Log("[HexRevival] [Campaign] champion " + champ + " sails to " + to.Key);
            QuestSim.Note("  -> " + to.Key);
            try { GameClient.Instance.PlayerProfile.GetPvEChampionById(champ).SourceBits.LastCampaignID = st.CampId; } catch { }
            Notify(st.CampId, champ, "transfer", "AREA", BuildState(st));
        }

        internal static IEnumerable<string> EventStrings(object[] args) { return Strings(args); }
        internal static string ConversationName(string id) { return ConvName(id); }

        private static void PanoEvent(ulong champ, string evt, ClientCampaignManager.StateCallback callback)
        {
            PanoState ps;
            s_Pano.TryGetValue(champ, out ps);
            if (evt == "return_parent") { LeavePanorama(champ); return; }
            if (ps != null && (evt == "conv_done" || evt == "accept") && ps.ALoc != null)
            {
                Npc n = HubNpcs(champ, ps.Z, ps.Node).Find(x => x.Name == ps.ALoc);
                ps.ALoc = null;
                if (n != null && evt == "conv_done")
                {
                    Debug.Log("[HexRevival] [Campaign] " + n.Name + ": " + n.ConvName);
                    Quests.OnGiverConversationDone(champ, n.ConvId, n.ConvName);
                }
            }
            else if (ps != null && (evt == "cancel" || evt == "enc_cancel")) ps.ALoc = null;
            AnswerPano(champ, callback);
        }

        // ------------------------------------------------------------------
        // ClientCampaignManager entry points (Harmony prefixes, see Plugin.cs)
        // ------------------------------------------------------------------

        public static void QueryCurrentCampaign(ulong champId, ClientCampaignManager.StateCallback callback)
        {
            // Inside a dungeon (or a new champion: Castle Crayburn first).
            GameplayState dg = Dungeons.Current(champId, !HasProgress(champId));
            if (dg != null)
            {
                try { GameClient.Instance.PlayerProfile.GetPvEChampionById(champId).SourceBits.LastCampaignID = dg.CampID; } catch { }
                Deliver(dg.CampID, dg, callback);
                return;
            }
            CampState st = GetChamp(champId, true);
            try
            {
                PlayerChampion ch = GameClient.Instance.PlayerProfile.GetPvEChampionById(champId);
                if (ch != null && ch.SourceBits != null) ch.SourceBits.LastCampaignID = st.CampId;
            }
            catch { }
            Answer(st, callback);
        }

        public static void Create(ulong champId, string template, ClientCampaignManager.StateCallback callback)
        {
            Answer(GetChamp(champId, true), callback);
        }

        public static void GetSummary(IEnumerable<ulong> ids, ClientCampaignManager.GetSummaryCallback callback)
        {
            List<CampSummary> res = new List<CampSummary>();
            foreach (ulong id in ids)
            {
                if (Quests.IsQuestCamp(id)) continue;
                if (Dungeons.IsDunCamp(id)) { Dungeons.GetSummary(id, res); continue; }
                if (IsPanoCamp(id)) { res.Add(PanoSummary(id - PanoBase)); continue; }
                CampState st = Get(id, false);
                if (st != null) res.Add(Summary(st));
            }
            CampSummary[] arr = res.ToArray();
            Enqueue(() => callback(arr));
        }

        public static void GetActiveStatus(ulong champId, CampaignType type, string template, ClientCampaignManager.GetActiveStatusCallback callback)
        {
            List<TemplateInfo> res = new List<TemplateInfo>();
            CampState st = GetChamp(champId, false);
            if (st != null && (type == CampaignType.AREA || type == CampaignType.ANY))
                res.Add(new TemplateInfo(st.CampId, CampaignType.AREA, GameClient.Instance.PlayerProfile.ReckoningID, st.Z.Key, 1, null));
            if (type == CampaignType.QUEST || type == CampaignType.ANY) res.AddRange(Quests.ActiveInfos(champId));
            if (type == CampaignType.DUNGEON || type == CampaignType.ANY) Dungeons.ActiveInfos(champId, res);
            TemplateInfo[] arr = res.ToArray();
            Enqueue(() => callback(arr));
        }

        public static void QueryState(ulong campId, ClientCampaignManager.StateCallback callback)
        {
            if (Quests.IsQuestCamp(campId)) { AnswerQuest(campId, callback); return; }
            if (Dungeons.IsDunCamp(campId)) { Dungeons.QueryState(campId, callback); return; }
            if (IsPanoCamp(campId)) { AnswerPano(campId - PanoBase, callback); return; }
            Answer(Get(campId, false), callback);
        }

        public static void Start(ulong campId, ClientCampaignManager.StateCallback callback)
        {
            if (Quests.IsQuestCamp(campId)) { AnswerQuest(campId, callback); return; }
            if (Dungeons.IsDunCamp(campId)) { Dungeons.QueryState(campId, callback); return; }
            if (IsPanoCamp(campId)) { AnswerPano(campId - PanoBase, callback); return; }
            CampState st = Get(campId, false);
            if (st != null && !st.Started) { st.Started = true; Save(); }
            Answer(st, callback);
        }

        public static void Forfeit(ulong campId, ClientCampaignManager.StateCallback callback)
        {
            if (Dungeons.IsDunCamp(campId)) { Dungeons.Forfeit(campId); return; }
            if (IsPanoCamp(campId)) { LeavePanorama(campId - PanoBase); return; }
            CampState st = Get(campId, false);
            if (st != null) st.ALoc = null;
            Answer(st, callback);
        }

        public static void StartLocation(ulong campId, string location, ClientCampaignManager.StateCallback callback)
        {
            if (Dungeons.IsDunCamp(campId)) { Dungeons.StartLocation(campId, location, callback); return; }
            if (IsPanoCamp(campId))
            {
                PanoState ps;
                if (s_Pano.TryGetValue(campId - PanoBase, out ps)) ps.ALoc = location;
                AnswerPano(campId - PanoBase, callback);
                return;
            }
            CampState st = Get(campId, false);
            if (st == null) return;
            MapNode n = FindNode(st.Z, location);
            if (n != null && st.Visible.Contains(n.Id))
            {
                st.ALoc = n.Name;
                st.LastNode = n.Id;
                Save();
            }
            Debug.Log("[HexRevival] [Campaign] StartLocation " + location + " -> " + (n != null ? n.Type : "unknown"));
            Answer(st, callback);
        }

        public static void FinishLocation(ulong campId, string location, ClientCampaignManager.StateCallback callback)
        {
            if (Dungeons.IsDunCamp(campId)) { Dungeons.SendEvent(campId, "conv_done", null, callback); return; }
            if (IsPanoCamp(campId)) { PanoEvent(campId - PanoBase, "conv_done", callback); return; }
            CampState st = Get(campId, false);
            if (st == null) return;
            MapNode n = FindNode(st.Z, location);
            if (n != null) Complete(st, n);
            st.ALoc = null;
            Save();
            Answer(st, callback);
        }

        public static void SendEvent(ulong campId, string evt, object[] args, ClientCampaignManager.StateCallback callback)
        {
            if (Quests.IsQuestCamp(campId)) return;
            if (Dungeons.IsDunCamp(campId)) { Dungeons.SendEvent(campId, evt, args, callback); return; }
            if (IsPanoCamp(campId)) { Debug.Log("[HexRevival] [Campaign] panorama event " + evt); PanoEvent(campId - PanoBase, evt, callback); return; }
            CampState st = Get(campId, false);
            if (st == null) return;
            string arg0 = args != null && args.Length > 0 && args[0] != null ? args[0].ToString() : null;
            Debug.Log("[HexRevival] [Campaign] event " + evt + (arg0 != null ? " " + arg0 : ""));
            if (evt != "visit_node" && evt != "visit_path" && evt != "pass_node") LastEvent = evt;
            MapNode active = st.ALoc != null ? FindNode(st.Z, st.ALoc) : null;
            if (ShipEvent(st, evt, arg0, active)) { Save(); Answer(st, callback); return; }
            switch (evt)
            {
                case "visit_node":
                    if (arg0 != null && !st.VisitedNodes.Contains(arg0)) st.VisitedNodes.Add(arg0);
                    if (arg0 != null) st.LastNode = arg0;
                    // A dungeon's node does not bar the way: what lies beyond is revealed once there.
                    MapNode visited = arg0 != null ? FindNode(st.Z, arg0) : null;
                    if (visited != null && visited.Dungeon != null) Reveal(st, visited.Id);
                    break;
                case "visit_path":
                    // The client sends the path names as one string array inside the arguments.
                    foreach (string path in Strings(args))
                        if (!st.VisitedPaths.Contains(path)) st.VisitedPaths.Add(path);
                    break;
                case "pass_node":
                    if (arg0 != null) st.LastNode = arg0;
                    break;
                case "conv_done":
                    if (active != null && Quests.Flag(st.ChampId, "outcome:" + active.Key) != null)
                    {
                        // End of the battle's outcome conversation.
                        Quests.SetFlag(st.ChampId, "outcome:" + active.Key, null);
                        st.ALoc = null;
                        break;
                    }
                    if (active != null && EncOf(st.ChampId, active) != null && !st.Completed.Contains(active.Id)
                        && Prelude(st.ChampId, active) != null && Quests.Flag(st.ChampId, "prelude:" + active.Key) == null)
                    {
                        // Without Herbert's gnomes the wormoid queen stays underground: nothing to fight (Src.Game dialogue).
                        if (ConvName(Prelude(st.ChampId, active)).ToUpperInvariant().Contains("NO GNOMES IN DECK")) { st.ALoc = null; break; }
                        // Prelude seen: the battle comes next, unless the player avoided it (sneak past, bribe).
                        Quests.SetFlag(st.ChampId, "prelude:" + active.Key, "1");
                        string choice = Quests.Flag(st.ChampId, "choice:" + active.Key);
                        if (choice == "choice_sneak" || choice == "choice_Bribe") { Complete(st, active); st.ALoc = null; }
                        break;
                    }
                    if (active != null) Quests.SetFlag(st.ChampId, "visited:" + active.Key, "1");
                    if (active != null && active.IsHaus && !st.Completed.Contains(active.Id))
                    {
                        // Shroomkin's conversation done: the card choice window opens next (location stays active).
                        st.HausReady.Add(active.Id);
                        break;
                    }
                    goto case "empty_done";
                case "shroom_choice":
                    if (active != null && active.IsHaus && !st.Completed.Contains(active.Id) && Progression.OnShroomHausChoice(st.ChampId, active.Key, arg0))
                    {
                        st.HausReady.Remove(active.Id);
                        Complete(st, active);
                        st.ALoc = null;
                    }
                    break;
                case "empty_done":
                case "display_done":
                case "count_choice":
                case "accept":
                {
                    // Conversation just shown (computed before the node is completed: the rules depend on it).
                    string convId = null;
                    if (active != null && evt == "conv_done")
                    {
                        if (active.RepeatConversation != null && st.Completed.Contains(active.Id)) convId = active.RepeatConversation;
                        else if (EncOf(st.ChampId, active) == null && active.Panorama == null && !active.IsHaus) convId = NodeConversation(st.ChampId, active);
                        if (convId == null) convId = active.Conversation;
                    }
                    if (active != null && evt == "conv_done" && active.FightEncounter != null && Quests.Flag(st.ChampId, "choice:" + active.Key) == "choice_fight")
                    {
                        // Dialogue ending in a fight: the battle decides whether the node is passed.
                        Quests.SetFlag(st.ChampId, "choice:" + active.Key, null);
                        StartBattle(st, active, active.FightEncounter);
                        break;
                    }
                    if (active != null && evt == "conv_done" && IsGate(st.ChampId, active, convId))
                    {
                        // The way stays closed: the node is not completed, so what lies beyond is not revealed.
                        st.ALoc = null;
                        break;
                    }
                    if (active != null) Complete(st, active);
                    st.ALoc = null;
                    if (active != null && evt == "conv_done")
                    {
                        Progression.OnCampaignConversationDone(st.ChampId, active.Key);
                        ConversationTemplate ct = convId != null ? Singleton<TemplateManager>.Instance.GetConversationTemplate(new ResourceId(convId)) : null;
                        if (ct != null)
                        {
                            Quests.OnAreaConversationDone(st.ChampId, active.Key, ct.m_Name);
                            // AZ2 turn-ins on the map (Brutecrown Delta's "Quest End"): the quest named
                            Quests.OnConversationDone(st.ChampId, convId, st.Z != AZ1 && Quests.RoleOf(ct.m_Name) == Quests.Role.End ? Quests.QuestOf(ct.m_Name) : null);
                        }
                    }
                    break;
                }
                case "enc_cancel":
                case "cancel":
                    st.ALoc = null;
                    break;
                case "start":
                    if (active != null && active.Z == AZ1 && active.Id == "Node019" && ZombieBlockade(st.ChampId) != null) { StartBattle(st, active, ZombieBlockade(st.ChampId)); break; }
                    if (active != null && active.Dungeon != null)
                    {
                        st.ALoc = null;
                        st.LastNode = active.Id;
                        Save();
                        Dungeons.Enter(st.ChampId, active.Dungeon);
                        return;
                    }
                    if (active != null && active.Panorama != null) { EnterPanorama(st, active); break; }
                    if (active != null && EncOf(st.ChampId, active) != null) StartBattle(st, active);
                    break;
                default:
                    // Army of Myth repeat: "Diff_N" = fight the army with N resources in play (0 = level 1); "Cancel" = no fight.
                    int diff;
                    if (evt == "do_transfer") { Transfer(st); return; }
                    if (active != null && active.Variants != null && active.Variants.ContainsKey(evt)) Quests.SetFlag(st.ChampId, "choice:" + active.Key, evt);
                    if (evt == "gaal_camp_accept") Fortunes.Buy(st.ChampId);
                    else if (evt.StartsWith("choice_") && active != null)
                    {
                        // Dialogue choices that change what follows (help or not, sneak, bribe, robot install).
                        Quests.SetFlag(st.ChampId, "choice:" + active.Key, evt);
                        if (evt == "choice_install" || evt == "choice_no_install") Quests.SetFlag(st.ChampId, "robot", evt);
                        if (evt == "choice_Bribe" && Saves.Current != null)
                        {
                            // Src.Game: the guard's answer "Fine. [Pay 1000 gold]".
                            Saves.Current.Gold = Math.Max(0L, Saves.Current.Gold - 1000);
                            Saves.SaveCurrent();
                            try { GameClient.Instance.PlayerProfile.UpdateBalance((int)Saves.Current.Gold, (int)Saves.Current.Platinum); } catch { }
                        }
                    }
                    else if (active != null && EncOf(st.ChampId, active) != null && evt.StartsWith("Diff_") && int.TryParse(evt.Substring(5), out diff))
                    {
                        s_Difficulty = diff;
                        StartBattle(st, active);
                    }
                    else if (evt == "Cancel") st.ALoc = null;
                    break;
            }
            Save();
            Answer(st, callback);
        }

        /// <summary>Event arguments as strings, nested arrays flattened.</summary>
        private static IEnumerable<string> Strings(object[] args)
        {
            if (args == null) yield break;
            foreach (object a in args)
            {
                System.Collections.IEnumerable list = a as System.Collections.IEnumerable;
                if (a is string || list == null) { if (a != null) yield return a.ToString(); continue; }
                foreach (object x in list) if (x != null) yield return x.ToString();
            }
        }

        // ------------------------------------------------------------------
        // Encounter battles
        // ------------------------------------------------------------------

        /// <summary>Node of the campaign battle in progress (the client may end the conversation that launched it before the battle ends).</summary>
        private static string s_BattleNode;

        /// <summary>Difficulty of the campaign battle in progress (Army of Myth: resources the AI started with), for its rewards.</summary>
        public static int BattleDifficulty { get; private set; }

        private static void StartBattle(CampState st, MapNode node) { StartBattle(st, node, EncOf(st.ChampId, node)); }

        /// <summary>Encounter of the battle in progress (the node's own, a variant, or a dialogue's fight).</summary>
        private static string s_BattleEnc;

        private static void StartBattle(CampState st, MapNode node, string encounter)
        {
            BattleDifficulty = s_Difficulty;
            s_BattleNode = node.Id;
            s_BattleEnc = encounter;
            Debug.Log("[HexRevival] [Campaign] battle " + node.Id + " difficulty " + BattleDifficulty);
            s_Battle = st;
            ResetShipBattle();
            LaunchBattle(Fighter(st, node), encounter, node.Name);
        }

        /// <summary>The profile's current champion, without the client's side effects (read by the battle HUD and the campaign screens).</summary>
        private static void SetCurrentChampion(ulong champId)
        {
            try { typeof(PlayerProfile).GetField("m_CurrentChampionId", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(GameClient.Instance.PlayerProfile, champId); }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Campaign] current champion: " + e.Message); }
        }

        /// <summary>Starts a campaign battle (map node or dungeon room) with the champion's deck.</summary>
        internal static void LaunchBattle(ulong champId, string encounter, string label)
        {
            UID deck = UID.Invalid;
            try
            {
                PlayerChampion ch = GameClient.Instance.PlayerProfile.GetPvEChampionById(champId);
                ulong sub = SubChampionDeck(champId);
                if (sub != 0) deck = new UID(UID.Type.Deck, sub);
                else if (ch != null && ch.LastDeckID != 0) deck = new UID(UID.Type.Deck, ch.LastDeckID);
                else
                {
                    ulong id = Champions.EnsureDeck(champId);
                    if (id != 0) deck = new UID(UID.Type.Deck, id);
                }
            }
            catch { }
            s_BattleChamp = champId;
            // The battle HUD reads the level from the profile's current champion.
            SetCurrentChampion(champId);
            BattleEvents.Clear();
            s_FortuneApplied = false;
            Enqueue(() => Patches.StartMatch(deck, new ResourceId(encounter), null, "campaign " + label));
        }

        /// <summary>Called once when a match ends. Returns false if it was not a campaign battle.</summary>
        public static bool OnGameEnded(bool won)
        {
            s_Difficulty = 0;
            s_BattleChamp = 0;
            if (Dungeons.OnGameEnded(won)) return true;
            CampState st = s_Battle;
            // BattleDifficulty is read by the rewards below, then reset
            if (st == null) return false;
            s_Battle = null;
            MapNode node = s_BattleNode != null ? FindNode(st.Z, s_BattleNode) : st.ALoc != null ? FindNode(st.Z, st.ALoc) : null;
            s_BattleNode = null;
            // A sea battle was the ship's: the champion is the current one again.
            SetCurrentChampion(st.ChampId);
            if (node != null && IsSea(node)) ShipBattleEnded(st.ChampId, won);
            if (won && node != null) Complete(st, node);
            st.ALoc = null;
            Save();
            Debug.Log("[HexRevival] [Campaign] battle " + (node != null ? node.Name : "?") + (won ? " won" : " lost") + ", difficulty " + BattleDifficulty);
            QuestSim.Note("  battle " + (node != null ? node.Name : "?") + (won ? " won" : " lost"));
            string enc = s_BattleEnc ?? (node != null ? node.Encounter : null);
            s_BattleEnc = null;
            if (won && node != null && enc != null)
            {
                Quests.SetFlag(st.ChampId, "won:" + enc, "1");
                Quests.SetFlag(st.ChampId, "won:" + node.Key, "1");
                if (node.Z == AZ1 && node.Id == "Node014") Quests.CompleteLoc(st.ChampId, "az01_q_cross_the_river", "Step1");
                Quests.OnEncounterWon(st.ChampId, enc, Progression.AnyTamed());
            }
            if (node != null)
            {
                Quests.SetFlag(st.ChampId, "prelude:" + node.Key, null);
                bool zombies = node.Z == AZ1 && node.Id == "Node019" && enc != null && enc == ZombieBlockade(st.ChampId);
                if (zombies && won) Quests.SetFlag(st.ChampId, "won:Node019B", "1");
                string outcome = zombies ? null : Outcome(st.ChampId, node, won);
                if (outcome != null)
                {
                    Quests.SetFlag(st.ChampId, "outcome:" + node.Key, outcome);
                    st.ALoc = node.Name;
                    Save();
                }
            }
            if (won && node != null) Progression.OnCampaignBattleWon(st.ChampId, node.Z == AZ1 && node.Id == "Node019" && Quests.Flag(st.ChampId, "won:Node019B") != null && enc != null && enc != node.FightEncounter ? "Node019B" : node.Key);
            BattleDifficulty = 0;
            return true;
        }

        // ------------------------------------------------------------------
        // Quest simulation helpers (see QuestSim)
        // ------------------------------------------------------------------

        /// <summary>New campaign for a simulated champion, the whole map visible.</summary>
        internal static void SimStart(ulong champ)
        {
            CampState st = GetChamp(champ, true);
            BuildMap(st.Z);
            foreach (MapNode n in st.Z.Nodes) st.Visible.Add(n.Id);
            Save();
        }

        /// <summary>Campaign id of the simulated champion in its current zone.</summary>
        internal static ulong SimCamp(ulong champ) { return CurrentZone(champ).Base + champ; }

        /// <summary>What the client would show at a node: type, conversation, flags.</summary>
        internal static string SimDescribe(ulong champ, string node)
        {
            CampState st = GetChamp(champ, false);
            MapNode mn = st != null ? FindNode(st.Z, node) : null;
            if (st == null || mn == null) return "no campaign or node " + node;
            Location l = BuildState(st).VisLocs.Find(x => x.Node == mn.Id);
            if (l == null) return mn.Id + ": not sent";
            SceneData sd;
            string enc = l.Encounter != null && Singleton<TemplateManager>.Instance.CampaignScenes.TryGetValue(new ResourceId(l.Encounter), out sd) && sd != null ? " [" + sd.m_Name + "]" : "";
            return mn.Id + " " + mn.Name + ": " + l.Type + enc + (l.ConversationID != null ? " \"" + ConvName(l.ConversationID) + "\"" : "")
                + (l.AutoStart ? " autostart" : "") + (l.Completed ? " completed" : "") + (l.Repeatable ? " repeatable" : "");
        }

        /// <summary>Battle at the active node, won or lost (no match is played).</summary>
        internal static void SimFight(ulong champ, bool won)
        {
            CampState st = GetChamp(champ, false);
            MapNode active = st != null && st.ALoc != null ? FindNode(st.Z, st.ALoc) : null;
            // A battle already started by the dialogue (Army of Myth "Diff_N") is fought even after the conversation closed.
            if (s_Battle == null && active != null && active.Z == AZ1 && active.Id == "Node019" && ZombieBlockade(champ) != null) StartBattle(st, active, ZombieBlockade(champ));
            if (s_Battle == null && (active == null || EncOf(champ, active) == null)) { QuestSim.Note("  (no battle here)"); return; }
            if (s_Battle == null) StartBattle(st, active);
            OnGameEnded(won);
        }

        /// <summary>Enters a hub: the NPCs and what each offers.</summary>
        internal static string SimHub(ulong champ, string node)
        {
            CampState st = GetChamp(champ, false);
            MapNode mn = FindNode(st.Z, node);
            if (st == null || mn == null || mn.Panorama == null) return "not a hub: " + node;
            EnterPanorama(st, mn);
            return SimNpcs(champ);
        }

        internal static string SimNpcs(ulong champ)
        {
            PanoState ps;
            if (!s_Pano.TryGetValue(champ, out ps)) return "not in a hub";
            List<string> l = new List<string>();
            foreach (Npc n in HubNpcs(champ, ps.Z, ps.Node)) l.Add(n.Name + (n.Give ? " [!]" : n.TurnIn ? " [?]" : "") + " \"" + n.ConvName + "\"");
            return string.Join("\n    ", l.ToArray());
        }

        /// <summary>Talks to a hub NPC (whole conversation).</summary>
        internal static string SimNpc(ulong champ, string npc)
        {
            PanoState ps;
            if (!s_Pano.TryGetValue(champ, out ps)) return "not in a hub";
            Npc n = HubNpcs(champ, ps.Z, ps.Node).Find(x => string.Equals(x.Name, npc, StringComparison.OrdinalIgnoreCase));
            if (n == null) return "no NPC " + npc;
            ps.ALoc = n.Name;
            PanoEvent(champ, "conv_done", null);
            return n.Name + ": \"" + n.ConvName + "\"";
        }

        internal static void SimLeaveHub(ulong champ) { s_Pano.Remove(champ); }

        // ------------------------------------------------------------------
        // Test command
        // ------------------------------------------------------------------

        /// <summary>"camp dump [filter]": AZ1 data as the client knows it; "camp map": the map model.</summary>
        public static string Command(string[] parts)
        {
            Zone cz = Array.IndexOf(parts, "AZ2") >= 0 ? AZ2 : AZ1;   // zone of the dev dumps ("AZ2" anywhere in the command)
            string sub = parts.Length > 1 ? parts[1] : "";
            if (sub == "dump") return Dump(parts.Length > 2 ? parts[2] : "AZ");
            if (sub == "rewards") return DumpRewards();
            if (sub == "deck" && parts.Length > 2)
            {
                // dev: "camp deck <encounter deck id>" = the AI deck's fields (champion, cards)
                TemplateManager tm = Singleton<TemplateManager>.Instance;
                EncounterDeck ed = tm.GetEncounterDeck(new ResourceId(parts[2]));
                DeckTemplate dt = ed != null ? tm.GetDeckTemplate(ed.m_DeckTemplateId) : tm.GetDeckTemplate(new ResourceId(parts[2]));
                if (dt == null) return "no deck " + parts[2];
                ChampionTemplate ch = tm.GetChampionTemplate(dt.m_ChampionId);
                List<string> cards = new List<string>();
                foreach (TemplateCardStack st in dt.m_DeckResources)
                {
                    CardTemplate ct = tm.GetCardTemplate(st.m_idTemplate);
                    cards.Add(st.m_Count + "x " + (ct != null ? ct.m_Name : st.m_idTemplate.ToString()));
                }
                return "deck '" + dt.m_DeckName + "' champion " + (ch != null ? ch.m_Name + " (" + ch.m_StartingHealth + " health): " + Txt(ch.m_GameText) : "none") + "\ncards: " + string.Join(", ", cards.ToArray());
            }
            if (sub == "scene" && parts.Length > 2)
            {
                // dev: "camp scene <exact encounter name>" = the encounter scene's fields (else the filter dump below)
                string name = string.Join(" ", parts, 2, parts.Length - 2);
                foreach (SceneData s in Singleton<TemplateManager>.Instance.CampaignScenes.Values)
                    if (s != null && string.Equals(s.m_Name, name, StringComparison.OrdinalIgnoreCase)) return Describe(s);
                // not an exact name: the dump below (names containing it)
            }
            if (sub == "boatdump")
            {
                // dev: the boat champion templates and boat talents (AZ2 sea voyages) to HexRevivalBoat.txt
                StringBuilder sb = new StringBuilder();
                TemplateManager tm = Singleton<TemplateManager>.Instance;
                foreach (ChampionTemplate c in tm.Champions.Values)
                    if (c != null && c.m_Class == EChampionClass.Boat)
                        sb.AppendLine("champion " + c.m_Id + " '" + c.m_Name + "' type=" + c.m_ChampionType + " selectable=" + c.m_IsPlayerSelectable + " race=" + c.m_Race + " gender=" + c.m_Gender
                            + " health=" + c.m_StartingHealth + " hand=" + c.m_StartingHandSize + " charges=" + c.m_StartingCharges + " set=" + c.m_SetId + " text=" + c.m_GameText);
                foreach (ChampionTalentData t in tm.ChampionTalentTemplates.Values)
                    if (t != null && t.m_Class == EChampionClass.Boat)
                        sb.AppendLine("talent " + t.m_Id + " '" + t.m_Name + "' cost=" + t.m_Cost + " minlevel=" + t.m_MinLevel + " : " + t.m_Description);
                File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "HexRevivalBoat.txt"), sb.ToString());
                return "boat data written to HexRevivalBoat.txt";
            }
            if (sub == "convs")
            {
                // dev: text and events of the AZ1 conversations (all, or those whose name contains the filter) to HexRevivalConvs.txt
                string filter = parts.Length > 2 ? string.Join(" ", parts, 2, parts.Length - 2).ToUpperInvariant() : "";
                StringBuilder sb = new StringBuilder();
                int count = 0;
                foreach (ConversationTemplate c in Singleton<TemplateManager>.Instance.ConversationTemplates.Values)
                {
                    if (c == null || (filter == "" && !c.m_Name.StartsWith("AZ1")) || (filter != "" && !c.m_Name.ToUpperInvariant().Contains(filter))) continue;
                    count++;
                    sb.AppendLine("=== " + c.m_Name + " (" + c.m_OwnerName + ")");
                    foreach (KeyValuePair<int, ConversationQuestion> q in c.m_Questions)
                    {
                        sb.AppendLine("Q" + q.Key + ": " + Txt(q.Value.m_QuestionText));
                        foreach (int aid in q.Value.m_AnswerIds)
                        {
                            ConversationAnswer a;
                            if (!c.m_Answers.TryGetValue(aid, out a)) continue;
                            sb.AppendLine("   A" + aid + " -> Q" + a.m_NextQuestionId + ": " + Txt(a.m_AnswerText));
                            foreach (object pr in a.m_Prerequisites) sb.AppendLine("      if " + Describe(pr));
                            foreach (object ev in a.m_EventsToFireWhenSelected) sb.AppendLine("      fire " + Describe(ev));
                            foreach (object ev in a.m_OnSelectedEvents) sb.AppendLine("      do " + Describe(ev));
                        }
                    }
                }
                File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "HexRevivalConvs.txt"), sb.ToString());
                return count + " conversations written to HexRevivalConvs.txt";
            }
            if (sub == "fight" && parts.Length > 2)
            {
                // dev: "camp fight Node066 Diff_6" = start that node's battle for the current champion's campaign, as if the event had been chosen
                ulong champ = GameClient.Instance.PlayerProfile.GetCurrentChampion().Id;
                CampState st = GetChamp(champ, false);
                MapNode n = FindNode(st.Z, parts[2]);
                string enc = n != null ? n.Encounter ?? n.FightEncounter : null;
                if (st == null || enc == null) return "no campaign for champion " + champ + " or no battle at " + parts[2];
                st.ALoc = n.Name;
                LastEvent = parts.Length > 3 ? parts[3] : "start";
                int diff;
                if (LastEvent.StartsWith("Diff_") && int.TryParse(LastEvent.Substring(5), out diff)) s_Difficulty = diff;
                StartBattle(st, n, enc);
                return "fight " + n.Id + " (" + LastEvent + ") for champion " + champ;
            }
            if (sub == "fortune")
            {
                // dev: "camp fortune" = a gaal reading for the current champion (pays 100 gold)
                ulong champ = GameClient.Instance.PlayerProfile.GetCurrentChampion().Id;
                Fortunes.Buy(champ);
                return "fortune " + Quests.Flag(champ, "fortune");
            }
            if (sub == "goto" && parts.Length > 2)
            {
                // dev: "camp goto Node012" = put the champion on a node (revealed) and select it on the open map
                ulong champ = GameClient.Instance.PlayerProfile.GetCurrentChampion().Id;
                CampState st = GetChamp(champ, false);
                MapNode mn = FindNode(st.Z, parts[2]);
                if (st == null || mn == null) return "no campaign or node";
                st.Visible.Add(mn.Id);
                Reveal(st, mn.Id);
                st.LastNode = mn.Id;
                st.ALoc = mn.Name;
                Save();
                GameplayState gs = BuildState(st);
                UICampaignZoneVMBase vm = UnityEngine.Object.FindObjectOfType<UICampaignZoneVMBase>();
                if (vm == null) return "campaign map not open";
                typeof(ClientCampaignManager).GetMethod("CacheCampaignState", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { st.CampId, gs });
                typeof(UICampaignZoneVMBase).GetMethod("OnHandleStateChange", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(vm, new object[] { gs });
                Location loc = gs.VisLocs.Find(l => l.Node == mn.Id);
                return mn.Id + " " + mn.Name + ": type=" + loc.Type + " conv=" + loc.ConversationID + " " + ConvName(loc.ConversationID);
            }
            if (sub == "open")
            {
                // dev: open the campaign map of the current champion (or "camp open <champion id>")
                ulong champ = parts.Length > 2 ? ulong.Parse(parts[2]) : GameClient.Instance.PlayerProfile.GetCurrentChampion().Id;
                SetCurrentChampion(champ);
                CampState cs = GetChamp(champ, true);
                UICampaignZoneVMBase.StateContext ctx = new UICampaignZoneVMBase.StateContext { CampaignId = cs.CampId, CampaignType = CampaignType.AREA, CampaignName = cs.Z.Key };
                Singleton<Main>.Instance.m_GameClient.StateManager.TransitionToState(EGameState.Dungeon, ctx);
                return "opening the campaign of champion " + champ;
            }
            if (sub == "pano" && parts.Length > 2)
            {
                // dev: "camp pano Node017" = enter that hub's panorama with the current champion (campaign map open)
                ulong champ = GameClient.Instance.PlayerProfile.GetCurrentChampion().Id;
                CampState st = GetChamp(champ, false);
                MapNode mn = FindNode(st.Z, parts[2]);
                if (st == null || mn == null || mn.Panorama == null) return "no campaign or not a hub";
                EnterPanorama(st, mn);
                List<string> o = new List<string>();
                foreach (Npc n in HubNpcs(champ, mn.Z, mn.Id)) o.Add(n.Name + (n.Give ? " [!]" : n.TurnIn ? " [?]" : "") + " = " + n.ConvName);
                return "panorama " + mn.Name + ": " + string.Join("; ", o.ToArray());
            }
            if (sub == "loc" && parts.Length > 2)
            {
                // dev: the location data sent to the client for a node, for the current champion
                ulong champ = GameClient.Instance.PlayerProfile.GetCurrentChampion().Id;
                CampState st = GetChamp(champ, false);
                MapNode mn = FindNode(st.Z, parts[2]);
                if (st == null || mn == null) return "no campaign or node";
                GameplayState gs = BuildState(st);
                foreach (Location l in gs.VisLocs)
                    if (l.Node == mn.Id) return "champion " + champ + " completed=" + st.Completed.Contains(mn.Id) + " repeatConv=" + mn.RepeatConversation + " type=" + l.Type + " conv=" + l.ConversationID + " enc=" + l.Encounter;
                return "not found";
            }
            if (sub == "renode" && parts.Length > 2)
            {
                // dev: "camp renode Node035" = make a node new again for the current champion (not completed, rewards claimable)
                ulong champ = GameClient.Instance.PlayerProfile.GetCurrentChampion().Id;
                CampState st = GetChamp(champ, false);
                MapNode n = FindNode(st.Z, parts[2]);
                if (st == null || n == null) return "no campaign or node";
                st.Completed.Remove(n.Id);
                st.HausReady.Remove(n.Id);
                Saves.Current.Claims.Remove(champ + ":" + n.Id);
                Saves.SaveCurrent();
                Save();
                return n.Id + " reset for champion " + champ + " (reopen the map to see it)";
            }
            if (sub == "scene" && parts.Length > 2)
            {
                // dev: every field of the encounter scenes whose name contains the filter, to HexRevivalScenes.txt
                string filter = string.Join(" ", parts, 2, parts.Length - 2).ToUpperInvariant();
                StringBuilder sb = new StringBuilder();
                int count = 0;
                foreach (SceneData sd in Singleton<TemplateManager>.Instance.CampaignScenes.Values)
                {
                    if (sd == null || sd.m_Name == null || !sd.m_Name.ToUpperInvariant().Contains(filter)) continue;
                    count++;
                    sb.AppendLine("=== " + sd.m_Name).AppendLine(Describe(sd));
                }
                File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "HexRevivalScenes.txt"), sb.ToString());
                return count + " scene(s) written to HexRevivalScenes.txt";
            }
            if (sub == "colors")
            {
                // dev: shard colours of the AI decks of the battles near each Shroom Haus (up to 3 steps away)
                BuildMap(cz);
                TemplateManager tm = Singleton<TemplateManager>.Instance;
                StringBuilder sb = new StringBuilder();
                foreach (MapNode h in cz.Nodes)
                {
                    if (!h.Scenes.ToUpperInvariant().Contains("SHROOM HAUS")) continue;
                    Dictionary<string, int> dist = new Dictionary<string, int> { { h.Id, 0 } };
                    Queue<string> q = new Queue<string>();
                    q.Enqueue(h.Id);
                    while (q.Count > 0)
                    {
                        string cur = q.Dequeue();
                        if (dist[cur] >= 3) continue;
                        foreach (string nb in Neighbours(cz, cur)) if (!dist.ContainsKey(nb)) { dist[nb] = dist[cur] + 1; q.Enqueue(nb); }
                    }
                    Dictionary<string, int> colors = new Dictionary<string, int>();
                    List<string> near = new List<string>();
                    foreach (KeyValuePair<string, int> kv in dist)
                    {
                        MapNode n = FindNode(cz, kv.Key);
                        if (n == null || n.Encounter == null) continue;
                        EncounterScene es = tm.CampaignScenes[new ResourceId(n.Encounter)] as EncounterScene;
                        if (es == null) continue;
                        near.Add(n.Id + "(" + kv.Value + ")");
                        foreach (EncounterPlayer pl in es.m_EncounterPlayers)
                        {
                            if (!pl.m_IsAI) continue;
                            ResourceId deckId = pl.m_EncounterDeck;
                            EncounterDeck ed;
                            if (tm.EncounterDecks.TryGetValue(deckId, out ed) && ed != null) deckId = ed.m_DeckTemplateId;
                            DeckTemplate d;
                            if (!tm.Decks.TryGetValue(deckId, out d) || d == null) continue;
                            foreach (TemplateCardStack cs in d.m_DeckResources)
                            {
                                CardTemplate ct = tm.GetCardTemplate(cs.m_idTemplate);
                                if (ct == null || ct.m_Threshold == null) continue;
                                foreach (CardThreshold th in ct.m_Threshold)
                                {
                                    string k = th.m_ColorFlags.ToString();
                                    int c; colors.TryGetValue(k, out c); colors[k] = c + cs.m_Count;
                                }
                            }
                        }
                    }
                    List<string> cl = new List<string>();
                    foreach (KeyValuePair<string, int> kv in colors) cl.Add(kv.Key + "=" + kv.Value);
                    sb.AppendLine(h.Id + " " + h.Name + ": " + string.Join(" ", cl.ToArray()) + "  near " + string.Join(",", near.ToArray()));
                }
                return sb.ToString();
            }
            if (sub == "questdump")
            {
                // dev: every quest template (objectives included) to HexRevivalQuests.txt
                StringBuilder sb = new StringBuilder();
                int count = 0;
                foreach (QuestTemplate q in Singleton<TemplateManager>.Instance.QuestTemplates.Values)
                {
                    if (q == null) continue;
                    count++;
                    sb.AppendLine("=== " + q.m_ScriptName + " | " + q.m_Name + " | " + q.m_Id);
                    foreach (QualifiedText t in q.m_Title) sb.AppendLine("  title: " + Describe(t));
                    foreach (QuestObjectiveBase o in q.m_Objectives) sb.AppendLine("  obj: " + Describe(o));
                }
                File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "HexRevivalQuests.txt"), sb.ToString());
                return count + " quests written to HexRevivalQuests.txt";
            }
            if (sub == "transfer")
            {
                // dev: the current champion goes to the other zone (as through the River of Whispers), map open
                CampState st = GetChamp(GameClient.Instance.PlayerProfile.GetCurrentChampion().Id, true);
                Transfer(st);
                return "transfer from " + st.Z.Key;
            }
            if (sub == "zonedump")
            {
                // dev: "camp zonedump AZ2" = data of another zone (quests, map prefabs, scenes, conversations) to HexRevival<zone>.txt
                string zone = parts.Length > 2 ? parts[2].ToUpperInvariant() : "AZ2";
                string num = zone.Substring(2);
                TemplateManager tm = Singleton<TemplateManager>.Instance;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("=== QUESTS");
                foreach (QuestTemplate q in tm.QuestTemplates.Values)
                {
                    if (q == null) continue;
                    string title = q.m_Title != null && q.m_Title.Count > 0 ? q.m_Title[0].m_Text : q.m_TitleOld;
                    sb.AppendLine("quest " + q.m_ScriptName + " '" + q.m_Name + "' title='" + Txt(title) + "' tag=" + q.m_BuildTag);
                    foreach (QuestObjectiveBase o in q.m_Objectives)
                    {
                        string ot = o.m_Title != null && o.m_Title.Count > 0 ? o.m_Title[0].m_Text : "";
                        QuestObjectiveEncounter enc = o as QuestObjectiveEncounter;
                        QuestObjectiveDungeon dun = o as QuestObjectiveDungeon;
                        QuestObjectiveConversation conv = o as QuestObjectiveConversation;
                        string what = enc != null ? "enc " + SceneLabel(tm, enc.m_EncounterId) : dun != null ? "dungeon " + SceneLabel(tm, dun.m_DungeonId)
                            : conv != null ? "conv " + (conv.m_ConversationIdOld.IsValid ? ConvName(conv.m_ConversationIdOld.ToString()) : "") + (conv.m_ConversationIdList != null ? " +" + conv.m_ConversationIdList.Count : "") : o.GetType().Name;
                        sb.AppendLine("  obj " + o.m_QuestLocationId + " '" + Txt(ot) + "' " + what);
                    }
                }
                sb.AppendLine("=== PREFABS");
                foreach (string path in new[] { "campaign/az0" + num + "/nodes", "campaign/azmap0" + num + "/azmap0" + num, "campaign/azmap/azmap0" + num, "campaign/az0" + num + "/azmap", "campaign/az0" + num + "/prefabs/nodes", "campaign/azmap" + num + "/azmap" + num })
                {
                    GameObject prefab = Resources.Load(path) as GameObject;
                    if (prefab == null) { sb.AppendLine("  " + path + ": not found"); continue; }
                    sb.AppendLine("  " + path + ":");
                    foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
                    {
                        UIDungeonMapNodeData nd = t.GetComponent<UIDungeonMapNodeData>();
                        if (nd != null) sb.AppendLine("    node " + t.name + " fork=" + nd.IsPathFork + " pos=" + t.localPosition);
                        else if (t.name.StartsWith("Path_")) sb.AppendLine("    " + t.name);
                    }
                }
                try
                {
                    foreach (GameObject g in Resources.LoadAll<GameObject>("campaign/az0" + num))
                        sb.AppendLine("  loadall " + g.name + " (" + g.GetComponentsInChildren<UIDungeonMapNodeData>(true).Length + " map nodes)");
                }
                catch (Exception e) { sb.AppendLine("  loadall: " + e.Message); }
                sb.AppendLine("=== SCENES");
                foreach (SceneData sc in tm.CampaignScenes.Values)
                {
                    if (sc == null || sc.m_Name == null) continue;
                    string u = sc.m_Name.Replace(" ", "").ToUpperInvariant();
                    if (!u.StartsWith(zone)) continue;
                    EncounterScene es = sc as EncounterScene;
                    sb.AppendLine("scene " + sc.m_Id + " '" + sc.m_Name + "' " + sc.GetType().Name + (es != null ? " title='" + es.m_Title + "' battle=" + OfflineMenu.IsBattle(es, tm) : ""));
                }
                sb.AppendLine("=== CONVERSATIONS");
                foreach (ConversationTemplate c in tm.ConversationTemplates.Values)
                {
                    if (c == null || c.m_Name == null || !c.m_Name.Replace(" ", "").ToUpperInvariant().StartsWith(zone)) continue;
                    List<string> evs = new List<string>();
                    foreach (ConversationAnswer an in c.m_Answers.Values)
                    {
                        foreach (object ev in an.m_EventsToFireWhenSelected) evs.Add("fire:" + ev.GetType().Name);
                        foreach (object ev in an.m_OnSelectedEvents) { string d = Describe(ev); int i = d.IndexOf("m_EventName=\""); evs.Add(i >= 0 ? d.Substring(i + 13, Math.Min(30, d.Length - i - 13)).Split('"')[0] : ev.GetType().Name); }
                    }
                    sb.AppendLine("conv " + c.m_Id + " '" + c.m_Name + "' owner='" + c.m_OwnerName + "'" + (evs.Count > 0 ? " events=" + string.Join(",", evs.ToArray()) : ""));
                }
                string file = Path.Combine(Directory.GetCurrentDirectory(), "HexRevival" + zone + ".txt");
                File.WriteAllText(file, sb.ToString());
                return "written " + file + " (" + sb.Length + " chars)";
            }
            if (sub == "dungeondump")
            {
                // dev: every DungeonTemplate (AZ1 map and dungeons) with its nodes, their scripted events
                // and the map prefab's objects, to HexRevivalDungeons.txt
                TemplateManager tm = Singleton<TemplateManager>.Instance;
                StringBuilder sb = new StringBuilder();
                foreach (SceneData s in tm.CampaignScenes.Values)
                {
                    DungeonTemplate dt = s as DungeonTemplate;
                    if (dt == null) continue;
                    sb.AppendLine("=== DUNGEON " + dt.m_Id + " '" + dt.m_Name + "' bg='" + dt.m_BackgroundTexture + "' status=" + dt.m_ImplStatus + " tag=" + dt.m_BuildTag + " filter=" + dt.m_DeckFilter + " desc='" + Txt(dt.m_Description) + "'");
                    if (dt.m_DungeonMods != null && dt.m_DungeonMods.Count > 0) sb.AppendLine("  mods " + Value(dt.m_DungeonMods));
                    foreach (DungeonItemData it in dt.m_ItemData)
                    {
                        sb.AppendLine("  item " + it.m_ID + " node=" + it.m_MapNodeId + " '" + it.m_Name + "' title='" + it.m_Title + "' totem=" + it.m_TotemType + " terrain=" + it.m_TerrainType + " scape=" + it.m_NodeScapePath + "/" + it.m_NodeScapeArtNumber);
                        if (it.m_EventsToFireWhenVisited != null)
                            foreach (object ev in it.m_EventsToFireWhenVisited) sb.AppendLine("    ev " + DescribeEvent(ev, tm));
                    }
                }
                bool az2 = Array.IndexOf(parts, "az02") >= 0;
                string[] keys = az2 ? new[] { "GRAVEYARD", "SCRAPTOOTH", "SCUTT", "KUKATAN", "KRAKEN", "PORTSMEARE", "BRUTECROWN", "NOGG", "KAZRAGA", "HAG QUEEN" }
                    : new[] { "CRAYBURN", "TRANQUIL", "ROMOR", "USURPER", "SMOLDERING", "DEVONSHIRE", "SISTER MIDNIGHT" };
                foreach (string folder in az2 ? new[] { "az02/brutecrownbluff", "az02/greatmachinegraveyard", "az02/kraken", "az02/ruinsofkukatan" }
                    : new[] { "az01/crayburncastle", "az01/tranquildream", "az01/fortromor", "az01/theusurper", "az01/smolderingdead", "az01/devonshirekeep" })
                {
                    string path = "campaign/" + folder + "/prefabs/nodes";
                    sb.AppendLine("=== PREFAB " + path);
                    try
                    {
                        GameObject prefab = Resources.Load(path) as GameObject;
                        if (prefab == null) { sb.AppendLine("  not found"); continue; }
                        foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
                        {
                            if (t == prefab.transform) continue;
                            if (t.parent != prefab.transform && !t.name.StartsWith("Path_") && !t.name.StartsWith("Node")) continue;
                            List<string> comps = new List<string>();
                            foreach (Component c in t.GetComponents<Component>()) if (c != null && !(c is Transform)) comps.Add(c.GetType().Name);
                            UIDungeonMapNodeData nd = t.GetComponent<UIDungeonMapNodeData>();
                            sb.AppendLine("  " + t.name + " parent=" + (t.parent != null ? t.parent.name : "-") + " [" + string.Join(",", comps.ToArray()) + "]"
                                + (nd != null ? " fork=" + nd.IsPathFork + " pos=" + t.localPosition : ""));
                        }
                    }
                    catch (Exception e) { sb.AppendLine("  error " + e.Message); }
                }
                sb.AppendLine("=== SCENES");
                foreach (SceneData sc in tm.CampaignScenes.Values)
                {
                    EncounterScene es = sc as EncounterScene;
                    if (es == null || es.m_Name == null) continue;
                    string u = es.m_Name.ToUpperInvariant();
                    if (Array.Exists(keys, k => u.Contains(k)) || (az2 && u.StartsWith("AZ2") == false && u.StartsWith("AZ 2") == false && (u.Contains("BRUTE") || u.Contains("MACHINE"))))
                        sb.AppendLine("  scene " + es.m_Id + " '" + es.m_Name + "' title='" + es.m_Title + "' battle=" + OfflineMenu.IsBattle(es, tm));
                }
                sb.AppendLine("=== CONVERSATIONS");
                foreach (ConversationTemplate c in tm.ConversationTemplates.Values)
                {
                    if (c == null || c.m_Name == null) continue;
                    string u = c.m_Name.ToUpperInvariant();
                    if (Array.Exists(keys, k => u.Contains(k)))
                    {
                        List<string> evs = new List<string>();
                        foreach (ConversationAnswer an in c.m_Answers.Values)
                            foreach (object ev in an.m_OnSelectedEvents) { string d = Describe(ev); int i = d.IndexOf("m_EventName=\""); if (i >= 0) evs.Add(d.Substring(i + 13).Split('"')[0]); }
                        sb.AppendLine("  conv " + c.m_Id + " '" + c.m_Name + "' owner='" + c.m_OwnerName + "'" + (evs.Count > 0 ? " events=" + string.Join(",", evs.ToArray()) : ""));
                    }
                }
                File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), az2 ? "HexRevivalDungeons2.txt" : "HexRevivalDungeons.txt"), sb.ToString());
                return "written HexRevivalDungeons.txt (" + sb.Length + " chars)";
            }
            if (sub == "nodes")
            {
                // dev: every AZ1 node with its scenes and conversations, to HexRevivalNodes.txt
                BuildMap(cz);
                TemplateManager tm = Singleton<TemplateManager>.Instance;
                List<string> lines = new List<string>();
                foreach (MapNode n in cz.Nodes)
                {
                    string convs = "";
                    string number = NumberOf(n);
                    if (number != null)
                        foreach (ConversationTemplate c in tm.ConversationTemplates.Values)
                            if (c != null && n.Z.Matches(NameNumber(c.m_Name, n.Z.Prefix), number)) convs += c.m_Name + " | ";
                    lines.Add(n.Id + "\t" + n.Name + "\t" + n.Type + "\tENC: " + n.Encounter + "\tSCENES: " + n.Scenes + "\tCONVS: " + convs);
                }
                File.WriteAllLines(Path.Combine(Directory.GetCurrentDirectory(), "HexRevivalNodes.txt"), lines.ToArray());
                return lines.Count + " nodes written to HexRevivalNodes.txt";
            }
            if (sub == "sets")
            {
                StringBuilder sb = new StringBuilder();
                TemplateManager tm = Singleton<TemplateManager>.Instance;
                foreach (CardSetTemplate cs in tm.CardSets.Values)
                {
                    if (cs == null) continue;
                    int n = 0;
                    Dictionary<string, int> rar = new Dictionary<string, int>();
                    foreach (CardTemplate t in tm.Cards.Values)
                    {
                        if (t == null || t.m_SetId != cs.m_Id) continue;
                        n++;
                        bool own; try { own = t.IsOwnable(); } catch { own = false; }
                        string k = t.m_CardRarity + (own ? "" : "(x)");
                        int c; rar.TryGetValue(k, out c); rar[k] = c + 1;
                    }
                    List<string> rs = new List<string>();
                    foreach (KeyValuePair<string, int> kv in rar) rs.Add(kv.Key + ":" + kv.Value);
                    sb.AppendLine("set " + cs.m_Id + " no=" + cs.m_SetNo + " '" + cs.m_Name + "' cards=" + n + " " + string.Join(" ", rs.ToArray()));
                }
                foreach (InventoryItemData it in tm.InventoryItems.Values)
                {
                    InventoryCardPack p = it as InventoryCardPack;
                    if (p != null) sb.AppendLine("pack " + p.m_Id + " " + p.m_CardPackType + " '" + p.m_Name + "' set=" + p.m_SetId);
                }
                return sb.ToString();
            }
            if (sub == "map")
            {
                BuildMap(cz);
                StringBuilder sb = new StringBuilder();
                foreach (MapNode n in cz.Nodes)
                    sb.AppendLine(n.Id + " '" + n.Name + "' " + n.Type + " " + (n.Encounter ?? n.Conversation ?? "") + (n.Variants != null ? " variants=" + string.Join(",", new List<string>(n.Variants.Keys).ToArray()) : "") + (n.Panorama != null ? " HUB" : "") + (n.Dungeon != null ? " DUNGEON " + n.Dungeon : "") + " -> " + string.Join(",", new List<string>(Neighbours(cz, n.Id)).ToArray()));
                return sb.ToString();
            }
            return "usage: camp dump [filter] | camp map";
        }

        /// <summary>"camp rewards": the game's reward templates and XP tables.</summary>
        private static string DumpRewards()
        {
            StringBuilder sb = new StringBuilder();
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            foreach (Game.Shared.Rewards.RewardTemplate r in tm.RewardTemplates.Values)
            {
                if (r == null) continue;
                sb.Append("reward ").Append(r.m_Id).Append(" '").Append(r.m_Name).Append("' once=").Append(r.m_OnlyEarnOnce);
                foreach (Game.Shared.Rewards.RewardItemBase it in r.m_AndRewardItems) sb.Append(" AND[").Append(DescribeReward(it, tm)).Append(']');
                foreach (Game.Shared.Rewards.RewardItemBase it in r.m_OrRewardItems) sb.Append(" OR[").Append(DescribeReward(it, tm)).Append(']');
                sb.Append('\n');
            }
            foreach (XpTable x in tm.XpTables.Values)
            {
                if (x == null) continue;
                sb.Append("xptable ").Append(x.m_Id).Append(':');
                foreach (XpTableItem i in x.m_XpTable) sb.Append(' ').Append(i.m_Level).Append('=').Append(i.m_LevelXpRequirement);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private static string DescribeReward(Game.Shared.Rewards.RewardItemBase it, TemplateManager tm)
        {
            Game.Shared.Rewards.RewardFungible f = it as Game.Shared.Rewards.RewardFungible;
            if (f != null) return f.m_RewardType + " " + f.m_Amount;
            Game.Shared.Rewards.RewardCard c = it as Game.Shared.Rewards.RewardCard;
            if (c != null) { CardTemplate t = tm.GetCardTemplate(c.m_CardTemplateId); return "card " + (t != null ? t.m_Name : c.m_CardTemplateId.ToString()); }
            Game.Shared.Rewards.RewardInventoryItem i = it as Game.Shared.Rewards.RewardInventoryItem;
            if (i != null) { InventoryItemData d = tm.GetInventoryItem(i.m_ItemId); return "item " + (d != null ? d.m_Name : i.m_ItemId.ToString()) + " x" + i.m_Amount; }
            Game.Shared.Rewards.RewardLootTable l = it as Game.Shared.Rewards.RewardLootTable;
            if (l != null) return "loot x" + l.m_Amount + " (" + l.m_Entries.Count + " entries)";
            return it != null ? it.GetType().Name : "null";
        }

        /// <summary>A node's scripted event (dev dump): its type and the templates it names.</summary>
        private static string SceneLabel(TemplateManager tm, ResourceId id)
        {
            SceneData sd;
            return tm.CampaignScenes.TryGetValue(id, out sd) && sd != null ? "'" + sd.m_Name + "'" : id.ToString();
        }

        private static string DescribeEvent(object ev, TemplateManager tm)
        {
            if (ev == null) return "null";
            StringBuilder sb = new StringBuilder(ev.GetType().Name);
            foreach (FieldInfo f in ev.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object v; try { v = f.GetValue(ev); } catch { continue; }
                if (v == null) continue;
                string val;
                if (v is ResourceId)
                {
                    ResourceId id = (ResourceId)v;
                    SceneData sd; ConversationTemplate ct;
                    if (tm.CampaignScenes.TryGetValue(id, out sd)) val = "scene '" + sd.m_Name + "' " + id;
                    else if (tm.ConversationTemplates.TryGetValue(id, out ct) && ct != null) val = "conv '" + ct.m_Name + "' " + id;
                    else val = Value(v);
                }
                else val = Value(v);
                sb.Append(' ').Append(f.Name).Append('=').Append(val);
            }
            return sb.ToString();
        }

        private static string Dump(string filter)
        {
            StringBuilder sb = new StringBuilder();
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            SceneData sd;
            if (tm.CampaignScenes.TryGetValue(AZ1TemplateId, out sd))
            {
                sb.AppendLine("template " + sd.GetType().Name + " '" + sd.m_Name + "'");
                DungeonTemplate dt = sd as DungeonTemplate;
                if (dt != null)
                    foreach (DungeonItemData it in dt.m_ItemData)
                        sb.AppendLine("  item " + it.m_ID + " " + it.m_MapNodeId + " '" + it.m_Name + "' title='" + it.m_Title + "' totem=" + it.m_TotemType + " terrain=" + it.m_TerrainType
                            + " events=" + (it.m_EventsToFireWhenVisited != null ? it.m_EventsToFireWhenVisited.Count : 0));
            }
            else sb.AppendLine("AZ1 template not found");
            foreach (SceneData s in tm.CampaignScenes.Values)
                if (s != null && s.m_Name != null && s.m_Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    sb.AppendLine("scene " + s.GetType().Name + " " + s.m_Id + " '" + s.m_Name + "'");
            foreach (ConversationTemplate c in tm.ConversationTemplates.Values)
                if (c != null && c.m_Name != null && c.m_Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    sb.AppendLine("conv " + c.m_Id + " '" + c.m_Name + "'");
            try
            {
                GameObject prefab = Resources.Load(AZ1.NodesPrefab) as GameObject;
                if (prefab == null) sb.AppendLine("nodes prefab not found");
                else
                    foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
                        if (t.name.StartsWith("Path_") || t.name.StartsWith("Node"))
                            sb.AppendLine("prefab " + t.name + " (parent " + (t.parent != null ? t.parent.name : "-") + ")");
            }
            catch (Exception e) { sb.AppendLine("prefab: " + e.Message); }
            return sb.ToString();
        }
    }
}
