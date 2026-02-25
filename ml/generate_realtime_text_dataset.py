from __future__ import annotations

import argparse
import json
import math
import random
import re
import time
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import numpy as np
import pandas as pd
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageGrab

try:
    import onnxruntime as ort
except ImportError:
    ort = None


AI_LABELS = [
    "chatgpt_ui",
    "claude_ui",
    "gemini_ui",
    "copilot_ui",
    "perplexity_ui",
    "deepseek_ui",
    "poe_ui",
    "grok_ui",
    "qwen_ui",
    "mistral_ui",
    "meta_ai_ui",
    "ai_ui",
]
NOT_AI_LABEL = "not_ai_ui"

LABEL_KEYWORDS: dict[str, tuple[str, ...]] = {
    "chatgpt_ui": ("chatgpt", "openai", "gpt-4", "gpt4", "chat.openai"),
    "claude_ui": ("claude", "anthropic", "claude.ai"),
    "gemini_ui": ("gemini", "bard", "deepmind", "gemini.google"),
    "copilot_ui": ("copilot", "microsoft copilot", "bing chat"),
    "perplexity_ui": ("perplexity", "perplexity.ai"),
    "deepseek_ui": ("deepseek", "chat.deepseek"),
    "poe_ui": ("poe.com", "poe by quora", "poe"),
    "grok_ui": ("grok", "xai", "grok.com"),
    "qwen_ui": ("qwen", "tongyi", "chat.qwen.ai"),
    "mistral_ui": ("mistral", "le chat", "chat.mistral.ai"),
    "meta_ai_ui": ("meta ai", "meta.ai", "llama"),
    "ai_ui": ("assistant", "chatbot", "language model", "llm", "prompt"),
}

LABEL_COLORS: dict[str, tuple[int, int, int]] = {
    "chatgpt_ui": (34, 197, 94),
    "claude_ui": (245, 158, 11),
    "gemini_ui": (59, 130, 246),
    "copilot_ui": (14, 165, 233),
    "perplexity_ui": (20, 184, 166),
    "deepseek_ui": (168, 85, 247),
    "poe_ui": (236, 72, 153),
    "grok_ui": (244, 63, 94),
    "qwen_ui": (99, 102, 241),
    "mistral_ui": (249, 115, 22),
    "meta_ai_ui": (16, 185, 129),
    "ai_ui": (234, 179, 8),
}

_ALNUM_RE = re.compile(r"[^0-9a-z]+", flags=re.IGNORECASE)


@dataclass(frozen=True)
class OcrHit:
    text: str
    confidence: float
    bbox: tuple[int, int, int, int]


@dataclass(frozen=True)
class MatchedHit:
    text: str
    confidence: float
    bbox: tuple[int, int, int, int]
    label: str
    keyword: str
    score: float


class OnnxModelScorer:
    def __init__(self, model_path: Path, labels_path: Path | None, image_size: int, provider: str) -> None:
        if ort is None:
            raise RuntimeError("onnxruntime is not installed. Run: pip install onnxruntime")
        self._image_size = int(image_size)
        self._session = ort.InferenceSession(str(model_path), providers=[provider])
        self._input_name = self._session.get_inputs()[0].name
        self._labels = self._load_labels(labels_path, expected_count=self._output_count())

    def _output_count(self) -> int:
        output = self._session.get_outputs()[0]
        shape = output.shape
        if isinstance(shape, list) and len(shape) >= 2 and isinstance(shape[1], int):
            return int(shape[1])
        return 0

    def _load_labels(self, labels_path: Path | None, expected_count: int) -> list[str]:
        if labels_path is not None and labels_path.exists():
            data = json.loads(labels_path.read_text(encoding="utf-8"))
            if isinstance(data, dict):
                labels = data.get("labels", [])
            else:
                labels = data
            if not isinstance(labels, list) or not labels:
                raise ValueError(f"Invalid labels file format: {labels_path}")
            normalized = [normalize_label(str(item)) for item in labels]
            return normalized
        if expected_count == 2:
            return [NOT_AI_LABEL, "ai_ui"]
        return [f"class_{index}" for index in range(max(1, expected_count))]

    def predict(self, image: Image.Image) -> dict[str, float]:
        resized = image.resize((self._image_size, self._image_size), Image.Resampling.BILINEAR).convert("RGB")
        array = np.asarray(resized, dtype=np.float32) / 255.0
        mean = np.array([0.485, 0.456, 0.406], dtype=np.float32)
        std = np.array([0.229, 0.224, 0.225], dtype=np.float32)
        array = (array - mean) / std
        tensor = np.transpose(array, (2, 0, 1))[None, :, :, :]

        raw_output = self._session.run(None, {self._input_name: tensor})[0]
        logits = np.asarray(raw_output, dtype=np.float32)
        if logits.ndim == 2:
            logits = logits[0]
        if logits.size == 1:
            positive = 1.0 / (1.0 + math.exp(-float(logits[0])))
            return {NOT_AI_LABEL: float(1.0 - positive), "ai_ui": float(positive)}
        logits = logits - np.max(logits)
        probs = np.exp(logits)
        probs = probs / np.sum(probs)

        score_map: dict[str, float] = {}
        for index, value in enumerate(probs.tolist()):
            label = self._labels[index] if index < len(self._labels) else f"class_{index}"
            canonical = normalize_label(label)
            score_map[canonical] = max(score_map.get(canonical, 0.0), float(value))
        return score_map


