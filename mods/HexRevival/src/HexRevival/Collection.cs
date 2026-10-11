using System;
using System.Collections.Generic;
using System.IO;
using Game.Client;
using Game.Shared;
using Game.Shared.Domain;
using Game.Shared.Resources;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Cards owned by the current save.
    ///
    /// "Unlock everything" saves own every collectible card (4 copies, one version per name).
    /// Progression saves own what is listed in collection.txt (one "template count" line per card):
    /// a starter collection when the save is created, then the cards earned by playing.
    /// Card instance ids are not stored: they are numbered again at each login.
    /// </summary>
    public static class Collection
    {
        public const int CopiesPerCard = 4;
        /// <summary>Basic shards of each kind owned from the start. Src.SteamGuide2016 ("200 of each base shard card").</summary>
        public const int BasicShardCopies = 200;

        private static ulong s_NextId = 1;
        private static UID s_Owner;
        private static readonly Dictionary<ResourceId, int> s_Counts = new Dictionary<ResourceId, int>();

        private static string FilePath { get { return Saves.File("collection.txt"); } }

        /// <summary>Card instances of the current save, for the profile sent at login.</summary>
        public static List<card_instance_bits> Build(UID owner)
        {
            s_Owner = owner;
            s_NextId = 1;
            s_Counts.Clear();
            if (Saves.Current.UnlockAll) AddEverything();
            else if (!LoadFile()) { AddStarter(); SaveFile(); }
            List<card_instance_bits> res = new List<card_instance_bits>();
            foreach (KeyValuePair<ResourceId, int> kv in s_Counts)
                for (int i = 0; i < kv.Value; i++) res.Add(NewInstance(kv.Key));
            Debug.Log("[HexRevival] [Collection] " + s_Counts.Count + " cards, " + res.Count + " copies" + (Saves.Current.UnlockAll ? " (unlock all)" : ""));
            return res;
        }

        /// <summary>
        /// Gives cards to the player during the game (rewards, starter deck of a new champion):
        /// added to the save and to the client's collection. Nothing happens on an "unlock
        /// everything" save, which already owns enough copies.
        /// </summary>
        public static void Grant(ResourceId template, int count)
        {
            if (Saves.Current == null || Saves.Current.UnlockAll || count <= 0) return;
            int n;
            s_Counts.TryGetValue(template, out n);
            s_Counts[template] = n + count;
            SaveFile();
            PlayerProfile profile = GameClient.Instance.PlayerProfile;
            for (int i = 0; i < count; i++)
            {
                card_instance_bits c = NewInstance(template);
                Decks.AddToCollection(c);
                try { profile.AddNewCardToCollection(c); }
                catch (Exception e) { Debug.LogWarning("[HexRevival] [Collection] AddNewCardToCollection: " + e.Message); }
            }
        }

        /// <summary>
        /// Same as Grant, but the client is not told: the instances are returned to be handed to
        /// it another way (campaign rewards, see Progression).
        /// </summary>
        public static List<card_instance_bits> GrantSilently(ResourceId template, int count)
        {
            List<card_instance_bits> res = new List<card_instance_bits>();
            if (Saves.Current == null || Saves.Current.UnlockAll || count <= 0) return res;
            int n;
            s_Counts.TryGetValue(template, out n);
            s_Counts[template] = n + count;
            SaveFile();
            for (int i = 0; i < count; i++)
            {
                card_instance_bits c = NewInstance(template);
                Decks.AddToCollection(c);
                res.Add(c);
            }
            return res;
        }

        /// <summary>Gives the cards of a deck template (missing copies only on progression saves).</summary>
        public static void GrantDeck(DeckTemplate deck)
        {
            if (deck == null) return;
            foreach (TemplateCardStack s in deck.m_DeckResources)
            {
                int owned;
                s_Counts.TryGetValue(s.m_idTemplate, out owned);
                Grant(s.m_idTemplate, s.m_Count - owned);
            }
        }

        private static card_instance_bits NewInstance(ResourceId template)
        {
            card_instance_bits c = new card_instance_bits();
            c.Id = s_NextId++;
            c.TemplateID = template;
            c.CardStats = new Dictionary<ECardStatType, int>();
            c.OwnerReckId = s_Owner.GetInstanceId();
            c.EscrowStatus = "";
            return c;
        }

        private static bool IsBasicShard(CardTemplate t)
        {
            try { return t != null && t.IsBasicResource() && t.IsOwnable(); } catch { return false; }
        }

        /// <summary>Basic shards: all versions (the game has two per color; some modes, e.g. the
        /// campaign, expect a specific version), in unlimited quantity in practice.</summary>
        private static void AddBasicShards()
        {
            foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
                if (IsBasicShard(t)) s_Counts[t.m_Id] = BasicShardCopies;
        }

        /// <summary>Starter collection: the game's starter decks for new players, plus basic shards.</summary>
        private static void AddStarter()
        {
            AddBasicShards();
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            foreach (ResourceId id in BuiltInResources.AdditionalStarterDecks)
            {
                DeckTemplate d;
                if (!tm.Decks.TryGetValue(id, out d) || d == null) continue;
                foreach (TemplateCardStack s in d.m_DeckResources)
                {
                    int n;
                    s_Counts.TryGetValue(s.m_idTemplate, out n);
                    if (!IsBasicShard(tm.GetCardTemplate(s.m_idTemplate))) s_Counts[s.m_idTemplate] = n + s.m_Count;
                }
            }
        }

        /// <summary>
        /// Every collectible card, one version per name: preferably the one from a "normal" set
        /// (sets 1 to 9, Immortal format), since the deck editor filters by format and would ignore a
        /// PvE or promo version.
        /// </summary>
        private static void AddEverything()
        {
            HashSet<ResourceId> mainSets = new HashSet<ResourceId>(BuiltInResources.Format_Immortal);
            Dictionary<string, CardTemplate> byName = new Dictionary<string, CardTemplate>();
            foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
            {
                if (t == null || string.IsNullOrEmpty(t.m_Name)) continue;
                bool ownable;
                try { ownable = t.IsOwnable(); } catch { ownable = false; }
                if (!ownable) continue;
                CardTemplate cur;
                if (!byName.TryGetValue(t.m_Name, out cur)
                    || (!mainSets.Contains(cur.m_SetId) && mainSets.Contains(t.m_SetId))
                    || (mainSets.Contains(cur.m_SetId) == mainSets.Contains(t.m_SetId) && string.CompareOrdinal(t.m_Id.ToString(), cur.m_Id.ToString()) < 0))
                    byName[t.m_Name] = t;
            }
            foreach (CardTemplate t in byName.Values) s_Counts[t.m_Id] = CopiesPerCard;
            AddBasicShards();
        }

        private static bool LoadFile()
        {
            if (!File.Exists(FilePath)) return false;
            try
            {
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] p = line.Split(' ');
                    int n;
                    if (p.Length == 2 && int.TryParse(p[1], out n) && n > 0) s_Counts[new ResourceId(p[0])] = n;
                }
                return true;
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Collection] read: " + e); return false; }
        }

        private static void SaveFile()
        {
            try
            {
                List<string> lines = new List<string>();
                foreach (KeyValuePair<ResourceId, int> kv in s_Counts) lines.Add(kv.Key + " " + kv.Value);
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Collection] save: " + e); }
        }
    }
}
