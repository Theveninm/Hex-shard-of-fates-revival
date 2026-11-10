using System.Collections.Generic;
using System.Reflection;
using Game.Shared;
using Game.Shared.Mechanics;
using Game.Shared.Mechanics.Abilities;
using Game.Shared.Mechanics.Abilities.TargetTemplates;
using Game.Shared.Mechanics.Transactions;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// The AI's answer to a target choice asked while an ability resolves, completed before the engine sees it.
    ///
    /// When an effect makes a champion choose cards as it resolves ("Each champion sacrifices a troop":
    /// Merciless Culler, invoked once per champion), the engine asks the responsible player
    /// (AbilityActivationDataRequired). The game's AI answers that request with an empty activation
    /// (AITactical.TriggeredAbilityDataRequested, forceEmptyActivation), so a required choice of its own was
    /// skipped: only the human player sacrificed (Stigro, Shin'hare Abductor's Merciless Culler on an empty
    /// board; the AI should have sacrificed the Culler). Here, a target the ability requires (the target
    /// template's minimum count) and that the AI left empty is filled from the engine's legal targets: the
    /// AI's own cards least valuable first, the other players' cards most valuable first.
    /// </summary>
    public static class AiTargets
    {
        private static readonly FieldInfo s_Instance = typeof(SetAbilityActivationDataTransaction).GetField("m_AbilityInstanceId", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo s_Data = typeof(SetAbilityActivationDataTransaction).GetField("m_AbilityActivationData", BindingFlags.Instance | BindingFlags.NonPublic);

        public static void Repair(StandaloneSession ss, SetAbilityActivationDataTransaction tx)
        {
            AbilityActivationData data = s_Data.GetValue(tx) as AbilityActivationData;
            AbilityInstance instance = ss.AbilityManager.GetAbilityInstance((long)s_Instance.GetValue(tx));
            if (data == null || instance == null || instance.m_ResponsiblePlayer == null) return;
            if (data.Disable || (data.OptedInSet && !data.OptedIn)) return;
            // "You may ...": the AI's choice not to is kept.
            if (instance.m_AbilityTemplate == null || instance.m_AbilityTemplate.IsOptional) return;
            Player ai = instance.m_ResponsiblePlayer;
            AbilityTemplate ability = instance.m_AbilityTemplate;
            Card source = instance.SourceCard;
            Dictionary<int, Dictionary<ResourceId, List<SessionCardId>>> legal = ss.GetPotentialTargetsForAbility(source, ai, ability.AbilityTemplateId, instance);
            if (legal == null) return;
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            // The engine's minimum counts (X counts included), in the order of the legal targets (as Session.ValidateXCost reads them).
            List<int> mins = ss.GetMinimumTargetCountsForAbility(source, ai, ability.AbilityTemplateId, instance) ?? new List<int>();
            int num = -1;
            foreach (KeyValuePair<int, Dictionary<ResourceId, List<SessionCardId>>> byIndex in legal)
            {
                num++;
                if (byIndex.Key < 0 || byIndex.Key >= ability.m_AbilityTargetTemplateIds.Count) continue;
                AbilityTargetTemplate target = tm.GetAbilityTargetTemplate(ability.m_AbilityTargetTemplateIds[byIndex.Key]);
                if (target == null || target.IsAutoTarget) continue;
                int min = target.MinTargetCount is TargetConstant ? ((TargetConstant)target.MinTargetCount).Value : 0;
                if (num < mins.Count && mins[num] > min) min = mins[num];
                AbilityTargetInstance given;
                if (data.TargetMap == null) data.TargetMap = new Dictionary<int, AbilityTargetInstance>();
                data.TargetMap.TryGetValue(byIndex.Key, out given);
                int have = given != null && given.m_SessionCardIds != null ? given.m_SessionCardIds.Count : 0;
                if (min <= 0 || have >= min) continue;
                List<SessionCardId> candidates;
                if (!byIndex.Value.TryGetValue(target.m_TemplateId, out candidates) || candidates == null || candidates.Count == 0) continue;
                List<Card> cards = new List<Card>();
                foreach (SessionCardId id in candidates)
                {
                    Card c = ss.ResourceCache.GetCard(id);
                    if (c != null && (given == null || !given.m_SessionCardIds.Contains(id))) cards.Add(c);
                }
                cards.Sort((a, b) => Rank(a, ai).CompareTo(Rank(b, ai)));
                if (given == null) given = new AbilityTargetInstance { m_TargetTemplateId = target.m_TemplateId };
                if (given.m_SessionCardIds == null) given.m_SessionCardIds = new List<SessionCardId>();
                List<string> picked = new List<string>();
                for (int i = 0; i < cards.Count && given.m_SessionCardIds.Count < min; i++)
                {
                    given.m_SessionCardIds.Add(cards[i].m_SessionCardId);
                    if (cards[i].m_ControllingPlayer != null && !given.m_PlayerIds.Contains(cards[i].m_ControllingPlayer.m_PlayerId))
                        given.m_PlayerIds.Add(cards[i].m_ControllingPlayer.m_PlayerId);
                    picked.Add(cards[i].GetName());
                }
                data.TargetMap[byIndex.Key] = given;
                Debug.Log("[HexRevival] [AiTargets] " + ability.Name + ": the AI left \"" + target.m_GameText + "\" empty, chosen " + string.Join(", ", picked.ToArray()));
            }
        }

        /// <summary>Sort key: the AI's own cards from the least valuable, then the others from the most valuable.</summary>
        private static int Rank(Card c, Player ai)
        {
            int value = c.m_CardTemplate.m_ResourceCost * 10 + c.CurrentAttackValueClamped + c.CurrentDefenseValueClamped;
            bool own = c.m_ControllingPlayer != null && c.m_ControllingPlayer.m_PlayerId == ai.m_PlayerId;
            return own ? value : 100000 - value;
        }
    }
}
