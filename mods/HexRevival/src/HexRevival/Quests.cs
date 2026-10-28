using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Game.Client;
using Game.Client.Campaign;
using Game.Shared;
using Game.Shared.Campaign.Messages;
using Game.Shared.Eval.Data;
using Game.Shared.Domain;
using Game.Shared.Mechanics;
using Reckoning.Game;
using Reckoning.Game.Conversations;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// AZ1 quests, driven by the game's quest templates (QuestTemplate: objectives of type
    /// encounter, dungeon, conversation, collect) and the naming of its conversations
    /// ("... Quest Start", "... Quest Not Complete", "... Quest End", "... Quest Completed Repeating").
    ///
    /// Each champion has a progress per quest (started, objectives done, finished), saved in
    /// quests.txt. Encounter and dungeon objectives can be done in any order once the quest is
    /// started; a conversation objective (a turn-in) is ready when every objective before it is done.
    /// The client's quest journal sees every started, unfinished quest as a QUEST campaign.
    /// </summary>
    public static class Quests
    {
        // ------------------------------------------------------------------
        // Definitions (from the game's QuestTemplates)
        // ------------------------------------------------------------------

        public class Objective
        {
            public string LocId;          // m_QuestLocationId, shown by the journal
            public string Kind;           // Encounter, Dungeon, Conversation, Collect
            public string Encounter;      // encounter scene id (Encounter)
            public List<string> AltEncounters = new List<string>(); // other scenes completing it (the other faction's variant)
            public string Dungeon;        // dungeon id (Dungeon)
            public List<string> Conversations = new List<string>(); // completing conversations (Conversation)
            public bool Taming;           // "Tame X and win match": needs a captured creature
            public string Creature;       // X of a taming objective ("Bubblebee")
            public string Title;          // objective text shown by the journal (English)
            public string ScriptVar;      // Collect: the public variable the quest panel shows ("Current gnomes:N")
        }

        public class Def
        {
            public string Script;
            public int Index;
            public List<Objective> Objectives = new List<Objective>();
        }

        /// <summary>The AZ1 quests, in the order of their templates.</summary>
        public static readonly string[] AZ1Scripts =
        {
            "az01_ar_find_ambling_mesa", "az01_uw_find_cave_in", "az01_tamed", "q_seawitch", "q_gnomes_come_home",
            "q_blood_beneath", "az01_q_cross_the_river", "az01_q_cross_the_river_part2", "az01_three_braves",
            "q_find_myaa", "q_army_of_myth", "q_smoldering_dead", "q_devonshire_keep", "q_tranquil_dream",
            "az01_q_usurper", "az01_q_fort_romor", "q_go_to_az2",
        };

        /// <summary>The AZ2 quests. Their templates lost the ids of their objectives' battles, dungeons and conversations.</summary>
        public static readonly string[] AZ2Scripts =
        {
            "q_alachian_sea", "q_ar_midnight_special", "q_boat_elementals", "q_boat_lobsters", "q_boat_pirates",
            "q_brutecrown_bluff", "q_great_machine_graveyard", "q_kraken", "q_the_mithral_river", "q_petulent_picaroons",
            "q_redhand_raiders", "q_ruins_of_kukatan", "q_the_new_land", "q_uw_victorias_secret",
        };

        private static IEnumerable<string> AllScripts() { foreach (string x in AZ1Scripts) yield return x; foreach (string x in AZ2Scripts) yield return x; }

        /// <summary>
        /// The AZ2 objectives' battles and dungeons, by name (Src.Game names matched to the objectives' texts;
        /// both faction variants count). "quest|location" -> scene names, or "dungeon:" + template id.
        /// </summary>
        private static readonly Dictionary<string, string[]> AZ2Objectives = new Dictionary<string, string[]>
        {
            { "q_alachian_sea|Fight1", new[] { "AZ 2 - NODE 8 - George Gorge - Flock of Seagulls" } },
            { "q_alachian_sea|Fight2", new[] { "AZ 2 - NODE 15 - Holland Tideflats - Giant Jellyfish" } },
            { "q_alachian_sea|Fight3", new[] { "AZ 2 - NODE 25 - Eslinger Isthmus - Dire Toad" } },
            { "q_alachian_sea|Fight4", new[] { "AZ 2 - NODE 32 - Alachian Flats - Bog Walker" } },
            { "q_ar_midnight_special|Fight1", new[] { "AZ2 - NODE 23A - Scamman Rock - Midnight Shepherd (vs. Ardent)" } },
            { "q_boat_elementals|Fight1", new[] { "AZ 2 - NODE B01 - 1st Elementals" } },
            { "q_boat_elementals|Fight2", new[] { "AZ 2 - NODE B06 - 2nd Elementals" } },
            { "q_boat_elementals|Fight3", new[] { "AZ 2 - NODE B13 - 3rd Elemental Battle" } },
            { "q_boat_lobsters|Fight1", new[] { "AZ 2 - NODE B03 - Lobster Grunt" } },
            { "q_boat_lobsters|Fight2", new[] { "AZ 2 - NODE B08 - Lobster Wizard" } },
            { "q_boat_lobsters|Fight3", new[] { "AZ 2 - NODE B16 - Lobster Lord" } },
            { "q_boat_pirates|Fight1", new[] { "AZ 2 - NODE B05- Pirates on the seas" } },
            { "q_boat_pirates|Fight2", new[] { "AZ 2 - NODE B15 - Pirates 2" } },
            { "q_boat_pirates|Fight3", new[] { "AZ 2 - NODE B17 - Pirates 3" } },
            { "q_brutecrown_bluff|Step1", new[] { "dungeon:b730906f-a446-4fe0-869a-147857e4396e" } },
            { "q_great_machine_graveyard|Kill_Boss", new[] { "dungeon:c875a374-7871-4a51-bb2e-cca8d5061ae2" } },
            { "q_kraken|Step1", new[] { "dungeon:6b73c598-3b92-46ea-bd57-62ca564b2e57" } },
            // Ardent: Eurig the Robomancer (Mithral Falls); Underworld: Constantina (Mithral River) - PLACEHOLDER pairing
            { "q_the_mithral_river|Fight1", new[] { "AZ 2 - NODE 48 - Mithral Falls - Eurig the Robomancer", "AZ 2 - NODE 46 - Mithral River - Constantina" } },
            { "q_petulent_picaroons|Fight1", new[] { "AZ 2 - NODE 11 - Smuggler's Cove - Hired Horn Picaroon" } },
            // the raiders: James River Road, Xentoth's Zealot (Ardent) / Lazghar Chul (Underworld) - PLACEHOLDER
            { "q_redhand_raiders|Fight1", new[] { "AZ 2 - NODE 21A - James River Road - Xentoth's Zealot (vs. Ardent)", "AZ 2 - NODE 21B - James River Road - Lazghar Chul (vs. Underworld)" } },
            { "q_ruins_of_kukatan|Step1", new[] { "dungeon:d7dc66e5-a3e4-4713-92c9-68717ae87442" } },
            { "q_uw_victorias_secret|Fight1", new[] { "AZ 2 - NODE 14B - Peterson Woods - Princess Victoria (vs. Underworld)" } },
        };

        private static Dictionary<string, Def> s_Defs;

        public static Def GetDef(string script)
        {
            Load();
            Def d;
            return s_Defs.TryGetValue(script, out d) ? d : null;
        }

        private static void Load()
        {
            if (s_Defs != null) return;
            s_Defs = new Dictionary<string, Def>();
            Dictionary<string, QuestTemplate> byScript = new Dictionary<string, QuestTemplate>();
            foreach (QuestTemplate q in Singleton<TemplateManager>.Instance.QuestTemplates.Values)
                if (q != null && !byScript.ContainsKey(q.m_ScriptName)) byScript[q.m_ScriptName] = q;
            Dictionary<string, string> scenes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (SceneData sc in Singleton<TemplateManager>.Instance.CampaignScenes.Values)
                if (sc != null && sc.m_Name != null && !scenes.ContainsKey(sc.m_Name.Trim())) scenes[sc.m_Name.Trim()] = sc.m_Id.ToString();
            List<string> scripts = new List<string>(AllScripts());
            for (int i = 0; i < scripts.Count; i++)
            {
                QuestTemplate q;
                if (!byScript.TryGetValue(scripts[i], out q)) { Debug.LogWarning("[HexRevival] [Quests] template not found: " + scripts[i]); continue; }
                Def d = new Def { Script = q.m_ScriptName, Index = i };
                foreach (QuestObjectiveBase o in q.m_Objectives)
                {
                    Objective ob = new Objective { LocId = o.m_QuestLocationId };
                    string title = o.m_Title != null && o.m_Title.Count > 0 ? o.m_Title[0].m_Text : "";
                    ob.Title = title;
                    QuestObjectiveEncounter enc = o as QuestObjectiveEncounter;
                    QuestObjectiveDungeon dun = o as QuestObjectiveDungeon;
                    QuestObjectiveConversation conv = o as QuestObjectiveConversation;
                    if (enc != null)
                    {
                        ob.Kind = "Encounter"; ob.Encounter = enc.m_EncounterId.ToString(); ob.Taming = title.StartsWith("Tame ");
                        if (ob.Taming) TamingScenes(ob, title);
                    }
                    else if (dun != null) { ob.Kind = "Dungeon"; ob.Dungeon = dun.m_DungeonId.ToString(); }
                    else if (conv != null)
                    {
                        ob.Kind = "Conversation";
                        if (conv.m_ConversationIdOld.IsValid) ob.Conversations.Add(conv.m_ConversationIdOld.ToString());
                        if (conv.m_ConversationIdList != null) foreach (ResourceId c in conv.m_ConversationIdList) if (c.IsValid) ob.Conversations.Add(c.ToString());
                    }
                    else
                    {
                        ob.Kind = "Collect";
                        QuestObjectiveCollect col = o as QuestObjectiveCollect;
                        if (col != null) ob.ScriptVar = col.m_PublicScriptVar;
                    }
                    string[] fix;
                    if (AZ2Objectives.TryGetValue(q.m_ScriptName + "|" + ob.LocId, out fix))
                        foreach (string name in fix)
                        {
                            if (name.StartsWith("dungeon:")) { ob.Kind = "Dungeon"; ob.Dungeon = name.Substring(8); continue; }
                            string id;
                            if (!scenes.TryGetValue(name, out id)) { Debug.LogWarning("[HexRevival] [Quests] " + q.m_ScriptName + " " + ob.LocId + ": no scene " + name); continue; }
                            ob.Kind = "Encounter";
                            if (ob.Encounter == null || ob.Encounter == ResourceId.Invalid.ToString() || !ob.Encounter.Contains("-") || ob.Encounter.StartsWith("00000000")) ob.Encounter = id;
                            else ob.AltEncounters.Add(id);
                        }
                    d.Objectives.Add(ob);
                }
                s_Defs[d.Script] = d;
            }
        }

        /// <summary>
        /// A taming objective is done in the battles where the creature can be tamed: the scenes whose mods hold the
        /// card "Taming X" (Src.Game: EncounterScene.m_Mods). The quest template does not always point there: "Tame
        /// Bubblebee" names Foul Fae (Node 32), while the Bubblebee is tamed against the Water Elemental (Node 31, 33).
        /// </summary>
        private static void TamingScenes(Objective ob, string title)
        {
            int end = title.IndexOf(" and win", StringComparison.Ordinal);
            ob.Creature = end > 5 ? title.Substring(5, end - 5).Trim() : null;
            if (ob.Creature == null) return;
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            List<string> found = new List<string>();
            foreach (SceneData sc in tm.CampaignScenes.Values)
            {
                EncounterScene es = sc as EncounterScene;
                if (es == null || es.m_Mods == null) continue;
                foreach (EncounterSceneMod m in es.m_Mods)
                    if (m != null && m.m_Mods != null && m.m_Mods.Exists(id => { CardTemplate ct = tm.GetCardTemplate(id); return ct != null && ct.m_Name == "Taming " + ob.Creature; }))
                        { if (!found.Contains(es.m_Id.ToString())) found.Add(es.m_Id.ToString()); break; }
            }
            if (found.Count == 0) return;
            if (!found.Contains(ob.Encounter)) Debug.Log("[HexRevival] [Quests] taming " + ob.Creature + ": battles " + string.Join(", ", found.ToArray()) + " (template said " + ob.Encounter + ")");
            ob.Encounter = found[0];
            ob.AltEncounters = found.GetRange(1, found.Count - 1);
        }

        /// <summary>Taming objectives of a creature this champion already tamed (reward claim "champ:tamed:X") are done.</summary>
        public static void SyncTamed(ulong champ)
        {
            if (Saves.Current == null) return;
            foreach (KeyValuePair<string, Progress> q in new List<KeyValuePair<string, Progress>>(Of(champ)))
            {
                if (q.Value.Finished) continue;
                Def d = GetDef(q.Key);
                if (d == null) continue;
                for (int i = 0; i < d.Objectives.Count; i++)
                {
                    Objective o = d.Objectives[i];
                    if (!o.Taming || o.Creature == null || q.Value.Done.Contains(i) || !Saves.Current.Claims.Contains(champ + ":tamed:" + o.Creature)) continue;
                    Log(champ + " " + d.Script + ": " + o.Creature + " already tamed");
                    Complete(champ, d, q.Value, i);
                    Save();
                }
            }
        }

        // ------------------------------------------------------------------
        // Progress (per champion, saved in quests.txt)
        // ------------------------------------------------------------------

        public class Progress
        {
            public bool Finished;
            public HashSet<int> Done = new HashSet<int>();
        }

        private static Dictionary<ulong, Dictionary<string, Progress>> s_Progress;
        /// <summary>Per champion facts used by quests (battles won, nodes visited, choices), saved with them.</summary>
        private static Dictionary<ulong, Dictionary<string, string>> s_Flags;

        private static string FilePath { get { return Saves.File("quests.txt"); } }

        /// <summary>Forgets the loaded progress (account switch).</summary>
        public static void Reset() { s_Progress = null; s_Flags = null; }

        public static string Flag(ulong champ, string key)
        {
            Of(champ);
            Dictionary<string, string> f;
            string v;
            return s_Flags.TryGetValue(champ, out f) && f.TryGetValue(key, out v) ? v : null;
        }

        /// <summary>Sets (or removes, value null) a flag.</summary>
        public static void SetFlag(ulong champ, string key, string value)
        {
            Of(champ);
            Dictionary<string, string> f;
            if (!s_Flags.TryGetValue(champ, out f)) s_Flags[champ] = f = new Dictionary<string, string>();
            if (value == null) f.Remove(key); else f[key] = value;
            Save();
        }

        private static Dictionary<string, Progress> Of(ulong champ)
        {
            if (s_Progress == null)
            {
                s_Progress = new Dictionary<ulong, Dictionary<string, Progress>>();
                s_Flags = new Dictionary<ulong, Dictionary<string, string>>();
                try
                {
                    if (File.Exists(FilePath))
                        foreach (string line in File.ReadAllLines(FilePath))
                        {
                            // champ|script|finished|done objectives (comma separated), or champ|#flag|key|value
                            string[] p = line.Split('|');
                            if (p.Length < 4) continue;
                            ulong c = ulong.Parse(p[0]);
                            if (p[1] == "#flag")
                            {
                                Dictionary<string, string> f;
                                if (!s_Flags.TryGetValue(c, out f)) s_Flags[c] = f = new Dictionary<string, string>();
                                f[p[2]] = p[3];
                                continue;
                            }
                            Progress pr = new Progress { Finished = p[2] == "1" };
                            foreach (string s in p[3].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) pr.Done.Add(int.Parse(s));
                            Dictionary<string, Progress> m;
                            if (!s_Progress.TryGetValue(c, out m)) s_Progress[c] = m = new Dictionary<string, Progress>();
                            m[p[1]] = pr;
                        }
                }
                catch (Exception e) { Debug.LogError("[HexRevival] [Quests] load: " + e); }
            }
            Dictionary<string, Progress> res;
            if (!s_Progress.TryGetValue(champ, out res)) s_Progress[champ] = res = new Dictionary<string, Progress>();
            return res;
        }

        private static void Save()
        {
            try
            {
                List<string> lines = new List<string>();
                foreach (KeyValuePair<ulong, Dictionary<string, Progress>> c in s_Progress)
                    foreach (KeyValuePair<string, Progress> q in c.Value)
                        lines.Add(c.Key + "|" + q.Key + "|" + (q.Value.Finished ? "1" : "0") + "|" + string.Join(",", new List<int>(q.Value.Done).ConvertAll(x => x.ToString()).ToArray()));
                foreach (KeyValuePair<ulong, Dictionary<string, string>> c in s_Flags)
                    foreach (KeyValuePair<string, string> f in c.Value)
                        lines.Add(c.Key + "|#flag|" + f.Key.Replace("|", "/") + "|" + (f.Value ?? "").Replace("|", "/"));
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Quests] save: " + e); }
        }

        public static Progress Get(ulong champ, string script)
        {
            Progress p;
            return Of(champ).TryGetValue(script, out p) ? p : null;
        }

        public static bool IsStarted(ulong champ, string script) { return Get(champ, script) != null; }
        public static bool IsFinished(ulong champ, string script) { Progress p = Get(champ, script); return p != null && p.Finished; }
        public static bool IsActive(ulong champ, string script) { Progress p = Get(champ, script); return p != null && !p.Finished; }

        /// <summary>True when the objective at this location of the quest is done (or the quest finished).</summary>
        public static bool DoneLoc(ulong champ, string script, string locId)
        {
            Def d = GetDef(script);
            Progress p = Get(champ, script);
            if (d == null || p == null) return false;
            if (p.Finished) return true;
            for (int i = 0; i < d.Objectives.Count; i++) if (d.Objectives[i].LocId == locId) return p.Done.Contains(i);
            return false;
        }

        /// <summary>Completes the objective at this location of an active quest.</summary>
        public static void CompleteLoc(ulong champ, string script, string locId)
        {
            Def d = GetDef(script);
            Progress p = Get(champ, script);
            if (d == null || p == null || p.Finished) return;
            for (int i = 0; i < d.Objectives.Count; i++)
                if (d.Objectives[i].LocId == locId && !p.Done.Contains(i)) { Complete(champ, d, p, i); break; }
        }

        /// <summary>Sends an active quest's state again (its panel counters changed).</summary>
        public static void Refresh(ulong champ, string script)
        {
            Def d = GetDef(script);
            Progress p = Get(champ, script);
            if (d == null || p == null || p.Finished) return;
            try { NotifyUpdated(champ, d); }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Quests] refresh " + script + ": " + e.Message); }
        }

        /// <summary>Index of the first objective not done (Objectives.Count when all are done).</summary>
        public static int FirstOpen(Def d, Progress p)
        {
            for (int i = 0; i < d.Objectives.Count; i++) if (!p.Done.Contains(i)) return i;
            return d.Objectives.Count;
        }

        /// <summary>
        /// True when the quest waits for this turn-in conversation: every objective before it done, and
        /// the open objective is this conversation or the quest's final turn-in.
        /// </summary>
        public static bool TurnInReady(ulong champ, string script, string convId)
        {
            Def d = GetDef(script);
            Progress p = Get(champ, script);
            if (d == null || p == null || p.Finished) return false;
            int i = FirstOpen(d, p);
            if (i >= d.Objectives.Count || d.Objectives[i].Kind != "Conversation") return false;
            // Templates often list one faction's turn-in only: the last objective accepts any turn-in of the quest.
            return d.Objectives[i].Conversations.Contains(convId) || i == d.Objectives.Count - 1;
        }

        // ------------------------------------------------------------------
        // Changes
        // ------------------------------------------------------------------

        private static void Log(string message)
        {
            Debug.Log("[HexRevival] [Quests] " + message);
            QuestSim.Note("  quest: " + message);
        }

        public static void Start(ulong champ, string script)
        {
            Def d = GetDef(script);
            if (d == null || IsStarted(champ, script)) return;
            Progress p = new Progress();
            Of(champ)[script] = p;
            // Battles already won before the quest was given count (the map lets them be fought first).
            for (int i = 0; i < d.Objectives.Count; i++)
            {
                Objective o = d.Objectives[i];
                if (o.Kind == "Encounter" && !o.Taming && o.Encounter != null && (Flag(champ, "won:" + o.Encounter) != null || o.AltEncounters.Exists(e => Flag(champ, "won:" + e) != null))) p.Done.Add(i);
                if (o.Kind == "Dungeon" && o.Dungeon != null && Flag(champ, "dungeon:" + ObjectiveDungeon(script, o)) != null) p.Done.Add(i);
            }
            if (script == "az01_q_cross_the_river" && Flag(champ, "won:Node014") != null) p.Done.Add(0);
            Save();
            Log(champ + " started " + script);
            NotifyStarted(champ, d);
        }

        /// <summary>Clears the objectives of an unfinished quest (the AZ2 sea quests: "Must defeat all 3 in one voyage").</summary>
        public static void ResetProgress(ulong champ, string script)
        {
            Def d = GetDef(script);
            Progress p = Get(champ, script);
            if (d == null || p == null || p.Finished || p.Done.Count == 0) return;
            p.Done.Clear();
            Save();
            Log(champ + " " + script + ": objectives reset");
            NotifyUpdated(champ, d);
        }

        /// <summary>A conversation was completed: completes the quest objectives it satisfies.</summary>
        public static void OnConversationDone(ulong champ, string conversationId, string turnInScript)
        {
            foreach (KeyValuePair<string, Progress> q in new List<KeyValuePair<string, Progress>>(Of(champ)))
            {
                if (q.Value.Finished) continue;
                Def d = GetDef(q.Key);
                if (d == null) continue;
                int i = FirstOpen(d, q.Value);
                if (i >= d.Objectives.Count || d.Objectives[i].Kind != "Conversation") continue;
                Objective o = d.Objectives[i];
                bool match = o.Conversations.Contains(conversationId) || (o.Conversations.Count == 0 && turnInScript == q.Key) || turnInScript == q.Key;
                if (match) Complete(champ, d, q.Value, i);
            }
        }

        /// <summary>A battle was won: completes the encounter objectives of that scene (taming ones need a captured creature).</summary>
        public static void OnEncounterWon(ulong champ, string sceneId, bool tamed)
        {
            foreach (KeyValuePair<string, Progress> q in new List<KeyValuePair<string, Progress>>(Of(champ)))
            {
                if (q.Value.Finished) continue;
                Def d = GetDef(q.Key);
                if (d == null) continue;
                for (int i = 0; i < d.Objectives.Count; i++)
                {
                    Objective o = d.Objectives[i];
                    if (q.Value.Done.Contains(i) || o.Kind != "Encounter" || (o.Encounter != sceneId && !o.AltEncounters.Contains(sceneId))) continue;
                    if (o.Taming && !tamed) continue;
                    Complete(champ, d, q.Value, i);
                }
            }
        }

        /// <summary>
        /// True while a started quest still asks to tame a creature in this battle ("Tame X and win match"): the battle
        /// must stay available until the creature is tamed (a win without taming it does not count).
        /// </summary>
        public static bool TamingPending(ulong champ, string sceneId)
        {
            if (sceneId == null) return false;
            foreach (KeyValuePair<string, Progress> q in Of(champ))
            {
                if (q.Value.Finished) continue;
                Def d = GetDef(q.Key);
                if (d == null) continue;
                for (int i = 0; i < d.Objectives.Count; i++)
                {
                    Objective o = d.Objectives[i];
                    if (o.Taming && !q.Value.Done.Contains(i) && o.Kind == "Encounter" && (o.Encounter == sceneId || o.AltEncounters.Contains(sceneId))) return true;
                }
            }
            return false;
        }

        /// <summary>A dungeon was finished (its last battle won): completes the dungeon objectives naming it.</summary>
        public static void OnDungeonDone(ulong champ, string dungeonId)
        {
            SetFlag(champ, "dungeon:" + dungeonId, "1");
            foreach (KeyValuePair<string, Progress> q in new List<KeyValuePair<string, Progress>>(Of(champ)))
            {
                if (q.Value.Finished) continue;
                Def d = GetDef(q.Key);
                if (d == null) continue;
                for (int i = 0; i < d.Objectives.Count; i++)
                {
                    Objective o = d.Objectives[i];
                    if (q.Value.Done.Contains(i) || o.Kind != "Dungeon" || ObjectiveDungeon(q.Key, o) != dungeonId) continue;
                    Complete(champ, d, q.Value, i);
                }
            }
        }

        /// <summary>
        /// Dungeon an objective really asks for. The Usurper quest's objective names Fort Romor's
        /// template (Src.Game data error: the two faction quests were copied from each other).
        /// </summary>
        private static string ObjectiveDungeon(string script, Objective o)
        {
            return script == "az01_q_usurper" ? Dungeons.UsurperId : o.Dungeon;
        }

        private static void Complete(ulong champ, Def d, Progress p, int objective)
        {
            p.Done.Add(objective);
            // Objectives the mod does not track (a "C" turn-in right after its "B" step) follow automatically.
            for (int i = FirstOpen(d, p); i < d.Objectives.Count; i = FirstOpen(d, p))
            {
                Objective o = d.Objectives[i];
                if (o.Kind == "Conversation" && o.LocId.EndsWith("C") && i > 0 && d.Objectives[i - 1].LocId.EndsWith("B")) p.Done.Add(i);
                else break;
            }
            if (FirstOpen(d, p) >= d.Objectives.Count)
            {
                p.Finished = true;
                Log(champ + " finished " + d.Script);
                Save();
                NotifyUpdated(champ, d);
                Progression.OnQuestFinished(champ, d.Script);
                OnFinished(champ, d.Script);
                return;
            }
            Log(champ + " " + d.Script + ": objective " + d.Objectives[objective].LocId + " done");
            Save();
            NotifyUpdated(champ, d);
        }

        /// <summary>Quests that follow another one (Src.Game: the quest giver's next conversation).</summary>
        private static void OnFinished(ulong champ, string script)
        {
            // Finding the faction's hub leads to its elder (Tranquil Dream giver): nothing to start here,
            // the hub's conversations offer the next quests.
        }

        // ------------------------------------------------------------------
        // Journal (QUEST campaigns seen by ClientQuestManager)
        // ------------------------------------------------------------------

        private const ulong QuestCampBase = 2000000;

        public static bool IsQuestCamp(ulong campId) { return campId >= QuestCampBase; }
        public static ulong CampId(ulong champ, Def d) { return QuestCampBase + champ * 100 + (ulong)d.Index; }

        private static void FromCamp(ulong campId, out ulong champ, out Def def)
        {
            champ = (campId - QuestCampBase) / 100;
            int idx = (int)((campId - QuestCampBase) % 100);
            Load();
            def = null;
            foreach (Def d in s_Defs.Values) if (d.Index == idx) def = d;
        }

        /// <summary>Active (started, unfinished) quests of a champion, for GetActiveStatus(QUEST).</summary>
        public static List<TemplateInfo> ActiveInfos(ulong champ)
        {
            List<TemplateInfo> res = new List<TemplateInfo>();
            Load();
            foreach (KeyValuePair<string, Progress> q in Of(champ))
            {
                Def d = GetDef(q.Key);
                if (d == null || q.Value.Finished) continue;
                res.Add(new TemplateInfo(CampId(champ, d), CampaignType.QUEST, GameClient.Instance.PlayerProfile.ReckoningID, d.Script, 1, null));
            }
            return res;
        }

        public static GameplayState State(ulong campId)
        {
            ulong champ; Def d;
            FromCamp(campId, out champ, out d);
            return d == null ? null : BuildState(champ, d);
        }

        private static GameplayState BuildState(ulong champ, Def d)
        {
            Progress p = Get(champ, d.Script) ?? new Progress();
            GameplayState gs = new GameplayState();
            gs.CampID = CampId(champ, d);
            gs.ChampID = champ;
            gs.TempType = "QUEST";
            gs.PayGroups = new PayableGroup[0];
            gs.CurState = "EXPLORE";
            gs.Started = "2020-01-01T00:00:00Z";
            gs.Finished = p.Finished ? "2020-01-01T00:00:00Z" : null;
            gs.Champions = new List<Champion>();
            gs.Flags = new Dictionary<string, object>();
            gs.VisLocs = new List<Location>();
            gs.LocNodes = new List<Node>();
            gs.Encounters = new List<Game.Shared.Eval.Data.Encounter>();
            // Objectives shown: done ones, then the open ones up to (and including) the next conversation.
            int first = FirstOpen(d, p);
            int last = first;
            while (last < d.Objectives.Count && d.Objectives[last].Kind != "Conversation") last++;
            for (int i = 0; i < d.Objectives.Count; i++)
            {
                if (i > last && !p.Done.Contains(i)) continue;
                Objective o = d.Objectives[i];
                // Counters ("Current gnomes:N") only for the quests that track them (Gnomes Come Home).
                if (o.LocId.EndsWith("_INFO") && d.Script != Gnomes) continue;
                Dictionary<string, object> l = new Dictionary<string, object>();
                l["name"] = o.LocId;
                l["node"] = o.LocId;
                l["type"] = o.Kind == "Conversation" ? "Convo" : o.Kind;
                l["visible"] = true;
                l["enabled"] = true;
                l["completed"] = p.Done.Contains(i);
                gs.VisLocs.Add(new Location(l));
                gs.LocNodes.Add(new Node(o.LocId, new Dictionary<string, object> { { "id", o.LocId }, { "type", "DEFAULT" } }));
            }
            // The map's quest tracker only lists the quests of its own zone.
            Dictionary<string, object> pub = new Dictionary<string, object> { { "CampaignGroup", Array.IndexOf(AZ2Scripts, d.Script) >= 0 ? "AZ2" : "AZ1" } };
            if (d.Script == Gnomes)
                foreach (Objective o in d.Objectives)
                    if (o.Kind == "Collect" && !string.IsNullOrEmpty(o.ScriptVar)) pub[o.ScriptVar] = (long)HexRevival.Gnomes.Counter(champ, o.LocId);
            gs.PublicState = new QueryState(pub);
            return gs;
        }

        // Client notifications, as the campaign server sent them (campspawn / cmpupdate).
        private static void NotifyStarted(ulong champ, Def d)
        {
            GameplayState gs = BuildState(champ, d);
            Invoke("HandleOnCampSpawnMessage", new CampSysGeneral.Any.CampSpawnNotify(0, gs.CampID, champ, "QUEST", d.Script, champ, "AREA", false, gs));
        }

        private static void NotifyUpdated(ulong champ, Def d)
        {
            GameplayState gs = BuildState(champ, d);
            Invoke("HandleOnNotifyCampUpdate", new CampSysGeneral.Any.CampUpdateNotify(0, gs.CampID, champ, "quest_progress", "QUEST", false, gs, null));
        }

        internal static void Invoke(string handler, object notify)
        {
            if (QuestSim.Active) return; // simulation: the client is not told
            try
            {
                object mgr = GameClient.Instance.CampaignMgr;
                MethodInfo m = typeof(ClientCampaignManager).GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                m.Invoke(mgr, new[] { notify });
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Quests] " + handler + ": " + (e.InnerException ?? e).Message); }
        }

        // ------------------------------------------------------------------
        // Conversations of the quest givers
        // ------------------------------------------------------------------

        /// <summary>Quest of a conversation, from its name (Src.Game naming).</summary>
        private static readonly string[][] Keywords =
        {
            new[] { "SEA WITCH", "q_seawitch" }, new[] { "GNOMES COME HOME", "q_gnomes_come_home" },
            new[] { "BLOOD BENEATH", "q_blood_beneath" }, new[] { "TAMED", "az01_tamed" },
            new[] { "ARMY OF MYTH", "q_army_of_myth" }, new[] { "SMOLDERING DEAD", "q_smoldering_dead" },
            new[] { "DEVONSHIRE KEEP", "q_devonshire_keep" }, new[] { "TRANQUIL DREAM", "q_tranquil_dream" },
            new[] { "USURPER", "az01_q_usurper" }, new[] { "FORT ROMOR", "az01_q_fort_romor" },
            new[] { "STARTS AZ2", "q_go_to_az2" },
            // AZ2 (Wakuna / Naagaan givers, Node 57's Brutecrown Delta)
            new[] { "ALACHIAN SEA", "q_alachian_sea" }, new[] { "MIDNIGHT SPECIAL", "q_ar_midnight_special" },
            new[] { "BRUTECROWN", "q_brutecrown_bluff" }, new[] { "MACHINE GRAVEYARD", "q_great_machine_graveyard" },
            new[] { "THE KRAKEN", "q_kraken" }, new[] { "MITHRIL RIVER", "q_the_mithral_river" },
            new[] { "PICAROONS", "q_petulent_picaroons" }, new[] { "CROSSED DARK", "q_redhand_raiders" },
            new[] { "RUINS OF KUKATAN", "q_ruins_of_kukatan" }, new[] { "THE NEW LAND", "q_the_new_land" },
            new[] { "VICTORIA'S SECRET", "q_uw_victorias_secret" },
        };

        public const string Gnomes = "q_gnomes_come_home";


        public enum Role { None, Start, NotComplete, End, Repeat }

        public static string QuestOf(string convName)
        {
            string u = convName.ToUpperInvariant();
            foreach (string[] k in Keywords) if (u.Contains(k[0])) return k[1];
            return null;
        }

        public static Role RoleOf(string convName)
        {
            string u = convName.ToUpperInvariant();
            if (u.Contains("COMPLETED REPEATING") || u.Contains("COMPLETE REPEATING") || u.Contains("REPEATING UNTIL")) return Role.Repeat;
            if (u.Contains("NOT COMPLETE")) return Role.NotComplete;
            if (u.Contains("QUEST END")) return Role.End;
            if (u.Contains("QUEST START") || u.Contains("STARTS AZ2")) return Role.Start;
            if (u.Contains("TAMED QUEST")) return Role.NotComplete; // Takumi's "Tamed Quest" (the Ardent one is "... Not Complete")
            return Role.None;
        }

        /// <summary>False when the conversation's name targets another faction or race (" - Underworld", "ARDENT", " - Dwarf"...).</summary>
        public static bool MatchesChampion(string convName, ulong champ)
        {
            champion_bits c = Champions.Get(champ);
            if (c == null) return true;
            string u = convName.ToUpperInvariant();
            EFactions f = c.Race.GetFaction();
            if (u.Contains("UNDERWORLD") && f != EFactions.Underworld) return false;
            if (u.Contains("ARDENT") && f != EFactions.Aria) return false;
            string[] races = { "DWARF", "NECROTIC", "SHIN'HARE", "VENNEN", "COYOTLE", "ELF", "HUMAN", "ORC" };
            string race = c.Race.ToString().ToUpperInvariant().Replace("SHINHARE", "SHIN'HARE");
            foreach (string r in races)
                if (u.EndsWith(" - " + r) && r != race) return false;
            return true;
        }

        /// <summary>
        /// Whether a quest can be offered (Src.Game: the givers' step numbers, e.g. Myaa's steps 3 to 7).
        /// </summary>
        public static bool CanStart(ulong champ, string script)
        {
            switch (script)
            {
                case "q_smoldering_dead": return IsFinished(champ, "q_army_of_myth");
                case "q_devonshire_keep": return IsFinished(champ, "q_smoldering_dead");
                case "q_go_to_az2": return IsFinished(champ, "az01_q_fort_romor") || IsFinished(champ, "az01_q_usurper");
                default: return true;
            }
        }

        /// <summary>Effects of a quest giver's conversation once it is completed.</summary>
        public static void OnGiverConversationDone(ulong champ, string convId, string convName)
        {
            string script = QuestOf(convName);
            Role role = RoleOf(convName);
            if (script != null && role == Role.Start) Start(champ, script);
            // Talking to Myaa completes "Find Myaa".
            if (convName.ToUpperInvariant().Contains("MYAA") && IsActive(champ, "q_find_myaa")) OnConversationDone(champ, convId, "q_find_myaa");
            OnConversationDone(champ, convId, role == Role.End ? script : null);
        }

        /// <summary>Quests started by area conversations (Src.Game: the AZ1 quest givers on the map).</summary>
        public static void OnAreaConversationDone(ulong champ, string node, string convName)
        {
            string u = convName.ToUpperInvariant();
            champion_bits c = Champions.Get(champ);
            bool ardent = c == null || c.Race.GetFaction() == EFactions.Aria;
            if (node == "Node002")
            {
                // Belarius / Takumi: taming quest and the way to the faction's hub.
                Start(champ, "az01_tamed");
                Start(champ, ardent ? "az01_ar_find_ambling_mesa" : "az01_uw_find_cave_in");
            }
            if (u.Contains("WINSTON - STEP 1")) Start(champ, "az01_q_cross_the_river");
            if (u.Contains("WALLACE - STEP 1")) Start(champ, "az01_q_cross_the_river_part2");
            if (u.Contains("ROBOT - STEP 1")) Start(champ, "az01_three_braves");
            // Gnomes Come Home: the gnomes in tow are home (all of them: Step 4).
            if (u.Contains("ABANDONED CAMP - STEP 3") || u.Contains("ABANDONED CAMP - STEP 4")) HexRevival.Gnomes.Deliver(champ);
            if (u.Contains("WARREN - STEP 1A")) CompleteLoc(champ, "az01_q_cross_the_river_part2", "Step3");
            if (u.Contains("ROBOT - STEP 2 - INSTALL")) CompleteLoc(champ, "az01_three_braves", "Step1B");
            if (u.Contains("ROBOT - STEP 3 - INSTALL")) CompleteLoc(champ, "az01_three_braves", "Step2B");
            if (u.Contains("ROBOT - STEP 4 - INSTALL")) CompleteLoc(champ, "az01_three_braves", "Step3B");
            if (u.Contains("BLUEJAY - STEP 4")) CompleteLoc(champ, "az01_three_braves", "Step6");
            // AZ2 quests given on the map: Brutecrown Delta (Belarius / Takumi at McGinty Coast), the
            // junkyard robot (Great Machine Graveyard), the harbor of Portsmeare (The New Land).
            string script = QuestOf(convName);
            if (script != null && RoleOf(convName) == Role.Start && u.StartsWith("AZ2")) Start(champ, script);
            if (u.Contains("AZ2 - NODE 45 - ROBOT")) Start(champ, "q_great_machine_graveyard");
            if (u.Contains("PORTSMEARE HARBOR")) CompleteLoc(champ, "q_the_new_land", "Step1");
        }

        // ------------------------------------------------------------------
        // Dev command
        // ------------------------------------------------------------------

        /// <summary>"quest [start|done script]": quests of the current champion.</summary>
        public static string Command(string[] parts)
        {
            ulong champ = parts.Length > 2 && parts[1] == "of" ? ulong.Parse(parts[2]) : GameClient.Instance.PlayerProfile.GetCurrentChampion().Id;
            if (parts.Length > 2 && parts[1] == "start") { Start(champ, parts[2]); return "started " + parts[2]; }
            // dev: "quest loc q_brutecrown_bluff Step1" completes one objective
            if (parts.Length > 3 && parts[1] == "loc") { CompleteLoc(champ, parts[2], parts[3]); return parts[2] + " " + parts[3] + " done=" + DoneLoc(champ, parts[2], parts[3]); }
            if (parts.Length > 2 && parts[1] == "reset") { Of(champ).Remove(parts[2]); Save(); return "reset " + parts[2]; }
            // dev: "quest flag boat:points 3" sets a champion flag ("-" clears it)
            if (parts.Length > 3 && parts[1] == "flag") { SetFlag(champ, parts[2], parts[3] == "-" ? null : parts[3]); return parts[2] + " = " + Flag(champ, parts[2]); }
            if (parts.Length > 1 && parts[1] == "tracker")
            {
                // dev: the quests the client's quest manager holds, as the map's tracker sees them
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                IEnumerable<Game.Client.Campaign.ClientQuestManager.Quest> l = GameClient.Instance.QuestMgr.GetCurrentQuestList();
                if (l == null) return "no quest list";
                foreach (Game.Client.Campaign.ClientQuestManager.Quest q in l)
                {
                    GameplayState gs = q.GetState();
                    sb.Append(q.QuestCampaignId).Append(' ').Append(q.QuestScriptName).Append(" template=").Append(q.GetTemplate() != null)
                      .Append(gs == null ? " state=null" : " group=" + gs.GetCampaignGroup() + " finished='" + gs.Finished + "' locs=" + gs.VisLocs.Count).Append('\n');
                }
                return sb.ToString();
            }
            if (parts.Length > 1 && parts[1] == "defs")
            {
                // dev: every AZ1 quest with its objectives, to HexRevivalQuests.txt
                Load();
                List<string> lines = new List<string>();
                foreach (string script in AllScripts())
                {
                    Def d = GetDef(script);
                    if (d == null) { lines.Add(script + ": NOT FOUND"); continue; }
                    lines.Add("=== " + script);
                    for (int i = 0; i < d.Objectives.Count; i++)
                    {
                        Objective ob = d.Objectives[i];
                        lines.Add("  " + i + " " + ob.LocId + " " + ob.Kind + (ob.Taming ? " (taming)" : "") + " enc=" + ob.Encounter + " dun=" + ob.Dungeon
                            + " convs=" + string.Join(",", ob.Conversations.ToArray()) + " | " + (ob.Title ?? "").Replace("\n", " "));
                    }
                }
                File.WriteAllLines(Path.Combine(Directory.GetCurrentDirectory(), "HexRevivalQuests.txt"), lines.ToArray());
                return lines.Count + " lines written to HexRevivalQuests.txt";
            }
            if (parts.Length > 1 && parts[1] == "convs")
            {
                // dev: conversations that complete each conversation objective, to HexRevivalQuestConvs.txt
                List<string> lines = new List<string>();
                foreach (string script in AllScripts())
                {
                    Def d = GetDef(script);
                    if (d == null) continue;
                    foreach (Objective ob in d.Objectives)
                    {
                        if (ob.Kind != "Conversation") continue;
                        foreach (string c in ob.Conversations)
                        {
                            ConversationTemplate t = null;
                            try { t = Singleton<TemplateManager>.Instance.GetConversationTemplate(new ResourceId(c)); } catch { }
                            lines.Add(script + " " + ob.LocId + " " + c + " = " + (t == null ? "?" : t.m_Name));
                        }
                    }
                }
                File.WriteAllLines(Path.Combine(Directory.GetCurrentDirectory(), "HexRevivalQuestConvs.txt"), lines.ToArray());
                return lines.Count + " lines written to HexRevivalQuestConvs.txt";
            }
            List<string> o = new List<string>();
            foreach (KeyValuePair<string, Progress> q in Of(champ))
                o.Add(q.Key + (q.Value.Finished ? " finished" : " done=" + string.Join(",", new List<int>(q.Value.Done).ConvertAll(x => x.ToString()).ToArray())));
            return "champion " + champ + ": " + (o.Count == 0 ? "no quest" : string.Join("; ", o.ToArray()));
        }
    }
}
