using System;
using System.Collections.Generic;
using System.IO;
using Game.Shared;
using Reckoning.Game;
using UnityEngine;
using JsonUtils = Game.Shared.Network.HConnect.Utils;

namespace HexRevival
{
    /// <summary>
    /// Which decks the AI plays in each mode that had human opponents (ladder, gauntlets, Siege) and in the Frost Ring
    /// Arena: for each mode, tiers (ladder stars, gauntlet wins, arena tier, Siege bounty) listing decks of four kinds:
    /// the shared decks (made for players or for the AI, with their own AI settings, see Decks.AiSettings), the game's
    /// own AI decks, and its encounters. An account uses the default configuration of a mode (shared by every account,
    /// HexRevivalSaves/ai_defaults.json) until it changes that mode (its ai.json); changing the defaults changes every
    /// account that kept them (user's choice).
    /// </summary>
    public static class AiConfig
    {
        public const string Ladder = "ladder", Gauntlet = "gauntlet", Limited = "limited", Arena = "arena", Siege = "siege";
        public static readonly string[] Modes = { Ladder, Gauntlet, Limited, Arena, Siege };

        /// <summary>Roles of a deck in the arena (elites, bosses, rare challengers) and in Siege (third fight: boss, second: elite).</summary>
        public const string Common = "common", Elite = "elite", Boss = "boss", Rare = "rare";

        /// <summary>The roles a mode uses (none: the mode has no roles).</summary>
        public static string[] RolesOf(string mode)
        {
            return mode == Arena ? new[] { Common, Elite, Boss, Rare } : mode == Siege ? new[] { Common, Elite, Boss } : new string[0];
        }

        /// <summary>Kinds of deck in a tier.</summary>
        public const string Shared = "deck", GameDeck = "game", Encounter = "encounter";

        public class DeckRef
        {
            public string Kind = Shared;
            /// <summary>Shared deck: its id; game deck: "name|champion"; encounter: its scene id.</summary>
            public string Key = "";
            /// <summary>Relative chance to be drawn among the decks of the tier.</summary>
            public int Weight = 1;
            /// <summary>Its place in the modes that have one (arena, Siege): Common, Elite, Boss or Rare.</summary>
            public string Role = Common;
            public DeckRef() { }
            public DeckRef(string kind, string key) { Kind = kind; Key = key; }
            public bool Same(DeckRef o) { return o != null && o.Kind == Kind && o.Key == Key; }
        }

        public class Tier
        {
            public string Name = "";
            /// <summary>Ladder: stars; gauntlet: match wins; arena: tier 1 to 4; Siege: bounty (gold).</summary>
            public int From, To;
            /// <summary>Sealed and draft only (no decks: they come from the card pools): the AI's skill in this tier.</summary>
            public string Skill = "";
            public List<DeckRef> Decks = new List<DeckRef>();
        }

        public class ModeConfig
        {
            public string Mode = Ladder;
            public List<Tier> Tiers = new List<Tier>();
        }

        private class File_
        {
            public int Version;
            public List<ModeConfig> Modes = new List<ModeConfig>();
        }

        private const int DefaultsVersion = 2;   // 2: weight and role per deck, no skill per tier

        private static string DefaultsPath { get { return Path.Combine(Path.Combine(LocalServer.Root, "HexRevivalSaves"), "ai_defaults.json"); } }
        private static string AccountPath { get { return Saves.File("ai.json"); } }

        private static File_ s_Defaults;
        private static File_ s_Account;
        private static string s_AccountPath;

        private static File_ Read(string path)
        {
            File_ f = null;
            try { if (File.Exists(path)) JsonUtils.DeserializeJson(File.ReadAllBytes(path), (File_ x) => { f = x; }); }
            catch (Exception e) { Debug.LogError("[HexRevival] [AiConfig] read " + path + ": " + e.Message); }
            return f;
        }

        private static void Write(File_ f, string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                byte[] b = null;
                JsonUtils.SerializeJson(f, (byte[] x) => { b = x; }, true);
                File.WriteAllBytes(path, b);
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [AiConfig] write " + path + ": " + e.Message); }
        }

