using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Game.Client;
using Game.Client.Siege;
using Game.Shared;
using Game.Shared.Campaign.Siege;
using Game.Shared.Domain;
using Game.Shared.Mechanics;
using Game.Shared.Network;
using Game.Shared.Profile;
using Game.Shared.Resources;
using Game.Shared.Utils;
using Reckoning.Game;
using UnityEngine;
using JsonUtils = Game.Shared.Network.HConnect.Utils;

namespace HexRevival
{
    /// <summary>
    /// Siege mode, served locally. The original was asynchronous PvP run by the server: a player stakes gold or
    /// platinum on a defense of 3 decks played by the AI; other players pay a fee to attack it and must beat the
    /// 3 decks in a row. The client asks for everything through one request channel (Execution.IssueRequest,
    /// answered here by type) and was told about battles and results by server pushes, simulated here with the
    /// client's own handlers.
    ///
    /// Without other players (see docs/PROGRESSION.md, Siege):
    /// - the defenses to attack come from a keep source (IKeepSource): placeholder defenses for now
    ///   (PlaceholderKeeps), other players' defenses from a server later;
    /// - the player's own defense is attacked over time by placeholder attackers, AIs simulating players of
    ///   varied skill (Simulate), human attackers later.
    /// Economy (Src.Video: the game's rules screens and keep list, MyHotStream 2018-05-29 and 2018-06-03): the
    /// attacker pays 40% of the bounty, of which 50% goes to the defender, 37.5% to the bounty, the rest is lost;
    /// minimum bounty 1000 gold or 50 platinum; the attacker who beats the 3 decks takes the bounty and the
    /// Siege Sacks, the defender gets the Sacks too. Siege Sacks: the client's own formula (Utils.SiegeSackValue).
    /// </summary>
    public static class Siege
    {
        /// <summary>Src.Video (rules screens): 1000 gold / 50 platinum minimum, fee 40%, 50% to the defender, 37.5% to the bounty.</summary>
        public static readonly Messaging.FeeDetail Fees = new Messaging.FeeDetail(1000, 50, 0.40f, 0.50f, 0.375f);

        // Between two games (Src.Video, A 1:50-2:17): a 27 s countdown, the next champion revealed, the game loading at 9 s.
        private const int IntermissionSeconds = 27;
        private const int LoadAtSeconds = 9;

        /// <summary>
        /// The fee split of FeeDetail.Calculate, in exact decimals: the client's float version gives 401 for a 1000
        /// bounty, the original server 400 (Src.Video: every row of the keep list, and the defender's -570).
        /// </summary>
        public static void Calculate(int bounty, out int total, out int defender, out int pot, out int rake)
        {
            total = (int)Math.Ceiling(bounty * 0.40m);
            defender = (int)Math.Ceiling(total * 0.50m);
            pot = (int)Math.Ceiling(total * 0.375m);
            rake = total - defender - pot;
        }

        private static readonly UID Source = new UID(UID.Type.ServiceCampaign, 1UL);

        /// <summary>Where the defenses to attack come from (placeholders now, a server later).</summary>
        public static IKeepSource Keeps = new PlaceholderKeeps();

        // ------------------------------------------------------------------
        // Request channel
        // ------------------------------------------------------------------

