using System;
using System.Collections.Generic;
using System.IO;
using Game.Client;
using Game.Client.Network.Campaign;
using Game.Shared;
using Game.Shared.Domain;
using Game.Shared.Mechanics;
using Game.Shared.Network.Campaign;
using Reckoning.Campaign.Messages.Arena;
using Reckoning.Game;
using Reckoning.Game.Arena;
using UnityEngine;
using JsonUtils = Game.Shared.Network.HConnect.Utils;

namespace HexRevival
{
    /// <summary>
    /// The Frost Ring Arena offline, on the game's own arena screens (UIArenaCampaignViewModel, ArenaClient): their
    /// CampaignService requests are answered here (Harmony prefixes, see Plugin.cs) and the battles run locally.
    ///
    /// A run (Src.HexCompendium, "Frost Ring Arena"; the client's screens): 20 fights in 4 tiers of 5, a boss closing
    /// each tier; tier 1: 4 standard fights; tier 2: 3 standard, 1 elite; tiers 3 and 4: 2 standard, 2 elite, tier 4's
    /// boss an elite. Loss marks and challenges: see OnGameEnded. The opponents come from the account's opponent pack
    /// (arena tiers, see Opponents): by default the game's own "Arena_" decks. Rewards: see Rewards. Not done: the
    /// buyout, Hogarth's dialogues.
    /// </summary>
    public static class Arena
    {
        private const int Fights = 20, PerTier = 5, MaxLosses = 3;

        // ------------------------------------------------------------------
        // The run (saved per account: arena.json)
        // ------------------------------------------------------------------

        private class Run
        {
            public ArenaData Info;
            public List<ArenaFight> Fights = new List<ArenaFight>();
            public int Current;                                     // index of the current fight
            public List<Opponents.Opponent> Opponents = new List<Opponents.Opponent>();   // by fight
            public List<ArenaReward> Loot = new List<ArenaReward>();
            public bool CashedOut;                                  // the client asks twice (summary, then the loot window)
            public int Gold;
            public int TierLosses, BossLosses;                      // the loss marks (see OnGameEnded)
            public List<string> Challenges = new List<string>();    // by fight: a challenge's key, or ""
            public List<string> Buffs = new List<string>();         // buffs for the next boss
            public bool BonusChest;                                 // see Rewards.ForWin
        }

        private class Store
        {
            public ulong NextId = 1;
            public Run Run;
        }

        private static Store s_Store;
        private static string s_StorePath;
        private static string StorePath { get { return Saves.File("arena.json"); } }

        private static Store Data
        {
            get
            {
                if (s_Store != null && s_StorePath == StorePath) return s_Store;
                s_StorePath = StorePath;
                s_Store = null;
                try { if (File.Exists(s_StorePath)) JsonUtils.DeserializeJson(File.ReadAllBytes(s_StorePath), (Store x) => { s_Store = x; }); }
                catch (Exception e) { Debug.LogError("[HexRevival] [Arena] read: " + e); }
                if (s_Store == null) s_Store = new Store();
                if (s_Store.Run != null) Prepare(s_Store.Run);
                return s_Store;
            }
        }

        private static void Save()
        {
            try
            {
                byte[] b = null;
                JsonUtils.SerializeJson(Data, (byte[] x) => { b = x; });
                File.WriteAllBytes(StorePath, b);
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Arena] save: " + e); }
        }

        private static Run Current { get { return Data.Run; } }

        // ------------------------------------------------------------------
        // Challengers: the screens show an opponent from its challenger id and deck template
        // ------------------------------------------------------------------

        /// <summary>The master list the client keeps for the session (the same list object: new challengers are added to it).</summary>
        private static readonly List<ArenaChallenger> s_Master = new List<ArenaChallenger>();

        private static ulong ChallengerId(Opponents.Opponent o)
        {
            byte[] b = Opponents.StableGuid("arena|" + o.Name + "|" + o.Prebuilt + "|" + o.Champion + "|" + o.CustomDeck + "|" + o.Encounter).ToByteArray();
            return BitConverter.ToUInt64(b, 0) & 0x7fffffffffffffffUL;
        }