class PreviewWindow:
    def __init__(self, enabled: bool, max_width: int) -> None:
        self._enabled = enabled
        self._max_width = max_width
        self._closed = False
        self._image_label = None
        self._tk = None
        self._image_tk = None
        if not enabled:
            return
        try:
            import tkinter as tk
            from PIL import ImageTk
        except Exception as exc:
            print(f"[preview] disabled: {exc}")
            self._enabled = False
            return
        self._tk = tk.Tk()
        self._tk.title("Controledu Realtime OCR Generator")
        self._tk.protocol("WM_DELETE_WINDOW", self._on_close)
        self._image_label = tk.Label(self._tk)
        self._image_label.pack(fill=tk.BOTH, expand=True)
        self._ImageTk = ImageTk

    def _on_close(self) -> None:
        self._closed = True
        if self._tk is not None:
            self._tk.destroy()

    @property
    def closed(self) -> bool:
        return self._closed

    def update(self, image: Image.Image) -> None:
        if not self._enabled or self._closed or self._tk is None or self._image_label is None:
            return
        preview = image
        if self._max_width > 0 and preview.width > self._max_width:
            scale = self._max_width / float(preview.width)
            preview = preview.resize((self._max_width, max(1, int(preview.height * scale))), Image.Resampling.BILINEAR)
        self._image_tk = self._ImageTk.PhotoImage(preview)
        self._image_label.configure(image=self._image_tk)
        self._tk.update_idletasks()
        self._tk.update()

    def close(self) -> None:
        if self._tk is not None and not self._closed:
            self._tk.destroy()
            self._closed = True


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Capture realtime desktop frames, detect AI keywords with OCR, apply adaptive FPS (4->8), "
            "and build dataset samples for later model training."
        )
    )
    parser.add_argument("--dataset-root", default="dataset")
    parser.add_argument("--prefix", default="realtime-text-ocr")
    parser.add_argument("--duration-seconds", type=int, default=0, help="0 means run until Ctrl+C or window close.")
    parser.add_argument("--max-frames", type=int, default=0, help="0 means unlimited.")
    parser.add_argument("--base-fps", type=float, default=4.0)
    parser.add_argument("--high-fps", type=float, default=8.0)
    parser.add_argument("--high-fps-trigger", type=float, default=0.20)
    parser.add_argument("--high-fps-return-threshold", type=float, default=0.12)
    parser.add_argument("--high-fps-hold-seconds", type=float, default=3.0)
    parser.add_argument("--label-threshold", type=float, default=0.20)
    parser.add_argument("--save-mode", choices=["all", "ai-only"], default="all")
    parser.add_argument("--ocr-langs", default="en", help="Comma-separated EasyOCR languages, e.g. en,ru")
    parser.add_argument("--ocr-gpu", action="store_true", help="Force GPU OCR (requires CUDA-enabled torch).")
    parser.add_argument("--ocr-force-cpu", action="store_true", help="Disable auto GPU usage even if CUDA is available.")
    parser.add_argument("--ocr-every-n-frames", type=int, default=1, help="Run OCR every N frames and reuse last OCR result in-between.")
    parser.add_argument("--ocr-interval-seconds", type=float, default=5.0, help="Run OCR no more than once per interval (seconds).")
    parser.add_argument("--ocr-max-width", type=int, default=1280)
    parser.add_argument("--keyword-gain", type=float, default=0.70)
    parser.add_argument("--ema-decay", type=float, default=0.65)
    parser.add_argument("--capture-left", type=int, default=0)
    parser.add_argument("--capture-top", type=int, default=0)
    parser.add_argument("--capture-width", type=int, default=0, help="0 means full desktop.")
    parser.add_argument("--capture-height", type=int, default=0, help="0 means full desktop.")
    parser.add_argument("--all-screens", action="store_true")
    parser.add_argument("--resolution-scale-min", type=float, default=0.55)
    parser.add_argument("--resolution-scale-max", type=float, default=1.0)
    parser.add_argument("--jpeg-quality-min", type=int, default=45)
    parser.add_argument("--jpeg-quality-max", type=int, default=92)
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--preview", action="store_true", help="Show realtime overlay window.")
    parser.add_argument("--preview-max-width", type=int, default=1440)
    parser.add_argument("--onnx-model", default="", help="Optional ONNX model path for score fusion.")
    parser.add_argument("--onnx-labels", default="", help="Optional JSON labels file for ONNX model.")
    parser.add_argument("--onnx-image-size", type=int, default=224)
    parser.add_argument("--onnx-provider", default="CPUExecutionProvider")
    parser.add_argument("--text-weight", type=float, default=0.75)
    parser.add_argument("--model-weight", type=float, default=0.25)
    return parser.parse_args()


