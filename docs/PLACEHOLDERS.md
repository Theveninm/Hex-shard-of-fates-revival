# Placeholder register (updated for 1.2.6, 2026-10-28)

Every value or behaviour in the mod that is not backed by a source is tagged `PLACEHOLDER` in the code
(or `Source = Src.Placeholder` in the reward tables). This file lists them so that, when a new source turns
up, it is clear what to check. Regenerate the full table with:
`grep -n "PLACEHOLDER\|Src.Placeholder" mods/HexRevival/src/HexRevival/*.cs`

Rule (from 1.2.6): no guess without the tag. A guess that the existing videos could settle is not a
placeholder, it is unfinished work: check the videos first (they are in campaign order, see
claude/campaign-chronology-and-dungeons.md), and use a placeholder only for what no video shows.

## Campaign: settled from the videos in 1.2.6 (source in the code comment next to each)
Video references: MHS = MyHotStream (UW = Underworld run, Ardent run), date + [YouTube id] + time; KVC part N + time;
Vandersveldt part N + time. Dialogue lines come from the dialogue pass (`_tools/dialogs.py`, results in
`<source>/_index/<video>.dlg.tsv` and `_handoff/campagne/dialogs_all.tsv`).

| Decision | Was | Now | Source |
|---|---|---|---|
| Devonshire Keep E, F, G, H1, H2, I, J rewards | between neighbours | 200/200, 400/250, 250/250, 500/300, 500/300, 400/300, 450/350 | reward windows, MHS UW + Ardent (Progression.cs) |
| Tranquil Dream 1st fight | 125/125 | 75 gold / 100 XP | Vandersveldt part 20 10:39 |
| Usurper quest (Sagebrush) | 900 XP + pack | 375 gold, 1000 XP, The Usurper pack | MHS Ardent 2017-03-22 [QPx3__eedOE] 13:20 |
| Brink Ridge zombie blockade scene | 19B (forest board) | 19B1 (castle board) | MHS Ardent 2017-05-16 [FoOAf3QKgC4] board |
| Scylla / Charybdis | always a battle, either line | "Pre-" line, no battle, until a sea quest is done | KVC part 14 6:36-45:55; MHS Ardent 2017-10-31; MHS UW 2017-08-18/24 |
| Bleakwind Highlands | (1.2.6 draft: post-Kraken text after the Kraken) | always the corpses text | MHS UW 2017-08-16 [H_u0T4fd7eE] 0:25, after the Kraken |
| Sea Witch opening | either | "Hag", Daphne in Distress | Vandersveldt 18-30, MHS Ardent 2017-02-07 |
| Burk Heart / Eagle Mesa faction battles | dialogue + reward | battle by faction | MHS Ardent/UW, Vandersveldt 37 |
| Gnomes Come Home | static battles | picker, routes with gnomes | MHS UW 2016-10-30 [kuNNfgNZiN4] 2:20-7:25; Vandersveldt 41-54 |
| Mithral River / raiders pairing | placeholder | confirmed | MHS Ardent 2018-03-02, 2017-08-22; MHS UW 2017-04-07 |
| Kraken mayor | placeholder "lives" | lives (both runs) | MHS UW 2017-05-25 10:32; MHS Ardent 2017-09-11 9:15 |
| Kukatan Tomb of Lingerers | placeholder | Underworld F 01 (coyotle), Ardent F 02 | KVC part 9 39:35/43:53, part 4 45:46 |
| Kukatan Blind Hollow | placeholder | dialogue only | KVC part 4 48:51, part 9 48:58 |
| Brutecrown H-1/2/3 | placeholder | Vargar / Burgg / Zagg | Src.Game: AI decks D / B / C |
| Gaal fortune duration | placeholder | next battle | MHS UW 2017-01-25 [W__kpqC147U] 0:51 |
| Graveyard robot in battle | placeholder | every battle once assembled | KVC part 3 (Mega-Bot card in every battle after the repair) |

Inferred from the game data only (no video shows them; check if a source turns up): White Hawk Step 2 after the
Killipede, AZ2 Milosh repeat dialogues, AZ2 map quest givers (Brutecrown Delta turn-in).

