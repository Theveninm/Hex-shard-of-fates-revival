using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Game.Client;
using Game.Client.Campaign;
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
    /// AZ1 dungeons (Castle Crayburn, the Tranquil Dream, Fort Romor, the Usurper, the Smoldering
    /// Dead, Devonshire Keep), played as the game's DUNGEON campaigns: their own map (the client's
    /// prefab "campaign/az01/&lt;dungeon&gt;/prefabs/nodes"), entered from the AZ1 map node.
    ///
    /// The game's DungeonTemplates only list the rooms; what the server did in each room is lost.
    /// Each room is rebuilt here as a list of steps (a conversation, a battle and its success / fail
    /// conversation), from the conversation and encounter names (Src.Game) and the let's plays
    /// (Src.Video, see docs/PROGRESSION.md). A lost battle costs a dungeon life (3, more with some
    /// talents, Src.Game TalentManager.GetTotalDungeonLives) and the room can be tried again; the last
    /// life lost ends the run (the client's "dungeon failed" window). Castle Crayburn, the starter
    /// dungeon, has no life limit.
    ///
    /// One run at a time per champion (campaign id DunBase + champion), saved in HexRevival.dungeons.txt.
    /// </summary>
    public static class Dungeons
    {
        public const string CrayburnId = "5bcba43a-95c7-44b4-ba09-a3555a5edf05";
        public const string UsurperId = "39f31d36-c96d-4858-a581-6bc4dfc90b5f";

        /// <summary>Dungeon campaign ids: DunBase + champion (panoramas below, quests above).</summary>
        internal const ulong DunBase = 1500000;
        public static bool IsDunCamp(ulong id) { return id >= DunBase && !Quests.IsQuestCamp(id); }

        // ------------------------------------------------------------------
        // Dungeon content
        // ------------------------------------------------------------------

        /// <summary>
        /// One step of a room. Names may hold tokens, resolved per champion and run:
        /// {R} race ("Shin'hare"), {F} faction ("Underworld" / "Ardent"), {C} the room's dream creature,
        /// {D} its difficulty, {RANK} the creature's rank, {FORM} Sister Midnight's form, {SHEP} who
        /// died before the Shepherd's Rest. "a|b" = first name found. A name may also be a template id.
        /// </summary>
        private class Step
        {
            public string Conv, Enc, Success, Fail, Reward;
            public string[] Requires = new string[0];  // rooms that must be cleared first
            public bool RequireAny;                     // one of them is enough
            // Dialogue choices (ServerScriptEvent of the conversation's answers): event -> the battle's
            // encounter (null: the step's own), Skip (no battle, the step is done) or Stay (nothing happens).
            public Dictionary<string, string> On;
            public Dictionary<string, string> OnReward; // event -> reward of that battle
        }

        private const string Skip = "<skip>", Stay = "<leave>";

        private class Room
        {
            public string Id;
            public List<Step> Steps = new List<Step>();
            public bool Final;                          // clearing it finishes the dungeon
        }

        private class Def
        {
            public string Key, Id, Name, Folder, AZ1Node, Start;
            public bool Starter;                        // Castle Crayburn: lost battles are fought again
            public Dictionary<string, Room> Rooms = new Dictionary<string, Room>();
            public Dictionary<string, HashSet<string>> Edges;
            public string NodesPrefab { get { return "campaign/az01/" + Folder + "/prefabs/nodes"; } }
            public string BackgroundPrefab { get { return "campaign/az01/" + Folder + "/prefabs/background"; } }
            public Def Add(string id, params Step[] steps) { Rooms[id] = new Room { Id = id, Steps = new List<Step>(steps) }; return this; }
            public Def Final(string id) { Rooms[id].Final = true; return this; }
        }

        private static Step Talk(string conv) { return new Step { Conv = conv }; }

        /// <summary>The usual room: "&lt;prefix&gt;&lt;title&gt;" conversation, battle, "... Success" / "... Fail".</summary>
        private static Step Fight(string prefix, string title, string enc, string reward)
        {
            return new Step { Conv = prefix + title, Enc = enc, Success = prefix + title + " Success", Fail = prefix + title + " Fail", Reward = reward };
        }

        private static readonly string[] Creatures = { "Turtle", "Skarn", "Spider", "Bear", "Eagle" };
        private static readonly string[] Forms = { "Retribution", "Conquest", "Savagery", "Instinct", "Cunning" };
        private static readonly string[] DreamRooms = { "NodeB", "NodeC", "NodeD", "NodeE", "NodeF" };

        private static List<Def> s_Defs;

        private static List<Def> Defs()
        {
            if (s_Defs != null) return s_Defs;
            s_Defs = new List<Def>();

            // Castle Crayburn, the starter dungeon of every race (Src.Game: "Crayburn Castle - <race> - ..."
            // conversations and "<race> Tutorial ..." battles; the tower's race guide gives the quest).
            const string cc = "Crayburn Castle - {R} - ";
            s_Defs.Add(new Def { Key = "crayburn", Id = CrayburnId, Name = "Castle Crayburn", Folder = "crayburncastle", Start = "WatchTower", Starter = true }
                .Add("WatchTower", Talk(cc + "Quest Start"), Talk(cc + "The Watchtower"))
                .Add("Drawbridge", Talk(cc + "The Drawbridge"))
                .Add("CastleGate", Fight(cc, "Castle Gatehouse", "{R} Tutorial Castle Gatehouse", "crayburn/CastleGate"))
                .Add("InnerBailey", Talk(cc + "Inner Bailey"))
                .Add("TowerGate", Fight(cc, "Tower Gatehouse", "{R} Tutorial Tower Gatehouse", "crayburn/TowerGate"))
                .Add("PenworthTower", Fight(cc, "Tower of Penworth", "{R} Tutorial Tower of Penworth", "crayburn/PenworthTower"), Talk(cc + "Quest End"))
                .Final("PenworthTower"));

            // The Tranquil Dream (Node 40): the Tipi, then Sister Midnight's dream; five dream creatures in
            // random rooms, harder with each one (Src.Video), then Sister Midnight in a random form.
            Def td = new Def { Key = "tranquildream", Id = "d657fcad-9dbe-4acc-bb62-2495fd4a4628", Name = "The Tranquil Dream", Folder = "tranquildream", AZ1Node = "Node040", Start = "NodeA" }
                .Add("NodeA", Talk("Tranquil Dream - Wigwam"))
                .Add("NodeG", Talk("Tranquil Dream - Sister Midnight"),
                    new Step { Conv = "Tranquil Dream - Sister Midnight 2", Enc = "Tranquil Dream Sister Midnight Form of {FORM}", Success = "Tranquil Dream - Sister Midnight 2 Success", Fail = "Tranquil Dream - Sister Midnight 2 Fail", Reward = "tranquildream/midnight", Requires = DreamRooms })
                .Final("NodeG");
            foreach (string r in DreamRooms)
                td.Add(r, new Step { Conv = "Tranquil Dream - The Dream {C}", Enc = "Tranquil Dream {C} {D}", Success = "Tranquil Dream - The Dream {C} Success", Fail = "Tranquil Dream - The Dream {C} Fail", Reward = "tranquildream/rank{RANK}" });
            s_Defs.Add(td);

            // Fort Romor (Node 11, Underworld quest). The template's rooms (Stables, Barracks, Command Post,
            // Defense) are not the prefab's node names: they are NodeB to NodeE in the template's order, which
            // matches the gate's description (Src.Game: Barracks north, Command Post northwest, Stables
            // northeast). Each room's foe is its conversation's speaker (the template's encounter list is
            // shifted by one room, as in the Usurper's).
            const string fr = "Fort Romor - ";
            s_Defs.Add(new Def { Key = "fortromor", Id = "d1461f76-6483-4514-b24b-a499fb65a209", Name = "Fort Romor", Folder = "fortromor", AZ1Node = "Node011", Start = "Node001" }
                .Add("NodeA", Talk(fr + "Ruined Gate - Entrance"))
                .Add("NodeB", Fight(fr, "Stables", fr + "Legionnaire of Gawaine", "fortromor/Stables"))
                .Add("NodeC", Fight(fr, "Barracks", fr + "Rhonwen", "fortromor/Barracks"))
                .Add("NodeD", Fight(fr, "Command Post", fr + "Commander Hawthorne", "fortromor/CmdPost"))
                .Add("NodeE", Defense("Lord Benjamin", 1), Defense("Lord Alexander", 2), Defense("Lord Adam", 3))
                .Final("NodeE"));

            // The Usurper (Node 8, Ardent quest): the Bleak Citadel, Ghuuhg (Hall of Vermin) and / or Mistsim
            // (Chamber of Awakening), Finanif at the Shepherd's Rest, then Verrev.
            const string us = "Usurper - ";
            s_Defs.Add(new Def { Key = "usurper", Id = UsurperId, Name = "The Usurper", Folder = "theusurper", AZ1Node = "Node008", Start = "Entrance" }
                .Add("Entrance", Talk(us + "Entrance"))
                .Add("NodeA", new Step { Conv = us + "Gates of the Bleak Citadel", Enc = "Usurper Gates Of The Bleak Citadel", Success = us + "Gates of the Bleak Citadel Success", Fail = us + "Gates of the Bleak Citadel Success Fail", Reward = "usurper/NodeA" })
                .Add("NodeB", Fight(us, "Grimskull Hall", "Usurper Grimskull Hall", "usurper/NodeB"))
                .Add("NodeC", Fight(us, "Hall of Vermin", "Usurper Hall of Vermin", "usurper/NodeC"))
                .Add("NodeD", Fight(us, "Chamber of Awakening", "Usurper Chamber of Awakening", "usurper/NodeD"))
                .Add("NodeE", new Step { Conv = us + "Shepherd's Rest", Enc = "Usurper Shepherd's Rest {SHEP} Dead", Success = us + "Shepherd's Rest Success", Fail = us + "Shepherd's Rest Fail", Reward = "usurper/NodeE", Requires = new[] { "NodeC", "NodeD" }, RequireAny = true })
                .Add("NodeF", Talk(us + "Last Rites"))
                .Add("NodeG", new Step { Conv = "758dce7e-3575-4109-9f58-9331b6e26cb5", Enc = "Usurper Last Rites", Success = us + "Last Rites Success", Fail = us + "Last Rites Fail", Reward = "usurper/NodeG" })
                .Final("NodeG"));

            // The Smoldering Dead (Node 24): King Edmund III's burial vault (entrance battle by faction).
            const string sd = "The Smoldering Dead - ";
            s_Defs.Add(new Def { Key = "smoldering", Id = "a8d2ad0e-306e-4d07-9cd1-e4d7c82cdf1c", Name = "The Smoldering Dead", Folder = "smolderingdead", AZ1Node = "Node024", Start = "NodeA" }
                .Add("NodeA", new Step { Conv = sd + "Entrance", Enc = "The Smoldering Dead Entrance ({F})|The Smoldering Dead Entrance to Raqqar ({F})", Success = sd + "Entrance Success - {F}", Fail = sd + "Entrance Fail", Reward = "smoldering/NodeA" })
                .Add("NodeB", Fight(sd, "Tomb of the Rose Knights", "The Smoldering Dead Tomb of the Rose Knights", "smoldering/NodeB"))
                .Add("NodeC", With(Fight(sd, "Crypt of the Thorn Knight", "The Smoldering Dead Crypt of the Thorn Knight", "smoldering/NodeC"),
                    "start_encounter", null, "choice_wake", null, "choice_leave", Stay))
                .Add("NodeD", Fight(sd, "Hall of Relics", "The Smoldering Dead Hall of Relics", "smoldering/NodeD"))
                .Add("NodeF", Fight(sd, "Haunted Hallway", "The Smoldering Dead Haunted Hallway", "smoldering/NodeF"))
                .Add("NodeG", Talk(sd + "Entrance to the Burial Vault"), Fight(sd, "Burial Vault of King Edmund III", "The Smoldering Dead Burial Vault of King Edmund III", "smoldering/NodeG"))
                .Final("NodeG"));

            // Devonshire Keep (Node 68): battles "Devonshire Keep <room letter>", two in the Oratory.
            const string dk = "Devonshire Keep - ";
            s_Defs.Add(new Def { Key = "devonshire", Id = "f380cf2b-99b9-4971-886c-f5f9bf3b310c", Name = "Devonshire Keep", Folder = "devonshirekeep", AZ1Node = "Node068", Start = "Node001" }
                .Add("Node001", Talk(dk + "Entrance"))
                .Add("NodeA", new Step { Conv = dk + "Courtyard", Enc = "Devonshire Keep A", Success = dk + "Courtyard Success - Hidden Path Unlocked", Fail = dk + "Courtyard Fail", Reward = "devonshire/NodeA" })
                .Add("NodeB", Talk(dk + "Watchtower"))
                .Add("NodeC", Fight(dk, "Stables", "Devonshire Keep C", "devonshire/NodeC"))
                .Add("NodeD", Fight(dk, "Chapel", "Devonshire Keep D", "devonshire/NodeD"))
                .Add("NodeE", Fight(dk, "Vestibule", "Devonshire Keep E", "devonshire/NodeE"))
                .Add("NodeF", Fight(dk, "Great Hall", "Devonshire Keep F", "devonshire/NodeF"))
                .Add("NodeG", Fight(dk, "Kitchen", "Devonshire Keep G", "devonshire/NodeG"))
                // The Oratory: prying the ruby out brings the burning zombies, leaving it the dead knight (Src.Game dialogue).
                .Add("NodeH", new Step { Conv = dk + "Oratory", Success = dk + "Oratory Success", Fail = dk + "Oratory Fail",
                    // (H2 is the burning zombies' battle: "Burning Mind", The Burning Zombie Board; H1 the knight's.)
                    On = new Dictionary<string, string> { { "zombie_fight", "Devonshire Keep H2" }, { "knight_fight", "Devonshire Keep H1" } },
                    OnReward = new Dictionary<string, string> { { "knight_fight", "devonshire/NodeH/1" }, { "zombie_fight", "devonshire/NodeH/2" } } })
                // The Minstrel's Gallery: the tripwires disabled, no battle.
                .Add("NodeI", With(Fight(dk, "Minstrel's Gallery", "Devonshire Keep I", "devonshire/NodeI"), "encounter_avoided", Skip, "encounter_triggered", null))
                .Add("NodeJ", Fight(dk, "Hall of Scribes", "Devonshire Keep J", "devonshire/NodeJ"))
                .Add("NodeK", Fight(dk, "Bower", "Devonshire Keep K", "devonshire/NodeK"))
                .Add("NodeL", Fight(dk, "Lord Devonshire's Bedchamber", "Devonshire Keep L", "devonshire/NodeL"))
                .Final("NodeL"));
            return s_Defs;
        }

        /// <summary>The step with its dialogue choices: event, outcome, event, outcome...</summary>
        private static Step With(Step s, params string[] eventsAndOutcomes)
        {
            s.On = new Dictionary<string, string>();
            for (int i = 0; i + 1 < eventsAndOutcomes.Length; i += 2) s.On[eventsAndOutcomes[i]] = eventsAndOutcomes[i + 1];
            return s;
        }

        private static Step Defense(string lord, int n)
        {
            string c = "Fort Romor - Defense of Fort Romor (" + lord + ")";
            return new Step { Conv = c, Enc = "Fort Romor - " + lord, Success = c + " Success", Fail = c + " Fail", Reward = "fortromor/Defense/" + n, Requires = new[] { "NodeB", "NodeC", "NodeD" } };
        }

        private static Def ByKey(string key) { return Defs().Find(d => d.Key == key); }

        /// <summary>Dungeon entered from an AZ1 map node (null if none).</summary>
        public static string AtNode(string az1Node) { Def d = Defs().Find(x => x.AZ1Node == az1Node); return d != null ? d.Key : null; }

        /// <summary>Paths of the dungeon's map, read from the client's prefab ("Path_A_B").</summary>
        private static Dictionary<string, HashSet<string>> Edges(Def d)
        {
            if (d.Edges != null) return d.Edges;
            d.Edges = new Dictionary<string, HashSet<string>>();
            try
            {
                GameObject prefab = Resources.Load(d.NodesPrefab) as GameObject;
                if (prefab != null)
                    foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.GetComponent<UIDungeonMapNodeData>() != null && !d.Edges.ContainsKey(t.name)) d.Edges[t.name] = new HashSet<string>();
                        string[] p = t.name.Split('_');
                        if (p.Length != 3 || p[0] != "Path") continue;
                        Link(d.Edges, p[1], p[2]);
                        Link(d.Edges, p[2], p[1]);
                    }
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Dungeons] " + d.Name + " map: " + e); }
            if (d.Edges.Count == 0) Debug.LogError("[HexRevival] [Dungeons] " + d.Name + ": no map nodes in " + d.NodesPrefab);
            return d.Edges;
        }

        private static void Link(Dictionary<string, HashSet<string>> e, string a, string b)
        {
            HashSet<string> s;
            if (!e.TryGetValue(a, out s)) e[a] = s = new HashSet<string>();
            s.Add(b);
        }

        // ------------------------------------------------------------------
        // Template lookups by name
        // ------------------------------------------------------------------

        private static Dictionary<string, string> s_Convs, s_Encs;

        private static void Index()
        {
            if (s_Convs != null) return;
            s_Convs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            s_Encs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            foreach (ConversationTemplate c in tm.ConversationTemplates.Values)
                if (c != null && !string.IsNullOrEmpty(c.m_Name) && !s_Convs.ContainsKey(c.m_Name.Trim())) s_Convs[c.m_Name.Trim()] = c.m_Id.ToString();
            foreach (SceneData s in tm.CampaignScenes.Values)
                if (s is EncounterScene && !string.IsNullOrEmpty(s.m_Name) && !s_Encs.ContainsKey(s.m_Name.Trim())) s_Encs[s.m_Name.Trim()] = s.m_Id.ToString();
        }

        /// <summary>Template id of the first name found ("a|b"), or of the id itself; null if none.</summary>
        private static string Lookup(Dictionary<string, string> index, string names, bool conv)
        {
            if (string.IsNullOrEmpty(names)) return null;
            Index();
            foreach (string n in names.Split('|'))
            {
                string id;
                if (index.TryGetValue(n.Trim(), out id) || index.TryGetValue(n.Trim().Replace("'", ""), out id)) return id;
                Guid g;
                if (IsGuid(n, out g))
                {
                    ResourceId rid = new ResourceId(g);
                    TemplateManager tm = Singleton<TemplateManager>.Instance;
                    SceneData sd;
                    if (conv ? tm.GetConversationTemplate(rid) != null : tm.CampaignScenes.TryGetValue(rid, out sd)) return rid.ToString();
                }
            }
            return null;
        }

        private static bool IsGuid(string s, out Guid g)
        {
            g = Guid.Empty;
            if (s == null || s.Length != 36) return false;
            try { g = new Guid(s); return true; } catch { return false; }
        }

        // ------------------------------------------------------------------
        // Runs (one per champion)
        // ------------------------------------------------------------------

        private class Run
        {
            public ulong Champ;
            public string Key;
            public string Last;                 // node the champion stands on
            public string ALoc;                 // room in progress
            public string Outcome;              // battle outcome conversation to show in ALoc
            public bool Lost;                   // battle lost: the run ends ("LOSE")
            public Dictionary<string, int> Steps = new Dictionary<string, int>();   // steps done per room
            public HashSet<string> Seen = new HashSet<string>();                     // "room:step" conversations seen before the battle
            public Dictionary<string, string> Vars = new Dictionary<string, string>();
            public List<string> VisitedNodes = new List<string>();
            public List<string> VisitedPaths = new List<string>();
            public ulong CampId { get { return DunBase + Champ; } }
            public Def Def { get { return ByKey(Key); } }
        }

        private static Dictionary<ulong, Run> s_Runs;
        private static string FilePath { get { return Saves.File("dungeons.txt"); } }

        /// <summary>Dungeon lives of the champion (Src.Game: 3, plus the talents' modifiers).</summary>
        private static int Lives(ulong champ)
        {
            try { champion_bits c = Champions.Get(champ); if (c != null) return Singleton<Game.Shared.Talents.TalentManager>.Instance.GetTotalDungeonLives(c); }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Dungeons] lives: " + e.Message); }
            return 3;
        }

        private static int Losses(Run r) { string v; int n; return r.Vars.TryGetValue("losses", out v) && int.TryParse(v, out n) ? n : 0; }

        /// <summary>Forgets the loaded runs (another save was selected).</summary>
        public static void Reset() { s_Runs = null; s_Battle = null; }

        private static Run Get(ulong champ)
        {
            Load();
            Run r;
            return s_Runs.TryGetValue(champ, out r) && r.Def != null ? r : null;
        }

        /// <summary>True while the champion is inside a dungeon.</summary>
        public static bool InDungeon(ulong champ) { return Get(champ) != null; }

        // File: champ|key|last|aloc|outcome|lost|room:n,..|seen,..|k=v,..|visitedNodes,..|visitedPaths,..
        private static void Load()
        {
            if (s_Runs != null) return;
            s_Runs = new Dictionary<ulong, Run>();
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] p = line.Split('|');
                    if (p.Length < 11) continue;
                    Run r = new Run { Champ = ulong.Parse(p[0]), Key = p[1], Last = Null(p[2]), ALoc = Null(p[3]), Outcome = Null(p[4]), Lost = p[5] == "1" };
                    foreach (string s in List(p[6])) { int i = s.LastIndexOf(':'); if (i > 0) r.Steps[s.Substring(0, i)] = int.Parse(s.Substring(i + 1)); }
                    r.Seen.UnionWith(List(p[7]));
                    foreach (string s in List(p[8])) { int i = s.IndexOf('='); if (i > 0) r.Vars[s.Substring(0, i)] = s.Substring(i + 1); }
                    r.VisitedNodes.AddRange(List(p[9]));
                    r.VisitedPaths.AddRange(List(p[10]));
                    s_Runs[r.Champ] = r;
                }
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Dungeons] load: " + e); }
        }

        private static string Null(string s) { return s == "" ? null : s; }
        private static string[] List(string s) { return s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries); }

        private static void Save()
        {
            if (s_Runs == null) return;
            try
            {
                List<string> lines = new List<string>();
                foreach (Run r in s_Runs.Values)
                {
                    List<string> steps = new List<string>(), vars = new List<string>();
                    foreach (KeyValuePair<string, int> kv in r.Steps) steps.Add(kv.Key + ":" + kv.Value);
                    foreach (KeyValuePair<string, string> kv in r.Vars) vars.Add(kv.Key + "=" + kv.Value);
                    lines.Add(string.Join("|", new[] { r.Champ.ToString(), r.Key, r.Last ?? "", r.ALoc ?? "", r.Outcome ?? "", r.Lost ? "1" : "0",
                        string.Join(",", steps.ToArray()), string.Join(",", new List<string>(r.Seen).ToArray()), string.Join(",", vars.ToArray()),
                        string.Join(",", r.VisitedNodes.ToArray()), string.Join(",", r.VisitedPaths.ToArray()) }));
                }
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Dungeons] save: " + e); }
        }

        private static Run NewRun(ulong champ, Def d)
        {
            Load();
            Run r = new Run { Champ = champ, Key = d.Key, Last = d.Start };
            if (d.Key == "tranquildream")
            {
                // Which dream creature waits in which room, and Sister Midnight's form: drawn for each run.
                List<string> c = new List<string>(Creatures);
                foreach (string room in DreamRooms)
                {
                    int i = UnityEngine.Random.Range(0, c.Count);
                    r.Vars["c:" + room] = c[i];
                    c.RemoveAt(i);
                }
                r.Vars["form"] = Forms[UnityEngine.Random.Range(0, Forms.Length)];
            }
            r.VisitedNodes.Add(d.Start);
            s_Runs[champ] = r;
            Save();
            Debug.Log("[HexRevival] [Dungeons] champion " + champ + " enters " + d.Name);
            return r;
        }

        private static void EndRun(Run r)
        {
            s_Runs.Remove(r.Champ);
            Save();
        }

        // ------------------------------------------------------------------
        // Rooms
        // ------------------------------------------------------------------

        private static Room RoomOf(Run r, string node) { Room room; return node != null && r.Def.Rooms.TryGetValue(node, out room) ? room : null; }
        private static int Done(Run r, string node) { int n; return r.Steps.TryGetValue(node, out n) ? n : 0; }

        /// <summary>Every step done (a map node without content is always cleared: entrances, forks).</summary>
        private static bool Cleared(Run r, string node) { Room room = RoomOf(r, node); return room == null || Done(r, node) >= room.Steps.Count; }

        /// <summary>The room was entered: its neighbours are revealed.</summary>
        private static bool Opened(Run r, string node) { return Cleared(r, node) || Done(r, node) > 0; }

        private static Step Current(Run r, string node)
        {
            Room room = RoomOf(r, node);
            int n = Done(r, node);
            return room != null && n < room.Steps.Count ? room.Steps[n] : null;
        }

        private static bool Available(Run r, Step s)
        {
            if (s == null || s.Requires.Length == 0) return true;
            int cleared = 0;
            foreach (string q in s.Requires) if (Cleared(r, q)) cleared++;
            return s.RequireAny ? cleared > 0 : cleared == s.Requires.Length;
        }

        /// <summary>Visible map nodes: the entrance, and the neighbours of every room entered.</summary>
        private static HashSet<string> Visible(Run r)
        {
            Dictionary<string, HashSet<string>> edges = Edges(r.Def);
            HashSet<string> vis = new HashSet<string> { r.Def.Start };
            Queue<string> todo = new Queue<string>(vis);
            while (todo.Count > 0)
            {
                string n = todo.Dequeue();
                if (!Opened(r, n)) continue;
                HashSet<string> next;
                if (edges.TryGetValue(n, out next))
                    foreach (string m in next)
                        if (vis.Add(m)) todo.Enqueue(m);
            }
            return vis;
        }

        /// <summary>Champion values used in the names: race, faction.</summary>
        private static string Resolve(Run r, string node, string s)
        {
            if (s == null || s.IndexOf('{') < 0) return s;
            champion_bits c = Champions.Get(r.Champ);
            ERace race = c != null ? c.Race : ERace.Human;
            string v;
            s = s.Replace("{R}", race == ERace.ShinHare ? "Shin'hare" : race.ToString());
            s = s.Replace("{F}", race.GetFaction() == EFactions.Underworld ? "Underworld" : "Ardent");
            if (r.Vars.TryGetValue("c:" + node, out v)) s = s.Replace("{C}", v);
            if (s.Contains("{FORM}")) s = s.Replace("{FORM}", Form(r));
            if (s.Contains("{RANK}") || s.Contains("{D}"))
            {
                // Rank of the next dream creature: 1 + the creatures already beaten (Src.Video: rewards grow with it).
                int rank = 1;
                foreach (string room in DreamRooms) if (Cleared(r, room)) rank++;
                rank = Math.Min(rank, DreamRooms.Length);
                // The creature's strength is the answer given in its tale: 1, 2 or 3 points (Src.Game:
                // "one/two/three_point_answer" events; "it is the dreamer that shapes the Dream"); the
                // rank decides it when no answer was recorded.
                int pts = Points(r, node);
                string d = pts == 1 ? "Easy" : pts == 2 ? "Medium" : pts == 3 ? "Hard" : rank <= 2 ? "Easy" : rank <= 4 ? "Medium" : "Hard";
                s = s.Replace("{RANK}", rank.ToString()).Replace("{D}", d);
            }
            if (s.Contains("{SHEP}"))
            {
                // Ghuuhg is the Hall of Vermin's foe, Mistsim the Chamber of Awakening's (Src.Game: their fail conversations).
                bool g = Cleared(r, "NodeC"), m = Cleared(r, "NodeD");
                s = g && m ? s.Replace("{SHEP}", "Both") : s.Replace("{SHEP} Dead", g ? "Ghuuhg Dead" : "Mistsim Dead|" + s.Replace("{SHEP} Dead", "Mistism Dead"));
            }
            return s;
        }

        private static int Points(Run r, string room) { string v; int n; return r.Vars.TryGetValue("pts:" + room, out v) && int.TryParse(v, out n) ? n : 0; }

        /// <summary>
        /// Sister Midnight's form, "an echo of your will" (Src.Game): from the points of the five tales'
        /// answers, gentlest to fiercest (order of the forms: placeholder); drawn at random without answers.
        /// </summary>
        private static string Form(Run r)
        {
            int total = 0, known = 0;
            foreach (string room in DreamRooms) { int p = Points(r, room); if (p > 0) { total += p; known++; } }
            if (known == DreamRooms.Length) return FormsByPoints[Math.Min(FormsByPoints.Length - 1, Math.Max(0, (total - 5) * FormsByPoints.Length / 11))];
            string v;
            return r.Vars.TryGetValue("form", out v) ? v : Forms[0];
        }

        private static readonly string[] FormsByPoints = { "Instinct", "Cunning", "Retribution", "Conquest", "Savagery" };

        /// <summary>Dialogue choice made in the room's conversation, as its outcome (encounter names, Skip, Stay); null if none.</summary>
        private static string Choice(Run r, string room, Step s, out string evt)
        {
            evt = null;
            string outcome;
            if (s == null || s.On == null || !r.Vars.TryGetValue("choice:" + room, out evt) || !s.On.TryGetValue(evt, out outcome)) return null;
            return outcome ?? s.Enc;
        }

        /// <summary>Encounter of a step (the one chosen in the dialogue if any).</summary>
        private static string StepEnc(Run r, string room, Step s)
        {
            if (s == null) return null;
            string evt, c = Choice(r, room, s, out evt);
            if (c == Skip || c == Stay) return null;
            return Enc(r, room, c ?? s.Enc);
        }

        private static string Conv(Run r, string node, string names) { return Lookup(s_Convs, Resolve(r, node, names), true); }
        private static string Enc(Run r, string node, string names) { return Lookup(s_Encs, Resolve(r, node, names), false); }

        // ------------------------------------------------------------------
        // GameplayState sent to the client
        // ------------------------------------------------------------------

        private static GameplayState BuildState(Run r)
        {
            Index();
            Def d = r.Def;
            GameplayState gs = new GameplayState();
            gs.CampID = r.CampId;
            gs.ChampID = r.Champ;
            gs.TempType = "DUNGEON";
            gs.PayGroups = new PayableGroup[0];
            gs.ALoc = r.ALoc;
            gs.LastNode = r.Last;
            gs.CurState = r.Lost && r.Outcome == null ? "LOSE" : "EXPLORE";
            gs.Started = "2020-01-01T00:00:00Z";
            gs.Champions = new List<Champion>();
            gs.Flags = new Dictionary<string, object>();
            gs.VisLocs = new List<Location>();
            gs.LocNodes = new List<Node>();
            gs.Encounters = new List<Game.Shared.Eval.Data.Encounter>();
            HashSet<string> vis = Visible(r);
            foreach (string n in Edges(d).Keys)
            {
                Step s = Current(r, n);
                bool visible = vis.Contains(n);
                Dictionary<string, object> l = new Dictionary<string, object>();
                l["name"] = n;
                l["node"] = n;
                l["visible"] = visible;
                l["enabled"] = visible && Available(r, s);
                l["completed"] = s == null || !Available(r, s);
                l["repeatable"] = false;
                l["allow_cancel"] = true;
                l["type"] = "Empty";
                if (r.ALoc == n && r.Outcome != null)
                {
                    // Battle just ended: its success / fail conversation opens by itself.
                    l["type"] = "Convo";
                    l["conversationId"] = r.Outcome;
                    l["completed"] = false;
                    l["autostart"] = true;
                }
                else if (s != null && Available(r, s))
                {
                    string conv = Conv(r, n, s.Conv), enc = StepEnc(r, n, s);
                    int step = Done(r, n);
                    if (conv != null && (enc == null || !r.Seen.Contains(n + ":" + step)))
                    {
                        l["type"] = "Convo";
                        l["conversationId"] = conv;
                    }
                    else if (enc != null)
                    {
                        // Battle (after its conversation, it starts by itself).
                        l["type"] = "Encounter";
                        l["encounter"] = enc;
                        l["autostart"] = conv != null || step > 0;
                        Dictionary<string, object> ed = new Dictionary<string, object>();
                        ed["encscene"] = enc;
                        gs.Encounters.Add(new Game.Shared.Eval.Data.Encounter(enc, ed));
                    }
                }
                gs.VisLocs.Add(new Location(l));
                gs.LocNodes.Add(new Node(n, new Dictionary<string, object> { { "id", n }, { "type", "DEFAULT" } }));
            }
            Dictionary<string, object> pub = new Dictionary<string, object>();
            pub["CampaignGroup"] = "AZ1";
            pub["visited_nodes"] = new List<object>(r.VisitedNodes.ToArray());
            pub["visited_paths"] = new List<object>(r.VisitedPaths.ToArray());
            pub["quest_nodes"] = new List<object>();
            gs.PublicState = new QueryState(pub);
            return gs;
        }

        private static CampSummary Summary(Run r)
        {
            Def d = r.Def;
            TemplateTypeInfo info = new TemplateTypeInfo(d.Name, "DUNGEON", null, false, false, "", "", d.BackgroundPrefab, d.NodesPrefab, d.Id);
            return new CampSummary(r.CampId, info, CampaignType.DUNGEON, GameClient.Instance.PlayerProfile.ReckoningID, d.Name, true, 1, null);
        }

        private static void Answer(Run r, ClientCampaignManager.StateCallback callback)
        {
            if (r == null) return;
            Campaign.Deliver(r.CampId, BuildState(r), callback);
        }

        // ------------------------------------------------------------------
        // Entering and leaving
        // ------------------------------------------------------------------

        /// <summary>The champion enters a dungeon (from its AZ1 node, or Castle Crayburn for a new champion).</summary>
        public static void Enter(ulong champ, string key)
        {
            Def d = ByKey(key);
            if (d == null) return;
            Run r = Get(champ);
            if (r == null || r.Key != key) r = NewRun(champ, d);
            GameplayState gs = BuildState(r);
            Campaign.Notify(r.CampId, champ, "area_dungeon", "DUNGEON", gs);
        }

        /// <summary>
        /// A champion without any campaign yet starts in Castle Crayburn (as in the original game).
        /// Returns the state of the run, or null if the champion plays the AZ1 map.
        /// </summary>
        public static GameplayState Current(ulong champ, bool newChampion)
        {
            Run r = Get(champ);
            if (r == null && newChampion && Quests.Flag(champ, "dungeon:" + CrayburnId) == null && !QuestSim.Active)
                r = NewRun(champ, ByKey("crayburn"));
            return r != null ? BuildState(r) : null;
        }

        /// <summary>Leaves the dungeon for the AZ1 map (forfeit, lost run, or finished).</summary>
        private static void Leave(Run r, string reason)
        {
            Debug.Log("[HexRevival] [Dungeons] champion " + r.Champ + " leaves " + r.Def.Name + " (" + reason + ")");
            QuestSim.Note("  leaves " + r.Def.Name + " (" + reason + ")");
            EndRun(r);
            Campaign.ReturnToArea(r.Champ, r.Def.AZ1Node);
        }

        private static void Finish(Run r)
        {
            Def d = r.Def;
            Quests.OnDungeonDone(r.Champ, d.Id);
            if (d.Key == "crayburn") Progression.OnCrayburnDone(r.Champ);
            Campaign.OnDungeonDone(r.Champ, d.AZ1Node);
            Leave(r, "finished");
        }

        // ------------------------------------------------------------------
        // ClientCampaignManager entry points (dungeon campaign ids, see Campaign)
        // ------------------------------------------------------------------

        public static void GetSummary(ulong campId, List<CampSummary> res)
        {
            Run r = Get(campId - DunBase);
            if (r != null) res.Add(Summary(r));
        }

        public static void ActiveInfos(ulong champ, List<TemplateInfo> res)
        {
            Run r = Get(champ);
            if (r != null) res.Add(new TemplateInfo(r.CampId, CampaignType.DUNGEON, GameClient.Instance.PlayerProfile.ReckoningID, r.Def.Name, 1, null));
        }

        public static void QueryState(ulong campId, ClientCampaignManager.StateCallback callback)
        {
            Run r = Get(campId - DunBase);
            if (r != null) { Answer(r, callback); return; }
            // No run any more (finished elsewhere): back to the AZ1 map.
            Campaign.ReturnToArea(campId - DunBase, null);
        }

        public static void Forfeit(ulong campId)
        {
            Run r = Get(campId - DunBase);
            if (r == null) { Campaign.ReturnToArea(campId - DunBase, null); return; }
            Leave(r, r.Lost ? "lost" : "forfeit");
        }

        public static void StartLocation(ulong campId, string location, ClientCampaignManager.StateCallback callback)
        {
            Run r = Get(campId - DunBase);
            if (r == null) return;
            if (!r.Lost && location != null && Visible(r).Contains(location) && Available(r, Current(r, location)))
            {
                r.ALoc = location;
                r.Last = location;
                Save();
            }
            Answer(r, callback);
        }

        public static void SendEvent(ulong campId, string evt, object[] args, ClientCampaignManager.StateCallback callback)
        {
            Run r = Get(campId - DunBase);
            if (r == null) return;
            string arg0 = args != null && args.Length > 0 && args[0] != null ? args[0].ToString() : null;
            if (evt != "visit_path") Debug.Log("[HexRevival] [Dungeons] event " + evt + (arg0 != null ? " " + arg0 : "") + " in " + r.ALoc);
            string room = r.ALoc;
            switch (evt)
            {
                case "visit_node":
                    if (arg0 != null && !r.VisitedNodes.Contains(arg0)) r.VisitedNodes.Add(arg0);
                    if (arg0 != null) r.Last = arg0;
                    break;
                case "pass_node":
                    if (arg0 != null) r.Last = arg0;
                    break;
                case "visit_path":
                    foreach (string p in Campaign.EventStrings(args)) if (!r.VisitedPaths.Contains(p)) r.VisitedPaths.Add(p);
                    break;
                case "conv_done":
                case "empty_done":
                case "accept":
                    if (room == null) break;
                    if (r.Outcome != null)
                    {
                        // End of the battle's outcome conversation.
                        bool wonBattle = r.Vars.ContainsKey("won");
                        r.Vars.Remove("won");
                        r.Outcome = null;
                        if (!wonBattle) ShowLives(r);
                        r.ALoc = null;              // lost run: "LOSE", the client shows the dungeon lost window
                        if (wonBattle && AfterStep(r, room)) return;
                        break;
                    }
                    Step s = Current(r, room);
                    if (s == null) { r.ALoc = null; break; }
                    string choiceEvt, choice = Choice(r, room, s, out choiceEvt);
                    if (choice == Stay)
                    {
                        // Left without doing anything: the room stays as it was.
                        r.Vars.Remove("choice:" + room);
                        r.ALoc = null;
                        break;
                    }
                    if (choice != Skip && evt == "conv_done" && StepEnc(r, room, s) != null)
                    {
                        // Conversation before the battle seen: the battle comes next.
                        r.Seen.Add(room + ":" + Done(r, room));
                        break;
                    }
                    r.Vars.Remove("choice:" + room);
                    r.Steps[room] = Done(r, room) + 1;
                    r.ALoc = null;
                    if (AfterStep(r, room)) return;
                    break;
                case "start":
                {
                    Step st = room != null ? Current(r, room) : null;
                    string enc = StepEnc(r, room, st);
                    if (enc != null) StartBattle(r, room, st, enc);
                    break;
                }
                case "enc_cancel":
                case "cancel":
                    r.ALoc = null;
                    break;
                default:
                {
                    // Dialogue events: the choices that change the room, the Dream's tale answers.
                    Step cur = room != null ? Current(r, room) : null;
                    if (cur != null && cur.On != null && cur.On.ContainsKey(evt)) r.Vars["choice:" + room] = evt;
                    int pts = evt == "one_point_answer" ? 1 : evt == "two_point_answer" ? 2 : evt == "three_point_answer" ? 3 : 0;
                    if (pts > 0 && room != null) r.Vars["pts:" + room] = pts.ToString();
                    break;
                }
            }
            Save();
            Answer(r, callback);
        }

        /// <summary>A step was done: the next one starts by itself (same room), or the dungeon is finished. True if the dungeon was left.</summary>
        private static bool AfterStep(Run r, string room)
        {
            Room rm = RoomOf(r, room);
            if (rm != null && rm.Final && Cleared(r, room)) { Save(); Finish(r); return true; }
            Step next = Current(r, room);
            if (next != null && Available(r, next) && (Conv(r, room, next.Conv) != null || StepEnc(r, room, next) != null))
                r.ALoc = room;
            return false;
        }

        // ------------------------------------------------------------------
        // Battles
        // ------------------------------------------------------------------

        private class Battle { public Run Run; public string Room; public Step Step; public string Reward; public string Enc; }
        private static Battle s_Battle;

        private static void StartBattle(Run r, string room, Step s, string enc)
        {
            string evt, reward = s.Reward;
            Choice(r, room, s, out evt);
            if (evt != null && s.OnReward != null && s.OnReward.ContainsKey(evt)) reward = s.OnReward[evt];
            s_Battle = new Battle { Run = r, Room = room, Step = s, Reward = Resolve(r, room, reward), Enc = enc };
            Debug.Log("[HexRevival] [Dungeons] battle " + r.Def.Name + " " + room + " (" + Resolve(r, room, s.Enc) + ")");
            Campaign.LaunchBattle(r.Champ, enc, r.Def.Name + " " + room);
        }

        /// <summary>Called once when a match ends. Returns false if it was not a dungeon battle.</summary>
        public static bool OnGameEnded(bool won)
        {
            Battle b = s_Battle;
            if (b == null) return false;
            s_Battle = null;
            Run r = Get(b.Run.Champ);
            if (r == null || r != b.Run) return true;
            string room = b.Room;
            QuestSim.Note("  battle " + r.Def.Name + " " + room + (won ? " won" : " lost"));
            Debug.Log("[HexRevival] [Dungeons] battle " + room + (won ? " won" : " lost"));
            r.Seen.Remove(room + ":" + Done(r, room));
            r.Vars.Remove("choice:" + room);
            string enc = b.Enc;
            if (won)
            {
                if (enc != null) Quests.SetFlag(r.Champ, "won:" + enc, "1");
                if (enc != null) Quests.OnEncounterWon(r.Champ, enc, Progression.AnyTamed());
                r.Outcome = Conv(r, room, b.Step.Success);
                if (r.Outcome != null) r.Vars["won"] = "1";
                r.Steps[room] = Done(r, room) + 1;
                if (b.Reward != null) Progression.OnCampaignBattleWon(r.Champ, b.Reward);
            }
            else
            {
                r.Outcome = Conv(r, room, b.Step.Fail);
                // A dungeon life is lost (none in Castle Crayburn); the last one ends the run.
                r.Vars["losses"] = (Losses(r) + 1).ToString();
                r.Lost = !r.Def.Starter && Losses(r) >= Lives(r.Champ);
                if (r.Outcome == null) ShowLives(r);
            }
            // Lost: no room in progress any more (the client shows the "dungeon lost" window once the
            // fail conversation is over; in Castle Crayburn the room can be tried again).
            r.ALoc = r.Outcome != null ? room : null;
            if (won && r.Outcome == null && AfterStep(r, room)) return true;
            Save();
            return true;
        }

        /// <summary>After a lost battle: the dungeon lives left (the client does not show them).</summary>
        private static void ShowLives(Run r)
        {
            if (r.Def.Starter || r.Lost) return;
            int left = Lives(r.Champ) - Losses(r);
            QuestSim.Note("  dungeon lives left: " + left);
            if (!QuestSim.Active) Notices.Show(Loc.Get("dungeon_lives", left));
        }

        // ------------------------------------------------------------------
        // Quest simulation and test commands
        // ------------------------------------------------------------------

        /// <summary>Sim: enters a dungeon (no transition is played).</summary>
        internal static string SimEnter(ulong champ, string key)
        {
            Def d = ByKey(key) ?? Defs().Find(x => x.Name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0);
            if (d == null) return "no dungeon " + key;
            Load();
            NewRun(champ, d);
            return "entered " + d.Name + ": " + SimMap(champ);
        }

        /// <summary>Sim: the map as the client gets it.</summary>
        internal static string SimMap(ulong champ)
        {
            Run r = Get(champ);
            if (r == null) return "not in a dungeon";
            GameplayState gs = BuildState(r);
            List<string> l = new List<string>();
            foreach (Location x in gs.VisLocs)
            {
                if (!x.Visible) continue;
                l.Add(x.Node + "=" + x.Type + (x.Enabled ? "" : " (locked)") + (x.Completed ? " (done)" : "") + (x.AutoStart ? " (auto)" : "")
                    + (x.ConversationID != null ? " \"" + Campaign.ConversationName(x.ConversationID) + "\"" : "")
                    + (x.Encounter != null ? " [" + SceneName(x.Encounter) + "]" : ""));
            }
            return (gs.CurState != "EXPLORE" ? gs.CurState + " " : "") + string.Join(", ", l.ToArray());
        }

        /// <summary>Sim: plays a room: its conversations, and its battle won or lost. Returns what happened.</summary>
        internal static string SimRoom(ulong champ, string room, bool win, params string[] events)
        {
            Run r = Get(champ);
            if (r == null) return "not in a dungeon";
            StartLocation(r.CampId, room, null);
            if (r.ALoc != room) return room + ": not reachable";
            StringBuilder sb = new StringBuilder(room + ":");
            for (int guard = 0; guard < 12 && Get(champ) == r && r.ALoc == room; guard++)
            {
                GameplayState gs = BuildState(r);
                Location l = gs.VisLocs.Find(x => x.Node == room);
                if (l == null) break;
                if (gs.CurState == "LOSE") { sb.Append(" LOSE"); break; }
                if (l.Type == "Convo")
                {
                    sb.Append(" \"" + Campaign.ConversationName(l.ConversationID) + "\"");
                    if (r.Outcome == null) foreach (string e in events) SendEvent(r.CampId, e, null, null);
                    SendEvent(r.CampId, "conv_done", null, null);
                }
                else if (l.Type == "Encounter")
                {
                    sb.Append(" [" + SceneName(l.Encounter) + "]");
                    SendEvent(r.CampId, "start", null, null);
                    if (s_Battle == null) break;
                    OnGameEnded(win);
                }
                else { SendEvent(r.CampId, "empty_done", null, null); }
            }
            if (Get(champ) != r) sb.Append(" -> left the dungeon");
            return sb.ToString();
        }

        internal static void SimForfeit(ulong champ) { Run r = Get(champ); if (r != null) Forfeit(r.CampId); }

        private static string SceneName(string id)
        {
            SceneData sd;
            return Singleton<TemplateManager>.Instance.CampaignScenes.TryGetValue(new ResourceId(id), out sd) ? sd.m_Name : id;
        }

        /// <summary>"dun check": every name of every dungeon, for every race (missing ones listed).</summary>
        public static string Check()
        {
            Index();
            List<string> missing = new List<string>();
            int count = 0;
            foreach (Def d in Defs())
            {
                if (Edges(d).Count == 0) missing.Add(d.Name + ": map prefab " + d.NodesPrefab);
                foreach (Room room in d.Rooms.Values)
                    if (!Edges(d).ContainsKey(room.Id)) missing.Add(d.Name + ": room " + room.Id + " not on the map");
                foreach (ERace race in new[] { ERace.Human, ERace.Elf, ERace.Coyotle, ERace.Orc, ERace.Dwarf, ERace.ShinHare, ERace.Vennen, ERace.Necrotic })
                {
                    Run r = new Run { Champ = 0, Key = d.Key };
                    foreach (Room room in d.Rooms.Values)
                        for (int i = 0; i < room.Steps.Count; i++)
                        {
                            Step s = room.Steps[i];
                            foreach (string form in d.Key == "tranquildream" ? Forms : new[] { "" })
                            foreach (string cr in d.Key == "tranquildream" && Array.IndexOf(DreamRooms, room.Id) >= 0 ? Creatures : new[] { "Turtle" })
                            {
                                r.Vars["form"] = form;
                                r.Vars["c:" + room.Id] = cr;
                                List<string[]> names = new List<string[]> { new[] { "conv", s.Conv }, new[] { "enc", s.Enc }, new[] { "success", s.Success }, new[] { "fail", s.Fail } };
                                if (s.On != null) foreach (string o in s.On.Values) if (o != null && o != Skip && o != Stay) names.Add(new[] { "enc", o });
                                foreach (string[] kv in names)
                                {
                                    if (kv[1] == null || kv[1].Length == 36) continue;
                                    string name = ResolveFor(r, room.Id, kv[1], race);
                                    count++;
                                    foreach (string variant in Variants(name))
                                        if ((kv[0] == "enc" ? Lookup(s_Encs, variant, false) : Lookup(s_Convs, variant, true)) == null)
                                        {
                                            string m = d.Name + " " + room.Id + " " + kv[0] + ": " + variant;
                                            if (!missing.Contains(m)) missing.Add(m);
                                        }
                                }
                            }
                        }
                }
            }
            return count + " names checked, " + missing.Count + " missing" + (missing.Count > 0 ? ":\n  " + string.Join("\n  ", missing.ToArray()) : "");
        }

        /// <summary>The name with the race given (champion-independent), and the Usurper's three Shepherd's Rest variants.</summary>
        private static string ResolveFor(Run r, string node, string s, ERace race)
        {
            s = s.Replace("{R}", race == ERace.ShinHare ? "Shin'hare" : race.ToString());
            s = s.Replace("{F}", race.GetFaction() == EFactions.Underworld ? "Underworld" : "Ardent");
            return Resolve(r, node, s);
        }

        private static IEnumerable<string> Variants(string name)
        {
            yield return name;
            foreach (string d in new[] { "Medium", "Hard" }) if (name.EndsWith(" Easy")) yield return name.Substring(0, name.Length - 4) + d;
            if (name.StartsWith("Usurper Shepherd's Rest")) { yield return "Usurper Shepherd's Rest Both Dead"; yield return "Usurper Shepherd's Rest Ghuuhg Dead"; }
        }

        /// <summary>"dun ..." test commands (current champion).</summary>
        public static string Command(string[] parts)
        {
            string sub = parts.Length > 1 ? parts[1] : "";
            if (sub == "check") return Check();
            if (sub == "go")
            {
                // dev: "dun go <champion id>" = play that champion, as the champion select screen does
                PlayerProfile pp = GameClient.Instance.PlayerProfile;
                ulong id;
                PlayerChampion pc = parts.Length > 2 && ulong.TryParse(parts[2], out id) ? pp.GetPvEChampionById(id) : null;
                if (pc == null)
                {
                    List<string> l = new List<string>();
                    foreach (PlayerChampion c in pp.Champions) if (!c.IsSubChampion) l.Add(c.Id + " " + c.Name + " " + c.Race);
                    return "champions: " + string.Join(", ", l.ToArray());
                }
                pp.SetCurrentChampion(pc);
                ClientCampaignManager.QueryCurrentCampaign(pc.Id, gs =>
                {
                    UICampaignZoneVMBase.StateContext ctx = new UICampaignZoneVMBase.StateContext();
                    ctx.CampaignId = gs.CampID;
                    ctx.CampaignType = gs.GetCampaignType();
                    UICampaignZoneVMBase.TransitionToState(ctx.CampaignType, ctx);
                });
                return "playing " + pc.Name;
            }
            if (sub == "click")
            {
                // dev: "dun click <node>" = click a node of the open map (dungeon or AZ1): walks there, or starts it if the token is on it
                UIDungeonZoneViewModel vm = UnityEngine.Object.FindObjectOfType<UIDungeonZoneViewModel>();
                if (vm == null) return "no map open";
                BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                Dictionary<string, UIDungeonMapNode> nodes = typeof(UIDungeonZoneViewModel).GetField("m_UIDungeonMapNodes", bf).GetValue(vm) as Dictionary<string, UIDungeonMapNode>;
                UIDungeonMapNode node;
                if (parts.Length < 3 || nodes == null || !nodes.TryGetValue(parts[2], out node)) return "nodes: " + (nodes != null ? string.Join(", ", new List<string>(nodes.Keys).ToArray()) : "none");
                if (vm.CurrentLocation == node) typeof(UIDungeonZoneViewModel).GetMethod("AttemptStartCurrentMapNode", bf).Invoke(vm, null);
                else typeof(UIDungeonZoneViewModel).GetMethod("AttemptMoveToNode", bf).Invoke(vm, new object[] { node, false });
                return (vm.CurrentLocation == node ? "start " : "move to ") + node.name + " (" + node.LocationType + (node.Completed ? ", done" : "") + (node.LocationEnabled ? "" : ", disabled") + ")";
            }
            ulong champ = GameClient.Instance.PlayerProfile.GetCurrentChampion().Id;
            switch (sub)
            {
                case "enter":
                    // dev: "dun enter tranquildream" = enter that dungeon now (from the AZ1 map)
                    if (parts.Length < 3 || ByKey(parts[2]) == null) return "dungeons: " + string.Join(", ", Defs().ConvertAll(d => d.Key).ToArray());
                    Enter(champ, parts[2]);
                    return "entering " + parts[2];
                case "map": return SimMap(champ);
                case "leave":
                {
                    Run r = Get(champ);
                    if (r == null) return "not in a dungeon";
                    Leave(r, "dev");
                    return "left";
                }
                case "clear":
                {
                    // dev: "dun clear NodeB" = the room's steps are done (its neighbours revealed)
                    Run r = Get(champ);
                    Room room = r != null && parts.Length > 2 ? RoomOf(r, parts[2]) : null;
                    if (room == null) return "no such room";
                    r.Steps[room.Id] = room.Steps.Count;
                    r.ALoc = null; r.Outcome = null;
                    Save();
                    if (room.Final) { Finish(r); return "finished"; }
                    return SimMap(champ);
                }
                case "skipcrayburn":
                {
                    // Castle Crayburn skipped: straight to the AZ1 map (no quest reward).
                    Run r = Get(champ);
                    if (r == null || r.Key != "crayburn") return "not in Castle Crayburn";
                    Quests.SetFlag(champ, "dungeon:" + CrayburnId, "skipped");
                    Leave(r, "skipped");
                    return "skipped";
                }
            }
            return "dun check | go <champion> | enter <key> | map | clear <room> | leave | skipcrayburn";
        }
    }
}
