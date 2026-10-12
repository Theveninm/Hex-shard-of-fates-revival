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
    /// Campaign champions of the local profile: AddChampion / DeleteChampion / UpdateChampionDeckID,
    /// saved in HexRevival.champions.txt. Without a file, a default champion is created.
    /// </summary>
    public static class Champions
    {
        private static readonly List<champion_bits> s_Champions = new List<champion_bits>();
        private static UID s_ReckId;

        private static string FilePath { get { return Saves.File("champions.txt"); } }

        public static List<champion_bits> Load(UID reckId, string defaultName)
        {
            s_ReckId = reckId;
            s_Champions.Clear();
            try
            {
                if (File.Exists(FilePath))
                    foreach (string line in File.ReadAllLines(FilePath))
                    {
                        string[] p = line.Split('|');
                        if (p.Length < 10) continue;
                        champion_bits c = New(ulong.Parse(p[0]), p[1]);
                        c.Level = int.Parse(p[2]);
                        c.ChampionClass = (EChampionClass)Enum.Parse(typeof(EChampionClass), p[3]);
                        c.Race = (ERace)Enum.Parse(typeof(ERace), p[4]);
                        c.Gender = (EGender)Enum.Parse(typeof(EGender), p[5]);
                        c.OwnerChampionId = ulong.Parse(p[6]);
                        c.LastDeckID = ulong.Parse(p[7]);
                        c.DeckTemplateId = ulong.Parse(p[8]);
                        c.PetName = p[9];
                        if (p.Length > 10)
                            foreach (string t in p[10].Split(';'))
                                if (t.Length > 0) { try { c.ChampionTalents.Add(new ResourceId(t)); } catch { } }
                        if (p.Length > 11) { int xp; if (int.TryParse(p[11], out xp)) c.CurrentXP = xp; }
                        c.LastCampaignID = Campaign.HasSave(c.Id) ? c.Id : 0UL;
                        s_Champions.Add(c);
                    }
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Champions] read: " + e); }
            if (s_Champions.Count == 0)
            {
                champion_bits c = New(1UL, defaultName);
                c.Race = ERace.Human; c.ChampionClass = EChampionClass.Warrior; c.Gender = EGender.Male;
                s_Champions.Add(c);
            }
            // Champion without a saved deck: attach the last PvE deck it owns.
            try
            {
                string decks = Saves.File("decks.txt");
                if (File.Exists(decks))
                    foreach (string line in File.ReadAllLines(decks))
                    {
                        string[] p = line.Split('|');
                        if (p.Length < 4 || p[0] != "DECK") continue;
                        ulong pve; ulong id;
                        if (!ulong.TryParse(p[3], out pve) || !ulong.TryParse(p[1], out id)) continue;
                        champion_bits c = Find(pve);
                        if (c != null && c.LastDeckID == 0) c.LastDeckID = id;
                    }
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Champions] decks: " + e); }
            Debug.Log("[HexRevival] [Champions] " + s_Champions.Count + " champion(s)");
            return new List<champion_bits>(s_Champions);
        }

        /// <summary>Reloads the champions of the save in use.</summary>
        public static void Reload() { Load(s_ReckId, null); }

        private static champion_bits New(ulong id, string name)
        {
            champion_bits c = new champion_bits();
            c.Id = id;
            c.Name = name ?? "";
            c.Level = 1;
            c.ReckID = s_ReckId;
            c.PetName = "";
            c.ChampionStats = new Dictionary<EChampionStatType, int>();
            c.ChampionTalents = new List<ResourceId>();
            return c;
        }

        public static object Handle(object args)
        {
            AddChampionRequestArgs add = args as AddChampionRequestArgs;
            if (add != null)
            {
                ulong id = 1;
                foreach (champion_bits x in s_Champions) if (x.Id >= id) id = x.Id + 1;
                champion_bits c = New(id, add.ChampionName);
                c.Race = add.Race; c.ChampionClass = add.ChampionClass; c.Gender = add.Gender;
                c.OwnerChampionId = add.OwnerChampionId;
                c.PetName = add.PetName ?? "";
                // Like a new champion of the original game: it receives its race's starter deck.
                Reckoning.Game.DeckTemplate starter = null;
                try { starter = Singleton<Reckoning.Game.TemplateManager>.Instance.GetPvEStarterDeckForRace(c.Race); } catch { }
                Collection.GrantDeck(starter);
                deck_bits deck = Decks.CreateForChampion(c.Name, c.Id, starter);
                c.LastDeckID = deck.Id;
                s_Champions.Add(c);
                Save();
                AddChampionResponse r = LocalServer.Ok<AddChampionResponse>();
                r.ChampionBits = c;
                r.DeckBits = deck;
                return r;
            }
            DeleteChampionRequestArgs del = args as DeleteChampionRequestArgs;
            if (del != null)
            {
                champion_bits c = Find(del.ChampionId);
                if (c == null) return null;
                s_Champions.Remove(c);
                Save();
                DeleteChampionResponse r = LocalServer.Ok<DeleteChampionResponse>();
                r.ChampionBits = c;
                return r;
            }
            UpdateChampionTalentsRequestArgs tal = args as UpdateChampionTalentsRequestArgs;
            if (tal != null)
            {
                champion_bits c = Find(tal.ChampionId);
                if (c == null) return null;
                c.ChampionTalents = tal.Talents != null ? new List<ResourceId>(tal.Talents) : new List<ResourceId>();
                Save();
                UpdateChampionTalentsResponse r = LocalServer.Ok<UpdateChampionTalentsResponse>();
                r.ChampionBits = c;
                return r;
            }
            UpdateChampionDeckIDRequestArgs upd = args as UpdateChampionDeckIDRequestArgs;
            if (upd != null)
            {
                champion_bits c = Find(upd.ChampionID);
                if (c == null) return null;
                c.LastDeckID = upd.LastDeckID;
                c.DeckTemplateId = upd.DeckTemplateID;
                Save();
                UpdateChampionDeckIDResponse r = LocalServer.Ok<UpdateChampionDeckIDResponse>();
                r.ChampionBits = c;
                return r;
            }
            return null;
        }

        /// <summary>Deck of a champion, created from its race's starter deck if it has none.</summary>
        public static ulong EnsureDeck(ulong championId)
        {
            deck_bits created;
            return EnsureDeck(championId, out created);
        }

        /// <summary>Gives a starter deck to every champion without one; returns the decks created.</summary>
        public static List<deck_bits> EnsureDecks()
        {
            List<deck_bits> res = new List<deck_bits>();
            foreach (champion_bits c in new List<champion_bits>(s_Champions))
            {
                deck_bits created;
                EnsureDeck(c.Id, out created);
                if (created != null) res.Add(created);
            }
            return res;
        }

        private static ulong EnsureDeck(ulong championId, out deck_bits created)
        {
            created = null;
            champion_bits c = Find(championId);
            if (c == null) return 0UL;
            if (c.LastDeckID != 0) return c.LastDeckID;
            Reckoning.Game.DeckTemplate starter = null;
            try { starter = Singleton<Reckoning.Game.TemplateManager>.Instance.GetPvEStarterDeckForRace(c.Race); } catch { }
            Collection.GrantDeck(starter);
            created = Decks.CreateForChampion(c.Name, c.Id, starter);
            c.LastDeckID = created.Id;
            Save();
            Debug.Log("[HexRevival] [Champions] champion " + c.Id + " had no deck: starter deck " + c.LastDeckID + " created");
            return c.LastDeckID;
        }

        /// <summary>New deck created for a champion (first save): it becomes its deck.</summary>
        public static void SetDeck(ulong championId, ulong deckId)
        {
            champion_bits c = Find(championId);
            if (c == null || c.LastDeckID == deckId) return;
            c.LastDeckID = deckId;
            Save();
        }

        /// <summary>
        /// Adds champion XP and recomputes the level from the game's own XP table. Returns the
        /// updated champion (null if unknown); levelsGained is the number of new levels.
        /// </summary>
        public static champion_bits AddXp(ulong championId, int xp, out int levelsGained)
        {
            levelsGained = 0;
            champion_bits c = Find(championId);
            if (c == null || xp <= 0) return c;
            c.CurrentXP += xp;
            int level = Progression.ChampionLevelForXp(c.CurrentXP);
            if (level > c.Level) { levelsGained = level - c.Level; c.Level = level; }
            Save();
            return c;
        }

        public static champion_bits Get(ulong id) { return Find(id); }

        private static champion_bits Find(ulong id)
        {
            foreach (champion_bits c in s_Champions) if (c.Id == id) return c;
            return null;
        }

        private static void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (champion_bits c in s_Champions)
                {
                    sb.Append(c.Id).Append('|').Append((c.Name ?? "").Replace("|", "/")).Append('|').Append(c.Level).Append('|')
                      .Append(c.ChampionClass).Append('|').Append(c.Race).Append('|').Append(c.Gender).Append('|')
                      .Append(c.OwnerChampionId).Append('|').Append(c.LastDeckID).Append('|').Append(c.DeckTemplateId).Append('|')
                      .Append((c.PetName ?? "").Replace("|", "/")).Append('|');
                    if (c.ChampionTalents != null) foreach (ResourceId t in c.ChampionTalents) sb.Append(t).Append(';');
                    sb.Append('|').Append(c.CurrentXP);
                    sb.Append('\n');
                }
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Champions] save: " + e); }
        }
    }
}