## Campaign placeholders still open
| Where | Placeholder | Why it stays |
|---|---|---|
| NodeScripts.cs (Node073) | Sapphire Gate open | no run saw it locked; every run had the key at the first visit |
| CampaignBoat.cs SeaMonstersAwake | "a sea quest done" | the runs also fit "from the 3rd voyage" |
| Progression.cs usurper/NodeE | Finanif reward | Shepherd's Rest never fought (MHS Ardent, KVC skip it) |
| Progression.cs kukatan/NodeQ | Tomb of Omens reward | never visited on video |
| Dungeons.cs (Sister Midnight) | Cunning / Instinct totals | never seen |
| Fortunes.cs | which fortune is drawn | random in the runs |
| Progression.cs TalentPointsPerLevel | 1 | the reward window only says "Leveled Up!" (Vandersveldt part 79 4:54) |

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
| CampaignBoat.cs:122 | 2017-08-24 [Z_41L6_4N0A], after its sea quests. PLACEHOLDER in part: the runs also fit "from the 3rd voyage". |
| Dungeons.cs:386 | "alert_gnolls", "Great Machine Graveyard F 2 - Roundabout (AI goes first)");   // PLACEHOLDER: the blast's victim plays second |
| Dungeons.cs:705 | return Quests.Flag(r.Champ, "brutecrown:ally") ?? Gnolls[0];   // Brutecrown Bluff not played: PLACEHOLDER (Vargar) |
| Dungeons.cs:728 | seen: their totals (12-13, 14-15) and the limits between the seen ones are PLACEHOLDER. |
| Fortunes.cs:17 | [W__kpqC147U] 0:51, Fortune of Diamond). PLACEHOLDER: how the fortune is drawn (random here; the runs show |
| Loot.cs:67 | the weights are a PLACEHOLDER. |
| Loot.cs:77 | equipment are drawn from the game's own AZ2 set (cards and equipment items), by rarity (PLACEHOLDER weights: |
| Mercenaries.cs:114 | PLACEHOLDER chance that an opened booster also brings a mercenary of its set. In the original, every opened |
| NodeScripts.cs:126 | Sapphire Gate: where the Sapphire Key came from is unknown (PLACEHOLDER: the gate is open). Src.Video: no run saw |
| PrizePacks.cs:20 | PLACEHOLDER (the sleeves are the game data's own: "Available in the 2018 Siege Pack" / "Cosmic Coin Pack"). |
| PrizePacks.cs:34 | PLACEHOLDER sets: the PvE promo cards and the alternate arts of the 2018 set (Doombringer, set 9). |
| PrizePacks.cs:43 | PLACEHOLDER odds: PvE card 50%, alternate art shard 20%, sleeve 15%, alternate art card 10%, mercenary 5%. |
| PrizePacks.cs:56 | "Nearly 1 in 3" a booster; sleeves "one of the rarest things" (PLACEHOLDER 5%); otherwise an alternate art card. |
| Progression.cs:23 | a PLACEHOLDER chosen for the mod, to be replaced when better information turns up. |
| Progression.cs:60 | Talent points per champion level gained. PLACEHOLDER (assumed 1). |
| Progression.cs:146 | Underworld and Ardent runs, Vandersveldt), values of the Dec 2016+ client; PLACEHOLDER where noted. |
| Progression.cs:173 | { "usurper/NodeE", new Reward { Gold = 300, Xp = 225, Source = Src.Placeholder } },   // Finanif: not read |
| Progression.cs:197 | ---- AZ2 map battles (Src.Video, MyHotStream Underworld run; the same for both factions: PLACEHOLDER for the |
| Progression.cs:275 | { "kukatan/NodeQ", new Reward { Gold = 600, Xp = 525, Source = Src.Placeholder, Note = "Tomb of Omens never fought on video" } }, |
| Progression.cs:524 | Gold of a quest whose reward is unknown. PLACEHOLDER. |
| Progression.cs:539 | if (!QuestRewards.TryGetValue(script, out r)) r = new Reward { Gold = QuestGold, Source = Src.Placeholder }; |
| Progression.cs:745 | PLACEHOLDER (assumed): 1 top card (legendary 1 time in 8; otherwise epic 1 time in 5 in |
| Progression.cs:748 | their common slots take uncommons (PLACEHOLDER, the real PvE pack content is unknown). |
| Progression.cs:857 | set), commons:N (PLACEHOLDER: random commons of the current set for the wiki's "fixed |
| Progression.cs:938 | PLACEHOLDERS: stardust and chest spins need systems the mod does not have yet. |
| Progression.cs:942 | PLACEHOLDER: tournament entry tickets have no offline use: a current set booster instead. |
| Progression.cs:968 | Random cards of a set (rarity 0 = any of common/uncommon/rare, weighted 10/3/1: PLACEHOLDER). |
| Progression.cs:1044 | if (Siege.OnGameEnded(won)) return;   // no account XP for Siege battles: PLACEHOLDER, not known |
| Progression.cs:1045 | if (Battlegrounds.OnGameEnded(won) // Battlegrounds.OnLadderGameEnded(won)) return;   // same for the Battlegrounds: PLACEHOLDER |
| Siege.cs:610 | PLACEHOLDER: the other defenses of the list are attacked by simulated players (how often and how well is not |
| Store.cs:21 | for gold: the gold price is a PLACEHOLDER. Bought packs had about a 2% chance of a bonus |
| Store.cs:26 | Gold price of a booster. PLACEHOLDER (no source: gold only bought packs from players, ~22,000 on the auction house in 2016). |
| Store.cs:45 | Gold per platinum: the boosters' 200 platinum (Src.Forum2017, also their base price in the game data) sell for 2000 gold. PLACEHOLDER ratio. |
| Store.cs:63 | PLACEHOLDER gold prices of products without a known price. |
