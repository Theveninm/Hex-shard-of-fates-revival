using System;
using System.Collections.Generic;
using Game.Shared;
using Game.Shared.Domain;
using Game.Shared.Mechanics;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Two account options, off by default (TalekithOP, 2026-11-24; the user: like other content that differs from the
    /// original game, an option, off by default as it may hold unfinished content):
    /// - "Champion levels up to 20": the campaign XP table (Game: levels 1-15, 110,000 XP at 15) gets levels 16 to 20,
    ///   20,000 XP each (130,000 ... 190,000; TalekithOP's suggestion: PLACEHOLDER). The game itself counts talent
    ///   points up to level 25 (TalentManager.GetTotalTalentPoints: min(25, level) - 1), and its screens read the
    ///   table (XpTable.GetLevelReqs, the shard alignment's levels), so nothing else changes.
    /// - "Level 30 talent at level 20" (with the first): each class's one talent of level 30, never reachable in the
    ///   original (cap 15), can be taken from level 20 when the game data marks it finished (m_ImplStage Done, with
    ///   its ability; 10 talents in all). Of the warrior, mage and cleric ones (8 races x 3 classes), 4 are: Siege
    ///   (Human Warrior), Alwyn's Loyalty (Human Cleric), Power Harvest (Shin'hare Mage), Viciousness (Vennen
    ///   Warrior). The 20 others are "NeedsFinalDesign", a description without an ability: they stay at level 30.
    /// Turning the levels off brings the champions above 15 back to 15 with their talents reset to the mandatory ones
    /// (a free respec: their points no longer cover what they took). The XP they earned is kept.
    /// </summary>
    public static class LevelCap
    {
        public const int ExtendedMax = 20;
        /// <summary>XP from one level to the next above 15 (TalekithOP's suggestion: PLACEHOLDER).</summary>
        public const int XpPerExtraLevel = 20000;
        private const int UnreachedTalentLevel = 30;

        private static readonly List<XpTableItem> s_Added = new List<XpTableItem>();
        private static readonly Dictionary<ChampionTalentData, int> s_Lowered = new Dictionary<ChampionTalentData, int>();

        public static bool LevelsOn { get { return Saves.Current != null && !Saves.IsOnline && Saves.Current.Level20; } }
        public static bool TalentOn { get { return LevelsOn && Saves.Current.Level30Talent; } }

        /// <summary>The game's campaign XP table and talents as the current account's options want them.</summary>
        public static void Apply()
        {
            try
            {
                TemplateManager tm = Singleton<TemplateManager>.Instance;
                XpTable table = tm != null ? tm.GetCampaignXpTable() : null;
                if (table == null && tm != null) foreach (XpTable x in tm.XpTables.Values) if (x != null) { table = x; break; }   // as ChampionLevelForXp
                // The game's data loads on demand: before the XP table is there, Tick applies it a little later.
                if (table == null) { s_Pending = true; return; }
                s_Pending = false;
                List<XpTableItem> items = table.m_XpTable;
                if (LevelsOn && s_Added.Count == 0)
                {
                    int top = 0, xp = 0;
                    foreach (XpTableItem i in items) if (i.m_Level > top) { top = i.m_Level; xp = i.m_LevelXpRequirement; }
                    for (int l = top + 1; l <= ExtendedMax; l++)
                    {
                        xp += XpPerExtraLevel;
                        XpTableItem it = new XpTableItem();
                        it.m_Level = l;
                        it.m_LevelXpRequirement = xp;
                        items.Add(it);
                        s_Added.Add(it);
                    }
                    Debug.Log("[HexRevival] [LevelCap] levels up to " + ExtendedMax);
                }
                else if (!LevelsOn && s_Added.Count > 0)
                {
                    foreach (XpTableItem it in s_Added) items.Remove(it);
                    s_Added.Clear();
                    Debug.Log("[HexRevival] [LevelCap] levels back to the game's table");
                }

                if (TalentOn && s_Lowered.Count == 0)
                {
                    foreach (ChampionTalentData t in AllTalents(tm))
                        if (t != null && t.m_MinLevel == UnreachedTalentLevel && Finished(t)) { s_Lowered[t] = t.m_MinLevel; t.m_MinLevel = ExtendedMax; }
                    Debug.Log("[HexRevival] [LevelCap] " + s_Lowered.Count + " level 30 talents at level " + ExtendedMax);
                }
                else if (!TalentOn && s_Lowered.Count > 0)
                {
                    foreach (KeyValuePair<ChampionTalentData, int> kv in s_Lowered) kv.Key.m_MinLevel = kv.Value;
                    s_Lowered.Clear();
                }
                Champions.FitLevels();
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [LevelCap] " + e); }
        }

        private static bool s_Pending;
        private static float s_NextTry;

        /// <summary>Every frame (CheatMenu.Update): Apply again once the XP table is loaded, if it was not yet.</summary>
        public static void Tick()
        {
            if (!s_Pending || Time.realtimeSinceStartup < s_NextTry) return;
            s_NextTry = Time.realtimeSinceStartup + 1f;
            Apply();
        }

        /// <summary>The level a champion's XP gives now, and whether its talents must be reset (it was above the cap).</summary>
        internal static bool Fit(champion_bits c)
        {
            if (c == null || c.ChampionClass == EChampionClass.Boat) return false;   // the ship's level is its talent points
            int level = Progression.ChampionLevelForXp(c.CurrentXP);
            if (level > c.Level) { c.Level = level; return true; }
            if (c.Level > level && c.Level > 15 && level >= 15)
            {
                // Back under a lower cap: the talents are taken again from the mandatory ones.
                c.Level = level;
                c.ChampionTalents = new List<ResourceId>();
                Game.Shared.Talents.TalentManager.AddMandatoryTalents(c, c.ChampionTalents);
                return true;
            }
            if (!TalentOn && c.ChampionTalents != null)
            {
                // A level 30 talent taken with the option, which is now off.
                int before = c.ChampionTalents.Count;
                TemplateManager tm = Singleton<TemplateManager>.Instance;
                for (int i = c.ChampionTalents.Count - 1; i >= 0; i--)
                {
                    ChampionTalentData t = tm.GetChampionTalentTemplate(c.ChampionTalents[i]);
                    if (t != null && t.m_MinLevel > c.Level) c.ChampionTalents.RemoveAt(i);
                }
                return c.ChampionTalents.Count != before;
            }
            return false;
        }

        /// <summary>A talent the game data marks finished (ImplStage Done) and that has its ability.</summary>
        private static bool Finished(ChampionTalentData t)
        {
            System.Collections.ICollection ab = t.m_Abilities as System.Collections.ICollection;
            return t.m_ImplStage.ToString() == "Done" && ab != null && ab.Count > 0;
        }

        /// <summary>Every class's talents (through the classes' talent groups: the talent templates load on demand).</summary>
        private static IEnumerable<ChampionTalentData> AllTalents(TemplateManager tm)
        {
            List<ChampionTalentData> res = new List<ChampionTalentData>();
            foreach (ERace r in Enum.GetValues(typeof(ERace)))
                foreach (EChampionClass k in Enum.GetValues(typeof(EChampionClass)))
                {
                    ChampionClassData cd = null;
                    try { cd = tm.LookupChampionClassTemplate(r, k); } catch { }
                    if (cd == null || cd.m_TalentGroups == null) continue;
                    foreach (ChampionTalentGroupData g in cd.m_TalentGroups)
                        foreach (ResourceId id in g.m_Talents) { ChampionTalentData t = tm.GetChampionTalentTemplate(id); if (t != null && !res.Contains(t)) res.Add(t); }
                }
            return res;
        }
    }
}