        /// <summary>Execution.IssueRequest (Harmony prefix): answers a Siege request locally. Always handled.</summary>
        public static void OnRequest(byte[] envelope, Action<CustomNetworkResponse<Messaging.Response>> notify)
        {
            Messaging.SiegeRequest head = Read<Messaging.SiegeRequest>(envelope);
            string type = head != null ? head.RequestType : null;
            object answer;
            string error = null;
            try
            {
                Simulate();
                PushNewLog();
                switch (type)
                {
                    case "feeinfo": answer = new Messaging.FeeInfo.Response { Detail = Fees }; break;
                    case "quser": answer = UserListing(Read<Messaging.UserListing>(envelope)); break;
                    case "getactive": answer = Query(Read<Messaging.Query>(envelope)); break;
                    case "create": answer = Create(Read<Messaging.Create>(envelope), out error); break;
                    case "remove": answer = Remove(Read<Messaging.Remove>(envelope).SiegeId, out error); break;
                    case "activate": answer = Activate(Read<Messaging.Activate>(envelope).SiegeId, out error); break;
                    case "qlog": answer = Log(Read<Messaging.SiegeLog>(envelope)); break;
                    case "challenge": answer = Challenge(Read<Messaging.Challenge>(envelope), out error); break;
                    case "forfeit": answer = Forfeit(Read<Messaging.Forfeit>(envelope).SiegeId); break;
                    case "pgame": answer = Practice(Read<Messaging.PracticeMatch>(envelope), out error); break;
                    default: answer = null; error = "UNSUPPORTED"; break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[HexRevival] [Siege] " + type + ": " + e);
                answer = null;
                error = "Generic_Error";
            }
            Debug.Log("[HexRevival] [Siege] request " + type + (error != null ? " -> " + error : ""));
            if (notify == null) return;
            CustomNetworkResponse<Messaging.Response> res = new CustomNetworkResponse<Messaging.Response>();
            if (error != null) res.ErrorMessage = new Game.Shared.Network.ErrorArgs { Error = 1, ErrorMessage = error };
            else res.Message = new Messaging.Response { Envelope = Write(answer) };
            Campaign.Enqueue(() => notify(res));
        }

        private static T Read<T>(byte[] data) where T : class
        {
            T res = null;
            if (data != null) JsonUtils.DeserializeJson(data, (T x) => { res = x; });
            return res;
        }

        private static byte[] Write(object o)
        {
            byte[] res = null;
            JsonUtils.SerializeJson(o, (byte[] x) => { res = x; });
            return res;
        }

        // ------------------------------------------------------------------
        // The player's own defenses (saved per account: siege.json)
        // ------------------------------------------------------------------

        private class Store
        {
            public ulong NextId = 1;
            public List<siege_bits> Mine = new List<siege_bits>();
            public List<siege_ledger_bits> Log = new List<siege_ledger_bits>();
            public DateTime LastSimulation = DateTime.UtcNow;
            public int Sacks;
        }

        private static Store s_Store;
        private static string s_StorePath;

        private static string StorePath { get { return Saves.File("siege.json"); } }

        private static Store Data
        {
            get
            {
                if (s_Store != null && s_StorePath == StorePath) return s_Store;
                s_StorePath = StorePath;
                s_Store = null;
                try { if (File.Exists(s_StorePath)) s_Store = Read<Store>(File.ReadAllBytes(s_StorePath)); }
                catch (Exception e) { Debug.LogError("[HexRevival] [Siege] read: " + e); }
                if (s_Store == null) s_Store = new Store();
                return s_Store;
            }
        }

        private static void SaveData()
        {
            try { File.WriteAllBytes(StorePath, Write(Data)); }
            catch (Exception e) { Debug.LogError("[HexRevival] [Siege] save: " + e); }
        }

        private static ulong PlayerId { get { return GameClient.Instance.PlayerProfile.ReckoningID.GetInstanceId(); } }

        private static string PlayerName
        {
            get
            {
                string n = null;
                try { n = GameClient.Instance.PlayerProfile.GetKeepName(); } catch { }
                return string.IsNullOrEmpty(n) && Saves.Current != null ? Saves.Current.Name : n;
            }
        }

        private static siege_bits Mine(ulong id) { return Data.Mine.Find(s => s.Id == id); }

        private static siege_bits ActiveDefense()
        {
            return Data.Mine.Find(s => s.CurStatus == siege_bits.Status.ACTIVE || s.CurStatus == siege_bits.Status.INGAME);
        }

        private static Messaging.UserListing.Response UserListing(Messaging.UserListing q)
        {
            List<siege_bits> res = new List<siege_bits>();
            foreach (siege_bits s in Data.Mine)
            {
                bool active = s.CurStatus == siege_bits.Status.ACTIVE || s.CurStatus == siege_bits.Status.INGAME;
                if (q == null || q.SiegeStatus == Messaging.UserListing.Status.ALL
                    || (q.SiegeStatus == Messaging.UserListing.Status.ACTIVE) == active) res.Add(s);
            }
            return new Messaging.UserListing.Response { SiegeStatus = q != null ? q.SiegeStatus : Messaging.UserListing.Status.ALL, Offset = 0, Count = res.Count, Results = res.ToArray() };
        }

        private static Messaging.Create.Response Create(Messaging.Create c, out string error)
        {
            error = null;
            if (c == null || c.Fights == null || c.Fights.Length != 3) { error = "Siege_MissingDecks_Error_Desc"; return null; }
            if (ActiveDefense() != null) { error = "Siege_Commit"; return null; }
            bool plat = c.Cur == Messaging.Create.Currency.PLATINUM;
            if (c.PotAmount < (plat ? Fees.MinPotPlatinum : Fees.MinPotGold)) { error = "Siege_Bounty_Minimum"; return null; }
            if (!Pay(plat, c.PotAmount)) { error = "Low_Funds"; return null; }
            siege_bits s = new siege_bits();
            s.Id = Data.NextId++;
            s.PlayerID = PlayerId;
            s.Name = PlayerName;
            s.FightInfo = Compressor.Compress(Write(c.Fights));
            s.Currency = plat ? siege_bits.CurrencyType.PLATINUM : siege_bits.CurrencyType.GOLD;
            s.Pot = c.PotAmount;
            s.ChallengeLimit = c.ChallengeLimit;
            s.Created = DateTime.UtcNow;
            s.StatusDate = s.Created;
            s.CurStatus = siege_bits.Status.ACTIVE;
            SetFees(s);
            Data.Mine.Add(s);
            Data.LastSimulation = DateTime.UtcNow;
            AddLog(s, siege_ledger_bits.SiegeAction.STATE, "ACTIVE", PlayerId, PlayerName, DateTime.UtcNow);
            SaveData();
            Debug.Log("[HexRevival] [Siege] defense " + s.Id + " committed: " + s.Pot + " " + s.Currency);
            return new Messaging.Create.Response { Updated = s };
        }

        /// <summary>Withdraw: the bounty comes back, no Siege Sacks (Src.Video, rules screen).</summary>
        private static Messaging.Remove.Response Remove(ulong id, out string error)
        {
            error = null;
            siege_bits s = Mine(id);
            if (s == null || (s.CurStatus != siege_bits.Status.ACTIVE && s.CurStatus != siege_bits.Status.INGAME)) { error = "Generic_Error"; return null; }
            Earn(s.Currency == siege_bits.CurrencyType.PLATINUM, s.Pot, 0);
            s.CurStatus = siege_bits.Status.CLOSED;
            s.CloseDate = DateTime.UtcNow;
            s.StatusDate = s.CloseDate;
            AddLog(s, siege_ledger_bits.SiegeAction.CLOSED, null, PlayerId, PlayerName, DateTime.UtcNow);
            SaveData();
            return new Messaging.Remove.Response { Updated = s };
        }

        private static Messaging.Activate.Response Activate(ulong id, out string error)
        {
            error = null;
            siege_bits s = Mine(id);
            if (s == null) { error = "Generic_Error"; return null; }
            if (s.CurStatus == siege_bits.Status.PENDING || s.CurStatus == siege_bits.Status.ESCROW) s.CurStatus = siege_bits.Status.ACTIVE;
            SaveData();
            return new Messaging.Activate.Response { Updated = s };
        }

        private static Messaging.SiegeLog.Response Log(Messaging.SiegeLog q)
        {
            siege_bits s = q != null && q.SiegeId != 0 ? Mine(q.SiegeId) : null;
            if (s == null) s = ActiveDefense();
            if (s == null && Data.Mine.Count > 0) s = Data.Mine[Data.Mine.Count - 1];
            List<siege_ledger_bits> logs = new List<siege_ledger_bits>();
            if (s != null)
                for (int i = Data.Log.Count - 1; i >= 0; i--) if (Data.Log[i].SiegeId == s.Id) logs.Add(Data.Log[i]);
            return new Messaging.SiegeLog.Response { Siege = s, Logs = logs.ToArray(), Replays = new Messaging.SiegeLog.Response.Record[0] };
        }

        private static readonly HashSet<ulong> s_Pushed = new HashSet<ulong>();
        private static string s_PushedFor;

        /// <summary>
        /// The client totals its defense's earnings from the events it was told about (siege:saction), not from
        /// the log it reads: every log entry of the player's defenses not yet told this session is pushed, oldest
        /// first (and before the defenses' list is answered, which replaces the client's copy of them).
        /// </summary>
        private static void PushNewLog()
        {
            if (s_PushedFor != StorePath) { s_Pushed.Clear(); s_PushedFor = StorePath; }
            foreach (siege_ledger_bits l in Data.Log)
                if (s_Pushed.Add(l.Id)) Push("handleNotifySiegeAction", new Messaging.EventSiegeAction(Source, l));
        }

        private static siege_ledger_bits AddLog(siege_bits s, siege_ledger_bits.SiegeAction action, string context, ulong player, string name, DateTime when)
        {
            siege_ledger_bits l = new siege_ledger_bits();
            l.Id = Data.NextId++;
            l.SiegeId = s.Id;
            l.Action = action;
            l.Context = context;
            l.PlayerId = player;
            l.PlayerName = name;
            l.Wins = s.Wins;
            l.Created = when;
            Data.Log.Add(l);
            return l;
        }

        /// <summary>Challenge cost of a defense (siege_bits.ChallengeFee + Bet + Rake is the cost shown in the list).</summary>
        internal static void SetFees(siege_bits s)
        {
            int total, defender, pot, rake;
            Calculate(s.Pot, out total, out defender, out pot, out rake);
            s.ChallengeFee = defender;
            s.ChallengeBet = pot;
            s.ChallengeRake = rake;
        }

        // ------------------------------------------------------------------
        // Currency
        // ------------------------------------------------------------------

        private static bool Pay(bool platinum, int amount)
        {
            if (Saves.Current == null) return false;
            if ((platinum ? Saves.Current.Platinum : Saves.Current.Gold) < amount) return false;
            if (platinum) Saves.Current.Platinum -= amount; else Saves.Current.Gold -= amount;
            Saves.SaveCurrent();
            PushBalance();
            return true;
        }

        private static void Earn(bool platinum, int amount, int sacks)
        {
            if (Saves.Current == null) return;
            if (platinum) Saves.Current.Platinum += amount; else Saves.Current.Gold += amount;
            Saves.SaveCurrent();
            PushBalance();
            if (sacks > 0)
            {
                Data.Sacks += sacks;
                SaveData();
                PushSacks();
            }
        }

        private static void PushBalance()
        {
            try { GameClient.Instance.PlayerProfile.UpdateBalance((int)Saves.Current.Gold, (int)Saves.Current.Platinum); } catch { }
        }

        private const ulong SackItemId = 900000001UL;

        /// <summary>Siege Sacks owned.</summary>
        public static int Sacks { get { return Data.Sacks; } }

        /// <summary>Pays Siege Sacks (the store's Prize Exchange); false if not enough.</summary>
        public static bool SpendSacks(int n)
        {
            if (n <= 0 || Data.Sacks < n) return false;
            Data.Sacks -= n;
            SaveData();
            return true;
        }

        /// <summary>The Siege Sacks stack as the client's currency inventory (also when empty).</summary>
        public static inventory_bits SackStack(UID owner)
        {
            inventory_bits inv = new inventory_bits();
            inv.Id = SackItemId;
            inv.TemplateID = BuiltInResources.CurrencySiegeSack;
            inv.ItemQuantity = Data.Sacks;
            inv.ClaimDate = DateTime.MinValue;
            inv.EscrowStatus = "";
            inv.OwnerReckId = owner.GetInstanceId();
            return inv;
        }

        /// <summary>Siege Sacks are an inventory currency (BuiltInResources.CurrencySiegeSack): one item holding the count.</summary>
        public static inventory_bits SackItem(UID owner)
        {
            if (Data.Sacks <= 0) return null;
            inventory_bits inv = new inventory_bits();
            inv.Id = SackItemId;
            inv.TemplateID = BuiltInResources.CurrencySiegeSack;
            inv.ItemQuantity = Data.Sacks;
            inv.ClaimDate = DateTime.MinValue;
            inv.EscrowStatus = "";
            inv.OwnerReckId = owner.GetInstanceId();
            return inv;
        }

        private static void PushSacks()
        {
            try
            {
                GameClient.Instance.PlayerProfile.AddInventoryItem(new UID(UID.Type.InventoryItem, SackItemId), BuiltInResources.CurrencySiegeSack, Data.Sacks);
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Siege] sacks: " + e.Message); }
        }

        // ------------------------------------------------------------------
        // Attack: the player against a defense of the keep source
        // ------------------------------------------------------------------

        private class Assault
        {
            public siege_bits Keep;
            public Messaging.Create.FightConfig[] Fights;
            public UID PlayerDeck;
            public ProfileDeckTemplate Deck;
            public int Seq;
            public DateTime StartAt;
            public bool Practice;
            public bool Started;    // the countdown is over (or not running)
            public bool InBattle;   // a fight of the assault is being played
        }

        private static Assault s_Assault;

        private static Messaging.Query.Response Query(Messaging.Query q)
        {
            List<siege_bits> res = new List<siege_bits>(Keeps.Active());
            siege_bits mine = ActiveDefense();
            if (mine != null && mine.CurStatus == siege_bits.Status.ACTIVE) res.Add(mine);
            return new Messaging.Query.Response { QuerySort = q != null ? q.Sorting : new Messaging.SortParam[0], QueryOffset = 0, QueryCount = res.Count, Results = res.ToArray() };
        }

        private static Messaging.Challenge.Response Challenge(Messaging.Challenge c, out string error)
        {
            error = null;
            siege_bits k = c != null ? Keeps.Get(c.SiegeId) : null;
            if (k == null || k.CurStatus != siege_bits.Status.ACTIVE || s_Assault != null) { error = "Siege_InitChallenge_Error_Text"; return null; }
            UID deck = Decks.FindDeck(c.Deck);
            if (deck == UID.Invalid) { error = "Siege_InitChallenge_Error_Text"; return null; }
            bool plat = k.Currency == siege_bits.CurrencyType.PLATINUM;
            int total, defender, pot, rake;
            Calculate(k.Pot, out total, out defender, out pot, out rake);
            if (!Pay(plat, total)) { error = "LOW_FUNDS"; return null; }
            // The fee is paid at once, no refund (Src.Video, A 1:50); the defense leaves the list during the attack.
            Keeps.Challenged(k, pot);
            Messaging.Create.FightConfig[] fights = new List<Messaging.Create.FightConfig>(k.GetFights()).ToArray();
            s_Assault = new Assault { Keep = k, Fights = fights, PlayerDeck = deck, Deck = c.Deck, Seq = 0, Started = true };   // started by NextFight's countdown
            Debug.Log("[HexRevival] [Siege] assault on " + k.Name + " (" + k.Id + "), fee " + total + ", bounty now " + k.Pot);
            Campaign.Enqueue(() => NextFight(false));
            return new Messaging.Challenge.Response { Status = "OK" };
        }

        /// <summary>The assault screen for the next fight (siege:challenge), then the fight itself after the countdown.</summary>
        private static void NextFight(bool afterWin)
        {
            Assault a = s_Assault;
            if (a == null) return;
            Debug.Log("[HexRevival] [Siege] next fight " + (a.Seq + 1) + " (after win " + afterWin + ")");
            a.StartAt = DateTime.UtcNow.AddSeconds(IntermissionSeconds - LoadAtSeconds);
            a.Started = false;
            Messaging.SiegeState st = new Messaging.SiegeState(a.Keep, new UID(UID.Type.ServicePlayer, PlayerId), PlayerName, a.Deck,
                DateTime.UtcNow.AddSeconds(IntermissionSeconds), a.Seq);
            Push("handleNotifySiegeChallenge", new Messaging.EventSiegeChallenge(Source, st));
        }

        /// <summary>Called every frame: starts the pending fight when its countdown is over.</summary>
        public static void Tick()
        {
            Assault a = s_Assault;
            if (a == null || a.Started || a.Practice || DateTime.UtcNow < a.StartAt) return;
            a.Started = true;
            a.InBattle = true;
            Messaging.Create.FightConfig f = a.Seq < a.Fights.Length ? a.Fights[a.Seq] : null;
            if (f == null || f.Deck == null) { Debug.LogError("[HexRevival] [Siege] no deck for fight " + a.Seq); s_Assault = null; return; }
            Patches.PendingSiege = SessionStateEncounterData.SeriesType.SIEGE;
            Patches.StartMatch(a.PlayerDeck, Patches.NeutralEncounterId, ToAIDeck(f.Deck), "siege " + a.Keep.Name + " " + (a.Seq + 1) + "/3",
                f.Deck.GetPersonality(), f.Message);
        }

        /// <summary>End of a match. Returns true if it was a Siege battle.</summary>
        public static bool OnGameEnded(bool won)
        {
            Assault a = s_Assault;
            Debug.Log("[HexRevival] [Siege] game ended, won " + won + ", assault " + (a != null ? a.Seq + " in battle " + a.InBattle : "none"));
            if (a == null || !a.InBattle) return false;
            a.InBattle = false;
            if (a.Practice) { s_Assault = null; return true; }
            if (won && a.Seq < a.Fights.Length - 1)
            {
                // Games 1 and 2 won: straight back to the assault screen, the next door opens (Src.Video, A 17:19-17:20).
                a.Seq++;
                Campaign.Enqueue(() => NextFight(true));
                return true;
            }
            s_Assault = null;
            siege_bits k = a.Keep;
            siege_ledger_bits l = new siege_ledger_bits();
            l.SiegeId = k.Id;
            l.PlayerId = PlayerId;
            l.PlayerName = PlayerName;
            l.FightSeq = a.Seq;
            l.Created = DateTime.UtcNow;
            if (won)
            {
                // The whole bounty and the Siege Sacks (Src.Video, A 31:49: "Earned: 1150 gold, 11 sacks").
                int sacks = Game.Shared.Campaign.Utils.SiegeSackValue(k);
                l.Action = siege_ledger_bits.SiegeAction.DEFEATED;
                l.Wins = k.Wins;
                Earn(k.Currency == siege_bits.CurrencyType.PLATINUM, k.Pot, sacks);
                Keeps.Sacked(k);
                Debug.Log("[HexRevival] [Siege] " + k.Name + " sacked: +" + k.Pot + " " + k.Currency + ", +" + sacks + " sacks");
            }
            else
            {
                l.Action = siege_ledger_bits.SiegeAction.DEFENDED;
                l.Wins = k.Wins;
                Keeps.Defended(k);
                Debug.Log("[HexRevival] [Siege] " + k.Name + " defended at fight " + (a.Seq + 1));
            }
            // The battle's end screen goes back to the Siege screen with this last event (UIBattle).
            Push("handleNotifySiegeAction", new Messaging.EventSiegeAction(Source, l));
            return true;
        }

        /// <summary>Withdraw during an assault: the attack is lost (the fee is not refunded).</summary>
        private static Messaging.Forfeit.Response Forfeit(ulong id)
        {
            Assault a = s_Assault;
            if (a != null && a.Keep.Id == id && !a.Practice)
            {
                s_Assault = null;
                Keeps.Defended(a.Keep);
            }
            return new Messaging.Forfeit.Response { Status = "OK" };
        }

        /// <summary>Practice game from the Siege deck builder: one of the player's decks played by the AI.</summary>
        private static Messaging.PracticeMatch.Response Practice(Messaging.PracticeMatch p, out string error)
        {
            error = null;
            UID mine = p != null ? Decks.FindDeck(p.UserDeck) : UID.Invalid;
            if (mine == UID.Invalid || p.AiDeck == null) { error = "Generic_Error"; return null; }
            s_Assault = new Assault { Practice = true, Started = true, InBattle = true };
            Campaign.Enqueue(() =>
            {
                Patches.PendingSiege = SessionStateEncounterData.SeriesType.SIEGE_PRACTICE;
                Patches.StartMatch(mine, Patches.NeutralEncounterId, ToAIDeck(p.AiDeck), "siege practice", p.AiDeck.GetPersonality());
            });
            return new Messaging.PracticeMatch.Response { Status = "OK" };
        }

        /// <summary>A defense deck as the AI's deck (cards, champion, equipment).</summary>
        private static DeckTemplate ToAIDeck(ProfileDeckTemplate t)
        {
            DeckTemplate d = new DeckTemplate();
            d.m_Id = new ResourceId(Guid.NewGuid());
            d.m_DeckName = t.Name;
            d.m_ChampionId = t.Champ;
            d.m_DeckSleeve = t.Sleeve;
            Dictionary<ResourceId, TemplateCardStack> stacks = new Dictionary<ResourceId, TemplateCardStack>();
            foreach (ProfileDeckTemplate.CardDescriptor c in t.CardDesc)
            {
                if (c.Res || c.Count <= 0) continue;
                TemplateCardStack st;
                if (!stacks.TryGetValue(c.Temp, out st))
                {
                    st = new TemplateCardStack { m_idTemplate = c.Temp, m_Count = 0 };
                    stacks[c.Temp] = st;
                    d.m_DeckResources.Add(st);
                }
                st.m_Count += c.Count;
            }
            if (t.Equip != null) foreach (ResourceId e in t.Equip.Values) if (e.IsValid) d.m_EquipmentIDs.Add(e);
            return d;
        }

        /// <summary>A server push, handed to the client's own handler (ClientSiegeManager.handleNotify*).</summary>
        private static void Push(string handler, Messaging.SiegeEvent evt)
        {
            try
            {
                CustomNetworkRequest<Messaging.Request> req = new CustomNetworkRequest<Messaging.Request>();
                req.Message = new Messaging.Request { Envelope = Write(evt) };
                CustomNetworkResponse<Messaging.Response> res = new CustomNetworkResponse<Messaging.Response>();
                MethodInfo m = typeof(ClientSiegeManager).GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic);
                m.Invoke(GameClient.Instance.SiegeManager, new object[] { req, res });
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Siege] push " + handler + ": " + e); }
        }

