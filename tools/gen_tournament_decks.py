#!/usr/bin/env python3
"""
Builds mods/HexRevival/assets/tournament_decks.txt: real decks played in HEX constructed tournaments, for the AI
opponents of the modes whose original opponents were players (ladder, constructed gauntlet, Siege).

Source: Hex Codex's tournament database (hex-codex.romosjr.workers.dev/tournaments/), 46,674 decks from 1,550
events (2016-2020). Its data files are not in this repository; download them first:
  /tournaments/data/core.json, /tournaments/data/decks.json, /tournaments/data/lists/constructed_9.json
  and /deck-builder/keywords.json (the game's raw card names), then:
  tools/gen_tournament_decks.py <folder with the downloaded files>

Selection (the last era, "Sets 9+", March 2018 - April 2020, the game's final card pool):
- each deck with a list of 60 to 75 main cards;
- each player is rated by their whole tournament record in that era (wins smoothed toward 50%), and each deck gets
  its player's rating: the ladder tiers take the decks of weaker players at the bottom and of the strongest at the
  top (PLACEHOLDER mapping: the original ladder matched players of a similar rank);
- in each tier, the most played archetypes (champion + shards) of that band, one list each (Bronze and Silver: a
  typical player's of the band; above: the best record), with the archetype's share of the band as its frequency;
- the constructed gauntlet and Siege: the 30 most played archetypes of the era, the best record of each.
No player names are written: a deck is named after its champion, shards and record.
"""
import collections, json, os, sys

SRC = sys.argv[1] if len(sys.argv) > 1 else "."
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "mods", "HexRevival", "assets", "tournament_decks.txt")
ERA = "constructed_9"
TIERS = ["Bronze", "Silver", "Gold", "Platinum", "Cosmic"]   # the ladder's tiers (AiConfig.BuildDefaults)
PER_TIER = 20       # archetypes per ladder tier
GAUNTLET = 30       # archetypes for the constructed gauntlet and Siege (all bands)
SHARDS = {"B": "Blood", "D": "Diamond", "R": "Ruby", "S": "Sapphire", "W": "Wild"}


def load(name):
    with open(os.path.join(SRC, name), encoding="utf-8") as f:
        return json.load(f)


def find(*names):
    for n in names:
        if os.path.exists(os.path.join(SRC, n)): return load(n)
    raise SystemExit("missing " + " or ".join(names))


core = find("core.json", "tournaments__data__core.json")
decks = find("decks.json", "tournaments__data__decks.json")
lists = find(ERA + ".json", "lists__" + ERA + ".json")
keywords = find("keywords.json", "deck-builder__keywords.json")

era = next(i for i, e in enumerate(core["eras"]) if e["id"] == ERA)
series = core["series"]
tournaments = core["tournaments"]          # [date, series index, era index]
cards = {c[0]: c[2] for c in core["cards"]}
# The game's own spelling where the codex tidied it (trailing spaces...): the mod looks cards up by the game's names.
raw = {i: n for i, n in keywords.get("rawNames", [])}
champions = {c[0]: c[2] for c in core["champions"]}
list_of = dict(zip(json.loads(lists["ids"]) if isinstance(lists["ids"], str) else lists["ids"],
                   json.loads(lists["lists"].replace("'", '"')) if isinstance(lists["lists"], str) else lists["lists"]))


def decode(packed):
    """The codex's packed list: 'id36*n,...' with ids as increasing deltas; '|' then the reserve; printings after."""
    main = packed.split("|")[0]
    res, cur = [], 0
    for part in main.split(","):
        if not part: continue
        k, _, n = part.partition("*")
        cur += int(k, 36)
        res.append((cur, int(n) if n else 1))
    return res


# deck: [tournament, player, champion, wins, losses, identity, has list, list id, cost]
era_decks = [d for d in decks if tournaments[d[0]][2] == era]

# Player ratings over the era: (wins + 5) / (games + 10), so that a few lucky games do not make a champion player.
wl = collections.defaultdict(lambda: [0, 0])
for d in era_decks:
    wl[d[1]][0] += d[3]; wl[d[1]][1] += d[4]
