# Controledu ML Starter Kit

This folder contains beginner-friendly scripts to train and export AI UI detectors for Controledu.

## Goal

1. Train binary classifier (`ai_ui` vs `not_ai_ui`)
2. Train multiclass classifier (`chatgpt_ui`, `claude_ui`, ...)
3. Export to ONNX for `Student.Agent`

## Prerequisites

- Python 3.10+
- `pip install -r ml/requirements.txt`
- Dataset prepared as documented in `docs/ml/02-data-collection.md`

## Quick start

```bash
copy ml\config.example.yaml ml\config.yaml
python ml/prepare_labels.py --config ml/config.yaml
python ml/train_binary.py --config ml/config.yaml
python ml/train_multiclass.py --config ml/config.yaml
python ml/eval.py --config ml/config.yaml --task binary
python ml/eval.py --config ml/config.yaml --task multiclass
python ml/export_onnx.py --config ml/config.yaml --task binary
python ml/export_onnx.py --config ml/config.yaml --task multiclass
```

## Output artifacts

Default folder: `ml/artifacts/`

- `binary-best.pt`
- `multiclass-best.pt`
- `ai-ui-binary.onnx`
- `ai-ui-multiclass.onnx`
- metrics json files

## No dataset yet?

Scripts intentionally stop with a clear error and links to:

- `docs/ml/02-data-collection.md`
- `docs/ml/03-labeling-guide.md`
- `docs/ml/dataset-template/*`

## Label prep helper

Use the helper to avoid manual CSV/split setup:

```bash
python ml/prepare_labels.py --config ml/config.yaml
```

What it does:

1. Scans `dataset/raw/**/*` images
2. Creates/updates `dataset/labels/classification.csv`
3. Creates `dataset/labels/review_queue.csv` with metadata-based label suggestions
4. Regenerates `dataset/splits/train.txt`, `val.txt`, `test.txt` from labeled rows

Useful options:

```bash
# Pre-fill new rows as binary negative baseline
python ml/prepare_labels.py --config ml/config.yaml --default-label not_ai_ui

# Keep CSV but drop rows for missing files
python ml/prepare_labels.py --config ml/config.yaml --drop-missing-files

# Use non-stratified random split
python ml/prepare_labels.py --config ml/config.yaml --no-stratified

# Auto-apply confident suggestions only to empty labels
python ml/prepare_labels.py --config ml/config.yaml --auto-apply-suggestions --suggestion-min-confidence 0.80

# Aggressive mode: overwrite existing labels too
python ml/prepare_labels.py --config ml/config.yaml --auto-apply-suggestions --suggestion-apply-mode all --suggestion-min-confidence 0.90
```

## Synthetic data generator

If manual labeling is slow, use browser-rendered synthetic data (Selenium + live webpages + artifacts).
The generator now supports:
- `live`: screenshots of real public pages from URL catalog.
- `template`: fully synthetic HTML renderer (offline-safe fallback).
- `hybrid`: tries real pages first, then falls back to templates.

1. Install dependencies:

```bash
pip install -r ml/requirements.txt
```

2. Generate screenshots (recommended hybrid mode):

```bash
# 2000 screenshots (50% ai / 50% not_ai), real pages first + artifact overlays
python ml/generate_synthetic_browser_dataset.py --dataset-root dataset --count 2000 --ai-ratio 0.5 --hard-negative-ratio 0.35 --page-source hybrid --real-pages-config ml/real_pages_catalog.json --prefix synthetic-browser-v2

# strict real-pages mode (no template fallback)
python ml/generate_synthetic_browser_dataset.py --dataset-root dataset --count 1000 --page-source live --real-pages-config ml/real_pages_catalog.json --max-attempts-multiplier 10 --prefix synthetic-browser-live-v2

# data-driven sampling (fit synthetic AI label distribution from your dataset labels)
python ml/generate_synthetic_browser_dataset.py --dataset-root dataset --count 1500 --ai-ratio 0.6 --page-source hybrid --ai-label-policy dataset

# boost rare classes (Perplexity/DeepSeek/Grok/Qwen/Mistral/Meta AI and others)
python ml/generate_synthetic_browser_dataset.py --dataset-root dataset --count 1500 --ai-ratio 0.7 --page-source hybrid --ai-label-policy inverse-frequency --dataset-label-floor 0.8
```

3. Regenerate splits and train:

```bash
python ml/prepare_labels.py --config ml/config.yaml
python ml/train_binary.py --config ml/config.yaml
python ml/eval.py --config ml/config.yaml --task binary
```

Notes:

- Edit `ml/real_pages_catalog.json` to add/remove real URLs for your locale.
- `--hard-negative-ratio` adds non-AI pages that still contain AI terms in text/title (e.g. wiki/docs/news).
- `--ai-label-policy dataset` uses your `dataset/labels/classification.csv` class distribution.
- `--ai-label-policy inverse-frequency` oversamples rare classes from your dataset.
- Artifact simulation includes blur/noise/jpeg degradation/scanlines/vignette/cursor/popup overlays.
- Keep your real captured dataset too; synthetic data should augment, not replace, real samples.
- Legacy generator is still available: `ml/generate_synthetic_dataset.py`.

## Realtime OCR collector (adaptive FPS)

If CAPTCHA blocks live web capture, use desktop recording + OCR evidence:

- Captures screen in realtime.
- Runs OCR and searches AI UI keywords (`chatgpt`, `claude`, `gemini`, etc.).
- Raises capture rate from base FPS to high FPS on suspicion.
- Saves artifact-augmented frames + JSON metadata and appends labels to `dataset/labels/classification.csv`.
- Can later fuse OCR score with your trained ONNX model.

Basic run (realtime window + adaptive 4 -> 8 FPS):

```bash
python ml/generate_realtime_text_dataset.py --dataset-root dataset --preview --base-fps 4 --high-fps 8 --label-threshold 0.20
```

Faster mode when OCR is heavy:

```bash
python ml/generate_realtime_text_dataset.py --dataset-root dataset --preview --base-fps 4 --high-fps 8 --ocr-every-n-frames 2 --ocr-max-width 960
```

GPU OCR (if CUDA is available):

```bash
python ml/generate_realtime_text_dataset.py --dataset-root dataset --preview --ocr-gpu
```

Note:
- Script now auto-enables GPU OCR when CUDA is detected.
- If CUDA is not detected, it falls back to CPU OCR.

Region-only capture (faster):

```bash
python ml/generate_realtime_text_dataset.py --dataset-root dataset --preview --capture-left 0 --capture-top 0 --capture-width 1920 --capture-height 1080
```

Fuse OCR with your ONNX model later:

```bash
python ml/generate_realtime_text_dataset.py --dataset-root dataset --preview --onnx-model ml/artifacts/ai-ui-binary.onnx --onnx-labels ml/artifacts/binary-labels.json --text-weight 0.65 --model-weight 0.35
```
