@echo off
chcp 65001 >nul
title Analyse des videos HEX
cd /d "%~dp0"
set "PATH=%PATH%;%LOCALAPPDATA%\Microsoft\WinGet\Links"

where py >nul 2>&1 || (
  echo Installation de Python...
  winget install -e --id Python.Python.3.12 --accept-source-agreements --accept-package-agreements
)
where ffmpeg >nul 2>&1 || (
  echo Installation de FFmpeg...
  winget install -e --id Gyan.FFmpeg --accept-source-agreements --accept-package-agreements
)
if not exist "%ProgramFiles%\Tesseract-OCR\tesseract.exe" (
  echo Installation de Tesseract OCR...
  winget install -e --id UB-Mannheim.TesseractOCR --accept-source-agreements --accept-package-agreements
)
set "PATH=%PATH%;%LOCALAPPDATA%\Microsoft\WinGet\Links"
set "TESSERACT=%ProgramFiles%\Tesseract-OCR\tesseract.exe"

py -m pip install --quiet --disable-pip-version-check opencv-python numpy

echo.
echo Analyse en continu des videos du dossier parent (resultats dans _resultats).
echo Laisse cette fenetre ouverte ; ferme-la pour arreter, l'analyse reprendra ou elle en etait.
py "%~dp0watch.py" "%~dp0.."
pause
