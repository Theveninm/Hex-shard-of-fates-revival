using System;
using System.Collections.Generic;
using System.IO;
using Game.Client;
using Game.Client.Network.Tournaments;
using Game.Client.Tournament;
using Game.Shared;
using Game.Shared.Domain;
using Game.Shared.Mechanics;
using Game.Shared.Network.Tournaments;
using Game.Shared.TournamentSystemMkII;
using Game.Shared.Tournaments;
using Reckoning.Game;
using UnityEngine;
using JsonUtils = Game.Shared.Network.HConnect.Utils;

namespace HexRevival
{
    /// <summary>
    /// Battlegrounds (the tournament hub) offline: the "On Demand" gauntlets, played against the AI.
    ///
    /// The server sent the tournament list, ran the drafts and matched the players; the client only showed them.
    /// Here the list holds the three gauntlets the game's texts describe (Src.Game, hex_uidata "gauntlet-*-description"):
    /// - Constructed Gauntlet: your own deck, matches best of 3, until 5 wins or 3 losses;
    /// - Sealed Gauntlet: 6 boosters opened, a 40-card deck, single-game matches, until 5 wins or 3 losses;
    /// - Draft Gauntlet: 3 boosters drafted at a table of 8, matches best of 3, 3 matches.
    /// The opponents are AIs (the other drafters' decks in a draft). Fees, prizes, the set of the boosters and the
    /// draft timer are PLACEHOLDER (not known). The client's own screens are driven through its TournamentManager:
    /// the requests it sends are answered here (Harmony prefixes, see Plugin.cs), the server's pushes are replaced by
    /// calls to the manager's own handlers.
    /// </summary>
    public static class Battlegrounds
    {
        // ------------------------------------------------------------------
        // The gauntlets
        // ------------------------------------------------------------------

        private enum Kind { Constructed, Sealed, Draft }

        private class Gauntlet
        {
            public Kind Kind;
            public ulong Id;               // tournament id: at least 5 digits (the client cuts the first 4), at most long.MaxValue
            public string Name;            // localization key of the name; its description is "gauntlet-<format>-description"
            public ETournamentFormats Format;
            public int GamesPerMatch;      // 1, or 3 (best of 3)
            public int MaxWins, MaxLosses, MaxMatches;
            public int GoldFee;            // PLACEHOLDER
            public int Boosters;           // boosters opened (sealed) or drafted
        }

        private static readonly Gauntlet[] Gauntlets =
        {
            new Gauntlet { Kind = Kind.Constructed, Id = 90000001, Name = "async-constructed", Format = ETournamentFormats.Constructed,
                           GamesPerMatch = 3, MaxWins = 5, MaxLosses = 3, MaxMatches = 7, GoldFee = 2000 },
            new Gauntlet { Kind = Kind.Sealed, Id = 90000002, Name = "async-sealed-pd", Format = ETournamentFormats.Sealed_Deck,
                           GamesPerMatch = 1, MaxWins = 5, MaxLosses = 3, MaxMatches = 7, GoldFee = 12000, Boosters = 6 },
            new Gauntlet { Kind = Kind.Draft, Id = 90000003, Name = "async-draft", Format = ETournamentFormats.Booster_Draft,
                           GamesPerMatch = 3, MaxWins = 3, MaxLosses = 3, MaxMatches = 3, GoldFee = 6000, Boosters = 3 },
        };

        /// <summary>PLACEHOLDER: the limited gauntlets use the last set's boosters (the server chose the product).</summary>
        private const string BoosterName = "Doombringer Booster Pack";
        /// <summary>PLACEHOLDER: basic shards of each kind added to a limited pool (the deck builder needs them).</summary>
        private const int BasicShardsPerKind = 20;
        /// <summary>PLACEHOLDER: time to pick a draft card.</summary>
        private static readonly TimeSpan PickTime = TimeSpan.FromSeconds(60);
        private const int DraftSeats = 8, PackSize = 17;

        private static Gauntlet Find(ulong id) { return Array.Find(Gauntlets, g => g.Id == id); }

        // ------------------------------------------------------------------
        // A run (saved per account: battlegrounds.json)
        // ------------------------------------------------------------------

        private class Run
        {
            public ulong TournamentId;
            public ulong PlayerId;
            public int Wins, Losses;                 // matches
            public int GameWins, GameLosses;         // games of the match in progress
            /// <summary>Each match played, in order: 1 won, 2 lost (the values of the deck builder's RECORD boxes).</summary>
            public List<int> Results = new List<int>();
            public deck_bits Deck;                   // the tournament deck: the pool in its reserves for a limited run
#pragma warning disable 0649   // read back from older saves (JSON); no longer set
            public bool DeckBuilt;                   // the pool was handed to the deck builder
#pragma warning restore 0649
            // Draft: the packs at the table (seat 0 is the player), round 0..2, pick 0..16.
            public List<List<ResourceId>> Packs;
            public List<List<ResourceId>> BotPicks;
            /// <summary>Draft: the AI player at each seat 1..7 (see AiConfig, sealed and draft players).</summary>
            public List<string> Seats;
            /// <summary>Sealed: the AI player and card pool of each match played.</summary>
            public List<SealedOpponent> SealedOpponents = new List<SealedOpponent>();
            public List<card_instance_bits> Picks = new List<card_instance_bits>();
            public List<card_instance_bits> PackShown;
            public ulong PackId;
            public int Round, Pick;
        }

        private class SealedOpponent
        {
            public string Player;
            public List<ResourceId> Pool = new List<ResourceId>();
        }

        private class Store
        {
            public ulong NextInstance = 7000000000000UL;
            public List<Run> Runs = new List<Run>();
            public LadderRecord Ladder = new LadderRecord();
        }

        private static Store s_Store;
        private static string s_StorePath;
        private static string StorePath { get { return Saves.File("battlegrounds.json"); } }

