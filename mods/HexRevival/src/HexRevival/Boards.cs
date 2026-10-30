using System;
using Game.Client.GameState;
using Game.Shared;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Which battleboard a battle is fought on. The game (UIBattle.Start) takes the board of the battle's scene: the
    /// campaign and dungeon encounters name theirs (Forest, CastleExterior, Boat...), the Frost Ring Arena is fought
    /// on its frozen lake. Only a scene without a board uses the decks' battleboards. It reads the scene from the
    /// session state the server had sent before the battle screen opened; offline that state does not exist yet,
    /// so every battle was on the deck's board (SirBeastmaster, 1.2.9). Src.Game: EncounterScene.m_Gameboard;
    /// Src.Video: the arena on the frozen lake (MHS 2017-07-27 [zrQSL4GDpjo], 2018-03-28 [xDS22ljeNa8]).
    /// </summary>
    public static class Boards
    {
        /// <summary>After UIBattle.Start: the scene's board (or the arena's) when the game found none.</summary>
        public static void OnBattleStart(UIBattle battle)
        {
            try
            {
                if (battle == null || battle.m_CurGameboard != GameboardEnums.Default || PlayerConfig.UseDefaultGameboard) return;
                BattleStateContext ctx = Singleton<Main>.Instance.m_GameClient.StateManager.GetCurrentStateContext() as BattleStateContext;
                // The arena: always its frozen lake (every arena fight seen), whatever the opponent's encounter.
                GameboardEnums board = Arena.InBattle ? ArenaBoard : GameboardEnums.Default;
                SceneData scene;
                if (board == GameboardEnums.Default && ctx != null && ctx.m_SceneTemplateId.IsValid
                    && Singleton<TemplateManager>.Instance.CampaignScenes.TryGetValue(ctx.m_SceneTemplateId, out scene) && scene is EncounterScene)
                    board = ((EncounterScene)scene).Gameboard;
                if (board == GameboardEnums.Default) return;
                battle.m_CurGameboard = board;
                Debug.Log("[HexRevival] [Boards] battleboard " + board);
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Boards] " + e); }
        }

        /// <summary>The Frost Ring Arena's board (the game's choice for an arena battle without a scene).</summary>
        public static GameboardEnums ArenaBoard = GameboardEnums.FrostLock;
    }
}
