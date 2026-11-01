using UnityEngine;

namespace HexRevival
{
    /// <summary>Copies of the game's NGUI widgets (main menu buttons, option rows) reused by the mod.</summary>
    public static class UiClone
    {
        /// <summary>Components kept on a copy: display, hover and click feedback, check boxes.</summary>
        private static readonly string[] Kept = { "UISprite", "UILabel", "UIWidget", "UITexture", "UIButton", "UIButtonColor",
            "UIButtonScale", "UIButtonOffset", "UIPlaySound", "UITooltipSetText", "UIToggle", "TweenScale", "TweenColor", "TweenAlpha",
            "TweenPosition", "UIDragScrollView" };

        /// <summary>
        /// Removes everything but the look from a copy: the game's bindings, localized texts and
        /// the original widget's actions (including its right-click ones).
        /// </summary>
        public static void Strip(GameObject go, params string[] alsoKept)
        {
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && System.Array.IndexOf(Kept, mb.GetType().Name) < 0 && System.Array.IndexOf(alsoKept, mb.GetType().Name) < 0)
                    Object.DestroyImmediate(mb);
        }

        /// <summary>Copy placed after the original, under the same parent.</summary>
        public static GameObject Copy(GameObject src, string name)
        {
            GameObject b = (GameObject)Object.Instantiate(src);
            b.name = name;
            b.transform.parent = src.transform.parent;
            b.transform.localPosition = src.transform.localPosition;
            b.transform.localRotation = src.transform.localRotation;
            b.transform.localScale = src.transform.localScale;
            return b;
        }

        /// <summary>Lays out the children of a grid or table again.</summary>
        public static void Reposition(Transform parent)
        {
            if (parent == null) return;
            UIGrid g = parent.GetComponent<UIGrid>();
            if (g != null) g.Reposition();
            UITable t = parent.GetComponent<UITable>();
            if (t != null) t.Reposition();
        }
    
        /// <summary>Moves a UI element up by dy pixels: through its anchors when it is an anchored widget (they would put it back), otherwise its position.</summary>
        public static void ShiftUp(GameObject g, int dy)
        {
            if (g == null) return;
            UIRect w = g.GetComponent<UIRect>();
            if (w != null && w.isAnchored)
            {
                foreach (UIRect.AnchorPoint a in new[] { w.topAnchor, w.bottomAnchor }) if (a.target != null) a.absolute += dy;
                w.ResetAndUpdateAnchors();
            }
            else g.transform.localPosition += new Vector3(0f, dy, 0f);
        }
}
}
