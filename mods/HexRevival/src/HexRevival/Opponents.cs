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
    /// An AI opponent at play time (resolved from the AI configuration, see AiConfig): its deck (a shared deck, a deck of
    /// the game or an encounter), the AI's skill and style, its arena role, and the handicaps applied after the mulligan.
    /// </summary>
    public static class Opponents
    {
        /// <summary>Modes (see AiConfig).</summary>
        public const string Ladder = AiConfig.Ladder, Gauntlet = AiConfig.Gauntlet, Limited = AiConfig.Limited, Arena = AiConfig.Arena;

        public class Opponent
        {
            public string Name = "";
            /// <summary>A shared deck (its id), see Decks.</summary>
            public ulong CustomDeck;
            /// <summary>An encounter of the game (its scene id): its own AI deck and rules.</summary>
            public string Encounter = "";
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
            /// <summary>Arena: a boss (the last fight of a tier), an elite, or a rare encounter (Uruunaz, Zakiir: only by chance).</summary>
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
            /// <summary>Cards in play from the start (names).</summary>
            public List<string> InPlay = new List<string>();
            /// <summary>Shards in play from the start, by color: Blood, Diamond, Ruby, Sapphire, Wild (resource and threshold).</summary>
            public int[] Shards = new int[5];
            public bool IsEmpty { get { return Health == 0 && Resources == 0 && Charges == 0 && Cards == 0 && (InPlay == null || InPlay.Count == 0) && ShardCount == 0; } }
            public int ShardCount { get { int n = 0; if (Shards != null) foreach (int x in Shards) n += x; return n; } }

            public static readonly ECardShards[] ShardColors = { ECardShards.Blood, ECardShards.Diamond, ECardShards.Ruby, ECardShards.Sapphire, ECardShards.Wild };

            public Handicap Clone()
            {
                Handicap h = (Handicap)MemberwiseClone();
                h.InPlay = InPlay != null ? new List<string>(InPlay) : new List<string>();
                h.Shards = new int[5];
                if (Shards != null) Array.Copy(Shards, h.Shards, Math.Min(5, Shards.Length));
                return h;
            }

            /// <summary>The sum of two handicaps (a mode's own bonus added to a pack's).</summary>
            public static Handicap Add(Handicap a, Handicap b)
            {
                if (b == null) return a;
                if (a == null) return b;
                Handicap r = new Handicap { Health = a.Health + b.Health, Resources = a.Resources + b.Resources, Charges = a.Charges + b.Charges, Cards = a.Cards + b.Cards };
                for (int i = 0; i < 5; i++) r.Shards[i] = (a.Shards != null && i < a.Shards.Length ? a.Shards[i] : 0) + (b.Shards != null && i < b.Shards.Length ? b.Shards[i] : 0);
                if (a.InPlay != null) r.InPlay.AddRange(a.InPlay);
                if (b.InPlay != null) r.InPlay.AddRange(b.InPlay);
                return r;
            }
        }

        // ------------------------------------------------------------------
        // Choosing and building an opponent
        // ------------------------------------------------------------------

        /// <summary>An opponent of the account's AI configuration for this mode and value (stars, wins, arena tier, bounty).</summary>
        public static Opponent Pick(string mode, int value, Func<Opponent, bool> filter = null) { return AiConfig.Pick(mode, value, filter); }

        /// <summary>The scene of a match against this opponent: its encounter, or the neutral one.</summary>
        public static ResourceId SceneOf(Opponent o)
        {
            if (o != null && !string.IsNullOrEmpty(o.Encounter))
                try { return new ResourceId(new Guid(o.Encounter)); } catch { }
            return Patches.NeutralEncounterId;
        }

        /// <summary>The opponent's deck as the AI's deck (null: its cards or prebuilt deck are unknown).</summary>
        public static DeckTemplate DeckOf(Opponent o)
        {
            if (o == null || !string.IsNullOrEmpty(o.Encounter)) return null;   // an encounter plays its own deck
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            if (o.CustomDeck != 0)
            {
                DeckTemplate own = Decks.ToTemplate(new UID(UID.Type.Deck, o.CustomDeck));
                if (own != null) own.m_Id = new ResourceId(StableGuid("deck|" + o.CustomDeck));
                return own;
            }
            if (o.Cards == null || o.Cards.Count == 0)
            {
                if (string.IsNullOrEmpty(o.Prebuilt)) return null;
                // A few game decks share a name (an arena opponent and its elite): the champion tells them apart.
                foreach (DeckTemplate d in tm.Decks.Values)
                    if (d != null && d.m_DeckName == o.Prebuilt && d.m_ChampionId.IsValid && (string.IsNullOrEmpty(o.Champion) || ChampionName(d.m_ChampionId) == o.Champion)) return d;
                Debug.LogWarning("[HexRevival] [Opponents] no prebuilt deck " + o.Prebuilt);
                return null;
            }
            // A stable id (the arena screens look the deck up in the game's templates, see Register).
            DeckTemplate res = new DeckTemplate();
            res.m_Id = new ResourceId(StableGuid("opponent|" + o.Name + "|" + o.Champion + "|" + string.Join(",", o.Cards.ToArray())));
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

        private static string ChampionName(ResourceId id)
        {
            ChampionTemplate ch = Singleton<TemplateManager>.Instance.GetChampionTemplate(id);
            return ch != null ? ch.m_Name : "";
        }

        public static Guid StableGuid(string text)
        {
            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
                return new Guid(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text)));
        }

        /// <summary>
        /// Adds a deck built from a pack to the game's deck templates (TemplateManager.m_Decks), for the screens
        /// that show an opponent from its deck id (the arena's).
        /// </summary>
        public static void Register(DeckTemplate d)
        {
            if (d == null) return;
            try
            {
                System.Reflection.FieldInfo f = typeof(TemplateManager).GetField("m_Decks", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Dictionary<ResourceId, DeckTemplate> decks = f.GetValue(Singleton<TemplateManager>.Instance) as Dictionary<ResourceId, DeckTemplate>;
                if (decks != null && !decks.ContainsKey(d.m_Id)) decks[d.m_Id] = d;
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Opponents] register " + d.m_DeckName + ": " + e.Message); }
        }

        private static Dictionary<string, CardTemplate> s_CardsByName;

        /// <summary>One printing per card name (see CardByName), sorted by name: the card picker's list.</summary>
        public static List<CardTemplate> CardsByName()
        {
            CardByName("");
            List<CardTemplate> res = new List<CardTemplate>(s_CardsByName.Values);
            res.Sort((a, b) => string.Compare(a.m_Name, b.m_Name, StringComparison.OrdinalIgnoreCase));
            return res;
        }

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

        private static Handicap s_ExtraAI, s_ExtraPlayer, s_BattleExtraAI, s_BattleExtraPlayer;

        /// <summary>The opponent of the next match started (its skill and handicaps), and a mode's own extras (arena challenges, buffs).</summary>
        public static void SetNext(Opponent o, Handicap extraAI = null, Handicap extraPlayer = null)
        {
            s_Pending = o;
            s_ExtraAI = extraAI;
            s_ExtraPlayer = extraPlayer;
        }

        /// <summary>The AI's personality for the match that starts (Patches.SendStartEncounter).</summary>
        public static AIPersonality TakePersonality()
        {
            s_InBattle = s_Pending;
            s_BattleExtraAI = s_ExtraAI;
            s_BattleExtraPlayer = s_ExtraPlayer;
            s_Pending = null;
            s_ExtraAI = s_ExtraPlayer = null;
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
            if (s_Applied || (s_InBattle == null && s_BattleExtraAI == null && s_BattleExtraPlayer == null) || ss == null || (int)ss.CurrentTurnPhase <= (int)ETurnPhases.Mulligan) return;
            s_Applied = true;
            Apply(ss, Cheats.FindPlayer(ss, "ai"), Handicap.Add(s_InBattle != null ? s_InBattle.AI : null, s_BattleExtraAI));
            Apply(ss, Cheats.FindPlayer(ss, "j"), Handicap.Add(s_InBattle != null ? s_InBattle.Player : null, s_BattleExtraPlayer));
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
                // Shards of a color: a resource and its threshold each.
                if (h.Shards != null)
                    for (int i = 0; i < h.Shards.Length && i < 5; i++)
                        if (h.Shards[i] > 0)
                        {
                            ss.IncrementPlayerTotalResourcePool(p, h.Shards[i], false);
                            ss.IncrementPlayerCurrentResourcePool(p, h.Shards[i], false);
                            ss.IncrementPlayerResourceThreshold(p, Handicap.ShardColors[i], h.Shards[i], false);
                        }
                if (h.Charges > 0) ss.AddChargePoints(p.m_ChampionCard, h.Charges, false);
                for (int i = 0; i < h.Cards; i++) Cheats.DrawCard(ss, p);
                if (h.InPlay != null)
                    foreach (string name in h.InPlay)
                    {
                        if (string.IsNullOrEmpty((name ?? "").Trim())) continue;
                        CardTemplate c = CardByName((name ?? "").Trim());
                        if (c == null) { Debug.LogWarning("[HexRevival] [Opponents] unknown card in play: " + name); continue; }
                        ss.CreateEncounterCardsInZone(p, c.m_Id, 1, ECardCollections.Warzone, ECardLocations.Unknown);
                    }
                Server.MarkDirty();
                Debug.Log("[HexRevival] [Opponents] handicap " + p.m_PlayerId + ": health " + h.Health + ", resources " + h.Resources + ", charges " + h.Charges + ", cards " + h.Cards + ", in play " + (h.InPlay != null ? string.Join(", ", h.InPlay.ToArray()) : ""));
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Opponents] handicap: " + e); }
        }
    }
}