        // ------------------------------------------------------------------
        // Defense: placeholder attackers (AIs simulating players of varied skill)
        // ------------------------------------------------------------------

        /// <summary>
        /// PLACEHOLDER: how often defenses were attacked and how strong attackers were is not known. An attack
        /// comes every AttackHours on average; each attacker has a skill level, the chance to beat one defense deck.
        /// </summary>
        internal const double AttackHours = 2.0;
        internal static readonly double[] SkillWinChance = { 0.30, 0.45, 0.60, 0.75 };

        /// <summary>Plays the placeholder attacks on the player's defense since the last time, then the world's.</summary>
        private static void Simulate()
        {
            if (Saves.Current == null) return;
            DateTime now = DateTime.UtcNow;
            Store d = Data;
            siege_bits s = ActiveDefense();
            if (s != null && s_Assault == null)
            {
                System.Random rng = new System.Random();
                DateTime t = d.LastSimulation;
                while (s.CurStatus == siege_bits.Status.ACTIVE)
                {
                    t = t.AddHours(-AttackHours * Math.Log(1.0 - rng.NextDouble()));
                    if (t > now) break;
                    SimulatedAttack(s, rng, t);
                }
            }
            d.LastSimulation = now;
            SaveData();
            Keeps.Simulate(now);
        }

