from __future__ import annotations

import argparse
import json
import random
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from pathlib import Path

import numpy as np
import pandas as pd
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont


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

AI_BRANDS = {
    "chatgpt_ui": ("ChatGPT", "OpenAI", "https://chat.openai.com/"),
    "claude_ui": ("Claude", "Anthropic", "https://claude.ai/"),
    "gemini_ui": ("Gemini", "Google", "https://gemini.google.com/"),
    "copilot_ui": ("Copilot", "Microsoft", "https://copilot.microsoft.com/"),
    "perplexity_ui": ("Perplexity", "Perplexity", "https://www.perplexity.ai/"),
    "deepseek_ui": ("DeepSeek", "DeepSeek", "https://chat.deepseek.com/"),
    "poe_ui": ("Poe", "Quora", "https://poe.com/"),
    "grok_ui": ("Grok", "xAI", "https://grok.com/"),
    "qwen_ui": ("Qwen", "Alibaba", "https://chat.qwen.ai/"),
    "mistral_ui": ("Le Chat", "Mistral", "https://chat.mistral.ai/"),
    "meta_ai_ui": ("Meta AI", "Meta", "https://www.meta.ai/"),
    "ai_ui": ("AI Assistant", "Generic", "https://assistant.local/"),
}

NOT_AI_SCENES = [
    ("Wikipedia article", "https://en.wikipedia.org/wiki/Computer_network", "chrome"),
    ("Documentation page", "https://docs.python.org/3/", "chrome"),
    ("News portal", "https://www.bbc.com/news", "chrome"),
    ("Spreadsheet", "file://C:/Users/Public/Documents/report.xlsx", "excel"),
    ("Presentation", "file://C:/Users/Public/Documents/lesson.pptx", "powerpoint"),
    ("IDE project", "file://C:/dev/project/src/main.cs", "devenv"),
    ("Email inbox", "https://mail.example.com/inbox", "outlook"),
    ("Video lesson", "https://www.youtube.com/watch?v=example", "chrome"),
    ("LMS dashboard", "https://portal.example.edu/dashboard", "chrome"),
]

DETECTION_CLASS_BY_LABEL = {
    "chatgpt_ui": "ChatGpt",
    "claude_ui": "Claude",
    "gemini_ui": "Gemini",
    "copilot_ui": "Copilot",
    "perplexity_ui": "Perplexity",
    "deepseek_ui": "DeepSeek",
    "poe_ui": "Poe",
    "grok_ui": "UnknownAi",
    "qwen_ui": "UnknownAi",
    "mistral_ui": "UnknownAi",
    "meta_ai_ui": "UnknownAi",
    "ai_ui": "UnknownAi",
    "not_ai_ui": "None",
}


@dataclass(frozen=True)
class SyntheticSample:
    image_rel: str
    label: str
    metadata_rel: str


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Generate synthetic UI screenshots for binary/multiclass training.\n"
            "Creates image+metadata files and appends labels to dataset/labels/classification.csv."
        )
    )
    parser.add_argument("--dataset-root", default="dataset", help="Dataset root folder.")
    parser.add_argument("--count", type=int, default=2000, help="Total number of synthetic screenshots.")
    parser.add_argument("--ai-ratio", type=float, default=0.5, help="Share of AI screenshots (0..1).")
    parser.add_argument("--hard-negative-ratio", type=float, default=0.25, help="Share of hard negatives among not_ai.")
    parser.add_argument("--width", type=int, default=1280, help="Image width.")
    parser.add_argument("--height", type=int, default=720, help="Image height.")
    parser.add_argument("--seed", type=int, default=42, help="Random seed.")
    parser.add_argument("--prefix", default="synthetic-v1", help="Subfolder prefix under dataset/raw.")
    return parser.parse_args()


def ensure_dirs(dataset_root: Path, prefix: str) -> tuple[Path, Path]:
    raw_root = dataset_root / "raw" / prefix
    labels_root = dataset_root / "labels"
    raw_root.mkdir(parents=True, exist_ok=True)
    labels_root.mkdir(parents=True, exist_ok=True)
    return raw_root, labels_root


