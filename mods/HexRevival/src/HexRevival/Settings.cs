using System.Text;
using UnityEngine;

namespace HexRevival
{
    /// <summary>Mod settings shared by all accounts (stored in the game's PlayerPrefs).</summary>
    public static class Settings
    {
        private const string ShowCheatsKey = "HexRevival.ShowCheats";

        /// <summary>"Show the cheat menu" (display options).</summary>
        public static bool ShowCheats
        {
            get { try { return PlayerPrefs.GetInt(ShowCheatsKey, 0) == 1; } catch { return false; } }
            set { PlayerPrefs.SetInt(ShowCheatsKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>
        /// Adds "Show the cheat menu" to the game's display options, as a copy of the "Enable hints" row, and the offline
        /// account's settings to its Account tab (user, 2026-10-30: changed there without making a new account):
        /// "Platinum prices in gold" and "Crafting" (progression accounts) and "Every gem in the campaign" (Saves.Info.AllGems), in place
        /// of "Change Password" and "Enable Authenticator", which do nothing offline. Applied immediately (they do not wait
        /// for the options' Apply button; neither needs a new session: the store and the gem list read them when shown).
        /// Called every frame; does nothing when the options are closed.
        /// </summary>
        public static void EnsureOptionRow()
        {
            UIToggle hints = null;
            // The display tab is hidden while the Account tab shows: its row is searched among the inactive objects too.
            foreach (UIToggle t in Resources.FindObjectsOfTypeAll<UIToggle>())
                if (t != null && t.name == "Checkbox_Hints" && t.transform.root.gameObject.activeInHierarchy && !t.transform.parent.name.StartsWith("HexRevival_")) { hints = t; break; }
            if (hints == null || hints.transform.parent == null) return;
            Transform row = hints.transform.parent;
            if (row.parent == null) return;
            bool offline = !Saves.IsOnline && Saves.Current != null;
            // The online account has no cheat menu, so no row for it either.
            EnsureRow(row, row.parent, "HexRevival_ShowCheats", "show_cheats", offline, () => ShowCheats, v => ShowCheats = v);

            Transform vis = row;
            while (vis != null && vis.name != "VisibilityBinding") vis = vis.parent;
            // The grid sits in the mod's scroll view once it is made (AccountScroll).
            Transform grid = vis != null ? (vis.Find("Option_Profile_Panel/" + ScrollName + "/LayoutGrid") ?? vis.Find("Option_Profile_Panel/LayoutGrid")) : null;
            if (grid == null) return;
            foreach (string n in new[] { "ChangePassword", "Authenticator" })
            {
                Transform t = grid.Find(n);
                if (t != null && t.gameObject.activeSelf == offline) { t.gameObject.SetActive(!offline); UiClone.Reposition(grid); }
            }
            EnsureRow(row, grid, "HexRevival_GoldPrices", "opt_goldprices", offline && !Saves.Current.UnlockAll,
                () => Saves.Current != null && Saves.Current.GoldPrices, v => { if (Saves.Current != null) { Saves.Current.GoldPrices = v; Saves.SaveCurrent(); } });
            // The prices (Criesener, 2026-11-01): copies of the Language row with their own list of values.
            EnsureChoiceRow(grid, "HexRevival_GoldPerPlatinum", "opt_goldperplat", offline && !Saves.Current.UnlockAll, Factors,
                () => Saves.Current != null ? Saves.Current.GoldPerPlatinum : Store.DefaultGoldPerPlatinum, v => { if (Saves.Current != null) { Saves.Current.GoldPerPlatinum = v; Saves.SaveCurrent(); } });
            EnsureChoiceRow(grid, "HexRevival_GoldDivisor", "opt_golddivisor", offline && !Saves.Current.UnlockAll, Factors,
                () => Saves.Current != null ? Saves.Current.GoldDivisor : 1, v => { if (Saves.Current != null) { Saves.Current.GoldDivisor = v; Saves.SaveCurrent(); } });
            EnsureTextRow(grid, "HexRevival_PriceExample", offline && !Saves.Current.UnlockAll,
                Store.PriceExample(Saves.Current.GoldPrices, Saves.Current.GoldPerPlatinum, Saves.Current.GoldDivisor));
            EnsureRow(row, grid, "HexRevival_Crafting", "opt_crafting", offline && !Saves.Current.UnlockAll,
                () => Saves.Current != null && Saves.Current.Crafting, v => { if (Saves.Current != null) { Saves.Current.Crafting = v; Saves.SaveCurrent(); } });
            EnsureRow(row, grid, "HexRevival_ExtraStore", "opt_extrastore", offline && !Saves.Current.UnlockAll,
                () => Saves.Current != null && Saves.Current.ExtraStore, v => { if (Saves.Current != null) { Saves.Current.ExtraStore = v; Saves.SaveCurrent(); } });
            EnsureRow(row, grid, "HexRevival_AllGems", "opt_allgems", offline,
                () => Saves.Current != null && Saves.Current.AllGems, v => { if (Saves.Current != null) { Saves.Current.AllGems = v; Saves.SaveCurrent(); } });
            AccountScroll(grid);
        }

        // ------------------------------------------------------------------
        // The Account tab's scroll view
        // ------------------------------------------------------------------

        private const string ScrollName = "HexRevival_AccountScroll";

        /// <summary>
        /// The Account tab scrolls (user 2026-11-02: with the mod's rows its list ran under the window's buttons). Its row
        /// grid goes into a clipped panel between the grid's top and the window's button bar (AccountScroller: mouse
        /// wheel over the list, and a scroll bar copied from one of the game's own when the rows do not fit). NGUI's own
        /// UIScrollView did not move here (tested 2026-11-02), so the panel is moved by hand.
        /// </summary>
        private static void AccountScroll(Transform grid)
        {
            try
            {
                if (!grid.gameObject.activeInHierarchy) return;
                Transform holder = grid.parent;
                if (holder == null || holder.name == ScrollName) return;
                float bottom;
                if (!ButtonBarTop(grid, out bottom)) return;
                GameObject go = new GameObject(ScrollName);
                go.layer = grid.gameObject.layer;
                go.transform.parent = holder;
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                UIPanel parentPanel = NGUITools.FindInParents<UIPanel>(holder.gameObject);
                UIPanel panel = go.AddComponent<UIPanel>();
                panel.depth = parentPanel != null ? parentPanel.depth + 1 : 10;
                panel.clipping = UIDrawCall.Clipping.SoftClip;
                panel.clipSoftness = new Vector2(0f, 4f);
                grid.parent = go.transform;   // the same place on screen: the new parent sits where the old one is
                NGUITools.MarkParentAsChanged(grid.gameObject);

                AccountScroller sc = go.AddComponent<AccountScroller>();
                sc.Grid = grid;
                sc.Panel = panel;
            }
            catch (System.Exception e) { Debug.LogWarning("[HexRevival] [Settings] account scroll: " + e.Message); }
        }

        /// <summary>
        /// The top of the window's button bar (Restore Defaults, Apply Changes, Cancel, Accept) in world space: the highest
        /// top of the visible buttons below the grid, outside it, looked for from the tab's panel up to the window.
        /// </summary>
        internal static bool ButtonBarTop(Transform grid, out float top)
        {
            top = float.MinValue;
            float gridTop = NGUIMath.CalculateAbsoluteWidgetBounds(grid).max.y;
            Transform w = grid;
            for (int up = 0; up < 8 && w.parent != null; up++)
            {
                w = w.parent;
                foreach (UIButton b in w.GetComponentsInChildren<UIButton>(false))
                {
                    if (b.transform.IsChildOf(grid)) continue;
                    Bounds bb = NGUIMath.CalculateAbsoluteWidgetBounds(b.transform);
                    if (bb.size.y <= 0f || bb.max.y >= gridTop) continue;
                    if (bb.max.y > top) top = bb.max.y;
                }
                if (top > float.MinValue) return true;
            }
            return false;
        }

        /// <summary>
        /// Moves the Account tab's clipped panel: the clip runs from the rows' top to the button bar; the mouse wheel over
        /// it scrolls; a scroll bar (a copy of a game one) shows the position and can be dragged, shown only when the rows
        /// do not fit. The clip is laid out again when the rows change or the window moves.
        /// </summary>
        public class AccountScroller : MonoBehaviour
        {
            public Transform Grid;
            public UIPanel Panel;
            private UIScrollBar m_Bar;
            private float m_Y, m_Overflow, m_ClipTop, m_ClipBottom, m_ClipLeft, m_ClipRight;
            private Vector3 m_LaidFor;
            private int m_LaidRows = -1;
            private bool m_Setting, m_Warned;
            private const float Step = 60f;   // panel units per wheel notch (a row is about 50)

            public float Overflow { get { return m_Overflow; } }
            public float Position { get { return m_Y; } }
            public float ContentMin;

            private void LateUpdate()
            {
                try
                {
                    if (Grid == null || Panel == null) return;
                    int rows = 0;
                    foreach (Transform t in Grid) if (t.gameObject.activeSelf) rows++;
                    if (rows != m_LaidRows || transform.parent.position != m_LaidFor) Layout(rows);
                    if (m_Overflow > 0f && Hovered())
                    {
                        // Input.mouseScrollDelta: the game's input settings may have no "Mouse ScrollWheel" axis.
                        float w = Input.mouseScrollDelta.y;
                        if (w != 0f) MoveTo(m_Y - Mathf.Sign(w) * Step);
                    }
                }
                catch (System.Exception e) { if (!m_Warned) Debug.LogWarning("[HexRevival] [Settings] account scroll: " + e); m_Warned = true; }
            }

            private void Layout(int rows)
            {
                float bottom;
                if (!ButtonBarTop(Grid, out bottom)) return;
                Transform pt = transform;
                float keep = m_Y;
                // A panel inside the rows (their header) clips with this one only if it clips too (NGUI: cumulative clipping).
                foreach (UIPanel inner in Grid.GetComponentsInChildren<UIPanel>(true))
                    if (inner != Panel && inner.clipping == UIDrawCall.Clipping.None)
                    {
                        inner.clipping = UIDrawCall.Clipping.SoftClip;
                        inner.baseClipRegion = new Vector4(0f, 0f, 100000f, 100000f);
                    }
                Place(0f);
                Bounds content = NGUIMath.CalculateRelativeWidgetBounds(pt, Grid, false);
                float bottomLocal = pt.InverseTransformPoint(new Vector3(0f, bottom, 0f)).y + 18f;   // above the buttons' frame
                float top = content.max.y + 2f;
                // Not above the tab buttons' bottom edge (Frame_Tabs/Tab_0x: their own sprite, not their glow children).
                Transform tabs = null;
                for (Transform w = pt.parent; w != null && tabs == null; w = w.parent) tabs = w.Find("Frame_Tabs");
                if (tabs != null)
                    foreach (UIButton tb in tabs.GetComponentsInChildren<UIButton>(false))
                    {
                        UIWidget tw = tb.GetComponent<UIWidget>();
                        if (tw == null || !tw.isVisible) continue;
                        float tabBottom = pt.InverseTransformPoint(tw.worldCorners[0]).y;
                        if (tabBottom - 1f < top && tabBottom > bottomLocal + 60f) top = tabBottom - 1f;
                    }
                if (top - bottomLocal < 60f) return;   // not laid out yet
                m_ClipTop = top; m_ClipBottom = bottomLocal;
                ContentMin = content.min.y;
                m_ClipLeft = content.min.x - 4f; m_ClipRight = content.max.x + 4f;
                Panel.baseClipRegion = new Vector4((m_ClipLeft + m_ClipRight) / 2f, (top + bottomLocal) / 2f, m_ClipRight - m_ClipLeft, top - bottomLocal);
                m_Overflow = Mathf.Max(0f, bottomLocal - content.min.y + 10f);   // the last row whole, with a little space
                if (m_Overflow <= 12f) m_Overflow = 0f;
                m_LaidRows = rows;
                m_LaidFor = pt.parent.position;
                EnsureBar();
                MoveTo(keep);
            }

            /// <summary>
            /// The rows are anchored to the window and would stay put when the panel moves: while scrolled, their anchors
            /// are frozen where they are (OnStart), and given back their own setting at the top (the window may move or
            /// resize then).
            /// </summary>
            private readonly System.Collections.Generic.Dictionary<UIRect, UIRect.AnchorUpdate> m_Anchors = new System.Collections.Generic.Dictionary<UIRect, UIRect.AnchorUpdate>();

            /// <summary>Widgets outside the rows (in the tab, not under the grid) anchored to something in the rows.</summary>
            private System.Collections.Generic.List<UIWidget> Headings()
            {
                System.Collections.Generic.List<UIWidget> res = new System.Collections.Generic.List<UIWidget>();
                Transform tab = transform.parent;
                if (tab == null) return res;
                foreach (UIWidget w in tab.GetComponentsInChildren<UIWidget>(false))
                {
                    if (w.transform.IsChildOf(transform) || !w.isAnchored) continue;
                    foreach (UIRect.AnchorPoint a in new[] { w.leftAnchor, w.rightAnchor, w.topAnchor, w.bottomAnchor })
                        if (a != null && a.target != null && a.target.IsChildOf(Grid)) { res.Add(w); break; }
                }
                return res;
            }

            private void Freeze(bool frozen)
            {
                if (frozen == (m_Anchors.Count > 0)) return;
                if (frozen)
                {
                    System.Collections.Generic.List<UIRect> rects = new System.Collections.Generic.List<UIRect>(Grid.GetComponentsInChildren<UIRect>(true));
                    foreach (UIWidget h in Headings()) rects.Add(h);
                    foreach (UIRect r in rects)
                        if (r.isAnchored && r.updateAnchors != UIRect.AnchorUpdate.OnStart) { m_Anchors[r] = r.updateAnchors; r.updateAnchors = UIRect.AnchorUpdate.OnStart; }
                    if (m_Anchors.Count == 0) m_Anchors[Panel] = Panel.updateAnchors;   // nothing anchored: remembered as frozen all the same
                }
                else
                {
                    foreach (System.Collections.Generic.KeyValuePair<UIRect, UIRect.AnchorUpdate> kv in m_Anchors) if (kv.Key != null) kv.Key.updateAnchors = kv.Value;
                    m_Anchors.Clear();
                }
            }

            /// <summary>The panel moved down by y (the rows go up), the clip kept in place.</summary>
            private void Place(float y)
            {
                Freeze(y > 0.01f);
                m_Y = y;
                transform.localPosition = new Vector3(0f, y, 0f);
                Panel.clipOffset = new Vector2(0f, -y);
            }

            public void MoveTo(float y)
            {
                Place(Mathf.Clamp(y, 0f, m_Overflow));
                if (m_Bar != null)
                {
                    bool show = m_Overflow > 0f;
                    if (m_Bar.gameObject.activeSelf != show) m_Bar.gameObject.SetActive(show);
                    if (show)
                    {
                        m_Setting = true;
                        float h = m_ClipTop - m_ClipBottom;
                        // The thumb as the visible share of the rows, but never more than 60% of the track, so
                        // it can always be dragged.
                        m_Bar.barSize = Mathf.Clamp(h / (h + m_Overflow), 0.15f, 0.6f);
                        m_Bar.value = m_Overflow > 0f ? m_Y / m_Overflow : 0f;
                        m_Setting = false;
                    }
                }
            }

            public void Step1(int dir) { MoveTo(m_Y + dir * Step); }

            /// <summary>The mouse over the clip (screen space, through the UI camera).</summary>
            private bool Hovered()
            {
                Camera cam = NGUITools.FindCameraForLayer(gameObject.layer);
                if (cam == null) return false;
                Transform pp = transform.parent;
                Vector3 a = cam.WorldToScreenPoint(pp.TransformPoint(new Vector3(m_ClipLeft, m_ClipBottom, 0f)));
                Vector3 b = cam.WorldToScreenPoint(pp.TransformPoint(new Vector3(m_ClipRight + 30f, m_ClipTop, 0f)));
                Vector3 m = Input.mousePosition;
                return m.x >= Mathf.Min(a.x, b.x) && m.x <= Mathf.Max(a.x, b.x) && m.y >= Mathf.Min(a.y, b.y) && m.y <= Mathf.Max(a.y, b.y);
            }

            /// <summary>
            /// A vertical scroll bar inside the clip's right edge: a copy of one of the game's own (its sprites), sized to the
            /// clip; none when no game bar is loaded (the wheel still scrolls).
            /// </summary>
            private void EnsureBar()
            {
                Transform pp = transform.parent;
                if (m_Bar == null)
                {
                    UIScrollBar src = null;
                    foreach (UIScrollBar b in Resources.FindObjectsOfTypeAll<UIScrollBar>())
                        if (b != null && b.gameObject.scene.IsValid() && b.fillDirection == UIProgressBar.FillDirection.TopToBottom
                            && b.backgroundWidget != null && b.foregroundWidget != null && !b.name.StartsWith(ScrollName)) { src = b; break; }
                    if (src == null) return;
                    GameObject copy = (GameObject)Instantiate(src.gameObject);
                    copy.name = ScrollName + "_Bar";
                    copy.layer = gameObject.layer;
                    copy.transform.parent = pp;   // outside the clip
                    copy.transform.localRotation = Quaternion.identity;
                    copy.transform.localScale = Vector3.one;
                    m_Bar = copy.GetComponent<UIScrollBar>();
                    foreach (UIWidget wd in copy.GetComponentsInChildren<UIWidget>(true)) wd.SetAnchor((GameObject)null);
                    // Drawn above the rows (their panel is one over the tab's): a panel of its own, higher still.
                    UIPanel barPanel = copy.GetComponent<UIPanel>() ?? copy.AddComponent<UIPanel>();
                    barPanel.depth = Panel.depth + 1;
                    // A visible track: the game's bar draws only its thumb here; the track takes the thumb's sprite, dimmed.
                    UISprite fgs = m_Bar.foregroundWidget as UISprite, bgs = m_Bar.backgroundWidget as UISprite;
                    if (fgs != null && bgs != null)
                    {
                        bgs.atlas = fgs.atlas;
                        bgs.spriteName = fgs.spriteName;
                        bgs.type = fgs.type;
                        Color c = fgs.color; c.a *= 0.3f; bgs.color = c;
                        bgs.alpha = 1f;
                    }
                    m_Bar.onChange.Clear();
                    m_Bar.onChange.Add(new EventDelegate(() => { if (!m_Setting) Place(Mathf.Clamp01(m_Bar.value) * m_Overflow); }));
                    copy.SetActive(true);
                }
                UIWidget bg = m_Bar.backgroundWidget;
                if (bg == null) return;
                // Height: the clip's, in the bar's own units; then moved so its top right sits at the clip's top right.
                Bounds now = NGUIMath.CalculateAbsoluteWidgetBounds(bg.transform);
                Vector3 topRight = pp.TransformPoint(new Vector3(m_ClipRight - 2f, m_ClipTop - 2f, 0f));
                Vector3 bottomRight = pp.TransformPoint(new Vector3(m_ClipRight - 2f, m_ClipBottom + 2f, 0f));
                float worldH = Mathf.Abs(topRight.y - bottomRight.y);
                if (now.size.y > 0f && bg.height > 0) bg.height = Mathf.Max(20, Mathf.RoundToInt(bg.height * worldH / now.size.y));
                m_Bar.ForceUpdate();
                now = NGUIMath.CalculateAbsoluteWidgetBounds(bg.transform);
                m_Bar.transform.position += new Vector3(topRight.x - now.max.x, topRight.y - now.max.y, 0f);
            }
        }

        /// <summary>Dev "acctscroll [up|down]": the Account tab's scroll (clip, overflow, position, the bar), or a scroll step.</summary>
        public static string DevScroll(string arg)
        {
            AccountScroller sc = null;
            foreach (AccountScroller v in Resources.FindObjectsOfTypeAll<AccountScroller>()) if (v != null && v.gameObject.activeInHierarchy) { sc = v; break; }
            if (sc == null) return "no account scroll (open Settings, Account tab)";
            if (arg == "down" || arg == "up") sc.Step1(arg == "down" ? 1 : -1);
            UIScrollBar bar = null;
            foreach (UIScrollBar b in Resources.FindObjectsOfTypeAll<UIScrollBar>()) if (b != null && b.name == ScrollName + "_Bar") bar = b;
            System.Collections.Generic.List<string> other = new System.Collections.Generic.List<string>();
            foreach (UIWidget w in sc.Grid.GetComponentsInChildren<UIWidget>(true))
                if (w.panel != sc.Panel) other.Add(w.name + "@" + (w.panel != null ? w.panel.name + "/" + w.panel.clipping : "null") + (w.isVisible ? "" : "(hidden)"));
            return "outside panel: " + string.Join(", ", other.ToArray()) + "\ncontentMin=" + sc.ContentMin + " clip=" + sc.Panel.baseClipRegion + " offset=" + sc.Panel.clipOffset + " overflow=" + sc.Overflow + " y=" + sc.Position
                + " bar=" + (bar != null ? "active=" + bar.gameObject.activeInHierarchy + " value=" + bar.value + " size=" + bar.barSize : "none");
        }

        /// <summary>
        /// A copy of the "Enable hints" row under parent, with its own label and value (made once, shown when show is true,
        /// its check box kept on the setting's value: the account may have changed since).
        /// </summary>
        private static void EnsureRow(Transform hintsRow, Transform parent, string name, string labelKey, bool show, System.Func<bool> get, System.Action<bool> set)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                if (existing.gameObject.activeSelf != show) { existing.gameObject.SetActive(show); UiClone.Reposition(parent); }
                if (show)
                    foreach (UIToggle t in existing.GetComponentsInChildren<UIToggle>(true))
                        if (t.value != get()) t.value = get();
                return;
            }
            if (!show) return;
            try
            {
                GameObject copy = UiClone.Copy(hintsRow.gameObject, name);
                if (copy.transform.parent != parent)
                {
                    Vector3 scale = copy.transform.localScale;
                    copy.transform.parent = parent;
                    copy.transform.localScale = scale;
                    copy.transform.localRotation = Quaternion.identity;
                }
                copy.SetActive(true);
                copy.transform.SetAsLastSibling();
                UiClone.Strip(copy);
                foreach (UILabel l in copy.GetComponentsInChildren<UILabel>(true)) l.text = Loc.Get(labelKey);
                foreach (UIToggle t in copy.GetComponentsInChildren<UIToggle>(true))
                {
                    t.onChange.Clear();
                    t.name = name + "_Toggle";   // not taken for the game's own "Enable hints" check box
                    t.value = get();
                    UIToggle toggle = t;
                    t.onChange.Add(new EventDelegate(() => set(toggle.value)));
                }
                UiClone.Reposition(parent);
            }
            catch (System.Exception e) { Debug.LogError("[HexRevival] [Settings] option row " + name + ": " + e); }
        }

        /// <summary>The values offered for the gold per platinum and the gold price divisor (a typed value joins them).</summary>
        private static readonly int[] Factors = { 1, 2, 3, 4, 5, 10, 20, 25, 50, 100 };

        /// <summary>
        /// A copy of the Account tab's "Language" row (a title and a drop-down list) under parent, with its own title and
        /// values (made once, shown when show is true, its list kept on the setting's value: the account may have changed since).
        /// </summary>
        private static void EnsureChoiceRow(Transform parent, string name, string labelKey, bool show, int[] choices, System.Func<int> get, System.Action<int> set)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                if (existing.gameObject.activeSelf != show) { existing.gameObject.SetActive(show); UiClone.Reposition(parent); }
                if (show)
                {
                    // The list keeps no selection (UIPopupList.Set clears it after its callbacks): the label is the memory.
                    UIPopupList list = existing.GetComponentInChildren<UIPopupList>(true);
                    UILabel shown = list != null ? ChoiceLabel(list) : null;
                    if (shown != null && shown.text != get().ToString()) { FillChoices(list, choices, get()); shown.text = get().ToString(); }
                }
                return;
            }
            if (!show) return;
            Transform language = parent.Find("Language");
            if (language == null) return;
            try
            {
                GameObject copy = UiClone.Copy(language.gameObject, name);
                copy.SetActive(true);
                copy.transform.SetAsLastSibling();
                UiClone.Strip(copy, "UIPopupList");   // the list stays; its binding to the game's languages goes
                Transform title = copy.transform.Find("LanguageLabel");
                UILabel titleLabel = title != null ? title.GetComponent<UILabel>() : null;
                if (titleLabel != null)
                {
                    // Longer than "Language": a wider label, right-aligned at the same edge, instead of shrunk text
                    // (the row's anchors would keep the width, so they go).
                    titleLabel.SetAnchor((GameObject)null);
                    titleLabel.pivot = UIWidget.Pivot.Right;
                    titleLabel.alignment = NGUIText.Alignment.Right;
                    titleLabel.overflowMethod = UILabel.Overflow.ClampContent;
                    titleLabel.width = 400;
                    titleLabel.text = Loc.Get(labelKey);
                }
                UIPopupList list = copy.GetComponentInChildren<UIPopupList>(true);
                UILabel shown = list != null ? ChoiceLabel(list) : null;
                if (list == null || shown == null) { Object.Destroy(copy); return; }
                list.name = name + "_List";
                list.isLocalized = false;
                list.onChange.Clear();
                FillChoices(list, choices, get());
                shown.text = get().ToString();
                UIPopupList chosen = list;
                // The chosen value is only readable during the callback (UIPopupList.current.value).
                list.onChange.Add(new EventDelegate(() =>
                {
                    int v;
                    if (int.TryParse(chosen.value, out v) && v > 0) { set(v); shown.text = v.ToString(); }
                }));
                UiClone.Reposition(parent);
            }
            catch (System.Exception e) { Debug.LogError("[HexRevival] [Settings] choice row " + name + ": " + e); }
        }