        private static void SimulatedAttack(siege_bits s, System.Random rng, DateTime when)
        {
            int skill = rng.Next(SkillWinChance.Length);
            string attacker = PlaceholderKeeps.RandomName(rng);
            int total, defender, pot, rake;
            Calculate(s.Pot, out total, out defender, out pot, out rake);
            // The defender's share goes straight to the account (Src.Video, rules screen).
            Earn(s.Currency == siege_bits.CurrencyType.PLATINUM, defender, 0);
            s.Pot += pot;
            s.ChallengeCount++;
            s.LastChallengeDate = when;
            siege_ledger_bits c = AddLog(s, siege_ledger_bits.SiegeAction.CHALLENGE, null, 0, attacker, when);
            c.Bet = (uint)pot; c.Fee = (uint)defender; c.Rake = (uint)rake;
            for (int i = 0; i < 3; i++)
            {
                if (rng.NextDouble() >= SkillWinChance[skill])
                {
                    siege_ledger_bits w = AddLog(s, siege_ledger_bits.SiegeAction.DEFENDED, null, 0, attacker, when);
                    w.FightSeq = i;
                    s.Wins++;
                    SetFees(s);
                    Debug.Log("[HexRevival] [Siege] defense held against " + attacker + " (skill " + skill + ") at fight " + (i + 1));
                    return;
                }
                AddLog(s, siege_ledger_bits.SiegeAction.SACKED, null, 0, attacker, when).FightSeq = i;
            }
            // Defense defeated: the attacker takes the bounty; the defender gets the Siege Sacks (Src.Video, A 0:40).
            int sacks = Game.Shared.Campaign.Utils.SiegeSackValue(s);
            s.CurStatus = siege_bits.Status.DEFEATED;
            s.StatusDate = when;
            s.CloseDate = when;
            s.Loss++;
            AddLog(s, siege_ledger_bits.SiegeAction.DEFEATED, null, 0, attacker, when);
            Earn(false, 0, sacks);
            Debug.Log("[HexRevival] [Siege] defense defeated by " + attacker + " (skill " + skill + "), +" + sacks + " sacks");
        }

