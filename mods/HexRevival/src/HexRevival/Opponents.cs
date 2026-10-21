using System;
using System.Collections.Generic;
using System.IO;
using Game.Shared;
using Game.Shared.AI;
using Game.Shared.Mechanics;
using Reckoning.Game;
using UnityEngine;
using JsonUtils = Game.Shared.Network.HConnect.Utils;

namespace HexRevival
{
    /// <summary>
    /// The AI opponents of the modes that had human opponents (ladder, gauntlets) and of the Frost Ring Arena:
    /// "opponent packs", one JSON file each in the HexRevivalOpponents folder of the game, to share by copying the
    /// file. A pack lists tiers (by ladder stars, gauntlet wins, arena tier); a tier lists opponents: name, deck
    /// (card names, so that a pack works on any computer; or a prebuilt deck of the game), AI skill, deck
    /// personality, and handicaps for the AI and for the player. Each account chooses its pack (account.txt).
    /// The default pack is written by the mod (Default.json) and rewritten when missing.
    /// </summary>
    public static class Opponents
    {
        // ------------------------------------------------------------------
        // Pack format
        // ------------------------------------------------------------------

        public class Pack
        {
            public string Name = "";
            public string Author = "";
            public string Description = "";
            public List<Tier> Tiers = new List<Tier>();
        }

        /// <summary>Modes of a tier.</summary>
        public const string Ladder = "ladder", Gauntlet = "gauntlet", Limited = "limited", Arena = "arena";

        public class Tier
        {
            /// <summary>ladder (From/To = stars), gauntlet (= match wins), limited (sealed and draft gauntlets: decks ignored), arena (= arena tier 1 to 4).</summary>
            public string Mode = Ladder;
            public string Name = "";
            public int From, To;
            public List<Opponent> Opponents = new List<Opponent>();
        }

        public class Opponent
        {
            public string Name = "";
            /// <summary>A prebuilt deck of the game (its name), used when Cards is empty.</summary>
            public string Prebuilt = "";
            /// <summary>Champion name (with Cards).</summary>
            public string Champion = "";
            /// <summary>"count name" lines, e.g. "4 Crackling Bolt".</summary>
            public List<string> Cards = new List<string>();
            /// <summary>The game's AI skill (EDifficulty): Easy ... Medium ... Hard (the original AI).</summary>
            public string Skill = "Hard";
            /// <summary>Random noise in the AI's card values (the game's default 50; higher plays worse).</summary>
            public int Noise = 50;
            /// <summary>Deck personality (EDeckPersonality): Default, Aggressive, Burn...</summary>
            public string Personality = "Default";
            /// <summary>Arena: a boss (the last fight of a tier) or an elite.</summary>
            public bool Boss, Elite;
            /// <summary>Relative chance to be picked in its tier.</summary>
            public int Weight = 1;
            public Handicap AI = new Handicap();
            public Handicap Player = new Handicap();
        }

        /// <summary>Applied once the mulligan is over. Health can be negative (never below 1).</summary>
        public class Handicap
        {
            public int Health;
            public int Resources;   // resources in play (no shard threshold)
            public int Charges;
            public int Cards;       // cards drawn
            public bool IsEmpty { get { return Health == 0 && Resources == 0 && Charges == 0 && Cards == 0; } }
        }

        // ------------------------------------------------------------------
        // Files
        // ------------------------------------------------------------------

        public const string DefaultPackName = "Default";

        public static string Folder { get { return Path.Combine(LocalServer.Root, "HexRevivalOpponents"); } }

        private static string PathOf(string name) { return Path.Combine(Folder, name + ".json"); }

        /// <summary>The names of the packs in the folder (the default one first).</summary>
        public static List<string> List()
        {
            EnsureDefault();
            List<string> res = new List<string>();
            foreach (string f in Directory.GetFiles(Folder, "*.json")) res.Add(Path.GetFileNameWithoutExtension(f));
            res.Sort((a, b) => a == DefaultPackName ? -1 : b == DefaultPackName ? 1 : string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
            return res;
        }

        public static Pack Load(string name)
        {
            EnsureDefault();
            Pack p = null;
            try
            {
                string path = PathOf(name);
                if (File.Exists(path)) JsonUtils.DeserializeJson(File.ReadAllBytes(path), (Pack x) => { p = x; });
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Opponents] " + name + ": " + e.Message); }
            if (p != null && string.IsNullOrEmpty(p.Name)) p.Name = name;
            return p;
        }

        public static void Save(Pack p, string fileName)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                byte[] b = null;
                JsonUtils.SerializeJson(p, (byte[] x) => { b = x; }, true);
                File.WriteAllBytes(PathOf(fileName), b);
                s_Active = null;
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Opponents] save " + fileName + ": " + e.Message); }
        }

