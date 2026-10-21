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
    /// boss an elite. A lost fight is a loss mark; a lost boss is fought again, another lost fight is passed; 3 marks
    /// end the run. The opponents come from the account's opponent pack (arena tiers, see Opponents): by default the
    /// game's own "Arena_" decks. Rewards: see Rewards (amounts PLACEHOLDER). Not done: the round challenges and
    /// their buffs, the buyout.
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
            byte[] b = Opponents.StableGuid("arena|" + o.Name + "|" + o.Prebuilt + "|" + o.Champion).ToByteArray();
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
            foreach (Opponents.Tier t in Opponents.Active.Tiers)
                if (t.Mode == Opponents.Arena) foreach (Opponents.Opponent o in t.Opponents) Challenger(o);
            if (Current != null) Prepare(Current);
        }

        private static void Prepare(Run r)
        {
            foreach (Opponents.Opponent o in r.Opponents) Challenger(o);
            if (r.Info.Buffs == null) r.Info.Buffs = new List<ArenaBuff>();
        }

        // ------------------------------------------------------------------
        // Requests
        // ------------------------------------------------------------------

        /// <summary>The arena screen starts: the client's reconnection check, never answered offline, lets it join.</summary>
        public static void OnScreenStart(object viewModel)
        {
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
                Opponents.Opponent o = null;
                // Uruunaz and Zakiir: on the 4th fight of tier 2 and on every fight but the boss of tiers 3 and 4 (Src.HexCompendium).
                bool rareSlot = !boss && (tier >= 3 || (tier == 2 && slot == 3));
                if (rareSlot && UnityEngine.Random.value < RareChance) o = Opponents.Pick(Opponents.Arena, tier, x => x.Rare && !used.Contains(x.Name));
                if (o == null) o = Opponents.Pick(Opponents.Arena, tier, x => !x.Rare && x.Boss == boss && (boss || x.Elite == elite) && !used.Contains(x.Name));
                if (o == null) o = Opponents.Pick(Opponents.Arena, tier, x => !x.Rare && x.Boss == boss && !used.Contains(x.Name));
                if (o == null) o = Opponents.Pick(Opponents.Arena, tier, x => !x.Rare);
                if (o == null) { Debug.LogError("[HexRevival] [Arena] no opponent for tier " + tier + " in the pack " + Opponents.Active.Name); return null; }
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
            Opponents.Opponent o = r.Opponents[r.Current];
            DeckTemplate deck = Opponents.DeckOf(o);
            Opponents.SetNext(o);
            ulong arenaId = r.Info.ArenaID, owner = r.Info.PlayerID;
            Patches.PendingEncounterSetup = e =>
            {
                // The game's arena battle: its end goes back to the arena screen (UIBattle.Quit).
                e.SessionFlags |= ESessionFlags.IsPvEArena;
                e.ArenaInstance = arenaId;
                e.ArenaOwner = owner;
            };
            s_InBattle = true;
            Patches.StartMatch(new UID(UID.Type.Deck, r.Info.DeckId), Patches.NeutralEncounterId, deck,
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
            List<ArenaReward> news = new List<ArenaReward>();
            if (won)
            {
                f.FightResults = "WIN";
                r.Info.Wins++;
                news.AddRange(Rewards.ForWin(r, f, o, boss));
                if (r.Current < Fights - 1) r.Current++;
            }
            else
            {
                r.Info.Loses++;
                r.Info.LastTierLoss |= 1 << f.FightTier;   // TierLoss: tier 1 = 2, tier 2 = 4...
                // A lost boss is fought again; another lost fight is passed (Src.HexCompendium).
                if (!boss || r.Info.Loses >= MaxLosses) f.FightResults = "LOSE";
                if (!boss && r.Current < Fights - 1 && r.Info.Loses < MaxLosses) r.Current++;
            }
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
                r.Loot.AddRange(Rewards.ForEnd(r));
                res.AllLoot = r.Loot;
                res.GoldWin = Rewards.Grant(r.Loot);
                res.Success = true;
                Save();
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

        /// <summary>PLACEHOLDER: chance of Uruunaz or Zakiir on a fight where they can appear ("extremely rare", Src.Steam2016).</summary>
        private const float RareChance = 0.02f;

        private static class Rewards
        {
            /// <summary>PLACEHOLDER gold per win by fight (standard, elite, boss).</summary>
            private const int GoldStandard = 150, GoldElite = 250, GoldBoss = 400;
            /// <summary>Uruunaz "nets you 100,000 gold" (Src.Steam2016, a player in April 2016).</summary>
            private const int GoldUruunaz = 100000;
            /// <summary>PLACEHOLDER: chance of a chest (a card or an equipment) for a win that is not a boss.</summary>
            private const float ChestChance = 0.4f;

            public static List<ArenaReward> ForWin(Run r, ArenaFight f, Opponents.Opponent o, bool boss)
            {
                List<ArenaReward> res = new List<ArenaReward>();
                int gold = boss ? GoldBoss : o.Elite ? GoldElite : GoldStandard;
                res.Add(new ArenaReward { ArenaInstance = r.Info.ArenaID, FightInstance = f.FightID, Type = "GOLD", Reason = "GOLD", Gold = gold, Rarity = "", TemplateID = ResourceId.Invalid });
                r.Info.GoldPacks++;
                if (o.Rare && o.Name.StartsWith("Uruunaz"))
                    res.Add(new ArenaReward { ArenaInstance = r.Info.ArenaID, FightInstance = f.FightID, Type = "GOLD", Reason = "URUUNAZ", Gold = GoldUruunaz, Rarity = "", TemplateID = ResourceId.Invalid });
                if (boss || o.Rare || UnityEngine.Random.value < ChestChance)
                {
                    ArenaReward chest = Chest(r, f, boss || o.Rare ? "WIN" : "BONUS");
                    if (chest != null) res.Add(chest);
                }
                // A tier without a loss (TierLoss bit): a "Perfect" chest.
                if (boss && (r.Info.LastTierLoss & (1 << f.FightTier)) == 0)
                {
                    ArenaReward chest = Chest(r, f, "PERFECT");
                    if (chest != null) res.Add(chest);
                }
                // The sleeves of the bosses (Src.Game, their "Earned by defeating ..." texts).
                string sleeve = o.Name.StartsWith("Hogarth") ? "Hogarth Sleeve" : o.Name.StartsWith("Uruunaz") ? "Uruunaz Sleeve" : o.Name.StartsWith("Zakiir") ? "Matriarch of Flames Sleeve" : null;
                if (sleeve != null) AddSleeve(res, r, f, sleeve, o.Rare ? (o.Name.StartsWith("Uruunaz") ? "URUUNAZ" : "ZAKIIR") : "WIN");
                return res;
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

            /// <summary>A chest: half the time a card of the arena's set, otherwise an arena equipment (PLACEHOLDER odds).</summary>
            private static ArenaReward Chest(Run r, ArenaFight f, string reason)
            {
                if (UnityEngine.Random.value < 0.5f)
                {
                    List<CardTemplate> cards = ArenaCards();
                    if (cards.Count > 0)
                    {
                        CardTemplate c = cards[UnityEngine.Random.Range(0, cards.Count)];
                        r.Info.CardPacks++;
                        return new ArenaReward { ArenaInstance = r.Info.ArenaID, FightInstance = f.FightID, Type = "CARD", Reason = reason, Rarity = c.m_CardRarity.ToString(), TemplateID = c.m_Id };
                    }
                }
                List<InventoryItemData> eq = ArenaEquipment();
                if (eq.Count == 0) return null;
                InventoryItemData e = eq[UnityEngine.Random.Range(0, eq.Count)];
                r.Info.EquipmentPacks++;
                return new ArenaReward { ArenaInstance = r.Info.ArenaID, FightInstance = f.FightID, Type = "EQUIPMENT", Reason = reason, Rarity = "", TemplateID = e.m_Id };
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

        /// <summary>Dev: "arena [state] | win | lose | reset".</summary>
        public static string Command(string[] parts)
        {
            string sub = parts.Length > 1 ? parts[1] : "state";
            Run r = Current;
            switch (sub)
            {
                case "state":
                    if (r == null) return "no run (pool: " + ArenaCards().Count + " cards, " + ArenaEquipment().Count + " equipment)";
                    string s = r.Info.Wins + "-" + r.Info.Loses + ", fight " + (r.Current + 1) + ", loot " + r.Loot.Count;
                    for (int i = 0; i < r.Fights.Count; i++) s += "\n" + (i + 1) + " t" + r.Fights[i].FightTier + " " + r.Opponents[i].Name + (r.Opponents[i].Boss ? " (boss)" : r.Opponents[i].Elite ? " (elite)" : "") + " " + r.Fights[i].FightResults;
                    return s;
                case "win":
                case "lose":
                    s_InBattle = true;
                    OnGameEnded(sub == "win");
                    return "ok";
                case "reset":
                    Data.Run = null;
                    Save();
                    return "run removed";
            }
            return "usage: arena [state] | win | lose | reset";
        }
    }
}