        // ------------------------------------------------------------------
        // Dev command: "siege state | attack | sacks <n>"
        // ------------------------------------------------------------------

        private static string DevRequest(object request)
        {
            string result = "sent";
            OnRequest(Write(request), r => { });
            siege_bits s = ActiveDefense();
            return result + "; active defense: " + (s != null ? s.Id + " " + s.CurStatus + " " + s.Pot : "none");
        }

        public static string Command(string[] parts)
        {
            string sub = parts.Length > 1 ? parts[1] : "state";
            if (sub == "attack")
            {
                // dev: one placeholder attack on the player's defense now
                siege_bits s = ActiveDefense();
                if (s == null) return "no active defense";
                SimulatedAttack(s, new System.Random(), DateTime.UtcNow);
                SaveData();
                return "attack done: " + s.CurStatus + ", wins " + s.Wins + ", bounty " + s.Pot;
            }
            if (sub == "defend")
            {
                // dev: commits a defense of 3 prebuilt decks (as the Defense screen would), bounty 1000 gold
                // (through the client's own JSON, as the Defense screen sends it)
                Messaging.Create c = new Messaging.Create(Messaging.Create.Currency.GOLD, 1000, PlaceholderKeeps.RandomFights(new System.Random()));
                return DevRequest(c);
            }
            if (sub == "withdraw")
            {
                siege_bits s = ActiveDefense();
                return s == null ? "no active defense" : DevRequest(new Messaging.Remove(s.Id));
            }
            if (sub == "practice")
            {
                // dev: the deck builder's practice game: the player's first deck against a prebuilt defense deck
                ProfileDeckTemplate mine = Decks.ToProfileTemplate(Decks.FirstPvpDeck());
                if (mine == null) return "no deck";
                return DevRequest(new Messaging.PracticeMatch(PlaceholderKeeps.RandomFights(new System.Random())[0].Deck, mine));
            }
            if (sub == "sacks" && parts.Length > 2)
            {
                int n;
                if (!int.TryParse(parts[2], out n)) return "usage: siege sacks <n>";
                Data.Sacks = n;
                SaveData();
                PushSacks();
                return "sacks = " + n;
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("sacks ").Append(Data.Sacks).Append(", assault ").Append(s_Assault != null ? (s_Assault.Practice ? "practice" : s_Assault.Keep.Name + " fight " + (s_Assault.Seq + 1)) : "none").Append('\n');
            foreach (siege_bits s in Data.Mine) sb.Append("mine ").Append(s.Id).Append(' ').Append(s.CurStatus).Append(" bounty ").Append(s.Pot).Append(' ').Append(s.Currency).Append(" wins ").Append(s.Wins).Append('\n');
            sb.Append(Keeps.Describe());
            return sb.ToString();
        }
    }

