using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Game.Shared;
using Game.Shared.Mechanics;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Equipment on alternate art cards (Stigro: War Machinist's alternate art kept its text with Tinkerer's Robe).
    /// A card's equipment versions are listed in its template's rule data (AlternateVersions, each with its
    /// RequiredEquipment); the game files list them on the base card only, not on its alternate art. The game picks the
    /// version in two places: CardTemplate.GetEquippedCardTemplate (deck editor, card text) and
    /// Session.CheckForAlternateCardTemplate (battle). For an alternate art without versions, both now take the base
    /// card's version and give it the alternate art's picture: a copy of the equipped template, registered under an id
    /// made from both ids (the same on every PC, for online games).
    /// </summary>
    public static class AltArtEquipment
    {
        private static Dictionary<string, CardTemplate> s_Bases;
        private static readonly Dictionary<string, CardTemplate> s_Copies = new Dictionary<string, CardTemplate>();

        /// <summary>Postfix of both lookups: the equipped version of an alternate art, from its base card's.</summary>
        public static CardTemplate Equipped(CardTemplate asked, CardTemplate found, Func<CardTemplate, CardTemplate> lookup)
        {
            try
            {
                if (asked == null || found != asked || !asked.m_HasAlternateArt || HasVersions(asked)) return found;
                CardTemplate b = Base(asked);
                if (b == null) return found;
                CardTemplate equipped = lookup(b);
                return equipped == null || equipped == b ? found : Copy(equipped, asked);
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [AltArtEquipment] " + e); return found; }
        }

        private static bool HasVersions(CardTemplate t)
        {
            TACList l = t.tac != null ? t.tac.Get(ListAttrs.AlternateVersions) : null;
            return l != null && l.Count > 0;
        }

        /// <summary>The card with the same name that has equipment versions and is neither an alternate art nor an equipped version.</summary>
        private static CardTemplate Base(CardTemplate alt)
        {
            if (s_Bases == null)
            {
                s_Bases = new Dictionary<string, CardTemplate>();
                foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
                    if (t != null && !t.m_HasAlternateArt && !t.m_EquipmentModifiedCard && HasVersions(t) && !s_Bases.ContainsKey(t.m_Name))
                        s_Bases[t.m_Name] = t;
            }
            CardTemplate b;
            return s_Bases.TryGetValue(alt.m_Name, out b) ? b : null;
        }

        /// <summary>The equipped template with the alternate art's picture and frame.</summary>
        private static CardTemplate Copy(CardTemplate equipped, CardTemplate alt)
        {
            string key = equipped.m_Id.m_Guid + "|" + alt.m_Id.m_Guid;
            CardTemplate c;
            if (s_Copies.TryGetValue(key, out c)) return c;
            c = (CardTemplate)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(equipped, null);
            using (MD5 md5 = MD5.Create())
                c.m_Id = new ResourceId(new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes("HexRevival.AltArtEquipment|" + key))));
            c.m_CardImagePath = alt.m_CardImagePath;
            c.m_ArtNumber = alt.m_ArtNumber;
            c.m_ArtistName = alt.m_ArtistName;
            c.m_ArtStatus = alt.m_ArtStatus;
            c.m_HasAlternateArt = alt.m_HasAlternateArt;
            c.m_HasExtendedArt = alt.m_HasExtendedArt;
            c.m_CardLayoutId = alt.m_CardLayoutId;
            c.m_DefaultLayout = alt.m_DefaultLayout;
            c.m_HasExtendedLayout = alt.m_HasExtendedLayout;
            c.m_ExtendedLayout = alt.m_ExtendedLayout;
            FieldInfo cards = typeof(TemplateManager).GetField("m_Cards", BindingFlags.Instance | BindingFlags.NonPublic);
            Dictionary<ResourceId, CardTemplate> all = cards != null ? cards.GetValue(Singleton<TemplateManager>.Instance) as Dictionary<ResourceId, CardTemplate> : null;
            if (all != null) all[c.m_Id] = c;
            s_Copies[key] = c;
            Debug.Log("[HexRevival] [AltArtEquipment] " + alt.m_Name + ": equipped version " + equipped.m_Id.m_Guid + " with the alternate art of " + alt.m_Id.m_Guid + " -> " + c.m_Id.m_Guid);
            return c;
        }
    }
}