rating = {p: (w + 5) / (w + l + 10) for p, (w, l) in wl.items()}

pool = []
for d in era_decks:
    if not d[6] or d[7] not in list_of or d[2] not in champions: continue
    main = decode(list_of[d[7]])
    n = sum(c for _, c in main)
    if n < 60 or n > 75 or any(i not in cards for i, _ in main): continue
    pool.append({"deck": d, "main": main, "rating": rating[d[1]], "date": tournaments[d[0]][0],
                 "series": series[tournaments[d[0]][1]]["name"], "arch": (champions[d[2]], d[5])})
pool.sort(key=lambda x: x["rating"])
print(len(era_decks), "decks in", ERA, "->", len(pool), "usable lists")

bands = [pool[len(pool) * i // len(TIERS):len(pool) * (i + 1) // len(TIERS)] for i in range(len(TIERS))]


def best_lists(group, count, typical):
    """
    The most played archetypes of a group: (one list, number of decks of that archetype). The list is the best
    record's (the best-rated player's, the latest) or, for the lower tiers (typical), the list of the player rated
    closest to the group's median: a weak player's lucky 4-0 is not how they usually played.
    """
    by = collections.defaultdict(list)
    for x in group: by[x["arch"]].append(x)
    top = sorted(by.items(), key=lambda kv: (-len(kv[1]), kv[0]))[:count]
    median = group[len(group) // 2]["rating"]
    res = []
    for arch, xs in top:
        if typical: one = min(xs, key=lambda x: (abs(x["rating"] - median), x["date"] and -int(x["date"].replace("-", ""))))
        else: one = max(xs, key=lambda x: (x["deck"][3] - x["deck"][4], x["rating"], x["date"]))
        res.append((one, len(xs)))
    return res


def key_of(x):
    return "t9-%d" % x["deck"][7]


chosen, tier_refs = {}, {}
for name, band in zip(TIERS, bands):
    picks = best_lists(band, PER_TIER, name in ("Bronze", "Silver"))
    top = max(n for _, n in picks)
    tier_refs[name] = []
    for x, n in picks:
        chosen[key_of(x)] = x
        tier_refs[name].append((key_of(x), max(1, round(99 * n / top))))
picks = best_lists(pool, GAUNTLET, False)
top = max(n for _, n in picks)
tier_refs["Gauntlet"] = []
for x, n in picks:
    chosen[key_of(x)] = x
    tier_refs["Gauntlet"].append((key_of(x), max(1, round(99 * n / top))))


def shards(identity):
    return "/".join(SHARDS[c] for c in identity) or "Shardless"


with open(OUT, "w", encoding="utf-8", newline="\n") as f:
    f.write("# HEX Revival: decks played in HEX constructed tournaments, for the AI (generated by tools/gen_tournament_decks.py).\n")
    f.write("# Source: Hex Codex tournament database (hex-codex.romosjr.workers.dev/tournaments/), era %s, %d usable lists.\n" % (ERA, len(pool)))
    f.write("# Format: 'deck <key>|<champion>|<shards>|<wins>-<losses>|<date>|<event series>', then '<count> <card>' lines.\n")
    f.write("# 'tier <name>' lines list '<key> <frequency>' (the archetype's share of its band, 1-99).\n")
    for name in TIERS + ["Gauntlet"]:
        f.write("tier %s %s\n" % (name, " ".join("%s:%d" % kv for kv in tier_refs[name])))
    for k in sorted(chosen, key=lambda k: (chosen[k]["arch"], k)):
        x = chosen[k]; d = x["deck"]
        f.write("deck %s|%s|%s|%d-%d|%s|%s\n" % (k, x["arch"][0], shards(x["arch"][1]), d[3], d[4], x["date"], x["series"]))
        for i, n in x["main"]:
            f.write("%d %s\n" % (n, raw.get(i, cards[i]).strip()))
print(len(chosen), "decks ->", os.path.normpath(OUT))
