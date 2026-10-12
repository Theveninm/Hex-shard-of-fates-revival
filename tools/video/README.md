# Video analysis (research tool)

Extracts campaign rewards from gameplay videos the user already has, without anyone watching them:

1. `run_all.py`: for every video of the folder, one frame every 3 s (ffmpeg), then `scan.py`, then
   `events.py`; resumable, works within a time budget per run.
2. `scan.py`: OCR (tesseract) of the frames that changed, matched against the game's card, equipment
   and location names (`HexRevivalNames.txt` / `HexRevivalNodes.txt`, written by the dev commands
   `loot names` and `camp nodes`).
3. `events.py`: reward windows (cropped and read again) and Shroom Haus choices, with the names seen
   just before as context.

Needs ffmpeg, tesseract (eng), python3 with opencv and numpy. Videos are not part of the repository.

## Background analysis on Windows

Copy `analyser.bat`, `watch.py`, `scan.py`, `events.py` and the two name lists into a `_tools` folder
inside the video folder, then run `analyser.bat`: it installs Python, FFmpeg and Tesseract with winget
if needed, then analyses every finished video (and the ones downloaded later) into `_resultats`:
one `NNN.txt` per video, `resultats.txt` for all of them, `journal.txt` for progress.
