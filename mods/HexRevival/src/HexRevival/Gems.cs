using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// The "Every gem in the campaign" account option (Saves.Info.AllGems; mornox, 2026-10-30). The campaign's deck
    /// editor (UICampaignDeckEditor) socket window lists the Standard gems only: its UISocketCardViewModel shows the
    /// others when told to (ShowImmortalGems), which the other deck editors do for non-Standard decks and the campaign's
    /// never does (Src.Game). The PvE deck check already accepts every gem not locked (GemHelper.IsGemLegalInFormat,
    /// ESetFormat.PvE), so the option only has to tell the window.
    /// </summary>
    public static class Gems
    {
        private static readonly System.Reflection.FieldInfo ShowImmortal =
            typeof(UISocketCardViewModel).GetField("m_ShowImmortalGems", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        /// <summary>UISocketCardViewModel.UpdateAvailableGems (prefix): in the campaign's deck editor, every gem with the option.</summary>
        public static void BeforeAvailableGems(UISocketCardViewModel vm)
        {
            if (vm == null || ShowImmortal == null || Saves.Current == null || Saves.IsOnline) return;
            if (Object.FindObjectOfType<UICampaignDeckEditor>() == null) return;
            ShowImmortal.SetValue(vm, Saves.Current.AllGems);
        }
    }
}