    /// <summary>Where the defenses to attack come from.</summary>
    public interface IKeepSource
    {
        /// <summary>Defenses that can be attacked now.</summary>
        IEnumerable<siege_bits> Active();
        siege_bits Get(ulong id);
        /// <summary>An attack begins: the fee's bounty share is added, the defense leaves the list.</summary>
        void Challenged(siege_bits keep, int bountyShare);
        /// <summary>The attack failed (a defense deck won, or the attacker withdrew).</summary>
        void Defended(siege_bits keep);
        /// <summary>The attack beat the 3 decks: the defense is gone.</summary>
        void Sacked(siege_bits keep);
        /// <summary>Time passes for the other defenses (placeholder attackers).</summary>
        void Simulate(DateTime now);
        string Describe();
    }

    /// <summary>
    /// PLACEHOLDER defenses until other players' defenses can be loaded from a server: shared by every account
    /// (HexRevivalSaves/siege_world.json), each made of 3 of the game's prebuilt decks with a PvP champion, an AI
    /// personality, a bounty and a record that follow the game's rules (bounty + 37.5% of each fee). Most new
    /// defenses hold 1000 gold, a few more, some platinum (Src.Video, A 0:28-8:04: most at 1000, some 1500-93056).
    /// </summary>
    public class PlaceholderKeeps : IKeepSource
    {
        private class World
        {
            public ulong NextId = 1000000;
            public List<siege_bits> Keeps = new List<siege_bits>();
            public DateTime LastSimulation = DateTime.UtcNow;
        }

