using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Shared;
using Game.Shared.Mechanics;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Cards whose picture the client cannot load, drawn with the picture of another version of the same card (same
    /// name and type; its own set and its printed version first) instead of the client's "Error" picture.
    /// Report: a player's screenshot of the epic Cerulean Mirror Knight (Discord, 2026-11-02). Its bundle in the
    /// client's archive (cardsets/set001/a0001896.ab) holds only the card's sound, no texture; the rare version's
    /// bundle (a0000076.ab) has both. The 31 alternates of the Doombringer core commons ("Set09_PvP_Core_Commons_alternates")
    /// name an archive the client does not have (set09_pvp_core_commons.harc); their pictures exist under their
    /// regular versions (archive breachcorecommonss). Every other card bundle of the client holds its texture (every
    /// archive read, 2026-11-02). The mod does not read the archives for this: it reacts to the client's failure
    /// (GoCardBuilder.SetPortraitToError, reached after the atlas, the full texture and the file on disk all failed),
    /// remembers the card and swaps the picture again whenever the client resets it (GoCardBuilder.UpdateArtOverride).
    /// </summary>
    public static class MissingArt
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo s_Override = typeof(GoCardBuilder).GetField("m_ArtOverrideTemplate", Any);
        private static readonly FieldInfo s_Set = typeof(GoCardBuilder).GetField("m_CardSetTemplate", Any);
        private static readonly MethodInfo s_UpdateTexture = typeof(GoCardBuilder).GetMethod("UpdateTexture", Any);

        /// <summary>The cards whose picture failed, with the version tried next (null: no other version is left).</summary>
        private static readonly Dictionary<ResourceId, CardTemplate> s_Missing = new Dictionary<ResourceId, CardTemplate>();
        private static Dictionary<string, List<CardTemplate>> s_ByName;

        /// <summary>
        /// GoCardBuilder.SetPortraitToError (prefix): the card's picture failed to load; another version's is tried.
        /// False when one is tried: the client's "Error" picture is not shown.
        /// </summary>
        public static bool BeforeError(GoCardBuilder b)
        {
            try
            {
                if (b == null || s_Override == null || s_Set == null || s_UpdateTexture == null) return true;
                CardTemplate o = s_Override.GetValue(b) as CardTemplate;
                if (o == null || string.IsNullOrEmpty(o.m_CardImagePath)) return true;
                CardTemplate next;
                if (!s_Missing.TryGetValue(o.m_Id, out next))
                {
                    next = Sibling(o);
                    s_Missing[o.m_Id] = next;
                    Debug.Log("[HexRevival] [MissingArt] " + o.m_Name + " [" + o.m_CardRarity + ", " + SetName(o) + "]: picture " + o.m_CardImagePath
                        + " not loaded" + (next != null ? "; drawn with " + next.m_CardImagePath + " [" + next.m_CardRarity + ", " + SetName(next) + "]" : "; no other version"));
                }
                next = Resolve(o);
                if (next == null || next == o) return true;
                Apply(b, next);
                s_UpdateTexture.Invoke(b, new object[] { false });
                return false;
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [MissingArt] " + e.Message); return true; }
        }

        /// <summary>GoCardBuilder.UpdateArtOverride (postfix, after ChoiceArt): a card known to lack its picture gets the other version's again.</summary>
        public static void AfterUpdate(GoCardBuilder b)
        {
            try
            {
                if (b == null || s_Override == null || s_Set == null || s_Missing.Count == 0) return;
                CardTemplate o = s_Override.GetValue(b) as CardTemplate;
                if (o == null || !s_Missing.ContainsKey(o.m_Id)) return;
                CardTemplate r = Resolve(o);
                if (r != null && r != o) Apply(b, r);
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [MissingArt] " + e.Message); }
        }

        /// <summary>The version drawn for a card whose picture is known to fail (null if it is drawn as is, or nothing is left).</summary>
        public static CardTemplate Replacement(CardTemplate t)
        {
            if (t == null || !s_Missing.ContainsKey(t.m_Id)) return null;
            CardTemplate r = Resolve(t);
            return r == t ? null : r;
        }

        private static void Apply(GoCardBuilder b, CardTemplate t)
        {
            s_Override.SetValue(b, t);
            CardSetTemplate set;
            if (Singleton<TemplateManager>.Instance.CardSets.TryGetValue(t.m_SetId, out set) && set != null) s_Set.SetValue(b, set);
        }

        /// <summary>Follows the chain of failed versions to one not known to fail (null when every version failed).</summary>
        private static CardTemplate Resolve(CardTemplate t)
        {
            CardTemplate cur = t;
            for (int i = 0; i < 16 && cur != null; i++)
            {
                CardTemplate next;
                if (!s_Missing.TryGetValue(cur.m_Id, out next)) return cur;
                cur = next;
            }
            return null;
        }

        /// <summary>
        /// Another version of the card with a picture of its own, not known to fail: the same name and type, a picture
        /// the client loads from elsewhere (another set's archive or another file; the core common alternates name
        /// the same file as their regular version, in an archive that does not exist); its own set first, then the
        /// printed version (a designer id), then not an epic, then the lowest id.
        /// </summary>
        public static CardTemplate Sibling(CardTemplate t)
        {
            if (t == null || string.IsNullOrEmpty(t.m_Name)) return null;
            if (s_ByName == null) Index();
            List<CardTemplate> l;
            if (!s_ByName.TryGetValue(Key(t.m_Name), out l)) return null;
            CardTemplate best = null;
            int bestScore = int.MinValue;
            string failed = BundlePath(t);
            foreach (CardTemplate c in l)
            {
                if (c == t || c.m_Id == t.m_Id || s_Missing.ContainsKey(c.m_Id) || c.m_CardType != t.m_CardType) continue;
                if (string.IsNullOrEmpty(c.m_CardImagePath) || BundlePath(c) == failed) continue;
                int score = (c.m_SetId == t.m_SetId ? 4 : 0) + (!string.IsNullOrEmpty(c.m_DesignerCardId) ? 2 : 0) + (c.m_CardRarity != ERarity.Epic ? 1 : 0);
                if (score > bestScore || (score == bestScore && string.CompareOrdinal(c.m_Id.ToString(), best.m_Id.ToString()) < 0)) { best = c; bestScore = score; }
            }
            return best;
        }

        private static void Index()
        {
            s_ByName = new Dictionary<string, List<CardTemplate>>();
            foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
            {
                if (t == null || string.IsNullOrEmpty(t.m_Name)) continue;
                List<CardTemplate> l;
                string k = Key(t.m_Name);
                if (!s_ByName.TryGetValue(k, out l)) s_ByName[k] = l = new List<CardTemplate>();
                l.Add(t);
            }
        }

        private static string Key(string name) { return name.Trim().ToLowerInvariant(); }

        /// <summary>Where the client loads the card's picture from: its set's archive and the file's name (GoCardBuilder.UpdateTexture).</summary>
        private static string BundlePath(CardTemplate t)
        {
            CardSetTemplate set;
            string bundle = Singleton<TemplateManager>.Instance.CardSets.TryGetValue(t.m_SetId, out set) && set != null
                ? (string.IsNullOrEmpty(set.m_BundleName) ? set.m_Name : set.m_BundleName) : "?";
            return (bundle + "/" + FolderUtils.GetFileNameWithoutPathOrExtension(t.m_CardImagePath)).ToLowerInvariant();
        }

        private static string SetName(CardTemplate t)
        {
            CardSetTemplate set;
            return Singleton<TemplateManager>.Instance.CardSets.TryGetValue(t.m_SetId, out set) && set != null ? set.m_Name : "?";
        }

        /// <summary>Dev "missingart": the cards whose picture failed so far this session, and what they are drawn with.</summary>
        public static string Check()
        {
            if (s_Missing.Count == 0) return "no card picture failed so far";
            List<string> o = new List<string>();
            foreach (KeyValuePair<ResourceId, CardTemplate> kv in s_Missing)
            {
                CardTemplate t;
                if (!Singleton<TemplateManager>.Instance.Cards.TryGetValue(kv.Key, out t) || t == null) continue;
                CardTemplate r = Resolve(t);
                o.Add(t.m_Name + " [" + t.m_CardRarity + ", " + SetName(t) + "] " + t.m_CardImagePath + " -> "
                    + (r != null ? r.m_CardImagePath + " [" + r.m_CardRarity + ", " + SetName(r) + "]" : "none (Error)"));
            }
            return s_Missing.Count + " card(s) whose picture failed:\n" + string.Join("\n", o.ToArray());
        }
    }
}
