using System.Collections.Generic;
using System.Reflection;
using Game.Shared;
using Game.Shared.Mechanics.Transactions;

namespace HexRevival
{
    /// <summary>
    /// The damage order window (BattleStateAssignDamage.NextStep, replaced here): an attacker blocked by several troops
    /// asked its order twice, the second answer replacing the first (Stigro; reproduced: one combat in
    /// CombatsThatNeedDamage, no event between the two windows, yet the client's combat list gave the same combat
    /// again). The window is now asked once per combat: a combat already ordered is skipped.
    /// </summary>
    public static class DamageOrder
    {
        private static readonly FieldInfo s_Assignments = typeof(BattleStateAssignDamage).GetField("m_DamageAssignments", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo s_Commit = typeof(BattleStateAssignDamage).GetMethod("Commit", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>Prefix of NextStep: the game's own steps, skipping the combats already ordered. Returns false (replaced).</summary>
        public static bool NextStep(BattleStateAssignDamage state)
        {
            List<AssignDamageOrderTransaction.DamageAssignment> done = s_Assignments.GetValue(state) as List<AssignDamageOrderTransaction.DamageAssignment>;
            while (UIBattle.Instance.State.Combats.Count > 0)
            {
                PlayerOptions.AttackerBlockerPair pair = UIBattle.Instance.State.GetNextAttackerBlockerPair();
                if (pair.Id == Game.Shared.Mechanics.CombatId.Invalid || pair.Blockers == null || pair.Blockers.Count == 0) continue;
                if (done != null && done.Exists(d => d.CombatId == pair.Id))
                {
                    UnityEngine.Debug.Log("[HexRevival] [DamageOrder] " + pair.Id + " already ordered: not asked again");
                    continue;
                }
                UIBattle.Instance.PushState(new BattleStateAssignCombatDamage(pair, (combatId, cardIds) =>
                {
                    if (done != null) done.Add(new AssignDamageOrderTransaction.DamageAssignment(combatId, cardIds));
                    NextStep(state);
                }, null));
                return false;
            }
            s_Commit.Invoke(state, null);
            return false;
        }
    }
}