        private const int GoldKeeps = 40, PlatinumKeeps = 10;
        private World m_World;

        private static string FilePath { get { return Path.Combine(Path.Combine(LocalServer.Root, "HexRevivalSaves"), "siege_world.json"); } }

        private World Data
        {
            get
            {
                if (m_World != null) return m_World;
                try
                {
                    if (File.Exists(FilePath))
                        JsonUtils.DeserializeJson(File.ReadAllBytes(FilePath), (World w) => { m_World = w; });
                }
                catch (Exception e) { Debug.LogError("[HexRevival] [Siege] world read: " + e); }
                if (m_World == null) m_World = new World();
                // An attack interrupted by quitting the game counts as repelled (the fee is not refunded).
                foreach (siege_bits k in m_World.Keeps)
                    if (k.CurStatus == siege_bits.Status.INGAME) { k.CurStatus = siege_bits.Status.ACTIVE; k.Wins++; Siege.SetFees(k); }
                Fill(m_World, new System.Random());
                return m_World;
            }
        }

        private void Save()
        {
            try
            {
                byte[] b = null;
                JsonUtils.SerializeJson(Data, (byte[] x) => { b = x; });
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllBytes(FilePath, b);
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Siege] world save: " + e); }
        }

        public IEnumerable<siege_bits> Active()
        {
            foreach (siege_bits k in Data.Keeps) if (k.CurStatus == siege_bits.Status.ACTIVE) yield return k;
        }

        public siege_bits Get(ulong id) { return Data.Keeps.Find(k => k.Id == id); }

        public void Challenged(siege_bits keep, int bountyShare)
        {
            keep.Pot += bountyShare;
            keep.ChallengeCount++;
            keep.LastChallengeDate = DateTime.UtcNow;
            keep.CurStatus = siege_bits.Status.INGAME;
            Save();
        }

        public void Defended(siege_bits keep)
        {
            keep.Wins++;
            keep.CurStatus = siege_bits.Status.ACTIVE;
            Siege.SetFees(keep);
            Save();
        }

        public void Sacked(siege_bits keep)
        {
            Data.Keeps.Remove(keep);
            Fill(Data, new System.Random());
            Save();
        }

        /// <summary>Placeholder attackers on the other defenses too, so that the list lives (same rules as the player's defense).</summary>
        public void Simulate(DateTime now)
        {
            World w = Data;
            System.Random rng = new System.Random();
            double hours = (now - w.LastSimulation).TotalHours;
            if (hours < 0.05) return;
            foreach (siege_bits k in new List<siege_bits>(w.Keeps))
            {
                if (k.CurStatus != siege_bits.Status.ACTIVE) continue;
                double attacks = hours / Siege.AttackHours;
                int n = (int)attacks + (rng.NextDouble() < attacks - Math.Floor(attacks) ? 1 : 0);
                for (int i = 0; i < n && w.Keeps.Contains(k); i++)
                {
                    double p = Siege.SkillWinChance[rng.Next(Siege.SkillWinChance.Length)];
                    int total, defender, pot, rake;
                    Siege.Calculate(k.Pot, out total, out defender, out pot, out rake);
                    k.Pot += pot;
                    k.ChallengeCount++;
                    if (rng.NextDouble() < p * p * p) w.Keeps.Remove(k);
                    else { k.Wins++; Siege.SetFees(k); }
                }
            }
            w.LastSimulation = now;
            Fill(w, rng);
            Save();
        }

        public string Describe()
        {
            int gold = 0, plat = 0;
            foreach (siege_bits k in Data.Keeps) if (k.Currency == siege_bits.CurrencyType.PLATINUM) plat++; else gold++;
            return "placeholder defenses: " + gold + " gold, " + plat + " platinum";
        }

        // ------------------------------------------------------------------
        // Generation
        // ------------------------------------------------------------------

        private static List<DeckTemplate> s_Decks;

