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
        /// Adds "Show the cheat menu" to the game's display options, as a copy of the
        /// "Enable hints" row. Applied immediately (it does not wait for the options' Apply button).
        /// Called every frame; does nothing when the options are closed or the row already exists.
        /// </summary>
        public static void EnsureOptionRow()
        {
            UIToggle hints = null;
            foreach (UIToggle t in Object.FindObjectsOfType<UIToggle>())
                if (t.name == "Checkbox_Hints") { hints = t; break; }
            if (hints == null || hints.transform.parent == null) return;
            Transform row = hints.transform.parent;
            if (row.parent == null || row.parent.Find("HexRevival_ShowCheats") != null) return;
            try
            {
                GameObject copy = UiClone.Copy(row.gameObject, "HexRevival_ShowCheats");
                copy.transform.SetAsLastSibling();
                UiClone.Strip(copy);
                foreach (UILabel l in copy.GetComponentsInChildren<UILabel>(true)) l.text = Loc.Get("show_cheats");
                foreach (UIToggle t in copy.GetComponentsInChildren<UIToggle>(true))
                {
                    t.onChange.Clear();
                    t.value = ShowCheats;
                    UIToggle toggle = t;
                    t.onChange.Add(new EventDelegate(() => ShowCheats = toggle.value));
                }
                UiClone.Reposition(row.parent);
            }
            catch (System.Exception e) { Debug.LogError("[HexRevival] [Settings] option row: " + e); }
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
