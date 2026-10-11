# Progression, rewards and store

The original game decided rewards and store prices on its servers; the client files only hold
the champion XP table and the starter decks. HEX Revival therefore rebuilds progression from period
sources. This page lists every value with its source, so that **placeholders** (values chosen for
the mod, without a source) can be replaced when better information turns up.

All of it applies to accounts created without "Unlock everything" (those already own every card,
so card rewards are skipped; gold, XP and levels still count).

Code: `mods/HexRevival/src/HexRevival/Progression.cs` (rewards, levels), `Store.cs`,
`Collection.cs` (starter collection). Each value is tagged there with `Src.*`.

## Sources

| Tag | Source |
|---|---|
| Game | the game's own data files |
| Guide2016 | moderator guide on the Steam forum, April 2016 ("New Player Pitstop - Free Cards") |
| HexServer | values the hex-server project read on 2017-2018 gameplay videos |
| Wiki2019 | Hex Compendium "Account Leveling", April 2019 snapshot (Wayback Machine) |
| Forum2017 | official forum guide "Spending $$$ for HEX - Strategy", February 2017 |
| Wiki2018 | Hex Compendium main page, May 2018 snapshot |

## New account

| What | Value | Source |
|---|---|---|
| Starter collection | the game's two "new player" starter decks + 60 of each basic shard | Game (decks); shard count PLACEHOLDER |
| Starting gold | 500 | PLACEHOLDER |
| New champion | its race's starter deck (added to the collection and used as the champion's deck) | Game |

Wiki2018 says new accounts got about 250 common and uncommon cards forming a deck per major race:
the mod's starter collection may be smaller than that (to check).

## Campaign (AZ1)

Every campaign battle won: **100 gold + 100 champion XP** (HexServer). The encounters below
replace that default the first time a champion completes them (one time per champion).

| Node | Encounter | Reward | Source |
|---|---|---|---|
| Node003 Dunnwood | Wild Cub ("Feralroot Bear") | Dire Toad, 150 gold | Guide2016 |
| Node004 Sporemist Hollow | Shroomkin Haus (conversation) | 1 uncommon AZ1 artifact, 100 gold | Guide2016; the game offered a choice of 3, the mod picks one at random: PLACEHOLDER |
| Node009 The Sutherland | Cockatwice | Chickatwice, 200 gold | Guide2016 |
| Node010 Sylvan Peninsula | Squashing Pumpkins | 1 AZ1 pack, 200 gold | Guide2016 ("Wandering Abomination"); node inferred from the route |
| Node013 Zila River | Gnash Bridges | Ravenous Piranha | HexServer |
| Node014 Razortooth Forest | Savage Lord | 200 gold, 200 XP | Guide2016 (gold), HexServer (XP) |
| Node015 Bridge over the Zila - West | first bridge quest (conversation) | 1 AZ1 pack, 300 gold | Guide2016; quest condition not checked yet |
| Node018 Shadowgrove | Corrupt Dryad | 1 AZ1 pack, race equipment, 250 XP | Guide2016, HexServer (XP); **race equipment not given yet** |
| Node030 Bridge over the Zodiac - East | second bridge quest (conversation) | 1 AZ1 pack, race rare | Guide2016 ("Zila East", node inferred); race rare picked at random among AZ1 rares: PLACEHOLDER |

Not yet: Crayburn Castle (1 race rare, 4 PvP commons, 150 gold, Guide2016), the dungeons, AZ2.

Champion levels follow the game's XP table (Game): level 2 at 1,000 XP … 15 at 110,000.
Talent points per level: 1 (PLACEHOLDER).

## Account level (Wiki2019)

- Account XP per win: 50 for PvE (campaign, Quick Match and practice against the AI).
- The first 1,200 XP of each period count double; cap 4,000 XP per period (8,000 from level 20);
  the period resets 20 hours after its first win.
- XP per level: 1,000 (level 2) rising to 7,000 (level 41 and up); maximum level 100.
- Rewards by level: levels 2-40 as listed by the wiki, then the 10-level cycle the game used.
  The XP bar at the bottom left of the main menu shows level and daily XP; rewards are given
  automatically on level up (the game made you claim them).

What each reward becomes in the mod:

| Wiki reward | In the mod | Status |
|---|---|---|
| Gold | gold | sourced |
| Howling Plains / Alachian Sea pack | opened right away (AZ1 / AZ2 cards) | sourced, contents PLACEHOLDER (see packs) |
| Current set booster | Doombringer booster, opened right away | "current set" = last set: assumption |
| 6 fixed commons | 6 random commons of the current set | PLACEHOLDER |
| 3 random cards | 3 random cards of the current set (common/uncommon/rare weighted 10/3/1) | weights PLACEHOLDER |
| Stardust | 300 gold | PLACEHOLDER (no stardust yet) |
| Common and uncommon free spin chests | 500 gold | PLACEHOLDER (no chest wheel yet) |
| Rare paid spin chest | 1,000 gold | PLACEHOLDER |
| Draft / Evolution entry ticket | 1 current set booster | PLACEHOLDER (no tournaments offline) |
| Exclusive deck sleeve, alternate arts | nothing | cosmetics not implemented |
| Sin'glar (level 9) | nothing | mercenaries not implemented |

## Store

| What | Value | Source |
|---|---|---|
| Items | every booster pack of the game (sets 1 to 9) | Game |
| Price | 2,000 gold | PLACEHOLDER: the store sold boosters for 200 platinum ($2, Forum2017); platinum cannot be earned offline |
| Bought packs | opened right away | mod choice |
| Bonus primal pack | 2% chance per bought pack: 2 legendaries + 13 rares of the same set (its legendary chest is not given) | Forum2017 |

Starter decks, collector's decks, full sets, chests and other store products are not sold yet.

## Packs

17 cards per pack (Forum2017: 3 packs = 51 cards). Rarity split PLACEHOLDER: 1 top card
(legendary 1 time in 8, otherwise epic 1 time in 5 in the sets that have epics, otherwise rare),
3 uncommons, 13 commons, no basic shards.

The campaign sets AZ1 (Howling Plains) and AZ2 (Alachian Sea) have no commons in the game data
(AZ1: 39 uncommons, 46 rares, 11 legendaries): their common slots take uncommons (PLACEHOLDER,
the real content of these packs is unknown).

## Matches outside the campaign

Quick Match and practice wins give account XP only: no source mentions gold or cards for AI
matches outside the campaign and the arena.
