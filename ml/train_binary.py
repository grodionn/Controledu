from __future__ import annotations

import argparse
from pathlib import Path

try:
    import pandas as pd
    import torch
    from torch import nn
    from tqdm import tqdm

    from common import (
        CsvImageDataset,
        apply_split,
        create_loader,
        create_model,
        create_transforms,
        ensure_dataset_ready,
        evaluate_model,
        get_device,
        load_config,
        print_dataset_summary,
        random_train_val_split,
        read_dataset_paths,
        read_labels_dataframe,
        read_split_file,
        save_json,
        set_seed,
    )
except ModuleNotFoundError as exc:
    print(f"Missing Python package '{exc.name}'. Install dependencies with: pip install -r ml/requirements.txt")
    raise SystemExit(1) from exc


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Train binary AI UI detector (ai_ui vs not_ai_ui).")
    parser.add_argument("--config", required=True, help="Path to YAML config file (see ml/config.example.yaml).")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    config = load_config(args.config)
    paths = read_dataset_paths(config)
    ensure_dataset_ready(paths)

    training_cfg = config.get("training", {})
    binary_cfg = config.get("binary", {})

    seed = int(training_cfg.get("seed", 42))
    set_seed(seed)

    labels = read_labels_dataframe(paths)
    positive_labels = {str(item).strip() for item in binary_cfg.get("positive_labels", ["ai_ui"])}
    negative_labels = {str(item).strip() for item in binary_cfg.get("negative_labels", ["not_ai_ui"])}
    allowed = positive_labels | negative_labels
    labels = labels[labels[paths.label_column].astype(str).isin(allowed)].copy()

    if labels.empty:
        raise RuntimeError(
            "No rows with binary labels were found. "
            "Check config.binary.positive_labels / negative_labels and docs/ml/03-labeling-guide.md"
        )

    labels[paths.label_column] = labels[paths.label_column].astype(str).apply(lambda x: "ai_ui" if x in positive_labels else "not_ai_ui")

    train_split = read_split_file(paths.train_split)
    val_split = read_split_file(paths.val_split)
    train_rows = apply_split(labels, train_split, paths.image_column) if train_split else pd.DataFrame()
    val_rows = apply_split(labels, val_split, paths.image_column) if val_split else pd.DataFrame()

    if train_rows.empty or val_rows.empty:
        train_rows, val_rows = random_train_val_split(labels, train_ratio=0.8, seed=seed)
        print("Split files are missing or empty. Using random 80/20 split.")

    print_dataset_summary("train", train_rows, paths.label_column)
    print_dataset_summary("val", val_rows, paths.label_column)

    label_to_index = {"not_ai_ui": 0, "ai_ui": 1}
    image_size = int(training_cfg.get("image_size", 224))
    batch_size = int(training_cfg.get("batch_size", 32))
    num_workers = int(training_cfg.get("num_workers", 2))

    train_dataset = CsvImageDataset(
        train_rows,
        paths.root,
        paths.image_column,
        paths.label_column,
        label_to_index,
        create_transforms(image_size, is_train=True),
    )
    val_dataset = CsvImageDataset(
        val_rows,
        paths.root,
        paths.image_column,
        paths.label_column,
        label_to_index,
        create_transforms(image_size, is_train=False),
    )

    train_loader = create_loader(train_dataset, batch_size, num_workers, shuffle=True)
    val_loader = create_loader(val_dataset, batch_size, num_workers, shuffle=False)

    device = get_device()
    model = create_model(
        model_name=str(training_cfg.get("model_name", "mobilenet_v3_small")),
        num_classes=2,
        use_pretrained=bool(training_cfg.get("use_pretrained", True)),
    ).to(device)

    criterion = nn.CrossEntropyLoss()
    optimizer = torch.optim.AdamW(
        model.parameters(),
        lr=float(training_cfg.get("learning_rate", 5e-4)),
        weight_decay=float(training_cfg.get("weight_decay", 1e-5)),
    )

    epochs = int(training_cfg.get("epochs", 8))
    output_dir = Path(str(training_cfg.get("output_dir", "ml/artifacts"))).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    checkpoint_path = output_dir / str(binary_cfg.get("checkpoint_name", "binary-best.pt"))
    metrics_path = output_dir / "binary-metrics.json"

    best_f1 = -1.0
    history: list[dict] = []

    for epoch in range(1, epochs + 1):
        model.train()
        train_losses: list[float] = []

        progress = tqdm(train_loader, desc=f"binary epoch {epoch}/{epochs}", unit="batch")
        for images, targets in progress:
            images = images.to(device)
            targets = targets.to(device)

            optimizer.zero_grad(set_to_none=True)
            logits = model(images)
            loss = criterion(logits, targets)
            loss.backward()
            optimizer.step()

            train_losses.append(float(loss.item()))
            progress.set_postfix(loss=f"{loss.item():.4f}")

        val_metrics = evaluate_model(model, val_loader, device, criterion)
        train_loss = float(sum(train_losses) / max(1, len(train_losses)))
        epoch_metrics = {
            "epoch": epoch,
            "train_loss": train_loss,
            **val_metrics,
        }
        history.append(epoch_metrics)
        print(f"[binary] epoch={epoch} train_loss={train_loss:.4f} val_f1={val_metrics['f1']:.4f} val_acc={val_metrics['accuracy']:.4f}")

        if val_metrics["f1"] >= best_f1:
            best_f1 = val_metrics["f1"]
            torch.save(
                {
                    "model_state_dict": model.state_dict(),
                    "model_name": str(training_cfg.get("model_name", "mobilenet_v3_small")),
                    "num_classes": 2,
                    "label_to_index": label_to_index,
                    "best_val_f1": best_f1,
                    "image_size": image_size,
                },
                checkpoint_path,
            )

    save_json(
        metrics_path,
        {
            "task": "binary",
            "best_val_f1": best_f1,
            "history": history,
            "checkpoint": str(checkpoint_path),
        },
    )

    print(f"Binary training completed.\nBest checkpoint: {checkpoint_path}\nMetrics: {metrics_path}")


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print(f"\nERROR: {exc}")
        raise
