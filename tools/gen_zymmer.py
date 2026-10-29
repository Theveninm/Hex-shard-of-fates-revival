#!/usr/bin/env python3
"""
Zymmer's notes -> data/zymmer_sources.tsv and mods/HexRevival/src/HexRevival/ZymmerData.cs.

"Zymmer's notes" (zymmerNotes.txt, a player's hand-made catalogue of HEX: every card, equipment and
mercenary with where it was obtained) are not in the repository. What the mod uses from them is the
"where obtained" line of each entry (Src.Zymmer): which pack, chest, wheel, arena or quest a card,
an equipment or a mercenary came from. Names are matched to the game's own templates (Data/gamedata)
so the generated table holds game ids; misspelled names are matched by similarity and listed.

Usage:
  tools/gen_zymmer.py <zymmerNotes.txt> <HEX>/Data/gamedata     # writes the TSV and the C# table
  tools/gen_zymmer.py --from-tsv                                 # C# table from the committed TSV only
"""
import collections, difflib, gzip, json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TSV = os.path.join(ROOT, "data", "zymmer_sources.tsv")
CS = os.path.join(ROOT, "mods", "HexRevival", "src", "HexRevival", "ZymmerData.cs")

PVP_SETS = ["Shards of Fate", "Shattered Destiny", "Armies of Myth", "Primal Dawn", "Herofall",
            "Scars of War", "Frostheart", "Dead of Winter", "Doombringer"]
SOURCE_RE = re.compile(r"^(Set \d Chest Lo(?:ot|ok)|Set \d Wheels? of Fate|.* Pack|.*Rewards?.*|AZ[12] .*|"
                       r"Frost Ring Arena.*|Core Commons|Kickstarter.*|Events - .*|Coins? of Kismet|"
                       + "|".join(map(re.escape, PVP_SETS)) + r")$")
SLOTS = ("Head", "Chest", "Gloves", "Feet", "Weapon", "Trinket")
# Typos of the notes' source lines.
SOURCE_FIX = {"Set 9 Chest Look": "Set 9 Chest Loot", "Frost Ring Arena Reward": "Frost Ring Arena Rewards",
              "Coins of Kismet": "Coin of Kismet", "Kickstarter Rewards (also Spectral Acorn)": "Kickstarter Rewards"}


def ind(l):
    return len(l) - len(l.lstrip("\t"))


def parse_notes(path):
    lines = open(path, encoding="utf8").read().replace("\r\n", "\n").replace("\r", "\n").split("\n")
    start = next(i for i, l in enumerate(lines) if l.strip() == "Troops:" and ind(l) == 0)
    head = re.compile(r"^(\t+)([^\t].*):\s*$")
    section, out, last = None, [], {}
    for i in range(start, len(lines)):
        l = lines[i]
        if l and ind(l) == 0 and l.endswith(":"):
            section = l[:-1]
            continue
        m = head.match(l)
        if not m:
            continue
        d, name = len(m.group(1)), m.group(2).strip()
        last[d] = name
        fields, j = [], i + 1
        while j < len(lines) and (not lines[j].strip() or ind(lines[j]) > d):
            s = lines[j]
            if s.strip() and ind(s) == d + 1 and not head.match(s):
                fields.append(s.strip())
            if not s.strip() and j + 1 < len(lines) and lines[j + 1].strip() and ind(lines[j + 1]) <= d:
                break
            j += 1
        source, slot, rarity = None, "", ""
        for f in fields[:6]:
            g = re.sub(r"^Unique\t+", "", f)
            if SOURCE_RE.match(g) and len(g) < 60:
                source = SOURCE_FIX.get(g, g)
                break
        for f in fields[:4]:
            mm = re.match(r"^(%s)\t+(\w+)" % "|".join(SLOTS), f)
            if mm:
                slot, rarity = mm.groups()
            mm = re.search(r"\b(Common|Uncommon|Rare|Legendary|Epic|Boss|Uncollectable)\b", f)
            if mm and not rarity:
                rarity = mm.group(1)
        if not source:
            continue
        kind = "e" if slot else ("m" if section == "Mercenary" else "c")
        # Equipment is listed under the card it equips (its "parent").
        parent = last.get(d - 1, "") if kind == "e" else ""
        out.append(dict(name=name, kind=kind, slot=slot, rarity=rarity, source=source, line=i + 1, parent=parent))
    return out


def load_gamedata(path):
    txt = gzip.open(path, "rt", encoding="utf8", errors="replace").read()
    tc, bk = re.compile(r",(\s*[}\]])"), re.compile(r'([{,\n]\s*)(-?[A-Za-z0-9_]+)(\s*):')

    def load(b):
        try:
            return json.loads(b, strict=False)
        except Exception:
            return json.loads(bk.sub(r'\1"\2"\3:', tc.sub(r"\1", b)), strict=False)

    cards, items, sets = [], [], {}
    for sec in txt.split("$$$---$$$\n"):
        h, _, rest = sec.partition("\n")
        h = h.strip()
        if h not in ("CardTemplate", "InventoryItemData", "CardSetTemplate"):
            continue
        for body in rest.split("$$--$$\n"):
            if not body.strip():
                continue
            try:
                o = load(body)
            except Exception:
                continue
            if h == "CardSetTemplate":
                sets[o["m_Id"]["m_Guid"]] = o["m_Name"]
            elif h == "CardTemplate":
                cards.append(o)
            elif o.get("_t", "").endswith(("EquipmentData", "MercenaryData")):
                items.append(o)
    return cards, items, sets


