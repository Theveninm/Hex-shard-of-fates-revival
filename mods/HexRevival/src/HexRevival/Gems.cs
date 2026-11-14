using System.Collections.Generic;
using Game.Shared.Mechanics;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// The gems offered by the deck editors' socket window (UISocketCardViewModel.UpdateAvailableGems). It lists the
    /// Standard gems (GemHelper.StandardGems: those of sets 9-10, Doombringer), or every gem not locked when told to
    /// (ShowImmortalGems), which the other deck editors do for non-Standard decks (Src.Game).
    /// - "Every gem in the campaign" account option (Saves.Info.AllGems; mornox, 2026-10-30): the campaign's deck editor
    ///   (UICampaignDeckEditor) never tells it. The PvE deck check already accepts every gem not locked
    ///   (GemHelper.IsGemLegalInFormat, ESetFormat.PvE), so the option only has to tell the window.
    /// - Sealed and Draft gauntlets: the tournament deck editor offered the Standard gems whatever the boosters' sets
    ///   (StuieOP, 2026-11-14). It now offers the gems of the boosters' chapters, as the game ties them
    ///   (GemHelper.IsGemLegalInFormat with ESetFormat.Chapter1..5: sets 1-4 the Chapter 1 gems, 5-6 Chapter 3,
    ///   7-8 Chapter 4, 9-10 Chapter 5). The mod's gauntlets do not check a limited deck's gems.
    /// </summary>
    public static class Gems
    {
        private static readonly System.Reflection.FieldInfo ShowImmortal =
            typeof(UISocketCardViewModel).GetField("m_ShowImmortalGems", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        private static readonly ESetFormat[] Chapters = { ESetFormat.Chapter1, ESetFormat.Chapter2, ESetFormat.Chapter3, ESetFormat.Chapter4, ESetFormat.Chapter5 };

        /// <summary>The limited gauntlet's chapters while its window's list is being built (between the prefix and the postfix).</summary>
        private static List<ESetFormat> s_Limited;

        /// <summary>The chapter of a set (1-10: two sets each), Invalid for any other number.</summary>
        public static ESetFormat ChapterOf(int setNumber)
        {
            return setNumber >= 1 && setNumber <= 2 * Chapters.Length ? Chapters[(setNumber - 1) / 2] : ESetFormat.Invalid;
        }

        /// <summary>UISocketCardViewModel.UpdateAvailableGems (prefix).</summary>
        public static void BeforeAvailableGems(UISocketCardViewModel vm)
        {
            s_Limited = null;
            if (vm == null || ShowImmortal == null || Saves.Current == null || Saves.IsOnline) return;
            if (Object.FindObjectOfType<UICampaignDeckEditor>() != null) { ShowImmortal.SetValue(vm, Saves.Current.AllGems); return; }
            if (Object.FindObjectOfType<UITournamentDeckEditorViewModel>() == null) return;
            s_Limited = Battlegrounds.LimitedGemFormats();
            if (s_Limited != null) ShowImmortal.SetValue(vm, true);   // every gem listed, then those of other chapters taken out
        }

        /// <summary>UISocketCardViewModel.UpdateAvailableGems (postfix): in a limited gauntlet, only its chapters' gems.</summary>
        public static void AfterAvailableGems(UISocketCardViewModel vm)
        {
            List<ESetFormat> chapters = s_Limited;
            s_Limited = null;
            if (chapters == null || vm == null || vm.Context == null) return;
            EZData.Collection<UIAvailableGemListItemContext> list = vm.Context.AvailableGems;
            int removed = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                UIAvailableGemListItemContext item = list.GetItem(i);
                if (item == null || chapters.Exists(f => GemHelper.IsGemLegalInFormat(item.GemType, f))) continue;
                list.Remove(i);
                removed++;
            }
            List<string> names = new List<string>();
            foreach (UIAvailableGemListItemContext item in list.Items) names.Add(item.GemType.ToString());
            Debug.Log("[HexRevival] [Gems] limited gauntlet (" + string.Join(", ", chapters.ConvertAll(f => f.ToString()).ToArray()) + "): "
                + list.Count + " gems offered, " + removed + " of other chapters left out: " + string.Join(", ", names.ToArray()));
        }
    }
}