        private static ArenaChallenger Challenger(Opponents.Opponent o)
        {
            ulong id = ChallengerId(o);
            ArenaChallenger c = s_Master.Find(x => x.ChallengerID == id);
            if (c != null) return c;
            DeckTemplate d = Opponents.DeckOf(o);
            Opponents.Register(d);
            c = new ArenaChallenger
            {
                ChallengerID = id,
                EncounterDeck = d != null ? d.m_Id : ResourceId.Invalid,
                ChallengerName = o.Name,
                IsBoss = o.Boss ? "true" : "false",
                Equipment = new List<ResourceId>(),
            };
            s_Master.Add(c);
            return c;
        }

        /// <summary>Every opponent of the pack's arena tiers, and the run's, in the master list.</summary>
        private static void FillMaster()
        {
            foreach (Opponents.Opponent o in AiConfig.All(AiConfig.Arena)) Challenger(o);
            if (Current != null) Prepare(Current);
        }

        private static void Prepare(Run r)
        {
            foreach (Opponents.Opponent o in r.Opponents) Challenger(o);
            if (r.Challenges == null || r.Challenges.Count != r.Fights.Count) { r.Challenges = new List<string>(); foreach (ArenaFight f in r.Fights) r.Challenges.Add(""); }
            if (r.Buffs == null) r.Buffs = new List<string>();
            r.Info.Buffs = r.Buffs.ConvertAll(b => Buff(b).Shown);
        }

        // ------------------------------------------------------------------
        // Requests
        // ------------------------------------------------------------------

