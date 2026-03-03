from __future__ import annotations

import argparse
import csv
import json
import random
import time
from datetime import datetime, timezone
from pathlib import Path

try:
    from PIL import Image, ImageDraw, ImageGrab
except ModuleNotFoundError as exc:
    if str(exc.name).lower().startswith("pil"):
        raise SystemExit(
            "Missing dependency 'Pillow'. Install it with: "
            "python -m pip install -r ml/requirements.txt"
        ) from exc
    raise


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Capture realtime desktop frames and label all saved images with a fixed label "
            "(default: not_ai_ui). Useful for quick not_ai_ui dataset expansion."
        )
    )
    parser.add_argument("--dataset-root", default="dataset")
    parser.add_argument("--prefix", default="manual-not-ai")
    parser.add_argument("--label", default="not_ai_ui")
    parser.add_argument("--source", default="manual_frame_labeler")
    parser.add_argument("--notes-prefix", default="manual_session")
    parser.add_argument("--duration-seconds", type=int, default=0, help="0 means run until Ctrl+C or preview close.")
    parser.add_argument("--max-frames", type=int, default=0, help="0 means unlimited.")
    parser.add_argument("--fps", type=float, default=2.0)
    parser.add_argument("--jpeg-quality", type=int, default=88)
    parser.add_argument("--max-image-width", type=int, default=1920)
    parser.add_argument("--hash-size", type=int, default=16)
    parser.add_argument("--min-hash-distance", type=int, default=8, help="Skip near-duplicate frames.")
    parser.add_argument("--capture-left", type=int, default=0)
    parser.add_argument("--capture-top", type=int, default=0)
    parser.add_argument("--capture-width", type=int, default=0, help="0 means full desktop.")
    parser.add_argument("--capture-height", type=int, default=0, help="0 means full desktop.")
    parser.add_argument("--all-screens", action="store_true")
    parser.add_argument("--preview", action="store_true")
    parser.add_argument("--preview-max-width", type=int, default=1440)
    parser.add_argument("--seed", type=int, default=42)
    return parser.parse_args()


def sanitize_label(value: str) -> str:
    cleaned = "".join(char if (char.isalnum() or char in ("-", "_")) else "_" for char in value.strip().lower())
    return cleaned.strip("_") or "label"


def grab_frame(args: argparse.Namespace) -> Image.Image:
    bbox = None
    if args.capture_width > 0 and args.capture_height > 0:
        bbox = (
            int(args.capture_left),
            int(args.capture_top),
            int(args.capture_left) + int(args.capture_width),
            int(args.capture_top) + int(args.capture_height),
        )
    frame = ImageGrab.grab(bbox=bbox, all_screens=bool(args.all_screens))
    return frame.convert("RGB")


def resize_if_needed(image: Image.Image, max_width: int) -> Image.Image:
    if max_width <= 0 or image.width <= max_width:
        return image
    scale = float(max_width) / float(image.width)
    target_size = (max_width, max(1, int(image.height * scale)))
    return image.resize(target_size, Image.Resampling.BILINEAR)


def compute_ahash(image: Image.Image, size: int) -> tuple[int, ...]:
    safe_size = max(4, int(size))
    small = image.convert("L").resize((safe_size, safe_size), Image.Resampling.BILINEAR)
    pixels = list(small.tobytes())
    mean_value = sum(pixels) / float(len(pixels))
    return tuple(1 if pixel >= mean_value else 0 for pixel in pixels)


def hamming_distance(left: tuple[int, ...] | None, right: tuple[int, ...]) -> int:
    if left is None or len(left) != len(right):
        return len(right)
    return sum(1 for l_bit, r_bit in zip(left, right, strict=False) if l_bit != r_bit)


