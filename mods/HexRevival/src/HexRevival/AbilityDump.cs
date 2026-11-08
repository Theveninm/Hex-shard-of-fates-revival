using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Game.Client;
using Game.Shared;
using Game.Shared.Mechanics;
using Reckoning.Game;

namespace HexRevival
{
    /// <summary>
    /// Dev: dumps the game's own rule data for cards (abilities, their trigger, and each effect's TAC data), to find
    /// statically what a battle's cards do. Written to HexRevivalAbilities.txt in the game folder.
    ///   "abildump battle"     every card of the current battle outside the decks, and its live state (cost, context)
    ///   "abildump card NAME"  the live state of the current battle's cards of that name
    ///   "abildump NAME"       every card template of that name
    /// </summary>
    internal static class AbilityDump
    {
        internal static string Command(StandaloneGameClient gc, string[] parts)
        {
            if (parts.Length < 2) return "abildump battle | card <name> | <name>";
            StringBuilder sb = new StringBuilder();
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            if (parts[1] == "battle" || parts[1] == "card")
            {
                StandaloneSession ss = Session(gc);
                if (ss == null) return "no authoritative session";
                string name = parts[1] == "card" ? string.Join(" ", parts, 2, parts.Length - 2) : null;
                HashSet<string> templatesDone = new HashSet<string>();
                foreach (Card c in ss.GetAllCards())
                {
                    if (c == null) continue;
                    if (name != null ? !string.Equals(c.m_CardTemplate.m_Name, name, StringComparison.OrdinalIgnoreCase)
                                     : c.m_CurrentCardCollection == ECardCollections.Deck) continue;
                    sb.Append("=== ").Append(c.m_SessionCardId).Append(' ').Append(c.m_CardTemplate.m_Name)
                      .Append(" zone=").Append(c.m_CurrentCardCollection)
                      .Append(" owner=").Append(c.m_ControllingPlayer != null ? c.m_ControllingPlayer.m_PlayerId.ToString() : "?")
                      .Append(" cost=").Append(c.CastingCost).Append(" (resource ").Append(c.ResourceCost)
                      .Append(", adjustment ").Append(c.CastingCostAdjustment).Append(", template ").Append(c.m_CardTemplate.m_ResourceCost).Append(")\n");
                    FieldInfo ctx = typeof(Card).GetField("m_InternalCurrentContext", BindingFlags.Instance | BindingFlags.NonPublic);
                    TAC context = ctx == null ? null : ctx.GetValue(c) as TAC;
                    if (context != null) sb.Append("  context: ").Append(context.TextDescription("    ")).Append('\n');
                    foreach (ResourceId id in c.DynamicAbilities) { sb.Append("  dynamic "); Ability(sb, tm, id, "    "); }
                    foreach (ResourceId id in c.RevokedAbilities) sb.Append("  revoked ").Append(id.m_Guid).Append('\n');
                    if (templatesDone.Add(c.m_CardTemplate.m_Id.m_Guid.ToString())) Template(sb, tm, c.m_CardTemplate);
                }
            }
            else
            {
                string name = string.Join(" ", parts, 1, parts.Length - 1);
                foreach (CardTemplate t in tm.Cards.Values)
                    if (t != null && string.Equals(t.m_Name, name, StringComparison.OrdinalIgnoreCase)) Template(sb, tm, t);
            }
            string path = Path.Combine(Directory.GetCurrentDirectory(), "HexRevivalAbilities.txt");
            File.WriteAllText(path, sb.ToString());
            return "written " + path + " (" + sb.Length + " chars)";
        }

