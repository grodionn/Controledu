from __future__ import annotations

import argparse
import csv
import json
import random
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

try:
    import pandas as pd
except ModuleNotFoundError as exc:
    print(f"Missing Python package '{exc.name}'. Install dependencies with: pip install -r ml/requirements.txt")
    raise SystemExit(1) from exc

from common import load_config, read_dataset_paths, set_seed


DEFAULT_IMAGE_EXTENSIONS = {".jpg", ".jpeg", ".png", ".bmp", ".webp"}


@dataclass(frozen=True)
class Suggestion:
    image_path: str
    current_label: str
    suggested_label: str
    reason: str
    metadata_path: str
    confidence: float


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Prepare labels CSV and split files for Controledu ML.\n"
            "1) Scans dataset/raw images\n"
            "2) Upserts labels/classification.csv\n"
            "3) Builds splits/train.txt, val.txt, test.txt from labeled rows\n"
            "4) Creates labels/review_queue.csv with suggested labels from metadata"
        )
    )
    parser.add_argument("--config", required=True, help="Path to YAML config file (see ml/config.example.yaml).")
    parser.add_argument(
        "--default-label",
        default="",
        help="Label value for newly discovered images. Default is empty (to annotate later).",
    )
    parser.add_argument(
        "--source",
        default="collection_mode",
        help="Default source value for newly discovered images in classification.csv.",
    )
    parser.add_argument(
        "--notes",
        default="",
        help="Default notes value for newly discovered images in classification.csv.",
    )
    parser.add_argument(
        "--include-unlabeled-in-csv",
        action="store_true",
        help="Keep rows with empty labels in classification.csv (default behavior is enabled).",
    )
    parser.add_argument(
        "--drop-missing-files",
        action="store_true",
        help="Remove CSV rows whose image files no longer exist on disk.",
    )
    parser.add_argument("--train-ratio", type=float, default=0.8, help="Train ratio for split generation.")
    parser.add_argument("--val-ratio", type=float, default=0.1, help="Validation ratio for split generation.")
    parser.add_argument("--test-ratio", type=float, default=0.1, help="Test ratio for split generation.")
    parser.add_argument("--seed", type=int, default=42, help="Random seed.")
    parser.add_argument(
        "--no-stratified",
        action="store_true",
        help="Disable class-stratified splitting and use simple random split.",
    )
    parser.add_argument(
        "--auto-apply-suggestions",
        action="store_true",
        help="Automatically apply suggested labels from metadata into classification.csv.",
    )
    parser.add_argument(
        "--suggestion-min-confidence",
        type=float,
        default=0.75,
        help="Minimum suggestion confidence to auto-apply (0..1). Default: 0.75.",
    )
    parser.add_argument(
        "--suggestion-apply-mode",
        choices=["empty_only", "all"],
        default="empty_only",
        help=(
            "Auto-apply mode: "
            "'empty_only' updates only unlabeled rows, "
            "'all' may overwrite existing labels."
        ),
    )
    return parser.parse_args()


def find_images(raw_root: Path) -> list[Path]:
    if not raw_root.exists():
        raise FileNotFoundError(
            f"Dataset raw folder not found: {raw_root}\n"
            "Expected structure: <dataset-root>/raw/<student-id>/<timestamp>.jpg"
        )

    result = [
        path
        for path in raw_root.rglob("*")
        if path.is_file() and path.suffix.lower() in DEFAULT_IMAGE_EXTENSIONS
    ]
    result.sort()
    return result


def normalize_relative(path: Path, root: Path) -> str:
    return str(path.relative_to(root)).replace("\\", "/")


def ensure_parent(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)


def load_existing_csv(labels_csv: Path) -> pd.DataFrame:
    if not labels_csv.exists():
        return pd.DataFrame(columns=["image_path", "label", "source", "notes"])

    return pd.read_csv(labels_csv, dtype=str).fillna("")


