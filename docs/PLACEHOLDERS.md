# Placeholder register (2026-10-28)

Every value or behaviour in the mod that is not backed by a source is tagged `PLACEHOLDER` in the code
(or `Source = Src.Placeholder` in the reward tables). This file lists them so that, when a new source turns
up, it is clear what to check. Regenerate the full table with:
`grep -n "PLACEHOLDER\|Src.Placeholder" mods/HexRevival/src/HexRevival/*.cs`

Rule (from 1.2.6): no guess without the tag. A guess that the existing videos could settle is not a
placeholder, it is unfinished work: check the videos first (they are in campaign order, see
claude/campaign-chronology-and-dungeons.md), and use a placeholder only for what no video shows.

## Campaign placeholders the existing videos may settle (to do)
| Where | Placeholder | Videos to check |
|---|---|---|
| NodeScripts.cs (Node073) | Sapphire Gate always open: where the Sapphire Key comes from is unknown | Vandersveldt part 63 "Through The Blue Gate" and the parts before it; MHS runs |
| Quests.cs:97 | Mithral River quest: Eurig (Ardent) / Constantina (Underworld) pairing | MHS Ardent and Underworld runs (Mithral River / Falls fights) |
| Quests.cs:100 | Raiders quest: Xentoth's Zealot (Ardent) / Lazghar Chul (Underworld) | MHS Ardent and Underworld runs |
| Campaign.cs:319 | Brink Ridge zombie blockade: variants B1-B3 unused | MHS Ardent run after the Smoldering Dead |
| Dungeons.cs:232 | Brutecrown H-1/2/3: which ally battle | MHS runs in Brutecrown Bluff |
| Dungeons.cs:277 | Kraken "Mayor Died": the mayor always lives | MHS Kraken runs (Portsmeare battle) |
| Dungeons.cs:310 | Kukatan explorers: F 01 = Ardent | MHS Ardent and Underworld Kukatan runs |
| Dungeons.cs:320 | Kukatan Blind Hollow: Success / Fail conversations left out | MHS Kukatan runs |
| Dungeons.cs:722 | Sister Midnight: Cunning / Instinct totals | no run seen them; JBStevenson? |
| Dungeons.cs:1104 | Graveyard robot joins every battle once assembled | MHS Great Machine Graveyard runs |
| Progression.cs:152, 172, 185-191, 273, 505 | Rewards not read: Tranquil Dream 1st fight, Usurper NodeE (Finanif), Devonshire E-J, Kukatan Tomb of Omens, Usurper quest | MHS Underworld (Usurper), MHS/JBStevenson (Devonshire) |
| Fortunes.cs:16 | Which fortune is drawn and how long it lasts | the gaal camp scenes (fortune windows in the index) |
| Progression.cs:60 | Talent points per level (1) | level-up screens in any run |

## Settled in 1.2.6 from the videos (no longer placeholders)
- Sea Witch: every fight seen (Vandersveldt parts 18, 19, 29, 30; MHS Ardent 2017-02-07) opens with "Hag" and is
  the "Daphne in Distress" scenario. "No Frog" and "Princess Revert" never appear in any run: not used.
- Gnomes Come Home, Burk Heart / Eagle Mesa, AZ2 towns, Scylla / Charybdis: see claude/release-1.2.6-notes.md.

## Placeholders with no source expected (other modes, economy)
Arena, Battlegrounds, Siege, Store, prize packs, loot weights, mercenary drop chance: the original values
were server-side or came from other players; only new sources (forums, wiki, data mining) can settle them.