def normalize_text(value: str) -> str:
    lowered = value.lower().strip()
    return _ALNUM_RE.sub(" ", lowered).strip()


def normalize_label(value: str) -> str:
    raw = value.strip().lower()
    mapping = {
        "chatgpt": "chatgpt_ui",
        "claude": "claude_ui",
        "gemini": "gemini_ui",
        "copilot": "copilot_ui",
        "perplexity": "perplexity_ui",
        "deepseek": "deepseek_ui",
        "poe": "poe_ui",
        "grok": "grok_ui",
        "qwen": "qwen_ui",
        "mistral": "mistral_ui",
        "meta ai": "meta_ai_ui",
        "not_ai": NOT_AI_LABEL,
        "none": NOT_AI_LABEL,
        "non_ai": NOT_AI_LABEL,
        "not ai": NOT_AI_LABEL,
        "ai": "ai_ui",
    }
    return mapping.get(raw, raw)


def grab_frame(args: argparse.Namespace) -> Image.Image:
    bbox = None
    if args.capture_width > 0 and args.capture_height > 0:
        bbox = (
            args.capture_left,
            args.capture_top,
            args.capture_left + args.capture_width,
            args.capture_top + args.capture_height,
        )
    frame = ImageGrab.grab(bbox=bbox, all_screens=bool(args.all_screens))
    return frame.convert("RGB")


def parse_ocr_hits(raw: list[Any]) -> list[OcrHit]:
    hits: list[OcrHit] = []
    for item in raw:
        if not isinstance(item, (list, tuple)) or len(item) < 3:
            continue
        bbox_raw, text_raw, conf_raw = item[0], item[1], item[2]
        text = str(text_raw).strip()
        if not text:
            continue
        confidence = float(conf_raw) if isinstance(conf_raw, (float, int)) else 0.0
        confidence = max(0.0, min(1.0, confidence))
        try:
            points = [(int(point[0]), int(point[1])) for point in bbox_raw]
        except Exception:
            continue
        xs = [point[0] for point in points]
        ys = [point[1] for point in points]
        bbox = (min(xs), min(ys), max(xs), max(ys))
        hits.append(OcrHit(text=text, confidence=confidence, bbox=bbox))
    return hits


