using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Game.Client;
using Game.Client.GameState;
using Game.Shared;
using Game.Shared.Mechanics;
using Game.Shared.Mechanics.Transactions;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// "Black box" + test harness.
    /// - Always on: writes a concise state to HexRevival.state.log (once per second) during a match.
    /// - Shortcuts: F8 = detailed dump, F9 = pass priority, F10 = play the first playable card,
    ///   F11 = declare no attacker and no blocker, F12 = force end of turn (auto-pass).
    /// - Full self-test only if the HexRevival.autotest marker exists.
    /// </summary>
    public class TestBehaviour : MonoBehaviour
    {
        private const string EncounterGuid = "6aca3608-0ef6-478b-a089-d76e94b13650";

        private bool m_Enabled;
        private bool m_AutoTest;
        /// <summary>HexRevival.autostart marker: login + start of the match, then the player takes over.</summary>
        private bool m_AutoStart;
        private bool m_AutoStartDone;
        private bool m_AutoAI;
        private bool m_ManualBlock;
        private float m_Speed = 1f;
        private bool m_Mute;
        private float m_KillAt = -1f;
        private float m_GameOverSince = -1f;
        private bool m_ContinuePressed;
        private int m_GamesPlayed;

        private string m_StallSig;
        /// <summary>"menu" keyword: automatic login only, stay on the main menu.</summary>
        private bool m_MenuOnly;
        /// <summary>Autostart logs in with the online profile ("online" keyword; always for a second instance).</summary>
        private bool m_AutoOnline;
        private float m_StallSince;

        private static StandaloneSession GetServer(StandaloneGameClient gc)
        {
            FieldInfo f = typeof(StandaloneGameClient).GetField("m_SessionServer", BindingFlags.Instance | BindingFlags.NonPublic);
            return ReferenceEquals(f, null) ? null : f.GetValue(gc) as StandaloneSession;
        }

        /// <summary>
        /// Soak mode: if nothing moves for 120 s (outside the end of a game), logs a
        /// full diagnostic ("STALL") then restarts the game on the next encounter.
        /// </summary>
        private void CheckStall(StandaloneGameClient gc, StandaloneSession ss, string line)
        {
            // Online, waiting for the other player is normal (and the guest has no session).
            if (OnlineMatch.Current != OnlineMatch.Role.None) return;
            if (!m_AutoAI || ReferenceEquals(gc.Player, null) || ReferenceEquals(gc.Player.AITactical, null)
                || (!ReferenceEquals(UIBattle.Instance, null) && UIBattle.Instance.GameOver))
            {
                m_StallSig = null;
                return;
            }
            string sig = line;
            float now = Time.realtimeSinceStartup;
            if (sig != m_StallSig)
            {
                m_StallSig = sig;
                m_StallSince = now;
                return;
            }
            if (now - m_StallSince < 120f || m_KillAt > 0f)
                return;
            WriteState("STALL: " + ServerDump(gc).Replace("\n", " | "));
            WriteState("STALL: action=" + Repl.Execute(gc, new string[] { "get", "server", "m_ActionStack.m_CurrentAction" }, Repl.BuildRefs(gc)));
            WriteState("STALL: anims=" + AnimsDump().Replace("\n", " | "));
            WriteState("STALL: restarting");
            // persistent counter (the game restarts after a stall)
            try
            {
                string f = Path.Combine(LocalServer.Root, Instance.Name("HexRevival.stalls"));
                int n = 0;
                if (File.Exists(f)) int.TryParse(File.ReadAllText(f).Trim(), out n);
                File.WriteAllText(f, (n + 1).ToString());
            }
            catch { }
            Execute(gc, "restart");
        }

        private void AutoDismissInfoStates() { Spectator.DismissInfoWindows(); }

        private float m_LastStatus;
        private string m_LastEncounter = "-";

        /// <summary>
        /// Human-readable status file (HexRevival.status.txt, or .k per instance), rewritten every 10 s.
        /// If it is no longer updated, the game is frozen or closed (see HexRevival_etat.bat).
        /// </summary>
        private static int ReadStalls()
        {
            try
            {
                string f = Path.Combine(LocalServer.Root, Instance.Name("HexRevival.stalls"));
                int n;
                if (File.Exists(f) && int.TryParse(File.ReadAllText(f).Trim(), out n)) return n;
            }
            catch { }
            return 0;
        }

        private void WriteStatus(StandaloneGameClient gc)
        {
            if (Time.realtimeSinceStartup - m_LastStatus < 10f)
                return;
            m_LastStatus = Time.realtimeSinceStartup;
            try
            {
                List<string> l = new List<string>();
                l.Add("Updated: " + DateTime.Now.ToString("HH:mm:ss") + "   (instance " + Instance.Id + (Instance.Headless ? ", headless" : "") + ")");
                l.Add("Since last start: " + (int)(Time.realtimeSinceStartup / 60) + " min, games finished: " + m_GamesPlayed + ", stalls detected (total): " + ReadStalls());
                l.Add("Encounter: " + m_LastEncounter);
                ClientSessionBase session = gc.Session as ClientSessionBase;
                l.Add("Phase : " + (session != null ? session.CurrentPhase.ToString() : gc.StateManager.CurrentGameState.ToString()));
                if (Gauntlet.Enabled())
                    l.Add(Gauntlet.Summary());
                File.WriteAllText(Path.Combine(LocalServer.Root, Instance.Name("HexRevival.status.txt")), string.Join("\r\n", l.ToArray()).Replace("\n", "\r\n").Replace("\r\r", "\r") + "\r\n");
            }
            catch { }
        }

        private void PressGameOverContinue()
        {
            WriteState(Spectator.PressContinue() ? "GAME OVER: Continue" : "GAME OVER: BattleStateGameOver state not found");
        }
        private bool m_EncounterStarted;
        private bool m_Quit;
        private bool m_ClearedLoading;
        private float m_StartTime;
        private float m_LastLoginAttempt;
        private float m_LastActionTime;
        private float m_LastStateLog;
        private string m_StatePath;
        private string m_CmdPath;
        private string m_RespPath;

        public static void Enable()
        {
            try
            {
                GameObject go = new GameObject("HexRevivalDebug");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<TestBehaviour>();
                go.AddComponent<OfflineMenu>();
                go.AddComponent<AccountMenu>();
                go.AddComponent<CheatMenu>();
                go.AddComponent<Notices>();
                go.AddComponent<DeckAiPanel>();
                go.AddComponent<ModifiersWindow>();
                go.AddComponent<Spectator>();
                go.AddComponent<AiDecksWindow>();
                go.AddComponent<AiButton>();
                go.AddComponent<OnlineLobby>();
                go.AddComponent<Community>();
                go.AddComponent<PackContents>();
                go.AddComponent<BattlegroundsScore>();
                Debug.Log("[HexRevival][Dbg] behaviour created");
            }
            catch (Exception e)
            {
                Debug.LogError("[HexRevival][Dbg] Enable failed: " + e);
            }
        }

        private void Start()
        {
            m_Enabled = true;
            m_StartTime = Time.realtimeSinceStartup;
            string root = LocalServer.Root;
            m_StatePath = Path.Combine(root, Instance.Name("HexRevival.state.log"));
            m_CmdPath = Path.Combine(root, Instance.Name("HexRevival.cmd"));
            m_RespPath = Path.Combine(root, Instance.Name("HexRevival.resp"));
            m_AutoTest = Dev.Enabled && File.Exists(Path.Combine(root, "HexRevival.autotest"));
            m_AutoStart = Dev.Enabled && File.Exists(Path.Combine(root, "HexRevival.autostart"));
            if (m_AutoStart)
            {
                // Optional content "WxH": desired window size (e.g. 2240x1260).
                try
                {
                    // Content: space-separated keywords. "WxH" = window size,
                    // "aiplay" = the AI also plays for the human player (soak test).
                    foreach (string tok in File.ReadAllText(Path.Combine(root, "HexRevival.autostart")).Split(new char[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (tok == "aiplay") { m_AutoAI = true; continue; }
                        if (tok == "menu") { m_MenuOnly = true; continue; }
                        if (tok == "online") { m_AutoOnline = true; continue; }
                        if (tok == "manualblock") { m_ManualBlock = true; continue; }
                        if (tok == "mute") { m_Mute = true; continue; }
                        if (tok == "background") { Application.runInBackground = true; continue; }
                        if (tok == "lowgfx") { QualitySettings.SetQualityLevel(0, true); QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; continue; }
                        if (tok.StartsWith("speed")) { float sp; if (float.TryParse(tok.Substring(5), out sp)) m_Speed = sp; continue; }
                        string[] wh = tok.Split('x');
                        int w, h;
                        if (wh.Length == 2 && int.TryParse(wh[0], out w) && int.TryParse(wh[1], out h))
                            Screen.SetResolution(w, h, false);
                    }
                }
                catch { }
            }
            // Keeps the previous session's log (soak mode) in soaklogs/.
            try
            {
                if (Dev.Enabled && File.Exists(m_StatePath))
                {
                    string dir = Path.Combine(root, "soaklogs");
                    Directory.CreateDirectory(dir);
                    File.Copy(m_StatePath, Path.Combine(dir, "state-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + Instance.Suffix + ".log"), true);
                    File.Delete(m_StatePath);
                }
            }
            catch { }
            try { if (File.Exists(m_CmdPath)) File.Delete(m_CmdPath); } catch { }
            WriteState("started autotest=" + m_AutoTest);
        }

        private void Update()
        {
            try
            {
                LocalServer.Pump();
                Campaign.Pump();
                Siege.Tick();
                Battlegrounds.Tick();
                Tutorial.Tick();
                LocalProfile.Pump();
                if (m_Speed != 1f && Time.timeScale != m_Speed)
                    Time.timeScale = m_Speed;
                // Stops supervised instances: HexRevival.soak.stop file.
                if (Instance.Supervised && Time.frameCount % 120 == 0
                    && File.Exists(Path.Combine(LocalServer.Root, "HexRevival.soak.stop")))
                    System.Diagnostics.Process.GetCurrentProcess().Kill();
                if (m_Mute && AudioListener.volume != 0f)
                    AudioListener.volume = 0f;
                if (m_KillAt > 0f && Time.realtimeSinceStartup > m_KillAt)
                    System.Diagnostics.Process.GetCurrentProcess().Kill();
                if (!m_Enabled || m_Quit)
                    return;
                if (ReferenceEquals(Singleton<Main>.Instance, null))
                    return;
                StandaloneGameClient gc = Singleton<Main>.Instance.m_GameClient as StandaloneGameClient;
                if (ReferenceEquals(gc, null))
                    return;

                HandleDebugKeys(gc);

                PollCommands(gc);
                WriteStatus(gc);
                TutorialAutoStep(gc);
                if (m_AutoAI && Gauntlet.Enabled())
                {
                    try { Gauntlet.FrameCheck(GetServer(gc), gc.Player); }
                    catch (Exception e) { Debug.LogWarning("[HexRevival] gauntlet frame: " + e.Message); }
                }

                // The "Loading" overlay (added by UIBattle.StartGame) is never removed in
                // our offline flow and blocks interaction: we remove it once the game has started.
                ClientSessionBase session = gc.Session as ClientSessionBase;
                if (!ReferenceEquals(session, null) && !m_ClearedLoading && (int)session.CurrentPhase > (int)ETurnPhases.Mulligan)
                {
                    m_ClearedLoading = true;
                    if (!ReferenceEquals(UIBattle.Instance, null) && !ReferenceEquals(UIBattle.Instance.Messages, null))
                    {
                        UIBattle.Instance.Messages.Clear();
                        WriteState("cleared loading overlay (phase=" + session.CurrentPhase + ")");
                    }
                }

                // "aiplay manualblock": the AI plays for us except for blocking (done by hand).
                bool defending = m_ManualBlock && !ReferenceEquals(session, null) && !ReferenceEquals(gc.Player, null)
                    && !gc.Player.IsActivePlayer()
                    && (session.CurrentPhase == ETurnPhases.DeclareAttackPriorityWindow || session.CurrentPhase == ETurnPhases.DeclareDefense);
                if (defending && !ReferenceEquals(gc.Player.AITactical, null))
                {
                    gc.Player.RemoveAITactical();
                    WriteState("aiplay: manual blocking");
                }
                if (m_AutoAI && !defending && !ReferenceEquals(session, null) && !ReferenceEquals(gc.Player, null)
                    && ReferenceEquals(gc.Player.AITactical, null) && !ReferenceEquals(UIBattle.Instance, null))
                {
                    gc.Player.InitializeAITactical(new Game.Shared.AI.AIPersonality());
                    gc.Player.RequestPriorityResync();
                    WriteState("aiplay: AI attached to the human player");
                }

                // Soak mode: close the information windows waiting for a player click
                // (revealed cards, etc.), otherwise the animation queue - and so the network - stays stuck.
                if (m_AutoAI && !ReferenceEquals(UIBattle.Instance, null) && !UIBattle.Instance.GameOver)
                    AutoDismissInfoStates();

                // Soak mode: at the end of the game, "Continue" then start another match.
                if (m_AutoAI && !ReferenceEquals(UIBattle.Instance, null) && UIBattle.Instance.GameOver)
                {
                    if (m_GameOverSince < 0f)
                    {
                        m_GameOverSince = Time.realtimeSinceStartup;
                        m_GamesPlayed++;
                        WriteState("GAME OVER #" + m_GamesPlayed + " vainqueur=" + UIBattle.Instance.WinnerID);
                    }
                    else if (Time.realtimeSinceStartup - m_GameOverSince > (Instance.Headless ? 1f : 6f) && !m_ContinuePressed)
                    {
                        m_ContinuePressed = true;
                        PressGameOverContinue();
                    }
                }
                else if (m_GameOverSince >= 0f && (ReferenceEquals(UIBattle.Instance, null) || !UIBattle.Instance.GameOver))
                {
                    m_GameOverSince = -1f;
                    m_ContinuePressed = false;
                    m_AutoStartDone = false;
                    m_LandingSince = -1f;
                }

                // Continuous state log during a match.
                if (Dev.Enabled && Time.realtimeSinceStartup - m_LastStateLog > 1f)
                {
                    m_LastStateLog = Time.realtimeSinceStartup;
                    LogState(gc);
                }

                if (m_AutoStart && !m_AutoTest)
                {
                    DriveAutoStart(gc, Time.realtimeSinceStartup);
                    return;
                }
                if (!m_AutoTest)
                    return;

                float now = Time.realtimeSinceStartup;
                if (now - m_StartTime > 110f)
                {
                    Finish("timeout");
                    return;
                }
                DriveAutoTest(gc, now);
            }
            catch (Exception e)
            {
                Debug.LogError("[HexRevival][Dbg] Update: " + e);
            }
        }

        private void HandleDebugKeys(StandaloneGameClient gc)
        {
            if (Input.GetKeyDown(KeyCode.F8))
                DumpDetailed(gc);
            LocalPlayer player = gc.Player;
            if (ReferenceEquals(player, null))
                return;
            if (Input.GetKeyDown(KeyCode.F9))
                player.PassPriority();
            else if (Input.GetKeyDown(KeyCode.F10))
                PlayFirstPlayable(gc);
            else if (Input.GetKeyDown(KeyCode.F11))
                DeclareNone(gc);
            else if (Input.GetKeyDown(KeyCode.F12))
                player.SendAutoPass(EPassingState.EndOfTurn);
        }

        private void LogState(StandaloneGameClient gc)
        {
            ClientSessionBase session = gc.Session as ClientSessionBase;
            if (ReferenceEquals(session, null))
            {
                WriteState("no-session state=" + gc.StateManager.CurrentGameState);
                return;
            }
            StandaloneSession ss = GetServer(gc);
            string srv = "";
            if (!ReferenceEquals(ss, null))
            {
                foreach (Player p in ss.GetAllPlayers())
                {
                    if (p == null) continue;
                    srv += " " + (p.IsAI() || Server.IsAIId(p.m_PlayerId) ? "AI" : "P") + ":hp="
                        + (p.m_ChampionCard != null ? p.m_ChampionCard.CurrentDefenseValue : 0)
                        + ",main=" + p.GetCardCollection(ECardCollections.Hand).CardCount
                        + ",play=" + p.GetCardCollection(ECardCollections.Warzone).CardCount
                        + ",res=" + p.m_CurrentResourcePool + "/" + p.m_TotalResourcePool;
                }
                srv += " srvPhase=" + ss.CurrentTurnPhase + " chain=" + ss.Chain.Count;
            }
            int play = 0, atk = 0, def = 0, act = 0;
            foreach (KeyValuePair<SessionCardId, CardRepresentation> kv in session.Cache.Cards)
            {
                if (session.Cache.HasUsage(kv.Key, ECardUsage.Play)) play++;
                if (session.Cache.HasUsage(kv.Key, ECardUsage.Attack)) atk++;
                if (session.Cache.HasUsage(kv.Key, ECardUsage.Defend)) def++;
                if (session.Cache.HasUsage(kv.Key, ECardUsage.Activate)) act++;
            }
            string line = string.Format(
                "phase={0} active={1} priority={2} play={3} attack={4} defend={5} activate={6} combats={7}{8}",
                session.CurrentPhase,
                session.ActivePlayer?.m_PlayerId,
                session.PriorityPlayer?.m_PlayerId,
                play, atk, def, act,
                session.Cache.Combats.Count, srv);
            WriteState(line);
            CheckStall(gc, ss, line);
            if (m_AutoAI && Gauntlet.Enabled())
            {
                try { Gauntlet.Tick(ss, gc.Player); }
                catch (Exception e) { WriteState("gauntlet: " + e.Message); }
            }
        }

        private void DumpDetailed(StandaloneGameClient gc)
        {
            ClientSessionBase session = gc.Session as ClientSessionBase;
            if (ReferenceEquals(session, null))
            {
                WriteState("F8: no session");
                return;
            }
            WriteState("F8 dump phase=" + session.CurrentPhase + " active=" + session.ActivePlayer?.m_PlayerId + " priority=" + session.PriorityPlayer?.m_PlayerId);
            WriteState("F8 playable:");
            foreach (SessionCardId id in session.Cache.GetCardsWithUsage(ECardUsage.Play))
                WriteState("   play " + id);
            foreach (SessionCardId id in session.Cache.GetCardsWithUsage(ECardUsage.Attack))
                WriteState("   attack " + id);
            foreach (SessionCardId id in session.Cache.GetCardsWithUsage(ECardUsage.Defend))
                WriteState("   defend " + id);
            ConsoleDumpSession(gc);
        }

        private void ConsoleDumpSession(StandaloneGameClient gc)
        {
            ClientSessionBase session = gc.Session as ClientSessionBase;
            foreach (Player p in session.PlayerList)
            {
                if (ReferenceEquals(p, null))
                    continue;
                WriteState("F8 player " + p.m_PlayerId + " champ=" + (p.m_ChampionCard != null ? p.m_ChampionCard.CurrentDefenseValue.ToString() : "?"));
            }
        }

        private void PlayFirstPlayable(StandaloneGameClient gc)
        {
            ClientSessionBase session = gc.Session as ClientSessionBase;
            if (ReferenceEquals(session, null))
                return;
            List<SessionCardId> playable = session.Cache.GetCardsWithUsage(ECardUsage.Play);
            if (playable.Count == 0)
            {
                WriteState("F10: no playable card");
                return;
            }
            SessionCardId id = playable[0];
            WriteState("F10: trying to play " + id);
            if (!ReferenceEquals(UIBattle.Instance, null))
                UIBattle.Instance.PlayCard(id);
        }

        private void DeclareNone(StandaloneGameClient gc)
        {
            LocalPlayer player = gc.Player;
            ClientSessionBase session = gc.Session as ClientSessionBase;
            if (ReferenceEquals(player, null) || ReferenceEquals(session, null))
                return;
            if (session.CurrentPhase == ETurnPhases.DeclareAttack)
                player.DeclareAttack(new List<CommitTroopsToAttackTransaction.AttackDeclaration>());
            else if (session.CurrentPhase == ETurnPhases.DeclareDefense)
                player.DeclareDefense(new List<CommitTroopsToDefenseTransaction.DefenseDeclaration>());
            else if (session.CurrentPhase == ETurnPhases.AssignDamage || session.CurrentPhase == ETurnPhases.AssignFirstStrikeDamage)
                player.AssignDamageOrder(new List<AssignDamageOrderTransaction.DamageAssignment>());
            else
                WriteState("F11: phase without declaration (" + session.CurrentPhase + ")");
        }

        private float m_LandingSince = -1f;
        private int m_EncounterIndex;

        /// <summary>
        /// Next encounter: HexRevival.encounters (one "guid name" line per encounter), walked
        /// in a loop from a random starting point; otherwise the default encounter.
        /// </summary>
        private string NextEncounter()
        {
            try
            {
                string path = Path.Combine(LocalServer.Root, "HexRevival.encounters");
                if (File.Exists(path))
                {
                    List<string> lines = new List<string>();
                    foreach (string l in File.ReadAllLines(path))
                        if (l.Trim().Length >= 36 && !l.StartsWith("#")) lines.Add(l.Trim());
                    if (lines.Count > 0)
                    {
                        if (m_EncounterIndex == 0)
                            m_EncounterIndex = new System.Random().Next(lines.Count) + 1;
                        string pick = lines[m_EncounterIndex % lines.Count];
                        m_EncounterIndex++;
                        return pick;
                    }
                }
            }
            catch (Exception e)
            {
                WriteState("encounters: " + e.Message);
            }
            return EncounterGuid;
        }

        /// <summary>
        /// HexRevival.autostart marker: clicks "Login" for us, then starts the Frost Ring
        /// match once on the main menu. The match itself is left to the player.
        /// </summary>
        private void DriveAutoStart(StandaloneGameClient gc, float now)
        {
            if (m_AutoStartDone)
                return;
            EGameState state = gc.StateManager.CurrentGameState;
            if (state == EGameState.Login || state == EGameState.Title || state == EGameState.Unknown)
            {
                if (now - m_LastLoginAttempt > 3f)
                {
                    m_LastLoginAttempt = now;
                    UILoginDialogViewModel login = UnityEngine.Object.FindObjectOfType<UILoginDialogViewModel>();
                    if (!ReferenceEquals(login, null) && login.isActiveAndEnabled)
                    {
                        WriteState("autostart: login");
                        // Developer shortcut: the most recently played account (the account menu is skipped).
                        // A second instance ("-hexinstance N") is there for online tests: its own online profile and account.
                        if (Saves.Current == null && (m_AutoOnline || Instance.Id > 0)) Saves.Select(Saves.Online);
                        if (Saves.Current == null) { List<Saves.Info> saves = Saves.List(); Saves.Select(saves.Count > 0 ? saves[0] : Saves.FindOrCreate("Offline")); }
                        login.LoginToGame();
                    }
                }
                return;
            }
            if (state != EGameState.LandingPage)
                return;
            if (m_MenuOnly) { m_AutoStartDone = true; WriteState("autostart: logged in, staying on the main menu (menu)"); return; }
            if (m_LandingSince < 0f)
                m_LandingSince = now;
            if (now - m_LandingSince < (Instance.Headless ? 1f : 4f))
                return;
            m_AutoStartDone = true;
            if (Gauntlet.Enabled()) Gauntlet.OnNewGame();
            string encounter = NextEncounter();
            WriteState("autostart: starting encounter " + encounter);
            m_LastEncounter = encounter.Length > 37 ? encounter.Substring(37) : encounter;
            BattleStateContext context = new BattleStateContext();
            context.m_SceneTemplateId = new ResourceId(new Guid(encounter.Split(' ')[0]));
            context.m_PlayerDeckId = UID.Invalid;
            context.m_PlayerDeckTemplate = ResourceId.Invalid;
            context.m_AIPlayerDeckId = ResourceId.Invalid;
            gc.StateManager.TransitionToState(EGameState.Battle, context);
        }

        private void DriveAutoTest(StandaloneGameClient gc, float now)
        {
            PlayerProfile profile = gc.PlayerProfile;
            if (ReferenceEquals(profile, null))
                return;
            if (!profile.IsInitialized())
            {
                if (now - m_LastLoginAttempt > 2f)
                {
                    m_LastLoginAttempt = now;
                    string error = null;
                    Dictionary<string, object> attrs = null;
                    profile.Login("offline", "offline", null, ref error, ref attrs);
                }
                return;
            }
            if (!Singleton<TemplateManager>.Instance.LoadCompleted)
                return;
            if (!m_EncounterStarted)
            {
                if (now - m_StartTime < 18f)
                    return;
                m_EncounterStarted = true;
                WriteState("autotest: starting encounter");
                BattleStateContext context = new BattleStateContext();
                context.m_SceneTemplateId = new ResourceId(new Guid(EncounterGuid));
                context.m_PlayerDeckId = UID.Invalid;
                gc.StateManager.TransitionToState(EGameState.Battle, context);
                return;
            }

            ClientSessionBase session = gc.Session as ClientSessionBase;
            if (ReferenceEquals(session, null))
                return;
            ETurnPhases phase = session.CurrentPhase;
            if (now - m_LastActionTime < 1.0f)
                return;
            m_LastActionTime = now;
            LocalPlayer player = gc.Player;
            if (ReferenceEquals(player, null))
                return;
            switch (phase)
            {
                case ETurnPhases.PickGoesFirst: player.ChoosePlayFirst(); break;
                case ETurnPhases.Mulligan: player.AcceptStartingHand(); break;
                case ETurnPhases.DeclareAttack: DeclareNone(gc); break;
                case ETurnPhases.DeclareDefense: DeclareNone(gc); break;
                case ETurnPhases.AssignDamage:
                case ETurnPhases.AssignFirstStrikeDamage: DeclareNone(gc); break;
                case ETurnPhases.EndGame: Finish("game ended"); break;
                default: player.PassPriority(); break;
            }
        }

        private void PollCommands(StandaloneGameClient gc)
        {
            try
            {
                // The command file (dev tools, test scripts) only works in dev mode (HexRevival.dev next to Hex.exe).
                if (!Dev.Enabled || string.IsNullOrEmpty(m_CmdPath) || !File.Exists(m_CmdPath))
                    return;
                string[] lines;
                try { lines = File.ReadAllLines(m_CmdPath); }
                catch { return; }
                try { File.Delete(m_CmdPath); } catch { }

                List<string> resp = new List<string>();
                foreach (string raw in lines)
                {
                    string cmd = raw.Trim();
                    if (cmd.Length == 0) continue;
                    resp.Add("> " + cmd);
                    try { resp.Add(Execute(gc, cmd)); }
                    catch (Exception e) { resp.Add("ERR " + e.Message); Debug.LogWarning("[HexRevival] command failed: " + e); }
                }
                try { File.WriteAllLines(m_RespPath, resp.ToArray()); } catch { }
            }
            catch { }
        }

        /// <summary>Inventory of the visible NGUI UI (sprites, atlases, fonts) to reproduce its style.</summary>
        /// <summary>
        /// dev: "click &lt;text&gt;" = clicks the first visible button whose path or label contains the text
        /// (NGUI OnClick, as a mouse click would); "click" alone lists the clickable buttons.
        /// </summary>
        private static string UiClick(string text)
        {
            List<string> found = new List<string>();
            foreach (Collider c in UnityEngine.Object.FindObjectsOfType<Collider>())
            {
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                if (c.GetComponent<UIWidget>() == null && c.GetComponent<UIButton>() == null && c.GetComponent<UIEventListener>() == null) continue;
                string path = c.name;
                Transform t = c.transform.parent;
                for (int i = 0; i < 4 && t != null; i++, t = t.parent) path = t.name + "/" + path;
                string label = "";
                foreach (UILabel l in c.GetComponentsInChildren<UILabel>()) if (!string.IsNullOrEmpty(l.text)) { label = l.text.Replace("\n", " "); break; }
                if (text.Length == 0) { found.Add(path + (label.Length > 0 ? " \"" + label + "\"" : "")); continue; }
                if (path.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0 && label.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                // The game's click bindings act only on a left mouse click (UICamera.currentKey). NGUI
                // buttons ignore a click while another one is being handled (UIButton.current): their
                // delegates are run directly, the other components get OnClick as from a mouse click.
                KeyCode key = UICamera.currentKey;
                UICamera.currentKey = KeyCode.Mouse0;
                try
                {
                    foreach (MonoBehaviour m in c.GetComponents<MonoBehaviour>())
                    {
                        if (m == null || !m.enabled) continue;
                        UIButton b = m as UIButton;
                        UIEventTrigger et = m as UIEventTrigger;
                        if (b != null) { UIButton prev = UIButton.current; UIButton.current = b; EventDelegate.Execute(b.onClick); UIButton.current = prev; continue; }
                        if (et != null) { EventDelegate.Execute(et.onClick); continue; }
                        MethodInfo onClick = m.GetType().GetMethod("OnClick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                        if (onClick != null) onClick.Invoke(m, null);
                    }
                }
                finally { UICamera.currentKey = key; }
                List<string> comps = new List<string>();
                foreach (Component k in c.GetComponents<Component>()) if (k != null) comps.Add(k.GetType().Name);
                return "clicked " + path + (label.Length > 0 ? " \"" + label + "\"" : "") + " [" + string.Join(",", comps.ToArray()) + "]";
            }
            return text.Length == 0 ? string.Join("\n", found.ToArray()) : "no button " + text;
        }

        private static string UiDump(string filter)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (UIWidget w in UnityEngine.Object.FindObjectsOfType<UIWidget>())
            {
                if (w == null || !w.gameObject.activeInHierarchy) continue;
                string path = w.name;
                Transform t = w.transform.parent;
                for (int i = 0; i < 4 && t != null; i++, t = t.parent) path = t.name + "/" + path;
                if (filter.Length > 0 && path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                UISprite sp = w as UISprite;
                UILabel lb = w as UILabel;
                if (sp != null) sb.Append("SPRITE ").Append(path).Append(" atlas=").Append(sp.atlas != null ? sp.atlas.name : "-").Append(" sprite=").Append(sp.spriteName).Append(" type=").Append(sp.type).Append(" ").Append(w.width).Append('x').Append(w.height).Append(" col=").Append(w.color).Append('\n');
                else if (w is UITexture)
                {
                    UITexture ut = (UITexture)w;
                    sb.Append("TEXTURE ").Append(path).Append(" tex=").Append(ut.mainTexture != null ? ut.mainTexture.name + " " + ut.mainTexture.width + "x" + ut.mainTexture.height : "-")
                      .Append(" uv=").Append(ut.uvRect).Append(" size=").Append(w.width).Append('x').Append(w.height);
                    NguiAssetChampionBinding cb = w.GetComponentInParent<NguiAssetChampionBinding>();
                    if (cb == null) cb = w.GetComponentInChildren<NguiAssetChampionBinding>();
                    if (cb != null) sb.Append(" suffix='").Append(cb.assetSuffix).Append("'");
                    sb.Append('\n');
                }
                else if (lb != null) sb.Append("LABEL ").Append(path).Append(" font=").Append(lb.bitmapFont != null ? "bmp:" + lb.bitmapFont.name + (lb.bitmapFont.dynamicFont != null ? "/" + lb.bitmapFont.dynamicFont.name : "") : lb.trueTypeFont != null ? "ttf:" + lb.trueTypeFont.name : "-").Append(" size=").Append(lb.fontSize).Append(" style=").Append(lb.fontStyle).Append(" col=").Append(lb.color).Append(" eff=").Append(lb.effectStyle).Append(" text=").Append((lb.text ?? "").Replace("\n", " ")).Append('\n');
                else sb.Append("WIDGET ").Append(w.GetType().Name).Append(' ').Append(path).Append('\n');
            }
            if (filter.StartsWith("atlas:"))
            {
                sb.Length = 0;
                string an = filter.Substring(6);
                foreach (UIAtlas a in Resources.FindObjectsOfTypeAll<UIAtlas>())
                {
                    if (a.name != an || a.spriteList == null) continue;
                    sb.Append("tex=").Append(a.texture != null ? a.texture.width + "x" + a.texture.height : "-").Append('\n');
                    foreach (UISpriteData d in a.spriteList)
                        sb.Append(d.name).Append(' ').Append(d.width).Append('x').Append(d.height).Append(" b=").Append(d.borderLeft).Append(',').Append(d.borderTop).Append(',').Append(d.borderRight).Append(',').Append(d.borderBottom).Append('\n');
                    break;
                }
                return sb.ToString();
            }
            if (filter == "fonts")
            {
                sb.Length = 0;
                foreach (Font f in Resources.FindObjectsOfTypeAll<Font>()) sb.Append("FONT ").Append(f.name).Append('\n');
                foreach (UIAtlas a in Resources.FindObjectsOfTypeAll<UIAtlas>()) sb.Append("ATLAS ").Append(a.name).Append(" sprites=").Append(a.spriteList != null ? a.spriteList.Count : 0).Append('\n');
            }
            return sb.ToString();
        }

        // dev: "tutorial auto" plays the tutorial's expected actions (one every 1.5 s) and logs them to HexRevival.tutorial.log
        private float m_TutorialAutoNext = -1f;
        private float m_TutorialAutoIdleSince;

        private void TutorialAutoStep(StandaloneGameClient gc)
        {
            if (m_TutorialAutoNext < 0f || Time.realtimeSinceStartup < m_TutorialAutoNext) return;
            m_TutorialAutoNext = Time.realtimeSinceStartup + 1.5f;
            string log = Path.Combine(LocalServer.Root, "HexRevival.tutorial.log");
            string step = Execute(gc, "tutorial step");
            if (step == "no tutorial running")
            {
                if (m_TutorialAutoIdleSince == 0f) m_TutorialAutoIdleSince = Time.realtimeSinceStartup;
                if (Time.realtimeSinceStartup - m_TutorialAutoIdleSince > 30f)
                {
                    m_TutorialAutoNext = -1f;
                    File.AppendAllText(log, DateTime.Now.ToString("HH:mm:ss") + " end, state " + gc.StateManager.CurrentGameState + "\n");
                }
                return;
            }
            m_TutorialAutoIdleSince = 0f;
            string dbg = Execute(gc, "tutorial debug");
            int bar = dbg.IndexOf(" | AI");
            File.AppendAllText(log, DateTime.Now.ToString("HH:mm:ss") + " " + step + "\n    " + Execute(gc, "tutorial state") + (bar >= 0 ? dbg.Substring(bar) : "") + "\n");
        }

        private Game.Shared.Tutorial.TurnPhaseActionBase m_TutorialPlayed;
        private int m_TutorialWaits;

        // dev: a target window is open (spell or ability being played): picks the first card the tutorial accepts
        private static string TutorialTarget(UIBattle ub)
        {
            BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            object top = null;
            foreach (object st in (System.Collections.IEnumerable)typeof(UIBattle).GetField("m_StateStack", bf).GetValue(ub)) { top = st; break; }
            BattleStateTargetBase target = top as BattleStateTargetBase;
            if (target == null) return null;
            MethodInfo can = typeof(BattleStateTargetBase).GetMethod("CanAddTarget", bf);
            System.Collections.IDictionary views = typeof(UIBattle).GetField("m_CardViews", bf).GetValue(ub) as System.Collections.IDictionary;
            foreach (object v in views.Values)
            {
                GoCardView cv = v as GoCardView;
                if (cv == null || cv.Card == null) continue;
                bool ok;
                try { ok = (bool)can.Invoke(top, new object[] { cv }); } catch { ok = false; }
                if (!ok) continue;
                target.OnClick(cv);
                target.OnButton("Accept", 0);
                return "target " + cv.Card.Name + " (" + top.GetType().Name + ")";
            }
            return "no valid target in " + top.GetType().Name;
        }

        // dev: the blocks the tutorial asks for, declared in the blockers window as a player would
        private static string TutorialBlock(Game.Shared.Tutorial.TurnPhaseActionDefendWithCards a, UIBattle ub)
        {
            BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            object top = null;
            object stack = typeof(UIBattle).GetField("m_StateStack", bf).GetValue(ub);
            foreach (object st in (System.Collections.IEnumerable)stack) { top = st; break; }
            if (!(top is BattleStateDeclareBlockers)) return "blockers window not open (" + (top != null ? top.GetType().Name : "none") + ")";
            Dictionary<SessionCardId, List<SessionCardId>> decl = (Dictionary<SessionCardId, List<SessionCardId>>)typeof(BattleStateDeclareBlockers).GetField("m_BlockingDeclarations", bf).GetValue(top);
            decl.Clear();
            UID me = ub.Player.m_PlayerId;
            foreach (Game.Shared.Tutorial.TutorialDefense d in a.Defenses)
            {
                SessionCardId attacker = SessionCardId.Invalid;
                List<SessionCardId> blockers = new List<SessionCardId>();
                List<ResourceId> wanted = new List<ResourceId>(d.m_DefendingCardIds);
                foreach (KeyValuePair<SessionCardId, CardRepresentation> c in ub.State.Cards)
                {
                    if ((c.Value.Collection & ECardCollections.Warzone) == 0) continue;
                    if (c.Value.Controller != me && c.Value.TemplateId == d.m_AttackingCardId) attacker = c.Key;
                    else if (c.Value.Controller == me && wanted.Remove(c.Value.TemplateId)) blockers.Add(c.Key);
                }
                if (attacker.IsValid()) decl[attacker] = blockers;
            }
            typeof(BattleStateDeclareBlockers).GetMethod("Commit", bf).Invoke(top, null);
            System.Text.StringBuilder r = new System.Text.StringBuilder();
            foreach (KeyValuePair<SessionCardId, List<SessionCardId>> kv in decl) r.Append(ub.State.Cards[kv.Key].Name).Append(" by ").Append(kv.Value.Count).Append(' ');
            return decl.Count + " attacker(s) blocked: " + r;
        }

        private string Execute(StandaloneGameClient gc, string cmd)
        {
            string[] parts = cmd.Split(' ');
            string verb = parts[0].ToLower();
            LocalPlayer player = gc.Player;
            ClientSessionBase session = gc.Session as ClientSessionBase;
            switch (verb)
            {
                case "quit": Application.Quit(); return "quitting";
                case "killothers":
                {
                    // Closes the other Hex.exe processes (orphaned test instances).
                    int me = System.Diagnostics.Process.GetCurrentProcess().Id, n = 0;
                    foreach (System.Diagnostics.Process pr in System.Diagnostics.Process.GetProcessesByName("Hex"))
                        if (pr.Id != me) { try { pr.Kill(); n++; } catch { } }
                    return "fermes: " + n;
                }
                case "soak":
                {
                    // Switch to headless testing: runs HexRevival_soak.bat N then closes this instance.
                    string root2 = LocalServer.Root;
                    string bat2 = Path.Combine(root2, "HexRevival_soak.bat");
                    if (!File.Exists(bat2)) return "HexRevival_soak.bat not found";
                    string n = parts.Length > 1 ? parts[1] : "1";
                    System.Diagnostics.ProcessStartInfo psi2 = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c \"" + bat2 + "\" " + n);
                    psi2.WorkingDirectory = root2;
                    psi2.UseShellExecute = true;
                    psi2.WindowStyle = System.Diagnostics.ProcessWindowStyle.Minimized;
                    System.Diagnostics.Process.Start(psi2);
                    m_KillAt = Time.realtimeSinceStartup + 0.5f;
                    return "soak " + n;
                }
                case "restart":
                {
                    if (m_KillAt > 0f) return "already in progress";
                    if (Instance.Supervised)
                    {
                        // Started by HexRevival_soak.bat: the supervisor restarts the instance when it exits.
                        m_KillAt = Time.realtimeSinceStartup + 0.5f;
                        return "restarting (superviseur)";
                    }
                    // Restart through HexRevival_restart.bat (installs a pending .new plugin, restarts Hex.exe).
                    string root = LocalServer.Root;
                    string bat = Path.Combine(root, "HexRevival_restart.bat");
                    if (!File.Exists(bat)) return "HexRevival_restart.bat not found";
                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c \"" + bat + "\"");
                    psi.WorkingDirectory = root;
                    psi.UseShellExecute = true;
                    psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                    System.Diagnostics.Process.Start(psi);
                    // Application.Quit() can be blocked (e.g. victory screen) and Environment.Exit
                    // freezes the Unity process: we kill the process directly, after answering.
                    m_KillAt = Time.realtimeSinceStartup + 0.5f;
                    return "restarting";
                }
                case "dump": return DumpString(session);
                case "store3d": return StoreIcons.Dump();   // dev: the store's 3D item and the cameras
                case "pass": return "pass=" + (player != null ? player.PassPriority().ToString() : "noplayer");
                case "autopass": if (player != null) player.SendAutoPass(EPassingState.EndOfTurn); return "autopass sent";
                case "accept": if (player != null) player.AcceptStartingHand(); return "accept sent";
                case "choose":
                    if (player == null) return "noplayer";
                    if (parts.Length > 1 && parts[1].StartsWith("d")) player.ChooseDrawFirst(); else player.ChoosePlayFirst();
                    return "choose sent";
                case "attack": if (player != null) player.DeclareAttack(new List<CommitTroopsToAttackTransaction.AttackDeclaration>()); return "attack none";
                case "discard":   // dev: discards the first card of the hand (discard phase)
                    if (player == null || session == null) return "noplayer";
                    foreach (SessionCardId id in session.Cache.GetCardsInCollection(ECardCollections.Hand, player.m_PlayerId)) { player.Discard(id); return "discard " + id; }
                    return "empty hand";
                case "block": if (player != null) player.DeclareDefense(new List<CommitTroopsToDefenseTransaction.DefenseDeclaration>()); return "block none";
                case "assign": if (player != null) player.AssignDamageOrder(new List<AssignDamageOrderTransaction.DamageAssignment>()); return "assign none";
                case "playable":
                    if (session == null) return "nosession";
                    return "playable=" + Join(session.Cache.GetCardsWithUsage(ECardUsage.Play));
                case "play":
                    if (session == null) return "nosession";
                    List<SessionCardId> playable = session.Cache.GetCardsWithUsage(ECardUsage.Play);
                    int idx = 0;
                    if (parts.Length > 1) int.TryParse(parts[1], out idx);
                    if (idx < 0 || idx >= playable.Count) return "index out of range (" + playable.Count + ")";
                    if (ReferenceEquals(UIBattle.Instance, null)) return "noUIBattle";
                    UIBattle.Instance.PlayCard(playable[idx]);
                    return "play " + playable[idx];
                case "server": return ServerDump(gc);
                case "anims": return AnimsDump();
                case "cheat": return Cheats.Execute(gc, parts);
                case "menu": return OfflineMenu.Command(parts);
                case "camp": return Campaign.Command(parts);
                case "dun": return Dungeons.Command(parts);
                case "packs": return Packs.Command(parts);   // dev: chests, "packs give <pack_name> [n]"
                case "home": return Hometown.Command(parts, GameClient.Instance.PlayerProfile.GetCurrentChampion().Id);   // dev: hometown flags
                case "sim": return QuestSim.Run(string.Join(" ", parts, 1, parts.Length - 1));
                case "quest": return Quests.Command(parts);
                case "siege": return Siege.Command(parts);
                case "bg": return Battlegrounds.Command(parts);
                case "arena": return Arena.Command(parts);
                case "deckai": return DeckAiPanel.Command(parts);
                case "mods": return ModifiersWindow.Command(parts);
                case "spectate": return Spectator.Toggle();
                case "online": return OnlineLobby.Command(parts);
                case "profile": return AccountMenu.Command(parts);
                case "net": return NetCodec.Stats();
                case "aidecks": return AiDecksWindow.Command(parts);
                case "craft": return Crafting.Command(parts);   // dev: breaking down and crafting (see Crafting)
                case "tdecks": return TournamentDecks.Command(parts);
                case "sleeves": return Sleeves.Command(parts);
                case "duelpit":   // dev probe: the console Dueling Pit button and view on the landing page
                    {
                        UILandingPageViewModel vm = UnityEngine.Object.FindObjectOfType<UILandingPageViewModel>();
                        if (vm == null) return "no landing page";
                        string r0 = "button " + (vm.MenuUI.DuelingPit != null) + ", view " + (vm.DuelingPit != null);
                        if (parts.Length > 1 && parts[1] == "show")
                        {
                            if (vm.MenuUI.DuelingPit != null) vm.MenuUI.DuelingPit.gameObject.SetActive(true);
                            if (vm.DuelingPit != null) { vm.DuelingPit.gameObject.SetActive(true); vm.DuelingPit.SetView(DuelingPitVM.EDuelingPitView.ChooseOpponent); }
                        }
                        return r0;
                    }   // dev: "sleeves [state|holiday|season|give <pattern>]"
                case "itemdump":   // dev: "itemdump <text>" = the game's items whose name contains it: type, pack type, set, price, description
                    {
                        string q = string.Join(" ", parts, 1, parts.Length - 1);
                        System.Text.StringBuilder sb = new System.Text.StringBuilder();
                        TemplateManager tm = Singleton<TemplateManager>.Instance;
                        foreach (InventoryItemData i in tm.InventoryItems.Values)
                        {
                            if (i == null || i.m_Name == null || i.m_Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                            InventoryCardPack cp = i as InventoryCardPack;
                            CardSetTemplate set = null;
                            if (cp != null) tm.CardSets.TryGetValue(cp.m_SetId, out set);
                            sb.Append(i.m_Name).Append(" | ").Append(i.GetType().Name).Append(cp != null ? " " + cp.m_CardPackType + " set=" + (set != null ? set.m_Name : cp.m_SetId.ToString()) : "")
                              .Append(" price=").Append(i.m_BasePrice).Append(" buy=").Append(i.m_AvailableForPurchase).Append(" tag=").Append(i.m_BuildTag).Append(" tex=").Append(i.m_TexturePath)
                              .Append(" | ").Append((i.Description ?? "").Replace("\n", " ")).Append('\n');
                        }
                        return sb.Length == 0 ? "none" : sb.ToString();
                    }
                case "altcheck":   // dev: card sets with "alternate" in their name, their cards and how many are ownable; m_HasAlternateArt cards
                    {
                        TemplateManager tm = Singleton<TemplateManager>.Instance;
                        Dictionary<string, int[]> bySet = new Dictionary<string, int[]>();
                        int hasAlt = 0, rar = 0, eqm = 0, choice = 0, owned = 0;
                        foreach (CardTemplate t in tm.Cards.Values)
                        {
                            if (t == null) continue;
                            if (t.m_HasAlternateArt) hasAlt++;
                            CardSetTemplate set;
                            string n = tm.CardSets.TryGetValue(t.m_SetId, out set) && set != null ? set.m_Name : "?" + t.m_SetId;
                            if (n == null || n.IndexOf("altern", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            int[] c;
                            if (!bySet.TryGetValue(n, out c)) bySet[n] = c = new int[3];
                            c[0]++;
                            bool own; try { own = t.IsOwnable(); } catch { own = false; }
                            if (own) c[1]++;
                            if (t.m_ImplStage == ECardImplStage.Done) c[2]++;
                            try { if (t.CardRarity > ERarity.Land || t.IsBasicResource()) rar++; } catch { }
                            if (t.m_EquipmentModifiedCard) eqm++;
                            try { if (t.IsChoiceCard()) choice++; } catch { }
                            if (Collection.Count(t.m_Id) > 0) owned++;
                        }
                        List<string> o = new List<string>();
                        foreach (KeyValuePair<string, int[]> kv in bySet) o.Add(kv.Key + ": " + kv.Value[0] + " cards, " + kv.Value[1] + " ownable, " + kv.Value[2] + " done");
                        return hasAlt + " cards with m_HasAlternateArt; alternates: rarity ok " + rar + ", equipment-modified " + eqm + ", choice " + choice + ", owned " + owned + "; sets:\n" + string.Join("\n", o.ToArray());
                    }
                case "hauses": return Campaign.DevHauses();   // dev: the Shroom Haus nodes and their cards
                case "sleevecheck": return Sleeves.DevCheck();   // dev: sleeves whose picture is not in the game's sleeve archive
                case "sleevedump":   // dev: "sleevedump [text]" = the game's sleeves whose description contains the text
                    {
                        string q = parts.Length > 1 ? string.Join(" ", parts, 1, parts.Length - 1) : "";
                        System.Text.StringBuilder sb = new System.Text.StringBuilder();
                        int n = 0, all = 0;
                        foreach (InventoryItemData i in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                        {
                            if (!(i is InventoryDeckSleeve)) continue;
                            all++;
                            if (q != "" && (i.Description ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                            if (n++ < 15) sb.Append(i.m_Name).Append(" | ").Append(i.m_Description).Append(" | ").Append(i.Description).Append(" | ").Append(i.m_BuildTag).Append('\n');
                        }
                        return all + " sleeves, " + n + " matching\n" + sb;
                    }
                case "cardsearch":   // dev: "cardsearch <text>" = abilities whose text contains it, and the cards holding them
                    {
                        string q = string.Join(" ", parts, 1, parts.Length - 1);
                        System.Text.StringBuilder sb = new System.Text.StringBuilder();
                        TemplateManager tm = Singleton<TemplateManager>.Instance;
                        HashSet<ResourceId> found = new HashSet<ResourceId>();
                        foreach (ResourceId k in tm.Abilities.Keys)
                        {
                            AbilityTemplate at = tm.Abilities[k];
                            // "=text": exactly that text.
                            if (at != null && at.m_GameText != null && (q.StartsWith("=") ? at.m_GameText.Trim() == q.Substring(1) : at.m_GameText.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)) found.Add(k);
                        }
                        sb.Append(found.Count).Append(" abilities\n");
                        int shownAb = 0;
                        foreach (ResourceId k in found) { if (shownAb++ >= 12) break; sb.Append("  ").Append(k.m_Guid).Append(" ").Append(tm.Abilities[k].m_GameText.Replace("\n", " ")).Append('\n'); }
                        int n = 0;
                        foreach (CardTemplate c in tm.Cards.Values)
                        {
                            if (c == null || c.m_CardAbilities == null) continue;
                            foreach (CardAbilityContainer a in c.m_CardAbilities)
                            {
                                if (!found.Contains(a.m_CardAbilityId)) continue;
                                CardSetTemplate set;
                                tm.CardSets.TryGetValue(c.m_SetId, out set);
                                sb.Append(c.m_Name).Append(" | ").Append(c.m_CardType).Append(" | ").Append(set != null ? set.m_Name : "?").Append(" | ").Append(tm.Abilities[a.m_CardAbilityId].m_GameText.Replace("\n", " ")).Append('\n');
                                n++;
                                break;
                            }
                            if (n >= 25) break;
                        }
                        return sb.ToString();
                    }   // dev: "tdecks [check]" = the bundled tournament decks
                case "cardvers":   // dev: "cardvers <card name>" = every card template of that name: set, format, ownable, state, copies owned
                    {
                        string q = string.Join(" ", parts, 1, parts.Length - 1);
                        TemplateManager tm = Singleton<TemplateManager>.Instance;
                        HashSet<ResourceId> immortal = new HashSet<ResourceId>(Game.Shared.Resources.BuiltInResources.Format_Immortal);
                        List<string> o = new List<string>();
                        HashSet<ResourceId> ownedIds = new HashSet<ResourceId>(Collection.Owned());   // Collection.Count says 4 for every id on unlock-all saves
                        if (q == "*")
                        {
                            // Names with several ownable versions: inside the Immortal sets, and (names absent there) elsewhere, with their sets.
                            Dictionary<string, List<CardTemplate>> main = new Dictionary<string, List<CardTemplate>>(), other = new Dictionary<string, List<CardTemplate>>();
                            foreach (CardTemplate c in tm.Cards.Values)
                            {
                                bool own; try { own = c != null && c.m_Name != null && c.IsOwnable(); } catch { own = false; }
                                if (!own) continue;
                                Dictionary<string, List<CardTemplate>> d = immortal.Contains(c.m_SetId) ? main : other;
                                List<CardTemplate> l;
                                if (!d.TryGetValue(c.m_Name, out l)) d[c.m_Name] = l = new List<CardTemplate>();
                                l.Add(c);
                            }
                            int mainMulti = 0, otherOnly = 0, otherMulti = 0, otherMultiSets = 0, shown = 0;
                            foreach (KeyValuePair<string, List<CardTemplate>> kv in main) if (kv.Value.Count > 1) mainMulti++;
                            foreach (KeyValuePair<string, List<CardTemplate>> kv in other)
                            {
                                if (main.ContainsKey(kv.Key)) continue;
                                otherOnly++;
                                if (kv.Value.Count < 2) continue;
                                otherMulti++;
                                HashSet<ResourceId> s = new HashSet<ResourceId>();
                                foreach (CardTemplate c in kv.Value) s.Add(c.m_SetId);
                                if (s.Count > 1) otherMultiSets++;
                                if (shown++ < 15)
                                {
                                    List<string> v = new List<string>();
                                    foreach (CardTemplate c in kv.Value) { CardSetTemplate set; tm.CardSets.TryGetValue(c.m_SetId, out set); v.Add((set != null ? set.m_Name : "?") + (c.m_HasAlternateArt ? "/alt" : "") + (ownedIds.Contains(c.m_Id) ? "/owned" : "")); }
                                    o.Add(kv.Key + ": " + string.Join(", ", v.ToArray()));
                                }
                            }
                            return main.Count + " names in Immortal sets, " + mainMulti + " with several versions; " + otherOnly + " names only elsewhere, " + otherMulti + " with several versions (" + otherMultiSets + " across sets)\n" + string.Join("\n", o.ToArray());
                        }
                        foreach (CardTemplate c in tm.Cards.Values)
                        {
                            if (c == null || c.m_Name == null || !string.Equals(c.m_Name, q, StringComparison.OrdinalIgnoreCase)) continue;
                            CardSetTemplate set;
                            tm.CardSets.TryGetValue(c.m_SetId, out set);
                            bool own; try { own = c.IsOwnable(); } catch { own = false; }
                            o.Add(c.m_Id + " | " + (set != null ? set.m_Name : "?") + (immortal.Contains(c.m_SetId) ? " [Immortal]" : "") + " | ownable=" + own
                                + " stage=" + c.m_ImplStage + " rarity=" + c.CardRarity + " altArt=" + c.m_HasAlternateArt + " eqMod=" + c.m_EquipmentModifiedCard
                                + " | owned=" + ownedIds.Contains(c.m_Id));
                        }
                        return o.Count + " templates\n" + string.Join("\n", o.ToArray());
                    }
                case "redeem": return Redeem.Command(parts);
                case "store":
                    if (parts.Length > 1 && parts[1] == "pools") return PrizePacks.Describe();
                    if (parts.Length > 1 && parts[1] == "products") return Store.Products(parts.Length > 2 ? string.Join(" ", parts, 2, parts.Length - 2) : "");
                    if (parts.Length > 2 && parts[1] == "cosmic" && Saves.Current != null)
                    {
                        Saves.Current.CosmicCoins = int.Parse(parts[2]);
                        Saves.SaveCurrent();
                        return "cosmic coins = " + Saves.Current.CosmicCoins + " (seen after the next login)";
                    }
                    if (parts.Length > 2 && parts[1] == "open") return Store.DevOpen(ulong.Parse(parts[2]));
                    if (parts.Length > 2 && parts[1] == "select")   // dev: selects the store screen's first item (of the tab shown) whose name holds the text
                    {
                        string q = string.Join(" ", parts, 2, parts.Length - 2);
                        UIPremiumStoreViewModel svm = UnityEngine.Object.FindObjectOfType<UIPremiumStoreViewModel>();
                        if (svm == null || svm.Context == null) return "no store screen";
                        for (int i = 0; i < svm.Context.Catalog.Count; i++)
                        {
                            UIPremiumStoreItemContext it = svm.Context.Catalog.GetItem(i);
                            if (it != null && it.Name != null && it.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                svm.Context.Catalog.SelectItem(i);
                                UIScrollView sv = svm.GetComponentInChildren<UIScrollView>();
                                if (sv != null && svm.Context.Catalog.Count > 1) sv.SetDragAmount(0f, (float)i / (svm.Context.Catalog.Count - 1), false);
                                return "selected " + it.Name;
                            }
                        }
                        return "not in this tab: " + q;
                    }
                    return "usage: store pools | store products [text] | store cosmic <n> | store open <product id> | store select <text>";
                case "toggles": return Settings.DumpToggles();
                case "click": return UiClick(string.Join(" ", parts, 1, parts.Length - 1));
                case "invoke":   // dev: "invoke <MonoBehaviour type> <method>" = calls a method without parameters on the scene's object of that type
                    {
                        if (parts.Length < 3) return "usage: invoke <type> <method>";
                        Type t = typeof(UIBattle).Assembly.GetType(parts[1]);
                        UnityEngine.Object o = t != null ? UnityEngine.Object.FindObjectOfType(t) : null;
                        System.Reflection.MethodInfo m = t != null ? t.GetMethod(parts[2], System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, Type.EmptyTypes, null) : null;
                        if (o == null || m == null) return "not found: " + (t == null ? "type" : o == null ? "object" : "method");
                        object res = m.Invoke(o, null);
                        return "invoked " + parts[1] + "." + parts[2] + (res != null ? " -> " + res : "");
                    }
                case "modal":   // dev: "modal 1|2|3" = presses that button of the visible modal popups
                    {
                        int n = 0;
                        foreach (UIModalPopupViewModel mp in UnityEngine.Object.FindObjectsOfType<UIModalPopupViewModel>())
                        {
                            if (mp == null || !mp.IsVisible()) continue;
                            n++;
                            if (parts.Length > 1 && parts[1] == "1") mp.OnClickButton1();
                            else if (parts.Length > 1 && parts[1] == "2") mp.OnClickButton2();
                            else if (parts.Length > 1 && parts[1] == "3") mp.OnClickButton3();
                        }
                        return n + " visible modal popup(s)";
                    }
                case "shot":   // dev: screenshot of the game window to HexRevivalShot.png (written at the end of the frame)
                    Application.CaptureScreenshot(System.IO.Path.Combine(LocalServer.Root, "HexRevivalShot.png"));
                    return "shot";
                case "tutorial":   // dev: the game's tutorial battle (as its "Tutorial" buttons do)
                    {
                        Reckoning.Game.SceneData scene;
                        bool known = Singleton<Reckoning.Game.TemplateManager>.Instance.CampaignScenes.TryGetValue(Game.Shared.Resources.BuiltInResources.BattleSceneTutorialTemplate, out scene);
                        if (!known) return "tutorial scene not in the game data";
                        if (parts.Length > 1 && parts[1] == "actions")
                        {
                            // dev: the tutorial's script (turn, active player, phase, who acts, action, tip)
                            System.Text.StringBuilder sb = new System.Text.StringBuilder();
                            int i = 0;
                            foreach (Game.Shared.Tutorial.TurnPhaseActionBase a in ((Game.Shared.Tutorial.TutorialTemplate)scene).m_TutorialActions)
                                sb.Append(i++).Append(" t").Append(a.m_Turn).Append(' ').Append(a.m_ActivePlayer).Append(' ').Append(a.m_TurnPhase).Append(" by ").Append(a.m_ActionPerformer)
                                  .Append(' ').Append(a.BuildActionName()).Append(string.IsNullOrEmpty(a.m_TipText) ? "" : " tip=" + a.m_TipText).Append('\n');
                            return sb.ToString();
                        }
                        if (parts.Length > 1 && parts[1] == "step")
                        {
                            // dev: does the tutorial's current player action, as the player would
                            UIBattle ub = UIBattle.Instance;
                            if (ub == null || ub.TutorialManager == null) return "no tutorial running";
                            string targeted = TutorialTarget(ub);
                            if (targeted != null) return targeted;
                            UITutorialRewards rewards = UnityEngine.Object.FindObjectOfType<UITutorialRewards>();
                            if (rewards != null && rewards.IsVisible()) { rewards.HandleAccept(); return "rewards window accepted"; }
                            Game.Shared.Tutorial.TurnPhaseActionBase a = ub.TutorialManager.CurrentAction;
                            bool uiAction = a == null || a is Game.Shared.Tutorial.TurnPhaseActionShowTip || a is Game.Shared.Tutorial.TurnPhaseActionShowSummary
                                || a is Game.Shared.Tutorial.TurnPhaseActionStartConversation || a is Game.Shared.Tutorial.TurnPhaseActionPassPriority;
                            if (uiAction)
                            {
                                string r;
                                if (a is Game.Shared.Tutorial.TurnPhaseActionStartConversation) r = UiClick("Answer_Grid/0");
                                else if (a is Game.Shared.Tutorial.TurnPhaseActionShowSummary)
                                {
                                    r = UiClick("TutorialSummaryWindow/Button_Confirm");
                                    if (r.StartsWith("no button")) r = UiClick("ContinueButton");
                                }
                                else r = UiClick("PassButton_Standard");
                                if (r.StartsWith("no button") && a is Game.Shared.Tutorial.TurnPhaseActionShowTip)
                                {
                                    // tips shown with the coin flip or the starting hand window: "Go First" / "Keep"
                                    BindingFlags sbf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                                    foreach (object st in (System.Collections.IEnumerable)typeof(UIBattle).GetField("m_StateStack", sbf).GetValue(ub))
                                    {
                                        BattleStateShowTip tip = st as BattleStateShowTip;
                                        if (tip != null) { tip.OnButton(a.m_TurnPhase == ETurnPhases.PickGoesFirst ? "PlayFirst" : "KeepHand", 0); r = "dialog answered"; }
                                        break;
                                    }
                                }
                                return (a != null ? a.BuildActionName() : "no action") + ": " + r;
                            }
                            ClientSessionBase cs = Singleton<Main>.Instance.m_GameClient.Session as ClientSessionBase;
                            if (a is Game.Shared.Tutorial.TurnPhaseActionDefendWithCards)
                                return "block: " + TutorialBlock((Game.Shared.Tutorial.TurnPhaseActionDefendWithCards)a, ub);
                            if (a is Game.Shared.Tutorial.TurnPhaseActionAttackWithCards)
                            {
                                // the attackers the script names, as a player would pick them (the action's own
                                // PerformActionForPlayer attacks with every troop that can)
                                List<ResourceId> wanted = new List<ResourceId>(((Game.Shared.Tutorial.TurnPhaseActionAttackWithCards)a).m_AttackingCardTemplateIds);
                                List<SessionCardId> attackers = new List<SessionCardId>();
                                foreach (SessionCardId id in ub.State.GetCardsInCollection(ECardCollections.Warzone, ub.Player.m_PlayerId))
                                    if (ub.State.HasUsage(id, ECardUsage.Attack) && wanted.Remove(ub.State.Cards[id].TemplateId)) attackers.Add(id);
                                SessionCardId target = SessionCardId.Invalid;
                                if (attackers.Count == 0 && ++m_TutorialWaits < 8) return "waiting for attackers";   // usages not received yet
                                m_TutorialWaits = 0;
                                foreach (KeyValuePair<UID, PlayerRepresentation> pr in ub.State.Players) if (pr.Key != ub.Player.m_PlayerId) target = pr.Value.ChampionSessionCardId;
                                ub.Player.DeclareAttack(new List<CommitTroopsToAttackTransaction.AttackDeclaration> { new CommitTroopsToAttackTransaction.AttackDeclaration(target, attackers) });
                                ub.TutorialManager.PlayerActionWasPerformed(a);
                                return "attack with " + attackers.Count;
                            }
                            if (a is Game.Shared.Tutorial.TurnPhaseActionPlayCard)
                            {
                                // as from the hand: the client moves the script on when the card's play animation ends
                                if (ReferenceEquals(a, m_TutorialPlayed))
                                {
                                    // the script's own play data does not fit every card (e.g. Bravery): moves on after a while
                                    if (++m_TutorialWaits < 6) return "waiting for " + a.BuildActionName();
                                    ub.TutorialManager.PlayerActionWasPerformed(a);
                                    return "gave up waiting for " + a.BuildActionName();
                                }
                                m_TutorialPlayed = a;
                                m_TutorialWaits = 0;
                                a.PerformActionForPlayer(Singleton<Main>.Instance.m_GameClient.Player, cs);
                                return "played " + a.BuildActionName();
                            }
                            a.PerformActionForPlayer(Singleton<Main>.Instance.m_GameClient.Player, cs);
                            ub.TutorialManager.PlayerActionWasPerformed(a);
                            return "performed " + a.BuildActionName();
                        }
                        if (parts.Length > 1 && parts[1] == "debug")
                        {
                            // dev: coin flip flags of UIBattle and the client tutorial manager's internals
                            UIBattle ub = UIBattle.Instance;
                            if (ub == null) return "no battle";
                            BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                            System.Text.StringBuilder sb = new System.Text.StringBuilder();
                            foreach (string f in new[] { "m_Started", "m_EnableCoinFlip", "m_CoinFlipStarted", "m_CoinFlipping", "m_CoinFlipDone", "m_CoinFlipSkip", "m_ReadyToPlay" })
                            {
                                FieldInfo fi = typeof(UIBattle).GetField(f, bf);
                                sb.Append(f).Append('=').Append(fi != null ? fi.GetValue(ub) : "?").Append(' ');
                            }
                            object toss = typeof(UIBattle).GetField("m_CoinToss", bf).GetValue(ub);
                            if (toss != null) sb.Append("toss.ready=").Append(toss.GetType().GetField("m_isReadyForGame", bf).GetValue(toss)).Append(' ');
                            object stack = typeof(UIBattle).GetField("m_StateStack", bf).GetValue(ub);
                            if (stack != null) foreach (object st in (System.Collections.IEnumerable)stack) sb.Append("[").Append(st.GetType().Name).Append("]");
                            if (ub.TutorialManager != null)
                            {
                                object tm = ub.TutorialManager;
                                for (Type t = tm.GetType(); t != null; t = t.BaseType)
                                    foreach (FieldInfo fi in t.GetFields(bf | BindingFlags.DeclaredOnly))
                                    {
                                        object v = fi.GetValue(tm);
                                        System.Collections.ICollection col = v as System.Collections.ICollection;
                                        sb.Append(' ').Append(fi.Name).Append('=').Append(col != null ? "#" + col.Count : v is Game.Shared.Tutorial.TurnPhaseActionBase ? ((Game.Shared.Tutorial.TurnPhaseActionBase)v).BuildActionName() : (v != null ? v.ToString() : "null"));
                                    }
                            }
                            // the AI's tutorial script
                            foreach (LocalPlayer lp in gc.m_LocalPlayerMap.Values)
                            {
                                if (lp == null || lp.AITactical == null || lp == gc.Player) continue;
                                Game.Shared.Tutorial.TutorialManager atm = typeof(Game.Shared.AI.AITactical).GetField("m_TutorialManager", bf).GetValue(lp.AITactical) as Game.Shared.Tutorial.TutorialManager;
                                ClientSessionBase acs = lp.m_Session as ClientSessionBase;
                                if (acs != null) sb.Append(" | AIsession phase=").Append(acs.CurrentPhase).Append(" combats=").Append(acs.Cache.Combats.Count).Append(" prio=").Append(lp.IsPriorityPlayer());
                                if (parts.Length > 2 && parts[2] == "tick") sb.Append(" tick=").Append(lp.AITactical.TryAIUpdate());
                                if (parts.Length > 2 && parts[2] == "resync") { lp.RequestPriorityResync(); sb.Append(" resync requested"); }
                                if (atm != null) sb.Append(" | AI current=").Append(atm.m_CurrentAction != null ? atm.m_CurrentAction.BuildActionName() : "none").Append(" next=").Append(atm.NextActionDescription).Append(" reins=").Append(atm.IsOnReins());
                            }
                            return sb.ToString();
                        }
                        if (parts.Length > 1 && parts[1] == "pending")
                        {
                            // dev: the current account plays the tutorial on its next visit of the home screen, as a new account
                            Saves.Current.TutorialPending = true;
                            Saves.SaveCurrent();
                            return "tutorial pending";
                        }
                        if (parts.Length > 1 && parts[1] == "menu")
                        {
                            // dev: opens the game's menu (Esc) and lists its visible buttons
                            UISystemMenuViewModel m = UISystem.SetTaggedUIElementVisible("SystemMenu", true, true) as UISystemMenuViewModel;
                            if (m == null) return "no menu";
                            return "exitTutorial enabled=" + m.Context.IsExitTutorialEnabled + " shown=" + (m.MenuButtons.ExitTutorial != null && m.MenuButtons.ExitTutorial.gameObject.activeInHierarchy);
                        }
                        if (parts.Length > 2 && parts[1] == "tracepass") { Tutorial.TracePass = parts[2] != "off"; return "tracepass " + Tutorial.TracePass; }
                        if (parts.Length > 1 && parts[1] == "stallai") { Tutorial.DevStallAI = true; return "the AI's next update does nothing"; }
                        if (parts.Length > 2 && parts[1] == "skipfix") { Tutorial.SkipFix = parts[2] != "off"; return "skipfix " + Tutorial.SkipFix; }
                        if (parts.Length > 1 && parts[1] == "auto")
                        {
                            m_TutorialAutoNext = Time.realtimeSinceStartup + 1.5f;
                            m_TutorialAutoIdleSince = 0f;
                            return "tutorial auto: on";
                        }
                        if (parts.Length > 1 && parts[1] == "state")
                        {
                            UIBattle ub = UIBattle.Instance;
                            if (ub == null || ub.TutorialManager == null) return "no tutorial running";
                            ClientSessionBase cs = Singleton<Main>.Instance.m_GameClient.Session as ClientSessionBase;
                            return "current=" + (ub.TutorialManager.m_CurrentAction != null ? ub.TutorialManager.m_CurrentAction.BuildActionName() + " t" + ub.TutorialManager.m_CurrentAction.m_Turn + " " + ub.TutorialManager.m_CurrentAction.m_TurnPhase : "none")
                                + " next=" + ub.TutorialManager.NextActionDescription + " reins=" + ub.TutorialManager.IsOnReins()
                                + (cs != null ? " phase=" + cs.CurrentPhase + " active=" + (cs.ActivePlayer != null ? cs.ActivePlayer.m_PlayerId + " turn " + cs.Cache.Players[cs.ActivePlayer.m_PlayerId].TurnNumber : "-")
                                   + " chain=" + (cs.Chain.PeekAbility() != null) : "");
                        }
                        Singleton<Main>.Instance.m_GameClient.StateManager.TransitionToState(Reckoning.Game.EGameState.Battle, new Game.Client.GameState.BattleStateContext
                        {
                            m_SceneTemplateId = Game.Shared.Resources.BuiltInResources.BattleSceneTutorialTemplate,
                            m_IsMandatoryTutorial = parts.Length > 1 && parts[1] == "mandatory"
                        });
                        return "tutorial: " + scene.GetType().Name;
                    }
                case "mercsets":   // dev: the mercenaries each set's boosters may bring (Mercenaries.OfSet)
                    {
                        System.Text.StringBuilder sb = new System.Text.StringBuilder();
                        foreach (string set in new[] { "Shards of Fate", "Shattered Destiny", "Armies of Myth", "Primal Dawn", "Herofall", "Scars of War", "Frostheart", "Dead of Winter", "Doombringer" })
                            sb.Append(set).Append(": ").Append(string.Join(", ", Mercenaries.OfSet(set).ConvertAll(m => m.m_Name).ToArray())).Append('\n');
                        return sb.ToString();
                    }
                case "deckgems": return Decks.DevGemRoundTrip(parts.Length > 1 ? ulong.Parse(parts[1]) : Decks.FirstPvpDeck());
                case "decknew":   // dev: "decknew <name> <game deck search>": a player deck of the account, copied from a game deck
                    {
                        if (parts.Length < 3) return "usage: decknew <name> <game deck search>";
                        string q = string.Join(" ", parts, 2, parts.Length - 2).ToLowerInvariant();
                        foreach (Reckoning.Game.DeckTemplate t in Singleton<Reckoning.Game.TemplateManager>.Instance.Decks.Values)
                        {
                            if (t == null || !t.m_ChampionId.IsValid || string.IsNullOrEmpty(t.m_DeckName) || t.m_DeckName.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) < 0) continue;
                            Game.Shared.Domain.deck_bits b = Decks.CreateFromTemplate(parts[1], t);
                            try { GameClient.Instance.PlayerProfile.AddDeck(b); } catch (Exception e) { return "created " + b.Id + ", profile: " + e.Message; }
                            return "created deck " + b.Id + " '" + parts[1] + "' from " + t.m_DeckName;
                        }
                        return "no game deck matching " + q;
                    }
                case "deckforai":   // dev: "deckforai <name>": marks a deck of the account as made for the AI
                case "deckcopy":    // dev: "deckcopy <name>": copies a deck made for the AI as a player deck (collection checked)
                    {
                        string n = string.Join(" ", parts, 1, parts.Length - 1);
                        Decks.DeckInfo di = Decks.ListAll().Find(x => x.Name == n);
                        if (di == null) return "no deck " + n;
                        ulong did = di.Id.GetInstanceId();
                        if (parts[0] == "deckforai")
                        {
                            Decks.AiSettings ai = Decks.Ai(did).Clone();
                            ai.ForAI = true;
                            Decks.SetAi(did, ai);
                            ai = Decks.Ai(did).Clone();
                            ai.ForAI = false;
                            Decks.SetAi(did, ai);   // must stay made for the AI
                            return "forAI=" + Decks.Ai(did).ForAI + " shared=" + Decks.IsAiDeck(did);
                        }
                        List<string> missing = new List<string>();
                        ulong copy = Decks.CopyToAccount(did, missing);
                        return copy != 0 ? "copied as " + copy : "missing: " + string.Join(", ", missing.ToArray());
                    }
                case "accountnew":   // dev: shows the "new account" screen of the account menu (login screen)
                    {
                        AccountMenu am = UnityEngine.Object.FindObjectOfType<AccountMenu>();
                        if (am == null) return "no account menu";
                        FieldInfo f = typeof(AccountMenu).GetField("m_Screen", BindingFlags.Instance | BindingFlags.NonPublic);
                        f.SetValue(am, Enum.Parse(f.FieldType, "Create"));
                        return "create screen";
                    }
                case "gostate":   // dev: e.g. "gostate PremiumStore"
                    {
                        Reckoning.Game.EGameState st = (Reckoning.Game.EGameState)Enum.Parse(typeof(Reckoning.Game.EGameState), parts[1]);
                        Singleton<Main>.Instance.m_GameClient.StateManager.TransitionToState(st, null);
                        return "-> " + st;
                    }
                case "storedump":   // dev: state of the store screen
                    {
                        UIPremiumStoreViewModel vm = UnityEngine.Object.FindObjectOfType<UIPremiumStoreViewModel>();
                        if (vm == null) return "no store";
                        BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                        System.Collections.IDictionary bp = typeof(UIPremiumStoreViewModel).GetField("m_BoosterPacks", bf).GetValue(vm) as System.Collections.IDictionary;
                        object ctx = typeof(UIPremiumStoreViewModel).GetField("Context", bf).GetValue(vm);
                        UIPremiumStoreContext c = ctx as UIPremiumStoreContext;
                        return "boosters=" + (bp != null ? bp.Count : -1) + " catalog=" + c.Catalog.Count + " tab=" + c.VisibleTab + " boosterTabVisible=" + c.BoosterPackTabVisible
                            + " curTab=" + typeof(UIPremiumStoreViewModel).GetField("m_CurrentTab", bf).GetValue(vm) + " dirty=" + typeof(UIPremiumStoreViewModel).GetField("m_CatalogDirty", bf).GetValue(vm) + " waiting=" + c.Waiting;
                    }
                case "zymmer": return Zymmer.Command(parts);
                case "news": return Community.Describe();   // dev: Featured Content source and the panels given to the game   // dev: Zymmer's pools, "zymmer chest 9 Rare", "zymmer kismet <pack>", "zymmer pack <pack>"
                case "loot":   // dev: "loot check" (names not found), "loot open [pack]" (draw without granting)
                    if (parts.Length > 1 && parts[1] == "open")
                    {
                        List<string> o = new List<string>();
                        foreach (Loot.Drop d in Loot.Open(parts.Length > 2 ? string.Join(" ", parts, 2, parts.Length - 2) : Loot.AZ1Pack))
                            o.Add(d.Card != null ? "card " + d.Card.m_Name : "item " + Singleton<TemplateManager>.Instance.GetInventoryItem(d.Item).m_Name);
                        return string.Join(", ", o.ToArray());
                    }
                    if (parts.Length > 1 && parts[1] == "names") return Loot.DumpNames();
                    if (parts.Length > 1 && parts[1] == "eqsets") return Loot.EquipmentSets();
                    if (parts.Length > 2 && parts[1] == "find") return Loot.Find(string.Join(" ", parts, 2, parts.Length - 2));
                    return Loot.Check();
                case "unclaim":   // dev: forget a one-time reward ("unclaim 1:aom6"; no argument: list)
                    if (parts.Length < 2) return string.Join(",", new List<string>(Saves.Current.Claims).ToArray());
                    string claim = string.Join(" ", parts, 1, parts.Length - 1);   // keys can hold spaces ("3:tamed:Dire Toad")
                    bool removed = Saves.Current.Claims.Remove(claim);
                    Saves.SaveCurrent();
                    return removed ? "removed " + claim : "not found";
                case "winbattle":   // dev: win the current battle
                    if (Server.CurrentSession == null) return "no game";
                    CheatMenu.EndAsWin(Server.CurrentSession);
                    return "won";
                case "losebattle":   // dev: lose the current battle
                    if (Server.CurrentSession == null) return "no game";
                    CheatMenu.EndAs(Server.CurrentSession, false);
                    return "lost";
                case "decktop":   // dev: each player's top deck card: what the game's rule (CanPlayerSeeCard) and the mod show, and whether it can be played
                    {
                        StandaloneSession ss = Server.CurrentSession;
                        if (ss == null) return "no game";
                        System.Text.StringBuilder sb = new System.Text.StringBuilder();
                        foreach (Player owner in ss.GetAllPlayers())
                        {
                            if (owner == null) continue;
                            Card top = owner.GetCardCollection(ECardCollections.Deck).GetCardAtLocation(ECardLocations.Top);
                            if (top == null) continue;
                            sb.Append(owner.IsAI() ? "AI" : "you").Append(" top=").Append(top.m_CardTemplate.m_Name).Append(" (").Append(top.m_SessionCardId).Append(")");
                            foreach (Player viewer in ss.GetAllPlayers())
                            {
                                if (viewer == null) continue;
                                sb.Append(" | to ").Append(viewer.IsAI() ? "AI" : "you").Append(": game=").Append(top.CanPlayerSeeCard(viewer)).Append(" mod=").Append(Server.IsVisibleTo(top, viewer)).Append(" canPlay=").Append(ss.CanPlayCard(top, viewer));
                            }
                            sb.Append('\n');
                            sb.Append("  flags: seeOppHand=").Append(owner.m_ChampionCard.GetCardContext().GetBool(IntAttrs.CanSeeOpponentsHand))
                              .Append(" seeTop=").Append(owner.m_ChampionCard.GetCardContext().GetBool(IntAttrs.CanSeeTopOfDeck))
                              .Append(" seeUnder=").Append(owner.m_ChampionCard.GetCardContext().GetBool(IntAttrs.CanSeeUndergroundTroops)).Append('\n');
                            if (owner.m_SimulacrumCard != null)
                            {
                                sb.Append("  simulacrum (party passives) in ").Append(owner.m_SimulacrumCard.m_CurrentCardCollection).Append(':');
                                foreach (ResourceId a in owner.m_SimulacrumCard.CurrentAbilities)
                                {
                                    AbilityTemplate at = Singleton<TemplateManager>.Instance.GetAbilityTemplate(a);
                                    sb.Append(" [").Append(at != null ? (at.m_GameText ?? at.m_Name).Replace("\n", " ") : a.ToString()).Append(']');
                                }
                                sb.Append('\n');
                            }
                            else sb.Append("  no simulacrum card\n");
                            try
                            {
                                sb.Append("  affecting champion:");
                                foreach (Card a in ss.GetListOfCardsAffectingTarget(owner.m_ChampionCard)) sb.Append(' ').Append(a.m_CardTemplate.m_Name).Append('/').Append(a.m_CurrentCardCollection);
                                sb.Append('\n');
                                foreach (Game.Shared.Mechanics.Abilities.AbilityEffectInstance ei in (System.Collections.Generic.IEnumerable<Game.Shared.Mechanics.Abilities.AbilityEffectInstance>)typeof(AbilityManager).GetField("m_EffectInstanceList", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).GetValue(ss.AbilityManager))
                                {
                                    if (ei == null || ei.TargetInstance == null || !ei.TargetInstance.m_SessionCardIds.Contains(owner.m_ChampionCard.m_SessionCardId)) continue;
                                    sb.Append("  effect ").Append(ei.AbilityEffectTemplate != null ? ei.AbilityEffectTemplate.GetType().Name : "?").Append(" from ").Append(ei.SourceCard != null ? ei.SourceCard.m_CardTemplate.m_Name : "?")
                                      .Append(" ability=").Append(ei.m_AbilityInstance != null && ei.m_AbilityInstance.m_AbilityTemplate != null ? ei.m_AbilityInstance.m_AbilityTemplate.m_Name + " " + (ei.m_AbilityInstance.m_AbilityTemplate.m_GameText ?? "").Replace("\n", " ") : "?").Append('\n');
                                }
                            }
                            catch (Exception ex) { sb.Append(" diag error " + ex.Message + '\n'); }
                            foreach (Card h in owner.GetAllCards())
                            {
                                if (h == null || h.m_CurrentCardCollection != ECardCollections.Hand) continue;
                                foreach (Player viewer in ss.GetAllPlayers())
                                {
                                    if (viewer == null || viewer == owner) continue;
                                    CardCollection col = h.GetCardCollection();
                                    sb.Append("  hand ").Append(h.m_CardTemplate.m_Name).Append(" col=").Append(col != null ? col.CollectionType.ToString() : "null")
                                      .Append(" faceDown=").Append(h.IsCardCollectionFaceDown(viewer)).Append(" onChain=").Append(h.IsOnChain())
                                      .Append(" game=").Append(h.CanPlayerSeeCard(viewer)).Append(" mod=").Append(Server.IsVisibleTo(h, viewer)).Append('\n');
                                }
                            }
                        }
                        return sb.ToString();
                    }
                case "pick":   // dev: "pick <n>" = selects the n-th card (from 0) of the open card window, as a click on it would (no OS mouse)
                    {
                        FieldInfo sf = typeof(UIBattle).GetField("m_StateStack", BindingFlags.Instance | BindingFlags.NonPublic);
                        Stack<BattleStateBase> stack = sf == null || UIBattle.Instance == null ? null : sf.GetValue(UIBattle.Instance) as Stack<BattleStateBase>;
                        BattleStateBase top = stack != null && stack.Count > 0 ? stack.Peek() : null;
                        if (top == null || top.Cards == null) return "no card window";
                        int n = 0;
                        if (parts.Length > 1) int.TryParse(parts[1], out n);
                        if (n < 0 || n >= top.Cards.Count) return "index out of range (" + top.Cards.Count + ")";
                        top.OnClick(top.Cards[n]);
                        return "picked " + (top.Cards[n].Card != null ? top.Cards[n].Card.SessionId.ToString() : "?") + " in " + top.GetType().Name;
                    }
                case "revealfix":   // dev: "revealfix on|off" = cards revealed from a deck go back face down when the reveal window closes (off = old behaviour)
                    Server.DeckRevealFix = parts.Length < 2 || parts[1] != "off";
                    return "revealfix " + (Server.DeckRevealFix ? "on" : "off");
                case "cardtext":   // dev: "cardtext <card name>" = the human player's cards of that name: abilities and whether their condition holds
                    {
                        StandaloneSession ss = Server.CurrentSession;
                        if (ss == null) return "no game";
                        string name = string.Join(" ", parts, 1, parts.Length - 1);
                        // "cardtext ai:<name>": the AI's cards ("ai:*" its champion's abilities and every troop's Rage).
                        bool ai = name.StartsWith("ai:");
                        if (ai) name = name.Substring(3);
                        System.Text.StringBuilder sb = new System.Text.StringBuilder();
                        foreach (Player p in ss.GetAllPlayers())
                        {
                            if (p == null || p.IsAI() != ai) continue;
                            if (name == "*")
                            {
                                foreach (ResourceId a in p.m_ChampionCard.CurrentAbilities) { AbilityTemplate at = Singleton<TemplateManager>.Instance.GetAbilityTemplate(a); if (at != null) sb.Append("champion: ").Append(at.m_GameText).Append('\n'); }
                                foreach (Player q in ss.GetAllPlayers())
                                    foreach (Card c in q.GetAllCards())
                                        if (c != null && c.m_CardTemplate != null && (c.m_CardTemplate.m_CardType & ECardTypes.Troop) != 0 && (c.m_CurrentCardCollection == ECardCollections.Warzone || c.m_CurrentCardCollection == ECardCollections.Hand))
                                            sb.Append(q.IsAI() ? "AI " : "you ").Append(c.m_CardTemplate.m_Name).Append(" in ").Append(c.m_CurrentCardCollection).Append(" rage=").Append(c.GetCardContext().Get(IntAttrs.Rage)).Append('\n');
                                continue;
                            }
                            sb.Append("equipment: ").Append(string.Join(", ", p.m_Equipment.ConvertAll(e => { InventoryItemData it = Singleton<TemplateManager>.Instance.GetInventoryItem(e); return it != null ? it.m_Name : e.ToString(); }).ToArray())).Append(" slots=").Append(p.m_EquipmentSlotsEnabled).Append('\n');
                            foreach (Card c in p.GetAllCards())
                            {
                                if (c == null || c.m_CardTemplate == null || !string.Equals(c.m_CardTemplate.m_Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                                sb.Append(c.m_SessionCardId).Append(" in ").Append(c.m_CurrentCardCollection).Append(" template ").Append(c.m_CardTemplate.m_Id).Append(" eqmod=").Append(c.m_CardTemplate.m_EquipmentModifiedCard).Append('\n');
                                foreach (ResourceId a in c.CurrentAbilities)
                                {
                                    AbilityTemplate at = Singleton<TemplateManager>.Instance.GetAbilityTemplate(a);
                                    if (at == null) continue;
                                    bool ok = at.m_AbilityCondition == null || at.m_AbilityCondition.IsValid(c, ss);
                                    sb.Append("   ").Append(ok ? "[on]  " : "[off] ").Append((at.m_GameText ?? "").Replace("\n", " ")).Append('\n');
                                }
                            }
                        }
                        return sb.ToString();
                    }
                case "deckown":   // dev: decks holding cards or equipment the account does not own
                    {
                        string rep = Decks.OwnershipReport();
                        File.WriteAllText(Path.Combine(LocalServer.Root, "HexRevivalDeckOwnership.txt"), rep);
                        return rep;
                    }
                case "deckeq":   // dev: "deckeq <deck id>" = the equipment of a stored deck, as sent to the client and to a match
                    {
                        ulong did;
                        if (parts.Length < 2 || !ulong.TryParse(parts[1], out did)) return "usage: deckeq <deck id>";
                        Game.Shared.Domain.deck_bits db = Decks.Bits(did);
                        if (db == null) return "no deck " + did;
                        List<string> o = new List<string>();
                        foreach (ResourceId e in new[] { db.equipment_1, db.equipment_2, db.equipment_3, db.equipment_4, db.equipment_5, db.equipment_6 })
                        {
                            if (!e.IsValid) continue;
                            InventoryItemData it = Singleton<TemplateManager>.Instance.GetInventoryItem(e);
                            o.Add((it != null ? it.m_Name : e.ToString()) + " owned=" + Inventory.Count(e));
                        }
                        return "deck " + did + " '" + db.DeckName + "': " + (o.Count == 0 ? "no equipment" : string.Join(", ", o.ToArray()));
                    }
                case "giveitem":   // dev: "giveitem <item name>" = the account owns one more of that inventory item (equipment...), client included
                    {
                        string name = string.Join(" ", parts, 1, parts.Length - 1);
                        InventoryItemData item = null;
                        foreach (InventoryItemData it in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                            if (string.Equals(it.m_Name, name, StringComparison.OrdinalIgnoreCase)) { item = it; break; }
                        if (item == null) return "no item " + name;
                        Game.Shared.Domain.inventory_bits b = Inventory.GrantSilently(item.m_Id);
                        if (b == null) return "not granted (unlock-everything account owns it already?)";
                        GameClient.Instance.PlayerProfile.SetInventoryItem(new UID(UID.Type.InventoryItem, b.Id), item.m_Id, 1);
                        return "gave " + item.m_Name + ", now " + Inventory.Count(item.m_Id);
                    }
                case "mercequip":   // dev: "mercequip <deck template id> <equipment name>" = equips a party deck template (as the party screen does)
                    {
                        ulong tid;
                        if (parts.Length < 3 || !ulong.TryParse(parts[1], out tid)) return "usage: mercequip <template id> <equipment name>";
                        Game.Shared.Profile.ProfileDeckTemplate t = DeckTemplates.Get(tid);
                        if (t == null) return "no template " + tid;
                        if (parts[2] == "clear") { t.Equip.Clear(); DeckTemplates.Put(t, tid); return "template " + tid + ": equipment removed"; }
                        string name = string.Join(" ", parts, 2, parts.Length - 2);
                        InventoryEquipmentData eq = null;
                        foreach (InventoryItemData it in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                            if (it is InventoryEquipmentData && string.Equals(it.m_Name, name, StringComparison.OrdinalIgnoreCase)) { eq = (InventoryEquipmentData)it; break; }
                        if (eq == null) return "no equipment " + name;
                        t.Equip[eq.m_EquipmentType] = eq.m_Id;
                        DeckTemplates.Put(t, tid);
                        return "template " + tid + " '" + t.Name + "': " + eq.m_EquipmentType + " = " + eq.m_Name;
                    }
                case "tameroll":   // dev: "tameroll [n]" = runs the Taming Sphere's 2-mana roll n times with the game's own effect and condition
                    {
                        StandaloneSession ss = Server.CurrentSession;
                        if (ss == null) return "no game";
                        Player human = null;
                        foreach (Player p in ss.GetAllPlayers()) if (p != null && !p.IsAI()) human = p;
                        if (human == null || human.m_ChampionCard == null) return "no human player";
                        int n = 200;
                        if (parts.Length > 1) int.TryParse(parts[1], out n);
                        Game.Shared.Mechanics.Abilities.AbilityActivationData data = new Game.Shared.Mechanics.Abilities.AbilityActivationData();
                        data.AbilityTemplateId = new ResourceId("5cd9dfa2-cb88-915a-3c06-8ef0bf6d8672");
                        data.SourceCardId = human.m_ChampionCard.m_SessionCardId;
                        Game.Shared.Mechanics.Abilities.AbilityEffectConditionTemplate cond = Singleton<TemplateManager>.Instance.GetAbilityEffectConditionTemplate(new ResourceId("6048ec17-555a-a1a9-b5e0-fcbc0312814c"));
                        if (cond == null) return "condition not found";
                        int tame = 0, min = int.MaxValue, max = int.MinValue;
                        for (int i = 0; i < n; i++)
                        {
                            Game.Shared.Mechanics.Abilities.AbilityInstance ai = new Game.Shared.Mechanics.Abilities.AbilityInstance(-1000 - i, data, human, ss);
                            Game.Shared.Mechanics.Abilities.AbilityEffectInstance roll = ai.m_AbilityEffectInstanceMap[0];
                            roll.AbilityEffectTemplate.Apply(roll);
                            int v; ai.TryGetAbilityVariable("RandomNumber", out v, false);
                            min = Math.Min(min, v); max = Math.Max(max, v);
                            if (cond.m_Condition.IsValid(ai.m_AbilityEffectInstanceMap[1])) tame++;
                        }
                        return "tameroll " + n + ": tame " + tame + " (" + (100 * tame / Math.Max(1, n)) + "%), rolls " + min + ".." + max;
                    }
                case "tametest":   // dev: "tametest [5]" = the human activates the Taming Sphere's 2-mana (or 5-mana) ability on an opposing Untamed troop
                    {
                        StandaloneSession ss = Server.CurrentSession;
                        if (ss == null || ReferenceEquals(gc.Player, null)) return "no game";
                        Player human = null, ai = null;
                        foreach (Player p in ss.GetAllPlayers()) if (p != null) { if (p.IsAI()) ai = p; else human = p; }
                        if (human == null || ai == null) return "players not found";
                        Card sphere = null, target = null;
                        foreach (Card c in human.m_Warzone) if (c != null && c.m_CardTemplate != null && c.m_CardTemplate.m_Name == "Taming Sphere") sphere = c;
                        foreach (Card c in ai.m_Warzone) if (c != null && c.IsTroop() && (c.Get(IntAttrs.Untamed) >= 1 || c.GetCardContext().Get(IntAttrs.Untamed) >= 1)) target = c;
                        if (sphere == null) return "no Taming Sphere";
                        if (target == null) return "no Untamed troop";
                        Game.Shared.Mechanics.Abilities.AbilityActivationData data = new Game.Shared.Mechanics.Abilities.AbilityActivationData();
                        data.AbilityTemplateId = new ResourceId(parts.Length > 1 && parts[1] == "5" ? "153271a7-9117-301f-e4bb-625d53064bfc" : "5cd9dfa2-cb88-915a-3c06-8ef0bf6d8672");
                        data.SourceCardId = sphere.m_SessionCardId;
                        data.TargetMap = new Dictionary<int, Game.Shared.Mechanics.Abilities.TargetTemplates.AbilityTargetInstance>();
                        Game.Shared.Mechanics.Abilities.TargetTemplates.AbilityTargetInstance ti = new Game.Shared.Mechanics.Abilities.TargetTemplates.AbilityTargetInstance();
                        ti.m_TargetTemplateId = new ResourceId("b71b0113-0f8f-9d9c-bf8a-1517143f3cab");
                        ti.m_SessionCardIds.Add(target.m_SessionCardId);
                        data.TargetMap[1] = ti;
                        gc.Player.ActivateAbility(data);
                        return "activated on " + target.m_SessionCardId + " " + target.m_CardTemplate.m_Name;
                    }
                case "res":   // dev: resource pools of both players
                    {
                        if (Server.CurrentSession == null) return "no game";
                        List<string> o = new List<string>();
                        foreach (Player p in Server.CurrentSession.GetAllPlayers())
                            if (p != null) o.Add(p.m_PlayerId + " res=" + p.m_CurrentResourcePool + "/" + p.m_TotalResourcePool);
                        return string.Join(", ", o.ToArray()) + " phase=" + Server.CurrentSession.CurrentTurnPhase;
                    }
                case "collsets":
                    {
                        // dev: copies owned per card set, from collection.txt
                        Dictionary<string, int> bySet = new Dictionary<string, int>();
                        foreach (string line in File.ReadAllLines(Saves.File("collection.txt")))
                        {
                            string[] p = line.Split(' ');
                            CardTemplate t = Singleton<TemplateManager>.Instance.GetCardTemplate(new ResourceId(p[0]));
                            CardSetTemplate set = null;
                            if (t != null) Singleton<TemplateManager>.Instance.CardSets.TryGetValue(t.m_SetId, out set);
                            string k = set != null ? set.m_Name : "?";
                            int c; bySet.TryGetValue(k, out c); bySet[k] = c + int.Parse(p[1]);
                        }
                        List<string> o = new List<string>();
                        foreach (KeyValuePair<string, int> kv in bySet) o.Add(kv.Key + "=" + kv.Value);
                        return string.Join(", ", o.ToArray());
                    }
                case "accountxp":
                    if (parts.Length > 2 && parts[2] == "reset") Saves.Current.PeriodStart = DateTime.MinValue;   // dev: ignore the daily cap
                    Progression.AddAccountXp(parts.Length > 1 ? int.Parse(parts[1]) : 50); return "account level " + Saves.Current.AccountLevel + ", xp " + Saves.Current.AccountXp + ", gold " + Saves.Current.Gold;
                case "cheatmenu": Settings.ShowCheats = parts.Length < 2 || parts[1] != "off"; return "show cheats = " + Settings.ShowCheats;
                case "uidump": return UiDump(parts.Length > 1 ? parts[1] : "");
                case "panonpcs":   // dev: the characters of the panorama shown (object, identifier, active, location given by the mod)
                    {
                        List<string> l = new List<string>();
                        foreach (GoPanoramaInteractable go in Resources.FindObjectsOfTypeAll<GoPanoramaInteractable>())
                            l.Add(go.name + " id=" + go.m_Identifier + " active=" + go.gameObject.activeInHierarchy
                                + " loc=" + (go.Context != null && go.Context.Location != null ? go.Context.Location.Node : "-"));
                        return string.Join("\n", l.ToArray());
                    }
                case "hover":   // dev: "hover <GameObject path>" = NGUI's OnHover(true) on it, as the mouse entering it would
                    {
                        GameObject go = parts.Length > 1 ? GameObject.Find(string.Join(" ", parts, 1, parts.Length - 1)) : null;
                        if (go == null) return "not found";
                        go.SendMessage("OnHover", true, SendMessageOptions.DontRequireReceiver);
                        return "hover " + go.name;
                    }
                case "equiphud":   // dev: the battle HUDs' equipment (and "equiphud open" = show the trays)
                    {
                        System.Text.StringBuilder sb = new System.Text.StringBuilder();
                        foreach (GoHUDPlayer h in UnityEngine.Object.FindObjectsOfType<GoHUDPlayer>())
                        {
                            ResourceId[] act = (ResourceId[])typeof(GoHUDPlayer).GetField("m_ActiveEquipment", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(h);
                            Game.Shared.Player p = h.m_Player != null && UIBattle.Instance != null ? UIBattle.Instance.GetPlayer(h.m_Player.PlayerId) : null;
                            sb.Append(h.name).Append(" player=").Append(p != null ? p.m_PlayerId.ToString() : "?").Append(" equip=").Append(p != null && p.m_Equipment != null ? p.m_Equipment.Count : -1)
                              .Append(" pve=").Append(h.Context.IsPvEMatch).Append(" shown=").Append(h.Context.ShowEquipment).Append(" active=");
                            foreach (ResourceId r in act) sb.Append(r.IsValid ? "x" : ".");
                            sb.Append('\n');
                            if (parts.Length > 1 && parts[1] == "open" && p != null) { h.SetActiveEquipmentForPlayer(p); h.OpenEquipmentTray(); }
                        }
                        return sb.ToString();
                    }
                case "sprites":   // dev: "sprites <MonoBehaviour type>" = the NGUI sprites and labels under every object of that type (inactive too)
                    {
                        Type t = parts.Length > 1 ? typeof(UIBattle).Assembly.GetType(parts[1]) : null;
                        if (t == null) return "no type";
                        System.Text.StringBuilder sb = new System.Text.StringBuilder();
                        foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(t))
                        {
                            Component c = o as Component;
                            if (c == null) continue;
                            foreach (UISprite sp in c.GetComponentsInChildren<UISprite>(true))
                                sb.Append(sp.name).Append(" atlas=").Append(sp.atlas != null ? sp.atlas.name : "?").Append(" sprite=").Append(sp.spriteName)
                                  .Append(" size=").Append(sp.width).Append('x').Append(sp.height).Append(" type=").Append(sp.type).Append('\n');
                            foreach (UILabel l in c.GetComponentsInChildren<UILabel>(true))
                                sb.Append("  label ").Append(l.name).Append(" '").Append(l.text).Append("' font=").Append(l.bitmapFont != null ? l.bitmapFont.name : l.trueTypeFont != null ? l.trueTypeFont.name : "?").Append(" size=").Append(l.fontSize).Append('\n');
                        }
                        return sb.Length > 0 ? sb.ToString() : "none";
                    }
                case "comps":   // dev: "comps <GameObject path>" = the components of that object and its children
                    {
                        GameObject go = parts.Length > 1 ? GameObject.Find(string.Join(" ", parts, 1, parts.Length - 1)) : null;
                        if (go == null) return "not found";
                        System.Text.StringBuilder sb = new System.Text.StringBuilder();
                        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                        {
                            sb.Append(t == go.transform ? t.name : "  " + t.name).Append(t.gameObject.activeSelf ? "" : " (off)").Append(":");
                            foreach (Component c in t.GetComponents<Component>()) sb.Append(' ').Append(c != null ? c.GetType().Name : "null");
                            sb.Append('\n');
                        }
                        return sb.ToString();
                    }
                case "concede":
                    {
                        UIBattle ui = UnityEngine.Object.FindObjectOfType<UIBattle>();
                        if (ui == null) return "no game";
                        // The game's own concede: the menu's "Concede", then "Yes".
                        try { ui.OnButton("Concede_Yes", 0, string.Empty); }
                        catch (Exception e) { return "ERR " + e; }
                        return "abandon";
                    }
                case "continue": PressGameOverContinue(); return "continue";
                case "aiplay":
                    if (player == null) return "noplayer";
                    if (parts.Length > 1 && parts[1] == "off")
                    {
                        m_AutoAI = false;
                        if (player.AITactical != null) player.RemoveAITactical();
                        return "aiplay off";
                    }
                    m_AutoAI = true;
                    m_ManualBlock = parts.Length > 1 && parts[1] == "manualblock";
                    if (player.AITactical == null) player.InitializeAITactical(new Game.Shared.AI.AIPersonality());
                    player.RequestPriorityResync();
                    return "aiplay on";
                case "aitick":
                    if (player == null || player.AITactical == null) return "no AI";
                    try
                    {
                        return "AIUpdate=" + player.AITactical.AIUpdate(session.CurrentPhase) + " phase=" + session.CurrentPhase
                            + " active=" + player.IsActivePlayer() + " prio=" + player.IsPriorityPlayer();
                    }
                    catch (Exception e) { return "AIUpdate exception: " + e; }
                case "packlist": return PackContents.DevOpen(parts.Length > 1 ? (parts[1] == "close" ? -1f : float.Parse(parts[1])) : 0f);
                case "artcheck":
                    if (parts.Length > 1)
                    {
                        string q = string.Join(" ", parts, 1, parts.Length - 1);
                        foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
                            if (t != null && t.m_Name == q) return t.m_Id + " text: " + t.m_GameText + "\nlayout: " + t.m_CardLayoutId;
                        return "not found";
                    }
                    return ChoiceArt.Check();
                case "showcard":   // dev: "showcard <name>" = the card viewer a card link opens (UISystem.DisplayLinkedCard)
                    {
                        string q = string.Join(" ", parts, 1, parts.Length - 1);
                        foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
                            if (t != null && t.m_Name == q) { UISystem.DisplayLinkedCard(t, true, false, false); return "shown " + t.m_Name + " image='" + t.m_CardImagePath + "' parent=" + (ChoiceArt.Parent(t.m_Id) != null ? ChoiceArt.Parent(t.m_Id).m_Name : "-"); }
                        return "not found";
                    }
                case "packcheck": return PackContents.DevCheck();
                case "packsnap": return PackContents.DevSnaps();
                case "packdetail": return PackContents.DevDetail(string.Join(" ", parts, 1, parts.Length - 1));
                case "iconcheck": return PackContents.IconCheck(parts.Length > 1 && parts[1] == "all");
                case "cards": return CardsDump(gc, session, parts.Length > 1 && parts[1] == "all");
                case "clitext":   // dev: "clitext <n>" = the text the client writes on card SessionCardId(Card.n), from its ability list
                    {
                        if (session == null || parts.Length < 2) return "usage: clitext <n>";
                        foreach (KeyValuePair<SessionCardId, CardRepresentation> kv in session.Cache.Cards)
                        {
                            if (kv.Key.ToString() != "SessionCardId(Card." + parts[1] + ")") continue;
                            CardRepresentation rep = kv.Value;
                            CardTemplate ct = Singleton<TemplateManager>.Instance.GetCardTemplate(rep.TemplateId);
                            List<AbilityTemplate> act = new List<AbilityTemplate>();
                            foreach (ResourceId id in rep.Abilities) { AbilityTemplate a = Singleton<TemplateManager>.Instance.GetAbilityTemplate(id); if (a != null) act.Add(a); }
                            return rep.Abilities.Count + " abilities: " + (ct != null ? ct.BuildGameText(rep, act, XLIFFLocalization.instance) : "?");
                        }
                        return "not found";
                    }
                default: return Repl.Execute(gc, parts, Repl.BuildRefs(gc));
            }
        }

        private string DumpString(ClientSessionBase session)
        {
            if (session == null) return "nosession";
            return string.Format("phase={0} active={1} priority={2} play={3} attack={4} defend={5} activate={6} combats={7}",
                session.CurrentPhase, session.ActivePlayer?.m_PlayerId, session.PriorityPlayer?.m_PlayerId,
                session.Cache.GetCardsWithUsage(ECardUsage.Play).Count,
                session.Cache.GetCardsWithUsage(ECardUsage.Attack).Count,
                session.Cache.GetCardsWithUsage(ECardUsage.Defend).Count,
                session.Cache.GetCardsWithUsage(ECardUsage.Activate).Count,
                session.Cache.Combats.Count);
        }

        private string ServerDump(StandaloneGameClient gc)
        {
            FieldInfo f = typeof(StandaloneGameClient).GetField("m_SessionServer", BindingFlags.Instance | BindingFlags.NonPublic);
            StandaloneSession ss = ReferenceEquals(f, null) ? null : f.GetValue(gc) as StandaloneSession;
            if (ReferenceEquals(ss, null)) return "no authoritative session";
            List<string> sb = new List<string>();
            sb.Add("phase=" + ss.CurrentTurnPhase + " active=" + (ss.GetActivePlayer()?.m_PlayerId) + " priority=" + (ss.GetPriorityPlayer()?.m_PlayerId));
            foreach (Player p in ss.GetAllPlayers())
            {
                sb.Add("player " + p.m_PlayerId + " ai=" + p.IsAI() + " hand=" + p.GetCardCollection(ECardCollections.Hand).CardCount + " war=" + p.GetCardCollection(ECardCollections.Warzone).CardCount + " deck=" + p.GetCardCollection(ECardCollections.Deck).CardCount);
                foreach (Card c in p.GetCardCollection(ECardCollections.Hand))
                    sb.Add("  hand " + c.m_CardTemplate.m_Name + " cost=" + c.CastingCost + " canPlay=" + ss.CanPlayCard(c, p) + " inPlayable=" + ss.IsInPlayableLocation(c, p));
                foreach (Card c in p.GetCardCollection(ECardCollections.Warzone))
                    sb.Add("  war  " + c.m_CardTemplate.m_Name + " canAtk=" + c.CanAttack() + " canBlk=" + c.CanBlock());
            }
            foreach (Combat cb in ss.CombatManager.Combats)
                sb.Add("combat atk=" + (cb.Attacker != null ? cb.Attacker.m_CardTemplate.m_Name : "?") + " blockers=" + cb.Blockers.Count);
            return string.Join(" | ", sb.ToArray());
        }

        /// <summary>
        /// Compares the client view (cache) and the server view of each card.
        /// Without "all": cards outside the deck, or diverging. '!' = zone/controller divergence.
        /// </summary>
        private string CardsDump(StandaloneGameClient gc, ClientSessionBase session, bool all)
        {
            FieldInfo f = typeof(StandaloneGameClient).GetField("m_SessionServer", BindingFlags.Instance | BindingFlags.NonPublic);
            StandaloneSession ss = ReferenceEquals(f, null) ? null : f.GetValue(gc) as StandaloneSession;
            if (ReferenceEquals(ss, null) || session == null) return "no session";
            List<string> sb = new List<string>();
            foreach (Card c in ss.GetAllCards())
            {
                if (c == null) continue;
                CardRepresentation r;
                session.Cache.Cards.TryGetValue(c.m_SessionCardId, out r);
                UID ctrl = c.m_ControllingPlayer != null ? c.m_ControllingPlayer.m_PlayerId : UID.Invalid;
                bool diff = r == null || r.Collection != c.m_CurrentCardCollection || r.Controller != ctrl;
                if (!all && !diff && c.m_CurrentCardCollection == ECardCollections.Deck) continue;
                sb.Add((diff ? "! " : "  ") + c.m_SessionCardId + " " + c.m_CardTemplate.m_Name
                    + " srv=" + c.m_CurrentCardCollection + "/" + ctrl.GetInstanceId()
                    + " " + c.CurrentAttackValue + "/" + c.CurrentDefenseValue + " (base " + c.m_CardTemplate.m_BaseAttackValue + "/" + c.m_CardTemplate.m_BaseDefenseValue + ")"
                    + " cli=" + (r == null ? "absent" : r.Collection + "/" + r.Controller.GetInstanceId() + " use=" + r.Usage));
            }
            return string.Join("\n", sb.ToArray());
        }

        /// <summary>Combat UI animation queue (an unfinished blocking animation freezes everything).</summary>
        private string AnimsDump()
        {
            if (ReferenceEquals(UIBattle.Instance, null)) return "noUIBattle";
            List<string> sb = new List<string>();
            DumpAnimQueue(UIBattle.Instance.AnimationManager, 0, sb);
            return string.Join("\n", sb.ToArray());
        }

        private static void DumpAnimQueue(object queue, int depth, List<string> sb)
        {
            if (queue == null || depth > 4 || sb.Count > 80) return;
            FieldInfo f = queue.GetType().GetField("m_Animations", BindingFlags.Instance | BindingFlags.NonPublic);
            System.Collections.IEnumerable items = f == null ? null : f.GetValue(queue) as System.Collections.IEnumerable;
            if (items == null) return;
            foreach (object o in items)
            {
                AnimationBase a = o as AnimationBase;
                if (a == null) continue;
                sb.Add(new string(' ', depth * 2) + a.GetType().Name + " done=" + a.IsFinished + " blk=" + a.IsBlocking + " " + a);
                DumpAnimQueue(a, depth + 1, sb);
            }
        }

        private static string Join(List<SessionCardId> ids)
        {
            string[] arr = new string[ids.Count];
            for (int i = 0; i < ids.Count; i++) arr[i] = ids[i].ToString();
            return string.Join(",", arr);
        }

        private void Finish(string reason)
        {
            if (m_Quit)
                return;
            m_Quit = true;
            WriteState("FINISH: " + reason);
            Application.Quit();
        }

        private void WriteState(string message)
        {
            try
            {
                if (!string.IsNullOrEmpty(m_StatePath))
                    File.AppendAllText(m_StatePath, "[" + Time.realtimeSinceStartup.ToString("F1") + "] " + message + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
