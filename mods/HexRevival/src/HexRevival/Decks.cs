using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Client;
using Game.Client.Network.Profile;
using Game.Shared;
using Game.Shared.Domain;
using Game.Shared.Mechanics;
using Game.Shared.Network.Profile;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Local profile decks: AddNewDeck / UpdateDeck / GetDeckInfo / RemoveDeck transactions. Cards are stored by
    /// template, not by copy number (the generated collection may change from one version to the next).
    /// - Each account has its own decks, campaign and player decks (decks.txt of the save; user's choice: a shared
    ///   deck gave every account cards outside its collection, and made accounts pointless).
    /// - The decks made for the AI are shared by every account (HexRevivalSaves/decks_common.txt): they are AI
    ///   opponents. Marking a deck "made for the AI" moves it there, unmarking it brings it back to the account.
    /// - The online account also lists every offline account's decks, campaign ones included, to play them (not to
    ///   edit them): loaded under ids of their own (ForeignBase).
    /// Non-campaign deck ids are unique across accounts (HexRevivalSaves/deck_ids.txt). Each deck has its settings as
    /// an AI opponent (AiSettings, decks_common_ai.json). A card an account does not own is shown with a copy of its own
    /// (VirtualInstance), so that the deck stays whole, and the deck builder reports it as not owned.
    /// </summary>
    public static class Decks
    {
        private class StoredDeck
        {
            public ulong Id;
            public string Name = "";
            public ulong PveChampion;
            public ResourceId PvpChampion = ResourceId.Invalid;
            public ResourceId Sleeve = ResourceId.Invalid, Gameboard = ResourceId.Invalid, Coin = ResourceId.Invalid;
            public int Persona;
            public List<ResourceId> Cards = new List<ResourceId>();
            public List<ResourceId> Side = new List<ResourceId>();
            public List<ResourceId> Equipment = new List<ResourceId>();
            public List<ResourceId> Talents = new List<ResourceId>();
            /// <summary>Socketed gems by position in Cards / Side (EGemTypesNew, several gems packed in one value).</summary>
            public Dictionary<int, ulong> Gems = new Dictionary<int, ulong>(), SideGems = new Dictionary<int, ulong>();
            /// <summary>The account that created it (see OwnerHere); "" for the decks made before owners were kept.</summary>
            public string Owner = "";
            /// <summary>Kept in decks_common.txt: a deck made for the AI, shared by every account.</summary>
            public bool Common;
            /// <summary>Another account's deck listed by the online account (played, never saved here).</summary>
            public bool Foreign;
            /// <summary>The champion of another account's campaign deck (its PvE champion).</summary>
            public champion_bits Champion;

            public StoredDeck Copy(ulong id)
            {
                StoredDeck c = (StoredDeck)MemberwiseClone();
                c.Id = id;
                c.Cards = new List<ResourceId>(Cards); c.Side = new List<ResourceId>(Side);
                c.Equipment = new List<ResourceId>(Equipment); c.Talents = new List<ResourceId>(Talents);
                c.Gems = new Dictionary<int, ulong>(Gems); c.SideGems = new Dictionary<int, ulong>(SideGems);
                return c;
            }
        }

        /// <summary>Owner of the online profile's decks (offline accounts are named by their save).</summary>
        public const string OnlineOwner = "@online";

        /// <summary>The account decks created now belong to.</summary>
        private static string OwnerHere { get { return Saves.IsOnline ? OnlineOwner : Saves.Current != null ? Saves.Current.Name : ""; } }

        private static bool IsForAI(StoredDeck d) { return HasAi(d.Id) && Ai(d.Id).ForAI; }

        /// <summary>Decks the current account lists and edits: its own and the decks made for the AI (not another account's).</summary>
        private static bool Visible(StoredDeck d) { return !d.Foreign; }

        private static bool IsCampaign(StoredDeck d) { return d.PveChampion != 0; }

        private static readonly Dictionary<ulong, StoredDeck> s_Decks = new Dictionary<ulong, StoredDeck>();
        private static Dictionary<ulong, card_instance_bits> s_Collection = new Dictionary<ulong, card_instance_bits>();
        private static ulong s_NextId = 1;

        /// <summary>Ids of the non-campaign decks start here (an account's campaign decks stay below).</summary>
        private const ulong CommonBase = 1000000;
        /// <summary>Ids given to the other accounts' decks listed by the online account.</summary>
        private const ulong ForeignBase = 7000000000000UL;
        private static ulong s_NextCommon = CommonBase;
        /// <summary>Copies shown for the cards of a shared deck the account does not own (id -> template).</summary>
        private static readonly Dictionary<ulong, ResourceId> s_Virtual = new Dictionary<ulong, ResourceId>();
        private const ulong VirtualBase = 8000000000000UL;
        private static ulong s_NextVirtual = VirtualBase;

        private static string IdsPath { get { return Path.Combine(Path.Combine(LocalServer.Root, "HexRevivalSaves"), "deck_ids.txt"); } }

        /// <summary>A deck played by the AI for the player's side (a Siege defense deck, see SetTemporary); never saved.</summary>
        private static StoredDeck s_Temporary;
        private const ulong TemporaryId = 9100000000000UL;

        /// <summary>A profile deck template (a Siege defense deck) as a player deck the match engine can load.</summary>
        public static UID SetTemporary(Game.Shared.Profile.ProfileDeckTemplate t)
        {
            StoredDeck d = new StoredDeck { Id = TemporaryId, Name = t.Name ?? "", PvpChampion = t.Champ, Sleeve = t.Sleeve, Persona = (int)t.GetPersonality() };
            AddCards(d, t);
            s_Temporary = d;
            return new UID(UID.Type.Deck, d.Id);
        }

        /// <summary>The AI settings of the shared deck a template was made from (same name and champion), or the defaults.</summary>
        public static AiSettings AiOfTemplate(Game.Shared.Profile.ProfileDeckTemplate t)
        {
            foreach (StoredDeck d in s_Decks.Values)
                if (!IsCampaign(d) && d.Name == t.Name && d.PvpChampion == t.Champ) return Ai(d.Id);
            return new AiSettings();
        }

        private static StoredDeck Find(ulong id)
        {
            StoredDeck d;
            if (s_Temporary != null && id == TemporaryId) return s_Temporary;
            if (s_Remote != null && id == RemoteId) return s_Remote;
            return s_Decks.TryGetValue(id, out d) ? d : null;
        }

        /// <summary>A new id: campaign decks count per account; the others are unique across accounts (deck_ids.txt).</summary>
        private static ulong NewId(bool common)
        {
            if (!common) return s_NextId++;
            ulong next = s_NextCommon;
            try { ulong f; if (File.Exists(IdsPath) && ulong.TryParse(File.ReadAllText(IdsPath).Trim(), out f) && f > next) next = f; } catch { }
            s_NextCommon = next + 1;
            try { File.WriteAllText(IdsPath, s_NextCommon.ToString()); } catch (Exception e) { Debug.LogWarning("[HexRevival] [Decks] ids: " + e.Message); }
            return next;
        }

        private static string CommonPath { get { return Path.Combine(Path.Combine(LocalServer.Root, "HexRevivalSaves"), "decks_common.txt"); } }

        /// <summary>Deck to use for matches started by the mod: the last one saved.</summary>
        public static UID LastDeck = UID.Invalid;

        private static string FilePath { get { return Saves.File("decks.txt"); } }

        // ------------------------------------------------------------------
        // Loading (at login, after the collection is generated)
        // ------------------------------------------------------------------

        public static List<deck_bits> Load(ICollection<card_instance_bits> collection)
        {
            s_Collection = new Dictionary<ulong, card_instance_bits>();
            foreach (card_instance_bits c in collection) s_Collection[c.Id] = c;
            s_Decks.Clear();
            s_Virtual.Clear();
            s_NextVirtual = VirtualBase;
            s_NextId = 1;
            s_NextCommon = CommonBase;
            try
            {
                if (File.Exists(CommonPath))
                    foreach (StoredDeck d in Parse(File.ReadAllLines(CommonPath))) { d.Common = true; s_Decks[d.Id] = d; }
                if (File.Exists(FilePath))
                    foreach (StoredDeck d in Parse(File.ReadAllLines(FilePath))) s_Decks[d.Id] = d;
                if (Saves.IsOnline) LoadOtherAccounts();
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Decks] read: " + e); }
            List<deck_bits> res = new List<deck_bits>();
            foreach (StoredDeck d in s_Decks.Values)
                if (Visible(d)) { res.Add(ToBits(d)); LastDeck = new UID(UID.Type.Deck, d.Id); }
            Debug.Log("[HexRevival] [Decks] " + res.Count + " deck(s) loaded");
            return res;
        }

        // ------------------------------------------------------------------
        // Transactions
        // ------------------------------------------------------------------

        /// <summary>A card given during the game (reward, starter deck) becomes usable in decks.</summary>
        public static void AddToCollection(card_instance_bits c)
        {
            s_Collection[c.Id] = c;
        }

        /// <summary>Deck of a new campaign champion, filled with a starter deck (or empty).</summary>
        public static deck_bits CreateForChampion(string name, ulong championId, Reckoning.Game.DeckTemplate starter)
        {
            StoredDeck d = new StoredDeck { Id = NewId(false), Name = name ?? "", PveChampion = championId };
            if (starter != null)
                foreach (Reckoning.Game.TemplateCardStack st in starter.m_DeckResources) AddStack(d, st);
            s_Decks[d.Id] = d;
            Save();
            return ToBits(d);
        }

        /// <summary>
        /// Playable copy of a profile deck template (the boat's or a mercenary's deck lives in the profile's deck
        /// templates, the match engine loads player decks): one stored deck per champion and template champion,
        /// refilled. Returns its id.
        /// </summary>
        public static ulong FromTemplate(ulong championId, Game.Shared.Profile.ProfileDeckTemplate t)
        {
            StoredDeck d = null;
            foreach (StoredDeck x in s_Decks.Values) if (x.PveChampion == championId && x.PvpChampion == t.Champ) { d = x; break; }
            if (d == null) { d = new StoredDeck { Id = NewId(false), PveChampion = championId }; s_Decks[d.Id] = d; }
            d.Name = t.Name ?? "";
            d.PvpChampion = t.Champ;
            d.Sleeve = t.Sleeve;
            d.Cards.Clear();
            d.Side.Clear();
            d.Gems.Clear();
            d.SideGems.Clear();
            AddCards(d, t);
            Save();
            return d.Id;
        }

        /// <summary>
        /// The player's own deck a profile deck template was made from (the client sends templates, not deck ids:
        /// Siege), matched by name and champion; UID.Invalid if none.
        /// </summary>
        public static UID FindDeck(Game.Shared.Profile.ProfileDeckTemplate t)
        {
            if (t == null) return UID.Invalid;
            StoredDeck byChamp = null;
            foreach (StoredDeck d in s_Decks.Values)
            {
                if (d.PveChampion != 0 || d.PvpChampion != t.Champ) continue;
                if (d.Name == (t.Name ?? "")) return new UID(UID.Type.Deck, d.Id);
                if (byChamp == null) byChamp = d;
            }
            return byChamp != null ? new UID(UID.Type.Deck, byChamp.Id) : UID.Invalid;
        }

        /// <summary>A new player deck made of a game deck (a deck bought in the store): cards, champion, equipment, sleeve.</summary>
        public static deck_bits CreateFromTemplate(string name, Reckoning.Game.DeckTemplate t)
        {
            StoredDeck d = new StoredDeck { Id = NewId(true), Name = name ?? t.m_DeckName ?? "", PvpChampion = t.m_ChampionId, Owner = OwnerHere };
            if (t.m_DeckSleeve.IsValid) d.Sleeve = t.m_DeckSleeve;
            foreach (Reckoning.Game.TemplateCardStack st in t.m_DeckResources) AddStack(d, st);
            foreach (ResourceId e in t.m_EquipmentIDs) if (e.IsValid) d.Equipment.Add(e);
            s_Decks[d.Id] = d;
            Save();
            return ToBits(d);
        }

        /// <summary>
        /// Dev: "deckgems": saves a deck as the deck editor does, with a gem on its first card, reloads the decks
        /// from disk and reports the gems the client would get back.
        /// </summary>
        public static string DevGemRoundTrip(ulong deckId)
        {
            StoredDeck d = Find(deckId);
            if (d == null || d.Cards.Count == 0) return "no deck " + deckId;
            deck_bits b = ToBits(d);
            Dictionary<ulong, EGemTypesNew> gems = new Dictionary<ulong, EGemTypesNew>(b.ActiveGems);
            EGemTypesNew gem = GemHelper.AddGemToGem((EGemTypesNew)1UL, 0);
            gems[b.CardsInDeck[0].Id] = gem;
            UpdateDeckRequestArgs upd = new UpdateDeckRequestArgs
            {
                DeckID = new UID(UID.Type.Deck, d.Id), DeckName = d.Name, PvEChampionId = new UID(UID.Type.Champion, d.PveChampion),
                PvPChampionId = d.PvpChampion, DeckCardIDs = Ids(b.CardsInDeck), SideboardCardIDs = Ids(b.CardsInSideboard),
                EquipmentIDs = new List<ResourceId>(d.Equipment), TalentIDs = new List<ResourceId>(d.Talents), ActiveGems = gems,
                DeckSleeveId = d.Sleeve, GameboardId = d.Gameboard, CoinId = d.Coin
            };
            Handle(0, upd);
            string file = File.ReadAllText(FilePath);
            int at = file.IndexOf("DECK|" + d.Id + "|");
            int g = at >= 0 ? file.IndexOf("GEMS|", at) : -1;
            deck_bits again = ToBits(Find(deckId));
            EGemTypesNew back;
            again.ActiveGems.TryGetValue(again.CardsInDeck[0].Id, out back);
            return "sent " + (ulong)gem + " on " + b.CardsInDeck[0].TemplateID + "; saved line: " + (g >= 0 ? file.Substring(g, Math.Min(40, file.Length - g)).Split('\n')[0] : "none")
                + "; given back: " + (ulong)back + " (" + again.ActiveGems.Count + " gem(s) in the deck)";
        }

        /// <summary>The first of the player's own (non-campaign) decks, 0 if none (dev commands).</summary>
        public static ulong FirstPvpDeck()
        {
            foreach (StoredDeck d in s_Decks.Values) if (d.PveChampion == 0 && d.PvpChampion.IsValid) return d.Id;
            return 0;
        }

        // ------------------------------------------------------------------
        // The shared decks as AI opponents (see AiConfig)
        // ------------------------------------------------------------------

        /// <summary>A shared deck's settings as an AI opponent (deck builder: "Set Personality" and "Modifiers").</summary>
        public class AiSettings
        {
            /// <summary>Made for the AI (listed apart from the decks made for players).</summary>
            public bool ForAI;
            /// <summary>The AI's default skill with this deck (EDifficulty); not used for the player's own Siege defense.</summary>
            public string Skill = "Hard";
            /// <summary>
            /// Randomness in the AI's choices: each time it values a card it lowers the value by a random 50 to 100% of
            /// this many hundredths (AICardEvaluator; the game's default 50, 0 = always its best estimate).
            /// </summary>
            public int Noise = 50;
            /// <summary>What the AI and the player start with when this deck is the opponent.</summary>
            public Opponents.Handicap AI = new Opponents.Handicap();
            public Opponents.Handicap Player = new Opponents.Handicap();

            public AiSettings Clone()
            {
                AiSettings a = (AiSettings)MemberwiseClone();
                a.AI = (AI ?? new Opponents.Handicap()).Clone();
                a.Player = (Player ?? new Opponents.Handicap()).Clone();
                return a;
            }
        }

        /// <summary>
        /// Set by the deck editor (see DeckAiPanel): the AI settings chosen for a deck not saved yet, and the deck being
        /// edited (its settings go to a copy saved with "save as"). A new shared deck saved from the editor takes them.
        /// </summary>
        public static AiSettings PendingAi;
        public static ulong EditorDeck;

        /// <summary>True when the deck has AI settings of its own (not just the defaults).</summary>
        public static bool HasAi(ulong deckId) { return AiStore.ContainsKey(deckId); }

        /// <summary>Replaces the AI settings of a deck and saves them.</summary>
        public static void SetAi(ulong deckId, AiSettings a)
        {
            // A deck saved as made for the AI stays one (shared, it has no owner any more): to play it, copy it
            // (CopyToAccount). A player deck marked for the AI becomes shared.
            StoredDeck d;
            bool found = s_Decks.TryGetValue(deckId, out d) && !d.Foreign && !IsCampaign(d);
            if (found && d.Common) a.ForAI = true;
            AiStore[deckId] = a;
            SaveAi();
            if (found && !d.Common && a.ForAI)
            {
                d.Common = true;
                Save();
            }
        }

        private static Dictionary<ulong, AiSettings> s_Ai;
        private static string AiPath { get { return Path.Combine(Path.Combine(LocalServer.Root, "HexRevivalSaves"), "decks_common_ai.json"); } }

        private static Dictionary<ulong, AiSettings> AiStore
        {
            get
            {
                if (s_Ai != null) return s_Ai;
                try { if (File.Exists(AiPath)) Game.Shared.Network.HConnect.Utils.DeserializeJson(File.ReadAllBytes(AiPath), (Dictionary<ulong, AiSettings> x) => { s_Ai = x; }); }
                catch (Exception e) { Debug.LogError("[HexRevival] [Decks] AI settings: " + e.Message); }
                if (s_Ai == null) s_Ai = new Dictionary<ulong, AiSettings>();
                return s_Ai;
            }
        }

        /// <summary>A shared deck's AI settings (created with the defaults).</summary>
        public static AiSettings Ai(ulong deckId)
        {
            AiSettings a;
            if (!AiStore.TryGetValue(deckId, out a)) return new AiSettings();   // defaults, not stored
            if (a.AI == null) a.AI = new Opponents.Handicap();
            if (a.Player == null) a.Player = new Opponents.Handicap();
            return a;
        }

        public static void SaveAi()
        {
            try
            {
                byte[] b = null;
                Game.Shared.Network.HConnect.Utils.SerializeJson(AiStore, (byte[] x) => { b = x; }, true);
                File.WriteAllBytes(AiPath, b);
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Decks] save AI settings: " + e.Message); }
        }

        public class SharedDeck
        {
            public ulong Id;
            public string Name;
            public ResourceId Champion;
            public int Count;
            public AiSettings Ai;
        }

        /// <summary>The shared decks (not the campaign ones).</summary>
        public static List<SharedDeck> Shared()
        {
            List<SharedDeck> res = new List<SharedDeck>();
            foreach (StoredDeck d in s_Decks.Values)
                if (!IsCampaign(d) && Visible(d)) res.Add(new SharedDeck { Id = d.Id, Name = d.Name, Champion = d.PvpChampion, Count = d.Cards.Count, Ai = Ai(d.Id) });
            res.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return res;
        }

        public static bool Exists(ulong id) { return Find(id) != null; }

        public static EDeckPersonality PersonalityOf(ulong id)
        {
            StoredDeck d;
            return (d = Find(id)) != null ? (EDeckPersonality)d.Persona : EDeckPersonality.Default;
        }

        /// <summary>A new shared deck copied from a deck of the game (an AI deck or an encounter's), with its settings.</summary>
        public static ulong CopyFromTemplate(Reckoning.Game.DeckTemplate t, string name, AiSettings ai, ResourceId champion = default(ResourceId))
        {
            // An encounter's deck may have no champion of its own (the encounter's champion plays it).
            StoredDeck d = new StoredDeck { Id = NewId(true), Name = name ?? t.m_DeckName ?? "", PvpChampion = t.m_ChampionId.IsValid || !champion.IsValid ? t.m_ChampionId : champion, Owner = OwnerHere };
            if (t.m_DeckSleeve.IsValid) d.Sleeve = t.m_DeckSleeve;
            foreach (Reckoning.Game.TemplateCardStack st in t.m_DeckResources) AddStack(d, st);
            foreach (ResourceId e in t.m_EquipmentIDs) if (e.IsValid) d.Equipment.Add(e);
            AiStore[d.Id] = ai ?? new AiSettings { ForAI = true };
            SaveAi();
            d.Common = AiStore[d.Id].ForAI;
            s_Decks[d.Id] = d;
            Save();
            try { GameClient.Instance.PlayerProfile.AddDeck(ToBits(d)); } catch (Exception e) { Debug.LogWarning("[HexRevival] [Decks] AddDeck: " + e.Message); }
            return d.Id;
        }

        /// <summary>Removes a shared deck (for every account).</summary>
        public static void Delete(ulong id)
        {
            StoredDeck x;
            if (!s_Decks.TryGetValue(id, out x) || x.Foreign || !s_Decks.Remove(id)) return;
            Save();
            AiStore.Remove(id);
            SaveAi();
            try { GameClient.Instance.PlayerProfile.RemoveDeck(new UID(UID.Type.Deck, id)); } catch (Exception e) { Debug.LogWarning("[HexRevival] [Decks] RemoveDeck: " + e.Message); }
        }

        /// <summary>A player deck as sent to the client (null if unknown).</summary>
        public static deck_bits Bits(ulong deckId)
        {
            StoredDeck d;
            return (d = Find(deckId)) != null ? ToBits(d) : null;
        }

        private static List<Reckoning.Game.DeckTemplate> s_PrebuiltPvp;

        /// <summary>The game's prebuilt decks with a PvP champion (what a player could field): placeholder opponents.</summary>
        public static List<Reckoning.Game.DeckTemplate> PrebuiltPvp()
        {
            if (s_PrebuiltPvp != null) return s_PrebuiltPvp;
            s_PrebuiltPvp = new List<Reckoning.Game.DeckTemplate>();
            Reckoning.Game.TemplateManager tm = Singleton<Reckoning.Game.TemplateManager>.Instance;
            HashSet<string> seen = new HashSet<string>();
            foreach (Reckoning.Game.DeckTemplate t in tm.Decks.Values)
            {
                if (t == null || !t.m_ChampionId.IsValid || string.IsNullOrEmpty(t.m_DeckName)) continue;
                Reckoning.Game.ChampionTemplate ch = tm.GetChampionTemplate(t.m_ChampionId);
                if (ch == null || ch.m_ChampionType != EChampionType.PvPChampion || !ch.m_IsPlayerSelectable) continue;
                int n = 0;
                foreach (Reckoning.Game.TemplateCardStack st in t.m_DeckResources) n += st.m_Count;
                if (n < 40 || !seen.Add(t.m_DeckName + "|" + t.m_ChampionId)) continue;
                s_PrebuiltPvp.Add(t);
            }
            Debug.Log("[HexRevival] " + s_PrebuiltPvp.Count + " prebuilt PvP decks");
            return s_PrebuiltPvp;
        }

        /// <summary>A player deck as a profile deck template (null if unknown).</summary>
        public static Game.Shared.Profile.ProfileDeckTemplate ToProfileTemplate(ulong deckId)
        {
            StoredDeck d;
            return (d = Find(deckId)) != null ? Game.Shared.Profile.ProfileDeckTemplate.FromDeckBits(ToBits(d)) : null;
        }

        /// <summary>The champion's bits with a mercenary in its place (MercID), for the match engine.</summary>
        private static champion_bits MercenaryBits(champion_bits c, ResourceId merc)
        {
            champion_bits m = new champion_bits();
            m.Id = c.Id; m.Name = c.Name; m.Level = c.Level; m.ReckID = c.ReckID;
            m.Race = c.Race; m.ChampionClass = c.ChampionClass; m.Gender = c.Gender;
            m.ChampionStats = c.ChampionStats; m.ChampionTalents = new List<ResourceId>();
            m.MercID = merc;
            return m;
        }

        public class DeckInfo
        {
            public UID Id;
            public string Name;
            public int Count;
            public ResourceId Champion = ResourceId.Invalid;
            /// <summary>Account that created it ("" unknown, OnlineOwner for the online profile).</summary>
            public string Owner = "";
            /// <summary>Made for the AI (shared by every account).</summary>
            public bool ForAI;
            /// <summary>A campaign champion's deck (another account's, listed by the online account).</summary>
            public bool Campaign;
            /// <summary>Another account's deck: played, not edited.</summary>
            public bool Foreign;
        }

        private static DeckInfo Info(StoredDeck d)
        {
            return new DeckInfo
            {
                Id = new UID(UID.Type.Deck, d.Id), Name = string.IsNullOrEmpty(d.Name) ? "Deck " + d.Id : d.Name, Count = d.Cards.Count,
                Champion = d.PvpChampion.IsValid ? d.PvpChampion : new ResourceId(GameConfig.DefaultChampionTemplateId),
                Owner = d.Owner ?? "", ForAI = d.Common, Campaign = IsCampaign(d), Foreign = d.Foreign,
            };
        }

        /// <summary>Player decks outside the campaign (PvP) that the current account lists, for the mod's menus.</summary>
        public static List<DeckInfo> List()
        {
            List<DeckInfo> res = new List<DeckInfo>();
            foreach (StoredDeck d in s_Decks.Values)
                if (d.PveChampion == 0 && Visible(d)) res.Add(Info(d));
            return res;
        }

        /// <summary>
        /// The account's player decks and the decks made for the AI, and, on the online account, every offline account's
        /// decks (campaign ones included) to play them.
        /// </summary>
        public static List<DeckInfo> ListAll()
        {
            List<DeckInfo> res = new List<DeckInfo>();
            foreach (StoredDeck d in s_Decks.Values)
                if (!IsCampaign(d) || d.Foreign) res.Add(Info(d));
            return res;
        }

        public static string NameOf(ulong id) { StoredDeck d = Find(id); return d != null ? d.Name : ""; }

        /// <summary>A saved deck made for the AI (its "made for the AI" box can no longer be unticked).</summary>
        public static bool IsAiDeck(ulong id)
        {
            StoredDeck d = Find(id);
            return d != null && d.Common;
        }

        /// <summary>
        /// A deck made for the AI copied as a player deck of the current account, if the account owns every card of it
        /// (accounts with everything unlocked and the online account do). Returns the new deck's id, 0 when cards are
        /// missing (their names in missing).
        /// </summary>
        public static ulong CopyToAccount(ulong id, List<string> missing)
        {
            StoredDeck d = Find(id);
            if (d == null) return 0;
            if (!Saves.IsOnline && Saves.Current != null && !Saves.Current.UnlockAll)
            {
                Dictionary<ResourceId, int> owned = new Dictionary<ResourceId, int>();
                foreach (card_instance_bits c in s_Collection.Values) { int n; owned.TryGetValue(c.TemplateID, out n); owned[c.TemplateID] = n + 1; }
                Dictionary<ResourceId, int> needed = new Dictionary<ResourceId, int>();
                foreach (ResourceId t in d.Cards) { int n; needed.TryGetValue(t, out n); needed[t] = n + 1; }
                foreach (ResourceId t in d.Side) { int n; needed.TryGetValue(t, out n); needed[t] = n + 1; }
                foreach (KeyValuePair<ResourceId, int> kv in needed)
                {
                    int have;
                    owned.TryGetValue(kv.Key, out have);
                    if (have >= kv.Value) continue;
                    Reckoning.Game.CardTemplate ct = Singleton<Reckoning.Game.TemplateManager>.Instance.GetCardTemplate(kv.Key);
                    missing.Add((kv.Value - have) + " x " + (ct != null ? ct.Name : kv.Key.ToString()));
                }
                if (missing.Count > 0) return 0;
            }
            StoredDeck c2 = d.Copy(NewId(true));
            c2.Common = false;
            c2.Foreign = false;
            c2.Owner = OwnerHere;
            c2.Name = d.Name;
            s_Decks[c2.Id] = c2;
            Save();
            try { GameClient.Instance.PlayerProfile.AddDeck(ToBits(c2)); } catch (Exception e) { Debug.LogWarning("[HexRevival] [Decks] AddDeck: " + e.Message); }
            return c2.Id;
        }

        // ------------------------------------------------------------------
        // A deck sent over the network (online games: the guest's deck, played on the host)
        // ------------------------------------------------------------------

        private const ulong RemoteId = 9200000000000UL;
        private static StoredDeck s_Remote;

        /// <summary>A deck as sent to the host: cards, champion, and a campaign deck's PvE champion.</summary>
        public static Newtonsoft.Json.Linq.JObject Export(UID id)
        {
            StoredDeck d = Find(id.GetInstanceId());
            Newtonsoft.Json.Linq.JObject j = new Newtonsoft.Json.Linq.JObject();
            if (d == null) return j;
            j["name"] = d.Name;
            j["champion"] = d.PvpChampion.IsValid ? d.PvpChampion.ToString() : "";
            j["sleeve"] = d.Sleeve.IsValid ? d.Sleeve.ToString() : "";
            j["cards"] = Join(d.Cards);
            j["side"] = Join(d.Side);
            j["equipment"] = Join(d.Equipment);
            j["gems"] = JoinGems(d.Gems);
            j["sidegems"] = JoinGems(d.SideGems);
            champion_bits c = d.Foreign ? d.Champion : IsCampaign(d) ? Champions.Get(d.PveChampion) : null;
            if (c != null)
            {
                Newtonsoft.Json.Linq.JObject pc = new Newtonsoft.Json.Linq.JObject();
                pc["name"] = c.Name; pc["level"] = c.Level; pc["class"] = c.ChampionClass.ToString(); pc["race"] = c.Race.ToString();
                pc["gender"] = c.Gender.ToString(); pc["talents"] = Join(c.ChampionTalents != null ? new List<ResourceId>(c.ChampionTalents) : new List<ResourceId>()); pc["pet"] = c.PetName ?? "";
                j["pve"] = pc;
            }
            return j;
        }

        /// <summary>The deck received from the other player, playable on this PC under a fixed id (see Export).</summary>
        public static UID SetRemote(Newtonsoft.Json.Linq.JObject j)
        {
            StoredDeck d = new StoredDeck { Id = RemoteId, Name = PlainName((string)j["name"]), Foreign = true };
            d.PvpChampion = Rid((string)j["champion"] ?? "");
            d.Sleeve = Rid((string)j["sleeve"] ?? "");
            d.Cards = Split((string)j["cards"] ?? "");
            d.Side = Split((string)j["side"] ?? "");
            d.Equipment = Split((string)j["equipment"] ?? "");
            d.Gems = SplitGems((string)j["gems"] ?? "");
            d.SideGems = SplitGems((string)j["sidegems"] ?? "");
            Newtonsoft.Json.Linq.JObject pc = j["pve"] as Newtonsoft.Json.Linq.JObject;
            if (pc != null)
            {
                champion_bits c = Champions.Make(RemoteId, PlainName((string)pc["name"]));
                c.Level = Math.Max(1, Math.Min(60, (int)pc["level"]));
                try { c.ChampionClass = (EChampionClass)Enum.Parse(typeof(EChampionClass), (string)pc["class"]); } catch { }
                try { c.Race = (ERace)Enum.Parse(typeof(ERace), (string)pc["race"]); } catch { }
                try { c.Gender = (EGender)Enum.Parse(typeof(EGender), (string)pc["gender"]); } catch { }
                c.ChampionTalents = Split((string)pc["talents"] ?? "");
                c.PetName = PlainName((string)pc["pet"]);
                d.Champion = c;
                d.PveChampion = RemoteId;
            }
            s_Remote = d;
            return new UID(UID.Type.Deck, RemoteId);
        }

        /// <summary>
        /// A name sent by the other player, shown by the game's labels: without their markup ([ff0000], [url=...]) and
        /// control characters, 40 characters at most.
        /// </summary>
        private static string PlainName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char ch in s)
            {
                if (ch == '[' || ch == ']' || ch == '<' || ch == '>' || char.IsControl(ch)) continue;
                sb.Append(ch);
                if (sb.Length >= 40) break;
            }
            return sb.ToString().Trim();
        }

        /// <summary>A deck as the game's deck validator reads it, with the champion of a campaign deck.</summary>
        public static deck_bits BitsForValidation(UID id, out champion_bits champion)
        {
            StoredDeck d = Find(id.GetInstanceId());
            champion = d == null ? null : d.Foreign ? d.Champion : IsCampaign(d) ? Champions.Get(d.PveChampion) : null;
            return d != null ? ToBits(d) : null;
        }

        public class DeckGroup
        {
            public string Title;
            /// <summary>The current account's own decks (editable).</summary>
            public bool Own;
            public List<DeckInfo> Decks = new List<DeckInfo>();
        }

        /// <summary>
        /// Decks (player decks, or those made for the AI) by the account that made them: the current account's first,
        /// then each offline account, then the decks made before owners were kept. Sorted by name in each group.
        /// </summary>
        public static List<DeckGroup> ByOwner(bool forAI)
        {
            Dictionary<string, DeckGroup> groups = new Dictionary<string, DeckGroup>();
            foreach (DeckInfo d in ListAll())
            {
                if (d.ForAI != forAI) continue;
                DeckGroup g;
                if (!groups.TryGetValue(d.Owner, out g)) groups[d.Owner] = g = new DeckGroup { Own = d.Owner == OwnerHere };
                g.Decks.Add(d);
            }
            List<string> owners = new List<string>(groups.Keys);
            owners.Sort((a, b) =>
            {
                int ra = a == OwnerHere ? 0 : a == "" ? 2 : 1, rb = b == OwnerHere ? 0 : b == "" ? 2 : 1;
                return ra != rb ? ra.CompareTo(rb) : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            });
            List<DeckGroup> res = new List<DeckGroup>();
            foreach (string o in owners)
            {
                DeckGroup g = groups[o];
                g.Title = o == OnlineOwner ? Loc.Get("deck_src_online") : o == "" ? Loc.Get("deck_src_unknown") : Loc.Get("deck_src_account", o);
                if (forAI) g.Title = Loc.Get("deck_src_ai", g.Title);
                g.Decks.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                res.Add(g);
            }
            return res;
        }

        /// <summary>Player deck converted to a deck template (so the AI can play it).</summary>
        public static Reckoning.Game.DeckTemplate ToTemplate(UID id)
        {
            StoredDeck d;
            if ((d = Find(id.GetInstanceId())) == null) return null;
            Reckoning.Game.DeckTemplate t = new Reckoning.Game.DeckTemplate();
            t.m_Id = new ResourceId(Guid.NewGuid());
            t.m_DeckName = d.Name;
            t.m_ChampionId = d.PvpChampion.IsValid ? d.PvpChampion : new ResourceId(GameConfig.DefaultChampionTemplateId);
            t.m_DeckSleeve = d.Sleeve;
            Dictionary<ResourceId, Reckoning.Game.TemplateCardStack> stacks = new Dictionary<ResourceId, Reckoning.Game.TemplateCardStack>();
            foreach (ResourceId c in d.Cards)
            {
                Reckoning.Game.TemplateCardStack st;
                if (!stacks.TryGetValue(c, out st))
                {
                    st = new Reckoning.Game.TemplateCardStack { m_idTemplate = c, m_Count = 0 };
                    stacks[c] = st;
                    t.m_DeckResources.Add(st);
                }
                st.m_Count++;
            }
            t.m_EquipmentIDs = new List<ResourceId>(d.Equipment);
            t.m_EquipmentIDs.RemoveAll(e => !e.IsValid);
            return t;
        }

        public static object Handle(int tx, object args)
        {
            object champ = Champions.Handle(args);
            if (champ != null) return champ;
            AddNewDeckRequestArgs add = args as AddNewDeckRequestArgs;
            if (add != null)
            {
                StoredDeck d = new StoredDeck { Id = NewId(add.PvEChampionId.GetInstanceId() == 0), Owner = OwnerHere };
                Fill(d, add.DeckName, add.PvEChampionId, add.PvPChampionId, add.DeckCardIDs, add.SideboardCardIDs, add.EquipmentIDs, add.TalentIDs, add.DeckSleeveId, add.GameboardId, add.CoinId, (int)add.Persona, add.ActiveGems);
                s_Decks[d.Id] = d;
                LastDeck = new UID(UID.Type.Deck, d.Id);
                Save();
                if (d.PveChampion != 0) Champions.SetDeck(d.PveChampion, d.Id);
                else
                {
                    // Saved from the deck editor: the AI settings chosen before the first save, or the copied deck's.
                    AiSettings ai = PendingAi ?? (EditorDeck != 0 && HasAi(EditorDeck) ? Ai(EditorDeck).Clone() : null);
                    PendingAi = null;
                    if (ai != null) SetAi(d.Id, ai);
                }
                AddNewDeckResponse r = LocalServer.Ok<AddNewDeckResponse>();
                r.DeckName = d.Name;
                r.DeckID = new UID(UID.Type.Deck, d.Id);
                r.Deckbits = ToBits(d);
                return r;
            }
            UpdateDeckRequestArgs upd = args as UpdateDeckRequestArgs;
            if (upd != null)
            {
                StoredDeck d;
                ulong id = upd.DeckID.GetInstanceId();
                if (!s_Decks.TryGetValue(id, out d)) { d = new StoredDeck { Id = id }; s_Decks[id] = d; Bump(id); }
                Fill(d, upd.DeckName, upd.PvEChampionId, upd.PvPChampionId, upd.DeckCardIDs, upd.SideboardCardIDs, upd.EquipmentIDs, upd.TalentIDs, upd.DeckSleeveId, upd.GameboardId, upd.CoinId, (int)upd.Persona, upd.ActiveGems);
                LastDeck = upd.DeckID;
                Save();
                UpdateDeckResponse r = LocalServer.Ok<UpdateDeckResponse>();
                r.DeckID = upd.DeckID;
                r.updated = true;
                return r;
            }
            GetDeckDetailsRequestArgs det = args as GetDeckDetailsRequestArgs;
            if (det != null)
            {
                // Requested by the combat engine when a match starts (player.m_DeckID).
                // Null ChampionBits: LoadPlayerDeck then takes the player's deck and PvP
                // champion (with ChampionBits, an encounter would force its own deck).
                StoredDeck d;
                deck_bits tournamentDeck = Battlegrounds.DeckFor(det.DeckID);
                if (tournamentDeck != null)
                {
                    GetDeckDetailsResponse tr = LocalServer.Ok<GetDeckDetailsResponse>();
                    tr.Deckbits = tournamentDeck;
                    tr.PlayerName = "";
                    return tr;
                }
                if ((d = Find(det.DeckID.GetInstanceId())) == null) return null;
                deck_bits b = ToBits(d);
                if (!b.PVPChampionId.IsValid)
                    b.PVPChampionId = new ResourceId(GameConfig.DefaultChampionTemplateId);
                GetDeckDetailsResponse r = LocalServer.Ok<GetDeckDetailsResponse>();
                r.Deckbits = b;
                r.ChampionBits = null;
                // Campaign battle: the champion card is built from the PvE champion (race, class,
                // level, talents: portrait and health as in the original game), unless the
                // encounter forces its own player deck.
                champion_bits pveChamp = Campaign.BattleChampion != 0 && d.PveChampion == Campaign.BattleChampion ? Champions.Get(d.PveChampion) : null;
                // Another account's campaign deck (or the other player's) played online: its PvE champion.
                if (d.Foreign && d.Champion != null && !d.PvpChampion.IsValid)
                {
                    b.PVPChampionId = ResourceId.Invalid;
                    r.ChampionBits = d.Champion;
                }
                if (pveChamp != null && !EncounterForcesPlayerDeck())
                {
                    // A mercenary's deck: the mercenary plays (its card as champion, Src.Game InstantiateMercenaryCard).
                    if (Mercenaries.IsMercenary(d.PvpChampion)) pveChamp = MercenaryBits(pveChamp, d.PvpChampion);
                    b.PVPChampionId = ResourceId.Invalid;
                    r.ChampionBits = pveChamp;
                }
                r.PlayerName = "";
                return r;
            }
            GetDeckInfoRequestArgs get = args as GetDeckInfoRequestArgs;
            if (get != null)
            {
                StoredDeck d;
                if ((d = Find(get.DeckID.GetInstanceId())) == null) return null;
                deck_bits b = ToBits(d);
                GetDeckInfoResponse r = LocalServer.Ok<GetDeckInfoResponse>();
                r.DeckName = d.Name;
                r.DeckID = get.DeckID;
                r.PvEChampionId = (d.PveChampion == 0 ? UID.Invalid : new UID(UID.Type.Champion, d.PveChampion));
                r.PvPChampionId = d.PvpChampion;
                r.DeckCardIDs = Ids(b.CardsInDeck);
                r.SideboardCardIDs = Ids(b.CardsInSideboard);
                r.EquipmentIDs = new List<ResourceId>(d.Equipment);
                r.TalentIDs = new List<ResourceId>(d.Talents);
                r.DeckSleeveId = d.Sleeve;
                r.GameboardId = d.Gameboard;
                r.CoinId = d.Coin;
                r.ActiveGems = new Dictionary<ulong, EGemTypesNew>(b.ActiveGems);
                r.Persona = (EDeckPersonality)d.Persona;
                return r;
            }
            RemoveDeckRequestArgs rem = args as RemoveDeckRequestArgs;
            if (rem != null)
            {
                StoredDeck x;
                if (s_Decks.TryGetValue(rem.DeckID.GetInstanceId(), out x) && !x.Foreign) s_Decks.Remove(x.Id);
                Save();
                if (AiStore.Remove(rem.DeckID.GetInstanceId())) SaveAi();
                RemoveDeckResponse r = LocalServer.Ok<RemoveDeckResponse>();
                r.DeckID = rem.DeckID;
                r.succeded = true;
                return r;
            }
            return null;
        }

        private static void Fill(StoredDeck d, string name, UID pve, ResourceId pvp, List<ulong> cards, List<ulong> side,
            List<ResourceId> equip, List<ResourceId> talents, ResourceId sleeve, ResourceId board, ResourceId coin, int persona,
            Dictionary<ulong, EGemTypesNew> gems)
        {
            d.Gems = GemsByPosition(cards, gems);
            d.SideGems = GemsByPosition(side, gems);
            d.Name = name ?? "";
            d.PveChampion = pve.GetInstanceId();
            d.PvpChampion = pvp;
            d.Sleeve = sleeve; d.Gameboard = board; d.Coin = coin; d.Persona = persona;
            d.Cards = Templates(cards);
            d.Side = Templates(side);
            d.Equipment = equip != null ? new List<ResourceId>(equip) : new List<ResourceId>();
            d.Talents = talents != null ? new List<ResourceId>(talents) : new List<ResourceId>();
        }

        /// <summary>The gems the client sent (by card instance), by position in the list of cards.</summary>
        private static Dictionary<int, ulong> GemsByPosition(List<ulong> ids, Dictionary<ulong, EGemTypesNew> gems)
        {
            Dictionary<int, ulong> res = new Dictionary<int, ulong>();
            if (ids == null || gems == null) return res;
            int pos = 0;
            foreach (ulong id in ids)
            {
                // Same filter as Templates: only the cards that are kept count for the positions.
                if (!(id != 0 && s_Collection.ContainsKey(id)) && !s_Virtual.ContainsKey(id)) continue;
                EGemTypesNew g;
                if (gems.TryGetValue(id, out g) && g != 0) res[pos] = (ulong)g;
                pos++;
            }
            return res;
        }

        /// <summary>A game deck's stack of copies, each with its own gems (TemplateCardStack.m_GemTypesNewList).</summary>
        private static void AddStack(StoredDeck d, Reckoning.Game.TemplateCardStack st)
        {
            for (int i = 0; i < st.m_Count; i++)
            {
                ulong g = 0;
                if (st.m_GemTypesNewList != null && i < st.m_GemTypesNewList.Count && st.m_GemTypesNewList[i] != null)
                    foreach (EGemTypesNew x in st.m_GemTypesNewList[i]) g = (ulong)GemHelper.AddGemToGem(x, (EGemTypesNew)g);
                if (g != 0) d.Gems[d.Cards.Count] = g;
                d.Cards.Add(st.m_idTemplate);
            }
        }

        /// <summary>A profile deck template's cards (a mercenary's, the ship's, a Siege defense), with their gems.</summary>
        private static void AddCards(StoredDeck d, Game.Shared.Profile.ProfileDeckTemplate t)
        {
            foreach (Game.Shared.Profile.ProfileDeckTemplate.CardDescriptor c in t.CardDesc)
            {
                ulong g = 0;
                if (c.Gems != null) foreach (ulong x in c.Gems) g = (ulong)GemHelper.AddGemToGem((EGemTypesNew)x, (EGemTypesNew)g);
                for (int i = 0; i < c.Count; i++)
                {
                    List<ResourceId> list = c.Res ? d.Side : d.Cards;
                    if (g != 0) (c.Res ? d.SideGems : d.Gems)[list.Count] = g;
                    list.Add(c.Temp);
                }
            }
        }

        /// <summary>"pos:gems,..." (gem values checked: an unknown gem would break the game's deck screens).</summary>
        private static string JoinGems(Dictionary<int, ulong> gems)
        {
            List<string> parts = new List<string>();
            foreach (KeyValuePair<int, ulong> g in gems) parts.Add(g.Key + ":" + g.Value);
            return string.Join(",", parts.ToArray());
        }

        private static Dictionary<int, ulong> SplitGems(string s)
        {
            Dictionary<int, ulong> res = new Dictionary<int, ulong>();
            if (string.IsNullOrEmpty(s)) return res;
            foreach (string part in s.Split(','))
            {
                string[] kv = part.Split(':');
                int pos; ulong g;
                if (kv.Length == 2 && int.TryParse(kv[0], out pos) && pos >= 0 && pos < 1000 && ulong.TryParse(kv[1], out g) && ValidGems(g)) res[pos] = g;
            }
            return res;
        }

        /// <summary>Every 10-bit slot of the packed value is empty or a gem the game knows.</summary>
        private static bool ValidGems(ulong g)
        {
            if (g == 0) return false;
            g &= ~(ulong)EGemTypesNew.GemFormatBit;   // marks the packed format (GemHelper.IsNewGemFormat)
            for (int i = 0; i < 6; i++)
            {
                ulong one = (g >> (10 * i)) & 1023UL;
                if (one != 0 && !Enum.IsDefined(typeof(EGemTypesNew), one)) return false;
            }
            return (g >> 60) == 0;
        }

        private static List<ResourceId> Templates(List<ulong> ids)
        {
            List<ResourceId> res = new List<ResourceId>();
            if (ids == null) return res;
            foreach (ulong id in ids)
            {
                card_instance_bits c;
                ResourceId v;
                if (id != 0 && s_Collection.TryGetValue(id, out c)) res.Add(c.TemplateID);
                else if (s_Virtual.TryGetValue(id, out v)) res.Add(v);
            }
            return res;
        }

        private static List<ulong> Ids(IList<card_instance_bits> cards)
        {
            List<ulong> res = new List<ulong>();
            foreach (card_instance_bits c in cards) res.Add(c.Id);
            return res;
        }

        /// <summary>deck_bits with copies from the collection (distinct copies per template).</summary>
        /// <summary>True when the current encounter gives the human player a deck of its own.</summary>
        private static bool EncounterForcesPlayerDeck()
        {
            try
            {
                object session = Server.CurrentSession;
                if (session == null) return false;
                System.Reflection.FieldInfo f = null;
                for (Type t = session.GetType(); t != null && f == null; t = t.BaseType)
                    f = t.GetField("m_Players", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly);
                System.Collections.IEnumerable players = f != null ? f.GetValue(session) as System.Collections.IEnumerable : null;
                if (players == null) return false;
                foreach (object o in players)
                {
                    Game.Shared.Player p = o as Game.Shared.Player;
                    if (p != null && !p.IsAI() && p.m_DeckTemplateID.IsValid) return true;
                }
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Decks] encounter deck: " + e.Message); }
            return false;
        }

        private static deck_bits ToBits(StoredDeck d)
        {
            deck_bits b = new deck_bits();
            b.Id = d.Id;
            b.DeckName = d.Name;
            b.PVEChampionId = d.PveChampion;
            b.PVPChampionId = d.PvpChampion;
            b.deck_sleeve = d.Sleeve; b.gameboard = d.Gameboard; b.Coin = d.Coin;
            b.Personality = (EDeckPersonality)d.Persona;
            b.ActiveGems = new Dictionary<ulong, EGemTypesNew>();
            Dictionary<ResourceId, int> used = new Dictionary<ResourceId, int>();
            b.CardsInDeck = Instances(d.Cards, used);
            b.CardsInSideboard = Instances(d.Side, used);
            // Gems belong to a card's place in the deck: given to the instance now standing there.
            foreach (KeyValuePair<int, ulong> g in d.Gems) if (g.Key < b.CardsInDeck.Count) b.ActiveGems[b.CardsInDeck[g.Key].Id] = (EGemTypesNew)g.Value;
            foreach (KeyValuePair<int, ulong> g in d.SideGems) if (g.Key < b.CardsInSideboard.Count) b.ActiveGems[b.CardsInSideboard[g.Key].Id] = (EGemTypesNew)g.Value;
            ResourceId[] eq = new ResourceId[6];
            for (int i = 0; i < 6; i++) eq[i] = i < d.Equipment.Count ? d.Equipment[i] : ResourceId.Invalid;
            b.equipment_1 = eq[0]; b.equipment_2 = eq[1]; b.equipment_3 = eq[2]; b.equipment_4 = eq[3]; b.equipment_5 = eq[4]; b.equipment_6 = eq[5];
            ResourceId[] ta = new ResourceId[5];
            for (int i = 0; i < 5; i++) ta[i] = i < d.Talents.Count ? d.Talents[i] : ResourceId.Invalid;
            b.talent_1 = ta[0]; b.talent_2 = ta[1]; b.talent_3 = ta[2]; b.talent_4 = ta[3]; b.talent_5 = ta[4];
            return b;
        }

        private static List<card_instance_bits> Instances(List<ResourceId> templates, Dictionary<ResourceId, int> used)
        {
            List<card_instance_bits> res = new List<card_instance_bits>();
            foreach (ResourceId t in templates)
            {
                int skip;
                used.TryGetValue(t, out skip);
                int seen = 0;
                card_instance_bits pick = null;
                foreach (card_instance_bits c in s_Collection.Values)
                {
                    if (c.TemplateID != t) continue;
                    if (seen++ == skip) { pick = c; break; }
                }
                used[t] = skip + 1;
                if (pick == null)
                {
                    // Not owned (a shared deck): a copy of its own, kept for the deck builder and the next save.
                    pick = new card_instance_bits { Id = s_NextVirtual++, TemplateID = t };
                    s_Virtual[pick.Id] = t;
                }
                res.Add(pick);
            }
            return res;
        }

        // ------------------------------------------------------------------
        // File
        // ------------------------------------------------------------------

        private static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, Serialize(false));
                File.WriteAllText(CommonPath, Serialize(true));
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Decks] save: " + e); }
        }

        private static string Serialize(bool common)
        {
                StringBuilder sb = new StringBuilder();
                List<StoredDeck> decks = new List<StoredDeck>();
                foreach (StoredDeck d in s_Decks.Values) if (!d.Foreign && d.Common == common) decks.Add(d);
                foreach (StoredDeck d in decks)
                {
                    sb.Append("DECK|").Append(d.Id).Append('|').Append(d.Name.Replace("|", "/")).Append('|').Append(d.PveChampion).Append('|')
                      .Append(d.PvpChampion).Append('|').Append(d.Sleeve).Append('|').Append(d.Gameboard).Append('|').Append(d.Coin).Append('|').Append(d.Persona).Append('|').Append((d.Owner ?? "").Replace("|", "/")).Append('\n');
                    sb.Append("CARDS|").Append(Join(d.Cards)).Append('\n');
                    sb.Append("SIDE|").Append(Join(d.Side)).Append('\n');
                    sb.Append("EQUIP|").Append(Join(d.Equipment)).Append('\n');
                    sb.Append("TALENT|").Append(Join(d.Talents)).Append('\n');
                    if (d.Gems.Count > 0) sb.Append("GEMS|").Append(JoinGems(d.Gems)).Append('\n');
                    if (d.SideGems.Count > 0) sb.Append("SIDEGEMS|").Append(JoinGems(d.SideGems)).Append('\n');
                }
                return sb.ToString();
        }

        private static void Bump(ulong id)
        {
            if (id >= CommonBase) { if (id >= s_NextCommon) s_NextCommon = id + 1; }
            else if (id >= s_NextId) s_NextId = id + 1;
        }

        /// <summary>The online account: every offline account's decks, to play them (campaign decks with their champion).</summary>
        private static void LoadOtherAccounts()
        {
            ulong next = ForeignBase;
            foreach (Saves.Info acc in Saves.List())
            {
                string dir = Saves.FolderOf(acc);
                string file = Path.Combine(dir, "decks.txt");
                if (!File.Exists(file)) continue;
                List<champion_bits> champs = Champions.ReadFile(Path.Combine(dir, "champions.txt"));
                ulong savedNext = s_NextId, savedCommon = s_NextCommon;
                foreach (StoredDeck d in Parse(File.ReadAllLines(file)))
                {
                    StoredDeck f = d.Copy(next++);
                    f.Foreign = true;
                    f.Owner = acc.Name;
                    if (IsCampaign(d)) f.Champion = champs.Find(c => c.Id == d.PveChampion);
                    s_Decks[f.Id] = f;
                }
                s_NextId = savedNext; s_NextCommon = savedCommon;   // their ids say nothing of this account's
            }
        }

        private static string Join(List<ResourceId> l)
        {
            string[] s = new string[l.Count];
            for (int i = 0; i < l.Count; i++) s[i] = l[i].ToString();
            return string.Join(",", s);
        }

        private static List<ResourceId> Split(string s)
        {
            List<ResourceId> l = new List<ResourceId>();
            foreach (string p in s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                try { l.Add(new ResourceId(p.Trim())); } catch { }
            }
            return l;
        }

        private static ResourceId Rid(string s)
        {
            try { return new ResourceId(s); } catch { return ResourceId.Invalid; }
        }

        private static List<StoredDeck> Parse(string[] lines)
        {
            List<StoredDeck> res = new List<StoredDeck>();
            StoredDeck cur = null;
            foreach (string line in lines)
            {
                string[] p = line.Split('|');
                if (p.Length < 2) continue;
                switch (p[0])
                {
                    case "DECK":
                        if (p.Length < 9) { cur = null; break; }
                        cur = new StoredDeck();
                        cur.Id = ulong.Parse(p[1]);
                        cur.Name = p[2];
                        cur.PveChampion = ulong.Parse(p[3]);
                        cur.PvpChampion = Rid(p[4]); cur.Sleeve = Rid(p[5]); cur.Gameboard = Rid(p[6]); cur.Coin = Rid(p[7]);
                        int.TryParse(p[8], out cur.Persona);
                        if (p.Length > 9) cur.Owner = p[9];
                        res.Add(cur);
                        Bump(cur.Id);
                        break;
                    case "CARDS": if (cur != null) cur.Cards = Split(p[1]); break;
                    case "SIDE": if (cur != null) cur.Side = Split(p[1]); break;
                    case "EQUIP": if (cur != null) cur.Equipment = Split(p[1]); break;
                    case "TALENT": if (cur != null) cur.Talents = Split(p[1]); break;
                    case "GEMS": if (cur != null) cur.Gems = SplitGems(p[1]); break;
                    case "SIDEGEMS": if (cur != null) cur.SideGems = SplitGems(p[1]); break;
                }
            }
            return res;
        }
    }
}
