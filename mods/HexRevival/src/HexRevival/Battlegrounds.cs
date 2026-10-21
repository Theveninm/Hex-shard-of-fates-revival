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
            public deck_bits Deck;                   // the tournament deck: the pool in its reserves for a limited run
            public bool DeckBuilt;                   // the pool was handed to the deck builder
            // Draft: the packs at the table (seat 0 is the player), round 0..2, pick 0..16.
            public List<List<ResourceId>> Packs;
            public List<List<ResourceId>> BotPicks;
            public List<card_instance_bits> Picks = new List<card_instance_bits>();
            public List<card_instance_bits> PackShown;
            public ulong PackId;
            public int Round, Pick;
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
            d.tournamentFees = new Dictionary<int, TournamentEntryInfo> { { 0, FeeInPlatinum ? new TournamentEntryInfo { platCost = g.GoldFee / HexRevival.Store.GoldPerPlatinum } : new TournamentEntryInfo { goldCost = g.GoldFee } } };
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

        /// <summary>PLACEHOLDER prize: one booster per match won.</summary>
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
                long fee = plat ? g.GoldFee / HexRevival.Store.GoldPerPlatinum : g.GoldFee;
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

        /// <summary>Accounts without "platinum prices in gold" pay the entries in platinum (the gold fees are platinum x 10).</summary>
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

        /// <summary>A bot's pick: the best rarity, then the colors it already drafts.</summary>
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
                float score = (int)t.m_CardRarity * 10 + UnityEngine.Random.value * 6;
                ECardShards c = Colors(t);
                if (colors != ECardShards.Unknown && c != ECardShards.Unknown) score += (c & ~colors) == 0 ? 8 : -8;
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
            LocalServer.Later(() => Patches.StartMatch(new UID(UID.Type.Deck, r.Deck.Id), Patches.NeutralEncounterId, ai,
                g.Name + " match " + match + " game " + game));
        }

        /// <summary>The AI's deck: a prebuilt deck (constructed), a sealed pool or another drafter's picks, built into 40 cards.</summary>
        private static DeckTemplate Opponent(Gauntlet g, Run r)
        {
            if (g.Kind == Kind.Constructed)
            {
                List<DeckTemplate> decks = Decks.PrebuiltPvp();
                return decks.Count > 0 ? decks[UnityEngine.Random.Range(0, decks.Count)] : null;
            }
            List<ResourceId> pool = new List<ResourceId>();
            if (g.Kind == Kind.Draft && r.BotPicks != null)
                pool.AddRange(r.BotPicks[1 + (r.Wins + r.Losses) % (DraftSeats - 1)]);
            else
                for (int i = 0; i < 6; i++) foreach (CardTemplate t in Progression.OpenPack(BoosterSet())) pool.Add(t.m_Id);
            return LimitedDeck(pool);
        }

        /// <summary>A 40-card deck from a pool: its two main colors, 23 cards (the rarest), 17 basic shards.</summary>
        private static DeckTemplate LimitedDeck(List<ResourceId> pool)
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
            cards.Sort((a, b) => b.m_CardRarity != a.m_CardRarity ? b.m_CardRarity.CompareTo(a.m_CardRarity) : a.m_ResourceCost.CompareTo(b.m_ResourceCost));
            if (cards.Count > 23) cards.RemoveRange(23, cards.Count - 23);
            DeckTemplate d = new DeckTemplate();
            d.m_Id = new ResourceId(Guid.NewGuid());
            d.m_DeckName = "Gauntlet opponent";
            d.m_ChampionId = RandomChampion();
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

        private static ResourceId RandomChampion()
        {
            List<DeckTemplate> decks = Decks.PrebuiltPvp();
            return decks.Count > 0 ? decks[UnityEngine.Random.Range(0, decks.Count)].m_ChampionId : new ResourceId(GameConfig.DefaultChampionTemplateId);
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
                text = (matchWon ? "Match won" : "Match lost") + ": " + r.Wins + "-" + r.Losses;
                r.GameWins = r.GameLosses = 0;
            }
            else text = "Game " + (won ? "won" : "lost") + ": " + r.GameWins + "-" + r.GameLosses + " in this match";
            TournamentPlayerInfo me;
            if (Manager.AsynchPlayerOverrides.TryGetValue(g.Id, out me)) { me.Wins = r.Wins; me.Losses = r.Losses; }
            Manager.CurrentlyInMatch = r.GameWins + r.GameLosses > 0;
            Debug.Log("[HexRevival] [Battlegrounds] " + g.Name + ": " + text);
            Notices.Show(text);
            if (r.Wins >= g.MaxWins || r.Losses >= g.MaxLosses || r.Wins + r.Losses >= g.MaxMatches) s_Finished = r;
            Save();
            return true;
        }

        /// <summary>Called every frame: the prizes of a finished run, once back from the battle.</summary>
        public static void Tick()
        {
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
            List<ResourceId> cards = new List<ResourceId>();
            CardSetTemplate set = BoosterSet();
            for (int i = 0; i < PrizeBoosters(r.Wins); i++)
                foreach (CardTemplate t in Progression.OpenPack(set)) { cards.Add(t.m_Id); Collection.Grant(t.m_Id, 1); }
            Debug.Log("[HexRevival] [Battlegrounds] " + (g != null ? g.Name : "?") + " over: " + r.Wins + "-" + r.Losses + ", " + cards.Count + " cards");
            Manager.OnTournamentReceivePrizeEvent(new TournamentReceivePrizeEventArgs
            {
                TournamentID = r.TournamentId,
                wins = r.Wins,
                losses = r.Losses,
                cardTemplateIDs = cards,
                inventoryTemplateIDs = new List<ResourceId>(),
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
            if (r != null) LocalServer.Later(() => Finish(r));
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
                case "pick":
                    if (r == null || r.PackShown == null) return "no pack shown";
                    SelectDraftCard(id, r.PackId, r.PackShown[0].Id);
                    return "picked " + r.PackShown[0].TemplateID + " (round " + r.Round + " pick " + r.Pick + ")";
                case "play":
                    if (r == null) return "no run " + id;
                    GameEntrance(id, null, true, null);
                    return "match started";
            }
            return "usage: bg [state] | autodeck <id> | pick <id> | play <id>";
        }

        // ------------------------------------------------------------------
        // Ladder (the bar at the bottom of the hub): matches against the AI
        // ------------------------------------------------------------------

        private class LadderRecord
        {
            public int ConstructedStars, ConstructedMax, ConstructedStreak;
            public int LimitedStars, LimitedMax, LimitedStreak;
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
                constructedSleeves = new List<ResourceId>(),
            };
        }

        /// <summary>LadderFindMatch: a match against the AI with the chosen deck (ladder 0 ranked, 2 unranked, 3 immortal).</summary>
        public static void LadderFindMatch(ulong ladderId, deck_bits deck, bool searching)
        {
            if (!searching || deck == null) return;
            List<DeckTemplate> decks = Decks.PrebuiltPvp();
            DeckTemplate ai = decks.Count > 0 ? decks[UnityEngine.Random.Range(0, decks.Count)] : null;
            s_LadderBattle = ladderId == 0;
            Patches.PendingEncounterSetup = e => e.SeriesFormat = ladderId == 0 ? SessionStateEncounterData.SeriesType.LADDER_CONSTRUCTED
                : ladderId == 3 ? SessionStateEncounterData.SeriesType.LADDER_IMMORTAL : SessionStateEncounterData.SeriesType.LADDER_UNRANKED;
            LocalServer.Later(() =>
            {
                Manager.InUnrankedMatch = false;
                Patches.StartMatch(new UID(UID.Type.Deck, deck.Id), Patches.NeutralEncounterId, ai, "ladder " + ladderId);
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
            Save();
            Notices.Show("Ladder: " + l.ConstructedStars + " stars");
            return true;
        }
    }
}