def ensure_dirs(dataset_root: Path, prefix: str) -> tuple[Path, Path]:
    stamp = datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S")
    raw_root = dataset_root / "raw" / f"{prefix}-{stamp}"
    labels_root = dataset_root / "labels"
    labels_csv = labels_root / "classification.csv"
    raw_root.mkdir(parents=True, exist_ok=True)
    labels_root.mkdir(parents=True, exist_ok=True)
    return raw_root, labels_csv


def append_labels_csv(labels_csv: Path, rows: list[dict[str, str]]) -> None:
    if not rows:
        return

    fields = ["image_path", "label", "source", "notes"]
    merged: dict[str, dict[str, str]] = {}
    if labels_csv.exists():
        with labels_csv.open("r", encoding="utf-8", newline="") as file:
            reader = csv.DictReader(file)
            for row in reader:
                image_path = str(row.get("image_path", "")).strip()
                if not image_path:
                    continue
                merged[image_path] = {field: str(row.get(field, "")).strip() for field in fields}

    for row in rows:
        image_path = str(row.get("image_path", "")).strip()
        if not image_path:
            continue
        merged[image_path] = {field: str(row.get(field, "")).strip() for field in fields}

    with labels_csv.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fields)
        writer.writeheader()
        for key in sorted(merged):
            writer.writerow(merged[key])


class PreviewWindow:
    def __init__(self, enabled: bool, max_width: int) -> None:
        self._enabled = enabled
        self._max_width = max(0, int(max_width))
        self._closed = False
        self._tk = None
        self._image_label = None
        self._image_tk = None
        self._image_tk_module = None

        if not self._enabled:
            return

        try:
            import tkinter as tk
            from PIL import ImageTk
        except Exception as exc:
            print(f"[preview] disabled: {exc}")
            self._enabled = False
            return

        self._image_tk_module = ImageTk
        self._tk = tk.Tk()
        self._tk.title("Controledu Manual Label Generator")
        self._tk.protocol("WM_DELETE_WINDOW", self._on_close)
        self._image_label = tk.Label(self._tk)
        self._image_label.pack(fill=tk.BOTH, expand=True)

    def _on_close(self) -> None:
        self._closed = True
        if self._tk is not None:
            self._tk.destroy()

    @property
    def closed(self) -> bool:
        return self._closed

    def update(self, image: Image.Image) -> None:
        if not self._enabled or self._closed or self._tk is None or self._image_label is None or self._image_tk_module is None:
            return
        preview = image
        if self._max_width > 0 and preview.width > self._max_width:
            scale = self._max_width / float(preview.width)
            preview = preview.resize((self._max_width, max(1, int(preview.height * scale))), Image.Resampling.BILINEAR)

        self._image_tk = self._image_tk_module.PhotoImage(preview)
        self._image_label.configure(image=self._image_tk)
        self._tk.update_idletasks()
        self._tk.update()

    def close(self) -> None:
        if self._tk is not None and not self._closed:
            self._tk.destroy()
            self._closed = True


def draw_overlay(frame: Image.Image, label: str, saved_count: int, processed_count: int, dedup_distance: int, threshold: int) -> Image.Image:
    output = frame.copy()
    draw = ImageDraw.Draw(output)
    bar_height = 56
    draw.rectangle((0, 0, output.width, bar_height), fill=(0, 0, 0, 180))
    draw.text((10, 8), f"label={label} processed={processed_count} saved={saved_count}", fill=(255, 255, 255))
    draw.text((10, 30), f"dedup_distance={dedup_distance} threshold={threshold}", fill=(180, 255, 180))
    return output


def random_suffix(rng: random.Random) -> str:
    return f"{rng.randint(0, 16**6 - 1):06x}"


def relative_posix(path: Path, root: Path) -> str:
    return str(path.relative_to(root)).replace("\\", "/")


def build_note(prefix: str, label: str, dedup_distance: int, threshold: int, metadata_rel_path: str) -> str:
    return (
        f"{prefix}; label={label}; dedup_distance={dedup_distance}; "
        f"min_hash_distance={threshold}; metadata={metadata_rel_path}"
    )


