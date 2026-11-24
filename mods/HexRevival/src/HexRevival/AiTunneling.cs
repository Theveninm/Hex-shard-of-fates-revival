using System;
using System.Reflection;
using Game.Shared;
using Game.Shared.AI;
using Game.Shared.Mechanics.Abilities;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// The game AI's use of Tunneling made sensible (Wormoid encounters).
    ///
    /// The game's AI (AITactical.PlayCard) only considers a hand card it can cast in full (CanPlay), and then, if the card
    /// has a tunnel ability it can activate, tunnels it instead of casting it: with 8 resources it tunnels its 8-cost
    /// Wormoid for 2 and does nothing else that turn; with less than 8 it never tunnels at all (GooseChase 2026-11-24; the
    /// same in the original game's videos). Its only other tunneling code (AIPersonality.FavorTunneling) is never called.
    /// Here: a card the AI can cast in full is cast (no tunneling inside PlayCard), and once the AI has nothing else to do
    /// in its main phase (BuildBoard returns false), a Tunneling card it cannot afford is tunneled if it can pay that.
    /// </summary>
    public static class AiTunneling
    {
        [ThreadStatic] private static bool s_InPlayCard;
        private static FieldInfo s_Functions;
        private static MethodInfo s_GetActivationFor, s_ActivateCard;

        public static void PlayCardEnter() { s_InPlayCard = true; }
        public static void PlayCardExit() { s_InPlayCard = false; }

        /// <summary>AIFunctions.HasAbilityEffectTemplate inside PlayCard: false for the tunnel effect (the card is cast).</summary>
        public static bool HideTunnel(Type t) { return s_InPlayCard && t == typeof(TunnelAbilityEffectTemplate); }

        /// <summary>After BuildBoard found nothing to do on the AI's turn: tunnels a card it cannot cast, if it can. True if done.</summary>
        public static bool TunnelLeftover(AITactical ai, bool myTurn)
        {
            if (ai == null || !myTurn) return false;
            try
            {
                BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                if (s_Functions == null) s_Functions = typeof(AITactical).GetField("m_Functions", bf);
                if (s_GetActivationFor == null) s_GetActivationFor = typeof(AITactical).GetMethod("GetActivationFor", bf);
                if (s_ActivateCard == null) s_ActivateCard = typeof(AITactical).GetMethod("ActivateCard", bf);
                AIFunctions fn = s_Functions != null ? s_Functions.GetValue(ai) as AIFunctions : null;
                if (fn == null || s_GetActivationFor == null || s_ActivateCard == null) return false;
                foreach (CardRepresentation card in fn.GetAICards(Game.Shared.Mechanics.ECardCollections.Hand))
                {
                    if (card == null || card.Tunneling <= 0 || card.CanPlay() || card.Abilities == null) continue;
                    foreach (ResourceId ab in card.Abilities)
                    {
                        if (!fn.HasAbilityEffectTemplate(ab, typeof(TunnelAbilityEffectTemplate), false) || !fn.CanActivateAbility(card.SessionId, ab)) continue;
                        AbilityActivationData data = s_GetActivationFor.Invoke(ai, new object[] { card, card, ab, 0L, false }) as AbilityActivationData;
                        if (data == null) continue;
                        Debug.Log("[HexRevival] [AI] tunnels " + card.Name + " (cost " + card.Cost + ", Tunneling " + card.Tunneling + ")");
                        s_ActivateCard.Invoke(ai, new object[] { data });
                        return true;
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [AI] tunneling: " + e.Message); }
            return false;
        }
    }
}
