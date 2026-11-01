using System;
using System.Collections.Generic;
using System.IO;
using Game.Client;
using Game.Shared;
using Game.Shared.Domain;
using Game.Shared.Mechanics;
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
    /// Card instance ids are not stored: they are numbered again at each login. How many copies of a card have their
    /// extended art (unlocked with stardust, see ExtendedArt) is kept apart, in collection_extended.txt ("template
    /// count" lines too), so that collection.txt stays readable by older builds.
    /// </summary>
    public static class Collection
    {
        public const int CopiesPerCard = 4;
        /// <summary>Basic shards of each kind owned from the start. Src.SteamGuide2016 ("200 of each base shard card").</summary>
        public const int BasicShardCopies = 200;

        private static ulong s_NextId = 1;
        private static UID s_Owner;
        private static readonly Dictionary<ResourceId, int> s_Counts = new Dictionary<ResourceId, int>();
        /// <summary>Copies with their extended art, per card (at most its count).</summary>
        private static readonly Dictionary<ResourceId, int> s_Extended = new Dictionary<ResourceId, int>();
        /// <summary>The instances handed out since the login, by instance id.</summary>
        private static readonly Dictionary<ulong, card_instance_bits> s_Instances = new Dictionary<ulong, card_instance_bits>();

        private static string FilePath { get { return Saves.File("collection.txt"); } }
        private static string ExtendedPath { get { return Saves.File("collection_extended.txt"); } }

        /// <summary>Card instances of the current save, for the profile sent at login.</summary>
        public static List<card_instance_bits> Build(UID owner)
        {
            s_Owner = owner;
            s_NextId = 1;
            s_Counts.Clear();
            s_Extended.Clear();
            s_Instances.Clear();
            if (Saves.Current.UnlockAll) AddEverything();
            else if (!LoadFile()) { AddStarter(); SaveFile(); }
            ReadCounts(ExtendedPath, s_Extended);
            List<card_instance_bits> res = new List<card_instance_bits>();
            foreach (KeyValuePair<ResourceId, int> kv in s_Counts)
            {
                int ext = ExtendedCount(kv.Key);
                for (int i = 0; i < kv.Value; i++)
                {
                    card_instance_bits c = NewInstance(kv.Key);
                    c.IsExtended = i < ext;
                    res.Add(c);
                }
            }
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
            Add(template, count);
            SaveFile();
        }

        /// <summary>Grant for many cards at once, saved once (redeem codes with every PvP card, see Redeem; dev "craft dupall").</summary>
        public static void Grant(IDictionary<ResourceId, int> cards)
        {
            if (Saves.Current == null || Saves.Current.UnlockAll) return;
            foreach (KeyValuePair<ResourceId, int> kv in cards) if (kv.Value > 0) Add(kv.Key, kv.Value);
            SaveFile();
        }

        private static void Add(ResourceId template, int count)
        {
            int n;
            s_Counts.TryGetValue(template, out n);
            s_Counts[template] = n + count;
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

        /// <summary>An instance handed out since the login (null if unknown or taken away).</summary>
        public static card_instance_bits Instance(ulong id)
        {
            card_instance_bits c;
            return s_Instances.TryGetValue(id, out c) ? c : null;
        }

        /// <summary>Copies of a card with their extended art (at most its count).</summary>
        public static int ExtendedCount(ResourceId template)
        {
            int n;
            return s_Extended.TryGetValue(template, out n) ? Math.Min(n, Count(template)) : 0;
        }

        /// <summary>One copy gets its extended art (saved); false if it is unknown or has it already.</summary>
        public static bool MarkExtended(ulong id)
        {
            card_instance_bits c = Instance(id);
            if (c == null || c.IsExtended || Saves.Current == null) return false;
            c.IsExtended = true;
            int n;
            s_Extended.TryGetValue(c.TemplateID, out n);
            s_Extended[c.TemplateID] = n + 1;
            WriteCounts(ExtendedPath, s_Extended);
            return true;
        }

        /// <summary>
        /// Takes n copies of a card from the save and the client (cards broken down, see Crafting): copies without
        /// extended art first, the last ones handed out first (the decks take the first ones). Returns how many.
        /// </summary>
        public static int Take(ResourceId template, int n, bool save = true)
        {
            if (Saves.Current == null || Saves.Current.UnlockAll || n <= 0) return 0;
            List<card_instance_bits> plain = new List<card_instance_bits>(), extended = new List<card_instance_bits>();
            foreach (card_instance_bits c in s_Instances.Values) if (c.TemplateID == template) (c.IsExtended ? extended : plain).Add(c);
            plain.Reverse();
            extended.Reverse();
            List<card_instance_bits> copies = plain;
            copies.AddRange(extended);
            n = Math.Min(n, Math.Min(copies.Count, Count(template)));
            PlayerProfile profile = GameClient.Instance.PlayerProfile;
            int ext = 0;
            for (int i = 0; i < n; i++)
            {
                card_instance_bits c = copies[i];
                if (c.IsExtended) ext++;
                s_Instances.Remove(c.Id);
                Decks.RemoveFromCollection(c.Id);
                try { profile.RemoveCardFromCollection(c.CardId.UID); }
                catch (Exception e) { Debug.LogWarning("[HexRevival] [Collection] RemoveCardFromCollection: " + e.Message); }
            }
            int left = Count(template) - n;
            if (left > 0) s_Counts[template] = left; else s_Counts.Remove(template);
            if (ext > 0)
            {
                int e = ExtendedCount(template) - ext;
                if (e > 0) s_Extended[template] = e; else s_Extended.Remove(template);
                WriteCounts(ExtendedPath, s_Extended);
            }
            if (save) SaveFile();
            return n;
        }

        /// <summary>The cards the save owns (templates).</summary>
        public static List<ResourceId> Owned() { return new List<ResourceId>(s_Counts.Keys); }

        /// <summary>Copies of a card the save owns (4 on "unlock everything" saves).</summary>
        public static int Count(ResourceId template)
        {
            if (Saves.Current != null && Saves.Current.UnlockAll) return 4;
            int n;
            return s_Counts.TryGetValue(template, out n) ? n : 0;
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
            s_Instances[c.Id] = c;
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
        /// Every collectible card: all versions from the "normal" sets (sets 1 to 9, Immortal format),
        /// since the deck editor filters by format and would ignore a PvE or promo version. A card
        /// with another art is its own template in the same set (e.g. Vampire Prince, Herofall), so
        /// every version of a name is taken, not one per name (mornox, 2026-11-01: fully unlocked
        /// accounts got only one of the two arts, the one with the lower id). A name absent from
        /// those sets (core commons, PvE cards) comes from one set only, with all its versions there:
        /// the core commons are reprinted in two sets.
        /// </summary>
        private static void AddEverything()
        {
            HashSet<ResourceId> mainSets = new HashSet<ResourceId>(BuiltInResources.Format_Immortal);
            HashSet<string> inMain = new HashSet<string>();
            Dictionary<string, CardTemplate> byName = new Dictionary<string, CardTemplate>();
            List<CardTemplate> others = new List<CardTemplate>();
            int main = 0, extra = 0;
            foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
            {
                if (t == null || string.IsNullOrEmpty(t.m_Name)) continue;
                bool ownable;
                try { ownable = t.IsOwnable(); } catch { ownable = false; }
                if (!ownable) continue;
                if (mainSets.Contains(t.m_SetId)) { s_Counts[t.m_Id] = CopiesPerCard; inMain.Add(t.m_Name); main++; continue; }
                others.Add(t);
                CardTemplate cur;
                if (!byName.TryGetValue(t.m_Name, out cur) || string.CompareOrdinal(t.m_Id.ToString(), cur.m_Id.ToString()) < 0) byName[t.m_Name] = t;
            }
            foreach (CardTemplate t in others)
            {
                if (inMain.Contains(t.m_Name) || byName[t.m_Name].m_SetId != t.m_SetId) continue;
                s_Counts[t.m_Id] = CopiesPerCard;
                extra++;
            }
            // The alternate arts too (sets "Set09_PvP_alternates"..., Src.Game): the same cards with another picture, which
            // the Siege Pack, the Cosmic Coin Pack and account levels give (mornox, 2026-10-30). The game lists their sets as
            // not ownable (BuiltInResources.NotOwnable_Sets) and flags every alternate as a modified card
            // (m_EquipmentModifiedCard): the other rules of CardTemplate.IsOwnable are checked here. The talent cards'
            // alternates (PvE_Talents) are champion talents, not collectibles: left out.
            var sets = Singleton<TemplateManager>.Instance.CardSets;
            int alternates = 0;
            foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
            {
                CardSetTemplate set;
                if (t == null || s_Counts.ContainsKey(t.m_Id) || !sets.TryGetValue(t.m_SetId, out set) || set == null || set.m_Name == null
                    || set.m_Name.IndexOf("alternates", StringComparison.OrdinalIgnoreCase) < 0 || set.m_Name.IndexOf("Talents", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                bool collectible;
                try { collectible = (t.CardRarity > ERarity.Land || t.IsBasicResource()) && t.m_ImplStage == ECardImplStage.Done && !t.IsChoiceCard(); }
                catch { collectible = false; }
                if (!collectible) continue;
                s_Counts[t.m_Id] = CopiesPerCard;
                alternates++;
            }
            Debug.Log("[HexRevival] [Collection] unlock all: " + main + " cards of the Immortal sets, " + extra + " from other sets, " + alternates + " alternate arts");
            AddBasicShards();
        }

        private static bool LoadFile()
        {
            return File.Exists(FilePath) && ReadCounts(FilePath, s_Counts);
        }

        /// <summary>Writes the collection (after Take with save false).</summary>
        public static void Save() { SaveFile(); }

        private static void SaveFile()
        {
            WriteCounts(FilePath, s_Counts);
        }

        private static bool ReadCounts(string path, Dictionary<ResourceId, int> into)
        {
            if (!File.Exists(path)) return false;
            try
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] p = line.Split(' ');
                    int n;
                    if (p.Length == 2 && int.TryParse(p[1], out n) && n > 0) into[new ResourceId(p[0])] = n;
                }
                return true;
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Collection] read " + Path.GetFileName(path) + ": " + e); return false; }
        }

        private static void WriteCounts(string path, Dictionary<ResourceId, int> counts)
        {
            try
            {
                List<string> lines = new List<string>();
                foreach (KeyValuePair<ResourceId, int> kv in counts) lines.Add(kv.Key + " " + kv.Value);
                File.WriteAllLines(path, lines.ToArray());
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Collection] save " + Path.GetFileName(path) + ": " + e); }
        }
    }
}