def preferred_sets(source):
    """Card sets to prefer, in order, when a card name exists in several sets."""
    m = re.match(r"Set (\d)", source)
    if m:
        n = int(m.group(1))
        return ["Set%02d_PvE_Promo" % n, "Set%02d_PvE_Talents" % n, "Set%02d_PvE_Holiday" % n, PVP_SETS[n - 1]]
    if source.startswith("Frost Ring Arena"):
        return ["Set01_PvE_Arena"]
    if source.startswith(("AZ2", "Alachian", "Brutecrown", "Great Machine", "Kraken", "Ruins of Kukatan")):
        return ["AZ2"]
    if source.startswith(("AZ1", "Howling", "Crayburn", "Tranquil", "Fort Romor", "The Usurper", "Smoldering", "Devonshire")):
        return ["AZ1"]
    if source.startswith("Kismet"):
        return ["Set06_PvE_Promo", "Set07_PvE_Promo"]
    if source.startswith("Kickstarter"):
        return ["Set01_Kickstarter"]
    if source in PVP_SETS:
        return [source]
    return []


def resolve(entries, cards, items, sets):
    def key(s):
        return re.sub(r"[^a-z0-9]", "", s.lower().replace("’", "'"))

    by_card, by_item = collections.defaultdict(list), collections.defaultdict(list)
    for c in cards:
        if c.get("m_Name"):
            by_card[key(c["m_Name"])].append(c)
    for o in items:
        if o.get("m_Name"):
            by_item[key(o["m_Name"])].append(o)
    card_keys, item_keys = list(by_card), list(by_item)
    fuzzy = []
    for e in entries:
        pool, keys = (by_item, item_keys) if e["kind"] in "em" else (by_card, card_keys)
        k = key(e["name"])
        cands = pool.get(k)
        if not cands:
            close = difflib.get_close_matches(k, keys, n=1, cutoff=0.88)
            if close:
                cands = pool[close[0]]
                fuzzy.append((e["name"], cands[0]["m_Name"]))
        if e["kind"] == "m" and cands:
            cands = [o for o in cands if o.get("_t", "").endswith("MercenaryData")] or cands
        if e["kind"] == "e" and cands:
            cands = [o for o in cands if o.get("_t", "").endswith("EquipmentData")] or cands
        if not cands:
            e["id"], e["game"] = "", ""
            continue
        pref = preferred_sets(e["source"])

        def rank(o):
            s = sets.get(o.get("m_SetId", {}).get("m_Guid", ""), "")
            alt = "alternates" in s or s in ("AI Only Cards", "PvE01_AZ_1_NPCs", "AZ2 NPCs")
            return (pref.index(s) if s in pref else len(pref), alt)

        best = sorted(cands, key=rank)[0]
        e["id"], e["game"] = best["m_Id"]["m_Guid"], best["m_Name"].strip()
    return fuzzy


def write_tsv(entries):
    with open(TSV, "w", encoding="utf8", newline="\n") as f:
        f.write("source\tkind\tname\tslot\trarity\tgame_id\tgame_name\tparent\tnotes_line\n")
        for e in entries:
            f.write("\t".join([e["source"], e["kind"], e["name"], e["slot"], e["rarity"], e["id"], e["game"], e["parent"], str(e["line"])]) + "\n")


def read_tsv():
    rows = []
    with open(TSV, encoding="utf8") as f:
        hdr = f.readline().rstrip("\n").split("\t")
        for l in f:
            rows.append(dict(zip(hdr, l.rstrip("\n").split("\t"))))
    return rows


def esc(s):
    return s.replace("\\", "\\\\").replace('"', '\\"')


# Sources the mod draws from (the TSV keeps everything; the C# table only these).
USED = re.compile(r"^(Set \d (Chest Loot|Wheels of Fate)|.* Pack|Frost Ring Arena.*|AZ[12] .*)$")


def write_cs(rows):
    by = collections.OrderedDict()
    for r in rows:
        if r["game_id"] and USED.match(r["source"]):
            by.setdefault(r["source"], []).append(r)
    with open(CS, "w", encoding="utf8", newline="\n") as f:
        f.write("// Generated by tools/gen_zymmer.py from data/zymmer_sources.tsv (Zymmer's notes). Do not edit by hand.\n")
        f.write("namespace HexRevival\n{\n    internal static class ZymmerData\n    {\n")
        f.write("        /// <summary>Source (where the notes say it was obtained), then per entry: kind (c card, e equipment, m mercenary), game id, game name,\n")
        f.write("        /// parent (for equipment: the card it equips, as named in the notes).</summary>\n")
        f.write("        public static readonly string[][] Sources =\n        {\n")
        for src, l in by.items():
            parts = ['"%s"' % esc(src)]
            for r in l:
                parts += ['"%s"' % r["kind"], '"%s"' % r["game_id"], '"%s"' % esc(r["game_name"]), '"%s"' % esc(r["parent"])]
            f.write("            new[] { " + ", ".join(parts) + " },\n")
        f.write("        };\n    }\n}\n")


def main():
    if len(sys.argv) == 2 and sys.argv[1] == "--from-tsv":
        write_cs(read_tsv())
        return
    if len(sys.argv) != 3:
        print(__doc__)
        sys.exit(1)
    entries = parse_notes(sys.argv[1])
    cards, items, sets = load_gamedata(sys.argv[2])
    fuzzy = resolve(entries, cards, items, sets)
    write_tsv(entries)
    write_cs(read_tsv())
    missing = [e for e in entries if not e["id"]]
    print("%d entries, %d matched by similarity, %d not in the game data" % (len(entries), len(fuzzy), len(missing)))
    for a, b in fuzzy:
        print("  ~ %s -> %s" % (a, b))
    for e in missing:
        print("  ? %s (%s, %s, line %d)" % (e["name"], e["kind"], e["source"], e["line"]))


if __name__ == "__main__":
    main()
