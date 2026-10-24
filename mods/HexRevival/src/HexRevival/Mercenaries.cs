using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Client;
using Game.Client.Campaign;
using Game.Shared;
using Game.Shared.Campaign.Messages;
using Game.Shared.Domain;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Campaign mercenaries (AZ2 rewards: Augustine, Katsuhiro, Mayor Galway, Sister of the Herd; Sin'glar at account
    /// level 9; Rosina Leckermaul in Siege Packs; all of them on "unlock everything" saves). A mercenary is
    /// an inventory item (InventoryMercenaryData); the reward window's "MERCPAC" line makes the client put it in
    /// the champion's party with its starter deck, saved as a profile deck template (see DeckTemplates). The party
    /// (ClientCampaignManager.SaveChampionParty, a server request) is saved here in parties.txt. A battle launched
    /// with a mercenary selected ("merc=" in the start event) is played by the mercenary: its deck, and its card
    /// as champion (champion_bits.MercID, the game's InstantiateMercenaryCard).
    /// </summary>
    public static class Mercenaries
    {
        private static Dictionary<ulong, CampSysGeneral.Party.ChampionParty> s_Parties;
        private static string s_LoadedFrom;

        private static string FilePath { get { return Saves.File("parties.txt"); } }

        // File: one line per champion: champId|merc item id:deck template id:upgrade;...
        private static Dictionary<ulong, CampSysGeneral.Party.ChampionParty> All
        {
            get
            {
                if (s_Parties != null && s_LoadedFrom == FilePath) return s_Parties;
                s_LoadedFrom = FilePath;
                s_Parties = new Dictionary<ulong, CampSysGeneral.Party.ChampionParty>();
                try
                {
                    if (File.Exists(FilePath))
                        foreach (string line in File.ReadAllLines(FilePath))
                        {
                            string[] p = line.Split('|');
                            ulong champ;
                            if (p.Length < 2 || !ulong.TryParse(p[0], out champ)) continue;
                            CampSysGeneral.Party.ChampionParty party = new CampSysGeneral.Party.ChampionParty { ChampionID = champ };
                            foreach (string m in p[1].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                string[] f = m.Split(':');
                                ulong deck; int up;
                                if (f.Length < 3 || !ulong.TryParse(f[1], out deck)) continue;
                                int.TryParse(f[2], out up);
                                party.Members.Add(new CampSysGeneral.Party.ChampionParty.ChampionPartyMember { Mercenary = new ResourceId(f[0]), DeckTemplate = deck, Upgrade = up });
                            }
                            s_Parties[champ] = party;
                        }
                }
                catch (Exception e) { Debug.LogError("[HexRevival] [Mercenaries] read: " + e); }
                return s_Parties;
            }
        }

        private static bool s_Pending;

        /// <summary>
        /// Login (SeedLocalProfile): party slots now; the parties themselves once the game's templates are parsed
        /// (ApplyPending), since the client drops a party member whose mercenary item it cannot resolve yet.
        /// </summary>
        public static void LoadInto(PlayerProfile profile)
        {
            // Party members count as earned mercenaries (party slots). Their decks' cards are given with them
            // (Progression), not here: the collection is not built yet at this point of the login.
            foreach (CampSysGeneral.Party.ChampionParty party in All.Values)
                foreach (CampSysGeneral.Party.ChampionParty.ChampionPartyMember m in party.Members)
                    if (Saves.Current != null) Saves.Current.Claims.Add("merc:" + m.Mercenary);
            RefreshPartyCap(profile);
            s_Pending = true;
        }

        /// <summary>
        /// The mercenaries a player can own: every mercenary item of the game data with its template, except the
        /// unfinished ones (build tag FutureRelease: "08 Kismet Merc A", Soul Broker Nicholas...). Only Augustine and
        /// Katsuhiro come with a starter deck (Game data); the others play a deck the player builds for them.
        /// The original gave them through campaign rewards, set chests, the Wheel of Fortune, the Kickstarter and
        /// events; "unlock everything" saves own them all (Inventory.Build).
        /// </summary>
        public static List<InventoryMercenaryData> Obtainable()
        {
            List<InventoryMercenaryData> res = new List<InventoryMercenaryData>();
            foreach (InventoryItemData item in Singleton<TemplateManager>.Instance.InventoryItems.Values)
            {
                InventoryMercenaryData m = item as InventoryMercenaryData;
                if (m == null || m.m_BuildTag == "FutureRelease" || m.MercenaryTemplate == null) continue;
                res.Add(m);
            }
            return res;
        }

        /// <summary>
        /// An owned mercenary with a starter deck gets it as a party deck template, if it has none: the party screen
        /// takes the mercenary's deck from the templates (PlayerProfile.FindDeckTemplateWithMerc), and without one
        /// the mercenary joins the party without a playable deck. (Campaign rewards get theirs from the client.)
        /// </summary>
        private static void EnsureStarterDecks(PlayerProfile profile)
        {
            HashSet<ResourceId> withDeck = new HashSet<ResourceId>();
            foreach (Game.Shared.Profile.ProfileDeckTemplate t in profile.DeckTemplateList.Values) withDeck.Add(t.Champ);
            int created = 0;
            foreach (InventoryMercenaryData m in Obtainable())
            {
                if (m.DeckTemplate == null || Inventory.Count(m.m_Id) == 0 || withDeck.Contains(m.MercenaryTemplate.m_Id)) continue;
                Game.Shared.Profile.ProfileDeckTemplate t = new Game.Shared.Profile.ProfileDeckTemplate();
                t.Name = m.m_Name;
                t.Champ = m.MercenaryTemplate.m_Id;
                t.Sleeve = m.DeckTemplate.m_DeckSleeve;
                foreach (TemplateCardStack c in m.DeckTemplate.m_DeckResources)
                    t.AddCard(c.m_idTemplate, new List<Game.Shared.Mechanics.EGemTypesNew>(), c.m_Count);
                DeckTemplates.Put(t, 0);
                withDeck.Add(t.Champ);
                created++;
            }
            if (created > 0) Debug.Log("[HexRevival] [Mercenaries] " + created + " starter deck(s) created");
        }

        /// <summary>Gives the client the saved parties, once per login (called when a campaign state is built).</summary>
        public static void ApplyPending()
        {
            if (!s_Pending) return;
            PlayerProfile profile = GameClient.Instance.PlayerProfile;
            if (profile == null) return;
            s_Pending = false;
            try { EnsureStarterDecks(profile); }
            catch (Exception e) { Debug.LogError("[HexRevival] [Mercenaries] starter decks: " + e); }
            foreach (KeyValuePair<ulong, CampSysGeneral.Party.ChampionParty> kv in All)
            {
                try { profile.SavedChampionParty(kv.Key, kv.Value); }
                catch (Exception e) { Debug.LogWarning("[HexRevival] [Mercenaries] party " + kv.Key + ": " + e.Message); }
            }
        }

        /// <summary>
        /// A mercenary earned: one more party slot (the game's hint: "You have just earned a mercenary and a party
        /// slot!"; the client reads the slots from the account flag CAMP_PARTYCAP and caps them by race itself).
        /// </summary>
        public static void OnEarned(ResourceId mercItem)
        {
            if (Saves.Current == null) return;
            Saves.Current.Claims.Add("merc:" + mercItem);
            Saves.SaveCurrent();
            RefreshPartyCap(GameClient.Instance.PlayerProfile);
        }

        private static void RefreshPartyCap(PlayerProfile profile)
        {
            if (Saves.Current == null || profile == null) return;
            int n = 0;
            foreach (string c in Saves.Current.Claims) if (c.StartsWith("merc:")) n++;
            if (Saves.Current.UnlockAll) n = 4;   // every mercenary owned: every slot (the client caps them by race)
            if (n > 0) profile.UpdateSingleUserFlag(new Reckoning.Profile.Messages.FlagData { Name = "CAMP_PARTYCAP", Progress = n, Maximum = 4 });
        }

        /// <summary>ClientCampaignManager.SaveChampionParty (Harmony prefix): saves the party and answers with it.</summary>
        public static void OnSaveParty(CampSysGeneral.Party.ChampionParty party, ClientCampaignManager.PartyMembersCallback callback)
        {
            if (party == null) return;
            All[party.ChampionID] = party;
            Save();
            Debug.Log("[HexRevival] [Mercenaries] party of " + party.ChampionID + ": " + party.Members.Count + " member(s)");
            if (callback != null) Campaign.Enqueue(() => callback(party.ChampionID, party));
        }

        /// <summary>
        /// The deck a mercenary plays for this champion (a player deck refilled from its party deck template, 0 if
        /// none) and its MercenaryTemplate id. mercItem: the InventoryMercenaryData id the client sent.
        /// </summary>
        public static ulong BattleDeck(ulong champ, string mercItem, out ResourceId mercTemplate)
        {
            mercTemplate = ResourceId.Invalid;
            CampSysGeneral.Party.ChampionParty party;
            ResourceId item;
            if (string.IsNullOrEmpty(mercItem) || !ResourceId.TryParse(mercItem, out item) || !item.IsValid || !All.TryGetValue(champ, out party)) return 0;
            InventoryMercenaryData data = Singleton<TemplateManager>.Instance.GetInventoryItem(item) as InventoryMercenaryData;
            CampSysGeneral.Party.ChampionParty.ChampionPartyMember member = party.Members.Find(m => m.Mercenary == item);
            if (data == null || data.MercenaryTemplate == null || member == null) return 0;
            Game.Shared.Profile.ProfileDeckTemplate t = DeckTemplates.Get(member.DeckTemplate);
            if (t == null) return 0;
            mercTemplate = data.MercenaryTemplate.Id;
            t.Champ = mercTemplate;
            return Decks.FromTemplate(champ, t);
        }

        /// <summary>True for a mercenary's template (a deck whose champion is a mercenary is played by it).</summary>
        public static bool IsMercenary(ResourceId template)
        {
            return template.IsValid && Singleton<TemplateManager>.Instance.Mercenaries.ContainsKey(template);
        }

        private static void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<ulong, CampSysGeneral.Party.ChampionParty> kv in All)
                {
                    sb.Append(kv.Key).Append('|');
                    foreach (CampSysGeneral.Party.ChampionParty.ChampionPartyMember m in kv.Value.Members)
                        sb.Append(m.Mercenary).Append(':').Append(m.DeckTemplate).Append(':').Append(m.Upgrade).Append(';');
                    sb.Append('\n');
                }
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Mercenaries] save: " + e); }
        }
    }
}
