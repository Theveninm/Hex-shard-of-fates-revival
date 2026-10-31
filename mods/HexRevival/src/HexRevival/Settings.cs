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
        /// "Platinum prices in gold" (progression accounts) and "Every gem in the campaign" (Saves.Info.AllGems), in place
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
            Transform grid = vis != null ? vis.Find("Option_Profile_Panel/LayoutGrid") : null;
            if (grid == null) return;
            foreach (string n in new[] { "ChangePassword", "Authenticator" })
            {
                Transform t = grid.Find(n);
                if (t != null && t.gameObject.activeSelf == offline) { t.gameObject.SetActive(!offline); UiClone.Reposition(grid); }
            }
            EnsureRow(row, grid, "HexRevival_GoldPrices", "opt_goldprices", offline && !Saves.Current.UnlockAll,
                () => Saves.Current != null && Saves.Current.GoldPrices, v => { if (Saves.Current != null) { Saves.Current.GoldPrices = v; Saves.SaveCurrent(); } });
            EnsureRow(row, grid, "HexRevival_AllGems", "opt_allgems", offline,
                () => Saves.Current != null && Saves.Current.AllGems, v => { if (Saves.Current != null) { Saves.Current.AllGems = v; Saves.SaveCurrent(); } });
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
