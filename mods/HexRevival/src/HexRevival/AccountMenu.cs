using System.Collections.Generic;
using Game.Client.GameState;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Account selection, drawn over the game's login screen: the list of local saves, with
    /// "Create a new account" at the top (name, "unlock everything", "play the tutorial").
    /// Choosing an account logs in with it. Same look as the Quick Match menu (see GameSkin).
    /// </summary>
    public class AccountMenu : MonoBehaviour
    {
        private enum Screen { List, Create }

        private Screen m_Screen = Screen.List;
        private List<Saves.Info> m_Saves;
        private Vector2 m_Scroll;
        private string m_Name = "";
        private bool m_Unlock;
        private bool m_Tutorial = true;
        private string m_Error;
        private bool m_LoggingIn;
        private float m_LoginRequestedAt;
        private readonly List<Behaviour> m_DisabledUi = new List<Behaviour>();

        private static bool OnLoginScreen()
        {
            try
            {
                Main main = Singleton<Main>.Instance;
                if (main == null || main.m_GameClient == null || main.m_GameClient.StateManager == null) return false;
                // The login screen shows up as Login, Title or Unknown depending on how it was reached.
                EGameState s = main.m_GameClient.StateManager.CurrentGameState;
                if (s != EGameState.Login && s != EGameState.Title && s != EGameState.Unknown) return false;
                UILoginDialogViewModel login = FindObjectOfType<UILoginDialogViewModel>();
                return login != null && login.isActiveAndEnabled;
            }
            catch { return false; }
        }

        private bool Visible { get { return !m_LoggingIn && OnLoginScreen(); } }

        private void Update()
        {
            bool onLogin = OnLoginScreen();
            // Back on the login screen after logging out: choose an account again.
            if (!onLogin && m_LoggingIn && Time.realtimeSinceStartup - m_LoginRequestedAt > 5f) m_LoggingIn = false;
            BlockGameUi(Visible);
            if (Visible && m_Saves == null) m_Saves = Saves.List();
            if (!Visible) m_Saves = null;
        }

        /// <summary>Blocks clicks to the game's login dialog under the menu.</summary>
        private void BlockGameUi(bool block)
        {
            if (block && m_DisabledUi.Count == 0)
            {
                foreach (UICamera c in UICamera.list.ToArray())
                    if (c != null && c.enabled) { c.enabled = false; m_DisabledUi.Add(c); }
            }
            else if (!block && m_DisabledUi.Count > 0)
            {
                foreach (Behaviour b in m_DisabledUi) if (b != null) b.enabled = true;
                m_DisabledUi.Clear();
            }
        }

        /// <summary>Logs in with the chosen account (the game's own login, answered locally).</summary>
        private void Play(Saves.Info save)
        {
            Saves.Select(save);
            UILoginDialogViewModel login = FindObjectOfType<UILoginDialogViewModel>();
            if (login == null) return;
            m_LoggingIn = true;
            m_LoginRequestedAt = Time.realtimeSinceStartup;
            BlockGameUi(false);
            login.LoginToGame();
        }

        private void OnGUI()
        {
            if (!Visible || m_Saves == null) return;
            float S = GameSkin.Scale;
            GUI.depth = -1000;
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float w = Mathf.Min(720f * S, UnityEngine.Screen.width - 24f), h = Mathf.Min(640f * S, UnityEngine.Screen.height - 24f);
            Rect win = new Rect((UnityEngine.Screen.width - w) / 2f, (UnityEngine.Screen.height - h) / 2f + 10f * S, w, h);
            if (!GameSkin.Sprite(win, "A_Frames", "Frame_With_Header_Hard_Corner"))
                GUI.DrawTexture(win, GameSkin.Solid(new Color(0.06f, 0.07f, 0.1f, 0.97f)));
            Rect title = new Rect(win.center.x - 240 * S, win.y - 22 * S, 480 * S, 64 * S);
            GameSkin.Sprite(title, "A_Frames", "Frame_Title_Bar");
            GameSkin.Label(title, Loc.Get(m_Screen == Screen.List ? "accounts" : "new_account_title"), GameSkin.Style("title", 28, GameSkin.Gold, TextAnchor.MiddleCenter));

            Rect inner = new Rect(win.x + 40 * S, win.y + 64 * S, win.width - 80 * S, win.height - 100 * S);
            if (m_Screen == Screen.List) DrawList(inner); else DrawCreate(inner);
        }

        private void DrawList(Rect area)
        {
            float S = GameSkin.Scale;
            float rowH = 64 * S, gap = 4 * S, newH = 54 * S;
            float contentH = newH + gap + m_Saves.Count * (rowH + gap);
            bool needBar = contentH > area.height;
            Rect view = new Rect(0, 0, area.width - (needBar ? 20 * S : 0), contentH);
            m_Scroll = GUI.BeginScrollView(area, m_Scroll, view, false, false, GUIStyle.none, needBar ? GameSkin.ScrollBar : GUIStyle.none);

            Rect nr = new Rect(0, 0, view.width, newH);
            bool over = nr.Contains(Event.current.mousePosition);
            GameSkin.Sprite(nr, "A_Buttons", over ? "Button_Bar_Main_hover" : "Button_Bar_Main_Mid");
            GameSkin.Sprite(new Rect(nr.x + 16 * S, nr.y + (newH - 34 * S) / 2f, 34 * S, 34 * S), "A_Buttons", over ? "Button_Add_Hover" : "Button_Add_Base", 1f, false);
            GameSkin.Label(new Rect(nr.x + 62 * S, nr.y, nr.width - 70 * S, newH), Loc.Get("new_account"), GameSkin.Style("title", 18, over ? Color.white : GameSkin.Gold));
            if (GUI.Button(nr, GUIContent.none, GUIStyle.none))
            {
                m_Screen = Screen.Create;
                m_Name = ""; m_Unlock = false; m_Tutorial = true; m_Error = null;
            }

            for (int i = 0; i < m_Saves.Count; i++)
            {
                Saves.Info s = m_Saves[i];
                Rect r = new Rect(0, newH + gap + i * (rowH + gap), view.width, rowH);
                string detail = Loc.Get("last_played", s.LastPlayed.ToString("g", System.Globalization.CultureInfo.CurrentCulture));
                if (GameSkin.Row(r, s.Name, detail, false)) Play(s);
            }
            GUI.EndScrollView();
            if (m_Saves.Count == 0)
                GUI.Label(new Rect(area.x, area.y + newH + 20 * S, area.width, 40 * S), Loc.Get("no_account"), GameSkin.Style("body", 15, GameSkin.Muted, TextAnchor.MiddleCenter));
        }

        private void DrawCreate(Rect area)
        {
            float S = GameSkin.Scale;
            float y = area.y + 10 * S;
            GameSkin.Label(new Rect(area.x, y, area.width, 30 * S), Loc.Get("account_name"), GameSkin.Style("title", 19, GameSkin.Gold));
            y += 36 * S;
            Rect field = new Rect(area.x, y, area.width, 40 * S);
            GameSkin.Sprite(field, "A_Frames", "Frame_Inner");
            GUI.DrawTexture(new Rect(field.x + 2, field.y + 2, field.width - 4, field.height - 4), GameSkin.Solid(new Color(0, 0, 0, 0.45f)));
            GUI.SetNextControlName("hexrevival_account_name");
            m_Name = GUI.TextField(field, m_Name ?? "", 32, GameSkin.Field);
            if (GUI.GetNameOfFocusedControl() == "") GUI.FocusControl("hexrevival_account_name");
            y += 64 * S;

            m_Unlock = Checkbox(new Rect(area.x, y, area.width, 66 * S), m_Unlock, Loc.Get("opt_unlock"), Loc.Get("opt_unlock_desc"));
            y += 84 * S;
            m_Tutorial = Checkbox(new Rect(area.x, y, area.width, 86 * S), m_Tutorial, Loc.Get("opt_tutorial"), Loc.Get("opt_tutorial_desc"));
            y += 104 * S;

            if (!string.IsNullOrEmpty(m_Error))
                GUI.Label(new Rect(area.x, y, area.width, 26 * S), Loc.Get(m_Error), GameSkin.Style("body", 15, new Color(1f, 0.55f, 0.45f)));

            float by = area.yMax - 56 * S;
            if (GameSkin.RedButton(new Rect(area.x, by, 180 * S, 52 * S), Loc.Get("cancel"))) m_Screen = Screen.List;
            bool submit = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if (GameSkin.GreenButton(new Rect(area.xMax - 220 * S, by, 220 * S, 52 * S), Loc.Get("create")) || submit)
            {
                string name = (m_Name ?? "").Trim();
                m_Error = Saves.CheckName(name);
                if (m_Error == null) Play(Saves.Create(name, m_Unlock, m_Tutorial));
            }
        }

        /// <summary>Check box with a title and a one-line description; returns the new value.</summary>
        private static bool Checkbox(Rect r, bool value, string title, string desc)
        {
            float S = GameSkin.Scale;
            bool hover = r.Contains(Event.current.mousePosition);
            Rect box = new Rect(r.x, r.y + 4 * S, 30 * S, 30 * S);
            GameSkin.Sprite(box, "A_Frames", "Frame_Inner");
            GUI.DrawTexture(new Rect(box.x + 2, box.y + 2, box.width - 4, box.height - 4), GameSkin.Solid(new Color(0, 0, 0, 0.5f)));
            if (value) GUI.DrawTexture(new Rect(box.x + 7 * S, box.y + 7 * S, box.width - 14 * S, box.height - 14 * S), GameSkin.Solid(GameSkin.Gold));
            GameSkin.Label(new Rect(box.xMax + 14 * S, r.y, r.width - box.width - 14 * S, 36 * S), title, GameSkin.Style("title", 18, hover ? Color.white : GameSkin.Gold));
            GUI.Label(new Rect(box.xMax + 14 * S, r.y + 34 * S, r.width - box.width - 14 * S, r.height - 34 * S), desc, GameSkin.Style("body", 14, GameSkin.Muted, TextAnchor.UpperLeft, true));
            return GUI.Button(r, GUIContent.none, GUIStyle.none) ? !value : value;
        }
    }
}