def draw_window_chrome(draw: ImageDraw.ImageDraw, width: int, height: int, title: str, url: str, brand_color: tuple[int, int, int]) -> None:
    top = int(height * 0.10)
    draw.rectangle((0, 0, width, top), fill=(36, 39, 44))
    draw.rectangle((0, int(top * 0.52), width, top), fill=(49, 54, 62))
    draw.rounded_rectangle((int(width * 0.12), int(top * 0.62), int(width * 0.90), int(top * 0.95)), radius=8, fill=(240, 242, 245))
    draw.text((18, int(top * 0.25)), title[:40], fill=(230, 230, 230), font=ImageFont.load_default())
    draw.text((int(width * 0.14), int(top * 0.70)), url[:85], fill=(70, 90, 120), font=ImageFont.load_default())
    draw.rectangle((0, top, width, top + 3), fill=brand_color)


def draw_chat_layout(img: Image.Image, rng: random.Random, label: str) -> tuple[str, str, str]:
    draw = ImageDraw.Draw(img)
    width, height = img.size
    brand_name, vendor, url = AI_BRANDS[label]
    brand_color = tuple(rng.randint(40, 140) for _ in range(3))

    draw_window_chrome(draw, width, height, f"{brand_name} - {vendor}", url, brand_color)

    top = int(height * 0.10) + 3
    sidebar_w = int(width * 0.19)
    draw.rectangle((0, top, sidebar_w, height), fill=(28, 31, 36))
    draw.rectangle((sidebar_w, top, width, height), fill=(245, 247, 250))

    y = top + 14
    for _ in range(12):
        h = rng.randint(18, 30)
        color = (55 + rng.randint(0, 15), 60 + rng.randint(0, 15), 67 + rng.randint(0, 15))
        draw.rounded_rectangle((10, y, sidebar_w - 10, y + h), radius=6, fill=color)
        y += h + rng.randint(8, 14)
        if y > height - 60:
            break

    content_left = sidebar_w + 20
    content_right = width - 20
    chat_y = top + 22
    for i in range(rng.randint(8, 14)):
        bubble_h = rng.randint(30, 70)
        bubble_w = rng.randint(int(width * 0.28), int(width * 0.55))
        if i % 2 == 0:
            x0 = content_left + rng.randint(0, 20)
            color = (230, 234, 240)
        else:
            x0 = content_right - bubble_w - rng.randint(0, 20)
            color = (207, 233, 255)
        x1 = x0 + bubble_w
        y1 = chat_y + bubble_h
        draw.rounded_rectangle((x0, chat_y, x1, y1), radius=14, fill=color)
        line_y = chat_y + 10
        for _ in range(rng.randint(2, 4)):
            lw = rng.randint(int(bubble_w * 0.45), int(bubble_w * 0.85))
            draw.rectangle((x0 + 12, line_y, x0 + 12 + lw, line_y + 4), fill=(150, 160, 172))
            line_y += 10
        chat_y = y1 + rng.randint(12, 20)
        if chat_y > height - 120:
            break

    input_h = 56
    draw.rounded_rectangle(
        (content_left, height - input_h - 18, content_right, height - 18),
        radius=16,
        fill=(255, 255, 255),
        outline=(198, 205, 214),
        width=2,
    )
    draw.text((content_left + 16, height - input_h), "Type a message...", fill=(123, 130, 139), font=ImageFont.load_default())

    process = "chrome"
    title = f"{brand_name} - {vendor}"
    return process, title, url