## Full list (generated)
| File:line | Comment |
|---|---|
| AiConfig.cs:217 | rank rises: PLACEHOLDER, the original opponents were players), and the game's own arena decks for the arena. |
| AiConfig.cs:244 | Uruunaz and Zakiir, "extremely rare" (Src.Steam2016; PLACEHOLDER 2% of the common fights where they can |
| AiConfig.cs:257 | The attackers of the player's defense: the game's prebuilt decks (PLACEHOLDER: the attackers were players). |
| AiConfig.cs:276 | PLACEHOLDER defenses (the original ones were other players'): 40 for gold, 10 for platinum, each 3 of the game's |
| AiConfig.cs:335 | Which boss in which tier: PLACEHOLDER (tiers 2-3 share the bosses, tier 4 their elite versions and Hogarth). |
| Arena.cs:262 | One fight of each tier but the boss may carry a challenge (Src.HexCompendium; PLACEHOLDER: always one). |
| Arena.cs:411 | pouch, not always the same). The split by fight is PLACEHOLDER (it gives about 8,500 for such a run). |
| Arena.cs:419 | "Boss Win" chests (1 in tier 1, 2 after, PLACEHOLDER from the counts seen), a "Perfect Tier Bonus" chest for a |
| Arena.cs:420 | tier without a loss, a "Bonus" chest (seen twice, cause unknown: PLACEHOLDER, a tier whose challenge was won). |
| Arena.cs:469 | A chest: a card (Src.Video: most chests; cards of the arena's set and of other sets: half each, PLACEHOLDER), |
| Arena.cs:470 | sometimes stardust (Src.Video: "Common Stardust x2", "Rare Stardust"; 20% PLACEHOLDER), rarely an arena |
| Arena.cs:471 | equipment (not seen in 10 chests; 5% PLACEHOLDER). |
| Arena.cs:498 | A card of the game's booster sets (common 10, uncommon 3, rare 1: PLACEHOLDER weights). |
| Battlegrounds.cs:28 | draft timer are PLACEHOLDER (not known). The client's own screens are driven through its TournamentManager: |
| Battlegrounds.cs:48 | public int GoldFee;            // PLACEHOLDER |
| Battlegrounds.cs:62 | PLACEHOLDER: the limited gauntlets use the last set's boosters (the server chose the product). |
| Battlegrounds.cs:64 | PLACEHOLDER: basic shards of each kind added to a limited pool (the deck builder needs them). |
| Battlegrounds.cs:66 | PLACEHOLDER: time to pick a draft card. |
| Battlegrounds.cs:206 | PLACEHOLDER prize: one booster per match won. |
| Battlegrounds.cs:951 | constructedIronStars = 10,   // past the iron ranks (PLACEHOLDER: the iron ranks of new players are not known) |
| Battlegrounds.cs:987 | so a win gives 1 star). Never below 0 stars (PLACEHOLDER). Returns true if it was a ladder match. |
| Campaign.cs:319 | PLACEHOLDER). Its encounter while it is to be fought, else null. |
| Dungeons.cs:232 | H-1/2/3: the ally's battle, numbered as the "J-n (Helping <ally>)" scenes (PLACEHOLDER: H's are not labelled). |
| Dungeons.cs:277 | "Mayor Died" follows Mayor Galway's death in the battle (an event of the battle, unknown): PLACEHOLDER, he lives. |
| Dungeons.cs:310 | Explorers of the other faction: shin'hare for the Ardent, coyotle for the Underworld (F 01 = Ardent: PLACEHOLDER). |
| Dungeons.cs:320 | A conversation room in the template, no K scene: its "Success" / "Fail" conversations are left out (PLACEHOLDER). |
| Dungeons.cs:380 | "alert_gnolls", "Great Machine Graveyard F 2 - Roundabout (AI goes first)");   // PLACEHOLDER: the blast's victim plays second |
| Dungeons.cs:699 | return Quests.Flag(r.Champ, "brutecrown:ally") ?? Gnolls[0];   // Brutecrown Bluff not played: PLACEHOLDER (Vargar) |
| Dungeons.cs:722 | seen: their totals (12-13, 14-15) and the limits between the seen ones are PLACEHOLDER. |
| Dungeons.cs:1104 | Cannon ... / Maxed), Mega-Bot 1000 to 8000. Which battles the robot joined: PLACEHOLDER, every battle once |
| Fortunes.cs:16 | PLACEHOLDER: which fortune is drawn (random here) and how long it lasts (one battle here). |
| Loot.cs:67 | the weights are a PLACEHOLDER. |
| Loot.cs:77 | equipment are drawn from the game's own AZ2 set (cards and equipment items), by rarity (PLACEHOLDER weights: |
| Mercenaries.cs:114 | PLACEHOLDER chance that an opened booster also brings a mercenary of its set. In the original, every opened |
| NodeScripts.cs:125 | Sapphire Gate: where the Sapphire Key came from is unknown (PLACEHOLDER: the gate is open). |
| PrizePacks.cs:20 | PLACEHOLDER (the sleeves are the game data's own: "Available in the 2018 Siege Pack" / "Cosmic Coin Pack"). |
| PrizePacks.cs:34 | PLACEHOLDER sets: the PvE promo cards and the alternate arts of the 2018 set (Doombringer, set 9). |
| PrizePacks.cs:43 | PLACEHOLDER odds: PvE card 50%, alternate art shard 20%, sleeve 15%, alternate art card 10%, mercenary 5%. |
| PrizePacks.cs:56 | "Nearly 1 in 3" a booster; sleeves "one of the rarest things" (PLACEHOLDER 5%); otherwise an alternate art card. |
| Progression.cs:23 | a PLACEHOLDER chosen for the mod, to be replaced when better information turns up. |
| Progression.cs:60 | Talent points per champion level gained. PLACEHOLDER (assumed 1). |
| Progression.cs:146 | Underworld and Ardent runs, Vandersveldt), values of the Dec 2016+ client; PLACEHOLDER where noted. |
| Progression.cs:151 | The Tranquil Dream: by the fight's rank, not the creature (1st fight not read: PLACEHOLDER 125 / 125) |
| Progression.cs:152 | { "tranquildream/rank1", new Reward { Gold = 125, Xp = 125, Source = Src.Placeholder } }, |
| Progression.cs:172 | { "usurper/NodeE", new Reward { Gold = 300, Xp = 225, Source = Src.Placeholder } },   // Finanif: not read |
| Progression.cs:181 | Devonshire Keep: read for A, C, D, K, L; the others are PLACEHOLDER (between their neighbours) |
| Progression.cs:185 | { "devonshire/NodeE", new Reward { Gold = 250, Xp = 225, Source = Src.Placeholder } }, |
| Progression.cs:186 | { "devonshire/NodeF", new Reward { Gold = 300, Xp = 250, Source = Src.Placeholder } }, |
| Progression.cs:187 | { "devonshire/NodeG", new Reward { Gold = 300, Xp = 250, Source = Src.Placeholder, Note = "XP 250: Src.Video" } }, |
| Progression.cs:188 | { "devonshire/NodeH/1", new Reward { Gold = 250, Xp = 225, Source = Src.Placeholder } }, |
| Progression.cs:189 | { "devonshire/NodeH/2", new Reward { Gold = 300, Xp = 250, Source = Src.Placeholder } }, |
| Progression.cs:190 | { "devonshire/NodeI", new Reward { Gold = 350, Xp = 275, Source = Src.Placeholder } }, |
| Progression.cs:191 | { "devonshire/NodeJ", new Reward { Gold = 400, Xp = 300, Source = Src.Placeholder } }, |
| Progression.cs:195 | ---- AZ2 map battles (Src.Video, MyHotStream Underworld run; the same for both factions: PLACEHOLDER for the |
| Progression.cs:273 | { "kukatan/NodeQ", new Reward { Gold = 600, Xp = 525, Source = Src.Placeholder, Note = "Tomb of Omens never fought on video" } }, |
| Progression.cs:505 | { "az01_q_usurper", new Reward { Xp = 900, Packs = 1, PackName = "The Usurper", Source = Src.Placeholder, Note = "not seen: as Fort Romor, the other faction's quest" } }, |
| Progression.cs:522 | Gold of a quest whose reward is unknown. PLACEHOLDER. |
| Progression.cs:537 | if (!QuestRewards.TryGetValue(script, out r)) r = new Reward { Gold = QuestGold, Source = Src.Placeholder }; |
| Progression.cs:743 | PLACEHOLDER (assumed): 1 top card (legendary 1 time in 8; otherwise epic 1 time in 5 in |
| Progression.cs:746 | their common slots take uncommons (PLACEHOLDER, the real PvE pack content is unknown). |
| Progression.cs:855 | set), commons:N (PLACEHOLDER: random commons of the current set for the wiki's "fixed |
| Progression.cs:936 | PLACEHOLDERS: stardust and chest spins need systems the mod does not have yet. |
| Progression.cs:940 | PLACEHOLDER: tournament entry tickets have no offline use: a current set booster instead. |
| Progression.cs:966 | Random cards of a set (rarity 0 = any of common/uncommon/rare, weighted 10/3/1: PLACEHOLDER). |
| Progression.cs:1042 | if (Siege.OnGameEnded(won)) return;   // no account XP for Siege battles: PLACEHOLDER, not known |
| Progression.cs:1043 | if (Battlegrounds.OnGameEnded(won) // Battlegrounds.OnLadderGameEnded(won)) return;   // same for the Battlegrounds: PLACEHOLDER |
| Quests.cs:97 | Ardent: Eurig the Robomancer (Mithral Falls); Underworld: Constantina (Mithral River) - PLACEHOLDER pairing |
| Quests.cs:100 | the raiders: James River Road, Xentoth's Zealot (Ardent) / Lazghar Chul (Underworld) - PLACEHOLDER |
| Siege.cs:610 | PLACEHOLDER: the other defenses of the list are attacked by simulated players (how often and how well is not |
| Store.cs:21 | for gold: the gold price is a PLACEHOLDER. Bought packs had about a 2% chance of a bonus |
| Store.cs:26 | Gold price of a booster. PLACEHOLDER (no source: gold only bought packs from players, ~22,000 on the auction house in 2016). |
| Store.cs:45 | Gold per platinum: the boosters' 200 platinum (Src.Forum2017, also their base price in the game data) sell for 2000 gold. PLACEHOLDER ratio. |
| Store.cs:63 | PLACEHOLDER gold prices of products without a known price. |
