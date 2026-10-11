using System.Collections.Generic;
using UnityEngine;

namespace HexRevival
{
    /// <summary>Short messages shown at the top of the screen for a few seconds (rewards, levels).</summary>
    public class Notices : MonoBehaviour
    {
        private const float Duration = 6f;
        private static readonly List<KeyValuePair<float, string>> s_Lines = new List<KeyValuePair<float, string>>();

        public static void Show(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            s_Lines.Add(new KeyValuePair<float, string>(Time.realtimeSinceStartup + Duration, text));
            Debug.Log("[HexRevival] [Notice] " + text);
        }

        private void OnGUI()
        {
            s_Lines.RemoveAll(l => l.Key < Time.realtimeSinceStartup);
            if (s_Lines.Count == 0) return;
            float S = GameSkin.Scale;
            GUI.depth = -950;
            float w = 640 * S, h = 34 * S;
            float y = 80 * S;
            foreach (KeyValuePair<float, string> l in s_Lines)
            {
                Rect r = new Rect((Screen.width - w) / 2f, y, w, h);
                GUI.DrawTexture(r, GameSkin.Solid(new Color(0, 0, 0, 0.7f)));
                GameSkin.Label(r, l.Value, GameSkin.Style("title", 18, GameSkin.Gold, TextAnchor.MiddleCenter));
                y += h + 4 * S;
            }
        }
    }
}
