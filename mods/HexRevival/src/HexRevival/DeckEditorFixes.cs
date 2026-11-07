using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Display fixes for the client's deck editor windows.
    /// </summary>
    public static class DeckEditorFixes
    {
        /// <summary>
        /// UIDeckTestDrawStatsViewModel.SetVisible (Harmony postfix). In the campaign deck editor's "Draw Test Hand"
        /// window, the prefab draws the "New Hand" / "Redraw" / "Draw Card" button sprites (depth 6) over their labels
        /// (depth 5), so the buttons look blank (duxxors 2026-11-07). The collection's own window has the labels above
        /// (sprite 4, label 5). Each button label is raised above its button.
        /// </summary>
        public static void TestDrawShown(MonoBehaviour window, bool visible)
        {
            if (!visible || window == null) return;
            foreach (UILabel l in window.GetComponentsInChildren<UILabel>(true))
            {
                UISprite button = l.transform.parent != null ? l.transform.parent.GetComponent<UISprite>() : null;
                if (button != null && button.GetComponent<Collider>() != null && l.depth <= button.depth) l.depth = button.depth + 1;
            }
        }
    }
}
