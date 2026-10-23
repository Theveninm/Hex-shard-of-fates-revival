using System;
using System.Collections.Generic;
using Game.Shared;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// The online lobby: its own screen, opened from the main menu's "Online play" button. Left, the deck to play;
    /// middle, the open games (create one, join one) and the challenges received; right, the connected players (to
    /// challenge one) and the chat. A game joined or a challenge accepted starts the match (see OnlineMatch).
    /// The connection is opened with the online profile when the screen is first shown and stays open in the
    /// background: a challenge received meanwhile is announced.
    /// </summary>
    public class OnlineLobby : MonoBehaviour
    {
        private class Player
        {
            public string Id, Name, Status;
        }

        private class OpenGame
        {
            public int Id;
            public string Format, Host, Name;
        }

        private static OnlineLobby s_Instance;
        private NetClient m_Net;
        private readonly List<Player> m_Players = new List<Player>();
        private readonly List<string> m_Chat = new List<string>();
        private string m_ChatInput = "";
        private string m_Message;
        private string m_ChallengedId;                      // our challenge waiting for an answer
        private readonly Dictionary<string, string> m_Challenges = new Dictionary<string, string>();   // id -> name of who challenged us
        private readonly List<OpenGame> m_Games = new List<OpenGame>();
        private UID m_Deck = UID.Invalid;
        private Vector2 m_ScrollPlayers, m_ScrollChat, m_ScrollGames, m_ScrollDecks;

        // The screen
        private bool m_Open;
        private List<Decks.DeckInfo> m_Decks = new List<Decks.DeckInfo>();
        private readonly Controls.UiBlocker m_Block = new Controls.UiBlocker();

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
            l.m_Games.Clear();
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
            l.m_Games.Clear();
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
            if (m_Open && (!OnLandingPage() || Input.GetKeyDown(KeyCode.Escape))) Close();
            m_Block.Update(m_Open);
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
                m_Games.Clear();
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
                    m_Games.Clear();
                    if (m["games"] is JArray) foreach (JToken g in (JArray)m["games"]) m_Games.Add(ToGame(g));
                    break;
                case "game_added": m_Games.Add(ToGame(m["game"])); break;
                case "game_removed": m_Games.RemoveAll(g => g.Id == (int)m["game"]); break;
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
                    if (!m_Open) Notices.Show(Loc.Get("online_challenged", (string)m["name"]));
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

        private static OpenGame ToGame(JToken g) { return new OpenGame { Id = (int)g["id"], Format = (string)g["format"], Host = (string)g["host"], Name = (string)g["name"] }; }

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

        private void Send(string type, string key = null, JToken value = null)
        {
            if (m_Net == null) return;
            JObject msg = new JObject();
            msg["t"] = type;
            if (key != null) msg[key] = value;
            m_Net.Send(msg);
        }

        private void Challenge(string id)
        {
            if (m_Net == null) return;
            JObject msg = new JObject();
            msg["t"] = "challenge";
            msg["to"] = id;
            msg["format"] = "constructed";
            m_Net.Send(msg);
        }

        private string MyId { get { return OnlineProfile.Current.PlayerId; } }

        // ------------------------------------------------------------------
        // The screen
        // ------------------------------------------------------------------

        private static bool OnLandingPage()
        {
            try
            {
                Main main = Singleton<Main>.Instance;
                return main != null && main.m_GameClient != null && main.m_GameClient.StateManager != null
                    && main.m_GameClient.StateManager.CurrentGameState == Reckoning.Game.EGameState.LandingPage;
            }
            catch { return false; }
        }

        /// <summary>Opens the lobby (main menu's "Online play" button), connecting if needed.</summary>
        public static void Open()
        {
            OnlineLobby l = s_Instance;
            if (l == null) return;
            l.m_Decks = Decks.List();
            l.m_Decks.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            if (!l.m_Decks.Exists(d => d.Id == l.m_Deck))
                l.m_Deck = l.m_Decks.Exists(d => d.Id == Decks.LastDeck) ? Decks.LastDeck : l.m_Decks.Count > 0 ? l.m_Decks[0].Id : UID.Invalid;
            l.m_Open = true;
            if (l.m_Net == null) { l.m_Message = null; Connect(); }
        }

        /// <summary>The "Host a game" menu opens a game in the lobby, played with this deck.</summary>
        public static void Host(UID deck, string format)
        {
            OnlineLobby l = s_Instance;
            if (l == null) return;
            l.m_Deck = deck;
            l.Send("create", "format", format);
        }

        private void Close()
        {
            m_Open = false;
            m_Block.Release();
        }

        private void OnGUI()
        {
            // Under the "Host a game" menu while it is open.
            if (!m_Open || OfflineMenu.IsOpen) return;
            float S = GameSkin.Scale;
            GUI.depth = -1000;
            Rect win = Controls.Window(1240, 780, Loc.Get("online_title"));
            float pad = 34 * S, top = win.y + 60 * S, bottom = win.yMax - 84 * S, gap = 18 * S;

            // Status line: who we are, connection.
            OnlineProfile op = OnlineProfile.Current;
            string status = Online ? Loc.Get("online_as", op.Name) : m_Net != null ? Loc.Get("online_connecting") : (m_Message ?? "");
            GUI.Label(new Rect(win.x + pad, top, win.width - 2 * pad - 200 * S, 28 * S), status, GameSkin.Style("body", 15, Online ? GameSkin.Gold : GameSkin.Muted));
            if (Online && !string.IsNullOrEmpty(m_Message))
                GUI.Label(new Rect(win.x + pad + win.width * 0.35f, top, win.width * 0.45f, 28 * S), m_Message, GameSkin.Style("body", 13, new Color(1f, 0.55f, 0.45f)));
            if (m_Net == null && GameSkin.GoldButton(new Rect(win.xMax - pad - 190 * S, top - 2 * S, 190 * S, 32 * S), Loc.Get("online_connect"), true, 14)) { m_Message = null; Connect(); }
            top += 36 * S;

            float w = win.width - 2 * pad - 2 * gap;
            Rect left = new Rect(win.x + pad, top, w * 0.28f, bottom - top);
            Rect mid = new Rect(left.xMax + gap, top, w * 0.38f, bottom - top);
            Rect right = new Rect(mid.xMax + gap, top, w * 0.34f, bottom - top);
            DrawDecks(left);
            DrawGames(mid);
            float playersH = right.height * 0.42f;
            DrawPlayers(new Rect(right.x, right.y, right.width, playersH));
            DrawChat(new Rect(right.x, right.y + playersH + gap, right.width, right.height - playersH - gap));

            if (GameSkin.RedButton(new Rect(win.xMax - pad - 200 * S, win.yMax - 72 * S, 200 * S, 52 * S), Loc.Get("close"))) Close();
        }

        private void DrawDecks(Rect r)
        {
            float S = GameSkin.Scale;
            Controls.Frame(r, Loc.Get("your_deck"));
            Rect list = new Rect(r.x + 10 * S, r.y + 46 * S, r.width - 20 * S, r.height - 56 * S);
            if (m_Decks.Count == 0)
            {
                GUI.Label(list, Loc.Get("online_no_deck"), GameSkin.Style("body", 14, GameSkin.Muted, TextAnchor.MiddleCenter, true));
                return;
            }
            float rowH = 64 * S;
            m_ScrollDecks = Controls.List(list, m_ScrollDecks, m_Decks.Count, rowH, i =>
            {
                Decks.DeckInfo d = m_Decks[i];
                return GameSkin.Row(new Rect(0, i * (rowH + 4 * S), list.width - 20 * S, rowH), d.Name, Loc.Get("cards", d.Count), d.Id == m_Deck, OfflineMenu.SafeChampion(d.Champion));
            }, i => m_Deck = m_Decks[i].Id);
        }

        /// <summary>Challenges received first, then the open games (ours first), and "Create a game".</summary>
        private void DrawGames(Rect r)
        {
            float S = GameSkin.Scale;
            Controls.Frame(r, Loc.Get("online_games"));
            bool ready = Online && m_Deck.IsValid();
            OpenGame mine = m_Games.Find(g => g.Host == MyId);
            Rect create = new Rect(r.x + 14 * S, r.yMax - 62 * S, r.width - 28 * S, 50 * S);
            // "Host a game": against a player (opened here) or against the AI, works offline too.
            if (mine == null)
            {
                if (GameSkin.GreenButton(create, Loc.Get("host_game"), true, 18)) OfflineMenu.Open(m_Deck);
            }
            else if (GameSkin.RedButton(create, Loc.Get("online_close_game"), 18)) Send("close");

            // One list: challenges, our game, the others' games.
            List<KeyValuePair<string, string>> challenges = new List<KeyValuePair<string, string>>(m_Challenges);
            List<OpenGame> games = new List<OpenGame>();
            if (mine != null) games.Add(mine);
            games.AddRange(m_Games.FindAll(g => g != mine));
            int count = challenges.Count + games.Count;
            Rect list = new Rect(r.x + 10 * S, r.y + 46 * S, r.width - 20 * S, create.y - r.y - 54 * S);
            if (count == 0 && Online)
                GUI.Label(list, Loc.Get("online_no_games"), GameSkin.Style("body", 14, GameSkin.Muted, TextAnchor.MiddleCenter, true));
            float rowH = 54 * S, bw = 118 * S;
            m_ScrollGames = Controls.List(list, m_ScrollGames, count, rowH, i =>
            {
                Rect row = new Rect(0, i * (rowH + 4 * S), list.width - 20 * S, rowH);
                Rect b1 = new Rect(row.xMax - bw - 6 * S, row.y + 7 * S, bw, row.height - 14 * S), b2 = new Rect(b1.x - bw - 6 * S, b1.y, bw, b1.height);
                GameSkin.Sprite(row, "A_Buttons", "Button_Bar_Main_Mid");
                Rect text = new Rect(row.x + 14 * S, row.y, b2.x - row.x - 20 * S, row.height);
                if (i < challenges.Count)
                {
                    KeyValuePair<string, string> c = challenges[i];
                    GameSkin.Label(new Rect(text.x, text.y + 4 * S, text.width, 26 * S), Loc.Get("online_challenge_row", c.Value), GameSkin.Style("title", 15, GameSkin.Gold));
                    GUI.Label(new Rect(text.x, text.y + 28 * S, text.width, 22 * S), Loc.Get("format_constructed"), GameSkin.Style("body", 13, GameSkin.Muted));
                    if (GameSkin.GreenButton(b1, Loc.Get("online_accept"), ready, 14)) Send("accept", "from", c.Key);
                    if (GameSkin.RedButton(b2, Loc.Get("online_decline"), 14)) { Send("decline", "from", c.Key); m_Challenges.Remove(c.Key); }
                    return false;
                }
                OpenGame g = games[i - challenges.Count];
                bool own = g == mine;
                GameSkin.Label(new Rect(text.x, text.y + 4 * S, text.width, 26 * S), own ? Loc.Get("online_your_game") : g.Name, GameSkin.Style("title", 15, GameSkin.Gold));
                GUI.Label(new Rect(text.x, text.y + 28 * S, text.width, 22 * S), Loc.Get("format_" + g.Format) + (own ? " - " + Loc.Get("online_waiting") : ""), GameSkin.Style("body", 13, GameSkin.Muted));
                if (!own && GameSkin.GreenButton(b1, Loc.Get("online_join_game"), ready, 14)) Send("join", "game", g.Id);
                return false;
            }, i => { });
        }

        private void DrawPlayers(Rect r)
        {
            float S = GameSkin.Scale;
            Controls.Frame(r, Loc.Get("online_players"));
            Rect list = new Rect(r.x + 10 * S, r.y + 46 * S, r.width - 20 * S, r.height - 56 * S);
            if (m_Players.Count == 0 && Online)
                GUI.Label(list, Loc.Get("online_alone"), GameSkin.Style("body", 14, GameSkin.Muted, TextAnchor.MiddleCenter, true));
            float rowH = 44 * S, bw = 110 * S;
            m_ScrollPlayers = Controls.List(list, m_ScrollPlayers, m_Players.Count, rowH, i =>
            {
                Player p = m_Players[i];
                Rect row = new Rect(0, i * (rowH + 4 * S), list.width - 20 * S, rowH);
                GameSkin.Sprite(row, "A_Buttons", "Button_Bar_Main_Mid");
                Rect b = new Rect(row.xMax - bw - 6 * S, row.y + 5 * S, bw, row.height - 10 * S);
                GameSkin.Label(new Rect(row.x + 12 * S, row.y, b.x - row.x - 16 * S, row.height * 0.6f), p.Name, GameSkin.Style("title", 15, GameSkin.Gold));
                if (p.Status == "playing")
                    GUI.Label(new Rect(row.x + 12 * S, row.y + row.height * 0.5f, b.x - row.x - 16 * S, row.height * 0.5f), Loc.Get("online_playing"), GameSkin.Style("body", 12, GameSkin.Muted));
                else if (m_ChallengedId == p.Id)
                {
                    if (GameSkin.RedButton(b, Loc.Get("cancel"), 13)) { Send("cancel"); m_ChallengedId = null; }
                }
                else if (GameSkin.GoldButton(b, Loc.Get("online_challenge"), Online && m_Deck.IsValid() && m_ChallengedId == null, 13)) Challenge(p.Id);
                return false;
            }, i => { });
        }

        private void DrawChat(Rect r)
        {
            float S = GameSkin.Scale;
            Controls.Frame(r, Loc.Get("online_chat"));
            Rect box = new Rect(r.x + 10 * S, r.y + 44 * S, r.width - 20 * S, r.height - 98 * S);
            GUI.DrawTexture(box, GameSkin.Solid(new Color(0, 0, 0, 0.45f)));
            GUIStyle line = GameSkin.Style("body", 13, GameSkin.Text, TextAnchor.UpperLeft, true);
            float lineH = 20 * S;
            Rect view = new Rect(0, 0, box.width - 24 * S, m_Chat.Count * lineH + 8 * S);
            m_ScrollChat = GUI.BeginScrollView(new Rect(box.x + 6 * S, box.y + 4 * S, box.width - 8 * S, box.height - 8 * S), m_ScrollChat, view, false, false, GUIStyle.none, view.height > box.height ? GameSkin.ScrollBar : GUIStyle.none);
            for (int i = 0; i < m_Chat.Count; i++) GUI.Label(new Rect(0, i * lineH, view.width, lineH), m_Chat[i], line);
            GUI.EndScrollView();

            Rect input = new Rect(r.x + 10 * S, r.yMax - 46 * S, r.width - 140 * S, 36 * S);
            GUI.SetNextControlName("hexrevival_chat");
            m_ChatInput = Controls.TextField(input, m_ChatInput, 300);
            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                && GUI.GetNameOfFocusedControl() == "hexrevival_chat";
            if ((GameSkin.GoldButton(new Rect(input.xMax + 8 * S, input.y, 112 * S, 36 * S), Loc.Get("online_send"), Online, 14) || enter) && m_ChatInput.Trim().Length > 0)
            {
                Send("chat", "text", m_ChatInput.Trim());
                m_ChatInput = "";
                if (enter) Event.current.Use();
            }
        }

        /// <summary>Dev: "online open|close|connect|disconnect|status|chat &lt;text&gt;|challenge &lt;name&gt;|accept &lt;name&gt;|create|join &lt;name&gt;|leave".</summary>
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
                case "open": Open(); return "open";
                case "close": l.Close(); return "closed";
                case "connect": Connect(); return "connecting";
                case "disconnect": Disconnect(); return "disconnected";
                case "chat": l.Send("chat", "text", arg); return "sent";
                case "challenge":
                    if (target == null) return "no player " + arg;
                    l.Challenge(target.Id);
                    return "challenged " + target.Name + " with deck " + l.m_Deck;
                case "accept":
                    if (target == null) return "no player " + arg;
                    l.Send("accept", "from", target.Id);
                    return "accepted, deck " + l.m_Deck;
                case "create": l.Send("create", "format", "constructed"); return "created, deck " + l.m_Deck;
                case "join":
                    OpenGame g = l.m_Games.Find(x => string.Equals(x.Name, arg, StringComparison.OrdinalIgnoreCase));
                    if (g == null) return "no game by " + arg;
                    l.Send("join", "game", g.Id);
                    return "joining " + g.Name + ", deck " + l.m_Deck;
                case "leave": LeaveMatch(); return "left";
            }
            List<string> names = l.m_Players.ConvertAll(p => p.Name + (p.Status == "playing" ? "*" : ""));
            List<string> games = l.m_Games.ConvertAll(g => g.Name + ":" + g.Format);
            return (l.m_Net == null ? "offline" : l.m_Net.State.ToString()) + " open=" + l.m_Open + " msg=" + l.m_Message + " match=" + OnlineMatch.Current
                + " players: " + string.Join(", ", names.ToArray()) + " games: " + string.Join(", ", games.ToArray())
                + " challenges: " + string.Join(", ", new List<string>(l.m_Challenges.Values).ToArray());
        }
    }
}
