#!/usr/bin/env python3
"""Background video analysis (Windows or Linux): watches a folder, analyses every finished video
(.mp4 without .part) once, writes _resultats/<NNN>.txt and _resultats/resultats.txt (all videos).

usage: python watch.py "<video folder>"     (Ctrl+C to stop; resumes where it left off)
Needs ffmpeg and tesseract (PATH or TESSERACT env var) and python packages opencv-python, numpy.
Name lists: HexRevivalNames.txt and HexRevivalNodes.txt next to this script."""
import os, sys, glob, time, shutil, subprocess, tempfile
here = os.path.dirname(os.path.abspath(__file__))
src = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(here)
res = os.path.join(src, '_resultats'); os.makedirs(res, exist_ok=True)
work = os.path.join(tempfile.gettempdir(), 'hex_video_work')
py = sys.executable

def log(m):
    line = time.strftime('%H:%M:%S ') + m
    print(line, flush=True)
    with open(os.path.join(res, 'journal.txt'), 'a', encoding='utf-8') as f: f.write(line + '\n')

def finished(v):
    # yt-dlp writes .part files while downloading; the final .mp4 only appears at the end.
    if os.path.exists(v + '.part'): return False
    s1 = os.path.getsize(v); time.sleep(2); return s1 == os.path.getsize(v) and s1 > 0

def process(v):
    key = os.path.basename(v)[:3]
    d = os.path.join(work, key); fdir = os.path.join(d, 'f')
    shutil.rmtree(d, ignore_errors=True); os.makedirs(fdir)
    log('video ' + os.path.basename(v) + ': frames')
    subprocess.run(['ffmpeg', '-loglevel', 'error', '-y', '-i', v, '-vf', 'fps=1/3', os.path.join(fdir, '%05d.png')], check=True)
    log('video ' + key + ': OCR')
    subprocess.run([py, os.path.join(here, 'scan.py'), fdir, os.path.join(d, 'scan.tsv'), '3', '1e9'], check=True)
    ev = subprocess.run([py, os.path.join(here, 'events.py'), d], capture_output=True, text=True).stdout
    with open(os.path.join(res, key + '.txt'), 'w', encoding='utf-8') as f: f.write(ev)
    shutil.copy(os.path.join(d, 'scan.tsv'), os.path.join(res, key + '_scan.tsv'))
    with open(os.path.join(res, 'resultats.txt'), 'a', encoding='utf-8') as f:
        f.write('=== ' + os.path.basename(v) + '\n' + ev)
    shutil.rmtree(d, ignore_errors=True)
    log('video ' + key + ': done, ' + str(len(ev.splitlines())) + ' event(s)')

log('watching ' + src)
while True:
    todo = [v for v in sorted(glob.glob(os.path.join(src, '*.mp4')))
            if not os.path.exists(os.path.join(res, os.path.basename(v)[:3] + '.txt'))]
    for v in todo:
        try:
            if finished(v): process(v)
        except Exception as e:
            log('error on ' + os.path.basename(v) + ': ' + str(e))
    time.sleep(60)
