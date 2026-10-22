using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Client;
using Game.Shared.AI;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// AI against AI with the player watching (the attacks on the player's Siege defense): the game AI also plays the
    /// player's side, the windows that wait for a click are closed, a button speeds the game up (x4) until it is pressed
    /// again or the game ends, and the end screen is left after a few seconds.
    /// </summary>
    public class Spectator : MonoBehaviour
    {
        private static Spectator s_Instance;
        private AIPersonality m_Personality;
        private bool m_Active, m_Fast, m_AiAttached, m_SeenBattle;
        private float m_GameOverSince = -1f;
        private Rect m_Button;
        private readonly Controls.UiBlocker m_Block = new Controls.UiBlocker();

        private const float FastSpeed = 4f;

        private void Awake() { s_Instance = this; }

        public static bool Active { get { return s_Instance != null && s_Instance.m_Active; } }

        /// <summary>The next battle is watched: personality is the AI that plays the player's side.</summary>
        public static void Begin(AIPersonality personality)
        {
            if (s_Instance == null) return;
            s_Instance.m_Personality = personality ?? new AIPersonality();
            s_Instance.m_Active = true;
            s_Instance.m_AiAttached = false;
            s_Instance.m_SeenBattle = false;
            s_Instance.m_GameOverSince = -1f;
        }

        /// <summary>Back to a normal game (normal speed).</summary>
        public static void End()
        {
            if (s_Instance == null) return;
            s_Instance.m_Active = false;
            s_Instance.m_SeenBattle = false;
            s_Instance.SetFast(false);
            s_Instance.m_Block.Release();
        }

        /// <summary>Dev: "spectate": the speed button.</summary>
        public static string Toggle()
        {
            if (!Active) return "not watching";
            s_Instance.SetFast(!s_Instance.m_Fast);
            return "timeScale " + Time.timeScale;
        }

        private void SetFast(bool fast)
        {
            m_Fast = fast;
            Time.timeScale = fast ? FastSpeed : 1f;
        }

        private void Update()
        {
            if (!m_Active) return;
            try
            {
                UIBattle ui = UIBattle.Instance;
                GameClient gc = Singleton<Main>.Instance != null ? Singleton<Main>.Instance.m_GameClient : null;
                if (ReferenceEquals(ui, null) || gc == null || gc.StateManager.CurrentGameState != EGameState.Battle)
                {
                    // Out of the battle it watched: back to a normal game.
                    if (m_SeenBattle) End();
                    m_Block.Release();
                    return;
                }
                m_SeenBattle = true;
                if (ui.GameOver)
                {
                    // Every game ends at normal speed.
                    if (m_Fast) SetFast(false);
                    if (m_GameOverSince < 0f) m_GameOverSince = Time.realtimeSinceStartup;
                    else if (Time.realtimeSinceStartup - m_GameOverSince > 5f) { m_GameOverSince = float.MaxValue; PressContinue(); }
                    m_Block.Release();
                    return;
                }
                Game.Shared.LocalPlayer player = gc.Player as Game.Shared.LocalPlayer;
                if (player != null && player.AITactical == null && gc.Session != null)
                {
                    player.InitializeAITactical(m_Personality);
                    player.RequestPriorityResync();
                    if (!m_AiAttached) Debug.Log("[HexRevival] [Spectator] the AI plays the player's side");
                    m_AiAttached = true;
                }
                DismissInfoWindows();
                m_Block.Update(false, m_Button);
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Spectator] " + e.Message); }
        }

        private void OnGUI()
        {
            if (!m_Active || ReferenceEquals(UIBattle.Instance, null) || UIBattle.Instance.GameOver) return;
            float S = GameSkin.Scale;
            GUI.depth = -900;
            m_Button = new Rect(Screen.width / 2f - 130 * S, 64 * S, 260 * S, 44 * S);
            if (GameSkin.GoldButton(m_Button, Loc.Get(m_Fast ? "spectate_normal" : "spectate_fast"), true, 16)) SetFast(!m_Fast);
        }

        // ------------------------------------------------------------------
        // Battle helpers (also used by the dev soak mode)
        // ------------------------------------------------------------------

        private static object s_DismissCandidate;
        private static float s_DismissSince;

        /// <summary>Closes a window waiting for a click (revealed cards...) after 3 s, otherwise the game waits forever.</summary>
        public static void DismissInfoWindows()
        {
            FieldInfo f = typeof(UIBattle).GetField("m_StateStack", BindingFlags.Instance | BindingFlags.NonPublic);
            Stack<BattleStateBase> stack = f == null ? null : f.GetValue(UIBattle.Instance) as Stack<BattleStateBase>;
            BattleStateBase top = (stack != null && stack.Count > 0) ? stack.Peek() : null;
            BattleStateCoverflow cover = top as BattleStateCoverflow;
            if (cover == null || cover.UserInitiated) { s_DismissCandidate = null; return; }
            float now = Time.realtimeSinceStartup;
            if (!ReferenceEquals(s_DismissCandidate, cover)) { s_DismissCandidate = cover; s_DismissSince = now; return; }
            if (now - s_DismissSince > 3f)
            {
                cover.OnButton("Accept", 0, "");
                s_DismissCandidate = null;
            }
        }

        /// <summary>The end screen's "Continue".</summary>
        public static bool PressContinue()
        {
            FieldInfo f = typeof(UIBattle).GetField("m_StateStack", BindingFlags.Instance | BindingFlags.NonPublic);
            System.Collections.IEnumerable stack = f == null ? null : f.GetValue(UIBattle.Instance) as System.Collections.IEnumerable;
            if (stack == null) return false;
            foreach (object st in stack)
            {
                BattleStateGameOver go = st as BattleStateGameOver;
                if (go != null) { go.OnButton("Continue", 0, ""); return true; }
            }
            return false;
        }
    }
}
