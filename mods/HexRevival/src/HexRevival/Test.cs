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
                string f = Path.Combine(Path.GetFullPath(Application.dataPath + "/../"), Instance.Name("HexRevival.stalls"));
                int n = 0;
                if (File.Exists(f)) int.TryParse(File.ReadAllText(f).Trim(), out n);
                File.WriteAllText(f, (n + 1).ToString());
            }
            catch { }
            Execute(gc, "restart");
        }

        private object m_DismissCandidate;
        private float m_DismissSince;

        private void AutoDismissInfoStates()
        {
            FieldInfo f = typeof(UIBattle).GetField("m_StateStack", BindingFlags.Instance | BindingFlags.NonPublic);
            Stack<BattleStateBase> stack = f == null ? null : f.GetValue(UIBattle.Instance) as Stack<BattleStateBase>;
            BattleStateBase top = (stack != null && stack.Count > 0) ? stack.Peek() : null;
            BattleStateCoverflow cover = top as BattleStateCoverflow;
            if (cover == null || cover.UserInitiated)
            {
                m_DismissCandidate = null;
                return;
            }
            float now = Time.realtimeSinceStartup;
            if (!ReferenceEquals(m_DismissCandidate, cover))
            {
                m_DismissCandidate = cover;
                m_DismissSince = now;
                return;
            }
            if (now - m_DismissSince > 3f)
            {
                WriteState("aiplay: auto-closing " + cover.GetType().Name);
                cover.OnButton("Accept", 0, "");
                m_DismissCandidate = null;
            }
        }

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
                string f = Path.Combine(Path.GetFullPath(Application.dataPath + "/../"), Instance.Name("HexRevival.stalls"));
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
                File.WriteAllText(Path.Combine(Path.GetFullPath(Application.dataPath + "/../"), Instance.Name("HexRevival.status.txt")), string.Join("\r\n", l.ToArray()).Replace("\n", "\r\n").Replace("\r\r", "\r") + "\r\n");
            }
            catch { }
        }

        private void PressGameOverContinue()
        {
            try
            {
                FieldInfo f = typeof(UIBattle).GetField("m_StateStack", BindingFlags.Instance | BindingFlags.NonPublic);
                System.Collections.IEnumerable stack = f == null ? null : f.GetValue(UIBattle.Instance) as System.Collections.IEnumerable;
                if (stack == null) return;
                foreach (object st in stack)
                {
                    BattleStateGameOver go = st as BattleStateGameOver;
                    if (go != null)
                    {
                        WriteState("GAME OVER: Continue");
                        go.OnButton("Continue", 0, "");
                        return;
                    }
                }
                WriteState("GAME OVER: BattleStateGameOver state not found");
            }
            catch (Exception e)
            {
                WriteState("GAME OVER: Continue a echoue " + e);
            }
        }
        private bool m_EncounterStarted;
        private bool m_Quit;
        private bool m_ClearedLoading;
        private float m_StartTime;
        private float m_LastLoginAttempt;
        private float m_LastActionTime;
        private float m_LastStateLog;
        private int m_LastPhase = -1;
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
            string root = Path.GetFullPath(Application.dataPath + "/../");
            m_StatePath = Path.Combine(root, Instance.Name("HexRevival.state.log"));
            m_CmdPath = Path.Combine(root, Instance.Name("HexRevival.cmd"));
            m_RespPath = Path.Combine(root, Instance.Name("HexRevival.resp"));
            m_AutoTest = File.Exists(Path.Combine(root, "HexRevival.autotest"));
            m_AutoStart = File.Exists(Path.Combine(root, "HexRevival.autostart"));
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
                LocalProfile.Pump();
                if (m_Speed != 1f && Time.timeScale != m_Speed)
                    Time.timeScale = m_Speed;
                // Stops supervised instances: HexRevival.soak.stop file.
                if (Instance.Supervised && Time.frameCount % 120 == 0
                    && File.Exists(Path.Combine(Path.GetFullPath(Application.dataPath + "/../"), "HexRevival.soak.stop")))
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
                    srv += " " + (p.IsAI() || p.m_PlayerId.GetUIDType() == UID.Type.AIPlayer ? "AI" : "P") + ":hp="
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
                string path = Path.Combine(Path.GetFullPath(Application.dataPath + "/../"), "HexRevival.encounters");
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
                if (string.IsNullOrEmpty(m_CmdPath) || !File.Exists(m_CmdPath))
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
                    catch (Exception e) { resp.Add("ERR " + e.Message); }
                }
                try { File.WriteAllLines(m_RespPath, resp.ToArray()); } catch { }
            }
            catch { }
        }

        /// <summary>Inventory of the visible NGUI UI (sprites, atlases, fonts) to reproduce its style.</summary>
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
                    string root2 = Path.GetFullPath(Application.dataPath + "/../");
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
                    string root = Path.GetFullPath(Application.dataPath + "/../");
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
                case "pass": return "pass=" + (player != null ? player.PassPriority().ToString() : "noplayer");
                case "autopass": if (player != null) player.SendAutoPass(EPassingState.EndOfTurn); return "autopass sent";
                case "accept": if (player != null) player.AcceptStartingHand(); return "accept sent";
                case "choose":
                    if (player == null) return "noplayer";
                    if (parts.Length > 1 && parts[1].StartsWith("d")) player.ChooseDrawFirst(); else player.ChoosePlayFirst();
                    return "choose sent";
                case "attack": if (player != null) player.DeclareAttack(new List<CommitTroopsToAttackTransaction.AttackDeclaration>()); return "attack none";
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
                case "uidump": return UiDump(parts.Length > 1 ? parts[1] : "");
                case "concede":
                    {
                        UIBattle ui = UnityEngine.Object.FindObjectOfType<UIBattle>();
                        if (ui == null) return "no game";
                        System.Reflection.MethodInfo q = typeof(UIBattle).GetMethod("Quit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(bool) }, null);
                        q.Invoke(ui, new object[] { false });
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
                case "cards": return CardsDump(gc, session, parts.Length > 1 && parts[1] == "all");
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