def upsert_labels(
    existing: pd.DataFrame,
    image_paths: list[str],
    default_label: str,
    default_source: str,
    default_notes: str,
    drop_missing_files: bool,
    image_column: str,
    label_column: str,
) -> pd.DataFrame:
    source_column = "source"
    notes_column = "notes"

    for column in (image_column, label_column, source_column, notes_column):
        if column not in existing.columns:
            existing[column] = ""

    existing = existing[[image_column, label_column, source_column, notes_column]].copy()
    existing[image_column] = existing[image_column].astype(str).str.replace("\\", "/", regex=False)
    existing[label_column] = existing[label_column].astype(str)
    existing[source_column] = existing[source_column].astype(str)
    existing[notes_column] = existing[notes_column].astype(str)

    existing_by_image = {
        row[image_column]: (
            row[label_column],
            row[source_column],
            row[notes_column],
        )
        for _, row in existing.iterrows()
        if str(row[image_column]).strip()
    }

    rows: list[tuple[str, str, str, str]] = []
    for image_path in image_paths:
        label, source, notes = existing_by_image.get(
            image_path,
            (default_label, default_source, default_notes),
        )
        rows.append((image_path, label, source, notes))

    if not drop_missing_files:
        known = set(image_paths)
        for image_path, values in existing_by_image.items():
            if image_path not in known:
                rows.append((image_path, values[0], values[1], values[2]))

    merged = pd.DataFrame(rows, columns=[image_column, label_column, source_column, notes_column])
    merged = merged.drop_duplicates(subset=[image_column], keep="first")
    merged = merged.sort_values(by=[image_column]).reset_index(drop=True)
    return merged


def metadata_path_for_image(dataset_root: Path, image_rel: str) -> Path:
    image_path = dataset_root / image_rel
    return image_path.with_suffix(".json")


def class_from_metadata(metadata: dict) -> tuple[str | None, str, float]:
    text_parts = [
        str(metadata.get("activeWindowTitle") or ""),
        str(metadata.get("activeProcessName") or ""),
        str(metadata.get("browserHintUrl") or ""),
    ]
    text = " ".join(text_parts).lower()

    rules: list[tuple[Iterable[str], str, str]] = [
        (("chatgpt", "openai"), "chatgpt_ui", "keyword: chatgpt/openai"),
        (("claude", "anthropic"), "claude_ui", "keyword: claude/anthropic"),
        (("gemini", "bard"), "gemini_ui", "keyword: gemini/bard"),
        (("copilot",), "copilot_ui", "keyword: copilot"),
        (("perplexity",), "perplexity_ui", "keyword: perplexity"),
        (("deepseek",), "deepseek_ui", "keyword: deepseek"),
        (("poe.com", "poe "), "poe_ui", "keyword: poe"),
    ]

    for keywords, suggested, reason in rules:
        if any(keyword in text for keyword in keywords):
            return suggested, reason, 0.8

    detection = metadata.get("detection")
    if isinstance(detection, dict):
        if bool(detection.get("isAiUiDetected")):
            confidence = float(detection.get("confidence") or 0.6)
            return "ai_ui", "detector marked ai_ui", min(max(confidence, 0.0), 1.0)

    return None, "", 0.0


def build_review_queue(
    dataset_root: Path,
    labels_df: pd.DataFrame,
    image_column: str,
    label_column: str,
) -> list[Suggestion]:
    suggestions: list[Suggestion] = []

    for _, row in labels_df.iterrows():
        image_rel = str(row[image_column]).strip().replace("\\", "/")
        if not image_rel:
            continue
        current_label = str(row[label_column]).strip()
        metadata_path = metadata_path_for_image(dataset_root, image_rel)
        if not metadata_path.exists():
            continue

        try:
            metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
        except Exception:
            continue

        suggested_label, reason, confidence = class_from_metadata(metadata)
        if not suggested_label:
            continue

        if current_label and current_label == suggested_label:
            continue

        suggestions.append(
            Suggestion(
                image_path=image_rel,
                current_label=current_label,
                suggested_label=suggested_label,
                reason=reason,
                metadata_path=normalize_relative(metadata_path, dataset_root),
                confidence=confidence,
            )
        )

    return suggestions