        private static Store Data
        {
            get
            {
                if (s_Store != null && s_StorePath == StorePath) return s_Store;
                s_StorePath = StorePath;
                s_Store = null;
                try { if (File.Exists(s_StorePath)) JsonUtils.DeserializeJson(File.ReadAllBytes(s_StorePath), (Store x) => { s_Store = x; }); }
                catch (Exception e) { Debug.LogError("[HexRevival] [Battlegrounds] read: " + e); }
                if (s_Store == null) s_Store = new Store();
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
            catch (Exception e) { Debug.LogError("[HexRevival] [Battlegrounds] save: " + e); }
        }

        /// <summary>
        /// Whether a deck is a sealed or draft run's (by its id, or the deck builder of such a run): its cards are the
        /// tournament's pool and its basic shards the mod's (AddBasicShards), not the collection's, so the ownership check
        /// (Decks.NotOwned) does not apply. Until 1.2.12 it refused them ("You don't own everything in this deck: 20x Blood
        /// Shard...") and the draft could not be played.
        /// </summary>
        public static bool IsLimitedPool(deck_bits b)
        {
            if (b == null) return false;
            Run r = Data.Runs.Find(x => x.Deck != null && b.Id != 0 && x.Deck.Id == b.Id);
            if (r == null)
            {
                try
                {
                    TournamentInfo cur = Manager != null ? Manager.CurrentTournament : null;
                    EGameState state = GameClient.Instance.StateManager.CurrentGameState;
                    if (cur != null && (state == EGameState.TournamentSideboarding || state == EGameState.TournamentLobby)) r = RunOf(cur.TournamentID);
                }
                catch { }
            }
            Gauntlet g = r != null ? Find(r.TournamentId) : null;
            return g != null && (g.Kind == Kind.Draft || g.Kind == Kind.Sealed);
        }

        private static Run RunOf(ulong tournamentId) { return Data.Runs.Find(r => r.TournamentId == tournamentId); }

        private static ulong NewInstanceId() { return Data.NextInstance++; }

        private static TournamentManager Manager { get { return TournamentManager.m_Instance; } }

        // ------------------------------------------------------------------
        // The hub: the list (GameStateTournament.OnEntry)
        // ------------------------------------------------------------------

        private static bool s_PopupsReady;

        /// <summary>Entering the Battlegrounds: the gauntlets in the list, and the runs in progress registered.</summary>
        public static void OnHubEntered()
        {
            try
            {
                TournamentManager m = Manager;
                if (m == null) { Debug.LogWarning("[HexRevival] [Battlegrounds] no TournamentManager"); return; }
                if (!s_PopupsReady)
                {
                    // Done at a network login only (Main.OnLoginSuccessfull): prize, draft and forfeit popups.
                    UITournamentPopupHandler.Init(m);
                    s_PopupsReady = true;
                }
                List<TournamentDesc> list = new List<TournamentDesc>();
                foreach (Gauntlet g in Gauntlets)
                {
                    TournamentDesc d = Describe(g);
                    m.AddDescriptionToTournamentList(d);
                    list.Add(d);
                    Run r = RunOf(g.Id);
                    if (r != null) Register(g, r);
                }
                // The Dueling Pit (no run: its entry opens its own screen, see DuelingPit).
                TournamentDesc pit = DuelingPit.Describe();
                m.AddDescriptionToTournamentList(pit);
                list.Add(pit);
                m.OnInitDescriptionsMKII(list);
                Debug.Log("[HexRevival] [Battlegrounds] " + list.Count + " gauntlets listed, " + Data.Runs.Count + " in progress");
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Battlegrounds] hub: " + e); }
        }

        private static TournamentDesc Describe(Gauntlet g)
        {
            TournamentDesc d = new TournamentDesc();
            d.TournamentID = g.Id;
            d.RoomType = "";
            d.Description = g.Name;
            d.Style = ETournamentStyle.Asynchronous;
            d.Format = g.Format;
            d.TournamentState = TournamentState.PlayGames.Asynchronous;
            d.TournamentStatus = ETournamentStatus.Asynchronous;
            d.MinPlayers = 1;
            d.MaxPlayers = 0;
            d.MaxRounds = g.MaxMatches;
            d.Players = new string[0];
            d.tournamentFees = new Dictionary<int, TournamentEntryInfo> { { 0, FeeInPlatinum ? new TournamentEntryInfo { platCost = HexRevival.Store.PlatinumOf(g.GoldFee) } : new TournamentEntryInfo { goldCost = HexRevival.Store.GoldOfPlatinum(HexRevival.Store.PlatinumOf(g.GoldFee)) } } };
            d.tournamentRewards = new TournamentRewardCollection();
            InventoryItemData booster = Booster();
            if (booster != null)
                for (int w = 1; w <= g.MaxWins; w++)
                {
                    TournamentRewardGroup grp = new TournamentRewardGroup { basedOnPoints = true, place = w };
                    grp.rewards.Add(new TournamentRewardInfo { prizeResource = booster.m_Id, quantity = PrizeBoosters(w), type = ETournamentRewardType.INVENTORY });
                    d.tournamentRewards.tournamentRewards.Add(grp);
                }
            return d;
        }

        /// <summary>PLACEHOLDER prize: one booster per match won (unopened, to the inventory).</summary>
        private static int PrizeBoosters(int wins) { return wins; }

        private static InventoryItemData Booster()
        {
            foreach (InventoryItemData i in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                if (i is InventoryCardPack && i.m_Name == BoosterName) return i;
            return null;
        }

        private static CardSetTemplate BoosterSet()
        {
            InventoryCardPack p = Booster() as InventoryCardPack;
            CardSetTemplate set;
            return p != null && Singleton<TemplateManager>.Instance.CardSets.TryGetValue(p.m_SetId, out set) ? set : null;
        }

        /// <summary>The run as the client's tournament: info, and the player's record (IsRegisteredInTournament).</summary>
        private static TournamentInfo Register(Gauntlet g, Run r)
        {
            TournamentManager m = Manager;
            TournamentInfo info = new TournamentInfo();
            info.TournamentID = g.Id;
            info.Format = g.Format;
            info.Style = ETournamentStyle.Asynchronous;
            info.TournamentStatus = ETournamentStatus.Asynchronous;
            info.State = TournamentState.PlayGames.Asynchronous;
            info.waitingOnInitialFlush = false;
            info.Description = g.Name;
            info.NumberOfRounds = g.MaxMatches;
            info.tournamentDescription = m.GetTournamentDesc(g.Id) ?? Describe(g);
            info.Games = new List<TournamentGameInfo>();
            TournamentPlayerInfo me = new TournamentPlayerInfo
            {
                PlayerUID = TournamentManager.m_ReckID,
                PlayerID = r.PlayerId,
                Name = Saves.Current != null ? Saves.Current.Name : "",
                State = PlayerStates.Deckbuilding,
                Wins = r.Wins,
                Losses = r.Losses,
            };
            info.Players = new List<TournamentPlayerInfo> { me };
            m.AsynchPlayerOverrides[g.Id] = me;
            m.OnTournamentInfo(info);
            return info;
        }

        // ------------------------------------------------------------------
        // Entering (TestTournamentEntry, EnterTournament)
        // ------------------------------------------------------------------

        public static void TestEntry(ulong id, Delegate handler)
        {
            TestTournamentEntryResponse r = LocalServer.Ok<TestTournamentEntryResponse>();
            Gauntlet g = Find(id);
            r.success = g != null;
            r.ErrorMessage = "";
            r.qualifyingEntryGroups = new List<int> { 0 };
            if (g == null) r.Error = ETestTournamentEntryError.DoesNotMeetPrereqsError;
            LocalServer.Answer(typeof(TournamentsService.TestTournamentEntry), r, handler);
        }

        public static void Enter(ulong id, UID deckId, Delegate handler)
        {
            EnterTournamentResponse res = LocalServer.Ok<EnterTournamentResponse>();
            res.TournamentID = id;
            res.ErrorMessage = "";
            Gauntlet g = Find(id);
            Run r = g != null ? RunOf(id) : null;
            if (g == null || Saves.Current == null) res.Error = EEnterTournamentError.InvalidTouranmentError;
            else if (r == null)
            {
                bool plat = FeeInPlatinum;
                long fee = plat ? HexRevival.Store.PlatinumOf(g.GoldFee) : HexRevival.Store.GoldOfPlatinum(HexRevival.Store.PlatinumOf(g.GoldFee));
                if ((plat ? Saves.Current.Platinum : Saves.Current.Gold) < fee) res.Error = EEnterTournamentError.DoesNotMeetPrereqsError;
                else
                {
                    if (plat) Saves.Current.Platinum -= fee; else Saves.Current.Gold -= fee;
                    Saves.SaveCurrent();
                    ShowBalance();
                    r = Start(g, deckId);
                }
            }
            if (r != null)
            {
                res.TournamentDeckInfo = r.Deck;
                Register(g, r);
            }
            Debug.Log("[HexRevival] [Battlegrounds] enter " + id + " -> " + res.Error);
            LocalServer.Answer(typeof(TournamentsService.EnterTournament), res, handler);
        }

        /// <summary>Accounts without "platinum prices in gold" pay the entries in platinum (the gold fees are platinum x 10); the others that platinum at the account's gold per platinum.</summary>
        private static bool FeeInPlatinum { get { return Saves.Current != null && !Saves.Current.GoldPrices; } }

        private static void ShowBalance()
        {
            try { GameClient.Instance.PlayerProfile.UpdateBalance((int)Saves.Current.Gold, (int)Saves.Current.Platinum); } catch { }
        }

        private static Run Start(Gauntlet g, UID deckId)
        {
            Run r = new Run { TournamentId = g.Id, PlayerId = NewInstanceId() };
            r.Deck = new deck_bits();
            r.Deck.Id = NewInstanceId();
            r.Deck.DeckName = Localization.Get(g.Name);
            r.Deck.CardsInDeck = new List<card_instance_bits>();
            r.Deck.CardsInSideboard = new List<card_instance_bits>();
            switch (g.Kind)
            {
                case Kind.Constructed:
                    deck_bits own = Decks.Bits(deckId.GetInstanceId());
                    if (own != null)
                    {
                        own.Id = r.Deck.Id;
                        r.Deck = own;
                    }
                    break;
                case Kind.Sealed:
                    // "Entering this tournament will open 6 booster packs [...] You will get to keep all the cards you opened."
                    CardSetTemplate set = BoosterSet();
                    for (int i = 0; i < g.Boosters; i++)
                        foreach (CardTemplate t in Progression.OpenPack(set)) r.Deck.CardsInSideboard.Add(Instance(t.m_Id));
                    Keep(r.Deck.CardsInSideboard);
                    AddBasicShards(r.Deck);
                    break;
                case Kind.Draft:
                    r.Packs = new List<List<ResourceId>>();
                    r.BotPicks = new List<List<ResourceId>>();
                    for (int s = 0; s < DraftSeats; s++) r.BotPicks.Add(new List<ResourceId>());
                    r.Seats = DrawSeats();
                    OpenDraftRound(r);
                    break;
            }
            Data.Runs.RemoveAll(x => x.TournamentId == g.Id);
            Data.Runs.Add(r);
            Save();
            Debug.Log("[HexRevival] [Battlegrounds] started " + g.Name + " (" + r.Deck.CardsInSideboard.Count + " cards in the pool)");
            return r;
        }

        private static card_instance_bits Instance(ResourceId template)
        {
            return new card_instance_bits { Id = NewInstanceId(), TemplateID = template };
        }

        /// <summary>The opened or drafted cards go to the collection too ("you get to keep all the cards").</summary>
        private static void Keep(IEnumerable<card_instance_bits> cards)
        {
            foreach (card_instance_bits c in cards) Collection.Grant(c.TemplateID, 1);
        }

        private static List<CardTemplate> s_BasicShards;

        /// <summary>One basic shard of each kind (Blood, Diamond, Ruby, Sapphire, Wild).</summary>
        private static List<CardTemplate> BasicShards()
        {
            if (s_BasicShards != null) return s_BasicShards;
            s_BasicShards = new List<CardTemplate>();
            ECardShards seen = ECardShards.Unknown;
            List<CardTemplate> all = new List<CardTemplate>(Singleton<TemplateManager>.Instance.Cards.Values);
            all.Sort((a, b) => string.CompareOrdinal(a.m_Name, b.m_Name));
            foreach (CardTemplate t in all)
            {
                if (t == null || !t.IsBasicResource()) continue;
                ECardShards s = t.GetThresholdsProvided();
                if (s == ECardShards.Unknown || (seen & s) != 0 || !t.m_Name.EndsWith(" Shard")) continue;
                seen |= s;
                s_BasicShards.Add(t);
            }
            return s_BasicShards;
        }

        private static void AddBasicShards(deck_bits deck)
        {
            foreach (CardTemplate t in BasicShards())
                for (int i = 0; i < BasicShardsPerKind; i++) deck.CardsInSideboard.Add(Instance(t.m_Id));
        }

        // ------------------------------------------------------------------
        // Into the run (TryReconnectionToDisconnectedTournament: the lobby of an asynchronous tournament)
        // ------------------------------------------------------------------

        /// <summary>The client goes to the run's lobby: the draft, or the deck builder.</summary>
        public static void Resume(ulong id)
        {
            LocalServer.Later(() =>
            {
                try
                {
                    Gauntlet g = Find(id);
                    Run r = RunOf(id);
                    if (g == null || r == null) { Debug.LogWarning("[HexRevival] [Battlegrounds] resume " + id + ": no run"); return; }
                    TournamentInfo info = Register(g, r);
                    if (g.Kind == Kind.Draft && r.Round < g.Boosters)
                    {
                        Manager.OnDraftStartingEvent(new DraftStartingEventArgs { TournamentID = id, Cards = new List<card_instance_bits>(r.Picks) });
                        foreach (card_instance_bits c in r.Picks) Manager.OnTournamentReceiveDraftCardEvent(id, c);
                        SendPack(r);
                        return;
                    }
                    Manager.CurrentlyInMatch = r.GameWins + r.GameLosses > 0;
                    Manager.OnDeckConstructionStartedEvent(new DeckConstructionStartedEventArgs
                    {
                        TournamentID = id,
                        TournamentInfo = info,
                        my_Deck = r.Deck,
                        timeForSideboarding = 0,
                        PlayerID = r.PlayerId,
                    });
                }
                catch (Exception e) { Debug.LogError("[HexRevival] [Battlegrounds] resume: " + e); }
            });
        }

        /// <summary>SaveTournamentDeck: the deck the player built (kept for the next matches and a restart).</summary>
        public static void SaveDeck(deck_bits deck, ulong id, Delegate handler)
        {
            Run r = RunOf(id);
            if (r != null && deck != null)
            {
                deck.Id = r.Deck.Id;
                r.Deck = deck;
                Save();
            }
            SaveTournamentDeckResponse res = LocalServer.Ok<SaveTournamentDeckResponse>();
            res.Success = r != null;
            res.ErrorMessage = "";
            LocalServer.Answer(typeof(TournamentsService.SaveTournamentDeck), res, handler);
        }

        /// <summary>The AI configuration mode of a Battlegrounds event (see AiConfig): gauntlets by format, else the ladder.</summary>
        public static string ModeOf(ulong tournamentId)
        {
            foreach (Gauntlet g in Gauntlets)
                if (g.Id == tournamentId) return g.Kind == Kind.Constructed ? AiConfig.Gauntlet : AiConfig.Limited;
            return AiConfig.Ladder;
        }

        /// <summary>The run's deck, when a match asks for it (Decks.GetDeckDetails).</summary>
        public static deck_bits DeckFor(UID deckId)
        {
            if (s_Battle == null) return null;
            Run r = RunOf(s_Battle.TournamentId);
            if (r == null || r.Deck == null || r.Deck.Id != deckId.GetInstanceId()) return null;
            if (!r.Deck.PVPChampionId.IsValid) r.Deck.PVPChampionId = new ResourceId(GameConfig.DefaultChampionTemplateId);
            return r.Deck;
        }

        // ------------------------------------------------------------------
        // Draft: a table of 8, the 7 others are bots
        // ------------------------------------------------------------------

        private static void OpenDraftRound(Run r)
        {
            CardSetTemplate set = BoosterSet();
            r.Packs.Clear();
            for (int s = 0; s < DraftSeats; s++)
            {
                List<ResourceId> pack = new List<ResourceId>();
                foreach (CardTemplate t in Progression.OpenPack(set)) pack.Add(t.m_Id);
                r.Packs.Add(pack);
            }
            r.Pick = 0;
        }

        private static void SendPack(Run r)
        {
            r.PackShown = new List<card_instance_bits>();
            foreach (ResourceId t in r.Packs[0]) r.PackShown.Add(Instance(t));
            r.PackId = NewInstanceId();   // a new id for every pick: the client ignores a second pick from the same pack id
            Save();
            Manager.OnReceiveDraftPackEvent(new ReceiveDraftPackEventArgs
            {
                TournamentID = r.TournamentId,
                PackID = r.PackId,
                TicksForSelection = (int)PickTime.Ticks,
                Cards = new List<card_instance_bits>(r.PackShown),
            });
        }

        /// <summary>RequestSelectDraftCard: the player's pick, the bots' picks, the packs passed on.</summary>
        public static void SelectDraftCard(ulong id, ulong packId, ulong cardId)
        {
            LocalServer.Later(() =>
            {
                try
                {
                    Gauntlet g = Find(id);
                    Run r = RunOf(id);
                    if (g == null || r == null || r.PackShown == null || packId != r.PackId) return;
                    card_instance_bits card = r.PackShown.Find(c => c.Id == cardId) ?? r.PackShown[0];
                    Manager.OnSelectDraftCardRequestResponse(LocalServer.Ok<DraftSelectCardResponse>());
                    Manager.OnTournamentReceiveDraftCardEvent(id, card);
                    r.Picks.Add(card);
                    r.Packs[0].Remove(card.TemplateID);
                    r.PackShown = null;
                    for (int s = 1; s < DraftSeats; s++) BotPick(r, s);
                    // Passed left, right, then left again.
                    int dir = r.Round % 2 == 0 ? 1 : DraftSeats - 1;
                    List<List<ResourceId>> passed = new List<List<ResourceId>>(r.Packs);
                    for (int s = 0; s < DraftSeats; s++) r.Packs[(s + dir) % DraftSeats] = passed[s];
                    r.Pick++;
                    if (r.Pick >= PackSize)
                    {
                        r.Round++;
                        if (r.Round >= g.Boosters) { EndDraft(g, r); return; }
                        OpenDraftRound(r);
                    }
                    SendPack(r);
                }
                catch (Exception e) { Debug.LogError("[HexRevival] [Battlegrounds] draft pick: " + e); }
            });
        }

        /// <summary>A bot's pick: the best card (the game AI's evaluation), favoring the colors it already drafts.</summary>
        private static void BotPick(Run r, int seat)
        {
            List<ResourceId> pack = r.Packs[seat];
            if (pack.Count == 0) return;
            ECardShards colors = MainColors(r.BotPicks[seat]);
            ResourceId best = pack[0];
            float bestScore = float.MinValue;
            foreach (ResourceId id in pack)
            {
                CardTemplate t = Template(id);
                if (t == null) continue;
                float score = Value(id) + UnityEngine.Random.value * 2;
                ECardShards c = Colors(t);
                if (colors != ECardShards.Unknown && c != ECardShards.Unknown) score += (c & ~colors) == 0 ? 2 : -2;
                if (score > bestScore) { bestScore = score; best = id; }
            }
            pack.Remove(best);
            r.BotPicks[seat].Add(best);
        }

        private static void EndDraft(Gauntlet g, Run r)
        {
            r.Packs = null;
            r.PackShown = null;
            r.Deck.CardsInSideboard = new List<card_instance_bits>(r.Picks);
            Keep(r.Picks);
            AddBasicShards(r.Deck);
            Save();
            Debug.Log("[HexRevival] [Battlegrounds] draft over: " + r.Picks.Count + " cards");
            Manager.OnDeckConstructionStartedEvent(new DeckConstructionStartedEventArgs
            {
                TournamentID = r.TournamentId,
                TournamentInfo = Manager.GetTournamentInfo(r.TournamentId),
                my_Deck = r.Deck,
                timeForSideboarding = 0,
                PlayerID = r.PlayerId,
            });
        }

        // ------------------------------------------------------------------
        // Matches (GameEntrance "Find Match")
        // ------------------------------------------------------------------

        private class Battle { public ulong TournamentId; }
        private static Battle s_Battle;
        private static Run s_Finished;

        public static void GameEntrance(ulong id, deck_bits deck, bool searching, Delegate handler)
        {
            GameEntranceResponse res = LocalServer.Ok<GameEntranceResponse>();
            res.success = true;
            res.ErrorMessage = "";
            LocalServer.Answer(typeof(TournamentsService.GameEntrance), res, handler);
            Gauntlet g = Find(id);
            Run r = RunOf(id);
            if (!searching || g == null || r == null) return;
            if (deck != null) { deck.Id = r.Deck.Id; r.Deck = deck; Save(); }
            DeckTemplate ai = Opponent(g, r);
            s_Battle = new Battle { TournamentId = id };
            int match = r.Wins + r.Losses + 1, game = r.GameWins + r.GameLosses + 1;
            Patches.PendingEncounterSetup = e =>
            {
                e.SessionFlags |= ESessionFlags.IsTournament;
                if (g.Kind == Kind.Sealed) e.SessionFlags |= ESessionFlags.IsSealed;
                if (g.Kind == Kind.Draft) e.SessionFlags |= ESessionFlags.IsDraft;
                e.TournamentID = id;
                e.TournamentRound = match;
                e.SeriesFormat = g.Kind == Kind.Sealed ? SessionStateEncounterData.SeriesType.ASYNC_SEALED
                    : g.Kind == Kind.Draft ? SessionStateEncounterData.SeriesType.ASYNC_DRAFT : SessionStateEncounterData.SeriesType.CONSTRUCTED;
                e.SeriesMaxGames = 1;   // the mod plays the best of 3 itself, one game at a time (back to the deck builder in between)
            };
            EDeckPersonality personality = Opponents.PersonalityOf(s_LastOpponent);
            ResourceId scene = Opponents.SceneOf(s_LastOpponent);
            LocalServer.Later(() => Patches.StartMatch(new UID(UID.Type.Deck, r.Deck.Id), scene, ai,
                g.Name + " match " + match + " game " + game, personality));
        }

        /// <summary>The AI's deck: a prebuilt deck (constructed), a sealed pool or another drafter's picks, built into 40 cards.</summary>
        private static Opponents.Opponent s_LastOpponent;

        private static DeckTemplate Opponent(Gauntlet g, Run r)
        {
            if (g.Kind == Kind.Constructed)
            {
                // The constructed gauntlet's configuration (see AiConfig): a deck with its AI level and modifiers.
                Opponents.Opponent o = Opponents.Pick(Opponents.Gauntlet, r.Wins);
                Opponents.SetNext(o);
                s_LastOpponent = o;
                DeckTemplate own = Opponents.DeckOf(o);
                if (own != null) return own;
                List<DeckTemplate> decks = Decks.PrebuiltPvp();
                return decks.Count > 0 ? decks[UnityEngine.Random.Range(0, decks.Count)] : null;
            }
            // Sealed and draft: an AI player of the configuration (its level and modifiers), with the deck built from its
            // pool: the drafter at the next seat, or a new sealed pool.
            string player;
            List<ResourceId> pool;
            int match = r.Wins + r.Losses;
            if (g.Kind == Kind.Draft && r.BotPicks != null)
            {
                if (r.Seats == null) { r.Seats = DrawSeats(); Save(); }
                int seat = 1 + match % (DraftSeats - 1);
                player = r.Seats[seat - 1];
                pool = r.BotPicks[seat];
            }
            else
            {
                if (r.SealedOpponents == null) r.SealedOpponents = new List<SealedOpponent>();
                if (r.SealedOpponents.Count <= match)
                {
                    AiConfig.AiPlayer p = PickPlayer();
                    SealedOpponent so = new SealedOpponent { Player = p != null ? p.Name : "AI" };
                    for (int i = 0; i < 6; i++) foreach (CardTemplate t in Progression.OpenPack(BoosterSet())) so.Pool.Add(t.m_Id);
                    r.SealedOpponents.Add(so);
                    Save();
                }
                player = r.SealedOpponents[match].Player;
                pool = r.SealedOpponents[match].Pool;
            }
            AiConfig.AiPlayer ap = AiConfig.Get(AiConfig.Limited).Players.Find(x => x.Name == player);
            Opponents.Opponent op = ap != null ? AiConfig.ToOpponent(ap) : new Opponents.Opponent { Name = player };
            Opponents.SetNext(op);
            s_LastOpponent = op;
            return LimitedDeck(pool, player);
        }

        /// <summary>A sealed / draft AI player of the configuration, by frequency (null if none).</summary>
        private static AiConfig.AiPlayer PickPlayer()
        {
            List<AiConfig.AiPlayer> all = AiConfig.Get(AiConfig.Limited).Players.FindAll(p => p.Weight > 0);
            int total = 0;
            foreach (AiConfig.AiPlayer p in all) total += p.Weight;
            if (total == 0) return null;
            int x = UnityEngine.Random.Range(0, total);
            foreach (AiConfig.AiPlayer p in all) { x -= p.Weight; if (x < 0) return p; }
            return all[all.Count - 1];
        }

        /// <summary>The 7 AI drafters of a table: the configured players by frequency, each once while there are enough.</summary>
        private static List<string> DrawSeats()
        {
            List<string> seats = new List<string>();
            List<AiConfig.AiPlayer> left = AiConfig.Get(AiConfig.Limited).Players.FindAll(p => p.Weight > 0);
            for (int i = 0; i < DraftSeats - 1; i++)
            {
                if (left.Count == 0) left = AiConfig.Get(AiConfig.Limited).Players.FindAll(p => p.Weight > 0);
                if (left.Count == 0) { seats.Add("AI " + (i + 1)); continue; }
                int total = 0;
                foreach (AiConfig.AiPlayer p in left) total += p.Weight;
                int x = UnityEngine.Random.Range(0, total);
                AiConfig.AiPlayer chosen = left[left.Count - 1];
                foreach (AiConfig.AiPlayer p in left) { x -= p.Weight; if (x < 0) { chosen = p; break; } }
                seats.Add(chosen.Name);
                left.Remove(chosen);
            }
            return seats;
        }

        /// <summary>An AI opponent of a sealed / draft run in progress, and the deck it plays (built from its pool).</summary>
        public class LimitedOpponent
        {
            public string Event, Player;
            public DeckTemplate Deck;
            public bool Faced, Next, Drafting;
        }

        /// <summary>The AI opponents of the sealed / draft runs in progress (for the AI window): faced, next, and the others.</summary>
        public static List<LimitedOpponent> LimitedOpponents()
        {
            List<LimitedOpponent> res = new List<LimitedOpponent>();
            foreach (Run r in Data.Runs)
            {
                Gauntlet g = Find(r.TournamentId);
                if (g == null || g.Kind == Kind.Constructed) continue;
                int match = r.Wins + r.Losses;
                string name = Localization.Get(g.Name);
                if (g.Kind == Kind.Draft && r.BotPicks != null)
                {
                    if (r.Seats == null) { r.Seats = DrawSeats(); Save(); }
                    for (int seat = 1; seat < DraftSeats; seat++)
                    {
                        int order = seat - 1;   // match index of this seat (one match per seat, in order)
                        res.Add(new LimitedOpponent
                        {
                            Event = name, Player = r.Seats[seat - 1], Deck = LimitedDeck(r.BotPicks[seat], r.Seats[seat - 1]),
                            Faced = order < match, Next = order == match, Drafting = r.Packs != null,
                        });
                    }
                }
                else if (r.SealedOpponents != null)
                    for (int i = 0; i < r.SealedOpponents.Count; i++)
                        res.Add(new LimitedOpponent { Event = name, Player = r.SealedOpponents[i].Player, Deck = LimitedDeck(r.SealedOpponents[i].Pool, r.SealedOpponents[i].Player), Faced = i < match, Next = i == match });
            }
            return res;
        }

        /// <summary>A 40-card deck from a pool: its two main colors, its 23 best cards (the game AI's evaluation), 17 basic shards.</summary>
        private static DeckTemplate LimitedDeck(List<ResourceId> pool, string player = null)
        {
            ECardShards colors = MainColors(pool);
            List<CardTemplate> cards = new List<CardTemplate>();
            foreach (ResourceId id in pool)
            {
                CardTemplate t = Template(id);
                if (t == null || (t.m_CardType & ECardTypes.Resource) != 0) continue;
                ECardShards c = Colors(t);
                if (c == ECardShards.Unknown || (c & ~colors) == 0) cards.Add(t);
            }
            cards.Sort((a, b) => Value(b.m_Id).CompareTo(Value(a.m_Id)));
            if (cards.Count > 23) cards.RemoveRange(23, cards.Count - 23);
            DeckTemplate d = new DeckTemplate();
            d.m_Id = new ResourceId(Guid.NewGuid());
            d.m_DeckName = string.IsNullOrEmpty(player) ? "Gauntlet opponent" : player;
            // The same champion for the same pool (the deck shown in the AI window is the one played).
            d.m_ChampionId = RandomChampion(pool.Count + (pool.Count > 0 ? pool[0].GetHashCode() : 0) + (player ?? "").GetHashCode());
            Dictionary<ResourceId, TemplateCardStack> stacks = new Dictionary<ResourceId, TemplateCardStack>();
            Action<ResourceId, int> add = (id, n) =>
            {
                TemplateCardStack st;
                if (!stacks.TryGetValue(id, out st)) { st = new TemplateCardStack { m_idTemplate = id, m_Count = 0 }; stacks[id] = st; d.m_DeckResources.Add(st); }
                st.m_Count += n;
            };
            foreach (CardTemplate t in cards) add(t.m_Id, 1);
            // 17 shards (40 cards with 23 others), split between the two colors.
            List<CardTemplate> shards = BasicShards().FindAll(s => (s.GetThresholdsProvided() & colors) != 0);
            int total = 40 - cards.Count;
            for (int i = 0; i < shards.Count; i++) add(shards[i].m_Id, total / shards.Count + (i < total % shards.Count ? 1 : 0));
            return d;
        }

        private static readonly Dictionary<ResourceId, float> s_Values = new Dictionary<ResourceId, float>();

        /// <summary>
        /// A card's worth for the bots' picks and the AI's limited decks: the game AI's own card evaluation
        /// (AITactical.GetValueFor, abilities, attack, defense, cost; used by the game only for a debug overlay)
        /// plus the rarity. Cached.
        /// </summary>
        private static float Value(ResourceId id)
        {
            float v;
            if (s_Values.TryGetValue(id, out v)) return v;
            CardTemplate t = Template(id);
            // Rarity weighs as much: the evaluator, made for a board in play, underrates the actions (removal...).
            int rarity = t == null ? 0 : Mathf.Clamp((int)t.m_CardRarity - (int)ERarity.Common, 0, 3);
            v = rarity * 2.5f;
            try { v += 0.6f * Mathf.Clamp((float)Game.Shared.AI.AITactical.GetValueFor(new CardRepresentation(id)), 0f, 12f); }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Battlegrounds] card value " + id + ": " + e.Message); }
            s_Values[id] = v;
            return v;
        }

        private static ResourceId RandomChampion(int seed)
        {
            List<DeckTemplate> decks = Decks.PrebuiltPvp();
            return decks.Count > 0 ? decks[new System.Random(seed).Next(decks.Count)].m_ChampionId : new ResourceId(GameConfig.DefaultChampionTemplateId);
        }

        private static CardTemplate Template(ResourceId id)
        {
            CardTemplate t;
            return Singleton<TemplateManager>.Instance.Cards.TryGetValue(id, out t) ? t : null;
        }

        private static ECardShards Colors(CardTemplate t)
        {
            ECardShards c = ECardShards.Unknown;
            if (t.m_Threshold != null) foreach (CardThreshold th in t.m_Threshold) c |= th.m_ColorFlags;
            return c & ~ECardShards.Colorless;
        }

        /// <summary>The two colors with the most cards of a pool (weighted by rarity).</summary>
        private static ECardShards MainColors(List<ResourceId> pool)
        {
            Dictionary<ECardShards, int> weight = new Dictionary<ECardShards, int>();
            foreach (ResourceId id in pool)
            {
                CardTemplate t = Template(id);
                if (t == null) continue;
                foreach (ECardShards one in new[] { ECardShards.Blood, ECardShards.Ruby, ECardShards.Sapphire, ECardShards.Wild, ECardShards.Diamond })
                    if ((Colors(t) & one) != 0) { int w; weight.TryGetValue(one, out w); weight[one] = w + 1 + (int)t.m_CardRarity; }
            }
            List<KeyValuePair<ECardShards, int>> sorted = new List<KeyValuePair<ECardShards, int>>(weight);
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
            ECardShards res = ECardShards.Unknown;
            for (int i = 0; i < sorted.Count && i < 2; i++) res |= sorted[i].Key;
            return res;
        }

        /// <summary>End of a match. Returns true if it was a Battlegrounds game.</summary>
        public static bool OnGameEnded(bool won)
        {
            Battle b = s_Battle;
            if (b == null) return false;
            s_Battle = null;
            Gauntlet g = Find(b.TournamentId);
            Run r = RunOf(b.TournamentId);
            if (g == null || r == null) return true;
            if (won) r.GameWins++; else r.GameLosses++;
            int needed = g.GamesPerMatch / 2 + 1;
            string text;
            if (r.GameWins >= needed || r.GameLosses >= needed)
            {
                bool matchWon = r.GameWins >= needed;
                if (matchWon) r.Wins++; else r.Losses++;
                if (r.Results == null) r.Results = new List<int>();
                r.Results.Add(matchWon ? 1 : 2);
                text = Loc.Get(matchWon ? "bg_match_won" : "bg_match_lost", r.Wins, r.Losses);
                r.GameWins = r.GameLosses = 0;
            }
            else text = Loc.Get(won ? "bg_game_won" : "bg_game_lost", r.GameWins, r.GameLosses);
            TournamentPlayerInfo me;
            if (Manager.AsynchPlayerOverrides.TryGetValue(g.Id, out me)) { me.Wins = r.Wins; me.Losses = r.Losses; }
            Manager.CurrentlyInMatch = r.GameWins + r.GameLosses > 0;
            Debug.Log("[HexRevival] [Battlegrounds] " + g.Name + ": " + text);
            Notices.Show(text);
            s_RefreshRecord = true;
            if (r.Wins >= g.MaxWins || r.Losses >= g.MaxLosses || r.Wins + r.Losses >= g.MaxMatches) s_Finished = r;
            Save();
            return true;
        }

        /// <summary>
        /// The draft's RECORD boxes: the client fills them with every win first, then the losses, so a loss then a win
        /// showed as won, lost. Here they follow the order of the matches (Run.Results). Runs saved before 1.2.13 have no
        /// order: left to the client. Returns the three boxes (1 won, 2 lost, 0 empty), or null.
        /// </summary>
        private static int[] RecordBoxes()
        {
            Run r = CurrentRun();
            if (r == null || r.Results == null || r.Results.Count != r.Wins + r.Losses) return null;
            int[] box = new int[3];
            for (int i = 0; i < box.Length && i < r.Results.Count; i++) box[i] = r.Results[i];
            return box;
        }

        private static Run CurrentRun()
        {
            TournamentInfo cur = Manager != null ? Manager.CurrentTournament : null;
            return cur != null ? RunOf(cur.TournamentID) : null;
        }

        /// <summary>Postfix of UIDeckBuilderVM.SetupTournamentUI and OnAsynchPlayerUpdated.</summary>
        public static void ShowRecord(DeckBuilder.UITournamentContext t)
        {
            int[] box = RecordBoxes();
            if (t == null || box == null) return;
            t.ResultOne = box[0]; t.ResultTwo = box[1]; t.ResultThree = box[2];
        }

        /// <summary>
        /// Postfix of UITournamentDeckEditorViewModel.Initialize and OnAsynchPlayerUpdated: the deck screen of the
        /// asynchronous gauntlets (draft, sealed, constructed).
        /// </summary>
        public static void ShowRecord(UITournamentDeckEditorContext t)
        {
            int[] box = RecordBoxes();
            if (t == null || box == null) return;
            t.ResultOne = box[0]; t.ResultTwo = box[1]; t.ResultThree = box[2];
        }

        /// <summary>
        /// Back from a game, the gauntlet's deck screen is the one left for the battle: it read the record when it opened
        /// and the server's update (OnAsynchPlayerUpdated) never came, so RECORD / WINS / LOSSES stayed as they were until
        /// the screen was opened again (2026-10-31). The update is given to it here.
        /// </summary>
        private static void RefreshDeckScreen()
        {
            Run r = CurrentRun();
            TournamentPlayerInfo me;
            if (r == null || !Manager.AsynchPlayerOverrides.TryGetValue(r.TournamentId, out me)) return;
            me.Wins = r.Wins;
            me.Losses = r.Losses;
            UITournamentDeckEditorViewModel vm = UnityEngine.Object.FindObjectOfType<UITournamentDeckEditorViewModel>();
            if (vm == null) return;
            System.Reflection.MethodInfo m = typeof(UITournamentDeckEditorViewModel).GetMethod("OnAsynchPlayerUpdated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (m != null) m.Invoke(vm, new object[] { r.TournamentId, me });   // the postfix puts the boxes in order
        }

        private static bool s_RefreshRecord;

        /// <summary>
        /// The score of the match in progress of a best-of-3 gauntlet ("Match 2 - games 1-0"), shown next to RECORD: the
        /// boxes only count whole matches, and the game score was only a notice after each game (2026-10-31). Null when
        /// there is nothing to show.
        /// </summary>
        public static string MatchScore()
        {
            try
            {
                if (GameClient.Instance.StateManager.CurrentGameState != EGameState.TournamentSideboarding) return null;
                Run r = CurrentRun();
                Gauntlet g = r != null ? Find(r.TournamentId) : null;
                if (g == null || g.GamesPerMatch < 2 || (g.Kind == Kind.Draft && r.Round < g.Boosters)) return null;
                return Loc.Get("bg_score", r.Wins + r.Losses + 1, r.GameWins, r.GameLosses);
            }
            catch { return null; }
        }

        /// <summary>Called every frame: the prizes of a finished run, once back from the battle.</summary>
        public static void Tick()
        {
            if (s_RefreshRecord && s_Battle == null)
            {
                try
                {
                    if (GameClient.Instance.StateManager.CurrentGameState == EGameState.TournamentSideboarding
                        && UnityEngine.Object.FindObjectOfType<UITournamentDeckEditorViewModel>() != null)
                    {
                        s_RefreshRecord = false;
                        RefreshDeckScreen();
                    }
                }
                catch (Exception e) { s_RefreshRecord = false; Debug.LogError("[HexRevival] [Battlegrounds] record: " + e); }
            }
            if (s_Finished == null || s_Battle != null) return;
            EGameState state = GameClient.Instance.StateManager.CurrentGameState;
            if (state != EGameState.TournamentSideboarding && state != EGameState.TournamentLobby && state != EGameState.Tournament) return;
            Run r = s_Finished;
            s_Finished = null;
            Finish(r);
        }

        /// <summary>The run is over (or dropped): prizes, and the run is removed.</summary>
        private static void Finish(Run r)
        {
            Gauntlet g = Find(r.TournamentId);
            Data.Runs.Remove(r);
            Save();
            // The prize boosters unopened, in the inventory (Open Pack in the store); until 1.2.12 they were opened at
            // once and the prize window listed their cards, read as the drafted cards (2026-10-31). The drafted and
            // sealed cards are the player's since the draft / the entry (Keep), not shown as a prize.
            List<ResourceId> cards = new List<ResourceId>();
            List<ResourceId> items = new List<ResourceId>();
            InventoryItemData booster = Booster();
            for (int i = 0; booster != null && i < PrizeBoosters(r.Wins); i++)
            {
                inventory_bits b = Inventory.GrantSilently(booster.m_Id);
                if (b == null) continue;
                try { GameClient.Instance.PlayerProfile.AddInventoryItem(b); } catch (Exception e) { Debug.LogWarning("[HexRevival] [Battlegrounds] prize: " + e.Message); }
                items.Add(booster.m_Id);
            }
            Debug.Log("[HexRevival] [Battlegrounds] " + (g != null ? g.Name : "?") + " over: " + r.Wins + "-" + r.Losses + ", " + items.Count + " booster(s)");
            // Sleeves (Src.HexCodex): "Awarded for getting 3 wins in Kismet's Draft events"; "Obtained through Gauntlet
            // Tournaments" for a constructed gauntlet (3 wins as well: PLACEHOLDER). One not owned yet.
            if (g != null && r.Wins >= 3 && (g.Kind == Kind.Draft || g.Kind == Kind.Constructed))
            {
                InventoryDeckSleeve sl = Sleeves.Unowned(Sleeves.Matching(g.Kind == Kind.Draft ? "^Awarded for getting 3 wins in Kismet's Draft" : "^Obtained through Gauntlet Tournaments"));
                if (sl != null && Sleeves.Grant(sl, false) != null) items.Add(sl.m_Id);
            }
            Manager.OnTournamentReceivePrizeEvent(new TournamentReceivePrizeEventArgs
            {
                TournamentID = r.TournamentId,
                wins = r.Wins,
                losses = r.Losses,
                cardTemplateIDs = cards,
                inventoryTemplateIDs = items,
            });
        }

        /// <summary>LeaveTournament: the player drops; the prizes of the matches won are given.</summary>
        public static void Leave(ulong id, Delegate handler)
        {
            Run r = RunOf(id);
            LeaveTournamentResponse res = LocalServer.Ok<LeaveTournamentResponse>();
            res.TournamentID = id;
            res.success = true;
            res.ErrorMessage = "";
            LocalServer.Answer(typeof(TournamentsService.LeaveTournament), res, handler);
            LocalServer.Later(() =>
            {
                if (r != null) Finish(r);
                // A forfeit from the deck builder of an asynchronous gauntlet (UIDeckBuilderVM.LeaveTournamentViaForfeit)
                // shows a waiting spinner until the server sends the player back to the tournament list: without this,
                // the screen stayed dark forever (2026-10-31, leaving a draft). The prize window follows to the list.
                try
                {
                    EGameState state = GameClient.Instance.StateManager.CurrentGameState;
                    if (state == EGameState.TournamentSideboarding || state == EGameState.TournamentLobby || state == EGameState.Tournament)
                        Manager.TransitionToTournamentList();
                }
                catch (Exception e) { Debug.LogError("[HexRevival] [Battlegrounds] leave: " + e); }
            });
        }

        /// <summary>Dev: "bg [state] | bg autodeck &lt;id&gt; (a 40-card deck from the pool) | bg play &lt;id&gt; (Find Match)".</summary>
        public static string Command(string[] parts)
        {
            string sub = parts.Length > 1 ? parts[1] : "state";
            ulong id = parts.Length > 2 ? ulong.Parse(parts[2]) : (Data.Runs.Count > 0 ? Data.Runs[0].TournamentId : 0);
            Run r = RunOf(id);
            switch (sub)
            {
                case "state":
                    string res = "ladder " + Data.Ladder.ConstructedStars + " stars";
                    foreach (Run x in Data.Runs)
                        res += "\n" + x.TournamentId + ": " + x.Wins + "-" + x.Losses + " (games " + x.GameWins + "-" + x.GameLosses + "), deck " + x.Deck.CardsInDeck.Count + " + " + x.Deck.CardsInSideboard.Count
                            + (x.Packs != null ? ", draft round " + x.Round + " pick " + x.Pick : "");
                    return res;
                case "autodeck":
                    if (r == null) return "no run " + id;
                    List<card_instance_bits> all = new List<card_instance_bits>(r.Deck.CardsInDeck);
                    all.AddRange(r.Deck.CardsInSideboard);
                    DeckTemplate t = LimitedDeck(all.FindAll(c => !BasicShards().Exists(b => b.m_Id == c.TemplateID)).ConvertAll(c => c.TemplateID));
                    r.Deck.CardsInDeck = new List<card_instance_bits>();
                    foreach (TemplateCardStack st in t.m_DeckResources)
                        for (int i = 0; i < st.m_Count; i++)
                        {
                            card_instance_bits c = all.Find(x => x.TemplateID == st.m_idTemplate);
                            if (c == null) continue;
                            all.Remove(c);
                            r.Deck.CardsInDeck.Add(c);
                        }
                    r.Deck.CardsInSideboard = all;
                    r.Deck.PVPChampionId = t.m_ChampionId;
                    Save();
                    return "deck: " + r.Deck.CardsInDeck.Count + " cards";
                case "values":   // dev: the AI's value of the cards of a booster
                    {
                        string res2 = "";
                        foreach (CardTemplate t2 in Progression.OpenPack(BoosterSet())) res2 += t2.m_CardRarity + " " + t2.m_Name + " = " + Value(t2.m_Id).ToString("0.00") + "\n";
                        return res2;
                    }
                case "pick":
                    if (r == null || r.PackShown == null) return "no pack shown";
                    SelectDraftCard(id, r.PackId, r.PackShown[0].Id);
                    return "picked " + r.PackShown[0].TemplateID + " (round " + r.Round + " pick " + r.Pick + ")";
                case "prize":   // dev: "bg prize <wins>" = the prize of a draft run with that record (nothing else changes)
                    Finish(new Run { TournamentId = 90000003, Wins = parts.Length > 2 ? int.Parse(parts[2]) : 2 });
                    return "prize given";
                case "resume":
                    if (r == null) return "no run " + id;
                    Resume(id);
                    return "resumed " + id;
                case "play":
                    if (r == null) return "no run " + id;
                    GameEntrance(id, null, true, null);
                    return "match started";
            }
            return "usage: bg [state] | autodeck <id> | pick <id> | resume <id> | play <id>";
        }

        // ------------------------------------------------------------------
        // Ladder (the bar at the bottom of the hub): matches against the AI
        // ------------------------------------------------------------------

        private class LadderRecord
        {
            public int ConstructedStars, ConstructedMax, ConstructedStreak;
#pragma warning disable 0649   // no limited ladder offline: 0, kept in the save (JSON)
            public int LimitedStars, LimitedMax, LimitedStreak;
#pragma warning restore 0649
            /// <summary>When the account played its first ranked game (DateTime ticks): the seasons count from it.</summary>
            public long SeasonsFrom;
        }

        /// <summary>The stars at which the ladder's tiers start (the AI configuration's default tiers; PLACEHOLDER).</summary>
        private static readonly int[] TierStars = { 0, 10, 25, 45, 70 };

        /// <summary>The ladder season now (Sleeves.Seasons), or null if the game has none.</summary>
        private static string Season()
        {
            LadderRecord l = Data.Ladder;
            if (l.SeasonsFrom == 0) return null;
            int i = Sleeves.SeasonIndex(new DateTime(l.SeasonsFrom), DateTime.Now);
            return i >= 0 ? Sleeves.Seasons()[i] : null;
        }

        /// <summary>
        /// The season's sleeves of the tiers reached ("Awarded by obtaining the [tier] Rank in the [season] Ladder",
        /// Src.HexCodex), each once.
        /// </summary>
        private static void LadderSleeves()
        {
            string season = Season();
            if (season == null || Saves.Current == null) return;
            for (int t = 0; t < TierStars.Length; t++)
            {
                if (Data.Ladder.ConstructedStars < TierStars[t]) break;
                string claim = "ladder:" + season + ":" + Sleeves.LadderTiers[t];
                if (Saves.Current.Claims.Contains(claim)) continue;
                Saves.Current.Claims.Add(claim);
                Sleeves.Grant(Sleeves.SeasonSleeve(season, Sleeves.LadderTiers[t]));
            }
            Saves.SaveCurrent();
        }

        private static bool s_LadderBattle;

        public static Game.Shared.Tournaments.Messages.Tournament.LadderRecord.Response LadderRecordResponse()
        {
            LadderRecord l = Data.Ladder;
            return new Game.Shared.Tournaments.Messages.Tournament.LadderRecord.Response
            {
                constructedIronStars = 10,   // past the iron ranks (PLACEHOLDER: the iron ranks of new players are not known)
                constructedStars = l.ConstructedStars,
                constructedMaxStars = l.ConstructedMax,
                constructedStreak = l.ConstructedStreak,
                limitedStars = l.LimitedStars,
                limitedMaxStars = l.LimitedMax,
                limitedStreak = l.LimitedStreak,
                limitedSleeves = new List<ResourceId>(),
                constructedSleeves = SeasonSleeves(),
            };
        }

        /// <summary>The current season's tier sleeves (Bronze to Cosmic), shown by the ladder bar.</summary>
        private static List<ResourceId> SeasonSleeves()
        {
            List<ResourceId> res = new List<ResourceId>();
            string season = Season() ?? (Sleeves.Seasons().Count > 0 ? Sleeves.Seasons()[0] : null);
            if (season == null) return res;
            foreach (string t in Sleeves.LadderTiers) { InventoryDeckSleeve s = Sleeves.SeasonSleeve(season, t); if (s != null) res.Add(s.m_Id); }
            return res;
        }

        /// <summary>LadderFindMatch: a match against the AI with the chosen deck (ladder 0 ranked, 2 unranked, 3 immortal).</summary>
        public static void LadderFindMatch(ulong ladderId, deck_bits deck, bool searching)
        {
            if (!searching || deck == null) return;
            // The opponent pack's opponent for the player's stars (see Opponents), else a prebuilt deck.
            Opponents.Opponent o = Opponents.Pick(Opponents.Ladder, Data.Ladder.ConstructedStars);
            Opponents.SetNext(o);
            DeckTemplate ai = Opponents.DeckOf(o);
            List<DeckTemplate> decks = Decks.PrebuiltPvp();
            if (ai == null && decks.Count > 0) ai = decks[UnityEngine.Random.Range(0, decks.Count)];
            EDeckPersonality personality = Opponents.PersonalityOf(o);
            s_LadderBattle = ladderId == 0;
            Patches.PendingEncounterSetup = e => e.SeriesFormat = ladderId == 0 ? SessionStateEncounterData.SeriesType.LADDER_CONSTRUCTED
                : ladderId == 3 ? SessionStateEncounterData.SeriesType.LADDER_IMMORTAL : SessionStateEncounterData.SeriesType.LADDER_UNRANKED;
            LocalServer.Later(() =>
            {
                Manager.InUnrankedMatch = false;
                Patches.StartMatch(new UID(UID.Type.Deck, deck.Id), Opponents.SceneOf(o), ai, "ladder " + ladderId + (o != null ? " vs " + o.Name : ""), personality);
            });
        }

        /// <summary>
        /// End of a ranked ladder match (Src.Steam2016, a player in Sep 2016: "you lose 1 star per loss", "1 extra
        /// for a win streak of 2 or more"; matches were best of 3, a 2-0 gave 2 stars: here every game is a match,
        /// so a win gives 1 star). Never below 0 stars (PLACEHOLDER). Returns true if it was a ladder match.
        /// </summary>
        public static bool OnLadderGameEnded(bool won)
        {
            if (!s_LadderBattle) return false;
            s_LadderBattle = false;
            LadderRecord l = Data.Ladder;
            l.ConstructedStreak = won ? l.ConstructedStreak + 1 : 0;
            int stars = won ? 1 + (l.ConstructedStreak >= 2 ? 1 : 0) : -1;
            l.ConstructedStars = Math.Max(0, l.ConstructedStars + stars);
            l.ConstructedMax = Math.Max(l.ConstructedMax, l.ConstructedStars);
            if (l.SeasonsFrom == 0) l.SeasonsFrom = DateTime.Now.Ticks;
            Save();
            Notices.Show(Loc.Get("ladder_stars", l.ConstructedStars));
            LadderSleeves();
            return true;
        }
    }

    /// <summary>Draws Battlegrounds.MatchScore left of the gauntlet deck screen's RECORD / WINS-LOSSES frame.</summary>
    public class BattlegroundsScore : MonoBehaviour
    {
        private float m_Next;
        private Transform m_Frame;
        private string m_Text;

        private void Update()
        {
            if (Time.realtimeSinceStartup < m_Next) return;
            m_Next = Time.realtimeSinceStartup + 0.5f;
            m_Text = Battlegrounds.MatchScore();
            if (m_Text == null) { m_Frame = null; return; }
            if (m_Frame == null)
            {
                UITournamentDeckEditorViewModel vm = FindObjectOfType<UITournamentDeckEditorViewModel>();
                m_Frame = vm != null ? vm.transform.Find("Asynchronous_Elements/Frame_Progress_Win_Loss") : null;
            }
        }

        private void OnGUI()
        {
            if (m_Text == null || m_Frame == null || !m_Frame.gameObject.activeInHierarchy) return;
            Bounds b = NGUIMath.CalculateAbsoluteWidgetBounds(m_Frame);
            Camera cam = NGUITools.FindCameraForLayer(m_Frame.gameObject.layer);
            if (cam == null) return;
            Vector3 lo = cam.WorldToScreenPoint(b.min), hi = cam.WorldToScreenPoint(b.max);
            float S = GameSkin.Scale;
            float left = Mathf.Min(lo.x, hi.x), top = Screen.height - Mathf.Max(lo.y, hi.y), h = Mathf.Abs(hi.y - lo.y);
            Rect r = new Rect(left - 330 * S, top, 316 * S, h);
            GameSkin.Label(r, m_Text, GameSkin.Style("title", 17, GameSkin.Gold, TextAnchor.MiddleRight));
        }
    }
}