def draw_not_ai_layout(img: Image.Image, rng: random.Random, hard_negative: bool) -> tuple[str, str, str]:
    draw = ImageDraw.Draw(img)
    width, height = img.size
    scene_name, url, process = rng.choice(NOT_AI_SCENES)
    brand_color = (72, 92, 118) if "wiki" in url else (96, 110, 136)

    draw_window_chrome(draw, width, height, scene_name, url, brand_color)
    top = int(height * 0.10) + 3

    if hard_negative:
        left = int(width * 0.2)
        draw.rectangle((0, top, left, height), fill=(243, 245, 248))
        draw.rectangle((left, top, width, height), fill=(255, 255, 255))
        y = top + 18
        for _ in range(9):
            h = rng.randint(18, 28)
            draw.rounded_rectangle((14, y, left - 10, y + h), radius=6, fill=(222, 227, 236))
            y += h + rng.randint(8, 12)
            if y > height - 80:
                break

        content_y = top + 24
        for i in range(10):
            block_h = rng.randint(40, 84)
            block_w = rng.randint(int(width * 0.28), int(width * 0.52))
            x0 = left + rng.randint(16, 36)
            x1 = min(width - 24, x0 + block_w)
            color = (237, 241, 246) if i % 3 else (226, 233, 242)
            draw.rounded_rectangle((x0, content_y, x1, content_y + block_h), radius=10, fill=color)
            content_y += block_h + rng.randint(10, 16)
            if content_y > height - 100:
                break
    else:
        draw.rectangle((0, top, width, height), fill=(248, 250, 252))
        hero_h = int(height * 0.24)
        draw.rectangle((0, top, width, top + hero_h), fill=(236, 242, 249))
        draw.rectangle((24, top + 24, int(width * 0.72), top + 42), fill=(92, 104, 122))
        draw.rectangle((24, top + 54, int(width * 0.55), top + 66), fill=(120, 131, 148))

        card_w = int((width - 64) / 3)
        cards_y = top + hero_h + 20
        for i in range(3):
            x0 = 16 + i * (card_w + 16)
            x1 = x0 + card_w
            draw.rounded_rectangle((x0, cards_y, x1, cards_y + 160), radius=12, fill=(255, 255, 255), outline=(216, 223, 232))
            ly = cards_y + 20
            for _ in range(6):
                lw = rng.randint(int(card_w * 0.40), int(card_w * 0.88))
                draw.rectangle((x0 + 14, ly, x0 + 14 + lw, ly + 5), fill=(158, 166, 178))
                ly += 16

        table_y = cards_y + 186
        draw.rounded_rectangle((16, table_y, width - 16, height - 20), radius=12, fill=(255, 255, 255), outline=(216, 223, 232))
        row_y = table_y + 16
        for _ in range(8):
            draw.rectangle((26, row_y, width - 26, row_y + 1), fill=(230, 235, 242))
            row_y += 26
            if row_y > height - 32:
                break

    window_title = f"{scene_name} - Browser"
    return process, window_title, url


def apply_screen_artifacts(image: Image.Image, rng: random.Random, np_rng: np.random.Generator) -> Image.Image:
    out = image

    if rng.random() < 0.85:
        out = ImageEnhance.Brightness(out).enhance(rng.uniform(0.92, 1.08))
    if rng.random() < 0.85:
        out = ImageEnhance.Contrast(out).enhance(rng.uniform(0.90, 1.12))
    if rng.random() < 0.80:
        out = ImageEnhance.Color(out).enhance(rng.uniform(0.88, 1.10))

    if rng.random() < 0.45:
        out = out.filter(ImageFilter.GaussianBlur(radius=rng.uniform(0.4, 1.4)))

    if rng.random() < 0.55:
        w, h = out.size
        scale = rng.uniform(0.82, 0.96)
        dw = max(320, int(w * scale))
        dh = max(180, int(h * scale))
        out = out.resize((dw, dh), Image.Resampling.BILINEAR).resize((w, h), Image.Resampling.BICUBIC)

    arr = np.asarray(out).astype(np.int16)
    if rng.random() < 0.75:
        sigma = rng.uniform(3.0, 9.0)
        noise = np_rng.normal(0.0, sigma, size=arr.shape)
        arr = arr + noise.astype(np.int16)
    if rng.random() < 0.18:
        band = np_rng.integers(-12, 13, size=(arr.shape[0], 1, arr.shape[2]), dtype=np.int16)
        arr = arr + band
    arr = np.clip(arr, 0, 255).astype(np.uint8)
    out = Image.fromarray(arr, mode="RGB")
    return out


def build_metadata(
    label: str,
    process: str,
    window_title: str,
    url: str,
    timestamp_utc: datetime,
    frame_hash: str,
) -> dict:
    is_ai = label != NOT_AI_LABEL
    detection_class = DETECTION_CLASS_BY_LABEL.get(label, "UnknownAi")
    confidence = round(random.uniform(0.74, 0.97), 3) if is_ai else round(random.uniform(0.01, 0.22), 3)
    stage = "OnnxMulticlass" if is_ai else "MetadataRule"
    reason = (
        "Synthetic AI UI pattern in rendered frame."
        if is_ai
        else "Synthetic non-AI UI pattern (docs/wiki/dashboard)."
    )

    return {
        "studentId": "synthetic-generator",
        "timestampUtc": timestamp_utc.isoformat(),
        "screenFrameHash": frame_hash,
        "frameChanged": True,
        "activeProcessName": process,
        "activeWindowTitle": window_title,
        "browserHintUrl": url,
        "detection": {
            "isAiUiDetected": is_ai,
            "confidence": confidence,
            "class": detection_class,
            "stageSource": stage,
            "reason": reason,
            "modelVersion": "synthetic-v1",
            "isStable": is_ai,
            "triggeredKeywords": None,
        },
    }


