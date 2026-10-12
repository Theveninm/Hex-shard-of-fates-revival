#!/usr/bin/env python3
"""From scan.tsv + frames: reward windows (cropped, clean OCR) and Shroom Haus choices, with the last
map location seen before. usage: events.py <dir>   (dir has f/ and scan.tsv) -> prints events"""
import sys, os, re, subprocess, difflib, cv2, tempfile
TESS = os.environ.get('TESSERACT', 'tesseract')
TMP = os.path.join(tempfile.gettempdir(), 'hex_ocr_%d.png' % os.getpid())
d = sys.argv[1]; step = 3
here = os.path.dirname(os.path.abspath(__file__))
names = {}
for line in open(os.path.join(here, 'HexRevivalNames.txt'), encoding='utf-8', errors='ignore'):
    p = line.rstrip('\n').split('\t')
    if len(p) > 1 and (p[0] == 'item' or (len(p) > 4 and p[4] == 'own')): names[p[1].strip().lower()] = p[1].strip()
nodes = set(); titles = {}
for line in open(os.path.join(here, 'HexRevivalNodes.txt'), encoding='utf-8', errors='ignore'):
    p = line.split('\t')
    if len(p) > 3:
        nodes.add(p[1].strip())
        if p[2] != 'Encounter': continue
        for sc in p[3].replace('SCENES:', '').split('|'):
            if ' / ' in sc:
                ti = sc.split(' / ', 1)[1].strip()
                if len(ti) >= 5: titles[ti.lower()] = p[0] + ' ' + p[1].strip() + ' (' + ti + ')'
tkeys = list(titles.keys())
def ocr(img, crop):
    x0, y0, x1, y1 = crop
    g = cv2.cvtColor(img[y0:y1, x0:x1], cv2.COLOR_BGR2GRAY)
    g = 255 - cv2.resize(g, None, fx=2.5, fy=2.5, interpolation=cv2.INTER_CUBIC)
    cv2.imwrite(TMP, g)
    return subprocess.run([TESS, TMP, '-', '--psm', '6'], capture_output=True, text=True).stdout
def name(s):
    m = difflib.get_close_matches(s.lower().strip(' .:|'), list(names.keys()), n=1, cutoff=0.75)
    return names[m[0]] if m else None
rows = [l.rstrip('\n').split('\t') for l in open(os.path.join(d, 'scan.tsv'), encoding='utf-8')]
rows.sort(key=lambda r: int(r[0]))
loc = '?'; last_sig = None
from collections import Counter, deque
recent = deque(maxlen=80)
for r in rows:
    t = int(r[0]); found = r[1].split(';') if len(r) > 1 else []; raw = r[2] if len(r) > 2 else ''
    recent.extend(found)
    up = raw.upper()
    for seg in raw.split(' | '):
        m = difflib.get_close_matches(seg.lower(), tkeys, n=1, cutoff=0.85)
        if m: loc = titles[m[0]]
    img_path = os.path.join(d, 'f', '%05d.png' % (t // step + 1))
    if ('RECEIVED' in up or 'REWAR' in up) and os.path.exists(img_path):
        txt = ocr(cv2.imread(img_path), (560, 200, 845, 540))
        items = []
        lines = [l.strip() for l in txt.split('\n') if l.strip()]
        for i, l in enumerate(lines):
            m = re.search(r'(\d[\d,]*)\s*(G[O0E]L[A-Z0]|C[O0]L[A-Z0]|XP)', l.upper())
            if m: items.append(m.group(1) + ' ' + ('XP' if 'XP' in m.group(2) else 'gold')); continue
            if 'RECEIVED' in l.upper() and i + 1 < len(lines):
                n = name(lines[i + 1]); items.append('card ' + (n or '?' + lines[i + 1]))
            elif name(l) and 'RECEIVED' not in l.upper(): pass
        sig = '|'.join(items)
        if items and sig != last_sig: print('%d:%02d\t%s [%s]\tREWARDS\t%s' % (t // 60, t % 60, loc, ', '.join(n for n, c in Counter(recent).most_common(3)), '; '.join(items))); last_sig = sig
    if 'CHOOSE ONE CARD' in up or 'Shroomkin Haus' in found:
        cards = [f for f in found if f.lower() in names]
        if cards: print('%d:%02d\t%s\tHAUS\t%s' % (t // 60, t % 60, loc, ', '.join(cards)))
