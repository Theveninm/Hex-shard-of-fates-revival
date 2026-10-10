using System;
using System.Collections.Generic;
using Game.Client;
using Game.Client.GameState;
using Game.Shared.Campaign.Messages;
using Game.Shared;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// In-game cheat menu (first draft), shown when "Show the cheat menu" is checked in the
    /// game's display options (see Settings): a small
    /// button in the bottom-left corner unfolds a list of cheats for the current screen,
    /// battle cheats during a match, progression cheats elsewhere.
    /// </summary>
    public class CheatMenu : MonoBehaviour
    {
        private bool m_Open;
        private string m_Message;
        private float m_MessageUntil;

        private static Main M { get { return Singleton<Main>.Instance; } }

        private static bool LoggedIn()
        {
            try
            {
                return Settings.ShowCheats && Saves.Current != null && M != null && M.m_GameClient != null
                    && M.m_GameClient.PlayerProfile != null && M.m_GameClient.PlayerProfile.IsInitialized();
            }
            catch { return false; }
        }

        private static EGameState State { get { return M.m_GameClient.StateManager.CurrentGameState; } }

        private void Say(string text)
        {
            m_Message = text;
            m_MessageUntil = Time.realtimeSinceStartup + 3f;
        }

        // Screen areas of the button and panel (GUI coordinates, set while drawing): the game's
        // interface is blocked while the mouse is over them, otherwise clicks go through to it.
        private Rect m_ToggleRect, m_PanelRect;
        private readonly List<Behaviour> m_DisabledUi = new List<Behaviour>();

        private void Update()
        {
            Settings.EnsureOptionRow();
            Vector2 mouse = new Vector2(Input.mousePosition.x, UnityEngine.Screen.height - Input.mousePosition.y);
            bool over = LoggedIn() && (m_ToggleRect.Contains(mouse) || (m_Open && m_PanelRect.Contains(mouse)));
            if (over && m_DisabledUi.Count == 0)
            {
                foreach (UICamera c in UICamera.list.ToArray())
                    if (c != null && c.enabled) { c.enabled = false; m_DisabledUi.Add(c); }
            }
            else if (!over && m_DisabledUi.Count > 0)
            {
                foreach (Behaviour b in m_DisabledUi) if (b != null) b.enabled = true;
                m_DisabledUi.Clear();
            }
        }

        private void OnGUI()
        {
            if (!LoggedIn()) { m_Open = false; return; }
            float S = GameSkin.Scale;
            GUI.depth = -900;
            Rect toggle = new Rect(10 * S, UnityEngine.Screen.height - 52 * S, 42 * S, 42 * S);
            m_ToggleRect = toggle;
            GameSkin.Sprite(toggle, "A_Frames", "Frame_Inner");
            GUI.DrawTexture(new Rect(toggle.x + 2, toggle.y + 2, toggle.width - 4, toggle.height - 4), GameSkin.Solid(new Color(0.1f, 0.05f, 0.15f, 0.85f)));
            GameSkin.Label(toggle, "✦", GameSkin.Style("title", 22, m_Open ? Color.white : GameSkin.Gold, TextAnchor.MiddleCenter));
            if (GUI.Button(toggle, new GUIContent("", Loc.Get("cheat_menu")), GUIStyle.none)) m_Open = !m_Open;

            if (m_Open)
            {
                List<KeyValuePair<string, Action>> items = Items();
                float rowH = 40 * S, w = 280 * S;
                float h = 46 * S + items.Count * (rowH + 4 * S) + 10 * S;
                Rect panel = new Rect(toggle.x, toggle.y - h - 8 * S, w, h);
                m_PanelRect = new Rect(panel.x, panel.y, panel.width, panel.height + 8 * S);
                if (!GameSkin.Sprite(panel, "A_Frames", "Frame_Generic_Inner"))
                    GUI.DrawTexture(panel, GameSkin.Solid(new Color(0.06f, 0.07f, 0.1f, 0.95f)));
                GUI.DrawTexture(new Rect(panel.x + 3, panel.y + 3, panel.width - 6, panel.height - 6), GameSkin.Solid(new Color(0, 0, 0, 0.55f)));
                GameSkin.Label(new Rect(panel.x + 14 * S, panel.y + 8 * S, w - 28 * S, 30 * S), Loc.Get("cheat_menu"), GameSkin.Style("title", 19, GameSkin.Gold));
                float y = panel.y + 46 * S;
                foreach (KeyValuePair<string, Action> it in items)
                {
                    if (GameSkin.Row(new Rect(panel.x + 10 * S, y, w - 20 * S, rowH), it.Key, null, false))
                    {
                        try { it.Value(); }
                        catch (Exception e) { Say(e.Message); Debug.LogError("[HexRevival] [Cheats] " + e); }
                    }
                    y += rowH + 4 * S;
                }
            }
            if (m_Message != null && Time.realtimeSinceStartup < m_MessageUntil)
                GameSkin.Label(new Rect(toggle.xMax + 10 * S, toggle.y, 600 * S, toggle.height), m_Message, GameSkin.Style("body", 16, GameSkin.Gold));
        }

        /// <summary>Cheats for the current screen.</summary>
        private List<KeyValuePair<string, Action>> Items()
        {
            List<KeyValuePair<string, Action>> items = new List<KeyValuePair<string, Action>>();
            StandaloneGameClient gc = M.m_GameClient as StandaloneGameClient;
            StandaloneSession ss = State == EGameState.Battle && gc != null ? Cheats.GetServer(gc) : null;
            if (ss != null)
            {
                items.Add(new KeyValuePair<string, Action>(Loc.Get("cheat_win"), () => WinBattle(ss)));
                Player me = Cheats.FindPlayer(ss, "j");
                if (me != null)
                {
                    items.Add(new KeyValuePair<string, Action>(Loc.Get("cheat_resources"), () =>
                        { Cheats.AddResourcesOnly(ss, me, 10); Say(Loc.Get("cheat_done")); }));
                    items.Add(new KeyValuePair<string, Action>(Loc.Get("cheat_shards"), () => { Cheats.AddThresholds(ss, me, 1); Say(Loc.Get("cheat_done")); }));
                    items.Add(new KeyValuePair<string, Action>(Loc.Get("cheat_draw"), () => { Cheats.DrawCard(ss, me); Say(Loc.Get("cheat_done")); }));
                }
            }
            else
            {
                items.Add(new KeyValuePair<string, Action>(Loc.Get("cheat_gold"), () => AddMoney(1000, 0)));
                items.Add(new KeyValuePair<string, Action>(Loc.Get("cheat_platinum"), () => AddMoney(0, 1000)));
                if (State == EGameState.Dungeon)
                    items.Add(new KeyValuePair<string, Action>(Loc.Get("cheat_reveal"), RevealMap));
            }
            return items;
        }

        private void WinBattle(StandaloneSession ss)
        {
            List<UID> winners = new List<UID>(), losers = new List<UID>();
            foreach (Player p in ss.GetAllPlayers())
                if (p != null) (p.m_PlayerId.GetUIDType() == UID.Type.AIPlayer ? losers : winners).Add(p.m_PlayerId);
            ss.EndGame(winners, losers, true);
            Server.MarkDirty();
            m_Open = false;
        }

        private void AddMoney(int gold, int platinum)
        {
            Saves.Current.Gold += gold;
            Saves.Current.Platinum += platinum;
            Saves.SaveCurrent();
            M.m_GameClient.PlayerProfile.UpdateBalance((int)Saves.Current.Gold, (int)Saves.Current.Platinum);
            Say(Loc.Get("cheat_done"));
        }

        /// <summary>Reveals the whole campaign map, then reloads the map screen to show it.</summary>
        private void RevealMap()
        {
            PlayerChampion ch = M.m_GameClient.PlayerProfile.GetCurrentChampion();
            if (ch == null || !Campaign.RevealAll(ch.Id)) return;
            m_Open = false;
            UICampaignZoneVMBase.TransitionToState(CampaignType.AREA,
                new UICampaignZoneVMBase.StateContext { CampaignId = ch.Id, CampaignType = CampaignType.AREA });
        }
    }
}
