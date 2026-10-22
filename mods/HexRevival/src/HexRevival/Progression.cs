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
            Video,         // reward windows read on gameplay videos (2016-2018), see docs/PROGRESSION.md
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
            public int Gold, Xp;                        // given on every win (Src.Video: repeated fights paid again)
            public string[] Cards = new string[0];     // card, equipment or stardust names, 1 copy each (first win only)
            public int Packs;                           // packs, see Loot (first win only)
            public string PackName;                     // pack table (Loot): null = Adventure Zone 1 (Howling Plains)
            public int RandomEquipment;                 // random equipment pieces (first win only)
            public string RaceQuest;                    // key of RaceRewards: the item of the champion's race
            public bool ShroomHaus;                     // one of the Haus's 3 cards (see ShroomHauses)
            public string NeedsEvent;                   // the items need this end-of-battle event (Src.Game: HandleGameplayChallenges)
            public int ShipTalentPoints;                // AZ2 ship talent points (first win only, Src.Video "N Ship Talent Point")
            public int BonusGold;                       // second gold line (dungeons, see OnCampaignBattleWon)
            public Src Source;
            public string Note;

            public bool HasItems { get { return Cards.Length > 0 || Packs > 0 || RandomEquipment > 0 || RaceQuest != null || ShroomHaus || ShipTalentPoints > 0; } }
            public Reward GoldXpOnly() { return new Reward { Gold = Gold, Xp = Xp, Source = Source, PackName = PackName }; }
            public Reward Copy() { return (Reward)MemberwiseClone(); }
        }

        /// <summary>Default for a campaign battle with no known reward. Src.HexServer: ~100 gold and 100 XP.</summary>
        private static readonly Reward DefaultBattle = new Reward { Gold = 100, Xp = 100, Source = Src.HexServer };

        private static Reward R(int gold, int xp, params string[] cards) { return new Reward { Gold = gold, Xp = xp, Cards = cards, Source = Src.Video }; }

        /// <summary>
        /// AZ1 battle rewards by map node (Src.Video: the battle's reward window, same values for the
        /// three champions seen, Dec 2016-2017 client; docs/PROGRESSION.md lists each source video).
        /// Gold and XP are given on every win, the items on the first win only. Rewards given by a
        /// quest giver after the fight are quest rewards (QuestRewards), not battle rewards.
        /// Tamed creatures (Dire Toad, Chickatwice, Bubblebee...) are only given when tamed (see TamedCreatures).
        /// </summary>
        private static readonly Dictionary<string, Reward> AZ1 = new Dictionary<string, Reward>
        {
            { "Node003", R(150, 100) },                                       // Dunnwood, Wild Cub (Feralroot Bear)
            // Sporemist Hollow and the other Shroomkin Hauses: the card picked only, no gold (Src.Video, 3 champions)
            { "Node004", new Reward { ShroomHaus = true, Source = Src.Video } },
            { "Node042", new Reward { ShroomHaus = true, Source = Src.Video } },
            { "Node025", new Reward { ShroomHaus = true, Source = Src.Video } },
            { "Node077", new Reward { ShroomHaus = true, Source = Src.Video } },
            { "Node029", new Reward { ShroomHaus = true, Source = Src.Video } },
            { "Node035", new Reward { ShroomHaus = true, Source = Src.Video } },
            { "Node005", new Reward { Xp = 200, Packs = 1, Source = Src.Video } },      // Fonferek Thicket, Mind-Controlled Manti
            // Horwich Sea, The Sea Witch Unfettered: Kiss of the Princess needs Princess Daphne saved (game event savedprincess)
            { "Node006", new Reward { Gold = 500, Xp = 250, Packs = 1, Cards = new[] { "Kiss of the Princess" }, NeedsEvent = "savedprincess", Source = Src.Video, Note = "card: Src.LootTables2016" } },
            { "Node009", R(200, 100, "Effigy of Nulzann") },                  // The Sutherland, Cockatwice
            { "Node010", new Reward { Gold = 200, Xp = 100, Packs = 1, Source = Src.Video } },   // Sylvan Peninsula, Wandering Abomination
            { "Node013", R(500, 600, "Piranha Swarm") },                     // Zila River, Gnash Bridges (Swarm of Piranhas)
            { "Node014", R(300, 200) },                                       // Razortooth Forest, Savage Lord
            { "Node018", new Reward { Xp = 250, Packs = 1, RaceQuest = "dryad", Source = Src.Video } },   // Shadowgrove, Corrupt Dryad
            { "Node019", R(0, 200) },                                         // Brink Ridge, Human Blockade
            { "Node019B", R(250, 250) },                                      // Brink Ridge, Barricade Zombie (after the Smoldering Dead)
            { "Node021", R(300, 200, "Conflagration Staff") },               // Clark Foothills, Bloated Zombie
            // Lake Wyalusing, Guru of the Wounded Petal
            { "Node022", new Reward { Gold = 400, Xp = 250, Packs = 1, Cards = new[] { "Uzume, Grand Concubunny Sleeve" }, Source = Src.Video } },
            { "Node023", new Reward { Gold = 250, Xp = 250, Packs = 1, Source = Src.Video } },   // Zombie Mob
            { "Node026", R(450, 250) },                                       // Furious Taskmaster / Brood Creeper
            { "Node028", new Reward { Gold = 500, Xp = 500, Packs = 1, Source = Src.Video } },   // Garden of Ophelia, Overgrown Garden
            { "Node031", R(400, 200) },                                       // Zodiac River, Water Elemental
            { "Node033", R(400, 200) },                                       // Indigo River, Water Elemental
            { "Node032", new Reward { Xp = 200, Packs = 1, Source = Src.Video } },               // Zodiac River - North, Fae Tricksters
            { "Node039", R(0, 150) },                                         // Moonrise Canyon, Ambling Bluff
            { "Node041", R(500, 250, "Common Stardust", "Uncommon Stardust") },   // Sunsoul Basin, Heart of the Phoenix
            { "Node045", R(400, 200) },                                       // The Burk Heart, Necrophage Sensei / Ashwood Maestro
            { "Node047", new Reward { Xp = 200, RandomEquipment = 2, Source = Src.Video } },     // Indigo Plains, Little Sky
            { "Node050", R(500, 200) },                                       // Crescent Mesa, Pale Moon
            { "Node052", R(0, 150) },                                         // Badlands, Old Dirty Bustard (Sandstone Rumbler)
            { "Node055", R(0, 180, "Wormoid Hydra") },                        // Deep Desert, Hard Wurms (Wormoid Queen)
            { "Node058", R(0, 60) }, { "Node059", R(0, 60) }, { "Node060", R(0, 60) },   // gnome escort wurms
            { "Node062", R(0, 90) }, { "Node063", R(0, 90) },                 // adult wurms (Wormoid, Wormoid Hydra)
            { "Node064", R(350, 250) },                                       // The Scar, Dusty Robots (Excavation Bot)
            { "Node065", R(0, 200, "Rare Stardust") },                        // Sky'le Tepui, Red Thunder
            { "Node066", R(150, 200) },                                       // Forsaken Plateau, Army of Myth
            { "Node067", new Reward { Xp = 250, Packs = 1, Source = Src.Video } },               // Devonshire Hills, Bone Goliath
            { "Node071", new Reward { Gold = 250, Xp = 250, RandomEquipment = 3, Source = Src.Video } },   // Valley of Sighs, The Killipede
            { "Node075", R(450, 250) },                                       // Eagle Mesa, Elimination Specialist / Earthcaller
            { "Node078", new Reward { Gold = 600, Xp = 350, Packs = 2, Source = Src.Video } },   // Skittering Ridge, Killipede's Revenge

            // ---- Dungeon battles (key "<dungeon>/<node>[/<fight>]", see Dungeons): Src.Video (MyHotStream
            // Underworld and Ardent runs, Vandersveldt), values of the Dec 2016+ client; PLACEHOLDER where noted.
            // Castle Crayburn (the castle's own reward is the quest's, see OnCrayburnDone)
            { "crayburn/CastleGate", R(50, 100) },
            { "crayburn/TowerGate", R(50, 100) },
            { "crayburn/PenworthTower", R(0, 0) },
            // The Tranquil Dream: by the fight's rank, not the creature (1st fight not read: PLACEHOLDER 125 / 125)
            { "tranquildream/rank1", new Reward { Gold = 125, Xp = 125, Source = Src.Placeholder } },
            { "tranquildream/rank2", R(125, 150) },
            { "tranquildream/rank3", R(250, 175) },
            { "tranquildream/rank4", R(250, 200) },
            { "tranquildream/rank5", R(300, 250) },
            { "tranquildream/midnight", new Reward { Gold = 450, Xp = 350, Packs = 1, PackName = "Tranquil Dream", Source = Src.Video } },
            // Fort Romor (Underworld)
            // (the videos' rewards are per foe: Hawthorne 150/100, Legionnaire 200/150, Rhonwen 250/200; each
            // room's foe is its conversation's speaker, see Dungeons)
            { "fortromor/CmdPost", R(150, 100) },
            { "fortromor/Stables", R(200, 150) },
            { "fortromor/Barracks", R(250, 200) },
            { "fortromor/Defense/1", R(300, 225) },
            { "fortromor/Defense/2", R(400, 250) },
            { "fortromor/Defense/3", new Reward { Gold = 500, Xp = 450, Packs = 1, PackName = "Fort Romor", Source = Src.Video } },
            // The Usurper (Ardent)
            { "usurper/NodeA", R(150, 100) },
            { "usurper/NodeB", R(200, 150) },
            { "usurper/NodeC", R(250, 200) },
            { "usurper/NodeD", R(300, 225) },
            { "usurper/NodeE", new Reward { Gold = 300, Xp = 225, Source = Src.Placeholder } },   // Finanif: not read
            { "usurper/NodeG", new Reward { Gold = 500, Xp = 450, Packs = 1, PackName = "The Usurper", Source = Src.Video } },
            // The Smoldering Dead
            { "smoldering/NodeA", R(150, 125) },
            { "smoldering/NodeB", R(200, 150) },
            { "smoldering/NodeC", R(600, 300, "Voracious Vultures") },
            { "smoldering/NodeD", R(350, 200) },
            { "smoldering/NodeF", R(450, 250) },
            { "smoldering/NodeG", new Reward { Gold = 650, Xp = 500, Packs = 1, PackName = "Smoldering Dead", Source = Src.Video } },
            // Devonshire Keep: read for A, C, D, K, L; the others are PLACEHOLDER (between their neighbours)
            { "devonshire/NodeA", R(150, 125) },
            { "devonshire/NodeC", new Reward { Gold = 75, Xp = 200, Source = Src.Video } },
            { "devonshire/NodeD", R(400, 250) },
            { "devonshire/NodeE", new Reward { Gold = 250, Xp = 225, Source = Src.Placeholder } },
            { "devonshire/NodeF", new Reward { Gold = 300, Xp = 250, Source = Src.Placeholder } },
            { "devonshire/NodeG", new Reward { Gold = 300, Xp = 250, Source = Src.Placeholder, Note = "XP 250: Src.Video" } },
            { "devonshire/NodeH/1", new Reward { Gold = 250, Xp = 225, Source = Src.Placeholder } },
            { "devonshire/NodeH/2", new Reward { Gold = 300, Xp = 250, Source = Src.Placeholder } },
            { "devonshire/NodeI", new Reward { Gold = 350, Xp = 275, Source = Src.Placeholder } },
            { "devonshire/NodeJ", new Reward { Gold = 400, Xp = 300, Source = Src.Placeholder } },
            { "devonshire/NodeK", R(550, 400) },
            { "devonshire/NodeL", new Reward { Gold = 750, Xp = 650, Packs = 1, PackName = "Devonshire Keep", Source = Src.Video } },

            // ---- AZ2 map battles (Src.Video, MyHotStream Underworld run; the same for both factions: PLACEHOLDER for the
            // Ardent side of faction nodes). Packs: Loot.AZ2Pack. Not given: the Lurking Crocodile's card (not identified)
            // and the mercenaries of Augustine and Katsuhiro (mercenaries are not implemented).
            { "AZ2/Node001", R(0, 350, "Phoenix Guard Tactician") },        // Lurking Crocodile (KVC)
            { "AZ2/Node038", R(0, 625, "Aryndel Warden", "Sister of the Herd (mercenary)") },   // and her mercenary (KVC)
            { "AZ2/Node043", R(0, 650, "Legendary Stardust") },               // Monk of the Six Strikes (KVC)
            { "AZ2/Node006", new Reward { Gold = 600, Xp = 425, Packs = 1, PackName = Loot.AZ2Pack, Source = Src.Video } },   // Winter Moon
            { "AZ2/Node008", R(0, 400, "Common Stardust") },                  // Flock of Seagulls
            { "AZ2/Node009", R(0, 450, "Katsuhiro (mercenary)") },                       // Katsuhiro, and his mercenary
            { "AZ2/Node011", new Reward { Gold = 400, Xp = 450, Packs = 1, PackName = Loot.AZ2Pack, Source = Src.Video } },   // Hired Horn Picaroon
            { "AZ2/Node013", new Reward { Xp = 500, Packs = 1, PackName = Loot.AZ2Pack, Source = Src.Video } },   // Orchin
            { "AZ2/Node015", R(0, 500, "Rare Stardust") },                    // Giant Jellyfish
            { "AZ2/Node020", R(0, 550, "Ricochet Blast") },                   // Ayotochi Refugee
            { "AZ2/Node021", new Reward { Gold = 600, Xp = 700, Packs = 1, PackName = Loot.AZ2Pack, Source = Src.Video } },   // Lazgar Chul
            { "AZ2/Node022", R(0, 575) },                                     // Darkspire Scout
            { "AZ2/Node025", R(0, 550, "Rare Stardust") },                    // Dire Toad
            { "AZ2/Node028", new Reward { Gold = 400, Xp = 450, Packs = 1, PackName = Loot.AZ2Pack, Source = Src.Video } },   // Puke Troll
            { "AZ2/Node030", R(0, 450, "Augustine (mercenary)") },                       // Augustine, and his mercenary with his deck
            { "AZ2/Node032", R(0, 425, "Uncommon Stardust") },                // Bog Walker
            // ---- AZ2 sea battles (Src.Video, Ardent run "The Reckoning"; the Underworld run adds its racial bonus line).
            // XP to the champion; 1 ship talent point for the first win over the three storms and Bloodhorn.
            { "AZ2/NodeB01", new Reward { Xp = 625, Packs = 1, PackName = Loot.AZ2Pack, ShipTalentPoints = 1, Source = Src.Video } },   // Cloud Queen
            { "AZ2/NodeB03", R(0, 650, "Hardshell Infantry") },               // Hardshell Deepwalker
            { "AZ2/NodeB04", new Reward { Xp = 675, Packs = 1, PackName = Loot.AZ2Pack, Source = Src.Video } },   // Scylla
            { "AZ2/NodeB05", R(350, 675) },                                   // Hired Horn Scum
            { "AZ2/NodeB06", new Reward { Xp = 650, Packs = 1, PackName = Loot.AZ2Pack, ShipTalentPoints = 1, Source = Src.Video } },   // Cloud King
            { "AZ2/NodeB08", R(0, 625, "Hardshell Wizard") },                 // Hardshell Saltmage
            { "AZ2/NodeB09", R(350, 600) },                                   // Lightning Armada
            { "AZ2/NodeB10", new Reward { Xp = 650, Packs = 1, PackName = Loot.AZ2Pack, Source = Src.Video } },   // Charybdis
            { "AZ2/NodeB13", new Reward { Xp = 650, Packs = 1, PackName = Loot.AZ2Pack, ShipTalentPoints = 1, Source = Src.Video } },   // Storm of the Century
            { "AZ2/NodeB14", R(300, 650) },                                   // Kraken Guard Captain
            { "AZ2/NodeB15", R(500, 675) },                                   // Admiral Chumspike
            { "AZ2/NodeB16", R(400, 650) },                                   // Hardshell Overseer
            { "AZ2/NodeB17", new Reward { Gold = 600, Xp = 675, ShipTalentPoints = 1, Source = Src.Video } },   // Bloodhorn
            { "AZ2/NodeB18", R(0, 700, "Old Man of the Sea") },               // Old Man of the Sea (Hexlantis)
            { "AZ2/NodeB07", R(0, 0, "Sea Spirit") },                         // the Lighthouse: "something useful inside"
            // ---- AZ2 dungeons (Src.Video: KVC let's play, Dec 2016-Jan 2017, each value seen in 2-4 runs; Brutecrown also
            // MyHotStream). The bonus gold line of dungeon battles: see DungeonBonusGold.
            // Brutecrown Bluff
            { "brutecrown/NodeB", R(200, 225) },                              // Burgg
            { "brutecrown/NodeC", R(250, 250) },                              // Zagg
            { "brutecrown/NodeD", R(300, 300) },                              // Vargar
            { "brutecrown/NodeH", R(400, 400) },                              // Nogg Nogg's Handler
            { "brutecrown/NodeJ", new Reward { Gold = 500, Xp = 500, Packs = 1, PackName = "Brutecrown Bluff", Source = Src.Video } },   // King Kazraga
            // Great Machine Graveyard
            { "machinegraveyard/NodeB", R(250, 250) },                        // Scraptooth Bouncer
            { "machinegraveyard/NodeC", R(300, 275) },                        // Scraptooth Mangler
            { "machinegraveyard/NodeD", R(350, 325) },                        // Crazed Rummager
            { "machinegraveyard/NodeE", R(400, 375) },                        // Kuul'Dag
            { "machinegraveyard/NodeF", R(500, 400) },                        // Scraptooth Bender
            { "machinegraveyard/NodeG", R(425, 500) },                        // Dynamo Swarm
            { "machinegraveyard/NodeI", R(650, 750) },                        // Scraptooth Crane Operator
            { "machinegraveyard/NodeJ", R(650, 750) },                        // Scraptooth Crusher
            { "machinegraveyard/NodeK", R(650, 750) },                        // Narl
            { "machinegraveyard/NodeM", R(575, 550) },                        // each Scrapborn sister
            { "machinegraveyard/NodeN", new Reward { Gold = 750, Xp = 1000, Packs = 1, PackName = "Great Machine Graveyard", Source = Src.Video } },   // Scutt
            // The Kraken
            { "kraken/NodeA", R(200, 225, "Mayor Galway (mercenary)") },                  // Mayor Galway lives: his mercenary (KVC)
            { "kraken/NodeE", R(250, 250) },                                  // Eye of the Storm
            { "kraken/NodeF", R(300, 300) },                                  // Hardshell Ambush
            { "kraken/NodeG", R(400, 350) },                                  // Nairne, Sea Sorceress
            { "kraken/NodeH", R(200, 0) },                                    // Algae Cave: "a couple of slime-covered coins"
            { "kraken/NodeI", R(450, 400) },                                  // Krharath, Hardshell Warlord
            { "kraken/NodeJ", R(500, 450) },                                  // The Siren
            { "kraken/NodeL", R(550, 500) },                                  // Queen Magaidh
            { "kraken/NodeO", R(600, 550) },                                  // Spirit of the Sea
            { "kraken/NodeP", new Reward { Gold = 750, Xp = 600, Packs = 1, PackName = "Kraken's Cove", Source = Src.Video } },   // The Kraken
            // Ruins of Kukatan
            { "kukatan/NodeA", R(200, 225) },                                 // Mosquito Swarm
            { "kukatan/NodeB", R(250, 250) },                                 // Nibblin Swarm
            { "kukatan/NodeC", R(325, 325) },                                 // Crocodile River
            { "kukatan/NodeO", R(425, 375) },                                 // Filk Ape (room by elimination)
            { "kukatan/NodeF", R(475, 425) },                                 // Shin'hare / Coyotle Explorers
            { "kukatan/NodeR", R(525, 450) },                                 // Hexshelled Turtle
            { "kukatan/NodeI", R(550, 475) },                                 // the glyphs (memory or trap)
            { "kukatan/NodeJ", R(575, 500) },
            { "kukatan/NodeL", R(600, 525) },                                 // Crushing Walls
            { "kukatan/NodeP", R(625, 550) },                                 // Nibblin Ceremony
            { "kukatan/NodeQ", new Reward { Gold = 600, Xp = 525, Source = Src.Placeholder, Note = "Tomb of Omens never fought on video" } },
            { "kukatan/NodeM", R(675, 600, "Rare Stardust", "Uncommon Stardust", "Common Stardust") },   // Exploding Zombies
            // Lukaz: one reward window for Echo Ridge (Src.Video), given after the second battle.
            { "kukatan/NodeN/1", new Reward { Source = Src.Video, Note = "one window for both Lukaz battles" } },
            { "kukatan/NodeN/2", new Reward { Gold = 700, Xp = 650, Packs = 1, PackName = "Ruins of Kukatan", Source = Src.Video } },
        };

        /// <summary>
        /// Turn-in of the race's "Crayburn Castle" quest at the end of the castle (Src.Video: 150 gold,
        /// 500 XP and the Crayburn Castle Pack, i.e. its cards, see CrayburnCards).
        /// </summary>
        public static void OnCrayburnDone(ulong champId)
        {
            champion_bits c = Champions.Get(champId);
            string[] cards = new string[0];
            if (c != null)
            {
                string race = c.Race.ToString().Replace("'", "").Replace("_", "");
                for (int i = 0; i < Races.Length; i++) if (string.Equals(Races[i], race, StringComparison.OrdinalIgnoreCase)) cards = CrayburnCards[i];
            }
            string key = champId + ":crayburn";
            if (Saves.Current.Claims.Contains(key)) return;
            Saves.Current.Claims.Add(key);
            Grant(champId, new Reward { Gold = 150, Xp = 500, Cards = cards, Source = Src.Video }, null, "quest Crayburn Castle");
        }

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
        /// Crayburn Castle Pack by race (given with 150 gold and 500 XP at the turn-in of the race's
        /// "Crayburn Castle" quest, Src.Video): 4 cards, Src.LootTables2016 ("Crayburn Packs"; Vennen
        /// "Entangling Webs" from Src.Video, the sheet says "Incubation Webs"), then the race's AZ1 rare:
        /// the companion champion of its Crayburn training (Src.Video for Necrotic Iddi, Elf Nerissa,
        /// Vennen Zilth; inferred for the others from the AZ0 encounters, Src.Game).
        /// Not given yet: the castle dungeon is not implemented.
        /// </summary>
        private static readonly string[][] CrayburnCards =
        {
            new[] { "Noble Citizenry", "Phoenix Guard Trainer", "Captain of the Dragon Guard", "Shards of Fate", "Gareth Kay" },
            new[] { "Rotroot Enchanter", "Emberleaf Wardancer", "Merry Minstrels", "Rootforged Regalia", "Nerissa" },
            new[] { "Nightsky Stargazer", "Brightmoon Brave", "Tyrannosaurus Hex", "Shards of Fate", "Whispering Breeze" },
            new[] { "Darkspire Priestess", "Mazat Spearman", "Wrathseeker", "Assault Bot", "Moqui" },
            new[] { "Axe Bot", "Researcher Adept", "War Bot Bunker", "Construction Plans: War Hulk", "Glendower" },
            new[] { "Call the Grave", "Spiritbound Spy", "Deepgaze Acolyte", "Shards of Fate", "Iddi" },
            new[] { "Concubunny", "Tyrannosaurus Hex", "Shroomtank", "Ebony Pawn", "Sora" },
            new[] { "Hatchery Priest", "Entangling Webs", "Vicious Vivisector", "Shield Bot", "Zilth" },
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
            foreach (Reward r in QuestRewards.Values) foreach (string c in r.Cards) yield return c;
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
        /// (Src.Game). Sporemist Hollow = Artifact (Guide2016, Src.Video), Uuug's Oasis = Ruby and Lena
        /// Grotto = Diamond (let's plays), Bauer Dunes = Sapphire and Blau Flats = Blood (Src.Video: the
        /// node selected on the map right before the Haus's choice window); Hoff Mines = Wild (the one left;
        /// a Wild choice was seen on video, its node not).
        /// </summary>
        private static readonly Dictionary<string, string> HausByNode = new Dictionary<string, string>
        {
            { "Node004", "Artifact" }, { "Node042", "Sapphire" }, { "Node025", "Diamond" },
            { "Node077", "Blood" }, { "Node029", "Wild" }, { "Node035", "Ruby" },
        };

        /// <summary>Card template ids offered by a Shroom Haus (the game's choice window).</summary>
        public static List<string> ShroomHausChoices(string node)
        {
            string kind;
            if (!HausByNode.TryGetValue(node, out kind)) kind = "Artifact";
            List<string> res = new List<string>();
            foreach (string name in ShroomHauses[kind])
            {
                Loot.Drop d = Loot.Resolve(name, true);
                if (d != null && d.Card != null) res.Add(d.Card.m_Id.ToString());
            }
            return res;
        }

        /// <summary>Card picked in a Shroom Haus window: grants it with the node's reward (once). False if the choice is not offered.</summary>
        public static bool OnShroomHausChoice(ulong champId, string node, string templateId)
        {
            if (templateId == null || !ShroomHausChoices(node).Contains(templateId)) { Debug.LogWarning("[HexRevival] [Progression] Shroom Haus choice rejected: " + templateId); return false; }
            string key = champId + ":" + node;
            if (Saves.Current.Claims.Contains(key)) return true;
            Saves.Current.Claims.Add(key);
            Reward r;
            if (!AZ1.TryGetValue(node, out r)) r = new Reward();
            CardTemplate t = Singleton<TemplateManager>.Instance.GetCardTemplate(new ResourceId(templateId));
            Reward chosen = new Reward { Gold = r.Gold, Xp = r.Xp, Cards = new[] { t.m_Name }, Source = r.Source };
            Grant(champId, chosen, node, "Shroom Haus " + node);
            return true;
        }

        private static string ShroomHausCard(string node)
        {
            string kind;
            if (!HausByNode.TryGetValue(node, out kind)) { kind = "Artifact"; Debug.Log("[HexRevival] [Progression] unknown Shroom Haus " + node + ": Artifact Haus"); }
            string[] cards = ShroomHauses[kind];
            return cards[UnityEngine.Random.Range(0, cards.Length)];
        }

        /// <summary>Campaign battle won on a map node: rewards, champion XP, account XP.</summary>
        /// <summary>
        /// A campaign battle won (map node, sea or dungeon room). bonusGold: dungeon battles' second gold line, 25% of
        /// the gold (Src.Video, KVC: never on a run's first battle, then on every win until the first defeat of the run).
        /// </summary>
        public static void OnCampaignBattleWon(ulong champId, string node, bool bonusGold = false)
        {
            Reward r;
            Reward special = AZ1.TryGetValue(node, out r) ? r : null;
            string key = champId + ":" + node;
            Reward give;
            bool seaRepeat = node.StartsWith("AZ2/NodeB") && Saves.Current.Claims.Contains(key + ":won");
            if (node.StartsWith("AZ2/NodeB")) Saves.Current.Claims.Add(key + ":won");
            if (special == null) give = DefaultBattle;
            // A sea battle won again (Src.Video, KVC): half the gold, a fifth of the XP (Overseer 200 / 130, Bloodhorn 300 / 135).
            else if (seaRepeat) give = new Reward { Gold = special.Gold / 2, Xp = special.Xp / 5, Source = Src.Video };
            else if (!special.HasItems || Saves.Current.Claims.Contains(key)) give = special.GoldXpOnly();
            else if (special.NeedsEvent != null && !Campaign.BattleEvents.Contains(special.NeedsEvent))
            {
                // Condition not met: gold and XP only, the items stay to win.
                give = special.GoldXpOnly();
                if (special.Packs > 0) give.Packs = special.Packs;   // the pack does not depend on the event (Src.Video)
            }
            else
            {
                give = special;
                Saves.Current.Claims.Add(key);
            }
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
                string tameKey = champId + ":tamed:" + t.m_Name;
                if (Saves.Current.Claims.Contains(tameKey) || Array.Exists(give.Cards, x => string.Equals(x, t.m_Name, StringComparison.OrdinalIgnoreCase))) continue;
                Saves.Current.Claims.Add(tameKey);
                extra.Add(t.m_Name);
            }
            if (extra.Count > 0)
            {
                Reward withTamed = new Reward { Gold = give.Gold, Xp = give.Xp, Packs = give.Packs, PackName = give.PackName, RandomEquipment = give.RandomEquipment, RaceQuest = give.RaceQuest, ShroomHaus = give.ShroomHaus, Source = give.Source };
                List<string> all = new List<string>(give.Cards);
                all.AddRange(extra);
                withTamed.Cards = all.ToArray();
                give = withTamed;
            }
            if (bonusGold && give.Gold >= 4) { give = give.Copy(); give.BonusGold = give.Gold / 4; }
            Grant(champId, give, node, "campaign " + node);
        }

        /// <summary>True when an opponent troop was captured with the taming item in the match that ended.</summary>
        public static bool AnyTamed() { return QuestSim.Active ? QuestSim.Tamed : TamedCreatures().Count > 0; }

        /// <summary>
        /// Quest rewards, given at the turn-in (Src.Video: the reward window after the quest giver's
        /// last conversation, Dec 2016-2017 client, Necrotic and Elf; race items Src.LootTables2016).
        /// </summary>
        private static readonly Dictionary<string, Reward> QuestRewards = new Dictionary<string, Reward>
        {
            // Find the Cave-In (Fahrny) / the Ambling Mesa (Feather Drifting)
            { "az01_ar_find_ambling_mesa", new Reward { Gold = 350, Xp = 350, Packs = 1, RaceQuest = "mesa", Source = Src.Video } },
            { "az01_uw_find_cave_in", new Reward { Gold = 350, Xp = 350, Packs = 1, RaceQuest = "mesa", Source = Src.Video } },
            // Cross the Zila River (Weston) and the Zodiac River (Wallace / Warren)
            { "az01_q_cross_the_river", new Reward { Gold = 300, Xp = 300, Packs = 1, RaceQuest = "zila", Source = Src.Video } },
            { "az01_q_cross_the_river_part2", new Reward { Gold = 400, Xp = 300, Packs = 1, RaceQuest = "zodiac", Source = Src.Video } },
            // Tamed (Takumi / Belarius, after the 5 creatures)
            { "az01_tamed", new Reward { Gold = 400, Xp = 400, Packs = 1, Cards = new[] { "Containment Sphere" }, Source = Src.Video } },
            // The Sea Witch (Ennis / Kian: the sea hag's saliva)
            { "q_seawitch", new Reward { Gold = 500, Xp = 400, Packs = 2, Source = Src.Video } },
            // Three Braves (Bluejay; same reward for both robot choices)
            { "az01_three_braves", new Reward { Gold = 350, Xp = 350, Cards = new[] { "Rare Stardust" }, Source = Src.Video } },
            // Gnomes Come Home (Vincent)
            { "q_gnomes_come_home", new Reward { Gold = 500, Xp = 500, Packs = 1, Cards = new[] { "Gnome Wayfarer" }, Source = Src.Video } },
            // The Blood Beneath (Rizzix / Gurtok: the flask)
            { "q_blood_beneath", new Reward { Gold = 750, Xp = 500, Packs = 1, Cards = new[] { "Contract Killing" }, Source = Src.Video } },
            // Find Myaa (first meeting)
            { "q_find_myaa", new Reward { Xp = 100, Source = Src.Video } },
            // Army of Myth (Myaa)
            { "q_army_of_myth", new Reward { Gold = 350, Xp = 350, Packs = 1, Source = Src.Video } },
            // Dungeon quests: the dungeon packs are given once the dungeons are implemented (see docs/PROGRESSION.md)
            { "q_tranquil_dream", new Reward { Xp = 700, Packs = 1, PackName = "Tranquil Dream", Source = Src.Video } },
            { "az01_q_fort_romor", new Reward { Xp = 900, Packs = 1, PackName = "Fort Romor", Source = Src.Video, Note = "early 2016: also 1000 gold" } },
            { "az01_q_usurper", new Reward { Xp = 900, Packs = 1, PackName = "The Usurper", Source = Src.Placeholder, Note = "not seen: as Fort Romor, the other faction's quest" } },
            { "q_smoldering_dead", new Reward { Gold = 550, Xp = 1200, Packs = 1, Source = Src.Video } },
            // AZ2 (Src.Video, Underworld run): the Alachian Sea (Takumi; its card not identified), Petulant Picaroons
            // (Bonewalker), Brutecrown Bluff, the Kraken (Renner)
            { "q_boat_elementals", new Reward { Gold = 500, Xp = 1000, Packs = 1, PackName = Loot.AZ2Pack, Source = Src.Video } },   // Stormwatch (KVC)
            { "q_boat_lobsters", new Reward { Gold = 500, Xp = 1000, Cards = new[] { "Hardshell Sealord" }, Source = Src.Video } },   // Lobster Tale (KVC)
            { "q_great_machine_graveyard", new Reward { Gold = 1800, Xp = 2400, Packs = 1, PackName = "Great Machine Graveyard", Source = Src.Video } },
            { "q_ruins_of_kukatan", new Reward { Gold = 1500, Xp = 2100, Packs = 1, PackName = "Ruins of Kukatan", Cards = new[] { "Chronicles of Entrath: Chapter 2 Sleeve" }, Source = Src.Video } },
            { "q_redhand_raiders", new Reward { Gold = 750, Xp = 1100, Cards = new[] { "Darkspire Acolyte" }, Source = Src.Video } },   // Two Tomahawks (KVC)
            { "q_boat_pirates", new Reward { Gold = 500, Xp = 1000, Cards = new[] { "Hired Horn Captain" }, Source = Src.Video } },
            { "q_alachian_sea", new Reward { Gold = 700, Xp = 1100, Source = Src.Video, Note = "+ a card not identified" } },
            { "q_petulent_picaroons", new Reward { Gold = 500, Xp = 800, Cards = new[] { "Hired Horn Cutthroat" }, Source = Src.Video } },
            { "q_brutecrown_bluff", new Reward { Gold = 1100, Xp = 1500, Packs = 1, PackName = "Brutecrown Bluff", Source = Src.Video } },
            { "q_kraken", new Reward { Gold = 1300, Xp = 1800, Packs = 1, PackName = "Kraken's Cove", Source = Src.Video } },
            { "q_devonshire_keep", new Reward { Gold = 600, Xp = 1500, Packs = 1, PackName = "Devonshire Keep", Cards = new[] { "Chronicles of Entrath: Chapter 1 Sleeve" }, Source = Src.Video, Note = "gold seen on the Elf run only" } },
        };

        /// <summary>Gold of a quest whose reward is unknown. PLACEHOLDER.</summary>
        private const int QuestGold = 300;

        /// <summary>The ship named at Portsmeare Harbor (Src.Video: a reward window with 3 ship talent points).</summary>
        public static void OnShipNamed(ulong champId)
        {
            string key = champId + ":shipnamed";
            if (Saves.Current.Claims.Contains(key)) return;
            Saves.Current.Claims.Add(key);
            Grant(champId, new Reward { ShipTalentPoints = 3, Source = Src.Video }, null, "ship named");
        }

        public static void OnQuestFinished(ulong champId, string script)
        {
            Reward r;
            if (!QuestRewards.TryGetValue(script, out r)) r = new Reward { Gold = QuestGold, Source = Src.Placeholder };
            Grant(champId, r, null, "quest " + script);
            // The three sea quests done: a sleeve, in its own window (Src.Video, KVC: after the last turn-in).
            if (script.StartsWith("q_boat_") && Quests.IsFinished(champId, "q_boat_pirates") && Quests.IsFinished(champId, "q_boat_elementals")
                && Quests.IsFinished(champId, "q_boat_lobsters") && !Saves.Current.Claims.Contains(champId + ":seasleeve"))
            {
                Saves.Current.Claims.Add(champId + ":seasleeve");
                Grant(champId, R(0, 0, "Alachian Sea Sleeve"), null, "sea quests");
            }
        }

        /// <summary>Opponent troops of the match that ended, in the void with the Tamed marker.</summary>
        private static List<CardTemplate> TamedCreatures()
        {
            // The game's taming effect voids the creature and records its card id in the human
            // champion's TamedCards list (Src.Game: TACAbilityEffectTemplate.Tame).
            List<CardTemplate> res = new List<CardTemplate>();
            try
            {
                StandaloneSession ss = Server.CurrentSession;
                if (ss == null) return res;
                List<Card> all = new List<Card>();
                Game.Shared.Player human = null;
                foreach (Game.Shared.Player p in ss.GetAllPlayers())
                {
                    if (p == null) continue;
                    all.AddRange(p.GetAllCards());
                    if (!p.IsAI()) human = p;
                }
                TACList tamed = human != null && human.m_ChampionCard != null ? human.m_ChampionCard.Get(TACAttrs.PermanentData, ListAttrs.TamedCards) : null;
                if (tamed == null) return res;
                foreach (TAC t in tamed)
                {
                    int id = t.Get(IntAttrs.Id);
                    Card c = all.Find(x => x != null && (int)x.m_SessionCardId.InstanceId == id);
                    if (c == null) continue;
                    CardTemplate tpl = c.m_OriginalCardTemplateId.IsValid ? Singleton<TemplateManager>.Instance.GetCardTemplate(c.m_OriginalCardTemplateId) : null;
                    if (tpl == null) tpl = c.m_CardTemplate;
                    if (tpl != null && !res.Exists(x => x.m_Name == tpl.m_Name)) res.Add(tpl);
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
            if (!AZ1.TryGetValue(node, out r) || r.ShroomHaus || Campaign.NodeHasBattle(node)) return;
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
            if (QuestSim.Active)
            {
                // Simulation: the reward is only described (nothing is given to the account in use).
                QuestSim.Note("  REWARD " + what + ": " + r.Gold + " gold" + (r.BonusGold > 0 ? " + " + r.BonusGold + " bonus" : "") + ", " + r.Xp + " XP, " + r.Packs + " pack(s)" + (names.Count > 0 ? ", " + string.Join(", ", names.ToArray()) : ""));
                return;
            }
            foreach (string name in names) AddDrop(Loot.Resolve(name, true), cards, items, shown);
            for (int i = 0; i < r.Packs; i++) foreach (Loot.Drop d in Loot.Open(r.PackName ?? Loot.AZ1Pack)) AddDrop(d, cards, items, shown);
            for (int i = 0; i < r.RandomEquipment; i++) AddDrop(Loot.RandomEquipment(Loot.AZ1Pack), cards, items, shown);
            // Racial talents add their bonus as a second line, as the game showed it (Src.Video, Src.Game)
            int xpPct, goldPct;
            RacialBonus(champId, out xpPct, out goldPct);
            int bonusGold = r.Gold * goldPct / 100, bonusXp = r.Xp * xpPct / 100;
            if (r.Gold > 0)
            {
                Saves.Current.Gold += r.Gold + bonusGold;
                shown.Add(Action("GOLD", r.Gold, null));
                if (bonusGold > 0) shown.Add(Action("GOLD", bonusGold, null));
            }
            if (r.BonusGold > 0)
            {
                Saves.Current.Gold += r.BonusGold;
                shown.Add(Action("GOLD", r.BonusGold, null));
            }
            int levels;
            champion_bits champ = Champions.AddXp(champId, r.Xp + bonusXp, out levels);
            if (r.Xp > 0) shown.Add(Action("XP", r.Xp, null));
            if (bonusXp > 0) shown.Add(Action("XP", bonusXp, null));
            if (levels > 0) shown.Add(Action("LEVEL", levels * TalentPointsPerLevel, null));
            if (r.ShipTalentPoints > 0)
            {
                // Shown as the game's talent point line (the original said "Ship Talent Point").
                Campaign.AddShipTalentPoints(champId, r.ShipTalentPoints);
                shown.Add(Action("LEVEL", r.ShipTalentPoints, null));
            }
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
                // A sub-champion's reward (the AZ2 ship) is shown on its owner's map, with the ship's XP bar.
                ulong shownTo = champ != null && champ.OwnerChampionId != 0 ? champ.OwnerChampionId : champId;
                m.Invoke(null, new object[] { shownTo, up });
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Progression] CacheRewards: " + e); }
            Debug.Log("[HexRevival] [Progression] " + what + ": " + r.Gold + " gold, " + r.Xp + " XP, " + cards.Count + " card(s), " + items.Count + " item(s)" + (levels > 0 ? ", level up" : ""));
            AddAccountXp(PveWinXp);
        }

        /// <summary>
        /// Racial talents of the champion's class (Src.Game: ChampionClassData, ChampionTalentData; level 1,
        /// no talent point): Necrotic "Efficient" +10% XP, +2% gold at maximum level; Human "Intellectual
        /// Pursuits" +5% XP, +3% gold at maximum level. The Necrotic line is seen on every reward window
        /// of two Necrotic champions (Src.Video: 100 + 10 XP, 350 + 7 gold at level 21).
        /// </summary>
        private static void RacialBonus(ulong champId, out int xpPct, out int goldPct)
        {
            xpPct = goldPct = 0;
            champion_bits c = Champions.Get(champId);
            if (c == null) return;
            bool maxLevel = c.Level >= MaxChampionLevel();
            if (c.Race == ERace.Necrotic) { xpPct = 10; if (maxLevel) goldPct = 2; }
            else if (c.Race == ERace.Human) { xpPct = 5; if (maxLevel) goldPct = 3; }
        }

        private static int MaxChampionLevel()
        {
            int max = 1;
            foreach (XpTable t in Singleton<TemplateManager>.Instance.XpTables.Values)
            {
                if (t == null) continue;
                foreach (XpTableItem i in t.m_XpTable) if (i.m_Level > max) max = i.m_Level;
                break;
            }
            return max;
        }

        /// <summary>Card or equipment: added to the save, listed in the loot window (silently: CacheRewards gives it to the client).</summary>
        private static void AddDrop(Loot.Drop d, List<card_instance_bits> cards, List<inventory_bits> items, List<EscrowAction> shown)
        {
            if (d == null) return;
            if (d.Card != null) { AddCard(d.Card, cards, shown); return; }
            inventory_bits it = Inventory.GrantSilently(d.Item);
            if (it != null) items.Add(it);
            // A mercenary: "MERCPAC" makes the client put it in the champion's party with its deck (Src.Game).
            InventoryMercenaryData mercData = Singleton<TemplateManager>.Instance.GetInventoryItem(d.Item) as InventoryMercenaryData;
            bool merc = mercData != null;
            // Its starter deck comes with it (Src.Video, KVC: Augustine "and his deck"), so that the deck is valid.
            if (merc) { Collection.GrantDeck(mercData.DeckTemplate); Mercenaries.OnEarned(d.Item); }
            shown.Add(Action(merc ? "MERCPAC" : "ITEM", 1, d.Item.ToString()));
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
            // The tutorial gives nothing (it can be replayed at will from the menus).
            StandaloneSession ss = session as StandaloneSession;
            if (ss != null && ss.m_EncounterData != null && ss.m_EncounterData.SessionFlags.HasAnyFlags(ESessionFlags.IsTutorial)) return;
            bool won = false;
            if (winners != null)
                foreach (UID u in winners)
                    if (!Server.IsAIId(u)) won = true;
            if (Arena.OnGameEnded(won)) return;   // the arena's own rewards (see Arena)
            if (Siege.OnGameEnded(won)) return;   // no account XP for Siege battles: PLACEHOLDER, not known
            if (Battlegrounds.OnGameEnded(won) || Battlegrounds.OnLadderGameEnded(won)) return;   // same for the Battlegrounds: PLACEHOLDER
            if (!Campaign.OnGameEnded(won) && won) OnOtherBattleWon();
        }
    }
}
