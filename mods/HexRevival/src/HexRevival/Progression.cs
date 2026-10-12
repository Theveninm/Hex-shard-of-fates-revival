using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Client;
using Game.Client.Campaign;
using Game.Shared;
using Game.Shared.Campaign.Messages;
using Game.Shared.Domain;
using Game.Shared.Eval.Data;
using Game.Shared.Mechanics;
using Game.Shared.Profile;
using Game.Shared.Resources;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Rewards and levels: campaign rewards, champion XP, account level.
    ///
    /// Every value carries its source (see Src). The original game decided rewards on its
    /// servers, so most values come from period guides and videos; anything without a source is
    /// a PLACEHOLDER chosen for the mod, to be replaced when better information turns up.
    /// The full list is in docs/PROGRESSION.md.
    /// </summary>
    public static class Progression
    {
        /// <summary>Where a value comes from.</summary>
        public enum Src
        {
            Game,          // the game's own data files
            Guide2016,     // moderator guide, Steam forum, April 2016 (AZ1 rewards)
            HexServer,     // values the hex-server project read on 2017-2018 gameplay videos
            Wiki2019,      // Hex Compendium "Account Leveling" page, April 2019 (Wayback Machine)
            Forum2017,     // official forum "Spending $$$ for HEX" guide, February 2017
            LootTables2016,// community spreadsheet "AZ1 Loot Tables", 2016 (data/az1_pack_loot_tables.csv)
            SteamGuide2016,// "HexTCG Comprehensive New Player Guide", Steam, 2016
            Placeholder,   // no source: chosen for the mod
        }

        // ==================================================================
        // Champion levels (campaign)
        // ==================================================================

        /// <summary>Champion level for a total XP, from the game's XP table (Src.Game).</summary>
        public static int ChampionLevelForXp(int xp)
        {
            int level = 1;
            foreach (XpTable t in Singleton<TemplateManager>.Instance.XpTables.Values)
            {
                if (t == null) continue;
                foreach (XpTableItem i in t.m_XpTable)
                    if (xp >= i.m_LevelXpRequirement && i.m_Level > level) level = i.m_Level;
                break; // the game has a single table
            }
            return level;
        }

        /// <summary>Talent points per champion level gained. PLACEHOLDER (assumed 1).</summary>
        private const int TalentPointsPerLevel = 1;

        // ==================================================================
        // Campaign rewards
        // ==================================================================

        private class Reward
        {
            public int Gold, Xp;
            public string[] Cards = new string[0];     // card or equipment names, 1 copy each
            public int Packs;                           // Adventure Zone 1 (Howling Plains) packs, see Loot
            public string RaceQuest;                    // key of RaceRewards: the item of the champion's race
            public bool ShroomHaus;                     // one of the Haus's 3 cards (see ShroomHauses)
            public bool OneTime;                        // once per champion
            public Src Source;
            public string Note;
        }

        /// <summary>Default for any campaign battle won (every win). Src.HexServer: ~100 gold and 100 XP.</summary>
        private static readonly Reward DefaultBattle = new Reward { Gold = 100, Xp = 100, Source = Src.HexServer };

        /// <summary>
        /// AZ1 rewards by map node, given when the node's battle is won or its conversation completed
        /// (whichever comes first, once per champion). Src.Guide2016 unless noted; race items from
        /// Src.LootTables2016; XP from Src.HexServer where known, else the default 100.
        /// </summary>
        private static readonly Dictionary<string, Reward> AZ1 = new Dictionary<string, Reward>
        {
            // Dunnwood, Wild Cub (the guide's "Feralroot Bear")
            { "Node003", new Reward { Cards = new[] { "Dire Toad" }, Gold = 150, Xp = 100, OneTime = true, Source = Src.Guide2016 } },
            // Sporemist Hollow, Shroomkin Haus: one card out of the Haus's 3 (picked at random: PLACEHOLDER until the choice window exists)
            { "Node004", new Reward { ShroomHaus = true, Gold = 100, OneTime = true, Source = Src.Guide2016, Note = "card picked at random among the Haus's 3 (the game let the player choose)" } },
            // Skittering Ridge, Killipede's Revenge: the flask of "The Blood Beneath" (Src.LootTables2016: Contract Killing;
            // the game gave it at the turn-in to Rizzix / Gurtok, quest steps not modelled: given on the win)
            { "Node078", new Reward { Cards = new[] { "Contract Killing" }, Gold = 100, Xp = 100, OneTime = true, Source = Src.LootTables2016, Note = "given on the win instead of the turn-in" } },
            // South Desert, Gnomes at Home: Herbert found, "Gnomes Come Home" (Src.LootTables2016: Gnome Wayfarer;
            // the game gave it at the turn-in to Vincent, quest steps not modelled: given at the camp)
            { "Node053", new Reward { Cards = new[] { "Gnome Wayfarer" }, OneTime = true, Source = Src.LootTables2016, Note = "given at Herbert's camp instead of the turn-in" } },
            // Other Shroom Hauses (scenes "SHROOM HAUS 2..6 - REWARD"): the Haus's card only (no source for gold)
            { "Node042", new Reward { ShroomHaus = true, OneTime = true, Source = Src.LootTables2016 } },
            { "Node025", new Reward { ShroomHaus = true, OneTime = true, Source = Src.LootTables2016 } },
            { "Node077", new Reward { ShroomHaus = true, OneTime = true, Source = Src.LootTables2016 } },
            { "Node029", new Reward { ShroomHaus = true, OneTime = true, Source = Src.LootTables2016 } },
            { "Node035", new Reward { ShroomHaus = true, OneTime = true, Source = Src.LootTables2016 } },
            // The Sutherland, Cockatwice
            { "Node009", new Reward { Cards = new[] { "Chickatwice" }, Gold = 200, Xp = 100, OneTime = true, Source = Src.Guide2016 } },
            // Sylvan Peninsula, Squashing Pumpkins: matched to the guide's "Wandering Abomination" (order of the route; hex-server also gives a pack here)
            { "Node010", new Reward { Packs = 1, Gold = 200, Xp = 100, OneTime = true, Source = Src.Guide2016, Note = "node inferred from the route" } },
            // Zila River, Gnash Bridges (conversation "Piranhas"): Piranha Swarm (Src.LootTables2016, "Piranhas";
            // hex-server's "Ravenous Piranha" is a non-collectible token of the encounter)
            { "Node013", new Reward { Cards = new[] { "Piranha Swarm" }, Gold = 100, Xp = 100, OneTime = true, Source = Src.LootTables2016 } },
            // Horwich Sea, The Sea Witch Unfettered (conversations "Hag", "Hag - Princess Revert"): Src.LootTables2016 "Hag Sorceress"
            { "Node006", new Reward { Cards = new[] { "Kiss of the Princess" }, Gold = 100, Xp = 100, OneTime = true, Source = Src.LootTables2016, Note = "given on the win; the Enchanted Frog condition is not checked" } },
            // Deep Desert, Hard Wurms (conversations "Wormoid Queen Tunnels/Surfaces"): Src.LootTables2016 "Wormoid Queen"
            { "Node055", new Reward { Cards = new[] { "Wormoid Hydra" }, Gold = 100, Xp = 100, OneTime = true, Source = Src.LootTables2016 } },
            // Razortooth Forest, Savage Lord: 200 gold (guide 2016); hex-server later saw 300 gold + 200 XP
            { "Node014", new Reward { Gold = 200, Xp = 200, OneTime = true, Source = Src.Guide2016, Note = "XP from hex-server" } },
            // Bridge over the Zila - West: "Cross the Zila River" turn-in (quest condition not modelled yet)
            { "Node015", new Reward { Packs = 1, RaceQuest = "zila", Gold = 300, OneTime = true, Source = Src.Guide2016, Note = "race equipment from the loot tables; quest not checked" } },
            // Cave-In (Underworld) / Ambling Mesa (Ardent): "Find the Cave-In / Ambling Mesa" quest item
            { "Node017", new Reward { RaceQuest = "mesa", OneTime = true, Source = Src.LootTables2016, Note = "given at the node; quest not checked" } },
            { "Node034", new Reward { RaceQuest = "mesa", OneTime = true, Source = Src.LootTables2016, Note = "given at the node; quest not checked" } },
            // Shadowgrove, Corrupt Dryad: AZ1 pack + race equipment; 250 XP (hex-server)
            { "Node018", new Reward { Packs = 1, RaceQuest = "dryad", Xp = 250, OneTime = true, Source = Src.Guide2016 } },
            // Bridge over the Zodiac - East: "Cross the Zodiac River" turn-in (guide's "Zila East"), AZ1 pack + race card
            { "Node030", new Reward { Packs = 1, RaceQuest = "zodiac", OneTime = true, Source = Src.Guide2016, Note = "node inferred; quest not checked" } },
        };

        /// <summary>Race order of the RaceRewards rows (as in the loot tables).</summary>
        private static readonly string[] Races = { "Human", "Elf", "Coyotle", "Orc", "Dwarf", "Necrotic", "Shinhare", "Vennen" };

        /// <summary>Race-specific quest rewards, Src.LootTables2016 ("Racial/Guaranteed Drops" sheet).</summary>
        private static readonly Dictionary<string, string[]> RaceRewards = new Dictionary<string, string[]>
        {
            { "mesa",   new[] { "Experienced Masons", "Ashwood Colossus", "Dance of Dusk", "Hunger of the Mountain God", "LZ-37 Targeting System", "Awaken", "Uzume's Handmaiden", "Phenteo's Gift" } },
            { "zila",   new[] { "Slippers of Intellect", "Rotwood Sandals", "Nightsky Handlers", "Darkspire Armor", "Rose Circlet", "Reaper's Helm", "Elite Handwraps", "Nursery Handlers" } },
            { "zodiac", new[] { "Orders from High Command", "Master of Masks", "Prophet of the Sun", "Bloodlust", "Junk Repurposer", "Grim Mandate", "Emperor Ito's Will", "Nazhk Lookout" } },
            { "dryad",  new[] { "Holy Beads", "Sylvan Sounder", "Earthcaller's Robes", "Lightning Medallion", "Winged Alloy Helm", "Gem Walkers", "Thug Topper", "Vile Stompers" } },
        };

        /// <summary>
        /// Crayburn Castle rewards by race: 4 cards each, Src.LootTables2016 ("Crayburn Packs"; the sheet
        /// also names each race's PvP champion). Not given yet: the castle dungeon is not implemented.
        /// </summary>
        private static readonly string[][] CrayburnCards =
        {
            new[] { "Noble Citizenry", "Phoenix Guard Trainer", "Captain of the Dragon Guard", "Shards of Fate" },
            new[] { "Rotroot Enchanter", "Emberleaf Wardancer", "Merry Minstrels", "Rootforged Regalia" },
            new[] { "Nightsky Stargazer", "Brightmoon Brave", "Tyrannosaurus Hex", "Shards of Fate" },
            new[] { "Darkspire Priestess", "Mazat Spearman", "Wrathseeker", "Assault Bot" },
            new[] { "Axe Bot", "Researcher Adept", "War Bot Bunker", "Construction Plans: War Hulk" },
            new[] { "Call the Grave", "Spiritbound Spy", "Deepgaze Acolyte", "Shards of Fate" },
            new[] { "Concubunny", "Tyrannosaurus Hex", "Shroomtank", "Ebony Pawn" },
            new[] { "Hatchery Priest", "Incubation Webs", "Vicious Vivisector", "Shield Bot" },
        };

        /// <summary>Army of Myth won at difficulty 6 (the army starts with 6 resources, choice "Diff_6"): Src.LootTables2016 "Army of Myth 6".</summary>
        private const string ArmyOfMyth6Card = "Spectral Caller";
        private static readonly string[] UnmappedQuestCards = { ArmyOfMyth6Card };

        /// <summary>Shroom Haus card choices by Haus, Src.LootTables2016 ("Shroom Hauses" sheet). The Haus is recognised from the node's scene names.</summary>
        private static readonly Dictionary<string, string[]> ShroomHauses = new Dictionary<string, string[]>
        {
            { "Artifact", new[] { "Builder Bot", "Deployment Orders", "Tricerobot" } },
            { "Blood",    new[] { "Elegy of Death", "Raving Ghoul", "Rotting Away" } },
            { "Diamond",  new[] { "Form Ranks", "Ethereal Healer", "Dandelion Faerie" } },
            { "Sapphire", new[] { "Morphology", "Chaotic Murmurs", "Heroic Echo" } },
            { "Ruby",     new[] { "Skittering Skarn", "Fuel for the Fire", "Emberbrute" } },
            { "Wild",     new[] { "Feralroot Jaguar", "Walking Hive", "Thornsprout" } },
        };

        /// <summary>Every fixed reward name (for the "loot check" dev command).</summary>
        public static IEnumerable<string> FixedRewardNames()
        {
            foreach (Reward r in AZ1.Values) foreach (string c in r.Cards) yield return c;
            foreach (string[] l in RaceRewards.Values) foreach (string c in l) yield return c;
            foreach (string[] l in CrayburnCards) foreach (string c in l) yield return c;
            foreach (string c in UnmappedQuestCards) yield return c;
            foreach (string[] l in ShroomHauses.Values) foreach (string c in l) yield return c;
        }

        private static string RaceReward(ulong champId, string quest)
        {
            champion_bits c = Champions.Get(champId);
            string[] l;
            if (c == null || !RaceRewards.TryGetValue(quest, out l)) return null;
            string race = c.Race.ToString().Replace("'", "").Replace("_", "");
            for (int i = 0; i < Races.Length; i++) if (string.Equals(Races[i], race, StringComparison.OrdinalIgnoreCase)) return l[i];
            Debug.LogWarning("[HexRevival] [Progression] no race reward for " + c.Race);
            return null;
        }

        /// <summary>
        /// Haus kind of each Shroom Haus node. The game numbers its Hauses without naming the kind
        /// (Src.Game), so the kinds are inferred (PLACEHOLDER, see docs/PROGRESSION.md):
        /// Sporemist Hollow = Artifact (Guide2016, hex-server); Blau Flats = Sapphire ("blau" = blue);
        /// Uuug's Oasis = Ruby (desert, only Ruby decks nearby); Lena Grotto = Blood (Blood decks
        /// nearby, dark grotto); Bauer Dunes = Diamond (Diamond decks nearby); Hoff Mines = Wild (the one left).
        /// </summary>
        private static readonly Dictionary<string, string> HausByNode = new Dictionary<string, string>
        {
            { "Node004", "Artifact" }, { "Node042", "Diamond" }, { "Node025", "Blood" },
            { "Node077", "Sapphire" }, { "Node029", "Wild" }, { "Node035", "Ruby" },
        };

        private static string ShroomHausCard(string node)
        {
            string kind;
            if (!HausByNode.TryGetValue(node, out kind)) { kind = "Artifact"; Debug.Log("[HexRevival] [Progression] unknown Shroom Haus " + node + ": Artifact Haus"); }
            string[] cards = ShroomHauses[kind];
            return cards[UnityEngine.Random.Range(0, cards.Length)];
        }

        /// <summary>Campaign battle won on a map node: rewards, champion XP, account XP.</summary>
        public static void OnCampaignBattleWon(ulong champId, string node)
        {
            Reward r;
            Reward special = AZ1.TryGetValue(node, out r) ? r : null;
            bool claimed = special != null && special.OneTime && Saves.Current.Claims.Contains(champId + ":" + node);
            Reward give = special != null && !claimed ? special : DefaultBattle;
            if (special != null && special.OneTime && !claimed) Saves.Current.Claims.Add(champId + ":" + node);
            // Taming quest ("AZ01 Tamed", Src.Game + Src.HexServer): an opponent troop tamed with the
            // taming item (IntAttr Tamed) and sent to the void is captured: 1 copy, once per champion and creature.
            List<string> extra = new List<string>();
            if (node == "Node066" && Campaign.BattleDifficulty == 6 && !Saves.Current.Claims.Contains(champId + ":aom6"))
            {
                Saves.Current.Claims.Add(champId + ":aom6");
                extra.Add(ArmyOfMyth6Card);
            }
            foreach (CardTemplate t in TamedCreatures())
            {
                string key = champId + ":tamed:" + t.m_Name;
                if (Saves.Current.Claims.Contains(key) || Array.Exists(give.Cards, x => string.Equals(x, t.m_Name, StringComparison.OrdinalIgnoreCase))) continue;
                Saves.Current.Claims.Add(key);
                extra.Add(t.m_Name);
            }
            if (extra.Count > 0)
            {
                Reward withTamed = new Reward { Gold = give.Gold, Xp = give.Xp, Packs = give.Packs, RaceQuest = give.RaceQuest, ShroomHaus = give.ShroomHaus, Source = give.Source };
                List<string> all = new List<string>(give.Cards);
                all.AddRange(extra);
                withTamed.Cards = all.ToArray();
                give = withTamed;
            }
            Grant(champId, give, node, "campaign " + node);
        }

        /// <summary>Opponent troops of the match that ended, in the void with the Tamed marker.</summary>
        private static List<CardTemplate> TamedCreatures()
        {
            List<CardTemplate> res = new List<CardTemplate>();
            try
            {
                object session = Server.CurrentSession;
                if (session == null) return res;
                FieldInfo f = null;
                for (Type t = session.GetType(); t != null && f == null; t = t.BaseType)
                    f = t.GetField("m_Players", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                System.Collections.IEnumerable players = f != null ? f.GetValue(session) as System.Collections.IEnumerable : null;
                if (players == null) return res;
                foreach (object o in players)
                {
                    Game.Shared.Player p = o as Game.Shared.Player;
                    if (p == null || !p.IsAI() || p.m_Void == null) continue;
                    foreach (Card c in p.m_Void.m_Cards)
                    {
                        if (c == null || !c.IsTroop()) continue;
                        bool tamed = c.GetBool(IntAttrs.Tamed) || c.GetCardContext().GetBool(IntAttrs.Tamed) || c.GetOrCreate(TACAttrs.PermanentData).GetBool(IntAttrs.Tamed);
                        if (!tamed) continue;
                        CardTemplate t = c.m_OriginalCardTemplateId.IsValid ? Singleton<TemplateManager>.Instance.GetCardTemplate(c.m_OriginalCardTemplateId) : null;
                        if (t == null) t = c.m_CardTemplate;
                        if (t != null && !res.Exists(x => x.m_Name == t.m_Name)) res.Add(t);
                    }
                }
                if (res.Count > 0) Debug.Log("[HexRevival] [Progression] tamed: " + string.Join(", ", res.ConvertAll(x => x.m_Name).ToArray()));
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Progression] tamed creatures: " + e.Message); }
            return res;
        }

        /// <summary>Campaign conversation completed: quest turn-in rewards (Shroomkin Haus, bridges, race quests).</summary>
        public static void OnCampaignConversationDone(ulong champId, string node)
        {
            Reward r;
            if (!AZ1.TryGetValue(node, out r) || !r.OneTime || Campaign.NodeHasBattle(node)) return;
            string key = champId + ":" + node;
            if (Saves.Current.Claims.Contains(key)) return;
            Saves.Current.Claims.Add(key);
            Grant(champId, r, node, "campaign " + node);
        }

        /// <summary>
        /// Applies a reward and hands it to the client like the campaign server did
        /// (ClientCampaignManager.CacheRewards): the map then shows the game's loot window.
        /// </summary>
        private static void Grant(ulong champId, Reward r, string node, string what)
        {
            List<card_instance_bits> cards = new List<card_instance_bits>();
            List<inventory_bits> items = new List<inventory_bits>();
            List<EscrowAction> shown = new List<EscrowAction>();
            List<string> names = new List<string>(r.Cards);
            if (r.RaceQuest != null) { string n = RaceReward(champId, r.RaceQuest); if (n != null) names.Add(n); }
            if (r.ShroomHaus) names.Add(ShroomHausCard(node));
            foreach (string name in names) AddDrop(Loot.Resolve(name, true), cards, items, shown);
            for (int i = 0; i < r.Packs; i++) foreach (Loot.Drop d in Loot.Open(Loot.AZ1Pack)) AddDrop(d, cards, items, shown);
            if (r.Gold > 0)
            {
                Saves.Current.Gold += r.Gold;
                shown.Add(Action("GOLD", r.Gold, null));
            }
            int levels;
            champion_bits champ = Champions.AddXp(champId, r.Xp, out levels);
            if (r.Xp > 0) shown.Add(Action("XP", r.Xp, null));
            if (levels > 0) shown.Add(Action("LEVEL", levels * TalentPointsPerLevel, null));
            Saves.SaveCurrent();

            AppliedUpdates up = new AppliedUpdates();
            up.Completed = shown;
            up.Cards = new List<EscrowResultCard>();
            foreach (card_instance_bits c in cards) up.Cards.Add(new EscrowResultCard(c));
            up.Items = new List<EscrowResultItem>();
            foreach (inventory_bits it in items) up.Items.Add(new EscrowResultItem(it));
            up.Accounts = new List<EscrowResultAccount> { new EscrowResultAccount(AccountBits()) };
            if (champ != null) up.Champions = new List<EscrowResultChampion> { new EscrowResultChampion(champ) };
            try
            {
                MethodInfo m = typeof(ClientCampaignManager).GetMethod("CacheRewards", BindingFlags.Static | BindingFlags.NonPublic);
                m.Invoke(null, new object[] { champId, up });
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Progression] CacheRewards: " + e); }
            Debug.Log("[HexRevival] [Progression] " + what + ": " + r.Gold + " gold, " + r.Xp + " XP, " + cards.Count + " card(s), " + items.Count + " item(s)" + (levels > 0 ? ", level up" : ""));
            AddAccountXp(PveWinXp);
        }

        /// <summary>Card or equipment: added to the save, listed in the loot window (silently: CacheRewards gives it to the client).</summary>
        private static void AddDrop(Loot.Drop d, List<card_instance_bits> cards, List<inventory_bits> items, List<EscrowAction> shown)
        {
            if (d == null) return;
            if (d.Card != null) { AddCard(d.Card, cards, shown); return; }
            inventory_bits it = Inventory.GrantSilently(d.Item);
            if (it != null) items.Add(it);
            shown.Add(Action("ITEM", 1, d.Item.ToString()));
        }

        /// <summary>Same, given to the client right away (account level rewards, outside the campaign map).</summary>
        private static void GiveDrop(Loot.Drop d)
        {
            if (d == null) return;
            if (d.Card != null) { Collection.Grant(d.Card.m_Id, 1); return; }
            inventory_bits it = Inventory.GrantSilently(d.Item);
            if (it == null) return;
            try { GameClient.Instance.PlayerProfile.AddInventoryItem(it); }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Progression] AddInventoryItem: " + e.Message); }
        }

        private static void AddCard(CardTemplate t, List<card_instance_bits> cards, List<EscrowAction> shown)
        {
            if (t == null) return;
            cards.AddRange(Collection.GrantSilently(t.m_Id, 1));
            shown.Add(Action("CARD", 1, t.m_Id.ToString()));
        }

        private static EscrowAction Action(string kind, int quantity, string template)
        {
            return new EscrowAction { ItemKind = kind, ItemQuantity = quantity, ItemAction = "GRANT", ItemTemplate = template ?? "", RCode = "", Created = DateTime.Now };
        }

        private static reckoning_bits AccountBits()
        {
            reckoning_bits b = new reckoning_bits();
            b.Gold = (int)Saves.Current.Gold;
            b.Platinum = (int)Saves.Current.Platinum;
            return b;
        }

        // ==================================================================
        // Packs and random cards
        // ==================================================================

        /// <summary>
        /// Booster content. 17 cards per pack (Src.Forum2017: 3 packs = 51 cards). Rarity split
        /// PLACEHOLDER (assumed): 1 top card (legendary 1 time in 8; otherwise epic 1 time in 5 in
        /// the sets that have epics, from Frostheart; otherwise rare), 3 uncommons, 13 commons;
        /// basic shards are not included. The campaign sets (AZ1, AZ2) have no commons (Src.Game):
        /// their common slots take uncommons (PLACEHOLDER, the real PvE pack content is unknown).
        /// </summary>
        public static List<CardTemplate> OpenPack(CardSetTemplate set)
        {
            List<CardTemplate> res = new List<CardTemplate>();
            if (set == null) return res;
            float v = UnityEngine.Random.value;
            CardTemplate top = v < 0.125f ? RandomCard(set, ERarity.Legendary, null)
                : v < 0.125f + 0.875f * 0.2f ? RandomCard(set, ERarity.Epic, null) : null;
            res.Add(top ?? RandomCard(set, ERarity.Rare, null));
            for (int i = 0; i < 3; i++) res.Add(RandomCard(set, ERarity.Uncommon, null));
            for (int i = 0; i < 13; i++) res.Add(RandomCard(set, ERarity.Common, null) ?? RandomCard(set, ERarity.Uncommon, null));
            res.RemoveAll(c => c == null);
            return res;
        }

        private static readonly Dictionary<string, List<CardTemplate>> s_Pools = new Dictionary<string, List<CardTemplate>>();

        public static CardTemplate RandomCard(CardSetTemplate set, ERarity rarity, ECardTypes? type)
        {
            if (set == null) return null;
            string key = set.m_Id + "|" + rarity + "|" + type;
            List<CardTemplate> pool;
            if (!s_Pools.TryGetValue(key, out pool))
            {
                pool = new List<CardTemplate>();
                foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
                {
                    if (t == null || t.m_SetId != set.m_Id || t.m_CardRarity != rarity) continue;
                    if (type.HasValue && (t.m_CardType & type.Value) == 0) continue;
                    bool ownable;
                    try { ownable = t.IsOwnable() && !t.IsBasicResource(); } catch { ownable = false; }
                    if (ownable) pool.Add(t);
                }
                s_Pools[key] = pool;
            }
            return pool.Count > 0 ? pool[UnityEngine.Random.Range(0, pool.Count)] : null;
        }

        /// <summary>Card sets of the campaign packs: "AZ1" holds the Howling Plains cards, "AZ2" the Alachian Sea ones (Src.Game).</summary>
        private const string AZ1Set = "AZ1", AZ2Set = "AZ2";

        public static CardSetTemplate FindSet(string name)
        {
            foreach (CardSetTemplate s in Singleton<TemplateManager>.Instance.CardSets.Values)
                if (s != null && string.Equals(s.m_Name, name, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        /// <summary>Name shown for a pack of this set.</summary>
        private static string PackName(CardSetTemplate set)
        {
            if (set == null) return "?";
            return set.m_Name == AZ1Set ? "Howling Plains" : set.m_Name == AZ2Set ? "Alachian Sea" : set.m_Name;
        }

        /// <summary>
        /// "Current set" of account rewards: the most recent PvP set (highest set number among
        /// the Immortal format sets). Src.Wiki2019 says "current set"; picking the last one is an assumption.
        /// </summary>
        public static CardSetTemplate CurrentSet()
        {
            CardSetTemplate best = null;
            foreach (ResourceId id in BuiltInResources.Format_Immortal)
            {
                CardSetTemplate s;
                if (Singleton<TemplateManager>.Instance.CardSets.TryGetValue(id, out s) && s != null && (best == null || s.m_SetNo > best.m_SetNo)) best = s;
            }
            return best;
        }

        public static CardTemplate FindCard(string name)
        {
            foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
                if (t != null && string.Equals(t.m_Name, name, StringComparison.OrdinalIgnoreCase)) return t;
            Debug.LogWarning("[HexRevival] [Progression] card not found: " + name);
            return null;
        }

        // ==================================================================
        // Account level (Src.Wiki2019 unless noted)
        // ==================================================================

        /// <summary>Account XP for a PvE win (campaign, Quick Match against the AI). Src.Wiki2019: PvE 50, AI Dueling Pit 50.</summary>
        public const int PveWinXp = 50;
        /// <summary>The first 1200 XP of each daily period count double. Src.Wiki2019.</summary>
        private const int BonusXp = 1200;
        /// <summary>Daily XP cap: 4000, 8000 from level 20. Src.Wiki2019.</summary>
        private static int DailyCap { get { return Saves.Current.AccountLevel >= 20 ? 8000 : 4000; } }
        /// <summary>The daily period resets 20 hours after its first win. Src.Wiki2019.</summary>
        private const int PeriodHours = 20;
        private const int MaxAccountLevel = 100;

        /// <summary>XP needed to go from level n-1 to level n (index = n). Src.Wiki2019; 7000 from level 41.</summary>
        private static readonly int[] LevelXp = { 0, 0, 1000, 1000, 1000, 1200, 1250, 1300, 1350, 1400, 1450, 1500, 1700, 1900, 2100, 2300, 2500, 2800,
            3100, 3400, 3700, 4000, 4200, 4400, 4600, 4800, 5000, 5200, 5400, 5600, 5800, 6000, 6200, 6200, 6300, 6400, 6500, 6600, 6700, 6800, 6900 };

        private static int XpForLevel(int level) { return level < LevelXp.Length ? LevelXp[level] : 7000; }

        private static int TotalXpForLevel(int level)
        {
            int total = 0;
            for (int l = 2; l <= level; l++) total += XpForLevel(l);
            return total;
        }

        /// <summary>
        /// Rewards of levels 2 to 40 (Src.Wiki2019). From 41 the game repeated a 10-level cycle
        /// (see CycleRewards). Codes: gold:N, pack:hp|as|cur (Howling Plains, Alachian Sea, current
        /// set), commons:N (PLACEHOLDER: random commons of the current set for the wiki's "fixed
        /// commons"), cards:N (random cards of the current set), stardust, chests, rarechest,
        /// ticket, sleeve, merc (see GrantLevelReward for what each becomes in the mod).
        /// </summary>
        private static readonly string[][] LevelRewards =
        {
            null, null,
            new[] { "pack:hp" }, new[] { "commons:6" }, new[] { "pack:as" }, new[] { "commons:6" }, new[] { "stardust" },        // 2-6
            new[] { "sleeve" }, new[] { "gold:2000" }, new[] { "merc" }, new[] { "ticket" }, new[] { "chests" },                 // 7-11
            new[] { "pack:hp", "pack:as" }, new[] { "stardust" }, new[] { "rarechest" }, new[] { "pack:cur" }, new[] { "chests" }, // 12-16
            new[] { "sleeve" }, new[] { "gold:3000" }, new[] { "cards:3" }, new[] { "ticket" }, new[] { "chests" },              // 17-21
            new[] { "stardust" }, new[] { "gold:4000" }, new[] { "rarechest" }, new[] { "pack:cur" }, new[] { "chests" },        // 22-26 (+ common alternate arts)
            new[] { "sleeve" }, new[] { "gold:4000" }, new[] { "cards:3" }, new[] { "ticket" }, new[] { "chests" },              // 27-31
            new[] { "stardust" }, new[] { "gold:4000" }, new[] { "rarechest" }, new[] { "pack:cur" }, new[] { "chests" },        // 32-36
            new[] { "sleeve" }, new[] { "gold:4000" }, new[] { "cards:3" }, new[] { "ticket" },                                  // 37-40
        };

        /// <summary>Levels 41-100, by last digit (1..9, 0). Src.Wiki2019 (alternate arts left out).</summary>
        private static readonly string[][] CycleRewards =
        {
            new[] { "ticket" }, new[] { "chests" }, new[] { "stardust" }, new[] { "gold:4000" }, new[] { "rarechest" },
            new[] { "pack:cur" }, new[] { "chests" }, new[] { "sleeve" }, new[] { "gold:4000" }, new[] { "cards:3" },
        };

        private static string[] RewardsForLevel(int level)
        {
            return level < LevelRewards.Length ? LevelRewards[level] : CycleRewards[level % 10];
        }

        /// <summary>Adds account XP after a win (daily bonus and cap), grants level rewards.</summary>
        public static void AddAccountXp(int xp)
        {
            Saves.Info s = Saves.Current;
            if (s == null) return;
            if (s.PeriodStart == DateTime.MinValue || DateTime.Now - s.PeriodStart > TimeSpan.FromHours(PeriodHours))
            {
                s.PeriodStart = DateTime.Now;
                s.XpInPeriod = 0;
            }
            int bonus = Math.Max(0, Math.Min(xp, BonusXp - s.XpInPeriod));   // doubled part
            int gain = Math.Min(xp + bonus, Math.Max(0, DailyCap - s.XpInPeriod));
            if (gain <= 0) { Notices.Show(Loc.Get("xp_capped")); return; }
            s.XpInPeriod += gain;
            s.AccountXp += gain;
            List<string> got = new List<string>();
            while (s.AccountLevel < MaxAccountLevel && s.AccountXp >= TotalXpForLevel(s.AccountLevel + 1))
            {
                s.AccountLevel++;
                foreach (string code in RewardsForLevel(s.AccountLevel)) got.Add(GrantLevelReward(code));
            }
            Saves.SaveCurrent();
            RefreshAccountUi();
            Notices.Show(Loc.Get("account_xp", gain));
            if (got.Count > 0) Notices.Show(Loc.Get("account_level", s.AccountLevel) + "  " + string.Join(", ", got.FindAll(x => x != null).ToArray()));
        }

        /// <summary>Gives one level reward; returns its description (null if nothing was given).</summary>
        private static string GrantLevelReward(string code)
        {
            string[] p = code.Split(':');
            int n = 0;
            if (p.Length > 1) int.TryParse(p[1], out n);
            switch (p[0])
            {
                case "gold": return GiveGold(n);
                case "pack":
                {
                    CardSetTemplate set = p[1] == "hp" ? FindSet(AZ1Set) : p[1] == "as" ? FindSet(AZ2Set) : CurrentSet();
                    // Howling Plains = the Adventure Zone 1 pack of the loot tables (Src.LootTables2016)
                    if (p[1] == "hp") foreach (Loot.Drop d in Loot.Open(Loot.AZ1Pack)) GiveDrop(d);
                    else foreach (CardTemplate t in OpenPack(set)) Collection.Grant(t.m_Id, 1);
                    return Loc.Get("reward_pack", PackName(set));
                }
                case "commons": GiveRandom(CurrentSet(), ERarity.Common, n); return Loc.Get("reward_cards", n);
                case "cards": GiveRandom(CurrentSet(), (ERarity)0, n); return Loc.Get("reward_cards", n);
                // PLACEHOLDERS: stardust and chest spins need systems the mod does not have yet.
                case "stardust": return GiveGold(300);
                case "chests": return GiveGold(500);
                case "rarechest": return GiveGold(1000);
                // PLACEHOLDER: tournament entry tickets have no offline use: a current set booster instead.
                case "ticket": return GrantLevelReward("pack:cur");
                // Not granted: cosmetic sleeves and the Sin'glar mercenary (mercenaries not implemented).
                default: return null;
            }
        }

        private static string GiveGold(int n)
        {
            Saves.Current.Gold += n;
            try { GameClient.Instance.PlayerProfile.UpdateBalance((int)Saves.Current.Gold, (int)Saves.Current.Platinum); } catch { }
            return Loc.Get("reward_gold", n);
        }

        /// <summary>Random cards of a set (rarity 0 = any of common/uncommon/rare, weighted 10/3/1: PLACEHOLDER).</summary>
        private static void GiveRandom(CardSetTemplate set, ERarity rarity, int n)
        {
            for (int i = 0; i < n; i++)
            {
                ERarity r = rarity;
                if (r == 0) { float v = UnityEngine.Random.value * 14f; r = v < 10f ? ERarity.Common : v < 13f ? ERarity.Uncommon : ERarity.Rare; }
                CardTemplate t = RandomCard(set, r, null);
                if (t != null) Collection.Grant(t.m_Id, 1);
            }
        }

        /// <summary>Account level, XP and daily cap for the game's XP bar (bottom left of the main menu).</summary>
        public static void RefreshAccountUi()
        {
            Saves.Info s = Saves.Current;
            PlayerProfile profile = GameClient.Instance != null ? GameClient.Instance.PlayerProfile : null;
            if (s == null || profile == null) return;
            try
            {
                Game.Shared.Profile.Network.AccountXpCap cap = new Game.Shared.Profile.Network.AccountXpCap();
                cap.BonusMod = 2f;
                cap.BonusCap = BonusXp;
                cap.XpCap = DailyCap;
                cap.TimerDurHours = PeriodHours;
                cap.XpForCurrentLevel = TotalXpForLevel(s.AccountLevel);
                cap.XpForNextLevel = TotalXpForLevel(Math.Min(MaxAccountLevel, s.AccountLevel + 1));
                cap.HasUnClaimed = false; // rewards are given automatically
                SetProperty(profile, "CapInfo", cap);
                SetProperty(profile, "AccountLevel", s.AccountLevel);
                SetProperty(profile, "Experience", s.AccountXp);
                SetProperty(profile, "XPGainedInPeriod", s.XpInPeriod);
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Progression] account bar: " + e.Message); }
        }

        private static void SetProperty(object target, string name, object value)
        {
            PropertyInfo p = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null) p.SetValue(target, value, null);
        }

        // ==================================================================
        // Match end (AuthoritativeSessionBase.EndGame postfix)
        // ==================================================================

        /// <summary>
        /// Any match won against the AI outside the campaign (Quick Match, practice): account XP
        /// only, as the original game's AI matches gave no gold or cards (no source says otherwise).
        /// </summary>
        public static void OnOtherBattleWon()
        {
            AddAccountXp(PveWinXp);
        }

        private static object s_EndedSession;

        /// <summary>
        /// A match ended. EndGame can run several times for one match: only the first call counts.
        /// </summary>
        public static void OnMatchEnded(List<UID> winners)
        {
            object session = Server.CurrentSession;
            if (session != null && ReferenceEquals(session, s_EndedSession)) return;
            s_EndedSession = session;
            if (Saves.Current == null) return;
            bool won = false;
            if (winners != null)
                foreach (UID u in winners)
                    if (u.GetUIDType() != UID.Type.AIPlayer) won = true;
            if (!Campaign.OnGameEnded(won) && won) OnOtherBattleWon();
        }
    }
}
