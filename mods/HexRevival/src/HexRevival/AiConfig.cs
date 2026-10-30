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
        public const string Ladder = "ladder", Gauntlet = "gauntlet", Limited = "limited", Arena = "arena",
            SiegeKeeps = "siege_keeps", SiegeAttackers = "siege_attackers";
        public static readonly string[] Modes = { Ladder, Gauntlet, Limited, Arena, SiegeKeeps, SiegeAttackers };

        /// <summary>
        /// The fights of an arena tier (Src.Video): its last one is a boss, tiers 2 to 4 have elite fights before it,
        /// the others are common. A deck's role says which of these fights it can be.
        /// </summary>
        public const string Common = "common", Elite = "elite", Boss = "boss";

        /// <summary>The roles a mode uses (none: the mode has no roles).</summary>
        public static string[] RolesOf(string mode)
        {
            return mode == Arena ? new[] { Common, Elite, Boss } : new string[0];
        }

        /// <summary>The highest frequency of a deck.</summary>
        public const int MaxWeight = 99;

        /// <summary>Kinds of deck in a tier.</summary>
        public const string Shared = "deck", GameDeck = "game", Encounter = "encounter", Tournament = "tournament";

        public class DeckRef
        {
            public string Kind = Shared;
            /// <summary>Shared deck: its id; game deck: "name|champion"; encounter: its scene id; tournament deck: its key (TournamentDecks).</summary>
            public string Key = "";
            /// <summary>Relative chance to be drawn among the decks of the tier.</summary>
            public int Weight = 1;
            /// <summary>Its place in the arena: Common, Elite or Boss (see RolesOf).</summary>
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
            public List<DeckRef> Decks = new List<DeckRef>();
            /// <summary>Modifiers for every fight of the tier, added to the deck's own (null: none): a tier's "upgraded" decks.</summary>
            public Opponents.Handicap AI, Player;
        }

        /// <summary>A sealed / draft opponent: its decks come from its card pool, the rest is its own.</summary>
        public class AiPlayer
        {
            public string Name = "";
            public int Weight = 1;
            public Decks.AiSettings Settings = new Decks.AiSettings();
        }

        /// <summary>A Siege defense to attack (as a player would set it up): 3 decks, a bounty, a message.</summary>
        public class KeepDef
        {
            public string Id = Guid.NewGuid().ToString();
            public string Name = "";
            public bool Platinum;
            public int Bounty = 1000;
            public List<KeepFight> Fights = new List<KeepFight>();
            public string Message = "";
        }

        public class KeepFight
        {
            public DeckRef Deck;
            /// <summary>The deck's AI style in this defense ("" = the deck's own).</summary>
            public string Personality = "";
        }

        public class ModeConfig
        {
            public string Mode = Ladder;
            /// <summary>Ladder, gauntlet, arena, Siege attackers: decks by tier.</summary>
            public List<Tier> Tiers = new List<Tier>();
            /// <summary>Sealed and draft: the AI players.</summary>
            public List<AiPlayer> Players = new List<AiPlayer>();
            /// <summary>Siege: the defenses to attack.</summary>
            public List<KeepDef> Keeps = new List<KeepDef>();
        }

        private class File_
        {
            public int Version;
            public List<ModeConfig> Modes = new List<ModeConfig>();
        }

        private const int DefaultsVersion = 6;   // 2: weight and role per deck, no skill per tier; 3: Siege keeps and attackers, limited players, no rare role; 4: Siege attackers are decks; 5: tournament decks; 6: prebuilt decks low, tournament decks high, modifiers at the top

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
                // Settings of an older version: Siege by tiers, sealed and draft by tiers (now AI players).
                s_Account.Modes.RemoveAll(m => m == null || m.Mode == "siege" || (m.Mode == Limited && (m.Players == null || m.Players.Count == 0))
                    || (m.Mode == SiegeAttackers && (m.Tiers == null || m.Tiers.Count == 0)));
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
        /// The default configuration of the modes whose opponents were players (user's choice, 2026-10-30): the game's
        /// prebuilt decks at the bottom, decks real players brought to HEX tournaments higher up (Src.HexCodex, see
        /// TournamentDecks: weaker players' typical lists first, then the strongest players' best records), and at the
        /// top those with modifiers for the AI. Which decks in which tier, and the modifiers: PLACEHOLDER (the original
        /// opponents were players of a similar rank). The arena plays the game's own arena decks. Without the bundled
        /// tournament decks, the prebuilt decks everywhere.
        /// </summary>
        private static File_ BuildDefaults()
        {
            File_ f = new File_ { Version = DefaultsVersion };
            List<DeckRef> prebuilt = Decks.PrebuiltPvp().ConvertAll(d => new DeckRef(GameDeck, GameKey(d)));
            List<DeckRef> gauntlet = TournamentTier("Gauntlet", prebuilt);

            // Ladder: Bronze the prebuilt decks; Silver half prebuilt, half the weakest band's lists; Gold the next two
            // bands; Platinum the strong band; Cosmic the strongest band, the AI with 5 more health and a card more.
            ModeConfig ladder = new ModeConfig { Mode = Ladder };
            ladder.Tiers.Add(new Tier { Name = "Bronze", From = 0, To = 9, Decks = Clone(prebuilt) });
            ladder.Tiers.Add(new Tier { Name = "Silver", From = 10, To = 24, Decks = Mix(prebuilt, TournamentTier("Bronze", prebuilt)) });
            ladder.Tiers.Add(new Tier { Name = "Gold", From = 25, To = 44, Decks = Merge(TournamentTier("Silver", prebuilt), TournamentTier("Gold", prebuilt)) });
            ladder.Tiers.Add(new Tier { Name = "Platinum", From = 45, To = 69, Decks = TournamentTier("Platinum", prebuilt) });
            ladder.Tiers.Add(new Tier { Name = "Cosmic", From = 70, To = 9999, Decks = TournamentTier("Cosmic", prebuilt), AI = Upgraded() });
            f.Modes.Add(ladder);

            // Constructed gauntlet: by wins in the run, the same steps.
            f.Modes.Add(new ModeConfig { Mode = Gauntlet, Tiers = new List<Tier>
            {
                new Tier { Name = "0-1 wins", From = 0, To = 1, Decks = Mix(prebuilt, TournamentTier("Bronze", prebuilt)) },
                new Tier { Name = "2-3 wins", From = 2, To = 3, Decks = Clone(gauntlet) },
                new Tier { Name = "4+ wins", From = 4, To = 99, Decks = Clone(gauntlet), AI = Upgraded() },
            } });
            f.Modes.Add(new ModeConfig { Mode = Limited, Players = DefaultPlayers() });

            ModeConfig arena = new ModeConfig { Mode = Arena };
            Tier t1 = new Tier { Name = "Tier 1", From = 1, To = 1 }, t23 = new Tier { Name = "Tiers 2-3", From = 2, To = 3 }, t4 = new Tier { Name = "Tier 4", From = 4, To = 4 };
            foreach (DeckTemplate d in ArenaDecks())
            {
                ArenaRole r = Role(d);
                string role = r.Boss ? Boss : r.Elite ? Elite : Common;
                foreach (Tier t in new[] { t1, t23, t4 })
                    if ((t == t1 && r.Tier1) || (t == t23 && r.Tier23) || (t == t4 && r.Tier4))
                        t.Decks.Add(new DeckRef(GameDeck, GameKey(d)) { Role = role, Weight = r.Rare ? 0 : CommonWeight });
            }
            // Uruunaz and Zakiir, "extremely rare" (Src.Steam2016; PLACEHOLDER 2% of the common fights where they can
            // appear, Src.HexCompendium): a frequency that gives them that chance among the tier's common decks.
            foreach (Tier t in new[] { t23, t4 })
            {
                int common = 0, rares = 0;
                foreach (DeckRef d in t.Decks) if (d.Role == Common) { if (d.Weight == 0) rares++; else common += d.Weight; }
                int w = rares == 0 ? 1 : Math.Max(1, (int)Math.Round(RareChance * common / (1 - RareChance) / rares));
                foreach (DeckRef d in t.Decks) if (d.Weight == 0) d.Weight = w;
            }
            arena.Tiers.AddRange(new[] { t1, t23, t4 });
            f.Modes.Add(arena);

            f.Modes.Add(new ModeConfig { Mode = SiegeKeeps, Keeps = DefaultKeeps(prebuilt, gauntlet) });
            // The attackers of the player's defense, by its bounty: prebuilt decks for a small one, players' tournament
            // decks above, with modifiers for a large one (PLACEHOLDER: the attackers were players).
            f.Modes.Add(new ModeConfig { Mode = SiegeAttackers, Tiers = new List<Tier>
            {
                new Tier { Name = "Small bounty", From = 0, To = 4999, Decks = Clone(prebuilt) },
                new Tier { Name = "Bounty 5,000+", From = 5000, To = 19999, Decks = Clone(gauntlet) },
                new Tier { Name = "Bounty 20,000+", From = 20000, To = 999999999, Decks = Clone(gauntlet), AI = Upgraded() },
            } });
            return f;
        }

        /// <summary>The AI's modifiers of the top tiers ("upgraded" decks): 5 more health, a card more (PLACEHOLDER).</summary>
        private static Opponents.Handicap Upgraded() { return new Opponents.Handicap { Health = 5, Cards = 1 }; }

        /// <summary>Two pools drawn equally often: each one's frequencies scaled to the same total.</summary>
        private static List<DeckRef> Mix(List<DeckRef> a, List<DeckRef> b)
        {
            int ta = 0, tb = 0;
            foreach (DeckRef r in a) ta += r.Weight;
            foreach (DeckRef r in b) tb += r.Weight;
            List<DeckRef> res = Clone(a);
            if (ta == 0 || tb == 0) return Merge(res, b);
            foreach (DeckRef r in b) res.Add(new DeckRef(r.Kind, r.Key) { Weight = Math.Max(1, Math.Min(MaxWeight, (int)Math.Round((double)r.Weight * ta / tb))), Role = r.Role });
            return res;
        }

        /// <summary>Both lists, a deck in both once (the higher frequency).</summary>
        private static List<DeckRef> Merge(List<DeckRef> a, List<DeckRef> b)
        {
            List<DeckRef> res = Clone(a);
            foreach (DeckRef r in b)
            {
                DeckRef same = res.Find(x => x.Same(r));
                if (same != null) same.Weight = Math.Max(same.Weight, r.Weight);
                else res.Add(new DeckRef(r.Kind, r.Key) { Weight = r.Weight, Role = r.Role });
            }
            return res;
        }

        /// <summary>A tier of the bundled tournament decks with their frequencies, or the fallback if there is none.</summary>
        private static List<DeckRef> TournamentTier(string name, List<DeckRef> fallback)
        {
            List<DeckRef> res = TournamentDecks.Tier(name).ConvertAll(kv => new DeckRef(Tournament, kv.Key) { Weight = Math.Min(MaxWeight, kv.Value) });
            return res.Count > 0 ? res : Clone(fallback);
        }

        /// <summary>The usual frequency of the arena's decks: room below it for the rare challengers.</summary>
        private const int CommonWeight = 10;
        private const double RareChance = 0.02;

        /// <summary>7 AI players for sealed and draft (a table of 8 drafters): the game AI at its Medium level, no modifier.</summary>
        private static List<AiPlayer> DefaultPlayers()
        {
            List<AiPlayer> res = new List<AiPlayer>();
            System.Random rng = new System.Random(7);
            for (int i = 0; i < 7; i++) res.Add(new AiPlayer { Name = PlaceholderKeeps.RandomName(rng), Settings = new Decks.AiSettings() });
            return res;
        }

        /// <summary>
        /// PLACEHOLDER defenses (the original ones were other players'): 40 for gold, 10 for platinum, each 3 decks with an
        /// AI style; most at the minimum bounty, a few higher (Src.Video, A 0:28-8:04). A defense at the minimum bounty
        /// plays prebuilt decks, a higher one players' tournament decks.
        /// </summary>
        private static List<KeepDef> DefaultKeeps(List<DeckRef> low, List<DeckRef> high)
        {
            List<KeepDef> res = new List<KeepDef>();
            if (low.Count < 3 || high.Count < 3) return res;
            System.Random rng = new System.Random(1000);
            string[] styles = Enum.GetNames(typeof(Game.Shared.Mechanics.EDeckPersonality));
            for (int i = 0; i < 50; i++)
            {
                bool platinum = i >= 40;
                int min = platinum ? Siege.Fees.MinPotPlatinum : Siege.Fees.MinPotGold;
                double r = rng.NextDouble();
                KeepDef k = new KeepDef
                {
                    Id = "default-" + i, Name = PlaceholderKeeps.RandomName(rng), Platinum = platinum,
                    Bounty = r < 0.75 ? min : r < 0.95 ? min * (2 + rng.Next(4)) / 2 : min * (5 + rng.Next(20)),
                };
                List<DeckRef> pool = k.Bounty > min ? high : low;
                List<int> used = new List<int>();
                while (k.Fights.Count < 3)
                {
                    int d = rng.Next(pool.Count);
                    if (used.Contains(d)) continue;
                    used.Add(d);
                    k.Fights.Add(new KeepFight { Deck = new DeckRef(pool[d].Kind, pool[d].Key), Personality = styles[rng.Next(styles.Length)] });
                }
                res.Add(k);
            }
            return res;
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
                case Tournament:
                    o = TournamentDecks.Opponent(r.Key);
                    if (o == null) return null;
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
            // The tier's weight and role (an arena boss, a Siege elite...), and its modifiers.
            if (t != null && t.AI != null && !t.AI.IsEmpty) o.AI = Opponents.Handicap.Add(o.AI, t.AI);
            if (t != null && t.Player != null && !t.Player.IsEmpty) o.Player = Opponents.Handicap.Add(o.Player, t.Player);
            o.Weight = r.Weight;
            o.Boss = r.Role == Boss; o.Elite = r.Role == Elite;
            return o;
        }

        /// <summary>An opponent for this mode and value (stars, wins, arena tier, bounty), weighted by frequency; null if none.</summary>
        public static Opponents.Opponent Pick(string mode, int value, Func<Opponents.Opponent, bool> filter = null)
        {
            List<Opponents.Opponent> all = new List<Opponents.Opponent>();
            foreach (Tier t in Get(mode).Tiers)
            {
                if (value < t.From || value > t.To) continue;
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

        /// <summary>A sealed / draft AI player as an opponent: its level and modifiers (its deck comes from its pool).</summary>
        public static Opponents.Opponent ToOpponent(AiPlayer p)
        {
            Decks.AiSettings a = p.Settings ?? new Decks.AiSettings();
            return new Opponents.Opponent { Name = p.Name, Skill = a.Skill, Noise = a.Noise, AI = a.AI, Player = a.Player };
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
