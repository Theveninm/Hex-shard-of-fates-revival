using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// The online profile's form (see OnlineProfile): name shown to the other players, player id, password, server.
    /// Used on the login screen (first time) and from the lobby ("Online profile"). Edits a copy; Save() validates
    /// and writes it.
    /// </summary>
    public class ProfileForm
    {
        private string m_Name, m_PlayerId, m_Password, m_Server, m_Error;

        /// <summary>Fills the form with the saved profile.</summary>
        public void Load()
        {
            OnlineProfile op = OnlineProfile.Current;
            m_Name = op.Name;
            m_PlayerId = op.PlayerId;
            m_Password = op.Password;
            m_Server = op.Server;
            m_Error = null;
        }

        /// <summary>The fields, from the top of area; leaves room for the caller's buttons at the bottom (60 px).</summary>
        public void Draw(Rect area)
        {
            float S = GameSkin.Scale;
            float y = area.y + 4 * S, labelH = 28 * S, fieldH = 38 * S, bw = 200 * S;
            GameSkin.Label(new Rect(area.x, y, area.width, labelH), Loc.Get("online_name"), GameSkin.Style("title", 18, GameSkin.Gold));
            y += labelH + 2 * S;
            GUI.SetNextControlName("hexrevival_profile_name");
            m_Name = Controls.TextField(new Rect(area.x, y, area.width, fieldH), m_Name, 20);
            if (GUI.GetNameOfFocusedControl() == "") GUI.FocusControl("hexrevival_profile_name");
            y += fieldH + 2 * S;
            GUI.Label(new Rect(area.x, y, area.width, 22 * S), Loc.Get("online_desc"), GameSkin.Style("body", 13, GameSkin.Muted, TextAnchor.UpperLeft, true));
            y += 28 * S;

            // The player id and the password: generated once, kept; a new id makes a new identity for the server.
            GameSkin.Label(new Rect(area.x, y, area.width, labelH), Loc.Get("online_id"), GameSkin.Style("title", 18, GameSkin.Gold));
            y += labelH + 2 * S;
            Rect idr = new Rect(area.x, y, area.width - bw - 10 * S, fieldH);
            GameSkin.Sprite(idr, "A_Frames", "Frame_Inner");
            GUI.DrawTexture(new Rect(idr.x + 2, idr.y + 2, idr.width - 4, idr.height - 4), GameSkin.Solid(new Color(0, 0, 0, 0.45f)));
            GUI.Label(idr, m_PlayerId, GameSkin.Field);
            if (GameSkin.GoldButton(new Rect(idr.xMax + 10 * S, y, bw, fieldH), Loc.Get("online_new_id"), true, 15)) m_PlayerId = OnlineProfile.NewId();
            y += fieldH + 10 * S;

            GameSkin.Label(new Rect(area.x, y, area.width, labelH), Loc.Get("online_password"), GameSkin.Style("title", 18, GameSkin.Gold));
            y += labelH + 2 * S;
            m_Password = Controls.TextField(new Rect(area.x, y, area.width - bw - 10 * S, fieldH), m_Password, 64);
            if (GameSkin.GoldButton(new Rect(area.xMax - bw, y, bw, fieldH), Loc.Get("online_new_password"), true, 15)) m_Password = OnlineProfile.NewPassword();
            y += fieldH + 2 * S;
            GUI.Label(new Rect(area.x, y, area.width, 44 * S), Loc.Get("online_password_desc"), GameSkin.Style("body", 13, GameSkin.Muted, TextAnchor.UpperLeft, true));
            y += 50 * S;

            GameSkin.Label(new Rect(area.x, y, area.width, labelH), Loc.Get("online_server"), GameSkin.Style("title", 18, GameSkin.Gold));
            y += labelH + 2 * S;
            m_Server = Controls.TextField(new Rect(area.x, y, area.width, fieldH), m_Server, 80);
            y += fieldH + 8 * S;
            if (!string.IsNullOrEmpty(m_Error))
                GUI.Label(new Rect(area.x, y, area.width, 26 * S), Loc.Get(m_Error), GameSkin.Style("body", 15, new Color(1f, 0.55f, 0.45f)));
        }

        /// <summary>Validates and saves the profile; false (and a message under the fields) when something is wrong.</summary>
        public bool Save()
        {
            string name = (m_Name ?? "").Trim();
            string password = (m_Password ?? "").Trim();
            m_Error = name.Length < 3 ? "online_err_bad_name" : password.Length < 8 ? "online_short_password" : null;
            if (m_Error != null) return false;
            OnlineProfile op = OnlineProfile.Current;
            op.Name = name;
            op.PlayerId = m_PlayerId;
            op.Password = password;
            op.Server = string.IsNullOrEmpty((m_Server ?? "").Trim()) ? OnlineProfile.DefaultServer : m_Server.Trim();
            OnlineProfile.Save();
            return true;
        }

        public static bool SubmitPressed()
        {
            return Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
        }
    }
}