        private static File_ Defaults
        {
            get
            {
                if (s_Defaults != null) return s_Defaults;
                s_Defaults = Read(DefaultsPath);
                if (s_Defaults == null || s_Defaults.Version < DefaultsVersion) { s_Defaults = BuildDefaults(); Write(s_Defaults, DefaultsPath); }
                // A mode added by a newer version of the mod.
                foreach (string m in Modes)
                    if (s_Defaults.Modes.Find(x => x.Mode == m) == null) { s_Defaults.Modes.Add(BuildDefaults().Modes.Find(x => x.Mode == m)); Write(s_Defaults, DefaultsPath); }
                return s_Defaults;
            }
        }

        private static File_ Account
        {
            get
            {
                if (s_Account != null && s_AccountPath == AccountPath) return s_Account;
                s_AccountPath = AccountPath;
                s_Account = Read(s_AccountPath) ?? new File_();
                return s_Account;
            }
        }

        /// <summary>The configuration the current account plays with.</summary>
        public static ModeConfig Get(string mode)
        {
            ModeConfig own = Saves.Current != null ? Account.Modes.Find(x => x.Mode == mode) : null;
            return own ?? Defaults.Modes.Find(x => x.Mode == mode) ?? new ModeConfig { Mode = mode };
        }

        public static ModeConfig GetDefault(string mode) { return Defaults.Modes.Find(x => x.Mode == mode) ?? new ModeConfig { Mode = mode }; }

        public static bool UsesDefault(string mode) { return Saves.Current == null || Account.Modes.Find(x => x.Mode == mode) == null; }

        /// <summary>Saves a mode for the current account (it stops following the defaults).</summary>
        public static void SaveForAccount(ModeConfig c)
        {
            if (Saves.Current == null) return;
            Account.Modes.RemoveAll(x => x.Mode == c.Mode);
            Account.Modes.Add(c);
            Write(Account, AccountPath);
        }

        /// <summary>The account follows the defaults again for this mode.</summary>
        public static void UseDefault(string mode)
        {
            if (Saves.Current == null) return;
            Account.Modes.RemoveAll(x => x.Mode == mode);
            Write(Account, AccountPath);
        }

        /// <summary>Changes the defaults (every account that kept them).</summary>
        public static void SaveDefault(ModeConfig c)
        {
            Defaults.Modes.RemoveAll(x => x.Mode == c.Mode);
            Defaults.Modes.Add(c);
            Write(Defaults, DefaultsPath);
        }

        /// <summary>A deep copy (the editor works on a copy).</summary>
        public static ModeConfig Copy(ModeConfig c)
        {
            ModeConfig r = null;
            byte[] b = null;
            JsonUtils.SerializeJson(c, (byte[] x) => { b = x; });
            JsonUtils.DeserializeJson(b, (ModeConfig x) => { r = x; });
            return r;
        }

        // ------------------------------------------------------------------
        // Defaults
        // ------------------------------------------------------------------