def write_review_queue(queue_path: Path, suggestions: list[Suggestion]) -> None:
    ensure_parent(queue_path)
    with queue_path.open("w", newline="", encoding="utf-8") as file:
        writer = csv.writer(file)
        writer.writerow(["image_path", "current_label", "suggested_label", "reason", "metadata_path", "confidence"])
        for item in suggestions:
            writer.writerow(
                [
                    item.image_path,
                    item.current_label,
                    item.suggested_label,
                    item.reason,
                    item.metadata_path,
                    f"{item.confidence:.3f}",
                ]
            )


def apply_suggestions(
    labels_df: pd.DataFrame,
    suggestions: list[Suggestion],
    image_column: str,
    label_column: str,
    min_confidence: float,
    apply_mode: str,
) -> tuple[pd.DataFrame, int]:
    threshold = min(max(min_confidence, 0.0), 1.0)
    if not suggestions:
        return labels_df, 0

    by_image: dict[str, Suggestion] = {}
    for suggestion in suggestions:
        existing = by_image.get(suggestion.image_path)
        if existing is None or suggestion.confidence > existing.confidence:
            by_image[suggestion.image_path] = suggestion

    updated = labels_df.copy()
    applied = 0

    for row_index, row in updated.iterrows():
        image_path = str(row[image_column]).strip().replace("\\", "/")
        if not image_path:
            continue

        suggestion = by_image.get(image_path)
        if suggestion is None or suggestion.confidence < threshold:
            continue

        current_label = str(row[label_column]).strip()
        if apply_mode == "empty_only" and current_label:
            continue
        if current_label == suggestion.suggested_label:
            continue

        updated.at[row_index, label_column] = suggestion.suggested_label
        applied += 1

    return updated, applied


def split_random(items: list[str], train_ratio: float, val_ratio: float, seed: int) -> tuple[list[str], list[str], list[str]]:
    rng = random.Random(seed)
    shuffled = items[:]
    rng.shuffle(shuffled)

    total = len(shuffled)
    train_count = int(total * train_ratio)
    val_count = int(total * val_ratio)
    if train_count + val_count > total:
        val_count = max(0, total - train_count)
    test_count = total - train_count - val_count

    train = shuffled[:train_count]
    val = shuffled[train_count : train_count + val_count]
    test = shuffled[train_count + val_count : train_count + val_count + test_count]
    return train, val, test


def split_stratified(
    frame: pd.DataFrame,
    image_column: str,
    label_column: str,
    train_ratio: float,
    val_ratio: float,
    seed: int,
) -> tuple[list[str], list[str], list[str]]:
    rng = random.Random(seed)
    train: list[str] = []
    val: list[str] = []
    test: list[str] = []

    for label, group in frame.groupby(label_column):
        items = group[image_column].astype(str).tolist()
        rng.shuffle(items)
        n = len(items)
        if n == 1:
            train.extend(items)
            continue

        train_count = int(n * train_ratio)
        val_count = int(n * val_ratio)
        test_count = n - train_count - val_count

        if train_count == 0:
            train_count = 1
            if test_count > 0:
                test_count -= 1
            elif val_count > 0:
                val_count -= 1

        if n >= 3 and val_count == 0:
            val_count = 1
            if train_count > 1:
                train_count -= 1
            elif test_count > 0:
                test_count -= 1

        test_count = n - train_count - val_count
        if test_count < 0:
            test_count = 0
            val_count = n - train_count

        train.extend(items[:train_count])
        val.extend(items[train_count : train_count + val_count])
        test.extend(items[train_count + val_count : train_count + val_count + test_count])

    rng.shuffle(train)
    rng.shuffle(val)
    rng.shuffle(test)
    return train, val, test


def write_split(path: Path, values: list[str]) -> None:
    ensure_parent(path)
    path.write_text("\n".join(values), encoding="utf-8")