def append_labels_csv(labels_csv: Path, samples: list[SyntheticSample]) -> None:
    if labels_csv.exists():
        frame = pd.read_csv(labels_csv, dtype=str).fillna("")
    else:
        frame = pd.DataFrame(columns=["image_path", "label", "source", "notes"])

    for column in ("image_path", "label", "source", "notes"):
        if column not in frame.columns:
            frame[column] = ""

    existing = {str(value).replace("\\", "/") for value in frame["image_path"].astype(str).tolist()}
    new_rows: list[dict[str, str]] = []
    for sample in samples:
        if sample.image_rel in existing:
            continue
        new_rows.append(
            {
                "image_path": sample.image_rel,
                "label": sample.label,
                "source": "synthetic_generator",
                "notes": f"synthetic ui; metadata={sample.metadata_rel}",
            }
        )

    if new_rows:
        frame = pd.concat([frame, pd.DataFrame(new_rows)], ignore_index=True)
        frame = frame.drop_duplicates(subset=["image_path"], keep="last")
        frame = frame.sort_values(by=["image_path"]).reset_index(drop=True)

    labels_csv.parent.mkdir(parents=True, exist_ok=True)
    frame.to_csv(labels_csv, index=False, encoding="utf-8")


def generate(args: argparse.Namespace) -> None:
    if args.count < 1:
        raise ValueError("--count must be > 0.")
    if args.width < 320 or args.height < 180:
        raise ValueError("Resolution is too small.")

    dataset_root = Path(args.dataset_root).resolve()
    raw_root, labels_root = ensure_dirs(dataset_root, args.prefix)
    labels_csv = labels_root / "classification.csv"

    rng = random.Random(args.seed)
    np_rng = np.random.default_rng(args.seed)
    ai_target = int(round(args.count * min(max(args.ai_ratio, 0.0), 1.0)))
    not_ai_target = args.count - ai_target

    start_time = datetime.now(timezone.utc) - timedelta(days=1)
    samples: list[SyntheticSample] = []

    for index in range(args.count):
        make_ai = index < ai_target
        if make_ai:
            label = rng.choice(AI_LABELS)
        else:
            label = NOT_AI_LABEL

        image = Image.new("RGB", (args.width, args.height), color=(246, 248, 252))
        hard_negative = (not make_ai) and (rng.random() < min(max(args.hard_negative_ratio, 0.0), 1.0))
        if make_ai:
            process, title, url = draw_chat_layout(image, rng, label)
        else:
            process, title, url = draw_not_ai_layout(image, rng, hard_negative)

        image = apply_screen_artifacts(image, rng, np_rng)
        quality = rng.randint(58, 90)
        timestamp = start_time + timedelta(seconds=index * rng.randint(2, 5) + rng.randint(0, 2))
        stamp = timestamp.strftime("%Y-%m-%dT%H-%M-%SZ")
        suffix = f"{rng.randint(0, 16**8 - 1):08x}"
        image_name = f"{stamp}-{index:05d}-{label}-{suffix}.jpg"
        metadata_name = image_name.replace(".jpg", ".json")

        image_path = raw_root / image_name
        metadata_path = raw_root / metadata_name
        image.save(image_path, format="JPEG", quality=quality, optimize=True)

        frame_hash = f"{rng.randint(0, 16**16 - 1):016X}"
        metadata = build_metadata(
            label=label,
            process=process,
            window_title=title,
            url=url,
            timestamp_utc=timestamp,
            frame_hash=frame_hash,
        )
        metadata_path.write_text(json.dumps(metadata, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")

        image_rel = str(image_path.relative_to(dataset_root)).replace("\\", "/")
        metadata_rel = str(metadata_path.relative_to(dataset_root)).replace("\\", "/")
        samples.append(SyntheticSample(image_rel=image_rel, label=label, metadata_rel=metadata_rel))

    append_labels_csv(labels_csv, samples)

    print("Synthetic dataset generation completed.")
    print(f"Dataset root: {dataset_root}")
    print(f"Output folder: {raw_root}")
    print(f"Generated images: {len(samples)}")
    print(f"AI samples: {ai_target}")
    print(f"not_ai samples: {not_ai_target}")
    print(f"Labels CSV updated: {labels_csv}")


if __name__ == "__main__":
    try:
        generate(parse_args())
    except Exception as exc:
        print(f"\nERROR: {exc}")
        raise
