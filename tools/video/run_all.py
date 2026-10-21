#!/usr/bin/env python3
"""Processes every video of the folder within a time budget (resumable): frames, scan, events."""
import os, glob, subprocess, time, sys
t0 = time.time(); budget = 160
src = os.path.expanduser('~/mnt/Hex Shards Of Fate'); here = os.path.dirname(os.path.abspath(__file__))
for v in sorted(glob.glob(os.path.join(src, '*.mp4'))):
    key = os.path.basename(v)[:3]; d = os.path.join(here, key)
    if os.path.exists(os.path.join(d, 'events.txt')): continue
    os.makedirs(os.path.join(d, 'f'), exist_ok=True)
    if not os.path.exists(os.path.join(d, 'frames_ok')):
        subprocess.run(['ffmpeg', '-loglevel', 'error', '-y', '-i', v, '-vf', 'fps=1/3', os.path.join(d, 'f', '%05d.png')], check=True)
        open(os.path.join(d, 'frames_ok'), 'w').close()
    left = budget - (time.time() - t0)
    if left < 15: print('budget'); sys.exit(0)
    r = subprocess.run(['python3', os.path.join(here, 'scan.py'), os.path.join(d, 'f'), os.path.join(d, 'scan.tsv'), '3', str(left - 10)])
    if r.returncode != 0: print('partial', key); sys.exit(0)
    ev = subprocess.run(['python3', os.path.join(here, 'events.py'), d], capture_output=True, text=True).stdout
    open(os.path.join(d, 'events.txt'), 'w').write(ev)
    for f in glob.glob(os.path.join(d, 'f', '*.png')): os.remove(f)
    print('done', key)
print('all done' if time.time() - t0 < budget else '')
