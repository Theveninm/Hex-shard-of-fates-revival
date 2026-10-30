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

        /// <summary>The fights with a challenge carry one for the client (RoundChallenge), so that Hogarth offers it.</summary>
        private static void MarkChallenges(Run r)
        {
            for (int i = 0; i < r.Fights.Count && i < r.Challenges.Count; i++)
            {
                ArenaFight f = r.Fights[i];
                if (r.Challenges[i] == "" || !Challenges.ContainsKey(r.Challenges[i])) continue;
                if (!f.RoundChallenge.IsValid) f.RoundChallenge = new ResourceId(Opponents.StableGuid("arena-challenge|" + r.Challenges[i]));
                if (string.IsNullOrEmpty(f.ChallengeResponse)) f.ChallengeResponse = "NONE";
            }
        }

        /// <summary>The current fight's challenge as Hogarth's window shows it (null if none); its texts already in the game's language.</summary>
        private static ArenaMCChallenge McChallenge(Run r)
        {
            string key = r.Current < r.Challenges.Count ? r.Challenges[r.Current] : "";
            Challenge c;
            if (key == "" || !Challenges.TryGetValue(key, out c)) return null;
            ArenaFight f = r.Fights[r.Current];
            return new ArenaMCChallenge { ChallengeID = f.FightID, TemplateID = f.RoundChallenge, Header = c.Name, Body = c.Text };
        }

        /// <summary>
        /// After the arena's data reached the screen: Hogarth offers the current fight's challenge (the client's own
        /// window, UIArenaCampaignViewModel.CheckMCChallenge, which nothing in the client calls).
        /// </summary>
        private static void OfferChallenge(Run r)
        {
            if (r == null || r.Current >= r.Fights.Count || r.Fights[r.Current].ChallengeResponse != "NONE" || McChallenge(r) == null) return;
            LocalServer.Later(() =>
            {
                try
                {
                    UIArenaCampaignViewModel vm = UnityEngine.Object.FindObjectOfType<UIArenaCampaignViewModel>();
                    if (vm != null) typeof(UIArenaCampaignViewModel).GetMethod("CheckMCChallenge", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(vm, null);
                }
                catch (Exception e) { Debug.LogWarning("[HexRevival] [Arena] challenge window: " + e.Message); }
            });
        }

        /// <summary>A fight's challenger as the screens show it: with the equipment it plays in that fight.</summary>
        private static ArenaChallenger ChallengerOf(Run r, int fight)
        {
            ArenaChallenger c = Challenger(r.Opponents[fight]);
            c.Equipment = FightEquipment(r, fight, Opponents.DeckOf(r.Opponents[fight]));
            return c;
        }

        /// <summary>
        /// The opponent's equipment in a fight: "Tier 1: Champions in this Tier each use no equipment. Tier 2: [...] 2
        /// equipment. Tier 3: [...] 3 [...]. Tier 4: [...] 4" (Src.HexCodex, the arena's own text). The game's arena
        /// decks carry none (the server chose it): pieces that modify cards of the deck, one per slot, drawn once per
        /// run and fight (which pieces: PLACEHOLDER).
        /// </summary>
        private static List<ResourceId> FightEquipment(Run r, int fight, DeckTemplate deck)
        {
            List<ResourceId> res = new List<ResourceId>();
            int n = fight < r.Fights.Count && r.Fights[fight].FightTier >= 2 ? r.Fights[fight].FightTier : 0;
            if (n == 0 || deck == null) return res;
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            // The pieces that modify a card of the deck (CardTemplate.m_EquipmentSlots, as the game's deck editor).
            List<InventoryEquipmentData> fits = new List<InventoryEquipmentData>();
            HashSet<ResourceId> seen = new HashSet<ResourceId>();
            foreach (TemplateCardStack st in deck.m_DeckResources)
            {
                CardTemplate c = tm.GetCardTemplate(st.m_idTemplate);
                if (c == null || c.m_EquipmentSlots == null) continue;
                foreach (ResourceId id in c.m_EquipmentSlots)
                {
                    if (!id.IsValid || !seen.Add(id)) continue;
                    InventoryEquipmentData e = tm.GetInventoryItem(id) as InventoryEquipmentData;
                    if (e != null) fits.Add(e);
                }
            }
            fits.Sort((a, b) => a.m_Id.m_Guid.CompareTo(b.m_Id.m_Guid));
            System.Random rng = new System.Random((int)(r.Info.ArenaID * 31 + (ulong)fight));
            HashSet<EEquipmentType> slots = new HashSet<EEquipmentType>();
            while (fits.Count > 0 && res.Count < n)
            {
                int k = rng.Next(fits.Count);
                InventoryEquipmentData e = fits[k];
                fits.RemoveAt(k);
                if (slots.Add(e.m_EquipmentType)) res.Add(e.m_Id);
            }
            return res;
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
            MarkChallenges(r);
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
                res.ChallengerData = ChallengerOf(r, r.Current);
                res.FightHistory = r.Fights;
                res.MCChallengeData = McChallenge(r);
            }
            Debug.Log("[HexRevival] [Arena] join: " + (r != null ? r.Info.Wins + " wins, " + r.Info.Loses + " losses, fight " + (r.Current + 1) : "no run"));
            LocalServer.Answer(typeof(CampaignService.JoinCampaignArena), res, handler);
            OfferChallenge(r);
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
                res.ChallengerData = ChallengerOf(r, r.Current);
                res.FightHistory = r.Fights;
                res.MCChallengeData = McChallenge(r);
            }
            LocalServer.Answer(typeof(CampaignService.RefreshArenaInfo), res, handler);
            OfferChallenge(r);
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
                res.ChallengerData = ChallengerOf(r, 0);
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
                // Elite fights (Src.HexCompendium, Src.Video, hka/hex-server): fight 9 in tier 2, fights 12 and 14 in
                // tier 3, 17 and 19 in tier 4, i.e. the 4th fight of tier 2 and the 2nd and 4th of tiers 3 and 4.
                bool elite = !boss && (tier == 2 ? slot == 3 : tier >= 3 && (slot == 1 || slot == 3));
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
            MarkChallenges(r);
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
                Notices.Show(Loc.Get("arena_deck_missing"));
                return;
            }
            Opponents.Opponent o = r.Opponents[r.Current];
            DeckTemplate deck = Opponents.DeckOf(o);
            List<ResourceId> equipment = FightEquipment(r, r.Current, deck);
            if (deck != null && equipment.Count > 0)
            {
                // A copy: the game's template stays as it is. The match engine equips the AI from its deck in the arena.
                deck = (DeckTemplate)typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(deck, null);
                deck.m_EquipmentIDs = equipment;
            }
            bool boss = (r.Current + 1) % PerTier == 0;
            Opponents.Handicap ai = null, me = null;
            // A challenge declined in Hogarth's window is not played (ArenaFight.ChallengeResponse: the client sets it
            // on the fight the mod gave it).
            string ch = r.Challenges[r.Current];
            if (ch != "" && r.Fights[r.Current].ChallengeResponse == "DECLINE") ch = "";
            UID playerDeck = new UID(UID.Type.Deck, r.Info.DeckId);
            if (ch != "")
            {
                Challenge c = Challenges[ch];
                c.Draw(out ai, out me);
                Notices.Show(Loc.Get("arena_goal", c.Name, c.Text));
                if (c.Swap && deck != null)
                {
                    // The Ol' Switcheroo: the player plays the opponent's deck, champion and equipment, the AI the player's.
                    DeckTemplate mine = Decks.ToTemplate(playerDeck);
                    if (mine != null) { playerDeck = Decks.SetTemporary(deck); deck = mine; }
                }
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
            Patches.StartMatch(playerDeck, Opponents.SceneOf(o), deck,
                "arena fight " + (r.Current + 1) + " vs " + o.Name, Opponents.PersonalityOf(o));
        }

        private static bool s_InBattle;
        /// <summary>True while an arena battle is being fought (see Boards).</summary>
        public static bool InBattle { get { return s_InBattle; } }

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
            if (challenge != "" && f.ChallengeResponse == "DECLINE") challenge = "";
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
                // "Remove strikes by Perfecting a Tier, or completing Hogarth's Challenges" (Src.HexCodex, the arena's
                // own text): a tier won without a loss takes off a boss's loss mark (the only marks left by then).
                if (boss && (r.Info.LastTierLoss & (1 << f.FightTier)) == 0 && r.BossLosses > 0) r.BossLosses--;
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
            /// A chest: a card (Src.Video: most chests; the arena's reward cards (Src.Zymmer, see ArenaCards) and cards of
            /// other sets: half each, PLACEHOLDER), sometimes stardust (Src.Video: "Common Stardust x2", "Rare Stardust";
            /// 20% PLACEHOLDER), rarely an arena equipment (Src.Zymmer list, see ArenaEquipment; not seen in 10 chests: 5% PLACEHOLDER).
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
            public string Key, Card;
            public Opponents.Handicap AI, Player;
            /// <summary>Each champion starts with one of these cards in play, drawn for each (The Pacts).</summary>
            public string[] EachOneOf;
            /// <summary>What each champion gets, drawn for each (Shards Aplenty).</summary>
            public Func<Opponents.Handicap> EachDraws;
            /// <summary>The players swap decks, champions and equipment (The Ol' Switcheroo).</summary>
            public bool Swap;
            /// <summary>One of Hogarth's challenges of the game's texts (ArenaPVE_Challenge_Header/Body n), 0 for the ones seen on video.</summary>
            public int GameText;
            /// <summary>In the game's language (the card's name as the game shows it).</summary>
            public string Name { get { return GameText > 0 ? Localization.Get("ArenaPVE_Challenge_Header" + GameText) : Loc.Get("arena_ch_" + Key); } }
            public string Text
            {
                get
                {
                    if (GameText > 0) return Localization.Get("ArenaPVE_Challenge_Body" + GameText).Replace("[b]", "").Replace("[/b]", "");
                    return Loc.Get(Player != null ? "arena_ch_reflection_text" : "arena_ch_opponent_text", CardName(Card));
                }
            }

            /// <summary>What each side starts with in this fight (EachOneOf drawn now).</summary>
            public void Draw(out Opponents.Handicap ai, out Opponents.Handicap me)
            {
                ai = AI; me = Player;
                if (EachDraws != null) { ai = Opponents.Handicap.Add(ai, EachDraws()); me = Opponents.Handicap.Add(me, EachDraws()); }
                if (EachOneOf == null) return;
                ai = Opponents.Handicap.Add(ai, new Opponents.Handicap { InPlay = new List<string> { EachOneOf[UnityEngine.Random.Range(0, EachOneOf.Length)] } });
                me = Opponents.Handicap.Add(me, new Opponents.Handicap { InPlay = new List<string> { EachOneOf[UnityEngine.Random.Range(0, EachOneOf.Length)] } });
            }
        }

        private static string CardName(string name)
        {
            CardTemplate c = Opponents.CardByName(name);
            return c != null && !string.IsNullOrEmpty(c.m_Name) ? c.m_Name : name;
        }

        /// <summary>
        /// The challenges seen (Src.Video: their texts on the battle screen; all with the goal "Win the game"), and those
        /// of Hogarth's challenges in the game's texts (Src.Game: ArenaPVE_Challenge_Header/Body 1-10): Shards Aplenty (2),
        /// Inferno (3), Rage (5), The Ol' Switcheroo (7), The Pacts (8). Not done: Swarm! Swarm! (troops cost -1) and
        /// Charge! (an additional charge) have no ability in the game's data to give; The Primal of Luck and Infernal
        /// Professor play an action for free. Cerebral Fulmination (1) is in both: "each champion" in the game's text,
        /// the opponent only on video; the video's is kept. Each is offered with the game's own Hogarth window
        /// (accept or decline, ArenaFight.ChallengeResponse); a declined challenge is not played and gives nothing.
        /// </summary>
        private static readonly Dictionary<string, Challenge> Challenges = new Dictionary<string, Challenge>
        {
            { "reflection", new Challenge { Key = "reflection", Card = "Locket of Reflection",
                AI = new Opponents.Handicap { InPlay = new List<string> { "Locket of Reflection" } }, Player = new Opponents.Handicap { InPlay = new List<string> { "Locket of Reflection" } } } },
            { "primordial", new Challenge { Key = "primordial", Card = "Primordial Caves",
                AI = new Opponents.Handicap { InPlay = new List<string> { "Primordial Caves" } } } },
            { "gooplasm", new Challenge { Key = "gooplasm", Card = "Gooplasm",
                AI = new Opponents.Handicap { InPlay = new List<string> { "Gooplasm" } } } },
            { "fulmination", new Challenge { Key = "fulmination", Card = "Cerebral Fulmination",
                AI = new Opponents.Handicap { InPlay = new List<string> { "Cerebral Fulmination" } } } },
            { "inferno", new Challenge { Key = "inferno", Card = "Inferno", GameText = 3,
                AI = new Opponents.Handicap { InPlay = new List<string> { "Inferno" } }, Player = new Opponents.Handicap { InPlay = new List<string> { "Inferno" } } } },
            { "pacts", new Challenge { Key = "pacts", GameText = 8, EachOneOf = new[] { "Pact of Life", "Pact of Pain" } } },
            // "each champion will randomly receive [2/0], [0/2], or [1/1] resources at the start of their first turn":
            // read as temporary / permanent resources (as the Resource buff's "1 temporary resource 1 permanent resource").
            { "shards", new Challenge { Key = "shards", GameText = 2, EachDraws = () =>
                {
                    int k = UnityEngine.Random.Range(0, 3);
                    return new Opponents.Handicap { TempResources = k == 0 ? 2 : k == 2 ? 1 : 0, Resources = k == 1 ? 2 : k == 2 ? 1 : 0 };
                } } },
            // "troops gain Rage 1 in all zones": the game's own ability "Troops in all zones have Rage 1." (on no card: the
            // challenge's), given to the opponent's champion; it acts on every troop.
            { "rage", new Challenge { Key = "rage", GameText = 5, AI = new Opponents.Handicap { Abilities = new List<string> { RageAbility } } } },
            { "switcheroo", new Challenge { Key = "switcheroo", GameText = 7, Swap = true } },
        };
        private const string RageAbility = "f56878b1-91c4-7aef-bc3d-25c93b2ea02f";
        private static readonly string[] ChallengeKeys = { "reflection", "primordial", "gooplasm", "fulmination", "inferno", "pacts", "shards", "rage", "switcheroo" };

        private class BuffDef { public ArenaBuff Shown; public Opponents.Handicap Effect; }

        /// <summary>The buffs (Src.HexCompendium: Brawler, Charge, Health, Resource; Src.Video: "Brawler Buff - You start the game with an Arena Brawler in play").</summary>
        private static BuffDef Buff(string key)
        {
            switch (key)
            {
                case "charge": return new BuffDef { Shown = Shown(Loc.Get("arena_buff_charge"), Loc.Get("arena_buff_charge_text")), Effect = new Opponents.Handicap { Charges = 2 } };
                case "health": return new BuffDef { Shown = Shown(Loc.Get("arena_buff_health"), Loc.Get("arena_buff_health_text")), Effect = new Opponents.Handicap { Health = 5 } };
                case "resource": return new BuffDef { Shown = Shown(Loc.Get("arena_buff_resource"), Loc.Get("arena_buff_resource_text")), Effect = new Opponents.Handicap { Resources = 1, TempResources = 1 } };   // "1 temporary resource 1 permanent resource" (Src.HexCodex)
                default: return new BuffDef { Shown = Shown(Loc.Get("arena_buff_brawler"), Loc.Get("arena_buff_brawler_text", CardName("Arena Brawler"))), Effect = new Opponents.Handicap { InPlay = new List<string> { "Arena Brawler" } } };
            }
        }
        private static readonly string[] BuffKeys = { "brawler", "charge", "health", "resource" };

        private static ArenaBuff Shown(string name, string text)
        {
            return new ArenaBuff { Name = name, Description = text, TemplateID = ResourceId.Invalid, ConversationID = ResourceId.Invalid };
        }

        private static List<CardTemplate> s_Cards;
        private static List<InventoryItemData> s_Equipment;

        /// <summary>
        /// The arena's reward cards: the 67 cards Zymmer's notes list as "Frost Ring Arena Rewards" (Src.Zymmer). The
        /// arena's card set (Set01_PvE_Arena) also holds Wheel of Fate, Kickstarter and event cards, so it is only the
        /// fallback (without its shards).
        /// </summary>
        private static List<CardTemplate> ArenaCards()
        {
            if (s_Cards != null) return s_Cards;
            s_Cards = Zymmer.Cards(ZymmerArena).FindAll(c => (c.m_CardType & ECardTypes.Resource) == 0);
            if (s_Cards.Count > 0) return s_Cards;
            ResourceId set = Game.Shared.Resources.BuiltInResources.Set1PvEArena;
            foreach (CardTemplate c in Singleton<TemplateManager>.Instance.Cards.Values)
                if (c != null && c.m_SetId == set && (c.m_CardType & ECardTypes.Resource) == 0) s_Cards.Add(c);
            return s_Cards;
        }

        private const string ZymmerArena = "Frost Ring Arena Rewards";

        /// <summary>
        /// The arena's equipment: the 436 pieces Zymmer's notes list as "Frost Ring Arena Rewards" (Src.Zymmer; the game
        /// data notes them "Arena" or "FRA"). Fallback: the items noted "Arena" by the designers (Src.Game).
        /// </summary>
        private static List<InventoryItemData> ArenaEquipment()
        {
            if (s_Equipment != null) return s_Equipment;
            s_Equipment = Zymmer.Equipment(ZymmerArena);
            if (s_Equipment.Count > 0) return s_Equipment;
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
                case "decks":   // dev: the game's arena decks: champion, health, equipment count, and the challenge cards found
                    {
                        System.Text.StringBuilder sb = new System.Text.StringBuilder();
                        foreach (DeckTemplate d in AiConfig.ArenaDecks())
                        {
                            ChampionTemplate ch = Singleton<TemplateManager>.Instance.GetChampionTemplate(d.m_ChampionId);
                            sb.Append(d.m_DeckName).Append(" | ").Append(ch != null ? ch.m_Name + " " + ch.m_StartingHealth : "?").Append(" | eq ").Append(d.m_EquipmentIDs != null ? d.m_EquipmentIDs.Count : 0).Append('\n');
                        }
                        int shown = 0;
                        foreach (InventoryItemData i in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                        {
                            InventoryEquipmentData e = i as InventoryEquipmentData;
                            if (e == null || shown++ >= 5) continue;
                            sb.Append("eq ").Append(e.m_Name).Append(" from=").Append(e.m_From).Append(" to=").Append(e.m_To).Append(" mods=").Append(e.m_Modifiers).Append('\n');
                        }
                        if (r != null)
                            for (int f = 0; f < r.Fights.Count; f++)
                                sb.Append("fight ").Append(f + 1).Append(" t").Append(r.Fights[f].FightTier).Append(": ").Append(string.Join(", ", FightEquipment(r, f, Opponents.DeckOf(r.Opponents[f])).ConvertAll(x => { InventoryItemData it = Singleton<TemplateManager>.Instance.GetInventoryItem(x); return it != null ? it.m_Name : "?"; }).ToArray())).Append('\n');
                        foreach (string n in new[] { "Inferno", "Pact of Life", "Pact of Pain", "Kismet's Reverie", "Infernal Professor", "Cerebral Fulmination" })
                        {
                            CardTemplate c = Opponents.CardByName(n);
                            sb.Append(n).Append(": ").Append(c != null ? c.m_CardType + (c.m_IsPvE ? " PvE" : "") + " cost " + c.m_ResourceCost : "NOT FOUND").Append('\n');
                            if (c != null) foreach (CardAbilityContainer a in c.m_CardAbilities) { AbilityTemplate at = Singleton<TemplateManager>.Instance.GetAbilityTemplate(a.m_CardAbilityId); if (at != null) sb.Append("   ").Append(at.m_GameText).Append('\n'); }
                        }
                        return sb.ToString();
                    }
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
