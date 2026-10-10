using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Client;
using Game.Shared;
using Game.Shared.Domain;
using Reckoning.Game;
using UnityEngine;

namespace HexOffline
{
    /// <summary>
    /// Profil joueur local (remplace le flux de profil que le serveur envoyait a la connexion).
    ///
    /// Le client recoit TOUT le profil (cartes, decks, champions, or, objets, XP) par une seule
    /// fonction : PlayerProfile.GetUserProfileInfoResponse(GetUserProfileInfoResponse{ReckoningBits}),
    /// appelee a la fin de HandleProfileStream. On lui fournit ce paquet, construit localement.
    ///
    /// Etape 1 : collection complete generee (4 exemplaires de chaque carte possedable, une version
    /// par nom) + or. La sauvegarde des decks viendra ensuite (requetes UpdateDeck/AddNewDeck).
    /// </summary>
    public static class LocalProfile
    {
        public const int CopiesPerCard = 4;
        public const int StartingGold = 100000;

        private static PlayerProfile s_PendingProfile;
        private static UID s_PendingId;
        private static string s_PendingName;

        /// <summary>Appele a chaque image : termine un chargement differe (cartes encore en cours de lecture a la connexion).</summary>
        public static void Pump()
        {
            if (s_PendingProfile == null || !Singleton<TemplateManager>.Instance.LoadCompleted) return;
            PlayerProfile p = s_PendingProfile;
            s_PendingProfile = null;
            try { Load(p, s_PendingId, s_PendingName); }
            catch (Exception e) { Debug.LogError("[HexOffline] [Profil] chargement differe : " + e); }
        }

        public static void Load(PlayerProfile profile, UID reckId, string name)
        {
            if (!Singleton<TemplateManager>.Instance.LoadCompleted)
            {
                // Les modeles de cartes sont encore en cours de lecture (autre thread) : on attend.
                s_PendingProfile = profile; s_PendingId = reckId; s_PendingName = name;
                Debug.Log("[HexOffline] [Profil] cartes pas encore chargees : profil differe");
                return;
            }
            reckoning_bits bits = new reckoning_bits();
            bits.ReckID = reckId.GetInstanceId();
            bits.Name = name;
            bits.Gold = StartingGold;
            bits.Platinum = 0;
            bits.Level = 1;
            bits.ProfileId = 1UL;

            // Une version par nom : de preference celle d'un set "normal" (sets 1 a 9, format
            // Immortal) ; l'editeur filtre par format et ignorerait une version PvE/promo.
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
                // Shards de base : nombre illimite dans un deck, on en donne 60.
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
            Debug.Log("[HexOffline] [Profil] versions retenues : " + main + " des sets 1-9 sur " + templates);

            // Shards de base : TOUTES les versions (le jeu en a deux par couleur ; certains modes,
            // ex. la campagne, attendent une version precise), 60 exemplaires chacune.
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
            Debug.Log("[HexOffline] [Profil] versions supplementaires de shards : " + extraShards);

            // Equipements (champions PvE) : un exemplaire de chaque, dans l'inventaire.
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
            Debug.Log("[HexOffline] [Profil] equipements : " + equip);

            try { foreach (deck_bits d in Decks.Load(bits.Cards)) bits.Decks.Add(d); }
            catch (Exception e) { Debug.LogError("[HexOffline] [Profil] decks : " + e); }

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
                // La fin de la methode rejoint un salon de discussion (reseau absent) : sans
                // importance, cartes/decks/or sont deja charges a ce stade.
                Debug.Log("[HexOffline] [Profil] fin de GetUserProfileInfoResponse ignoree : " + (e.InnerException != null ? e.InnerException.Message : e.Message));
            }
            Debug.Log("[HexOffline] [Profil] profil local charge : " + templates + " cartes x" + CopiesPerCard + ", or " + StartingGold);
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