        /// <summary>The arena screen starts: the client's reconnection check, never answered offline, lets it join.</summary>
        public static void OnScreenStart(object viewModel)
        {
            // A notification flag left on by a window of a previous visit would queue every later window (the
            // cash out among them) forever; the loot to show is kept in the client's own loot list.
            ArenaClient client = Singleton<ArenaClient>.Instance;
            client.m_UINotificationActive = false;
            client.m_UINotificationQueue.Clear();
            LocalServer.Later(() =>
            {
                try
                {
                    System.Reflection.MethodInfo m = viewModel.GetType().GetMethod("ReconnectionResponse", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    m.Invoke(viewModel, new object[] { new Game.Client.Network.LoadBalancer.TryReconnectionToDisconnectedGameResponse() });
                }
                catch (Exception e) { Debug.LogError("[HexRevival] [Arena] screen start: " + e); }
            });
        }

        public static void Join(Delegate handler)
        {
            Run r = Current;
            JoinCampaignArenaResponse res = LocalServer.Ok<JoinCampaignArenaResponse>();
            res.Success = r != null;
            res.ErrorMessage = "";
            if (r != null)
            {
                res.ArenaInfo = r.Info;
                res.EncounterData = r.Fights[r.Current];
                res.ChallengerData = Challenger(r.Opponents[r.Current]);
                res.FightHistory = r.Fights;
            }
            Debug.Log("[HexRevival] [Arena] join: " + (r != null ? r.Info.Wins + " wins, " + r.Info.Loses + " losses, fight " + (r.Current + 1) : "no run"));
            LocalServer.Answer(typeof(CampaignService.JoinCampaignArena), res, handler);
        }

        public static void Refresh(Delegate handler)
        {
            Run r = Current;
            RefreshArenaInfoResponse res = LocalServer.Ok<RefreshArenaInfoResponse>();
            res.Success = r != null;
            res.ErrorMessage = "";
            if (r != null)
            {
                res.ArenaInfo = r.Info;
                res.EncounterData = r.Fights[r.Current];
                res.ChallengerData = Challenger(r.Opponents[r.Current]);
                res.FightHistory = r.Fights;
            }
            LocalServer.Answer(typeof(CampaignService.RefreshArenaInfo), res, handler);
        }

        public static void MasterList(Delegate handler)
        {
            FillMaster();
            GetMasterListOfChallengersResponse res = LocalServer.Ok<GetMasterListOfChallengersResponse>();
            res.Success = true;
            res.ErrorMessage = "";
            res.Challengers = s_Master;
            LocalServer.Answer(typeof(CampaignService.GetMasterListOfChallengers), res, handler);
        }

        /// <summary>AssignArenaDeck: a new run with this deck.</summary>
        public static void AssignDeck(ulong deckId, Delegate handler)
        {
            Run r = NewRun(deckId);
            AssignArenaDeckResponse res = LocalServer.Ok<AssignArenaDeckResponse>();
            res.ErrorMessage = "";
            res.Success = r != null;
            if (r == null) res.Error = EAssignArenaDeckError.InternalServerError;
            else
            {
                res.ArenaInfo = r.Info;
                res.EncounterData = r.Fights[0];
                res.ChallengerData = Challenger(r.Opponents[0]);
                res.FightHistory = r.Fights;
            }
            LocalServer.Answer(typeof(CampaignService.AssignArenaDeck), res, handler);
        }

        private static Run NewRun(ulong deckId)
        {
            Store st = Data;
            Run r = new Run();
            ulong id = st.NextId++;
            r.Info = new ArenaData
            {
                ArenaID = id,
                PlayerID = GameClient.Instance.PlayerProfile.ReckoningID.GetInstanceId(),
                GameMode = ECampaignDifficulty.NORMAL,
                DeckId = deckId,
                Buffs = new List<ArenaBuff>(),
            };
            HashSet<string> used = new HashSet<string>();
            for (int i = 0; i < Fights; i++)
            {
                int tier = i / PerTier + 1, slot = i % PerTier;
                bool boss = slot == PerTier - 1;
                int elites = tier == 1 ? 0 : tier == 2 ? 1 : 2;
                bool elite = !boss && slot >= PerTier - 1 - elites;
                // The fight's role (boss, elite, common), then any deck of the tier. Uruunaz and Zakiir are common decks
                // with a low frequency (see AiConfig).
                Opponents.Opponent o = Opponents.Pick(Opponents.Arena, tier, x => x.Boss == boss && (boss || x.Elite == elite) && !used.Contains(x.Name));
                if (o == null) o = Opponents.Pick(Opponents.Arena, tier, x => x.Boss == boss && !used.Contains(x.Name));
                if (o == null) o = Opponents.Pick(Opponents.Arena, tier, x => !used.Contains(x.Name));
                if (o == null) o = Opponents.Pick(Opponents.Arena, tier);
                if (o == null) { Debug.LogError("[HexRevival] [Arena] no opponent for tier " + tier + " in the arena's AI decks"); return null; }
                used.Add(o.Name);
                r.Opponents.Add(o);
                r.Fights.Add(new ArenaFight
                {
                    FightID = id * 100 + (ulong)i + 1,
                    FightTier = tier,
                    FightOrder = i,
                    ArenaInstance = id,
                    ChallengerInstance = Challenger(o).ChallengerID,
                    FightResults = "NONE",
                    RoundChallenge = ResourceId.Invalid,
                    ChallengeResponse = "NONE",
                });
            }
            // One fight of each tier but the boss may carry a challenge (Src.HexCompendium; PLACEHOLDER: always one).
            for (int i = 0; i < Fights; i++) r.Challenges.Add("");
            for (int t = 0; t < Fights / PerTier; t++)
                r.Challenges[t * PerTier + UnityEngine.Random.Range(0, PerTier - 1)] = ChallengeKeys[UnityEngine.Random.Range(0, ChallengeKeys.Length)];
            r.Info.FightId = r.Fights[0].FightID;
            st.Run = r;
            Save();
            Debug.Log("[HexRevival] [Arena] new run with deck " + deckId + ": " + string.Join(", ", r.Opponents.ConvertAll(o => o.Name).ToArray()));
            return r;
        }

        /// <summary>BattleChampion: the current fight, against the AI with the challenger's deck.</summary>
        public static void Battle()
        {
            Run r = Current;
            if (r == null) return;
            if (!Decks.Exists(r.Info.DeckId))
            {
                // The run's deck was deleted: withdraw so that the player can start again with another deck.
                Notices.Show("Your arena deck no longer exists: withdraw from this run and start a new one.");
                return;
            }
            Opponents.Opponent o = r.Opponents[r.Current];
            DeckTemplate deck = Opponents.DeckOf(o);
            bool boss = (r.Current + 1) % PerTier == 0;
            Opponents.Handicap ai = null, me = null;
            string ch = r.Challenges[r.Current];
            if (ch != "")
            {
                Challenge c = Challenges[ch];
                ai = c.AI; me = c.Player;
                Notices.Show(c.Name + ": " + c.Text + " Goal: win the game.");
            }
            if (boss)
                foreach (string b in r.Buffs) me = Opponents.Handicap.Add(me, Buff(b).Effect);
            Opponents.SetNext(o, ai, me);
            ulong arenaId = r.Info.ArenaID, owner = r.Info.PlayerID;
            Patches.PendingEncounterSetup = e =>
            {
                // The game's arena battle: its end goes back to the arena screen (UIBattle.Quit).
                e.SessionFlags |= ESessionFlags.IsPvEArena;
                e.ArenaInstance = arenaId;
                e.ArenaOwner = owner;
            };
            s_InBattle = true;
            Patches.StartMatch(new UID(UID.Type.Deck, r.Info.DeckId), Opponents.SceneOf(o), deck,
                "arena fight " + (r.Current + 1) + " vs " + o.Name, Opponents.PersonalityOf(o));
        }

        private static bool s_InBattle;

        /// <summary>End of a match. Returns true if it was an arena battle.</summary>
        public static bool OnGameEnded(bool won)
        {
            if (!s_InBattle) return false;
            s_InBattle = false;
            Run r = Current;
            if (r == null) return true;
            ArenaFight f = r.Fights[r.Current];
            Opponents.Opponent o = r.Opponents[r.Current];
            bool boss = (r.Current + 1) % PerTier == 0;
            string challenge = r.Challenges[r.Current];
            List<ArenaReward> news = new List<ArenaReward>();
            if (won)
            {
                f.FightResults = "WIN";
                r.Info.Wins++;
                // A challenge won removes a loss mark, otherwise gives a buff for the next boss (Src.HexCompendium;
                // Src.Video: Hogarth's "As a reward, for your next boss fight, you will start the game with an Arena Brawler in play").
                if (challenge != "")
                {
                    if (r.TierLosses > 0) r.TierLosses--;
                    else if (r.BossLosses > 0) r.BossLosses--;
                    else if (r.Buffs.Count < 2) { string b = BuffKeys[UnityEngine.Random.Range(0, BuffKeys.Length)]; r.Buffs.Add(b); Notices.Show(Buff(b).Shown.Name + ": " + Buff(b).Shown.Description); }
                    r.BonusChest = true;
                }
                news.AddRange(Rewards.ForWin(r, f, o, boss));
                if (boss) { r.TierLosses = 0; r.Buffs.Clear(); r.BonusChest = false; }
                if (r.Current < Fights - 1) r.Current++;
            }
            else
            {
                // Loss marks (Src.Video, 2017-2018 runs): a lost fight is passed (a red X) and counts until the tier's boss
                // is beaten; a lost boss is fought again and its mark stays. 3 marks end the run.
                r.Info.LastTierLoss |= 1 << f.FightTier;   // TierLoss: tier 1 = 2, tier 2 = 4... (no "perfect tier")
                if (boss) r.BossLosses++; else r.TierLosses++;
                bool over = r.BossLosses + r.TierLosses >= MaxLosses;
                if (!boss || over) f.FightResults = "LOSE";
                if (!boss && !over && r.Current < Fights - 1) r.Current++;
            }
            r.Info.Loses = r.BossLosses + r.TierLosses;
            r.Info.Buffs = r.Buffs.ConvertAll(b => Buff(b).Shown);
            r.Info.FightId = r.Fights[r.Current].FightID;
            r.Loot.AddRange(news);
            Save();
            Debug.Log("[HexRevival] [Arena] fight " + (f.FightOrder + 1) + " " + (won ? "won" : "lost") + ": " + r.Info.Wins + "-" + r.Info.Loses);
            if (news.Count > 0)
                try { Singleton<ArenaClient>.Instance.UpdateLoot(new LootUpdateEventArgs { Rewards = news }); }
                catch (Exception e) { Debug.LogError("[HexRevival] [Arena] loot: " + e); }
            return true;
        }

        /// <summary>DoArenaCashOut: the rewards of the run, granted.</summary>
        public static void CashOut(Delegate handler)
        {
            Run r = Current;
            DoArenaCashOutResponse res = LocalServer.Ok<DoArenaCashOutResponse>();
            res.ErrorMessage = "";
            res.AllLoot = new List<ArenaReward>();
            if (r == null) res.Error = EDoArenaCashOutError.InternalServerError;
            else if (r.Info.Wins == 0) res.Error = EDoArenaCashOutError.FailedNoWins;
            else
            {
                if (!r.CashedOut)
                {
                    r.Loot.AddRange(Rewards.ForEnd(r));
                    r.Gold = Rewards.Grant(r.Loot);
                    r.CashedOut = true;
                    Save();
                }
                res.AllLoot = r.Loot;
                res.GoldWin = r.Gold;
                res.Success = true;
            }
            Debug.Log("[HexRevival] [Arena] cash out: " + res.Error + ", " + res.AllLoot.Count + " rewards, " + res.GoldWin + " gold");
            LocalServer.Answer(typeof(CampaignService.DoArenaCashOut), res, handler);
        }

        public static void Destroy(Delegate handler)
        {
            Data.Run = null;
            Save();
            DestroyArenaDataResponse res = LocalServer.Ok<DestroyArenaDataResponse>();
            res.Success = true;
            res.ErrorMessage = "";
            LocalServer.Answer(typeof(CampaignService.DestroyArenaData), res, handler);
        }

        // ------------------------------------------------------------------
        // Rewards
        // ------------------------------------------------------------------

        /// <summary>The rare challengers (their own rewards): Uruunaz and Zakiir.</summary>
        private static bool IsRare(Opponents.Opponent o) { return o.Name.StartsWith("Uruunaz") || o.Name.StartsWith("Zakiir"); }

        private static class Rewards
        {
            /// <summary>
            /// Gold per win, one pouch each (Src.Video: one pouch per victory; a run of 18 wins paid 8,400 gold, about 467 a
            /// pouch, not always the same). The split by fight is PLACEHOLDER (it gives about 8,500 for such a run).
            /// </summary>
            private const int GoldStandard = 400, GoldElite = 500, GoldBoss = 600;
            /// <summary>Uruunaz "nets you 100,000 gold" (Src.Steam2016-b, a player in April 2016).</summary>
            private const int GoldUruunaz = 100000;

            /// <summary>
            /// A win: a gold pouch. A boss win also opens the tier's chests (Src.Video: chests only come with the boss):
            /// "Boss Win" chests (1 in tier 1, 2 after, PLACEHOLDER from the counts seen), a "Perfect Tier Bonus" chest for a
            /// tier without a loss, a "Bonus" chest (seen twice, cause unknown: PLACEHOLDER, a tier whose challenge was won).
            /// </summary>
            public static List<ArenaReward> ForWin(Run r, ArenaFight f, Opponents.Opponent o, bool boss)
            {
                List<ArenaReward> res = new List<ArenaReward>();
                int gold = boss ? GoldBoss : o.Elite ? GoldElite : GoldStandard;
                res.Add(new ArenaReward { ArenaInstance = r.Info.ArenaID, FightInstance = f.FightID, Type = "GOLD", Reason = "GOLD", Gold = gold, Rarity = "", TemplateID = ResourceId.Invalid });
                r.Info.GoldPacks++;
                if (o.Name.StartsWith("Uruunaz"))
                    res.Add(new ArenaReward { ArenaInstance = r.Info.ArenaID, FightInstance = f.FightID, Type = "GOLD", Reason = "URUUNAZ", Gold = GoldUruunaz, Rarity = "", TemplateID = ResourceId.Invalid });
                if (IsRare(o)) AddChest(res, r, f, o.Name.StartsWith("Uruunaz") ? "URUUNAZ" : "ZAKIIR");
                if (boss)
                {
                    for (int i = 0; i < (f.FightTier == 1 ? 1 : 2); i++) AddChest(res, r, f, "WIN");
                    if ((r.Info.LastTierLoss & (1 << f.FightTier)) == 0) AddChest(res, r, f, "PERFECT");
                    if (r.BonusChest) AddChest(res, r, f, "BONUS");
                }
                // The sleeves of the bosses (Src.Game, their "Earned by defeating ..." texts).
                string sleeve = o.Name.StartsWith("Hogarth") ? "Hogarth Sleeve" : o.Name.StartsWith("Uruunaz") ? "Uruunaz Sleeve" : o.Name.StartsWith("Zakiir") ? "Matriarch of Flames Sleeve" : null;
                if (sleeve != null) AddSleeve(res, r, f, sleeve, IsRare(o) ? (o.Name.StartsWith("Uruunaz") ? "URUUNAZ" : "ZAKIIR") : "WIN");
                return res;
            }

            private static void AddChest(List<ArenaReward> into, Run r, ArenaFight f, string reason)
            {
                ArenaReward c = Chest(r, f, reason);
                if (c != null) into.Add(c);
            }

            /// <summary>End of the run: the completion and perfect run sleeves (Src.Game: "Earned by completing the Frost Ring Arena [without losing a life]").</summary>
            public static List<ArenaReward> ForEnd(Run r)
            {
                List<ArenaReward> res = new List<ArenaReward>();
                ArenaFight last = r.Fights[Fights - 1];
                if (last.FightResults != "WIN") return res;
                AddSleeve(res, r, last, "Frost Ring Arena Sleeve", "BONUS");
                if (r.Info.Loses == 0) AddSleeve(res, r, last, "Frost Ring Arena Perfect Sleeve", "PERFECT");
                return res;
            }

            private static void AddSleeve(List<ArenaReward> into, Run r, ArenaFight f, string name, string reason)
            {
                InventoryItemData s = Item(name);
                // Never twice (the loot window fails on twice the same sleeve).
                if (s == null || Inventory.Count(s.m_Id) > 0 || r.Loot.Exists(x => x.TemplateID == s.m_Id) || into.Exists(x => x.TemplateID == s.m_Id)) return;
                into.Add(new ArenaReward { ArenaInstance = r.Info.ArenaID, FightInstance = f.FightID, Type = "SLEEVE", Reason = reason, Rarity = "", TemplateID = s.m_Id });
            }

            /// <summary>
            /// A chest: a card (Src.Video: most chests; cards of the arena's set and of other sets: half each, PLACEHOLDER),
            /// sometimes stardust (Src.Video: "Common Stardust x2", "Rare Stardust"; 20% PLACEHOLDER), rarely an arena
            /// equipment (not seen in 10 chests; 5% PLACEHOLDER).
            /// </summary>
            private static ArenaReward Chest(Run r, ArenaFight f, string reason)
            {
                ArenaReward res = new ArenaReward { ArenaInstance = r.Info.ArenaID, FightInstance = f.FightID, Reason = reason, Rarity = "" };
                float x = UnityEngine.Random.value;
                if (x < 0.05f)
                {
                    List<InventoryItemData> eq = ArenaEquipment();
                    if (eq.Count > 0) { res.Type = "EQUIPMENT"; res.TemplateID = eq[UnityEngine.Random.Range(0, eq.Count)].m_Id; r.Info.EquipmentPacks++; return res; }
                }
                if (x < 0.25f)
                {
                    InventoryItemData dust = Item(UnityEngine.Random.value < 0.7f ? "Common Stardust" : "Rare Stardust");
                    if (dust != null) { res.Type = "EQUIPMENT"; res.TemplateID = dust.m_Id; r.Info.EquipmentPacks++; return res; }
                }
                CardTemplate c = null;
                if (UnityEngine.Random.value < 0.5f) { List<CardTemplate> cards = ArenaCards(); if (cards.Count > 0) c = cards[UnityEngine.Random.Range(0, cards.Count)]; }
                if (c == null) c = AnySetCard();
                if (c == null) return null;
                res.Type = "CARD";
                res.Rarity = c.m_CardRarity.ToString();
                res.TemplateID = c.m_Id;
                r.Info.CardPacks++;
                return res;
            }

            /// <summary>A card of the game's booster sets (common 10, uncommon 3, rare 1: PLACEHOLDER weights).</summary>
            private static CardTemplate AnySetCard()
            {
                List<CardSetTemplate> sets = new List<CardSetTemplate>();
                foreach (InventoryItemData i in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                {
                    InventoryCardPack p = i as InventoryCardPack;
                    CardSetTemplate set;
                    if (p != null && p.m_CardPackType == ECardPackType.BoosterPack && Singleton<TemplateManager>.Instance.CardSets.TryGetValue(p.m_SetId, out set) && set != null && !sets.Contains(set)) sets.Add(set);
                }
                if (sets.Count == 0) return null;
                float x = UnityEngine.Random.value * 14;
                ERarity rarity = x < 10 ? ERarity.Common : x < 13 ? ERarity.Uncommon : ERarity.Rare;
                return Progression.RandomCard(sets[UnityEngine.Random.Range(0, sets.Count)], rarity, null);
            }

            /// <summary>Gives the run's rewards; returns the gold.</summary>
            public static int Grant(List<ArenaReward> loot)
            {
                int gold = 0;
                foreach (ArenaReward x in loot)
                {
                    try
                    {
                        switch (x.Type)
                        {
                            case "GOLD": gold += x.Gold; break;
                            case "CARD": Collection.Grant(x.TemplateID, 1); break;
                            default:
                                inventory_bits it = Inventory.GrantSilently(x.TemplateID);
                                if (it != null) GameClient.Instance.PlayerProfile.AddInventoryItem(it);
                                break;
                        }
                    }
                    catch (Exception e) { Debug.LogError("[HexRevival] [Arena] grant " + x.Type + " " + x.TemplateID + ": " + e.Message); }
                }
                if (Saves.Current != null && gold > 0)
                {
                    Saves.Current.Gold += gold;
                    Saves.SaveCurrent();
                    try { GameClient.Instance.PlayerProfile.UpdateBalance((int)Saves.Current.Gold, (int)Saves.Current.Platinum); } catch { }
                }
                return gold;
            }
        }

        // ------------------------------------------------------------------
        // Challenges and buffs
        // ------------------------------------------------------------------

        private class Challenge
        {
            public string Name, Text;
            public Opponents.Handicap AI, Player;
        }

        /// <summary>The challenges seen (Src.Video: their texts on the battle screen; all with the goal "Win the game").</summary>
        private static readonly Dictionary<string, Challenge> Challenges = new Dictionary<string, Challenge>
        {
            { "reflection", new Challenge { Name = "Reflection Challenge", Text = "Both players start this game with Locket of Reflection in play.",
                AI = new Opponents.Handicap { InPlay = new List<string> { "Locket of Reflection" } }, Player = new Opponents.Handicap { InPlay = new List<string> { "Locket of Reflection" } } } },
            { "primordial", new Challenge { Name = "Primordial Challenge", Text = "Your opponent starts this game with Primordial Caves in play.",
                AI = new Opponents.Handicap { InPlay = new List<string> { "Primordial Caves" } } } },
            { "gooplasm", new Challenge { Name = "Gooplasm Challenge", Text = "Your opponent starts this game with a Gooplasm in play.",
                AI = new Opponents.Handicap { InPlay = new List<string> { "Gooplasm" } } } },
            { "fulmination", new Challenge { Name = "Fulmination Challenge", Text = "Your opponent starts this game with Cerebral Fulmination in play.",
                AI = new Opponents.Handicap { InPlay = new List<string> { "Cerebral Fulmination" } } } },
        };
        private static readonly string[] ChallengeKeys = { "reflection", "primordial", "gooplasm", "fulmination" };

        private class BuffDef { public ArenaBuff Shown; public Opponents.Handicap Effect; }

        /// <summary>The buffs (Src.HexCompendium: Brawler, Charge, Health, Resource; Src.Video: "Brawler Buff - You start the game with an Arena Brawler in play").</summary>
        private static BuffDef Buff(string key)
        {
            switch (key)
            {
                case "charge": return new BuffDef { Shown = Shown("Charge Buff", "You start the game with 2 extra charges."), Effect = new Opponents.Handicap { Charges = 2 } };
                case "health": return new BuffDef { Shown = Shown("Health Buff", "You start the game with 5 extra health."), Effect = new Opponents.Handicap { Health = 5 } };
                case "resource": return new BuffDef { Shown = Shown("Resource Buff", "You start the game with a resource in play."), Effect = new Opponents.Handicap { Resources = 1 } };
                default: return new BuffDef { Shown = Shown("Brawler Buff", "You start the game with an Arena Brawler in play."), Effect = new Opponents.Handicap { InPlay = new List<string> { "Arena Brawler" } } };
            }
        }
        private static readonly string[] BuffKeys = { "brawler", "charge", "health", "resource" };

        private static ArenaBuff Shown(string name, string text)
        {
            return new ArenaBuff { Name = name, Description = text, TemplateID = ResourceId.Invalid, ConversationID = ResourceId.Invalid };
        }

        private static List<CardTemplate> s_Cards;
        private static List<InventoryItemData> s_Equipment;

        /// <summary>The arena's card set (Src.Game: Set01_PvE_Arena), without its shards.</summary>
        private static List<CardTemplate> ArenaCards()
        {
            if (s_Cards != null) return s_Cards;
            s_Cards = new List<CardTemplate>();
            ResourceId set = Game.Shared.Resources.BuiltInResources.Set1PvEArena;
            foreach (CardTemplate c in Singleton<TemplateManager>.Instance.Cards.Values)
                if (c != null && c.m_SetId == set && (c.m_CardType & ECardTypes.Resource) == 0) s_Cards.Add(c);
            return s_Cards;
        }

        /// <summary>The arena's equipment (Src.Game: the items noted "Arena" by the designers).</summary>
        private static List<InventoryItemData> ArenaEquipment()
        {
            if (s_Equipment != null) return s_Equipment;
            s_Equipment = new List<InventoryItemData>();
            foreach (InventoryItemData i in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                if (i is InventoryEquipmentData && i.m_DesignNotes != null && i.m_DesignNotes.Trim() == "Arena") s_Equipment.Add(i);
            return s_Equipment;
        }

        private static InventoryItemData Item(string name)
        {
            foreach (InventoryItemData i in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                if (i != null && i.m_Name == name) return i;
            return null;
        }

        /// <summary>Dev: "arena [state] | win | lose | deck <id> | battle | reset".</summary>
        public static string Command(string[] parts)
        {
            string sub = parts.Length > 1 ? parts[1] : "state";
            Run r = Current;
            switch (sub)
            {
                case "state":
                    if (r == null) return "no run (pool: " + ArenaCards().Count + " cards, " + ArenaEquipment().Count + " equipment)";
                    string s = r.Info.Wins + "-" + r.Info.Loses + ", fight " + (r.Current + 1) + ", loot " + r.Loot.Count + ", buffs " + string.Join(",", r.Buffs.ToArray());
                    for (int i = 0; i < r.Fights.Count; i++) s += "\n" + (i + 1) + " t" + r.Fights[i].FightTier + " " + r.Opponents[i].Name + (r.Opponents[i].Boss ? " (boss)" : r.Opponents[i].Elite ? " (elite)" : "") + " " + r.Fights[i].FightResults + (r.Challenges[i] != "" ? " [" + r.Challenges[i] + "]" : "");
                    return s;
                case "win":
                case "lose":
                    s_InBattle = true;
                    OnGameEnded(sub == "win");
                    return "ok";
                case "deck":
                    if (r == null || parts.Length < 3) return "usage: arena deck <id>";
                    r.Info.DeckId = ulong.Parse(parts[2]);
                    Save();
                    return "deck " + r.Info.DeckId;
                case "battle":
                    Battle();
                    return "ok";
                case "reset":
                    Data.Run = null;
                    Save();
                    return "run removed";
            }
            return "usage: arena [state] | win | lose | deck <id> | battle | reset";
        }
    }
}
