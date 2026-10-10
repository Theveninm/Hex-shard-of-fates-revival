using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Client;
using Game.Shared;
using Game.Shared.Domain;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Local player profile (replaces the profile stream the server sent at login).
    ///
    /// The client receives the WHOLE profile (cards, decks, champions, gold, items, XP) through a single
    /// function: PlayerProfile.GetUserProfileInfoResponse(GetUserProfileInfoResponse{ReckoningBits}),
    /// called at the end of HandleProfileStream. We give it that packet, built locally.
    ///
    /// Step 1: generated full collection (4 copies of every collectible card, one version
    /// per name) + gold. Deck saving comes next (UpdateDeck/AddNewDeck requests).
    /// </summary>
    public static class LocalProfile
    {
        public const int CopiesPerCard = 4;
        public const int StartingGold = 100000;

        private static PlayerProfile s_PendingProfile;
        private static UID s_PendingId;
        private static string s_PendingName;

        /// <summary>Called every frame: finishes a deferred load (cards still being read at login).</summary>
        public static void Pump()
        {
            if (s_PendingProfile == null || !Singleton<TemplateManager>.Instance.LoadCompleted) return;
            PlayerProfile p = s_PendingProfile;
            s_PendingProfile = null;
            try { Load(p, s_PendingId, s_PendingName); }
            catch (Exception e) { Debug.LogError("[HexRevival] [Profile] deferred load: " + e); }
        }

        public static void Load(PlayerProfile profile, UID reckId, string name)
        {
            if (!Singleton<TemplateManager>.Instance.LoadCompleted)
            {
                // Card templates are still being read (another thread): wait.
                s_PendingProfile = profile; s_PendingId = reckId; s_PendingName = name;
                Debug.Log("[HexRevival] [Profile] cards not loaded yet: profile deferred");
                return;
            }
            reckoning_bits bits = new reckoning_bits();
            bits.ReckID = reckId.GetInstanceId();
            bits.Name = name;
            bits.Gold = StartingGold;
            bits.Platinum = 0;
            bits.Level = 1;
            bits.ProfileId = 1UL;

            // One version per name: preferably the one from a "normal" set (sets 1 to 9, Immortal
            // format); the editor filters by format and would ignore a PvE/promo version.
            HashSet<ResourceId> mainSets = new HashSet<ResourceId>(Game.Shared.Resources.BuiltInResources.Format_Immortal);
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
            int templates = 0, main = 0;
            ulong nextId = 1;
            List<string> names = new List<string>(byName.Keys);
            names.Sort(string.CompareOrdinal);
            foreach (string n in names)
            {
                CardTemplate t = byName[n];
                templates++;
                if (mainSets.Contains(t.m_SetId)) main++;
                // Basic shards: unlimited in a deck, we give 60.
                int copies = CopiesPerCard;
                try { if (t.IsBasicResource()) copies = 60; } catch { }
                for (int i = 0; i < copies; i++)
                {
                    card_instance_bits c = new card_instance_bits();
                    c.Id = nextId++;
                    c.TemplateID = t.m_Id;
                    c.CardStats = new Dictionary<ECardStatType, int>();
                    c.OwnerReckId = reckId.GetInstanceId();
                    c.EscrowStatus = "";
                    bits.Cards.Add(c);
                }
            }
            Debug.Log("[HexRevival] [Profile] versions kept: " + main + " from sets 1-9 out of " + templates);

            // Basic shards: ALL versions (the game has two per color; some modes,
            // e.g. the campaign, expect a specific version), 60 copies each.
            HashSet<ResourceId> given = new HashSet<ResourceId>();
            foreach (CardTemplate t in byName.Values) given.Add(t.m_Id);
            int extraShards = 0;
            foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
            {
                bool basic;
                try { basic = t != null && t.IsBasicResource() && t.IsOwnable(); } catch { basic = false; }
                if (!basic || given.Contains(t.m_Id)) continue;
                given.Add(t.m_Id);
                extraShards++;
                for (int i = 0; i < 60; i++)
                {
                    card_instance_bits c = new card_instance_bits();
                    c.Id = nextId++;
                    c.TemplateID = t.m_Id;
                    c.CardStats = new Dictionary<ECardStatType, int>();
                    c.OwnerReckId = reckId.GetInstanceId();
                    c.EscrowStatus = "";
                    bits.Cards.Add(c);
                }
            }
            Debug.Log("[HexRevival] [Profile] extra shard versions: " + extraShards);

            // Equipment (PvE champions): one copy of each, in the inventory.
            int equip = 0;
            ulong itemId = 1;
            foreach (InventoryItemData item in Singleton<TemplateManager>.Instance.InventoryItems.Values)
            {
                if (!(item is InventoryEquipmentData)) continue;
                inventory_bits inv = new inventory_bits();
                inv.Id = itemId++;
                inv.TemplateID = item.m_Id;
                inv.ItemQuantity = 1;
                inv.ClaimDate = DateTime.MinValue;
                inv.EscrowStatus = "";
                inv.OwnerReckId = reckId.GetInstanceId();
                bits.InventoryIds.Add(inv);
                equip++;
            }
            Debug.Log("[HexRevival] [Profile] equipements : " + equip);

            try { foreach (deck_bits d in Decks.Load(bits.Cards)) bits.Decks.Add(d); }
            catch (Exception e) { Debug.LogError("[HexRevival] [Profile] decks : " + e); }

            Type respType = typeof(PlayerProfile).Assembly.GetType("Game.Client.Network.Profile.GetUserProfileInfoResponse");
            object resp = Activator.CreateInstance(respType);
            SetMember(resp, "ReckoningBits", bits);
            SetMember(resp, "Keep", new KeepInfo());
            MethodInfo m = typeof(PlayerProfile).GetMethod("GetUserProfileInfoResponse", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { respType }, null);
            try
            {
                m.Invoke(profile, new object[] { resp });
            }
            catch (TargetInvocationException e)
            {
                // The end of the method joins a chat room (no network): irrelevant,
                // cards/decks/gold are already loaded at this point.
                Debug.Log("[HexRevival] [Profile] end of GetUserProfileInfoResponse ignored: " + (e.InnerException != null ? e.InnerException.Message : e.Message));
            }
            Debug.Log("[HexRevival] [Profile] local profile loaded: " + templates + " cards x" + CopiesPerCard + ", gold " + StartingGold);
        }

        private static void SetMember(object target, string name, object value)
        {
            Type t = target.GetType();
            PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanWrite) { p.SetValue(target, value, null); return; }
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) { f.SetValue(target, value); return; }
            throw new MissingMemberException(t.Name + "." + name);
        }
    }
}