        /// <summary>The object's fields that differ from their default, recursively (rule data has no other text form).</summary>
        private static string Describe(object o, int depth)
        {
            if (o == null) return "-";
            Type t = o.GetType();
            if (t.IsPrimitive || t.IsEnum || o is string || o is ResourceId || o is Guid) return o.ToString();
            if (t.Name.StartsWith("ConstrainedType")) { FieldInfo v = t.GetField("m_InternalType"); object it = v == null ? null : v.GetValue(o); return it != null ? ((Type)it).Name : "-"; }
            if (o is TAC) { string d = ((TAC)o).TextDescription(""); return d.Length == 0 ? "" : "TAC{" + d + "}"; }
            if (depth > 5) return t.Name + "{...}";
            if (o is System.Collections.IDictionary) return t.Name + "(" + ((System.Collections.IDictionary)o).Count + ")";
            if (o is System.Collections.IEnumerable)
            {
                List<string> items = new List<string>();
                foreach (object i in (System.Collections.IEnumerable)o) { items.Add(Describe(i, depth + 1)); if (items.Count > 20) break; }
                return items.Count == 0 ? "" : "[" + string.Join(", ", items.ToArray()) + "]";
            }
            List<string> fields = new List<string>();
            for (Type bt = t; bt != null && bt != typeof(object); bt = bt.BaseType)
                foreach (FieldInfo f in bt.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.Name == "m_Logger" || f.Name.StartsWith("<") || f.Name == "m_tac") continue;
                    object v = f.GetValue(o);
                    if (v == null || (v is bool && !(bool)v) || (v is int && (int)v == 0) || (v is string && ((string)v).Length == 0)) continue;
                    if (v is ResourceId && !((ResourceId)v).IsValid) continue;
                    string d = Describe(v, depth + 1);
                    if (d.Length == 0 || d == "-") continue;
                    fields.Add(f.Name.Replace("m_", "") + "=" + d);
                }
            return t.Name + "{" + string.Join(" ", fields.ToArray()) + "}";
        }

        private static StandaloneSession Session(StandaloneGameClient gc)
        {
            FieldInfo f = typeof(StandaloneGameClient).GetField("m_SessionServer", BindingFlags.Instance | BindingFlags.NonPublic);
            return ReferenceEquals(f, null) || gc == null ? null : f.GetValue(gc) as StandaloneSession;
        }

        private static void Template(StringBuilder sb, TemplateManager tm, CardTemplate t)
        {
            sb.Append("  template ").Append(t.m_Id.m_Guid).Append(' ').Append(t.m_CardType).Append(" cost ").Append(t.m_ResourceCost).Append('\n');
            foreach (CardAbilityContainer a in t.m_CardAbilities) { sb.Append("  ability "); Ability(sb, tm, a.m_CardAbilityId, "    "); }
        }

        private static void Ability(StringBuilder sb, TemplateManager tm, ResourceId id, string indent)
        {
            AbilityTemplate at = tm.GetAbilityTemplate(id);
            if (at == null) { sb.Append(id.m_Guid).Append(" (unknown)\n"); return; }
            sb.Append(id.m_Guid).Append(" \"").Append(at.m_Name).Append("\": ").Append((at.m_GameText ?? "").Replace("\n", " ")).Append('\n');
            sb.Append(indent).Append("trigger=").Append(Describe(at.m_TriggerEventType, 0))
              .Append(" in=").Append(at.m_TriggerCollectionFlags)
              .Append(" triggerCondition=").Append(Describe(at.m_TriggerCondition, 0))
              .Append(" condition=").Append(Describe(at.m_AbilityCondition, 0))
              .Append(" casting=").Append(at.m_CastingBehavior).Append('\n');
            if (at.tac != null) sb.Append(indent).Append("tac: ").Append(at.tac.TextDescription("")).Append('\n');
            for (int i = 0; i < at.m_AbilityTargetTemplateIds.Count; i++)
                sb.Append(indent).Append("target#").Append(i).Append(' ').Append(Describe(tm.GetAbilityTargetTemplate(at.m_AbilityTargetTemplateIds[i]), 0)).Append('\n');
            foreach (AbilityEffectTargetMapping m in at.m_AbilityEffectList)
            {
                sb.Append(indent).Append("mapping ").Append(Describe(m, 0)).Append('\n');
                if (m.m_ConditionId.IsValid) sb.Append(indent).Append("condition ").Append(Describe(tm.GetAbilityEffectConditionTemplate(m.m_ConditionId), 0)).Append('\n');
                AbilityEffectTemplate e = tm.GetAbilityEffectTemplate(m.m_EffectTemplateId);
                sb.Append(indent).Append("effect ").Append(m.m_EffectTemplateId.m_Guid).Append(" target#").Append(m.m_TargetTemplateIndex)
                  .Append(" duration=").Append(m.m_EffectDuration);
                if (e == null) { sb.Append(" (unknown)\n"); continue; }
                sb.Append(' ').Append(e.GetType().Name).Append(" \"").Append(e.m_Name).Append("\"\n");
                sb.Append(indent).Append("  ").Append(Describe(e, 0)).Append('\n');
            }
        }
    }
}
