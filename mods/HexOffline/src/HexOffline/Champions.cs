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

namespace HexOffline
{
    /// <summary>
    /// Champions de campagne du profil local : AddChampion / DeleteChampion / UpdateChampionDeckID,
    /// sauvegarde dans HexOffline.champions.txt. Sans fichier, un champion par defaut est cree.
    /// </summary>
    public static class Champions
    {
        private static readonly List<champion_bits> s_Champions = new List<champion_bits>();
        private static UID s_ReckId;

        private static string FilePath { get { return Path.Combine(LocalServer.Root, "HexOffline.champions.txt"); } }

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
                        s_Champions.Add(c);
                    }
            }
            catch (Exception e) { Debug.LogError("[HexOffline] [Champions] lecture : " + e); }
            if (s_Champions.Count == 0)
            {
                champion_bits c = New(1UL, defaultName);
                c.Race = ERace.Human; c.ChampionClass = EChampionClass.Warrior; c.Gender = EGender.Male;
                s_Champions.Add(c);
            }
            // Champion sans deck enregistre : on rattache le dernier deck PvE qui lui appartient.
            try
            {
                string decks = Path.Combine(LocalServer.Root, "HexOffline.decks.txt");
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
            catch (Exception e) { Debug.LogError("[HexOffline] [Champions] decks : " + e); }
            Debug.Log("[HexOffline] [Champions] " + s_Champions.Count + " champion(s)");
            return new List<champion_bits>(s_Champions);
        }

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
                deck_bits deck = Decks.CreateForChampion(c.Name, c.Id);
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

        /// <summary>Nouveau deck cree pour un champion (premiere sauvegarde) : il devient son deck.</summary>
        public static void SetDeck(ulong championId, ulong deckId)
        {
            champion_bits c = Find(championId);
            if (c == null || c.LastDeckID == deckId) return;
            c.LastDeckID = deckId;
            Save();
        }

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
                    sb.Append('\n');
                }
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (Exception e) { Debug.LogError("[HexOffline] [Champions] sauvegarde : " + e); }
        }
    }
}
