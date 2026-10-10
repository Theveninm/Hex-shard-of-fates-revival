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
    /// Cards, gold, decks and champions come from the current save (see Saves, Collection, Decks).
    /// </summary>
    public static class LocalProfile
    {
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
            bits.Gold = (int)Saves.Current.Gold;
            bits.Platinum = (int)Saves.Current.Platinum;
            bits.Level = 1;
            bits.ProfileId = 1UL;

            foreach (card_instance_bits c in Collection.Build(reckId)) bits.Cards.Add(c);

            // Equipment (PvE champions): one copy of each, in the inventory ("unlock everything" saves).
            int equip = 0;
            ulong itemId = 1;
            foreach (InventoryItemData item in Singleton<TemplateManager>.Instance.InventoryItems.Values)
            {
                if (!(item is InventoryEquipmentData) || !Saves.Current.UnlockAll) continue;
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
            Debug.Log("[HexRevival] [Profile] equipment: " + equip);

            try { foreach (deck_bits d in Decks.Load(bits.Cards)) bits.Decks.Add(d); }
            catch (Exception e) { Debug.LogError("[HexRevival] [Profile] decks: " + e); }

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
            Debug.Log("[HexRevival] [Profile] local profile loaded: account '" + Saves.Current.Name + "', " + bits.Cards.Count + " cards, gold " + bits.Gold);
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
