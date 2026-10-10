using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Client.Network.Profile;
using Game.Shared;
using Game.Shared.Domain;
using Game.Shared.Mechanics;
using Game.Shared.Network.Profile;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Local profile decks: AddNewDeck / UpdateDeck / GetDeckInfo / RemoveDeck transactions,
    /// saved in HexRevival.decks.txt (cards stored by template, not by copy
    /// number: the generated collection may change from one version to the next).
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
        }

        private static readonly Dictionary<ulong, StoredDeck> s_Decks = new Dictionary<ulong, StoredDeck>();
        private static Dictionary<ulong, card_instance_bits> s_Collection = new Dictionary<ulong, card_instance_bits>();
        private static ulong s_NextId = 1;

        /// <summary>Deck to use for matches started by the mod: the last one saved.</summary>
        public static UID LastDeck = UID.Invalid;

        private static string FilePath { get { return Path.Combine(LocalServer.Root, "HexRevival.decks.txt"); } }

        // ------------------------------------------------------------------
        // Loading (at login, after the collection is generated)
        // ------------------------------------------------------------------

        public static List<deck_bits> Load(ICollection<card_instance_bits> collection)
        {
            s_Collection = new Dictionary<ulong, card_instance_bits>();
            foreach (card_instance_bits c in collection) s_Collection[c.Id] = c;
            s_Decks.Clear();
            s_NextId = 1;
            try
            {
                if (File.Exists(FilePath)) Parse(File.ReadAllLines(FilePath));
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Decks] lecture : " + e); }
            List<deck_bits> res = new List<deck_bits>();
            foreach (StoredDeck d in s_Decks.Values) { res.Add(ToBits(d)); LastDeck = new UID(UID.Type.Deck, d.Id); }
            Debug.Log("[HexRevival] [Decks] " + res.Count + " deck(s) loaded");
            return res;
        }

        // ------------------------------------------------------------------
        // Transactions
        // ------------------------------------------------------------------

        /// <summary>Empty deck attached to a campaign champion (champion creation).</summary>
        public static deck_bits CreateForChampion(string name, ulong championId)
        {
            StoredDeck d = new StoredDeck { Id = s_NextId++, Name = name ?? "", PveChampion = championId };
            s_Decks[d.Id] = d;
            Save();
            return ToBits(d);
        }

        public class DeckInfo
        {
            public UID Id;
            public string Name;
            public int Count;
            public ResourceId Champion = ResourceId.Invalid;
        }

        /// <summary>Player decks outside the campaign (PvP), for the mod's menu.</summary>
        public static List<DeckInfo> List()
        {
            List<DeckInfo> res = new List<DeckInfo>();
            foreach (StoredDeck d in s_Decks.Values)
                if (d.PveChampion == 0)
                    res.Add(new DeckInfo { Id = new UID(UID.Type.Deck, d.Id), Name = string.IsNullOrEmpty(d.Name) ? "Deck " + d.Id : d.Name, Count = d.Cards.Count, Champion = d.PvpChampion.IsValid ? d.PvpChampion : new ResourceId(GameConfig.DefaultChampionTemplateId) });
            return res;
        }

        /// <summary>Player deck converted to a deck template (so the AI can play it).</summary>
        public static Reckoning.Game.DeckTemplate ToTemplate(UID id)
        {
            StoredDeck d;
            if (!s_Decks.TryGetValue(id.GetInstanceId(), out d)) return null;
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
                StoredDeck d = new StoredDeck { Id = s_NextId++ };
                Fill(d, add.DeckName, add.PvEChampionId, add.PvPChampionId, add.DeckCardIDs, add.SideboardCardIDs, add.EquipmentIDs, add.TalentIDs, add.DeckSleeveId, add.GameboardId, add.CoinId, (int)add.Persona);
                s_Decks[d.Id] = d;
                LastDeck = new UID(UID.Type.Deck, d.Id);
                Save();
                if (d.PveChampion != 0) Champions.SetDeck(d.PveChampion, d.Id);
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
                if (!s_Decks.TryGetValue(id, out d)) { d = new StoredDeck { Id = id }; s_Decks[id] = d; if (id >= s_NextId) s_NextId = id + 1; }
                Fill(d, upd.DeckName, upd.PvEChampionId, upd.PvPChampionId, upd.DeckCardIDs, upd.SideboardCardIDs, upd.EquipmentIDs, upd.TalentIDs, upd.DeckSleeveId, upd.GameboardId, upd.CoinId, (int)upd.Persona);
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
                if (!s_Decks.TryGetValue(det.DeckID.GetInstanceId(), out d)) return null;
                deck_bits b = ToBits(d);
                if (!b.PVPChampionId.IsValid)
                    b.PVPChampionId = new ResourceId(GameConfig.DefaultChampionTemplateId);
                GetDeckDetailsResponse r = LocalServer.Ok<GetDeckDetailsResponse>();
                r.Deckbits = b;
                r.ChampionBits = null;
                r.PlayerName = "";
                return r;
            }
            GetDeckInfoRequestArgs get = args as GetDeckInfoRequestArgs;
            if (get != null)
            {
                StoredDeck d;
                if (!s_Decks.TryGetValue(get.DeckID.GetInstanceId(), out d)) return null;
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
                r.ActiveGems = new Dictionary<ulong, EGemTypesNew>();
                r.Persona = (EDeckPersonality)d.Persona;
                return r;
            }
            RemoveDeckRequestArgs rem = args as RemoveDeckRequestArgs;
            if (rem != null)
            {
                s_Decks.Remove(rem.DeckID.GetInstanceId());
                Save();
                RemoveDeckResponse r = LocalServer.Ok<RemoveDeckResponse>();
                r.DeckID = rem.DeckID;
                r.succeded = true;
                return r;
            }
            return null;
        }

        private static void Fill(StoredDeck d, string name, UID pve, ResourceId pvp, List<ulong> cards, List<ulong> side,
            List<ResourceId> equip, List<ResourceId> talents, ResourceId sleeve, ResourceId board, ResourceId coin, int persona)
        {
            d.Name = name ?? "";
            d.PveChampion = pve.GetInstanceId();
            d.PvpChampion = pvp;
            d.Sleeve = sleeve; d.Gameboard = board; d.Coin = coin; d.Persona = persona;
            d.Cards = Templates(cards);
            d.Side = Templates(side);
            d.Equipment = equip != null ? new List<ResourceId>(equip) : new List<ResourceId>();
            d.Talents = talents != null ? new List<ResourceId>(talents) : new List<ResourceId>();
        }

        private static List<ResourceId> Templates(List<ulong> ids)
        {
            List<ResourceId> res = new List<ResourceId>();
            if (ids == null) return res;
            foreach (ulong id in ids)
            {
                card_instance_bits c;
                if (id != 0 && s_Collection.TryGetValue(id, out c)) res.Add(c.TemplateID);
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
                if (pick == null) continue; // more copies than the collection has
                used[t] = skip + 1;
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
                StringBuilder sb = new StringBuilder();
                foreach (StoredDeck d in s_Decks.Values)
                {
                    sb.Append("DECK|").Append(d.Id).Append('|').Append(d.Name.Replace("|", "/")).Append('|').Append(d.PveChampion).Append('|')
                      .Append(d.PvpChampion).Append('|').Append(d.Sleeve).Append('|').Append(d.Gameboard).Append('|').Append(d.Coin).Append('|').Append(d.Persona).Append('\n');
                    sb.Append("CARDS|").Append(Join(d.Cards)).Append('\n');
                    sb.Append("SIDE|").Append(Join(d.Side)).Append('\n');
                    sb.Append("EQUIP|").Append(Join(d.Equipment)).Append('\n');
                    sb.Append("TALENT|").Append(Join(d.Talents)).Append('\n');
                }
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Decks] sauvegarde : " + e); }
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

        private static void Parse(string[] lines)
        {
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
                        s_Decks[cur.Id] = cur;
                        if (cur.Id >= s_NextId) s_NextId = cur.Id + 1;
                        break;
                    case "CARDS": if (cur != null) cur.Cards = Split(p[1]); break;
                    case "SIDE": if (cur != null) cur.Side = Split(p[1]); break;
                    case "EQUIP": if (cur != null) cur.Equipment = Split(p[1]); break;
                    case "TALENT": if (cur != null) cur.Talents = Split(p[1]); break;
                }
            }
        }
    }
}
