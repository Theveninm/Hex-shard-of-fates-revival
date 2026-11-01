using System.Collections.Generic;
using Game.Shared;
using Game.Shared.Mechanics;
using Game.Shared.Mechanics.Transactions;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// The AI's block declaration made legal before the engine sees it.
    ///
    /// The game's AI (AICombat.DetermineBestBlocks) picks its blockers without the blocking rules the
    /// engine enforces: "Opposing troops must block this if able" on an attacker (Pheromones, Killblade of
    /// the Milky Eye; IntAttrs.MustBeBlocked), "must block" on its own troop (IntAttrs.MustBlock), and Feral
    /// (two blockers or none). CommitTroopsToDefenseTransaction.Resolve rejects the whole declaration when
    /// any of them is broken ("Illegal blocks in transaction") and the AI, seeing the same phase again,
    /// passes: it blocks nothing at all, not even the attackers it had chosen blockers for (Criesener,
    /// 2026-11-01, Pheromones on an attacker in a campaign battle: the three AI troops did not block).
    /// The declaration is repaired here with the engine's own rules (Session.AreBlocksLegal /
    /// AreNonBlocksLegal): a troop that can block an attacker that must be blocked blocks one of them (the
    /// one with the fewest blockers so far), a troop that must block blocks the first attacker it can, a lone
    /// blocker on a Feral attacker is freed. The AI's other choices are kept.
    /// </summary>
    public static class AiBlocks
    {
        public static void Repair(Session ss, CommitTroopsToDefenseTransaction tx)
        {
            if (ss == null || tx == null) return;
            if (tx.m_DefenseDeclarations == null) tx.m_DefenseDeclarations = new List<CommitTroopsToDefenseTransaction.DefenseDeclaration>();
            Player defender = ss.GetPlayer(tx.m_PlayerId);
            Player active = ss.GetActivePlayer();
            if (defender == null || active == null || defender == active) return;
            List<Card> mustBeBlocked = active.GetAllAttackersThatMustBeBlocked();
            List<Card> attackers = active.GetAllAttackers();
            if (attackers == null || attackers.Count == 0) return;

            List<string> changes = new List<string>();
            foreach (Card troop in defender.GetCardCollection(ECardCollections.Warzone).GetTroopCards(false))
            {
                if (troop == null || troop.IsBlocking()) continue;
                CommitTroopsToDefenseTransaction.DefenseDeclaration current = DeclarationOf(tx, troop.m_SessionCardId);
                Card currentAttacker = current != null ? ss.ResourceCache.GetCard(current.AttackerId) : null;

                // An attacker that must be blocked takes every troop able to block it.
                List<Card> can = new List<Card>();
                if (mustBeBlocked != null)
                    foreach (Card a in mustBeBlocked) if (troop.CanBlock(a) == ECombatantStatus.Ok) can.Add(a);
                if (can.Count > 0)
                {
                    if (currentAttacker != null && can.Contains(currentAttacker)) continue;
                    Card target = null;
                    foreach (Card a in can) if (target == null || Blockers(tx, a.m_SessionCardId) < Blockers(tx, target.m_SessionCardId)) target = a;
                    if (current != null) current.DefendingCardIds.Remove(troop.m_SessionCardId);
                    Assign(tx, target.m_SessionCardId, troop.m_SessionCardId);
                    changes.Add(troop.GetName() + " -> " + target.GetName() + " (must be blocked)");
                    continue;
                }

                // A troop that must block blocks the first attacker it can.
                if (current == null && troop.GetCardContext().GetBool(IntAttrs.MustBlock))
                {
                    foreach (Card a in attackers)
                    {
                        if (troop.CanBlock(a) != ECombatantStatus.Ok) continue;
                        Assign(tx, a.m_SessionCardId, troop.m_SessionCardId);
                        changes.Add(troop.GetName() + " -> " + a.GetName() + " (must block)");
                        break;
                    }
                }
            }

            // Feral: one blocker alone is illegal; it is freed unless the attacker must be blocked (nothing
            // better to do then, the engine decides).
            foreach (CommitTroopsToDefenseTransaction.DefenseDeclaration d in tx.m_DefenseDeclarations)
            {
                Card a = ss.ResourceCache.GetCard(d.AttackerId);
                if (a == null || d.DefendingCardIds == null || d.DefendingCardIds.Count != 1 || !a.GetCardContext().GetBool(IntAttrs.Feral) || a.MustBeBlocked()) continue;
                Card b = ss.ResourceCache.GetCard(d.DefendingCardIds[0]);
                changes.Add((b != null ? b.GetName() : d.DefendingCardIds[0].ToString()) + " freed from " + a.GetName() + " (Feral)");
                d.DefendingCardIds.Clear();
            }
            tx.m_DefenseDeclarations.RemoveAll(d => d.DefendingCardIds == null || d.DefendingCardIds.Count == 0);
            if (changes.Count > 0) Debug.Log("[HexRevival] [Server] AI blocks repaired: " + string.Join("; ", changes.ToArray()));
        }

        private static CommitTroopsToDefenseTransaction.DefenseDeclaration DeclarationOf(CommitTroopsToDefenseTransaction tx, SessionCardId blocker)
        {
            foreach (CommitTroopsToDefenseTransaction.DefenseDeclaration d in tx.m_DefenseDeclarations)
                if (d.DefendingCardIds != null && d.DefendingCardIds.Contains(blocker)) return d;
            return null;
        }

        private static int Blockers(CommitTroopsToDefenseTransaction tx, SessionCardId attacker)
        {
            foreach (CommitTroopsToDefenseTransaction.DefenseDeclaration d in tx.m_DefenseDeclarations)
                if (d.AttackerId == attacker) return d.DefendingCardIds != null ? d.DefendingCardIds.Count : 0;
            return 0;
        }

        private static void Assign(CommitTroopsToDefenseTransaction tx, SessionCardId attacker, SessionCardId blocker)
        {
            foreach (CommitTroopsToDefenseTransaction.DefenseDeclaration d in tx.m_DefenseDeclarations)
            {
                if (d.AttackerId != attacker) continue;
                if (d.DefendingCardIds == null) d.DefendingCardIds = new List<SessionCardId>();
                d.DefendingCardIds.Add(blocker);
                return;
            }
            tx.m_DefenseDeclarations.Add(new CommitTroopsToDefenseTransaction.DefenseDeclaration(attacker, new List<SessionCardId> { blocker }));
        }
    }
}