def main() -> None:
    args = parse_args()
    if (args.train_ratio + args.val_ratio + args.test_ratio) <= 0:
        raise ValueError("train/val/test ratios sum must be > 0.")

    total_ratio = args.train_ratio + args.val_ratio + args.test_ratio
    train_ratio = args.train_ratio / total_ratio
    val_ratio = args.val_ratio / total_ratio
    _ = args.test_ratio / total_ratio

    set_seed(args.seed)
    config = load_config(args.config)
    paths = read_dataset_paths(config)
    dataset_root = paths.root
    raw_root = dataset_root / "raw"
    labels_csv = paths.labels_csv
    review_csv = dataset_root / "labels" / "review_queue.csv"

    images = find_images(raw_root)
    if not images:
        raise FileNotFoundError(f"No images found under {raw_root}. Collect data first.")

    image_rel_paths = [normalize_relative(path, dataset_root) for path in images]
    existing = load_existing_csv(labels_csv)
    merged = upsert_labels(
        existing=existing,
        image_paths=image_rel_paths,
        default_label=args.default_label,
        default_source=args.source,
        default_notes=args.notes,
        drop_missing_files=args.drop_missing_files,
        image_column=paths.image_column,
        label_column=paths.label_column,
    )

    suggestions_before = build_review_queue(
        dataset_root=dataset_root,
        labels_df=merged,
        image_column=paths.image_column,
        label_column=paths.label_column,
    )
    auto_applied = 0
    if args.auto_apply_suggestions:
        merged, auto_applied = apply_suggestions(
            labels_df=merged,
            suggestions=suggestions_before,
            image_column=paths.image_column,
            label_column=paths.label_column,
            min_confidence=args.suggestion_min_confidence,
            apply_mode=args.suggestion_apply_mode,
        )

    suggestions_after = build_review_queue(
        dataset_root=dataset_root,
        labels_df=merged,
        image_column=paths.image_column,
        label_column=paths.label_column,
    )

    ensure_parent(labels_csv)
    merged.to_csv(labels_csv, index=False, encoding="utf-8")
    write_review_queue(review_csv, suggestions_after)

    labeled = merged[merged[paths.label_column].astype(str).str.strip() != ""].copy()
    if labeled.empty:
        write_split(paths.train_split, [])
        write_split(paths.val_split, [])
        write_split(paths.test_split, [])
        print("No labeled rows yet. CSV and review queue are prepared. Fill labels, then rerun this script.")
        if args.auto_apply_suggestions:
            print(
                "Auto-apply suggestions: "
                f"applied={auto_applied}, "
                f"remaining={len(suggestions_after)}, "
                f"mode={args.suggestion_apply_mode}, "
                f"threshold={min(max(args.suggestion_min_confidence, 0.0), 1.0):.2f}"
            )
        print(f"Labels CSV: {labels_csv}")
        print(f"Review queue: {review_csv}")
        return

    if args.no_stratified:
        all_items = labeled[paths.image_column].astype(str).tolist()
        train, val, test = split_random(all_items, train_ratio, val_ratio, args.seed)
    else:
        train, val, test = split_stratified(
            labeled,
            image_column=paths.image_column,
            label_column=paths.label_column,
            train_ratio=train_ratio,
            val_ratio=val_ratio,
            seed=args.seed,
        )

    write_split(paths.train_split, train)
    write_split(paths.val_split, val)
    write_split(paths.test_split, test)

    print("prepare_labels completed.")
    print(f"Images found: {len(image_rel_paths)}")
    print(f"CSV rows: {len(merged)}")
    print(f"Labeled rows: {len(labeled)}")
    if args.auto_apply_suggestions:
        print(
            "Auto-apply suggestions: "
            f"applied={auto_applied}, "
            f"remaining={len(suggestions_after)}, "
            f"mode={args.suggestion_apply_mode}, "
            f"threshold={min(max(args.suggestion_min_confidence, 0.0), 1.0):.2f}"
        )
    else:
        print(f"Suggestions: {len(suggestions_after)} -> {review_csv}")
    print(f"train={len(train)} val={len(val)} test={len(test)}")
    print(f"Labels CSV: {labels_csv}")


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print(f"\nERROR: {exc}")
        raise
