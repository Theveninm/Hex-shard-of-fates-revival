using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Client;
using Game.Shared;
using Game.Shared.Mechanics;
using Game.Shared.Tutorial;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// The game's tutorial (5 scripted rounds against the AI, Src.Game: TutorialTemplate "NPE").
    /// </summary>
    public static class Tutorial
    {
        private static float s_LandingSince = -1f;

        /// <summary>
        /// On the home screen: a new account that asked for the tutorial starts it, as the game did right
        /// after creating an account (UICreateAccountViewModel.PromptAndGoToTutorial). It is started as
        /// the optional tutorial so that it can be left from the menu (the game's first one could not).
        /// The "TutorialStateFlag" makes the game show its rewards between the rounds, as the first time.
        /// </summary>
        public static void OnLandingPage()
        {
            Saves.Info save = Saves.Current;
            if (save == null || !save.TutorialPending) { s_LandingSince = -1f; return; }
            if (s_LandingSince < 0f) s_LandingSince = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - s_LandingSince < 1.5f) return;   // lets the home screen settle
            s_LandingSince = -1f;
            save.TutorialPending = false;
            Saves.SaveCurrent();
            PlayerPrefs.SetInt("TutorialStateFlag", 1);
            Debug.Log("[HexRevival] [Tutorial] started for the new account");
            Singleton<Main>.Instance.m_GameClient.StateManager.TransitionToState(Reckoning.Game.EGameState.Battle, new Game.Client.GameState.BattleStateContext
            {
                m_SceneTemplateId = Game.Shared.Resources.BuiltInResources.BattleSceneTutorialTemplate,
                m_IsMandatoryTutorial = false
            });
        }

        /// <summary>
        /// The menu (Esc) during an optional tutorial: the game enables its "Exit Tutorial" entry but shows the
        /// button only in other battles (UISystemMenuViewModel.Awake shows it with the Concede condition).
        /// </summary>
        public static void OnSystemMenu(UISystemMenuViewModel menu)
        {
            try
            {
                if (menu != null && menu.Context != null && menu.Context.IsExitTutorialEnabled && menu.MenuButtons.ExitTutorial != null)
                    menu.MenuButtons.ExitTutorial.gameObject.SetActive(true);
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Tutorial] menu: " + e.Message); }
        }

        /// <summary>
        /// The game's "Tutorial Rewards" window ("You've been rewarded with some new cards!", Src.Game):
        /// the cards it shows are given, once per account.
        /// </summary>
        public static void OnRewards(IEnumerable<ResourceId> cards)
        {
            try
            {
                Saves.Info save = Saves.Current;
                if (save == null || cards == null) return;
                List<string> names = new List<string>();
                foreach (ResourceId id in cards)
                {
                    if (!save.Claims.Add("tutorial:" + id)) continue;
                    Collection.Grant(id, 1);
                    Reckoning.Game.CardTemplate t = Singleton<Reckoning.Game.TemplateManager>.Instance.GetCardTemplate(id);
                    names.Add(t != null ? t.m_Name : id.ToString());
                }
                Saves.SaveCurrent();
                if (names.Count > 0) Debug.Log("[HexRevival] [Tutorial] rewards: " + string.Join(", ", names.ToArray()));
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Tutorial] rewards: " + e.Message); }
        }
        // Between two rounds the server removes every card of the previous round without telling the
        // clients (AuthoritativeSessionBase.PerformTutorialBoardSetup), so the clients' card caches keep
        // the old hand and board: the AI then believes it holds too many cards and tries forever to
        // discard a card that no longer exists, and the old troops stay on the player's board.
        private static bool s_Reset;          // a round was set up again: purge the AI's cache
        private static bool s_PurgePlayer;    // the new round has started: purge the player's cache and board too

        /// <summary>The server set the board up for the next tutorial round.</summary>
        public static void OnBoardReset() { s_Reset = true; s_PurgePlayer = false; }

        /// <summary>Every frame: removes from the clients' caches the cards the server no longer has.</summary>
        public static void Tick()
        {
            if (!s_Reset) return;
            if (UIBattle.Instance == null || UIBattle.Instance.TutorialManager == null) { s_Reset = false; return; }
            try
            {
                StandaloneSession server = Server.CurrentSession;
                StandaloneGameClient gc = Singleton<Main>.Instance != null ? Singleton<Main>.Instance.m_GameClient as StandaloneGameClient : null;
                if (server == null || gc == null) { s_Reset = false; return; }
                foreach (LocalPlayer lp in gc.m_LocalPlayerMap.Values)
                {
                    if (lp == null || lp.m_Session == null) continue;
                    bool human = lp == gc.Player;
                    if (human && !s_PurgePlayer) continue;   // the round's end animations still use the old cards
                    Purge(lp.m_Session as ClientSessionBase, server, human);
                }
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Tutorial] purge: " + e.Message); s_Reset = false; }
        }

        private static FieldInfo s_CardViews;

        private static void Purge(ClientSessionBase session, StandaloneSession server, bool human)
        {
            if (session == null || session.Cache == null) return;
            List<SessionCardId> stale = null;
            foreach (SessionCardId id in session.Cache.Cards.Keys)
            {
                Card c;
                if (!server.ResourceCache.TryGetCard(id, out c))
                {
                    if (stale == null) stale = new List<SessionCardId>();
                    stale.Add(id);
                }
            }
            if (stale == null) return;
            IDictionary views = null;
            if (human && UIBattle.Instance != null)
            {
                if (s_CardViews == null) s_CardViews = typeof(UIBattle).GetField("m_CardViews", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                views = s_CardViews != null ? s_CardViews.GetValue(UIBattle.Instance) as IDictionary : null;
            }
            foreach (SessionCardId id in stale)
            {
                session.Cache.Cards.Remove(id);
                GoCardView v = views != null && views.Contains(id) ? views[id] as GoCardView : null;
                if (v != null) UIBattle.Instance.DestroyCardView(v);
            }
            Debug.Log("[HexRevival] [Tutorial] " + stale.Count + " card(s) of the previous round removed from " + session.m_SessionName);
        }

        /// <summary>
        /// While the tutorial holds the player's hand ("on reins") the player stops at the phases its
        /// script uses (e.g. the opponent's Ready phase, where a tip explains the opponent's turn); the
        /// client passes by itself when nothing is expected (ClientTutorialManager.ShouldPassPriority).
        /// Without these stops the server passed for the player, the script missed its phase and waited
        /// forever while the turns went on. Once freed, the player's own stops apply.
        /// </summary>
        public static bool ScriptStop(Player p, Player turnPlayer, ETurnPhases phase)
        {
            try
            {
                if (p == null || turnPlayer == null || p.m_Session == null || !p.m_Session.IsTutorial() || Server.IsAIId(p.m_PlayerId)) return false;
                ClientTutorialManager tm = UIBattle.Instance != null ? UIBattle.Instance.TutorialManager : null;
                if (tm == null || !tm.IsOnReins()) return false;
                if (s_Template == null) s_Template = typeof(TutorialManager).GetField("m_TutorialTemplate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                TutorialTemplate t = s_Template != null ? s_Template.GetValue(tm) as TutorialTemplate : null;
                if (t == null) return false;
                EPlayerType turn = turnPlayer.m_PlayerId == p.m_PlayerId ? EPlayerType.Player : EPlayerType.AI;
                foreach (TurnPhaseActionBase a in t.m_TutorialActions)
                    if (a.m_TurnPhase == phase && a.m_ActivePlayer == turn) return true;
            }
            catch { }
            return false;
        }

        private static FieldInfo s_Template;

        // The script's actions are shared objects of the game data. The AI's copy of the script moves the
        // player's blocks to the AssignDamage phase (TutorialManager constructor), which on the original
        // servers only touched the server's own copy; here it also changed the player's script, which
        // then never offered the block. The AI gets its own copies of those actions instead.
        private static Dictionary<TurnPhaseActionBase, ETurnPhases> s_Phases;
        private static MethodInfo s_Clone;
        private static FieldInfo s_Queue;

        public static void BeforeScriptCopy(ClientSessionBase session)
        {
            s_Phases = null;
            try
            {
                Reckoning.Game.SceneData scene;
                if (session == null || session.m_EncounterData == null
                    || !Singleton<Reckoning.Game.TemplateManager>.Instance.CampaignScenes.TryGetValue(session.m_EncounterData.SceneTemplateId, out scene)) return;
                TutorialTemplate t = scene as TutorialTemplate;
                if (t == null) return;
                s_Phases = new Dictionary<TurnPhaseActionBase, ETurnPhases>();
                foreach (TurnPhaseActionBase a in t.m_TutorialActions) s_Phases[a] = a.m_TurnPhase;
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Tutorial] script copy: " + e.Message); }
        }

        public static void AfterScriptCopy(TutorialManager tm)
        {
            Dictionary<TurnPhaseActionBase, ETurnPhases> phases = s_Phases;
            s_Phases = null;
            if (phases == null || tm == null) return;
            try
            {
                BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                if (s_Queue == null) s_Queue = typeof(TutorialManager).GetField("m_ActionQueue", bf);
                if (s_Clone == null) s_Clone = typeof(object).GetMethod("MemberwiseClone", bf);
                Queue<TurnPhaseActionBase> queue = s_Queue.GetValue(tm) as Queue<TurnPhaseActionBase>;
                if (queue == null) return;
                Queue<TurnPhaseActionBase> own = new Queue<TurnPhaseActionBase>();
                int n = 0;
                foreach (TurnPhaseActionBase a in queue)
                {
                    ETurnPhases before;
                    if (phases.TryGetValue(a, out before) && before != a.m_TurnPhase)
                    {
                        TurnPhaseActionBase copy = (TurnPhaseActionBase)s_Clone.Invoke(a, null);
                        a.m_TurnPhase = before;
                        own.Enqueue(copy);
                        n++;
                    }
                    else own.Enqueue(a);
                }
                s_Queue.SetValue(tm, own);
                if (n > 0) Debug.Log("[HexRevival] [Tutorial] " + n + " action(s) of the shared script restored");
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Tutorial] script copy: " + e.Message); }
        }

        // The AI's script holds a copy of each of the player's blocks, performed in the AssignDamage
        // phase to order the damage of its blocked attacker (TurnPhaseActionDefendWithCards). The local
        // server only asks for an order when an attacker has several blockers; with a single blocker the
        // copy found no combat, sent nothing, and the AI waited without passing: the battle stopped.
        // The step is then simply done and the AI passes.
        private static FieldInfo s_AIScript;

        // The AI's script plays its cards by name (TurnPhaseActionPlayCard): when the card is not in the AI's hand, or
        // cannot be played (not enough resources), the action sends nothing, yet the script counts it as done and the
        // AI waits for an answer that never comes: its turn never ends (Zymmer, 1.2.7, last round). It happens once the
        // player is free (the last round: "Remove Reins from Player") and the battle no longer follows the script, e.g.
        // the AI's own logic already played that card. Such an action is skipped and the AI plays as usual.
        private static FieldInfo s_AIPlayer, s_AISession;
        public static bool SkipFix = true;   // dev "tutorial skipfix off": the game's behaviour, to reproduce the stall
        /// <summary>Dev ("tutorial stallai"): the AI's next update acts as if it had sent something and sends nothing.</summary>
        public static bool DevStallAI;

        /// <summary>Before the AI's update: skips the scripted card plays the AI cannot make now.</summary>
        public static void SkipImpossiblePlays(Game.Shared.AI.AITactical ai)
        {
            try
            {
                if (!SkipFix || ai == null || !ai.IsOnReins) return;
                BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                if (s_AIScript == null) s_AIScript = typeof(Game.Shared.AI.AITactical).GetField("m_TutorialManager", bf);
                if (s_AIPlayer == null) s_AIPlayer = typeof(Game.Shared.AI.AITactical).GetField("m_Player", bf);
                if (s_AISession == null) s_AISession = typeof(Game.Shared.AI.AITactical).GetField("m_Session", bf);
                TutorialManager tm = s_AIScript.GetValue(ai) as TutorialManager;
                LocalPlayer p = s_AIPlayer.GetValue(ai) as LocalPlayer;
                ClientSessionBase cs = s_AISession.GetValue(ai) as ClientSessionBase;
                if (tm == null || p == null || cs == null || cs.Cache == null) return;
                for (int guard = 0; guard < 20; guard++)
                {
                    TurnPhaseActionPlayCard play = tm.CurrentAction as TurnPhaseActionPlayCard;
                    if (play == null || CanPlay(cs, p, play.m_CardTemplateId)) return;
                    Debug.Log("[HexRevival] [Tutorial] AI script: " + play.BuildActionName() + " (turn " + play.m_Turn + ") cannot be played now, skipped");
                    tm.AdvanceTutorial();
                }
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Tutorial] AI script: " + e.Message); }
        }

        private static bool CanPlay(ClientSessionBase cs, LocalPlayer p, ResourceId template)
        {
            foreach (SessionCardId id in cs.Cache.GetCardsInCollection(ECardCollections.Hand, p.m_PlayerId))
            {
                CardRepresentation c;
                if (cs.Cache.Cards.TryGetValue(id, out c) && c.TemplateId == template && cs.Cache.HasUsage(id, ECardUsage.Play)) return true;
            }
            return false;
        }

        /// <summary>Dev "tutorial tracepass": logs who passes priority for the human (stack) to HexRevival.tracepass.log.</summary>
        public static bool TracePass;

        public static void OnPass(string what, object player)
        {
            if (!TracePass) return;
            try
            {
                string phase = "";
                ClientSessionBase cs = Singleton<Main>.Instance.m_GameClient.Session as ClientSessionBase;
                if (cs != null) phase = cs.CurrentPhase.ToString();
                System.IO.File.AppendAllText(System.IO.Path.Combine(LocalServer.Root, "HexRevival.tracepass.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + what + " " + player + " clientphase=" + phase + "\n" + Environment.StackTrace + "\n\n");
            }
            catch { }
        }

        /// <summary>True if the AI's update was done here (tutorial: block of a single troop).</summary>
        public static bool AIDamageOrder(Game.Shared.AI.AITactical ai, ETurnPhases phase, ref bool result)
        {
            try
            {
                if (ai == null || !ai.IsOnReins || (phase != ETurnPhases.AssignDamage && phase != ETurnPhases.AssignFirstStrikeDamage)) return false;
                BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                if (s_AIScript == null) s_AIScript = typeof(Game.Shared.AI.AITactical).GetField("m_TutorialManager", bf);
                TutorialManager tm = s_AIScript.GetValue(ai) as TutorialManager;
                ClientSessionBase cs = typeof(Game.Shared.AI.AITactical).GetField("m_Session", bf).GetValue(ai) as ClientSessionBase;
                if (tm == null || cs == null || cs.Cache.Combats.Count > 0) return false;
                TurnPhaseActionDefendWithCards block = tm.CurrentAction as TurnPhaseActionDefendWithCards;
                if (block == null || block.m_ActionPerformer != EPlayerType.Player) return false;
                tm.PlayerActionWasPerformed(block);
                result = ai.Pass();
                return true;
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Tutorial] AI damage order: " + e.Message); return false; }
        }

        /// <summary>
        /// End of the coin toss that opens a tutorial round. Between rounds the local server deals the
        /// new decks at once, so the round's first tip becomes current while the round's conversations
        /// are still shown; the client then skips it (ClientTutorialManager.SaveAction does nothing
        /// between rounds) and waits forever. The tip is shown here once the round has started.
        /// </summary>
        public static void OnRoundStarted()
        {
            if (s_Reset) s_PurgePlayer = true;
            try
            {
                UIBattle ub = UIBattle.Instance;
                ClientTutorialManager tm = ub != null ? ub.TutorialManager : null;
                if (tm == null || !(tm.m_CurrentAction is TurnPhaseActionShowTip)) return;
                FieldInfo f = typeof(UIBattle).GetField("m_StateStack", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                IEnumerable stack = f != null ? f.GetValue(ub) as IEnumerable : null;
                if (stack != null)
                    foreach (object st in stack) if (st is BattleStateShowTip) return;
                Debug.Log("[HexRevival] [Tutorial] round started: tip " + tm.m_CurrentAction.m_TipText);
                tm.HandleTutorialActionInit();
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Tutorial] round start: " + e.Message); }
        }
    }
}