        /// <summary>
        /// The default configuration: the game's prebuilt decks with a PvP champion (the AI playing better as the ladder
        /// rank rises: PLACEHOLDER, the original opponents were players), and the game's own arena decks for the arena.
        /// </summary>
        private static File_ BuildDefaults()
        {
            File_ f = new File_ { Version = DefaultsVersion };
            List<DeckRef> prebuilt = Decks.PrebuiltPvp().ConvertAll(d => new DeckRef(GameDeck, GameKey(d)));

            ModeConfig ladder = new ModeConfig { Mode = Ladder };
            string[] names = { "Bronze", "Silver", "Gold", "Platinum", "Cosmic" };
            int[] from = { 0, 10, 25, 45, 70 };
            for (int i = 0; i < names.Length; i++)
                ladder.Tiers.Add(new Tier { Name = names[i], From = from[i], To = i + 1 < from.Length ? from[i + 1] - 1 : 9999, Decks = Clone(prebuilt) });
            f.Modes.Add(ladder);

            f.Modes.Add(new ModeConfig { Mode = Gauntlet, Tiers = new List<Tier> { new Tier { Name = "Constructed gauntlet", From = 0, To = 99, Decks = Clone(prebuilt) } } });
            f.Modes.Add(new ModeConfig { Mode = Limited, Tiers = new List<Tier> { new Tier { Name = "Sealed and draft gauntlets", From = 0, To = 99, Skill = "Hard" } } });

            ModeConfig arena = new ModeConfig { Mode = Arena };
            Tier t1 = new Tier { Name = "Tier 1", From = 1, To = 1 }, t23 = new Tier { Name = "Tiers 2-3", From = 2, To = 3 }, t4 = new Tier { Name = "Tier 4", From = 4, To = 4 };
            foreach (DeckTemplate d in ArenaDecks())
            {
                ArenaRole r = Role(d);
                string role = r.Rare ? Rare : r.Boss ? Boss : r.Elite ? Elite : Common;
                if (r.Tier1) t1.Decks.Add(new DeckRef(GameDeck, GameKey(d)) { Role = role });
                if (r.Tier23) t23.Decks.Add(new DeckRef(GameDeck, GameKey(d)) { Role = role });
                if (r.Tier4) t4.Decks.Add(new DeckRef(GameDeck, GameKey(d)) { Role = role });
            }
            arena.Tiers.AddRange(new[] { t1, t23, t4 });
            f.Modes.Add(arena);

            f.Modes.Add(new ModeConfig { Mode = Siege, Tiers = new List<Tier> { new Tier { Name = "Placeholder keeps", From = 0, To = 999999999, Decks = Clone(prebuilt) } } });
            return f;
        }

        private static List<DeckRef> Clone(List<DeckRef> refs) { return refs.ConvertAll(r => new DeckRef(r.Kind, r.Key) { Weight = r.Weight, Role = r.Role }); }

        public static string GameKey(DeckTemplate d)
        {
            ChampionTemplate ch = Singleton<TemplateManager>.Instance.GetChampionTemplate(d.m_ChampionId);
            return d.m_DeckName + "|" + (ch != null ? ch.m_Name : "");
        }

        private static List<DeckTemplate> s_Arena;

        /// <summary>The game's Frost Ring Arena decks ("Arena_", Src.Game).</summary>
        public static List<DeckTemplate> ArenaDecks()
        {
            if (s_Arena != null) return s_Arena;
            s_Arena = new List<DeckTemplate>();
            foreach (DeckTemplate d in Singleton<TemplateManager>.Instance.Decks.Values)
                if (d != null && d.m_DeckName != null && d.m_DeckName.StartsWith("Arena_") && d.m_ChampionId.IsValid && Singleton<TemplateManager>.Instance.GetChampionTemplate(d.m_ChampionId) != null) s_Arena.Add(d);
            s_Arena.Sort((a, b) => string.CompareOrdinal(GameKey(a), GameKey(b)));
            return s_Arena;
        }

        private class ArenaRole { public bool Tier1, Tier23, Tier4, Boss, Elite, Rare; }

        /// <summary>
        /// An arena deck's place, from its champion's health (Src.HexCompendium: tier 1 has its own pool, its opponents with
        /// 7 less health; Src.Video: elites, the Eternal Guardian as tier 1 boss, tier 4's boss an elite): 15 = tier 1,
        /// 19 = the Eternal Guardian, 23 = standard, 25 or "Elite" = elite, 30 = boss, 40 = rare (Uruunaz, Zakiir).
        /// Which boss in which tier: PLACEHOLDER (tiers 2-3 share the bosses, tier 4 their elite versions and Hogarth).
        /// </summary>
        private static ArenaRole Role(DeckTemplate d)
        {
            ChampionTemplate ch = Singleton<TemplateManager>.Instance.GetChampionTemplate(d.m_ChampionId);
            ArenaRole r = new ArenaRole();
            if (ch == null) return r;
            int health = ch.m_StartingHealth;
            bool elite = ch.m_Name.Contains("Elite");
            if (health <= 15) r.Tier1 = true;
            else if (health < 23) { r.Tier1 = true; r.Boss = true; }
            else if (health >= 40) { r.Rare = true; r.Tier23 = r.Tier4 = true; }
            else if (health >= 30)
            {
                r.Boss = true;
                if (ch.m_Name.StartsWith("Hogarth")) r.Tier4 = elite;
                else if (elite) r.Tier4 = true; else r.Tier23 = true;
            }
            else { r.Elite = elite; r.Tier23 = r.Tier4 = true; }
            return r;
        }

