using System;
using System.Collections.Generic;

namespace HexRevival
{
    /// <summary>
    /// Which conversation a map node shows, for the nodes whose dialogue depends on quest progress
    /// (Src.Game: the conversations are numbered steps of a quest giver, e.g. "Winston - Step 2").
    /// For each node, the first rule whose condition holds names the conversation (a part of its name).
    /// </summary>
    public static class NodeScripts
    {
        private class Rule
        {
            public string Name;
            public Func<ulong, bool> When;
        }

        private const string Cross = "az01_q_cross_the_river";
        private const string Cross2 = "az01_q_cross_the_river_part2";
        private const string Braves = "az01_three_braves";
        private const string KrakenDungeon = "6b73c598-3b92-46ea-bd57-62ca564b2e57";   // Dungeons: "The Kraken"

        private static bool S(ulong c, string q) { return Quests.IsStarted(c, q); }
        private static bool F(ulong c, string q) { return Quests.IsFinished(c, q); }
        private static bool D(ulong c, string q, string loc) { return Quests.DoneLoc(c, q, loc); }

        private static Rule R(string name, Func<ulong, bool> when) { return new Rule { Name = name, When = when }; }

        private static readonly Dictionary<string, Rule[]> Rules = new Dictionary<string, Rule[]>
        {
            // Cross the Zila River: Winston (west bank) and Weston (the bridge).
            { "Node012", new[] {
                R("Winston - Step 1", c => !S(c, Cross)),
                R("Winston - Step 2", c => !D(c, Cross, "Step1")),
                R("Winston - Step 3", c => !D(c, Cross, "Step2")),
                R("Winston - Step 4", c => true) } },
            { "Node015", new[] {
                R("Weston - Step 1a", c => D(c, Cross, "Step1") && !F(c, Cross)),
                R("Weston - Step 1b", c => !D(c, Cross, "Step1")),
                R("Weston - Step 2", c => true) } },
            // Cross the Zodiac River: Wallace and Warren.
            { "Node030", new[] {
                R("Wallace - Step 1", c => !S(c, Cross2)),
                R("Wallace - Step 2", c => !D(c, Cross2, "Step1")),
                R("Wallace - Step 3", c => !D(c, Cross2, "Step2")),
                R("Wallace - Step 4", c => true) } },
            { "Node038", new[] {
                R("Warren - Step 1a", c => D(c, Cross2, "Step1") && !F(c, Cross2)),
                R("Warren - Step 1b", c => !D(c, Cross2, "Step1")),
                R("Warren - Step 2", c => true) } },
            // Three Braves: the robot of Barking Forest and Bluejay.
            { "Node046", new[] {
                R("Robot - Step 1", c => !S(c, Braves) || (!D(c, Braves, "Step1") && !D(c, Braves, "Step1B"))),
                R("Robot - Step 2 - Install", c => D(c, Braves, "Step1") && !D(c, Braves, "Step1B")),
                R("Robot - Step 2a", c => !D(c, Braves, "Step2")),
                R("Robot - Step 3 - Install", c => !D(c, Braves, "Step2B")),
                R("Robot - Step 3a", c => !D(c, Braves, "Step3")),
                R("Robot - Step 4", c => !D(c, Braves, "Step3B")),
                R("Robot - Step 3a", c => true) } },
            { "Node020", new[] {
                R("Bluejay - Step 1", c => !S(c, Braves)),
                R("Bluejay - Step 2", c => !D(c, Braves, "Step2")),
                R("Bluejay - Step 3", c => !D(c, Braves, "Step3C")),
                R("Bluejay - Step 4a", c => !F(c, Braves) && Quests.Flag(c, "robot") == "choice_install"),
                R("Bluejay - Step 4b", c => !F(c, Braves)),
                R("Bluejay - Step 5", c => true) } },
            // Gaal fortune tellers: first meeting, then the repeat dialogue.
            { "Node007", new[] {
                R("Step 3 Player already has fortune", c => Fortunes.Has(c) && Quests.Flag(c, "visited:Node007") != null),
                R("Step 1 First encounter", c => Quests.Flag(c, "visited:Node007") == null),
                R("Step 2 Repeat", c => true) } },
            { "Node016", new[] {
                R("Step 1 First encounter - Player already has fortune", c => Fortunes.Has(c) && Quests.Flag(c, "visited:Node016") == null),
                R("Step 3 Player already has fortune", c => Fortunes.Has(c)),
                R("Step 1 First encounter", c => Quests.Flag(c, "visited:Node016") == null),
                R("Step 2 Repeat", c => true) } },
            { "Node048", new[] {
                R("Step 1 First encounter - Player already has fortune", c => Fortunes.Has(c) && Quests.Flag(c, "visited:Node048") == null),
                R("Step 3 Player already has fortune", c => Fortunes.Has(c)),
                R("Step 1 First encounter", c => Quests.Flag(c, "visited:Node048") == null),
                R("Step 2 Repeat", c => true) } },
            // Gnomes Come Home: Herbert's camps and the desert crossing.
            { "Node057", new[] {
                R("Lost Gnomes - Step 1", c => S(c, Quests.Gnomes) && !D(c, Quests.Gnomes, "Step1")),
                R("Lost Gnomes - Step 2", c => true) } },
            // South camp: Herbert thanks for the last gnomes (Step 4) or waits for the others (Step 3), see Gnomes.
            { "Node053", new[] {
                R("Abandoned Camp - Step 4", c => Gnomes.Carried(c) > 0 && Gnomes.Delivered(c) + Gnomes.Carried(c) >= Gnomes.Total),
                R("Abandoned Camp - Step 3", c => Gnomes.Carried(c) > 0),
                R("Abandoned Camp - Step 2", c => Quests.Flag(c, "visited:Node053") != null),
                R("Abandoned Camp - Step 1", c => true) } },
            // Deep Desert: the Wormoid Queen fights gnomes in tow; once all are home she stays a one-time battle.
            { "Node055", new[] {
                R("Gnomes In Deck (Fight)", c => Gnomes.Carried(c) > 0),
                R("Wormoid Queen Surfaces", c => Gnomes.AllHome(c)),
                R("No Gnomes In Deck", c => true) } },
            // Masked Army at the Vale of Oberon: the way south needs the faction's quest (Usurper / Fort Romor).
            { "Node00R", new[] {
                R("Masked Army - Step 3", c => F(c, "az01_q_usurper") || F(c, "az01_q_fort_romor")),
                R("Masked Army - Step 2", c => S(c, "az01_q_usurper") || S(c, "az01_q_fort_romor")),
                R("Masked Army - Step 1", c => true) } },
            // Dusk Hollow: White Hawk asks "Did you find what you were seeking?" once the Killipede is beaten in its lair
            // (Src.Game dialogues; before 1.2.6 Step 1 every time).
            { "Node074", new[] {
                R("White Hawk - Step 2", c => Quests.Flag(c, "won:Node078") != null),
                R("White Hawk - Step 1", c => true) } },
            // Horwich Sea: the hag's opening lines; "Princess Revert" goes with the "No Frog" rematch, which the mod does
            // not use (open point), so it must not be picked by chance (both names rank the same).
            { "Node006", new[] { R("Node 6 - Hag", c => true) } },
            // AZ2 (before 1.2.6 these nodes showed whichever dialogue came first, see the comments).
            // Milosh at the Road of Acorns: first meeting, then the repeat by fortune (was the first meeting every time).
            { "AZ2/Node012", new[] {
                R("First encounter", c => Quests.Flag(c, "visited:AZ2/Node012") == null),
                R("Repeat encounter (player already has fortune)", c => Fortunes.Has(c)),
                R("Repeat encounter (no fortune)", c => true) } },
            // Bleakwind Highlands: the mad villagers' corpses, gone once the Kraken is beaten (its dungeon done).
            { "AZ2/Node016", new[] {
                R("post-Kraken defeated", c => Quests.Flag(c, "dungeon:" + KrakenDungeon) != null),
                R("Bleakwind Highlands", c => true) } },
            // Scylla and Charybdis: Src.Video (MyHotStream Underworld 111th battle) shows "A horrid hydra lunges up from
            // the waves..." before the battle, not the "Pre-" lines (both names ranked the same before 1.2.6).
            { "AZ2/NodeB04", new[] { R("- Scylla", c => true) } },
            { "AZ2/NodeB10", new[] { R("- Charybdis", c => true) } },
            // Sapphire Gate: where the Sapphire Key came from is unknown (PLACEHOLDER: the gate is open).
            { "Node073", new[] { R("Sapphire Gate - Step 2", c => true) } },
            // Brink Ridge blockade, Bungle in the Jungle (gnomes), Blood Gate.
            { "Node019", new[] {
                R("Blockade - Step 2", c => true) } },
            { "Node044", new[] {
                R("Bungle in the Jungle - Step 2", c => S(c, "q_gnomes_come_home")),
                R("Bungle in the Jungle - Step 1", c => true) } },
            // Blood Gate: open with the Blood Key that Rizzix / Gurtok give with "The Blood Beneath".
            { "Node037", new[] {
                R("Blood Gate - Step 2", c => S(c, "q_blood_beneath")),
                R("Blood Gate - Step 1", c => true) } },
        };

        /// <summary>Conversation id for the node (convs: name, id of the node's conversations for this champion); null if the node has no rules.</summary>
        public static string Pick(ulong champ, string node, List<KeyValuePair<string, string>> convs)
        {
            Rule[] rules;
            if (!Rules.TryGetValue(node, out rules)) return null;
            foreach (Rule r in rules)
            {
                if (!r.When(champ)) continue;
                // A name ending with the rule's text first ("Step 1 First encounter" is not "... - Player already has fortune").
                foreach (KeyValuePair<string, string> c in convs)
                    if (c.Key.TrimEnd().EndsWith(r.Name, StringComparison.OrdinalIgnoreCase)) return c.Value;
                foreach (KeyValuePair<string, string> c in convs)
                    if (c.Key.IndexOf(r.Name, StringComparison.OrdinalIgnoreCase) >= 0) return c.Value;
            }
            return null;
        }

        public static bool HasRules(string node) { return Rules.ContainsKey(node); }
    }
}