def score_text_hits(hits: list[OcrHit], keyword_gain: float) -> tuple[dict[str, float], list[MatchedHit]]:
    raw_scores: dict[str, float] = {label: 0.0 for label in AI_LABELS}
    matched: list[MatchedHit] = []

    for hit in hits:
        normalized = normalize_text(hit.text)
        if not normalized:
            continue
        best_match: MatchedHit | None = None
        for label, keywords in LABEL_KEYWORDS.items():
            for keyword in keywords:
                key_norm = normalize_text(keyword)
                if not key_norm:
                    continue
                if key_norm in normalized:
                    weight = min(1.0, 0.35 + len(key_norm) / 16.0)
                    score = hit.confidence * weight
                    raw_scores[label] += score
                    entry = MatchedHit(
                        text=hit.text,
                        confidence=hit.confidence,
                        bbox=hit.bbox,
                        label=label,
                        keyword=keyword,
                        score=score,
                    )
                    if best_match is None or entry.score > best_match.score:
                        best_match = entry
        if best_match is not None:
            matched.append(best_match)

    text_scores: dict[str, float] = {}
    for label in AI_LABELS:
        raw = max(0.0, raw_scores.get(label, 0.0))
        text_scores[label] = float(1.0 - math.exp(-raw * max(0.05, keyword_gain)))
    max_ai = max(text_scores.values()) if text_scores else 0.0
    text_scores[NOT_AI_LABEL] = max(0.0, 1.0 - max_ai)
    return text_scores, matched


def smooth_scores(previous: dict[str, float], current: dict[str, float], decay: float) -> dict[str, float]:
    decay = min(max(decay, 0.0), 0.99)
    labels = set(previous) | set(current)
    result: dict[str, float] = {}
    for label in labels:
        prev = float(previous.get(label, 0.0))
        cur = float(current.get(label, 0.0))
        result[label] = prev * decay + cur * (1.0 - decay)
    return result


def fuse_scores(text_scores: dict[str, float], model_scores: dict[str, float], text_weight: float, model_weight: float) -> dict[str, float]:
    text_weight = max(0.0, text_weight)
    model_weight = max(0.0, model_weight)
    total = text_weight + model_weight
    if total <= 0:
        total = 1.0
        text_weight = 1.0
        model_weight = 0.0
    labels = set(text_scores) | set(model_scores)
    fused: dict[str, float] = {}
    for label in labels:
        fused[label] = (
            text_scores.get(label, 0.0) * text_weight + model_scores.get(label, 0.0) * model_weight
        ) / total
    return fused


def decide_label(scores: dict[str, float], label_threshold: float) -> tuple[str, float]:
    ai_candidates = {label: value for label, value in scores.items() if label in AI_LABELS}
    if not ai_candidates:
        return NOT_AI_LABEL, 1.0
    best_label = max(ai_candidates, key=ai_candidates.get)
    best_score = float(ai_candidates[best_label])
    if best_score >= label_threshold:
        return best_label, best_score
    not_ai_score = float(scores.get(NOT_AI_LABEL, max(0.0, 1.0 - best_score)))
    return NOT_AI_LABEL, not_ai_score


def draw_overlay(
    frame: Image.Image,
    matched_hits: list[MatchedHit],
    predicted_label: str,
    predicted_confidence: float,
    fps_target: float,
    text_scores: dict[str, float],
    model_scores: dict[str, float],
) -> Image.Image:
    out = frame.copy()
    draw = ImageDraw.Draw(out)
    for hit in matched_hits:
        color = LABEL_COLORS.get(hit.label, (255, 215, 0))
        x1, y1, x2, y2 = hit.bbox
        draw.rectangle((x1, y1, x2, y2), outline=color, width=2)
        draw.text((x1, max(0, y1 - 14)), f"{hit.keyword} {hit.confidence:.2f}", fill=color)

    top_height = 74
    draw.rectangle((0, 0, out.width, top_height), fill=(0, 0, 0, 180))
    draw.text((10, 8), f"label={predicted_label} conf={predicted_confidence:.2f} target_fps={fps_target:.1f}", fill=(255, 255, 255))

    top_text = sorted(((label, score) for label, score in text_scores.items() if label in AI_LABELS), key=lambda x: x[1], reverse=True)[:3]
    top_model = sorted(((label, score) for label, score in model_scores.items() if label in AI_LABELS), key=lambda x: x[1], reverse=True)[:3]

    if top_text:
        text_line = "text: " + ", ".join(f"{label}:{score:.2f}" for label, score in top_text)
        draw.text((10, 28), text_line, fill=(180, 255, 180))
    if top_model:
        model_line = "model: " + ", ".join(f"{label}:{score:.2f}" for label, score in top_model)
        draw.text((10, 48), model_line, fill=(180, 220, 255))
    return out


