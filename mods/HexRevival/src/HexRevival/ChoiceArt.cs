using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Game.Shared;
using Reckoning.Game;

namespace HexRevival
{
    /// <summary>
    /// Cards without art of their own (the choices of a card, as Zealous Excruciator's "Pain" and "Suffering"; report:
    /// KrystoferRobin 2026-10-31) drawn with the art of the card that links to them, as the client does in a battle
    /// (GoCardBuilder.UpdateArtOverride: a choice card takes its first related card's art). Outside a battle there is
    /// no related card: opened from a card's link ("Click to view this card"), the client drew "Error". The card that
    /// links to it is the one whose text names it in bold ("chooses <b>Pain</b> or <b>Suffering</b>"), the names the
    /// client turns into links.
    /// </summary>
    public static class ChoiceArt
    {
        private static readonly FieldInfo s_Override = typeof(GoCardBuilder).GetField("m_ArtOverrideTemplate", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo s_Set = typeof(GoCardBuilder).GetField("m_CardSetTemplate", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Regex s_Bold = new Regex("<b>(.*?)</b>");
        private static Dictionary<ResourceId, CardTemplate> s_Parents;

        /// <summary>GoCardBuilder.UpdateArtOverride (postfix): an art-less card gets its parent's art.</summary>
        public static void AfterUpdate(GoCardBuilder b)
        {
            try
            {
                if (b == null || s_Override == null) return;
                CardTemplate o = s_Override.GetValue(b) as CardTemplate;
                if (o == null || !string.IsNullOrEmpty(o.m_CardImagePath)) return;
                CardTemplate p = Parent(o.m_Id);
                if (p == null) return;
                s_Override.SetValue(b, p);
                CardSetTemplate set;
                if (s_Set != null && Singleton<TemplateManager>.Instance.CardSets.TryGetValue(p.m_SetId, out set)) s_Set.SetValue(b, set);
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[HexRevival] [ChoiceArt] " + e.Message); }
        }

        /// <summary>The card with art whose text links to this one (null if none).</summary>
        public static CardTemplate Parent(ResourceId id)
        {
            if (s_Parents == null) Index();
            CardTemplate p;
            return s_Parents.TryGetValue(id, out p) ? p : null;
        }

        private static void Index()
        {
            s_Parents = new Dictionary<ResourceId, CardTemplate>();
            // The cards without art, by name.
            Dictionary<string, List<ResourceId>> artless = new Dictionary<string, List<ResourceId>>();
            List<CardTemplate> cards = new List<CardTemplate>();
            foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
            {
                if (t == null || string.IsNullOrEmpty(t.m_Name)) continue;
                if (string.IsNullOrEmpty(t.m_CardImagePath))
                {
                    List<ResourceId> l;
                    if (!artless.TryGetValue(t.m_Name.Trim(), out l)) artless[t.m_Name.Trim()] = l = new List<ResourceId>();
                    l.Add(t.m_Id);
                }
                else if (!string.IsNullOrEmpty(t.m_GameText)) cards.Add(t);
            }
            // The same parent every time: by name (several printings name the same choices).
            cards.Sort((a, c) => string.CompareOrdinal(a.m_Name, c.m_Name));
            foreach (CardTemplate t in cards)
                foreach (Match m in s_Bold.Matches(t.m_GameText))
                {
                    List<ResourceId> ids;
                    if (!artless.TryGetValue(m.Groups[1].Value.Trim(), out ids)) continue;
                    foreach (ResourceId id in ids) if (!s_Parents.ContainsKey(id)) s_Parents[id] = t;
                }
        }

        /// <summary>Dev "artcheck": the cards without art, and which take another card's art.</summary>
        public static string Check()
        {
            int none = 0, fixedN = 0;
            List<string> missing = new List<string>();
            foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
            {
                if (t == null || !string.IsNullOrEmpty(t.m_CardImagePath)) continue;
                none++;
                CardTemplate p = Parent(t.m_Id);
                if (p != null) fixedN++;
                else if (missing.Count < 40) missing.Add(t.m_Name + " [" + t.m_CardType + ", " + t.m_BuildTag + "]");
            }
            return none + " card(s) without art, " + fixedN + " drawn with a linking card's art; without one:\n" + string.Join("\n", missing.ToArray());
        }
    }
}