def main() -> None:
    args = parse_args()
    rng = random.Random(int(args.seed))

    dataset_root = Path(args.dataset_root).resolve()
    label = sanitize_label(str(args.label))
    raw_root, labels_csv = ensure_dirs(dataset_root, str(args.prefix))
    preview = PreviewWindow(enabled=bool(args.preview), max_width=int(args.preview_max_width))

    print("[run] Press Ctrl+C to stop.")
    print(f"[run] Every saved frame will be labeled as: {label}")

    start_time = time.time()
    rows_to_append: list[dict[str, str]] = []
    processed_count = 0
    saved_count = 0
    skipped_duplicates = 0
    last_hash: tuple[int, ...] | None = None

    try:
        while True:
            loop_start = time.perf_counter()
            if int(args.duration_seconds) > 0 and (time.time() - start_time) >= int(args.duration_seconds):
                break
            if int(args.max_frames) > 0 and processed_count >= int(args.max_frames):
                break
            if preview.closed:
                print("[run] preview window closed")
                break

            frame = grab_frame(args)
            frame = resize_if_needed(frame, int(args.max_image_width))

            current_hash = compute_ahash(frame, int(args.hash_size))
            distance = hamming_distance(last_hash, current_hash)
            should_save = last_hash is None or distance >= int(args.min_hash_distance)

            if should_save:
                ts = datetime.now(timezone.utc)
                stamp = ts.strftime("%Y-%m-%dT%H-%M-%S.%fZ")
                name = f"{stamp}-{processed_count:06d}-{label}-{random_suffix(rng)}.jpg"
                image_path = raw_root / name
                meta_path = raw_root / name.replace(".jpg", ".json")

                frame.save(
                    image_path,
                    format="JPEG",
                    quality=max(20, min(95, int(args.jpeg_quality))),
                    optimize=True,
                )

                metadata = {
                    "timestampUtc": ts.isoformat(),
                    "source": str(args.source),
                    "label": label,
                    "frameIndex": processed_count,
                    "dedupDistance": distance,
                    "minHashDistance": int(args.min_hash_distance),
                    "imageWidth": frame.width,
                    "imageHeight": frame.height,
                }
                meta_path.write_text(json.dumps(metadata, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")

                image_rel = relative_posix(image_path, dataset_root)
                meta_rel = relative_posix(meta_path, dataset_root)
                rows_to_append.append(
                    {
                        "image_path": image_rel,
                        "label": label,
                        "source": str(args.source),
                        "notes": build_note(str(args.notes_prefix), label, distance, int(args.min_hash_distance), meta_rel),
                    }
                )
                saved_count += 1
                last_hash = current_hash
            else:
                skipped_duplicates += 1

            overlay = draw_overlay(
                frame=frame,
                label=label,
                saved_count=saved_count,
                processed_count=processed_count + 1,
                dedup_distance=distance,
                threshold=int(args.min_hash_distance),
            )
            preview.update(overlay)

            if processed_count % 10 == 0:
                print(
                    f"[frame {processed_count}] saved={saved_count} "
                    f"skipped_duplicates={skipped_duplicates} dedup_distance={distance}"
                )

            processed_count += 1
            elapsed = time.perf_counter() - loop_start
            target_sleep = max(0.0, (1.0 / max(0.2, float(args.fps))) - elapsed)
            if target_sleep > 0:
                time.sleep(target_sleep)
    except KeyboardInterrupt:
        print("\n[run] interrupted by user")
    finally:
        preview.close()

    append_labels_csv(labels_csv, rows_to_append)

    print("\nManual frame labeling completed.")
    print(f"Dataset root: {dataset_root}")
    print(f"Output folder: {raw_root}")
    print(f"Frames processed: {processed_count}")
    print(f"Frames saved: {saved_count}")
    print(f"Duplicates skipped: {skipped_duplicates}")
    print(f"Labels CSV updated: {labels_csv}")


if __name__ == "__main__":
    main()