        /// <summary>The game's prebuilt decks with a PvP champion (what a player could field).</summary>
        private static List<DeckTemplate> Decks()
        {
            if (s_Decks != null) return s_Decks;
            s_Decks = new List<DeckTemplate>();
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            HashSet<string> seen = new HashSet<string>();
            foreach (DeckTemplate t in tm.Decks.Values)
            {
                if (t == null || !t.m_ChampionId.IsValid || string.IsNullOrEmpty(t.m_DeckName)) continue;
                ChampionTemplate ch = tm.GetChampionTemplate(t.m_ChampionId);
                if (ch == null || ch.m_ChampionType != EChampionType.PvPChampion || !ch.m_IsPlayerSelectable) continue;
                int n = 0;
                foreach (TemplateCardStack s in t.m_DeckResources) n += s.m_Count;
                if (n < 40 || !seen.Add(t.m_DeckName + "|" + t.m_ChampionId)) continue;
                s_Decks.Add(t);
            }
            Debug.Log("[HexRevival] [Siege] " + s_Decks.Count + " prebuilt decks for placeholder defenses");
            return s_Decks;
        }

        private void Fill(World w, System.Random rng)
        {
            if (Decks().Count < 3) return;
            int gold = 0, plat = 0;
            foreach (siege_bits k in w.Keeps) if (k.Currency == siege_bits.CurrencyType.PLATINUM) plat++; else gold++;
            bool changed = false;
            while (gold < GoldKeeps) { w.Keeps.Add(NewKeep(w, rng, false)); gold++; changed = true; }
            while (plat < PlatinumKeeps) { w.Keeps.Add(NewKeep(w, rng, true)); plat++; changed = true; }
            if (changed && m_World != null) Save();
        }

        private static readonly EDeckPersonality[] Personalities =
        {
            EDeckPersonality.Default, EDeckPersonality.Aggressive, EDeckPersonality.BigThreats, EDeckPersonality.BuildArmy,
            EDeckPersonality.Burn, EDeckPersonality.Bury, EDeckPersonality.Destruction, EDeckPersonality.HandAdvantage, EDeckPersonality.Reanimation
        };

        private static siege_bits NewKeep(World w, System.Random rng, bool platinum)
        {
            siege_bits k = new siege_bits();
            k.Id = w.NextId++;
            k.PlayerID = k.Id;
            k.Name = RandomName(rng);
            k.Currency = platinum ? siege_bits.CurrencyType.PLATINUM : siege_bits.CurrencyType.GOLD;
            // PLACEHOLDER distribution, shaped on the video's list: most at the minimum, a few higher.
            double r = rng.NextDouble();
            int min = platinum ? Siege.Fees.MinPotPlatinum : Siege.Fees.MinPotGold;
            k.Pot = r < 0.75 ? min : r < 0.95 ? min * (2 + rng.Next(4)) / 2 : min * (5 + rng.Next(20));
            int wins = rng.NextDouble() < 0.6 ? 0 : rng.Next(1, 9);
            for (int i = 0; i < wins; i++)
            {
                int total, defender, pot, rake;
                Siege.Calculate(k.Pot, out total, out defender, out pot, out rake);
                k.Pot += pot;
            }
            k.Wins = wins;
            k.ChallengeCount = wins;
            k.Created = DateTime.UtcNow.AddHours(-rng.NextDouble() * (2 + wins * 6));
            k.StatusDate = k.Created;
            k.CurStatus = siege_bits.Status.ACTIVE;
            Messaging.Create.FightConfig[] fights = RandomFights(rng);
            byte[] json = null;
            JsonUtils.SerializeJson(fights, (byte[] x) => { json = x; });
            k.FightInfo = Compressor.Compress(json);
            Siege.SetFees(k);
            return k;
        }

        /// <summary>3 different prebuilt decks, each with an AI personality.</summary>
        internal static Messaging.Create.FightConfig[] RandomFights(System.Random rng)
        {
            List<DeckTemplate> decks = Decks();
            List<int> picked = new List<int>();
            Messaging.Create.FightConfig[] fights = new Messaging.Create.FightConfig[3];
            for (int i = 0; i < 3; i++)
            {
                int d;
                do d = rng.Next(decks.Count); while (picked.Contains(d) && picked.Count < decks.Count);
                picked.Add(d);
                ProfileDeckTemplate t = ToProfile(decks[d]);
                t.SetPersonality(Personalities[rng.Next(Personalities.Length)]);
                fights[i] = new Messaging.Create.FightConfig { Deck = t, Message = "" };
            }
            return fights;
        }

        private static ProfileDeckTemplate ToProfile(DeckTemplate d)
        {
            ProfileDeckTemplate t = new ProfileDeckTemplate();
            t.Name = d.m_DeckName;
            t.Champ = d.m_ChampionId;
            t.Sleeve = d.m_DeckSleeve;
            foreach (TemplateCardStack s in d.m_DeckResources)
                t.AddCard(s.m_idTemplate, new List<EGemTypesNew>(), s.m_Count);
            return t;
        }

        private static readonly string[] Start = { "Ash", "Bran", "Cor", "Dra", "El", "Fen", "Gal", "Hal", "Ir", "Jor", "Kal", "Lor", "Mor", "Nar", "Or", "Pell", "Quin", "Ros", "Syl", "Tor", "Ul", "Val", "Wren", "Yar", "Zor" };
        private static readonly string[] End = { "ath", "bane", "dor", "dric", "en", "eth", "gar", "ian", "ik", "ira", "is", "mir", "na", "orn", "ra", "rik", "ros", "sa", "th", "us", "vyn", "wyn" };

        /// <summary>An invented player name (never a real player's).</summary>
        internal static string RandomName(System.Random rng)
        {
            string n = Start[rng.Next(Start.Length)] + End[rng.Next(End.Length)];
            return rng.NextDouble() < 0.3 ? n + rng.Next(2, 99) : n;
        }
    }
}
