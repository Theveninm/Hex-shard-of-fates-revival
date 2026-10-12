#!/usr/bin/env python3
"""Resumable frame scanner: OCR of changed frames (preprocessed), matched against game names.
usage: scan.py <framedir> <out.tsv> <step_seconds> [max_seconds_of_work]
Output lines: time<TAB>matched names (;)<TAB>raw text (shortened)"""
import sys, os, glob, subprocess, time, re, difflib
import cv2, numpy as np
fd, out, step = sys.argv[1], sys.argv[2], int(sys.argv[3])
budget = float(sys.argv[4]) if len(sys.argv) > 4 else 160
t0 = time.time()
names = set()
for line in open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'HexRevivalNames.txt'), encoding='utf-8', errors='ignore'):
    p = line.rstrip('\n').split('\t')
    if len(p) > 1 and (p[0] == 'item' or (len(p) > 4 and p[4] == 'own')): names.add(p[1].strip())
for line in open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'HexRevivalNodes.txt'), encoding='utf-8', errors='ignore'):
    p = line.split('\t')
    if len(p) > 1: names.add(p[1].strip())
names |= {'Shroomkin Haus', 'Choose one card to add to your collection', 'Rewards', 'Loot', 'Victory', 'Defeat', 'Gold', 'Experience'}
low = {n.lower(): n for n in names if len(n) >= 5}
keys = list(low.keys())
done = set()
if os.path.exists(out):
    for l in open(out, encoding='utf-8'): done.add(l.split('\t')[0])
frames = sorted(glob.glob(os.path.join(fd, '*.png')))
prev = None
with open(out, 'a', encoding='utf-8') as f:
    for img in frames:
        n = int(os.path.basename(img)[:-4]); t = str((n - 1) * step)
        im = cv2.imread(img)
        thumb = cv2.resize(cv2.cvtColor(im, cv2.COLOR_BGR2GRAY), (64, 36)).astype(np.int16)
        changed = prev is None or np.abs(thumb - prev).mean() > 6
        prev = thumb
        if t in done: continue
        if time.time() - t0 > budget: sys.exit(3)
        if not changed:
            f.write(t + '\t=\t\n'); continue
        g = cv2.cvtColor(im, cv2.COLOR_BGR2GRAY)
        g = cv2.resize(g, None, fx=1.6, fy=1.6, interpolation=cv2.INTER_CUBIC)
        g = 255 - g
        cv2.imwrite('/tmp/scan.png', g)
        txt = subprocess.run(['tesseract', '/tmp/scan.png', '-', '--psm', '11'], capture_output=True, text=True).stdout
        lines = [re.sub(r'\s+', ' ', l).strip() for l in txt.split('\n') if len(l.strip()) >= 4]
        found = []
        cands = set(lines)
        for i in range(len(lines) - 1): cands.add(lines[i] + ' ' + lines[i + 1])
        for c in cands:
            m = difflib.get_close_matches(c.lower(), keys, n=1, cutoff=0.86)
            if m and low[m[0]] not in found: found.append(low[m[0]])
        f.write(t + '\t' + ';'.join(found) + '\t' + ' | '.join(lines)[:300] + '\n')
print('complete')