def apply_capture_artifacts(image: Image.Image, rng: random.Random, np_rng: np.random.Generator, args: argparse.Namespace) -> Image.Image:
    out = image
    if rng.random() < 0.94:
        out = ImageEnhance.Brightness(out).enhance(rng.uniform(0.82, 1.18))
    if rng.random() < 0.9:
        out = ImageEnhance.Contrast(out).enhance(rng.uniform(0.85, 1.22))
    if rng.random() < 0.84:
        out = ImageEnhance.Color(out).enhance(rng.uniform(0.82, 1.20))
    if rng.random() < 0.42:
        out = out.filter(ImageFilter.GaussianBlur(radius=rng.uniform(0.2, 1.4)))

    arr = np.asarray(out).astype(np.int16)
    noise_sigma = rng.uniform(1.0, 10.0)
    arr += np_rng.normal(0.0, noise_sigma, size=arr.shape).astype(np.int16)
    if rng.random() < 0.26:
        arr += np_rng.integers(-10, 11, size=(arr.shape[0], 1, arr.shape[2]), dtype=np.int16)
    out = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8))

    min_scale = min(max(args.resolution_scale_min, 0.1), 1.0)
    max_scale = min(max(args.resolution_scale_max, min_scale), 1.5)
    scale = rng.uniform(min_scale, max_scale)
    target_w = max(320, int(out.width * scale))
    target_h = max(200, int(out.height * scale))
    out = out.resize((target_w, target_h), Image.Resampling.BILINEAR)
    return out


def ensure_dirs(dataset_root: Path, prefix: str) -> tuple[Path, Path, Path]:
    stamp = datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S")
    raw_root = dataset_root / "raw" / f"{prefix}-{stamp}"
    labels_root = dataset_root / "labels"
    labels_csv = labels_root / "classification.csv"
    raw_root.mkdir(parents=True, exist_ok=True)
    labels_root.mkdir(parents=True, exist_ok=True)
    return raw_root, labels_root, labels_csv


def append_labels_csv(labels_csv: Path, rows: list[dict[str, str]]) -> None:
    if not rows:
        return
    frame = pd.read_csv(labels_csv, dtype=str).fillna("") if labels_csv.exists() else pd.DataFrame(columns=["image_path", "label", "source", "notes"])
    for column in ("image_path", "label", "source", "notes"):
        if column not in frame.columns:
            frame[column] = ""
    frame = pd.concat([frame, pd.DataFrame(rows)], ignore_index=True)
    frame = frame.drop_duplicates(subset=["image_path"], keep="last").sort_values("image_path").reset_index(drop=True)
    frame.to_csv(labels_csv, index=False, encoding="utf-8")


