using System;
using System.Collections.Generic;
using Game.Shared;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// The online lobby, shown in the Quick Match menu's "Online" tab: players connected to the master server, the
    /// chat, challenges. A challenge accepted starts a game (see OnlineMatch) with the deck chosen on the left.
    /// The connection is opened with the online profile when the tab is first shown and stays open in the background
    /// (a challenge received meanwhile is announced).
    /// </summary>
    public class OnlineLobby : MonoBehaviour
    {
        private class Player
        {
            public string Id, Name, Status;
        }

        private static OnlineLobby s_Instance;
        private NetClient m_Net;
        private readonly List<Player> m_Players = new List<Player>();
        private readonly List<string> m_Chat = new List<string>();
        private string m_ChatInput = "";
        private string m_Message;
        private string m_ChallengedId;                      // our challenge waiting for an answer
        private readonly Dictionary<string, string> m_Challenges = new Dictionary<string, string>();   // id -> name of who challenged us
        private UID m_Deck = UID.Invalid;
        private Vector2 m_ScrollPlayers, m_ScrollChat;

        private void Awake() { s_Instance = this; }

        public static bool Online { get { return s_Instance != null && s_Instance.m_Net != null && s_Instance.m_Net.State == NetClient.Status.Online; } }

        /// <summary>Connects with the saved profile (nothing when already connected or connecting).</summary>
        public static void Connect()
        {
            OnlineLobby l = s_Instance;
            if (l == null || (l.m_Net != null && l.m_Net.State != NetClient.Status.Closed)) return;
            OnlineProfile p = OnlineProfile.Current;
            if (string.IsNullOrEmpty(p.Name)) { l.m_Message = Loc.Get("online_no_profile"); return; }
            l.m_Players.Clear();
            l.m_Challenges.Clear();
            l.m_ChallengedId = null;
            l.m_Message = Loc.Get("online_connecting");
            l.m_Net = NetClient.Connect(p);
        }

        public static void Disconnect()
        {
            OnlineLobby l = s_Instance;
            if (l == null || l.m_Net == null) return;
            l.m_Net.Close();
            l.m_Net = null;
            l.m_Players.Clear();
            OnlineMatch.End();
        }

        /// <summary>The battle of the match was left: back to the lobby.</summary>
        public static void LeaveMatch()
        {
            OnlineMatch.End();
            if (s_Instance != null && s_Instance.m_Net != null) s_Instance.Send("leave");
        }

        private void Update()
        {
            OnlineMatch.Update();
            if (m_Net == null) return;
            foreach (NetClient.Incoming m in m_Net.Poll())
            {
                if (m.Relay != null) OnlineMatch.OnRelay(m.Relay);
                else
                {
                    try { Handle(m.Json); }
                    catch (Exception e) { Debug.LogWarning("[HexRevival] [Online] " + e.Message); }
                }
            }
            if (m_Net.State == NetClient.Status.Closed)
            {
                m_Message = Loc.Get("online_closed", ErrorText(m_Net.CloseReason));
                Debug.Log("[HexRevival] [Online] connection closed: " + m_Net.CloseReason);
                m_Net = null;
                m_Players.Clear();
                if (OnlineMatch.Current != OnlineMatch.Role.None) Notices.Show(m_Message);
                OnlineMatch.End();
            }
        }

        private void Handle(JObject m)
        {
            switch ((string)m["t"])
            {
                case "welcome":
                    m_Message = null;
                    m_Players.Clear();
                    foreach (JToken p in (JArray)m["players"]) m_Players.Add(ToPlayer(p));
                    break;
                case "joined": m_Players.Add(ToPlayer(m["player"])); break;
                case "left":
                    m_Players.RemoveAll(p => p.Id == (string)m["id"]);
                    m_Challenges.Remove((string)m["id"]);
                    break;
                case "status":
                    foreach (Player p in m_Players) if (p.Id == (string)m["id"]) p.Status = (string)m["status"];
                    break;
                case "chat":
                    AddChat((string)m["name"] + ": " + (string)m["text"]);
                    break;
                case "challenged":
                    m_Challenges[(string)m["from"]] = (string)m["name"];
                    AddChat(Loc.Get("online_challenged", (string)m["name"]));
                    if (!OfflineMenu.IsOpen) Notices.Show(Loc.Get("online_challenged", (string)m["name"]));
                    break;
                case "challenge_sent": m_ChallengedId = (string)m["to"]; break;
                case "challenge_end":
                    string with = (string)m["with"];
                    if (with == m_ChallengedId) { m_ChallengedId = null; AddChat(Loc.Get("online_challenge_" + (string)m["reason"], NameOf(with))); }
                    m_Challenges.Remove(with);
                    break;
                case "match":
                    m_ChallengedId = null;
                    m_Challenges.Clear();
                    JObject opp = (JObject)m["opponent"];
                    OnlineMatch.Begin(m_Net, (int)m["seat"] == 0, (string)opp["name"], m_Deck);
                    break;
                case "match_end":
                    if (OnlineMatch.Current != OnlineMatch.Role.None)
                        Notices.Show(Loc.Get("online_match_end"));
                    break;
                case "error":
                case "notice":
                    m_Message = (string)m["msg"];
                    break;
            }
        }

        /// <summary>A server error code in words (the code itself when there is no text for it).</summary>
        private static string ErrorText(string code)
        {
            string key = "online_err_" + code, text = Loc.Get(key);
            return text == key ? code : text;
        }

        private static Player ToPlayer(JToken p) { return new Player { Id = (string)p["id"], Name = (string)p["name"], Status = (string)p["status"] }; }

        private string NameOf(string id)
        {
            Player p = m_Players.Find(x => x.Id == id);
            return p != null ? p.Name : "?";
        }

        private void AddChat(string line)
        {
            m_Chat.Add(line);
            if (m_Chat.Count > 200) m_Chat.RemoveAt(0);
            m_ScrollChat.y = float.MaxValue;
        }

        private void Send(string type, string key = null, string value = null)
        {
            if (m_Net == null) return;
            JObject msg = new JObject();
            msg["t"] = type;
            if (key != null) msg[key] = value;
            m_Net.Send(msg);
        }

        // ------------------------------------------------------------------
        // Drawing (inside the Quick Match menu)
        // ------------------------------------------------------------------

        /// <summary>The Online tab; deck is the player's deck chosen on the left (used if a game starts).</summary>
        public static void Draw(Rect area, UID deck)
        {
            if (s_Instance != null) s_Instance.DrawTab(area, deck);
        }

        private void DrawTab(Rect area, UID deck)
        {
            m_Deck = deck;
            if (m_Net == null && m_Message == null) Connect();
            float S = GameSkin.Scale;
            OnlineProfile op = OnlineProfile.Current;
            string status = Online ? Loc.Get("online_as", op.Name) : m_Net != null ? Loc.Get("online_connecting") : (m_Message ?? "");
            GUI.Label(new Rect(area.x, area.y, area.width - 170 * S, 30 * S), status, GameSkin.Style("body", 14, Online ? GameSkin.Gold : GameSkin.Muted));
            if (m_Net == null && GameSkin.GoldButton(new Rect(area.xMax - 160 * S, area.y, 160 * S, 32 * S), Loc.Get("online_connect"), true, 14)) { m_Message = null; Connect(); }
            if (!Online) return;
            if (m_Message != null)
                GUI.Label(new Rect(area.x, area.y + 28 * S, area.width, 22 * S), m_Message, GameSkin.Style("body", 13, new Color(1f, 0.55f, 0.45f)));

            // Players (top), chat (bottom).
            float top = area.y + 52 * S, chatH = area.height * 0.42f;
            Rect players = new Rect(area.x, top, area.width, area.yMax - top - chatH - 8 * S);
            float rowH = 44 * S;
            List<Player> others = m_Players;
            if (others.Count == 0)
                GUI.Label(players, Loc.Get("online_alone"), GameSkin.Style("body", 14, GameSkin.Muted, TextAnchor.MiddleCenter, true));
            m_ScrollPlayers = Controls.List(players, m_ScrollPlayers, others.Count, rowH, i => DrawPlayer(new Rect(0, i * (rowH + 4 * S), players.width - 20 * S, rowH), others[i], deck), i => { });

            Rect chat = new Rect(area.x, area.yMax - chatH, area.width, chatH - 40 * S);
            GameSkin.Sprite(chat, "A_Frames", "Frame_Inner");
            GUI.DrawTexture(new Rect(chat.x + 2, chat.y + 2, chat.width - 4, chat.height - 4), GameSkin.Solid(new Color(0, 0, 0, 0.45f)));
            GUIStyle line = GameSkin.Style("body", 13, GameSkin.Text, TextAnchor.UpperLeft, true);
            float lineH = 20 * S;
            Rect view = new Rect(0, 0, chat.width - 24 * S, m_Chat.Count * lineH + 8 * S);
            m_ScrollChat = GUI.BeginScrollView(new Rect(chat.x + 6 * S, chat.y + 4 * S, chat.width - 8 * S, chat.height - 8 * S), m_ScrollChat, view, false, false, GUIStyle.none, view.height > chat.height ? GameSkin.ScrollBar : GUIStyle.none);
            for (int i = 0; i < m_Chat.Count; i++) GUI.Label(new Rect(0, i * lineH, view.width, lineH), m_Chat[i], line);
            GUI.EndScrollView();

            Rect input = new Rect(area.x, area.yMax - 34 * S, area.width - 130 * S, 34 * S);
            GUI.SetNextControlName("hexrevival_chat");
            m_ChatInput = Controls.TextField(input, m_ChatInput, 300);
            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                && GUI.GetNameOfFocusedControl() == "hexrevival_chat";
            if ((GameSkin.GoldButton(new Rect(input.xMax + 8 * S, input.y, 122 * S, 34 * S), Loc.Get("online_send"), true, 14) || enter) && m_ChatInput.Trim().Length > 0)
            {
                Send("chat", "text", m_ChatInput.Trim());
                m_ChatInput = "";
                if (enter) Event.current.Use();
            }
        }

        /// <summary>A player's row: name, status, then the challenge buttons. Returns false (the row itself is not clickable).</summary>
        private bool DrawPlayer(Rect r, Player p, UID deck)
        {
            float S = GameSkin.Scale;
            GameSkin.Sprite(r, "A_Buttons", "Button_Bar_Main_Mid");
            bool me = p.Id == OnlineProfile.Current.PlayerId;
            string detail = me ? Loc.Get("online_you") : p.Status == "playing" ? Loc.Get("online_playing") : "";
            GameSkin.Label(new Rect(r.x + 14 * S, r.y, r.width * 0.45f, r.height), p.Name, GameSkin.Style("title", 16, GameSkin.Gold));
            GUI.Label(new Rect(r.x + r.width * 0.45f, r.y, r.width * 0.2f, r.height), detail, GameSkin.Style("body", 13, GameSkin.Muted));
            if (me || p.Status == "playing") return false;
            float bw = 120 * S;
            Rect b1 = new Rect(r.xMax - bw - 6 * S, r.y + 5 * S, bw, r.height - 10 * S), b2 = new Rect(b1.x - bw - 6 * S, b1.y, bw, b1.height);
            bool hasDeck = deck.IsValid();
            if (m_Challenges.ContainsKey(p.Id))
            {
                if (GameSkin.GreenButton(b1, Loc.Get("online_accept"), hasDeck, 14)) Send("accept", "from", p.Id);
                if (GameSkin.RedButton(b2, Loc.Get("online_decline"), 14)) { Send("decline", "from", p.Id); m_Challenges.Remove(p.Id); }
            }
            else if (m_ChallengedId == p.Id)
            {
                if (GameSkin.RedButton(b1, Loc.Get("cancel"), 14)) { Send("cancel"); m_ChallengedId = null; }
            }
            else if (GameSkin.GoldButton(b1, Loc.Get("online_challenge"), hasDeck && m_ChallengedId == null, 14))
            {
                JObject msg = new JObject();
                msg["t"] = "challenge";
                msg["to"] = p.Id;
                msg["format"] = "constructed";
                m_Net.Send(msg);
            }
            return false;
        }

        /// <summary>Dev: "online connect|disconnect|status|chat &lt;text&gt;|challenge &lt;name&gt;|accept &lt;name&gt;|leave".</summary>
        public static string Command(string[] parts)
        {
            OnlineLobby l = s_Instance;
            if (l == null) return "no lobby";
            string sub = parts.Length > 1 ? parts[1] : "status";
            string arg = parts.Length > 2 ? string.Join(" ", parts, 2, parts.Length - 2) : "";
            Player target = l.m_Players.Find(p => string.Equals(p.Name, arg, StringComparison.OrdinalIgnoreCase));
            if (l.m_Deck == UID.Invalid) l.m_Deck = Decks.LastDeck;
            switch (sub)
            {
                case "connect": Connect(); return "connecting";
                case "disconnect": Disconnect(); return "disconnected";
                case "chat": l.Send("chat", "text", arg); return "sent";
                case "challenge":
                    if (target == null) return "no player " + arg;
                    JObject msg = new JObject();
                    msg["t"] = "challenge"; msg["to"] = target.Id; msg["format"] = "constructed";
                    l.m_Net.Send(msg);
                    return "challenged " + target.Name + " with deck " + l.m_Deck;
                case "accept":
                    if (target == null) return "no player " + arg;
                    l.Send("accept", "from", target.Id);
                    return "accepted, deck " + l.m_Deck;
                case "leave": LeaveMatch(); return "left";
            }
            List<string> names = l.m_Players.ConvertAll(p => p.Name + (p.Status == "playing" ? "*" : ""));
            return (l.m_Net == null ? "offline" : l.m_Net.State.ToString()) + " msg=" + l.m_Message + " match=" + OnlineMatch.Current
                + " players: " + string.Join(", ", names.ToArray()) + " challenges: " + string.Join(", ", new List<string>(l.m_Challenges.Values).ToArray());
        }
    }
}