        private static void EnsureDefault()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                if (!File.Exists(PathOf(DefaultPackName))) Save(BuildDefault(), DefaultPackName);
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Opponents] default pack: " + e.Message); }
        }

        private static Pack s_Active;
        private static string s_ActiveName;

        /// <summary>The account's pack (the default pack if none or unreadable).</summary>
        public static Pack Active
        {
            get
            {
                string name = Saves.Current != null && !string.IsNullOrEmpty(Saves.Current.OpponentPack) ? Saves.Current.OpponentPack : DefaultPackName;
                if (s_Active != null && s_ActiveName == name) return s_Active;
                s_ActiveName = name;
                s_Active = Load(name) ?? Load(DefaultPackName) ?? BuildDefault();
                return s_Active;
            }
        }

        /// <summary>
        /// The default pack: the game's prebuilt decks with a PvP champion, the AI getting better with the rank
        /// (PLACEHOLDER: the original opponents were players). No handicap.
        /// </summary>
        private static Pack BuildDefault()
        {
            Pack p = new Pack { Name = DefaultPackName, Author = "HEX Revival",
                Description = "The game's prebuilt decks; the AI plays better as you climb. No handicaps." };
            string[] ladderNames = { "Bronze", "Silver", "Gold", "Platinum", "Cosmic" };
            int[] ladderFrom = { 0, 10, 25, 45, 70 };
            string[] skills = { "Medium", "BluffWithOneResource", "Kneecap", "Hard", "Hard" };
            List<DeckTemplate> decks = Decks.PrebuiltPvp();
            for (int i = 0; i < ladderNames.Length; i++)
            {
                Tier t = new Tier { Mode = Ladder, Name = ladderNames[i], From = ladderFrom[i], To = i + 1 < ladderFrom.Length ? ladderFrom[i + 1] - 1 : 9999 };
                foreach (DeckTemplate d in decks) t.Opponents.Add(new Opponent { Name = d.m_DeckName, Prebuilt = d.m_DeckName, Skill = skills[i] });
                p.Tiers.Add(t);
            }
            Tier g = new Tier { Mode = Gauntlet, Name = "Constructed gauntlet", From = 0, To = 99 };
            foreach (DeckTemplate d in decks) g.Opponents.Add(new Opponent { Name = d.m_DeckName, Prebuilt = d.m_DeckName, Skill = "Hard" });
            p.Tiers.Add(g);
            p.Tiers.Add(new Tier { Mode = Limited, Name = "Sealed and draft gauntlets", From = 0, To = 99,
                Opponents = new List<Opponent> { new Opponent { Name = "Limited player", Skill = "Hard" } } });
            return p;
        }

        // ------------------------------------------------------------------
        // Choosing and building an opponent
        // ------------------------------------------------------------------

        /// <summary>An opponent of the active pack for this mode and value (stars, wins, arena tier); null if none.</summary>
        public static Opponent Pick(string mode, int value, Func<Opponent, bool> filter = null)
        {
            List<Opponent> all = new List<Opponent>();
            foreach (Tier t in Active.Tiers)
                if (t.Mode == mode && value >= t.From && value <= t.To)
                    foreach (Opponent o in t.Opponents) if (o.Weight > 0 && (filter == null || filter(o))) all.Add(o);
            if (all.Count == 0) return null;
            int total = 0;
            foreach (Opponent o in all) total += o.Weight;
            int r = UnityEngine.Random.Range(0, total);
            foreach (Opponent o in all) { r -= o.Weight; if (r < 0) return o; }
            return all[all.Count - 1];
        }

        /// <summary>The opponent's deck as the AI's deck (null: its cards or prebuilt deck are unknown).</summary>
        public static DeckTemplate DeckOf(Opponent o)
        {
            if (o == null) return null;
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            if (o.Cards == null || o.Cards.Count == 0)
            {
                if (string.IsNullOrEmpty(o.Prebuilt)) return null;
                foreach (DeckTemplate d in tm.Decks.Values) if (d != null && d.m_DeckName == o.Prebuilt && d.m_ChampionId.IsValid) return d;
                Debug.LogWarning("[HexRevival] [Opponents] no prebuilt deck " + o.Prebuilt);
                return null;
            }
            DeckTemplate res = new DeckTemplate();
            res.m_Id = new ResourceId(Guid.NewGuid());
            res.m_DeckName = o.Name;
            res.m_ChampionId = ChampionId(o.Champion);
            foreach (string line in o.Cards)
            {
                string l = (line ?? "").Trim();
                int sp = l.IndexOf(' '), n;
                if (sp <= 0 || !int.TryParse(l.Substring(0, sp), out n)) { n = 1; sp = -1; }
                CardTemplate c = CardByName(l.Substring(sp + 1).Trim());
                if (c == null) { Debug.LogWarning("[HexRevival] [Opponents] unknown card \"" + l + "\" in " + o.Name); continue; }
                res.m_DeckResources.Add(new TemplateCardStack { m_idTemplate = c.m_Id, m_Count = n });
            }
            return res.m_DeckResources.Count > 0 ? res : null;
        }

        private static Dictionary<string, CardTemplate> s_CardsByName;

        public static CardTemplate CardByName(string name)
        {
            if (s_CardsByName == null)
            {
                s_CardsByName = new Dictionary<string, CardTemplate>(StringComparer.OrdinalIgnoreCase);
                // Prefer the regular printing: non-PvE, then the oldest set (alternate arts come later).
                List<CardTemplate> all = new List<CardTemplate>(Singleton<TemplateManager>.Instance.Cards.Values);
                all.RemoveAll(c => c == null || string.IsNullOrEmpty(c.m_Name));
                all.Sort((a, b) => a.m_IsPvE != b.m_IsPvE ? (a.m_IsPvE ? 1 : -1) : string.CompareOrdinal(a.m_SetId.ToString(), b.m_SetId.ToString()));
                foreach (CardTemplate c in all) if (!s_CardsByName.ContainsKey(c.m_Name)) s_CardsByName[c.m_Name] = c;
            }
            CardTemplate t;
            return s_CardsByName.TryGetValue(name, out t) ? t : null;
        }

        public static ResourceId ChampionId(string name)
        {
            foreach (ChampionTemplate ch in Singleton<TemplateManager>.Instance.Champions.Values)
                if (ch != null && ch.m_ChampionType == EChampionType.PvPChampion && string.Equals(ch.m_Name, name, StringComparison.OrdinalIgnoreCase)) return ch.m_Id;
            List<DeckTemplate> decks = Decks.PrebuiltPvp();
            return decks.Count > 0 ? decks[0].m_ChampionId : new ResourceId(GameConfig.DefaultChampionTemplateId);
        }

        /// <summary>A deck as "count name" lines (to import the player's decks into a pack).</summary>
        public static List<string> CardLines(DeckTemplate d)
        {
            List<string> res = new List<string>();
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            foreach (TemplateCardStack s in d.m_DeckResources)
            {
                CardTemplate c;
                if (tm.Cards.TryGetValue(s.m_idTemplate, out c) && c != null) res.Add(s.m_Count + " " + c.m_Name);
            }
            return res;
        }

        // ------------------------------------------------------------------
        // The next match
        // ------------------------------------------------------------------

        private static Opponent s_Pending;
        private static Opponent s_InBattle;
        private static bool s_Applied;

        /// <summary>The opponent of the next match started (its skill and handicaps).</summary>
        public static void SetNext(Opponent o) { s_Pending = o; }

        /// <summary>The AI's personality for the match that starts (Patches.SendStartEncounter).</summary>
        public static AIPersonality TakePersonality()
        {
            s_InBattle = s_Pending;
            s_Pending = null;
            s_Applied = false;
            AIPersonality p = new AIPersonality();
            if (s_InBattle == null) return p;
            try { p.Difficulty = (EDifficulty)Enum.Parse(typeof(EDifficulty), s_InBattle.Skill, true); }
            catch { Debug.LogWarning("[HexRevival] [Opponents] unknown skill " + s_InBattle.Skill); }
            p.CardNoiseRange = Math.Max(0, s_InBattle.Noise);
            Debug.Log("[HexRevival] [Opponents] " + s_InBattle.Name + ": skill " + p.Difficulty + ", noise " + p.CardNoiseRange);
            return p;
        }

        public static EDeckPersonality PersonalityOf(Opponent o)
        {
            try { return o != null ? (EDeckPersonality)Enum.Parse(typeof(EDeckPersonality), o.Personality, true) : EDeckPersonality.Default; }
            catch { return EDeckPersonality.Default; }
        }

        /// <summary>Every server update: the handicaps, once the mulligan is over.</summary>
        public static void OnSessionTick(StandaloneSession ss)
        {
            if (s_Applied || s_InBattle == null || ss == null || (int)ss.CurrentTurnPhase <= (int)ETurnPhases.Mulligan) return;
            s_Applied = true;
            Apply(ss, Cheats.FindPlayer(ss, "ai"), s_InBattle.AI);
            Apply(ss, Cheats.FindPlayer(ss, "j"), s_InBattle.Player);
        }

        private static void Apply(StandaloneSession ss, Player p, Handicap h)
        {
            if (p == null || h == null || h.IsEmpty || p.m_ChampionCard == null) return;
            try
            {
                if (h.Health != 0)
                {
                    int now = p.m_ChampionCard.CurrentHealthValue;
                    ss.SetChampionHealth(p.m_ChampionCard, Math.Max(1, now + h.Health), false);
                }
                if (h.Resources > 0) Cheats.AddResourcesOnly(ss, p, h.Resources);
                if (h.Charges > 0) ss.AddChargePoints(p.m_ChampionCard, h.Charges, false);
                for (int i = 0; i < h.Cards; i++) Cheats.DrawCard(ss, p);
                Server.MarkDirty();
                Debug.Log("[HexRevival] [Opponents] handicap " + p.m_PlayerId + ": health " + h.Health + ", resources " + h.Resources + ", charges " + h.Charges + ", cards " + h.Cards);
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Opponents] handicap: " + e); }
        }
    }
}