        // ------------------------------------------------------------------
        // From a tier's decks to the opponents
        // ------------------------------------------------------------------

        /// <summary>The opponent a deck reference stands for (null if the deck no longer exists).</summary>
        public static Opponents.Opponent Resolve(DeckRef r, Tier t)
        {
            Opponents.Opponent o = null;
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            switch (r.Kind)
            {
                case Shared:
                    ulong id;
                    if (!ulong.TryParse(r.Key, out id) || !Decks.Exists(id)) return null;
                    Decks.AiSettings ai = Decks.Ai(id);
                    DeckTemplate dt = Decks.ToTemplate(new UID(UID.Type.Deck, id));
                    o = new Opponents.Opponent
                    {
                        Name = dt != null ? dt.m_DeckName : r.Key, CustomDeck = id, Skill = ai.Skill, Noise = ai.Noise,
                        Personality = Decks.PersonalityOf(id).ToString(), AI = ai.AI, Player = ai.Player,
                    };
                    break;
                case GameDeck:
                    string[] p = r.Key.Split('|');
                    o = new Opponents.Opponent { Name = p.Length > 1 && p[1] != "" ? p[1] : p[0], Prebuilt = p[0], Champion = p.Length > 1 ? p[1] : "" };
                    DeckTemplate gd = Opponents.DeckOf(o);
                    if (gd == null) return null;
                    break;
                case Encounter:
                    SceneData sc;
                    ResourceId sid;
                    try { sid = new ResourceId(new Guid(r.Key)); } catch { return null; }
                    if (!tm.CampaignScenes.TryGetValue(sid, out sc) || sc == null) return null;
                    o = new Opponents.Opponent { Name = sc.m_Name, Encounter = r.Key };
                    break;
                default: return null;
            }
            // The tier's weight and role (an arena boss, a Siege elite...).
            o.Weight = r.Weight;
            o.Boss = r.Role == Boss; o.Elite = r.Role == Elite; o.Rare = r.Role == Rare;
            return o;
        }

        /// <summary>An opponent for this mode and value (stars, wins, arena tier, bounty), weighted by frequency; null if none.</summary>
        public static Opponents.Opponent Pick(string mode, int value, Func<Opponents.Opponent, bool> filter = null)
        {
            List<Opponents.Opponent> all = new List<Opponents.Opponent>();
            foreach (Tier t in Get(mode).Tiers)
            {
                if (value < t.From || value > t.To) continue;
                if (t.Decks.Count == 0)
                {
                    // A tier without decks (sealed and draft: the decks come from the pools): its skill only.
                    Opponents.Opponent skillOnly = new Opponents.Opponent { Name = t.Name, Skill = string.IsNullOrEmpty(t.Skill) ? "Hard" : t.Skill };
                    if (filter == null || filter(skillOnly)) all.Add(skillOnly);
                    continue;
                }
                foreach (DeckRef r in t.Decks)
                {
                    Opponents.Opponent o = Resolve(r, t);
                    if (o != null && o.Weight > 0 && (filter == null || filter(o))) all.Add(o);
                }
            }
            if (all.Count == 0) return null;
            int total = 0;
            foreach (Opponents.Opponent o in all) total += o.Weight;
            int x = UnityEngine.Random.Range(0, total);
            foreach (Opponents.Opponent o in all) { x -= o.Weight; if (x < 0) return o; }
            return all[all.Count - 1];
        }

        /// <summary>Every opponent a mode can draw (the arena's challenger list).</summary>
        public static List<Opponents.Opponent> All(string mode)
        {
            List<Opponents.Opponent> res = new List<Opponents.Opponent>();
            foreach (Tier t in Get(mode).Tiers)
                foreach (DeckRef r in t.Decks) { Opponents.Opponent o = Resolve(r, t); if (o != null) res.Add(o); }
            return res;
        }
    }
}
