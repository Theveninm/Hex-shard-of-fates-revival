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
using Game.Shared.Eval.Data;
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
    public static class Campaign
    {
        /// <summary>AZ1 (Howling Plains) map template: the DungeonTemplate listing the map nodes.</summary>
        public static readonly ResourceId AZ1TemplateId = new ResourceId(new Guid("742c6fc1-77e5-4029-bf9e-4e8af387857a"));
        private const string AZ1Name = "AZ1";
        private const string AZ1NodesPrefab = "campaign/az01/nodes";
        private const string AZ1BackgroundPrefab = "campaign/azmap/azmap";
        private const string StartNode = "Node001";

        // ------------------------------------------------------------------
        // Map model (built once from the game data)
        // ------------------------------------------------------------------

        private class MapNode
        {
            public string Id;            // "Node001", the map prefab's node name
            public string Name;          // location name sent back by the client (unique)
            public string Type = "Empty"; // LocationType: Encounter, Convo or Empty
            public string Encounter;     // EncounterScene GUID
            public string Conversation;  // ConversationTemplate GUID
            public string Scenes = "";   // names and titles of every scene of the node (reward lookups, e.g. "Shroom Haus")
            public bool IsHaus { get { return Scenes.ToUpperInvariant().Contains("SHROOM HAUS"); } }
            public string RepeatConversation; // "... - Repeat Encounters" conversation, offered once the node is completed (Army of Myth)
        }

        private static List<MapNode> s_Nodes;
        private static Dictionary<string, HashSet<string>> s_Edges;

        private static MapNode FindNode(string idOrName)
        {
            BuildMap();
            return s_Nodes.Find(n => n.Id == idOrName || n.Name == idOrName);
        }

        private static void BuildMap()
        {
            if (s_Nodes != null) return;
            s_Nodes = new List<MapNode>();
            s_Edges = new Dictionary<string, HashSet<string>>();
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            SceneData sd;
            DungeonTemplate dt = tm.CampaignScenes.TryGetValue(AZ1TemplateId, out sd) ? sd as DungeonTemplate : null;
            if (dt == null) { Debug.LogError("[HexRevival] [Campaign] AZ1 template not found"); return; }

            HashSet<string> names = new HashSet<string>();
            foreach (DungeonItemData it in dt.m_ItemData)
            {
                if (string.IsNullOrEmpty(it.m_MapNodeId)) continue;
                MapNode n = new MapNode { Id = it.m_MapNodeId };
                string title = !string.IsNullOrEmpty(it.m_Title) ? it.m_Title : it.m_Name;
                n.Name = string.IsNullOrEmpty(title) || names.Contains(title) ? n.Id : title;
                names.Add(n.Name);
                ResolveContent(n, tm);
                s_Nodes.Add(n);
            }

            // Paths: the map prefab holds one object per path, named "Path_NodeA_NodeB".
            try
            {
                GameObject prefab = Resources.Load(AZ1NodesPrefab) as GameObject;
                if (prefab != null)
                    foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
                    {
                        string[] p = t.name.Split('_');
                        if (p.Length != 3 || p[0] != "Path") continue;
                        AddEdge(p[1], p[2]);
                        AddEdge(p[2], p[1]);
                    }
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Campaign] map paths: " + e); }
            Debug.Log("[HexRevival] [Campaign] AZ1: " + s_Nodes.Count + " nodes, " + s_Edges.Count + " connected");
        }

        private static void AddEdge(string a, string b)
        {
            HashSet<string> set;
            if (!s_Edges.TryGetValue(a, out set)) s_Edges[a] = set = new HashSet<string>();
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

        /// <summary>Resources the AI starts with in play in the next campaign battle (Army of Myth difficulty).</summary>
        private static int s_Difficulty;

        /// <summary>Called on every server update (Server.Flush): applies the difficulty once the mulligan is over.</summary>
        public static void OnSessionTick(StandaloneSession ss)
        {
            if (s_Difficulty <= 0 || ss == null || (int)ss.CurrentTurnPhase <= (int)Game.Shared.Mechanics.ETurnPhases.Mulligan) return;
            Player ai = Cheats.FindPlayer(ss, "ai");
            if (ai != null)
            {
                Cheats.AddResources(ss, ai, s_Difficulty);
                Debug.Log("[HexRevival] [Campaign] difficulty: AI starts with " + s_Difficulty + " resource(s)");
            }
            s_Difficulty = 0;
        }

        /// <summary>Last campaign event sent by the client (e.g. the conversation choice "Diff_6" of the Army of Myth).</summary>
        public static string LastEvent;

        /// <summary>True when the node is a battle (its rewards come with the win, not with a conversation).</summary>
        public static bool NodeHasBattle(string node)
        {
            MapNode n = FindNode(node);
            return n != null && n.Encounter != null;
        }

        /// <summary>Names and titles of the scenes of a map node ("" if unknown).</summary>
        public static string NodeScenes(string node)
        {
            MapNode n = FindNode(node);
            return n != null ? n.Scenes : "";
        }

        private static IEnumerable<string> Neighbours(string node)
        {
            HashSet<string> set;
            return s_Edges.TryGetValue(node, out set) ? (IEnumerable<string>)set : new string[0];
        }

        /// <summary>
        /// Content of a map node, from the game's templates named after the node: encounter scenes
        /// "AZ 1 - NODE 03 - WILD CUB" and conversations "AZ1 - Node 3 - Feralroot Bear".
        /// A scene with an AI deck is a battle; otherwise the node's first conversation is used
        /// (dialog, panorama, dungeon and reward scenes are not handled yet: the node is empty).
        /// </summary>
        private static void ResolveContent(MapNode n, TemplateManager tm)
        {
            string number = NodeNumber(n.Id);
            if (number == null) return;
            foreach (SceneData s in tm.CampaignScenes.Values)
            {
                EncounterScene es = s as EncounterScene;
                if (es != null && NameNumber(es.m_Name, "AZ1-NODE") == number) n.Scenes += es.m_Name + " / " + es.m_Title + " | ";
            }
            foreach (ConversationTemplate c in tm.ConversationTemplates.Values)
                if (c != null && NameNumber(c.m_Name, "AZ1-NODE") == number && c.m_Name.ToUpperInvariant().Contains("REPEAT ENCOUNTERS")) n.RepeatConversation = c.m_Id.ToString();
            foreach (SceneData s in tm.CampaignScenes.Values)
            {
                EncounterScene es = s as EncounterScene;
                if (es == null || NameNumber(es.m_Name, "AZ1-NODE") != number || !OfflineMenu.IsBattle(es, tm)) continue;
                n.Type = "Encounter";
                n.Encounter = es.m_Id.ToString();
                return;
            }
            ConversationTemplate best = null;
            foreach (ConversationTemplate c in tm.ConversationTemplates.Values)
            {
                if (c == null || NameNumber(c.m_Name, "AZ1-NODE") != number) continue;
                if (best == null || ConversationRank(c.m_Name) < ConversationRank(best.m_Name)) best = c;
            }
            if (best != null)
            {
                n.Type = "Convo";
                n.Conversation = best.m_Id.ToString();
            }
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
            string s = id.Substring(4).TrimStart('0');
            return s.Length > 0 ? s : null;
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
            s = s.TrimStart('0');
            return s.Length > 0 ? s : null;
        }

        // ------------------------------------------------------------------
        // Campaign state (one per champion)
        // ------------------------------------------------------------------

        private class CampState
        {
            public ulong ChampId;
            public bool Started;
            public string ALoc;                 // active location (name), null when exploring
            public string LastNode = StartNode;
            public HashSet<string> Completed = new HashSet<string>();
            public HashSet<string> Visible = new HashSet<string>();
            public List<string> VisitedNodes = new List<string>();
            public List<string> VisitedPaths = new List<string>();
            public HashSet<string> HausReady = new HashSet<string>(); // Shroom Hauses whose card choice window is open (after the Shroomkin's conversation)
            public ulong CampId { get { return ChampId; } } // one campaign per champion
        }

        private static Dictionary<ulong, CampState> s_States;
        private static CampState s_Battle; // campaign whose encounter is being played

        /// <summary>Champion playing the current campaign battle (0 outside campaign battles).</summary>
        public static ulong BattleChampion { get { return s_Battle != null ? s_Battle.ChampId : 0UL; } }

        private static string FilePath { get { return Saves.File("campaign.txt"); } }

        /// <summary>Forgets the loaded campaigns (another save was selected).</summary>
        public static void Reset()
        {
            s_States = null;
            s_Battle = null;
        }

        /// <summary>Cheat: every node of the map becomes visible and selectable.</summary>
        public static bool RevealAll(ulong champId)
        {
            CampState st = Get(champId, false);
            if (st == null) return false;
            BuildMap();
            foreach (MapNode n in s_Nodes) st.Visible.Add(n.Id);
            Save();
            return true;
        }

        /// <summary>True if this champion has a saved campaign (used for LastCampaignID).</summary>
        public static bool HasSave(ulong champId)
        {
            LoadAll();
            return s_States.ContainsKey(champId);
        }

        private static CampState Get(ulong id, bool create)
        {
            LoadAll();
            CampState st;
            if (s_States.TryGetValue(id, out st) || !create) return st;
            BuildMap();
            st = new CampState { ChampId = id };
            st.Completed.Add(StartNode);
            st.VisitedNodes.Add(StartNode);
            Reveal(st, StartNode);
            s_States[id] = st;
            Save();
            return st;
        }

        /// <summary>A node and its neighbours become visible (and selectable).</summary>
        private static void Reveal(CampState st, string node)
        {
            st.Visible.Add(node);
            foreach (string n in Neighbours(node)) st.Visible.Add(n);
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
                    CampState st = new CampState { ChampId = ulong.Parse(p[0]), Started = p[1] == "1", LastNode = p[2] };
                    st.Completed.UnionWith(SplitList(p[3]));
                    st.Visible.UnionWith(SplitList(p[4]));
                    st.VisitedNodes.AddRange(SplitList(p[5]));
                    st.VisitedPaths.AddRange(SplitList(p[6]));
                    if (p.Length > 7) st.HausReady.UnionWith(SplitList(p[7]));
                    s_States[st.ChampId] = st;
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
                    lines.Add(string.Join("|", new[] { st.ChampId.ToString(), st.Started ? "1" : "0", st.LastNode,
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
            BuildMap();
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
            foreach (MapNode n in s_Nodes)
            {
                bool visible = st.Visible.Contains(n.Id);
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["name"] = n.Name;
                d["node"] = n.Id;
                bool repeat = n.RepeatConversation != null && st.Completed.Contains(n.Id);
                d["type"] = repeat ? "Convo" : n.Type;
                d["visible"] = visible;
                d["enabled"] = visible;
                d["completed"] = st.Completed.Contains(n.Id);
                d["repeatable"] = repeat;
                d["allow_cancel"] = true;
                if (n.Encounter != null) d["encounter"] = n.Encounter;
                if (repeat) d["conversationId"] = n.RepeatConversation;
                else if (n.Conversation != null) d["conversationId"] = n.Conversation;
                if (n.IsHaus && st.HausReady.Contains(n.Id) && !st.Completed.Contains(n.Id))
                {
                    // The game's card choice window (Src.Game, as hex-server serves it): 3 card templates to pick from.
                    d["type"] = "ShroomHaus";
                    d.Remove("conversationId");
                    d["autostart"] = true;
                    d["choices"] = new List<object>(Progression.ShroomHausChoices(n.Id).ToArray());
                    d["selection-type"] = "Shroomkin_Haus";
                    d["selection-desc"] = "Shroomkin_Instructions";
                }
                gs.VisLocs.Add(new Location(d));
                Dictionary<string, object> nd = new Dictionary<string, object>();
                nd["id"] = n.Id;
                nd["type"] = "DEFAULT";
                gs.LocNodes.Add(new Node(n.Id, nd));
                if (n.Encounter != null)
                {
                    Dictionary<string, object> ed = new Dictionary<string, object>();
                    ed["encscene"] = n.Encounter;
                    gs.Encounters.Add(new Game.Shared.Eval.Data.Encounter(n.Encounter, ed));
                }
            }
            Dictionary<string, object> pub = new Dictionary<string, object>();
            pub["CampaignGroup"] = AZ1Name;
            pub["visited_nodes"] = new List<object>(st.VisitedNodes.ToArray());
            pub["visited_paths"] = new List<object>(st.VisitedPaths.ToArray());
            pub["quest_nodes"] = new List<object>();
            gs.PublicState = new QueryState(pub);
            return gs;
        }

        private static CampSummary Summary(CampState st)
        {
            TemplateTypeInfo info = new TemplateTypeInfo(AZ1Name, "AREA", null, false, false, "", "",
                AZ1BackgroundPrefab, AZ1NodesPrefab, AZ1TemplateId.ToString());
            return new CampSummary(st.CampId, info, CampaignType.AREA, GameClient.Instance.PlayerProfile.ReckoningID, AZ1Name, true, 1, null);
        }

        // ------------------------------------------------------------------
        // Answers (delivered on the next frame, like a network answer)
        // ------------------------------------------------------------------

        // Answers are delivered a few frames later, like network answers: the map screen asks for
        // the state in the same frame it creates the map nodes, which are only initialized by
        // Unity on the next frame (UIDungeonMapNode.Start).
        private const int AnswerDelayFrames = 5;
        private static readonly List<KeyValuePair<int, Action>> s_Answers = new List<KeyValuePair<int, Action>>();

        private static void Enqueue(Action a) { s_Answers.Add(new KeyValuePair<int, Action>(Time.frameCount + AnswerDelayFrames, a)); }

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
        // ClientCampaignManager entry points (Harmony prefixes, see Plugin.cs)
        // ------------------------------------------------------------------

        public static void QueryCurrentCampaign(ulong champId, ClientCampaignManager.StateCallback callback)
        {
            CampState st = Get(champId, true);
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
            Answer(Get(champId, true), callback);
        }

        public static void GetSummary(IEnumerable<ulong> ids, ClientCampaignManager.GetSummaryCallback callback)
        {
            List<CampSummary> res = new List<CampSummary>();
            foreach (ulong id in ids)
            {
                CampState st = Get(id, false);
                if (st != null) res.Add(Summary(st));
            }
            CampSummary[] arr = res.ToArray();
            Enqueue(() => callback(arr));
        }

        public static void GetActiveStatus(ulong champId, CampaignType type, string template, ClientCampaignManager.GetActiveStatusCallback callback)
        {
            List<TemplateInfo> res = new List<TemplateInfo>();
            CampState st = Get(champId, false);
            if (st != null && (type == CampaignType.AREA || type == CampaignType.ANY))
                res.Add(new TemplateInfo(st.CampId, CampaignType.AREA, GameClient.Instance.PlayerProfile.ReckoningID, AZ1Name, 1, null));
            TemplateInfo[] arr = res.ToArray();
            Enqueue(() => callback(arr));
        }

        public static void QueryState(ulong campId, ClientCampaignManager.StateCallback callback)
        {
            Answer(Get(campId, false), callback);
        }

        public static void Start(ulong campId, ClientCampaignManager.StateCallback callback)
        {
            CampState st = Get(campId, false);
            if (st != null && !st.Started) { st.Started = true; Save(); }
            Answer(st, callback);
        }

        public static void Forfeit(ulong campId, ClientCampaignManager.StateCallback callback)
        {
            CampState st = Get(campId, false);
            if (st != null) st.ALoc = null;
            Answer(st, callback);
        }

        public static void StartLocation(ulong campId, string location, ClientCampaignManager.StateCallback callback)
        {
            CampState st = Get(campId, false);
            if (st == null) return;
            MapNode n = FindNode(location);
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
            CampState st = Get(campId, false);
            if (st == null) return;
            MapNode n = FindNode(location);
            if (n != null) Complete(st, n);
            st.ALoc = null;
            Save();
            Answer(st, callback);
        }

        public static void SendEvent(ulong campId, string evt, object[] args, ClientCampaignManager.StateCallback callback)
        {
            CampState st = Get(campId, false);
            if (st == null) return;
            string arg0 = args != null && args.Length > 0 && args[0] != null ? args[0].ToString() : null;
            Debug.Log("[HexRevival] [Campaign] event " + evt + (arg0 != null ? " " + arg0 : ""));
            if (evt != "visit_node" && evt != "visit_path" && evt != "pass_node") LastEvent = evt;
            MapNode active = st.ALoc != null ? FindNode(st.ALoc) : null;
            switch (evt)
            {
                case "visit_node":
                    if (arg0 != null && !st.VisitedNodes.Contains(arg0)) st.VisitedNodes.Add(arg0);
                    if (arg0 != null) st.LastNode = arg0;
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
                    if (active != null && active.IsHaus && !st.Completed.Contains(active.Id))
                    {
                        // Shroomkin's conversation done: the card choice window opens next (location stays active).
                        st.HausReady.Add(active.Id);
                        break;
                    }
                    goto case "empty_done";
                case "shroom_choice":
                    if (active != null && active.IsHaus && !st.Completed.Contains(active.Id) && Progression.OnShroomHausChoice(st.ChampId, active.Id, arg0))
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
                    if (active != null) Complete(st, active);
                    st.ALoc = null;
                    if (active != null && evt == "conv_done") Progression.OnCampaignConversationDone(st.ChampId, active.Id);
                    break;
                case "enc_cancel":
                case "cancel":
                    st.ALoc = null;
                    break;
                case "start":
                    if (active != null && active.Encounter != null) StartBattle(st, active);
                    break;
                default:
                    // Army of Myth repeat: "Diff_N" = fight the army with N resources in play (0 = level 1); "Cancel" = no fight.
                    int diff;
                    if (active != null && active.Encounter != null && evt.StartsWith("Diff_") && int.TryParse(evt.Substring(5), out diff))
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

        /// <summary>Difficulty of the campaign battle in progress (Army of Myth: resources the AI started with), for its rewards.</summary>
        public static int BattleDifficulty { get; private set; }

        private static void StartBattle(CampState st, MapNode node)
        {
            BattleDifficulty = s_Difficulty;
            Debug.Log("[HexRevival] [Campaign] battle " + node.Id + " difficulty " + BattleDifficulty);
            UID deck = UID.Invalid;
            try
            {
                PlayerChampion ch = GameClient.Instance.PlayerProfile.GetPvEChampionById(st.ChampId);
                if (ch != null && ch.LastDeckID != 0) deck = new UID(UID.Type.Deck, ch.LastDeckID);
            }
            catch { }
            s_Battle = st;
            // The battle HUD reads the level from the profile's current champion.
            try { typeof(PlayerProfile).GetField("m_CurrentChampionId", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(GameClient.Instance.PlayerProfile, st.ChampId); }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Campaign] current champion: " + e.Message); }
            Enqueue(() => Patches.StartMatch(deck, new ResourceId(node.Encounter), null, "campaign " + node.Name));
        }

        /// <summary>Called once when a match ends. Returns false if it was not a campaign battle.</summary>
        public static bool OnGameEnded(bool won)
        {
            s_Difficulty = 0;
            CampState st = s_Battle;
            // BattleDifficulty is read by the rewards below, then reset
            if (st == null) return false;
            s_Battle = null;
            MapNode node = st.ALoc != null ? FindNode(st.ALoc) : null;
            if (won && node != null) Complete(st, node);
            st.ALoc = null;
            Save();
            Debug.Log("[HexRevival] [Campaign] battle " + (node != null ? node.Name : "?") + (won ? " won" : " lost") + ", difficulty " + BattleDifficulty);
            if (won && node != null) Progression.OnCampaignBattleWon(st.ChampId, node.Id);
            BattleDifficulty = 0;
            return true;
        }

        // ------------------------------------------------------------------
        // Test command
        // ------------------------------------------------------------------

        /// <summary>"camp dump [filter]": AZ1 data as the client knows it; "camp map": the map model.</summary>
        public static string Command(string[] parts)
        {
            string sub = parts.Length > 1 ? parts[1] : "";
            if (sub == "dump") return Dump(parts.Length > 2 ? parts[2] : "AZ");
            if (sub == "rewards") return DumpRewards();
            if (sub == "convs")
            {
                // dev: text and events of the AZ1 conversations (all, or those whose name contains the filter) to HexRevivalConvs.txt
                string filter = parts.Length > 2 ? string.Join(" ", parts, 2, parts.Length - 2).ToUpperInvariant() : "";
                StringBuilder sb = new StringBuilder();
                int count = 0;
                foreach (ConversationTemplate c in Singleton<TemplateManager>.Instance.ConversationTemplates.Values)
                {
                    if (c == null || !c.m_Name.StartsWith("AZ1") || (filter != "" && !c.m_Name.ToUpperInvariant().Contains(filter))) continue;
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
                CampState st = Get(champ, false);
                MapNode n = FindNode(parts[2]);
                if (st == null || n == null || n.Encounter == null) return "no campaign for champion " + champ + " or no battle at " + parts[2];
                st.ALoc = n.Name;
                LastEvent = parts.Length > 3 ? parts[3] : "start";
                int diff;
                if (LastEvent.StartsWith("Diff_") && int.TryParse(LastEvent.Substring(5), out diff)) s_Difficulty = diff;
                StartBattle(st, n);
                return "fight " + n.Id + " (" + LastEvent + ") for champion " + champ;
            }
            if (sub == "loc" && parts.Length > 2)
            {
                // dev: the location data sent to the client for a node, for the current champion
                ulong champ = GameClient.Instance.PlayerProfile.GetCurrentChampion().Id;
                CampState st = Get(champ, false);
                MapNode mn = FindNode(parts[2]);
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
                CampState st = Get(champ, false);
                MapNode n = FindNode(parts[2]);
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
                BuildMap();
                TemplateManager tm = Singleton<TemplateManager>.Instance;
                StringBuilder sb = new StringBuilder();
                foreach (MapNode h in s_Nodes)
                {
                    if (!h.Scenes.ToUpperInvariant().Contains("SHROOM HAUS")) continue;
                    Dictionary<string, int> dist = new Dictionary<string, int> { { h.Id, 0 } };
                    Queue<string> q = new Queue<string>();
                    q.Enqueue(h.Id);
                    while (q.Count > 0)
                    {
                        string cur = q.Dequeue();
                        if (dist[cur] >= 3) continue;
                        foreach (string nb in Neighbours(cur)) if (!dist.ContainsKey(nb)) { dist[nb] = dist[cur] + 1; q.Enqueue(nb); }
                    }
                    Dictionary<string, int> colors = new Dictionary<string, int>();
                    List<string> near = new List<string>();
                    foreach (KeyValuePair<string, int> kv in dist)
                    {
                        MapNode n = FindNode(kv.Key);
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
            if (sub == "nodes")
            {
                // dev: every AZ1 node with its scenes and conversations, to HexRevivalNodes.txt
                BuildMap();
                TemplateManager tm = Singleton<TemplateManager>.Instance;
                List<string> lines = new List<string>();
                foreach (MapNode n in s_Nodes)
                {
                    string convs = "";
                    string number = NodeNumber(n.Id);
                    if (number != null)
                        foreach (ConversationTemplate c in tm.ConversationTemplates.Values)
                            if (c != null && NameNumber(c.m_Name, "AZ1-NODE") == number) convs += c.m_Name + " | ";
                    lines.Add(n.Id + "\t" + n.Name + "\t" + n.Type + "\tSCENES: " + n.Scenes + "\tCONVS: " + convs);
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
                BuildMap();
                StringBuilder sb = new StringBuilder();
                foreach (MapNode n in s_Nodes)
                    sb.AppendLine(n.Id + " '" + n.Name + "' " + n.Type + " " + (n.Encounter ?? n.Conversation ?? "") + " -> " + string.Join(",", new List<string>(Neighbours(n.Id)).ToArray()));
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
                GameObject prefab = Resources.Load(AZ1NodesPrefab) as GameObject;
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