def main() -> None:
    args = parse_args()
    rng = random.Random(args.seed)
    np_rng = np.random.default_rng(args.seed)

    dataset_root = Path(args.dataset_root).resolve()
    raw_root, _, labels_csv = ensure_dirs(dataset_root, args.prefix)

    languages = [value.strip() for value in str(args.ocr_langs).split(",") if value.strip()]
    if not languages:
        languages = ["en"]
    try:
        import easyocr  # type: ignore[import-not-found]
    except ImportError as exc:
        raise SystemExit("Install OCR dependency first: pip install easyocr") from exc

    try:
        import torch  # type: ignore[import-not-found]
    except ImportError:
        torch = None

    cuda_available = bool(torch is not None and torch.cuda.is_available())
    use_gpu = False
    if bool(args.ocr_force_cpu):
        use_gpu = False
        print("[ocr] forced CPU mode (--ocr-force-cpu)")
    elif bool(args.ocr_gpu):
        if cuda_available:
            use_gpu = True
        else:
            print("[ocr] warning: --ocr-gpu requested, but CUDA is not available. Falling back to CPU.")
    else:
        if cuda_available:
            use_gpu = True
            device_name = ""
            try:
                device_name = str(torch.cuda.get_device_name(0))
            except Exception:
                device_name = "CUDA device"
            print(f"[ocr] CUDA detected, auto-enabling GPU OCR on '{device_name}'. Use --ocr-force-cpu to disable.")
        else:
            print("[ocr] CUDA not detected, using CPU OCR.")

    print(f"[ocr] initializing EasyOCR with languages={languages}, gpu={use_gpu}")
    ocr_reader = easyocr.Reader(languages, gpu=use_gpu)

    model_scorer: OnnxModelScorer | None = None
    if str(args.onnx_model).strip():
        model_path = Path(str(args.onnx_model)).resolve()
        labels_path = Path(str(args.onnx_labels)).resolve() if str(args.onnx_labels).strip() else None
        if not model_path.exists():
            raise FileNotFoundError(f"ONNX model not found: {model_path}")
        model_scorer = OnnxModelScorer(model_path, labels_path, args.onnx_image_size, args.onnx_provider)
        print(f"[onnx] enabled: {model_path}")

    preview = PreviewWindow(enabled=bool(args.preview), max_width=int(args.preview_max_width))
    print("[run] Press Ctrl+C to stop.")

    rows_to_append: list[dict[str, str]] = []
    smoothed_scores: dict[str, float] = {}
    last_ocr_hits: list[OcrHit] = []
    last_matched_hits: list[MatchedHit] = []
    last_text_scores: dict[str, float] = {label: 0.0 for label in [*AI_LABELS, NOT_AI_LABEL]}
    ocr_every_n_frames = max(1, int(args.ocr_every_n_frames))
    ocr_interval_seconds = max(0.0, float(args.ocr_interval_seconds))
    last_ocr_run_ts = 0.0

    start_time = time.time()
    frame_index = 0
    saved_count = 0
    high_mode_until = 0.0
    current_fps = max(1.0, float(args.base_fps))
    live_stats = {
        "ai_frames": 0,
        "not_ai_frames": 0,
        "high_fps_frames": 0,
    }

    try:
        while True:
            loop_start = time.perf_counter()
            if args.duration_seconds > 0 and (time.time() - start_time) >= args.duration_seconds:
                break
            if args.max_frames > 0 and frame_index >= args.max_frames:
                break
            if preview.closed:
                print("[run] preview window closed")
                break

            frame = grab_frame(args)
            ocr_input = frame
            if args.ocr_max_width > 0 and ocr_input.width > args.ocr_max_width:
                scale = float(args.ocr_max_width) / float(ocr_input.width)
                ocr_input = ocr_input.resize((args.ocr_max_width, max(1, int(ocr_input.height * scale))), Image.Resampling.BILINEAR)

            now_loop = time.time()
            by_frame = frame_index == 0 or (frame_index % ocr_every_n_frames == 0)
            by_time = frame_index == 0 or (ocr_interval_seconds <= 0.0) or ((now_loop - last_ocr_run_ts) >= ocr_interval_seconds)
            run_ocr_this_frame = by_frame and by_time
            if run_ocr_this_frame:
                raw_ocr = ocr_reader.readtext(np.asarray(ocr_input), detail=1, paragraph=False)
                ocr_hits = parse_ocr_hits(raw_ocr)
                if ocr_input.size != frame.size:
                    scale_x = frame.width / float(ocr_input.width)
                    scale_y = frame.height / float(ocr_input.height)
                    ocr_hits = [
                        OcrHit(
                            text=hit.text,
                            confidence=hit.confidence,
                            bbox=(
                                int(hit.bbox[0] * scale_x),
                                int(hit.bbox[1] * scale_y),
                                int(hit.bbox[2] * scale_x),
                                int(hit.bbox[3] * scale_y),
                            ),
                        )
                        for hit in ocr_hits
                    ]
                text_scores, matched_hits = score_text_hits(ocr_hits, keyword_gain=float(args.keyword_gain))
                last_ocr_hits = ocr_hits
                last_matched_hits = matched_hits
                last_text_scores = text_scores
                last_ocr_run_ts = now_loop
            else:
                ocr_hits = last_ocr_hits
                matched_hits = last_matched_hits
                text_scores = last_text_scores
            model_scores: dict[str, float] = {}
            if model_scorer is not None:
                model_scores = model_scorer.predict(frame)

            fused = fuse_scores(text_scores, model_scores, text_weight=float(args.text_weight), model_weight=float(args.model_weight))
            smoothed_scores = smooth_scores(smoothed_scores, fused, decay=float(args.ema_decay))
            predicted_label, predicted_conf = decide_label(smoothed_scores, label_threshold=float(args.label_threshold))

            ai_max = max((smoothed_scores.get(label, 0.0) for label in AI_LABELS), default=0.0)
            now = time.time()
            if ai_max >= float(args.high_fps_trigger):
                high_mode_until = now + max(0.0, float(args.high_fps_hold_seconds))
            if now < high_mode_until:
                current_fps = max(float(args.base_fps), float(args.high_fps))
                live_stats["high_fps_frames"] += 1
            elif ai_max <= float(args.high_fps_return_threshold):
                current_fps = float(args.base_fps)

            if predicted_label == NOT_AI_LABEL:
                live_stats["not_ai_frames"] += 1
            else:
                live_stats["ai_frames"] += 1

            save_frame = args.save_mode == "all" or predicted_label != NOT_AI_LABEL
            if save_frame:
                saved_image = apply_capture_artifacts(frame, rng, np_rng, args)
                ts = datetime.now(timezone.utc)
                stamp = ts.strftime("%Y-%m-%dT%H-%M-%S.%fZ")
                suffix = f"{rng.randint(0, 16**6 - 1):06x}"
                file_name = f"{stamp}-{frame_index:06d}-{predicted_label}-{suffix}.jpg"
                meta_name = file_name.replace(".jpg", ".json")
                image_path = raw_root / file_name
                meta_path = raw_root / meta_name
                quality = int(max(20, min(95, rng.randint(args.jpeg_quality_min, args.jpeg_quality_max))))
                saved_image.save(image_path, format="JPEG", quality=quality, optimize=True)

                top_hits = sorted(matched_hits, key=lambda item: item.score, reverse=True)[:20]
                metadata = {
                    "timestampUtc": ts.isoformat(),
                    "source": "realtime_text_ocr",
                    "predictedLabel": predicted_label,
                    "predictedConfidence": round(float(predicted_conf), 6),
                    "fpsTarget": round(float(current_fps), 3),
                    "textScores": {key: round(float(value), 6) for key, value in text_scores.items()},
                    "modelScores": {key: round(float(value), 6) for key, value in model_scores.items()},
                    "fusedScores": {key: round(float(value), 6) for key, value in smoothed_scores.items()},
                    "ocrHitCount": len(ocr_hits),
                    "matchedHits": [
                        {
                            "label": hit.label,
                            "keyword": hit.keyword,
                            "confidence": round(float(hit.confidence), 6),
                            "score": round(float(hit.score), 6),
                            "bbox": [int(hit.bbox[0]), int(hit.bbox[1]), int(hit.bbox[2]), int(hit.bbox[3])],
                            "text": hit.text,
                        }
                        for hit in top_hits
                    ],
                }
                meta_path.write_text(json.dumps(metadata, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")

                rel_img = str(image_path.relative_to(dataset_root)).replace("\\", "/")
                rel_meta = str(meta_path.relative_to(dataset_root)).replace("\\", "/")
                note = (
                    f"realtime_text_ocr; confidence={predicted_conf:.4f}; fps={current_fps:.2f}; "
                    f"ocr_hits={len(ocr_hits)}; metadata={rel_meta}"
                )
                rows_to_append.append(
                    {
                        "image_path": rel_img,
                        "label": predicted_label,
                        "source": "realtime_text_ocr",
                        "notes": note,
                    }
                )
                saved_count += 1

            overlay = draw_overlay(
                frame=frame,
                matched_hits=matched_hits,
                predicted_label=predicted_label,
                predicted_confidence=predicted_conf,
                fps_target=current_fps,
                text_scores=text_scores,
                model_scores=model_scores,
            )
            preview.update(overlay)

            if frame_index % 10 == 0:
                print(
                    f"[frame {frame_index}] label={predicted_label} conf={predicted_conf:.2f} "
                    f"fps={current_fps:.1f} ocr_hits={len(ocr_hits)} saved={saved_count}"
                )

            frame_index += 1
            elapsed = time.perf_counter() - loop_start
            sleep_time = max(0.0, (1.0 / max(1.0, current_fps)) - elapsed)
            if sleep_time > 0:
                time.sleep(sleep_time)
    except KeyboardInterrupt:
        print("\n[run] interrupted by user")
    finally:
        preview.close()

    append_labels_csv(labels_csv, rows_to_append)

    print("\nRealtime OCR dataset generation completed.")
    print(f"Dataset root: {dataset_root}")
    print(f"Output folder: {raw_root}")
    print(f"Frames processed: {frame_index}")
    print(f"Frames saved: {saved_count}")
    print(f"AI frames: {live_stats['ai_frames']}")
    print(f"not_ai frames: {live_stats['not_ai_frames']}")
    print(f"High FPS frames: {live_stats['high_fps_frames']}")
    print(f"Labels CSV updated: {labels_csv}")


if __name__ == "__main__":
    main()
