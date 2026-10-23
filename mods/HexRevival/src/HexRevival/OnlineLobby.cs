using System;
using System.Collections.Generic;
using Game.Shared;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// The online lobby: the main screen of the online profile (chosen on the login screen, see Saves.Online); offline
    /// accounts never see it. Left, the deck to play (favorites or every deck); middle, the challenges received, the
    /// open games (join one) and "Host a game"; right, the connected players (to challenge one) and the chat. Below:
    /// deck editor, game settings, online profile, log out. A game joined or a challenge accepted starts the match
    /// (see OnlineMatch). The connection opens with the lobby and stays open during the games.
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
        private class ChallengeIn
        {
            public string Name, Format;
        }

        /// <summary>Who challenged us (player id -> name and format).</summary>
        private readonly Dictionary<string, ChallengeIn> m_Challenges = new Dictionary<string, ChallengeIn>();
        /// <summary>The format the deck list shows ("" every deck); joining or answering a game of a format selects it.</summary>
        private string m_Format = "";
        private readonly List<OpenGame> m_Games = new List<OpenGame>();
        private UID m_Deck = UID.Invalid;
        private Vector2 m_ScrollPlayers, m_ScrollChat, m_ScrollGames, m_ScrollDecks;

        // The screen
        private bool m_Open, m_AllDecks, m_SettingsShown;
        private readonly ProfileForm m_Profile = new ProfileForm();
        private bool m_EditingProfile;
        private float m_ReopenAfter, m_NoSystemMenuUntil;
        /// <summary>The name asked for at login when the server refused it (taken by another player or reserved).</summary>
        private string m_NameRefused;
        private HashSet<ulong> m_Favorites;
        /// <summary>The player decks by account (the decks made for the AI are only opponents, see OfflineMenu).</summary>
        private List<Decks.DeckGroup> m_Groups = new List<Decks.DeckGroup>();
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
            // Esc on the lobby = "Switch account" (the online account's way back, as Esc leaves the game's other
            // screens); on the profile form, it closes the form. The game's own Esc menu is kept closed meanwhile.
            if (m_Open && !OfflineMenu.IsOpen && Input.GetKeyDown(KeyCode.Escape))
            {
                if (m_EditingProfile) m_EditingProfile = false;
                else { m_NoSystemMenuUntil = Time.realtimeSinceStartup + 2f; LogOut(); }
            }
            if (Time.realtimeSinceStartup < m_NoSystemMenuUntil && UISystem.IsTaggedUIElementVisible("SystemMenu"))
                UISystem.SetTaggedUIElementVisible("SystemMenu", false);
            OnlineMatch.Update();
            if (m_Open && (!OnLandingPage() || GameMenuShown())) Close();
            // The online account lives in the lobby: back on the main menu (after a game, the deck editor, the game's
            // settings), it opens again.
            if (!m_Open && Saves.IsOnline && OnLandingPage() && !OfflineMenu.IsOpen && !GameMenuShown() && Time.realtimeSinceStartup > m_ReopenAfter) Open();
            m_Block.Update(m_Open && !m_SettingsShown);
            // An offline account has no online features: a connection left from the online account closes.
            if (!Saves.IsOnline && m_Net != null && OnlineMatch.Current == OnlineMatch.Role.None) Disconnect();
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
                    WelcomeName((string)m["name"]);
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
                    m_Challenges[(string)m["from"]] = new ChallengeIn { Name = (string)m["name"], Format = (string)m["format"] ?? OnlineFormats.Default };
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
                    OnlineMatch.Begin(m_Net, (int)m["seat"] == 0, (string)opp["name"], m_Deck, (string)m["format"]);
                    break;
                case "match_end":
                    if (OnlineMatch.Current != OnlineMatch.Role.None)
                        Notices.Show(Loc.Get("online_match_end"));
                    break;
                case "error":
                case "notice":
                    string code = (string)m["code"];
                    m_Message = Loc.Get("online_err_" + code) != "online_err_" + code ? ErrorText(code) : (string)m["msg"];
                    if (code == "name_taken" && (string)m["t"] == "notice") m_NameRefused = OnlineProfile.Current.Name;
                    else if (code == "name_taken" || code == "bad_name")
                    {
                        // Registration refused: the profile form, to choose another name.
                        m_Profile.Load();
                        m_Profile.Error = ErrorText(code);
                        m_EditingProfile = true;
                    }
                    break;
            }
        }

        /// <summary>
        /// The name the server gave us is the one shown (in the lobby and in battle). When the name asked for was
        /// refused (another player has it, or it is reserved), the profile keeps the account's name and its form opens
        /// to say so.
        /// </summary>
        private void WelcomeName(string name)
        {
            OnlineProfile op = OnlineProfile.Current;
            if (!string.IsNullOrEmpty(name) && name != op.Name) { op.Name = name; OnlineProfile.Save(); }
            // The game's own name of the player (bottom bar, battle), set at login from the profile.
            try
            {
                var pp = Singleton<Main>.Instance.m_GameClient.PlayerProfile;
                if (!string.IsNullOrEmpty(name) && pp != null && pp.Keep != null) pp.Keep.Name = name;
                GameObject bar = GameObject.Find("Bottom_Nav/Anchor_L/Account_XP/XP_Bar/Label_Name");
                UILabel label = bar != null ? bar.GetComponent<UILabel>() : null;
                if (label != null && !string.IsNullOrEmpty(name)) label.text = name;
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Online] name: " + e.Message); }
            if (m_NameRefused == null) return;
            m_Profile.Load();
            m_Profile.Error = Loc.Get("online_name_kept", m_NameRefused, name);
            m_EditingProfile = true;
            m_NameRefused = null;
            m_Message = null;
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
            // The format the deck list shows (any deck: free).
            msg["format"] = string.IsNullOrEmpty(m_Format) ? "free" : m_Format;
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
            OnlineFormats.ClearCache();   // the deck editor may have changed decks
            l.m_Groups = Decks.ByOwner(false);
            l.m_Decks = new List<Decks.DeckInfo>();
            foreach (Decks.DeckGroup g in l.m_Groups) l.m_Decks.AddRange(g.Decks);
            if (!l.m_Decks.Exists(d => d.Id == l.m_Deck))
                l.m_Deck = l.m_Decks.Exists(d => d.Id == Decks.LastDeck) ? Decks.LastDeck : l.m_Decks.Count > 0 ? l.m_Decks[0].Id : UID.Invalid;
            l.m_Open = true;
            l.m_SettingsShown = false;
            if (l.m_Net == null) { l.m_Message = null; Connect(); }
        }

        // ------------------------------------------------------------------
        // Favorite decks (the online account sees every deck; the favorites come first)
        // ------------------------------------------------------------------

        private HashSet<ulong> Favorites
        {
            get
            {
                if (m_Favorites != null) return m_Favorites;
                m_Favorites = new HashSet<ulong>();
                try
                {
                    string f = Saves.File("favorites.txt");
                    if (System.IO.File.Exists(f))
                        foreach (string line in System.IO.File.ReadAllLines(f)) { ulong id; if (ulong.TryParse(line.Trim(), out id)) m_Favorites.Add(id); }
                }
                catch (Exception e) { Debug.LogWarning("[HexRevival] [Online] favorites: " + e.Message); }
                return m_Favorites;
            }
        }

        private void ToggleFavorite(UID deck)
        {
            ulong id = deck.GetInstanceId();
            if (!Favorites.Remove(id)) Favorites.Add(id);
            try
            {
                List<string> lines = new List<string>();
                foreach (ulong f in Favorites) lines.Add(f.ToString());
                System.IO.File.WriteAllLines(Saves.File("favorites.txt"), lines.ToArray());
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Online] favorites: " + e.Message); }
        }

        /// <summary>
        /// The game's options screen (the lobby's "Game settings") or its system menu (Esc: Resume, Settings, Help, Exit):
        /// the lobby gives way to them and comes back when they close.
        /// </summary>
        private static bool GameMenuShown()
        {
            try
            {
                if (UISystem.IsTaggedUIElementVisible("SystemMenu")) return true;
                UIOptionsScreenViewModel o = FindObjectOfType<UIOptionsScreenViewModel>();
                return o != null && o.m_Context != null && o.IsVisible();
            }
            catch { return false; }
        }

        private void ShowGameSettings()
        {
            Close();
            try { UISystem.SetTaggedUIElementVisible("OptionsScreen", true, true); }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Online] settings: " + e.Message); }
        }

        private void OpenDeckEditor()
        {
            Close();
            try { Singleton<Main>.Instance.m_GameClient.StateManager.TransitionToState(Reckoning.Game.EGameState.CardCollection, null); }
            catch (Exception e) { Debug.LogError("[HexRevival] [Online] deck editor: " + e); }
        }

        private void LogOut()
        {
            Disconnect();
            Close();
            try { Singleton<Main>.Instance.RequestLogOut(); }
            catch (Exception e) { Debug.LogError("[HexRevival] [Online] log out: " + e); }
        }

        /// <summary>The "Host a game" menu opens a game in the lobby, played with this deck.</summary>
        public static void Host(UID deck, string format)
        {
            OnlineLobby l = s_Instance;
            if (l == null) return;
            l.m_Deck = deck;
            l.m_Format = format;
            l.Send("create", "format", format);
        }

        private void Close()
        {
            m_Open = false;
            // Leaving for another screen: the main menu is still shown for a moment.
            m_ReopenAfter = Time.realtimeSinceStartup + 3f;
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
            if (!m_EditingProfile)
            {
                // A newer version (other players may have it), then the Discord server.
                if (Community.NewVersion != null)
                {
                    Banner(new Rect(win.x + pad, top, win.width - 2 * pad, 40 * S), Loc.Get("lobby_update", Community.NewVersion), new Color(1f, 0.72f, 0.35f),
                           Loc.Get("lobby_update_get"), Community.NewVersionUrl);
                    top += 46 * S;
                }
                Banner(new Rect(win.x + pad, top, win.width - 2 * pad, 40 * S), Loc.Get("lobby_discord"), GameSkin.Gold, Loc.Get("lobby_discord_join"), Community.DiscordUrl);
                top += 50 * S;
            }

            float w = win.width - 2 * pad - 2 * gap;
            Rect left = new Rect(win.x + pad, top, w * 0.31f, bottom - top);
            Rect mid = new Rect(left.xMax + gap, top, w * 0.36f, bottom - top);
            Rect right = new Rect(mid.xMax + gap, top, w * 0.33f, bottom - top);
            if (m_EditingProfile)
            {
                DrawProfile(new Rect(win.x + pad + 120 * S, top, win.width - 2 * pad - 240 * S, win.yMax - top - 20 * S));
                return;
            }
            DrawDecks(left);
            DrawGames(mid);
            float playersH = right.height * 0.42f;
            DrawPlayers(new Rect(right.x, right.y, right.width, playersH));
            DrawChat(new Rect(right.x, right.y + playersH + gap, right.width, right.height - playersH - gap));

            // The online account's buttons.
            float by = win.yMax - 72 * S, bw = 230 * S, bh = 52 * S;
            if (GameSkin.GoldButton(new Rect(win.x + pad, by, bw, bh), Loc.Get("online_deck_editor"), true, 17)) OpenDeckEditor();
            if (GameSkin.GoldButton(new Rect(win.x + pad + bw + 12 * S, by, bw, bh), Loc.Get("online_settings"), true, 17)) ShowGameSettings();
            if (GameSkin.GoldButton(new Rect(win.x + pad + 2 * (bw + 12 * S), by, bw, bh), Loc.Get("online_edit_profile"), true, 17)) { m_Profile.Load(); m_EditingProfile = true; }
            if (GameSkin.RedButton(new Rect(win.xMax - pad - bw, by, bw, bh), Loc.Get("switch_account"), 17)) LogOut();
        }

        /// <summary>A one-line notice across the window with a button that opens a web page.</summary>
        private static void Banner(Rect r, string text, Color color, string button, string url)
        {
            float S = GameSkin.Scale;
            GameSkin.Sprite(r, "A_Frames", "Frame_Generic_Inner");
            float bw = 220 * S;
            GameSkin.Label(new Rect(r.x + 16 * S, r.y, r.width - bw - 40 * S, r.height), text, GameSkin.Style("title", 16, color));
            if (GameSkin.GoldButton(new Rect(r.xMax - bw - 6 * S, r.y + 4 * S, bw, r.height - 8 * S), button, true, 14)) Community.Open(url);
        }

        /// <summary>The online profile's form; saving reconnects with it.</summary>
        private void DrawProfile(Rect area)
        {
            float S = GameSkin.Scale;
            m_Profile.Draw(area);
            float by = area.yMax - 56 * S;
            if (GameSkin.RedButton(new Rect(area.x, by, 180 * S, 52 * S), Loc.Get("cancel"))) m_EditingProfile = false;
            if ((GameSkin.GreenButton(new Rect(area.xMax - 260 * S, by, 260 * S, 52 * S), Loc.Get("online_save")) || ProfileForm.SubmitPressed()) && m_Profile.Save())
            {
                m_EditingProfile = false;
                Disconnect();
                Connect();
            }
        }

        /// <summary>
        /// The deck to play: the favorites (when there are some), or every player deck by account (the online profile's
        /// own first, then each offline account's, which can be played but not edited here); "+" / "-" marks a favorite.
        /// </summary>
        private void DrawDecks(Rect r)
        {
            float S = GameSkin.Scale;
            Controls.Frame(r, Loc.Get("online_your_deck"));
            bool all = m_AllDecks || !m_Decks.Exists(d => Favorites.Contains(d.Id.GetInstanceId()));
            // Format: only the decks valid for it.
            string[] formats = new string[OnlineFormats.All.Length + 1];
            formats[0] = "";
            OnlineFormats.All.CopyTo(formats, 1);
            m_Format = Controls.Cycle(new Rect(r.x + 14 * S, r.y + 42 * S, r.width - 28 * S, 30 * S), Loc.Get("online_format_filter"), m_Format, formats, OnlineFormats.Name);
            float tabW = (r.width - 28 * S) / 2f, tabY = r.y + 78 * S;
            if (GameSkin.Tab(new Rect(r.x + 14 * S, tabY, tabW, 30 * S), Loc.Get("online_favorites"), !all)) m_AllDecks = false;
            if (GameSkin.Tab(new Rect(r.x + 14 * S + tabW, tabY, tabW, 30 * S), Loc.Get("online_all_decks"), all)) m_AllDecks = true;

            // Rows: a title per account, then its decks.
            List<KeyValuePair<string, Decks.DeckInfo>> rows = new List<KeyValuePair<string, Decks.DeckInfo>>();
            if (all)
                foreach (Decks.DeckGroup g in m_Groups)
                {
                    List<Decks.DeckInfo> valid = g.Decks.FindAll(d => OnlineFormats.Accepts(m_Format, d.Id));
                    if (valid.Count == 0) continue;
                    rows.Add(new KeyValuePair<string, Decks.DeckInfo>(g.Title, null));
                    foreach (Decks.DeckInfo d in valid) rows.Add(new KeyValuePair<string, Decks.DeckInfo>(null, d));
                }
            else
                foreach (Decks.DeckInfo d in m_Decks)
                    if (Favorites.Contains(d.Id.GetInstanceId()) && OnlineFormats.Accepts(m_Format, d.Id)) rows.Add(new KeyValuePair<string, Decks.DeckInfo>(null, d));

            Rect list = new Rect(r.x + 10 * S, tabY + 38 * S, r.width - 20 * S, r.yMax - tabY - 48 * S);
            if (rows.Count == 0)
            {
                GUI.Label(list, Loc.Get(m_Decks.Count == 0 ? "online_no_deck" : m_Format != "" ? "online_no_valid_deck" : "online_no_favorite"), GameSkin.Style("body", 14, GameSkin.Muted, TextAnchor.MiddleCenter, true));
                return;
            }
            float rowH = 64 * S, headH = 30 * S, gap = 4 * S, fw = 34 * S;
            float contentH = 0;
            foreach (KeyValuePair<string, Decks.DeckInfo> row in rows) contentH += (row.Value == null ? headH : rowH) + gap;
            Rect view = new Rect(0, 0, list.width - 20 * S, contentH);
            m_ScrollDecks = GUI.BeginScrollView(list, m_ScrollDecks, view, false, false, GUIStyle.none, contentH > list.height ? GameSkin.ScrollBar : GUIStyle.none);
            float y = 0;
            foreach (KeyValuePair<string, Decks.DeckInfo> row in rows)
            {
                if (row.Value == null)
                {
                    // Separator: the account's name over a thin line.
                    GameSkin.Label(new Rect(4 * S, y + 2 * S, view.width - 8 * S, headH - 6 * S), row.Key, GameSkin.Style("title", 14, GameSkin.GoldDim));
                    GUI.DrawTexture(new Rect(4 * S, y + headH - 3 * S, view.width - 8 * S, 1), GameSkin.Solid(new Color(0.8f, 0.72f, 0.57f, 0.5f)));
                    y += headH + gap;
                    continue;
                }
                Decks.DeckInfo d = row.Value;
                Rect rr = new Rect(0, y, view.width, rowH);
                y += rowH + gap;
                bool fav = Favorites.Contains(d.Id.GetInstanceId());
                // The favorite button handles the click before the row (whose button covers it), and is drawn after it.
                Rect fb = new Rect(rr.xMax - fw - 8 * S, rr.y + (rowH - fw) / 2f, fw, fw);
                if (Event.current.type != EventType.Repaint && Controls.Small(fb, fav ? "-" : "+")) { ToggleFavorite(d.Id); continue; }
                string detail = Loc.Get("cards", d.Count) + (d.Campaign ? " - " + Loc.Get("deck_campaign") : "");
                if (GameSkin.Row(rr, d.Name, detail, d.Id == m_Deck, OfflineMenu.SafeChampion(d.Champion), fw + 12 * S)) m_Deck = d.Id;
                if (Event.current.type == EventType.Repaint) Controls.Small(fb, fav ? "-" : "+");
            }
            GUI.EndScrollView();
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
                if (GameSkin.GreenButton(create, Loc.Get("host_game"), true, 18)) OfflineMenu.Open(m_Deck, m_Format);
            }
            else if (GameSkin.RedButton(create, Loc.Get("online_close_game"), 18)) Send("close");

            // One list: challenges, our game, the others' games.
            List<KeyValuePair<string, ChallengeIn>> challenges = new List<KeyValuePair<string, ChallengeIn>>(m_Challenges);
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
                // The buttons handle their click before the row's own (which shows the decks valid for its format).
                if (i < challenges.Count)
                {
                    KeyValuePair<string, ChallengeIn> c = challenges[i];
                    bool valid = ready && OnlineFormats.Accepts(c.Value.Format, m_Deck);
                    if (GameSkin.GreenButton(b1, Loc.Get("online_accept"), valid, 14)) Send("accept", "from", c.Key);
                    if (GameSkin.RedButton(b2, Loc.Get("online_decline"), 14)) { Send("decline", "from", c.Key); m_Challenges.Remove(c.Key); }
                    GameSkin.Label(new Rect(text.x, text.y + 4 * S, text.width, 26 * S), Loc.Get("online_challenge_row", c.Value.Name), GameSkin.Style("title", 15, GameSkin.Gold));
                    GUI.Label(new Rect(text.x, text.y + 28 * S, text.width, 22 * S), OnlineFormats.Name(c.Value.Format) + (ready && !valid ? " - " + Loc.Get("online_deck_not_valid") : ""), GameSkin.Style("body", 13, GameSkin.Muted));
                    if (GUI.Button(text, GUIContent.none, GUIStyle.none)) m_Format = c.Value.Format;
                    return false;
                }
                OpenGame g = games[i - challenges.Count];
                bool own = g == mine;
                bool ok = ready && OnlineFormats.Accepts(g.Format, m_Deck);
                // One button (Join), none on our own game: the text takes the room left.
                text.width = (own ? row.xMax - 14 * S : b1.x - 6 * S) - text.x;
                if (!own && GameSkin.GreenButton(b1, Loc.Get("online_join_game"), ok, 14)) Send("join", "game", g.Id);
                GameSkin.Label(new Rect(text.x, text.y + 4 * S, text.width, 26 * S), own ? Loc.Get("online_your_game") : g.Name, GameSkin.Style("title", 15, GameSkin.Gold));
                GUI.Label(new Rect(text.x, text.y + 28 * S, text.width, 22 * S), OnlineFormats.Name(g.Format) + (own ? " - " + Loc.Get("online_waiting") : ready && !ok ? " - " + Loc.Get("online_deck_not_valid") : ""), GameSkin.Style("body", 13, GameSkin.Muted));
                if (GUI.Button(text, GUIContent.none, GUIStyle.none)) m_Format = g.Format;
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
                else if (GameSkin.GoldButton(b, Loc.Get("online_challenge"), Online && OnlineFormats.Accepts(m_Format, m_Deck) && m_ChallengedId == null, 13)) Challenge(p.Id);
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
            // Enter is read before the text field, which takes the key for itself.
            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                && GUI.GetNameOfFocusedControl() == "hexrevival_chat";
            GUI.SetNextControlName("hexrevival_chat");
            m_ChatInput = Controls.TextField(input, m_ChatInput, 300);
            if ((GameSkin.GoldButton(new Rect(input.xMax + 8 * S, input.y, 112 * S, 36 * S), Loc.Get("online_send"), Online, 14) || enter) && m_ChatInput.Trim().Length > 0)
            {
                Send("chat", "text", m_ChatInput.Trim());
                m_ChatInput = "";
                if (enter) Event.current.Use();
            }
        }

        /// <summary>Dev: "online open|close|host|connect|disconnect|status|chat &lt;text&gt;|challenge &lt;name&gt;|accept &lt;name&gt;|create|join &lt;name&gt;|leave".</summary>
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
                case "profile": l.m_Profile.Load(); l.m_EditingProfile = !l.m_EditingProfile; return "profile " + l.m_EditingProfile;
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
                case "create": l.Send("create", "format", arg.Length > 0 ? arg : OnlineFormats.Default); return "created, deck " + l.m_Deck;
                case "host": OfflineMenu.Open(l.m_Deck, l.m_Format); return "host menu";
                case "format": l.m_Format = arg; return "format '" + arg + "'";
                case "sets":
                    {
                        string[] a2 = arg.Split(' ');
                        Decks.DeckInfo dd = Decks.ListAll().Find(x => string.Equals(x.Name, a2[0], StringComparison.OrdinalIgnoreCase));
                        return dd == null ? "no deck " + a2[0] : OnlineFormats.Sets(dd.Id, a2.Length > 1 ? a2[1] : "standard");
                    }
                case "deck":
                    Decks.DeckInfo dk = l.m_Decks.Find(x => string.Equals(x.Name, arg, StringComparison.OrdinalIgnoreCase));
                    if (dk == null) return "no deck " + arg;
                    l.m_Deck = dk.Id;
                    return "deck " + dk.Id;
                case "decks":
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    foreach (Decks.DeckGroup gr in Decks.ByOwner(false))
                    {
                        sb.Append("[").Append(gr.Title).Append("]\n");
                        foreach (Decks.DeckInfo d in gr.Decks)
                        {
                            sb.Append("  ").Append(d.Name).Append(" ").Append(d.Id).Append(d.Campaign ? " campaign" : "").Append(" :");
                            foreach (string f in OnlineFormats.All)
                            {
                                List<string> errors = new List<string>();
                                bool ok = OnlineFormats.Validate(f, d.Id, errors);
                                sb.Append(" ").Append(f).Append(ok ? "=ok" : "=no(" + string.Join(";", errors.ToArray()) + ")");
                            }
                            sb.Append("\n");
                        }
                    }
                    foreach (Decks.DeckGroup gr in Decks.ByOwner(true))
                    {
                        sb.Append("[").Append(gr.Title).Append("]\n");
                        foreach (Decks.DeckInfo d in gr.Decks) sb.Append("  ").Append(d.Name).Append(" ").Append(d.Id).Append("\n");
                    }
                    return sb.ToString();
                case "settings": l.ShowGameSettings(); return "settings";
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
                + " challenges: " + string.Join(", ", new List<ChallengeIn>(l.m_Challenges.Values).ConvertAll(c => c.Name + ":" + c.Format).ToArray());
        }
    }
}