        /// <summary>
        /// A line of text under parent (a copy of the Account tab's "User Name" label), kept on the given text:
        /// the example prices follow the lists above as they change.
        /// </summary>
        private static void EnsureTextRow(Transform parent, string name, bool show, string text)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                if (existing.gameObject.activeSelf != show) { existing.gameObject.SetActive(show); UiClone.Reposition(parent); }
                UILabel l = show ? existing.GetComponent<UILabel>() : null;
                if (l != null && l.text != text) l.text = text;
                return;
            }
            if (!show) return;
            Transform src = parent.Find("AccountName");
            if (src == null || src.GetComponent<UILabel>() == null) return;
            try
            {
                GameObject copy = UiClone.Copy(src.gameObject, name);
                copy.SetActive(true);
                copy.transform.SetAsLastSibling();
                UiClone.Strip(copy);
                UILabel l = copy.GetComponent<UILabel>();
                // One line across the row (the "User Name" label is narrower and anchored).
                l.SetAnchor((GameObject)null);
                l.overflowMethod = UILabel.Overflow.ClampContent;
                l.width = 780;
                l.alignment = NGUIText.Alignment.Center;
                l.fontSize = 18;
                l.text = text;
                UiClone.Reposition(parent);
            }
            catch (System.Exception e) { Debug.LogError("[HexRevival] [Settings] text row " + name + ": " + e); }
        }

        /// <summary>The list's items: the choices, and the current value if it is not one of them (typed when the account was made).</summary>
        private static void FillChoices(UIPopupList list, int[] choices, int current)
        {
            list.items.Clear();
            list.itemData.Clear();
            bool seen = false;
            foreach (int c in choices)
            {
                if (!seen && current < c) { list.items.Add(current.ToString()); seen = true; }
                if (c == current) seen = true;
                list.items.Add(c.ToString());
            }
            if (!seen) list.items.Add(current.ToString());
        }

        /// <summary>The list's label, which shows the value (the game's binding did that for the Language row).</summary>
        private static UILabel ChoiceLabel(UIPopupList list)
        {
            Transform label = list.transform.Find("Label");
            return label != null ? label.GetComponent<UILabel>() : null;
        }

        /// <summary>Test command "toggles": check boxes on screen, with their path and nearby labels.</summary>
        public static string DumpToggles()
        {
            StringBuilder sb = new StringBuilder();
            foreach (UIToggle t in Object.FindObjectsOfType<UIToggle>())
            {
                string path = t.name;
                for (Transform p = t.transform.parent; p != null; p = p.parent) path = p.name + "/" + path;
                sb.Append(path).Append(" value=").Append(t.value).Append(" group=").Append(t.group);
                Transform row = t.transform.parent;
                if (row != null)
                    foreach (UILabel l in row.GetComponentsInChildren<UILabel>(true)) sb.Append(" | ").Append(l.name).Append("='").Append(l.text).Append("'");
                sb.Append('\n');
            }
            return sb.Length > 0 ? sb.ToString() : "no toggle on screen";
        }
    }
}
