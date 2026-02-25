from __future__ import annotations

import argparse
from pathlib import Path

try:
    import numpy as np
    import pandas as pd
    import torch
    from sklearn.metrics import accuracy_score, f1_score, precision_score, recall_score
    from torch import nn
    from torch.utils.data import DataLoader

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
    )
except ModuleNotFoundError as exc:
    print(f"Missing Python package '{exc.name}'. Install dependencies with: pip install -r ml/requirements.txt")
    raise SystemExit(1) from exc


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Evaluate trained binary/multiclass model.")
    parser.add_argument("--config", required=True, help="Path to YAML config file.")
    parser.add_argument("--task", required=True, choices=["binary", "multiclass"], help="Model task.")
    parser.add_argument("--onnx", action="store_true", help="Evaluate ONNX model with onnxruntime instead of PyTorch.")
    return parser.parse_args()


def build_eval_rows(config: dict, task: str) -> tuple[dict, pd.DataFrame, dict]:
    paths = read_dataset_paths(config)
    ensure_dataset_ready(paths)
    labels = read_labels_dataframe(paths)

    if task == "binary":
        binary_cfg = config.get("binary", {})
        positives = {str(item).strip() for item in binary_cfg.get("positive_labels", ["ai_ui"])}
        negatives = {str(item).strip() for item in binary_cfg.get("negative_labels", ["not_ai_ui"])}
        labels = labels[labels[paths.label_column].astype(str).isin(positives | negatives)].copy()
        labels[paths.label_column] = labels[paths.label_column].astype(str).apply(lambda x: "ai_ui" if x in positives else "not_ai_ui")
        label_to_index = {"not_ai_ui": 0, "ai_ui": 1}
    else:
        class_labels = [str(item).strip() for item in config.get("multiclass", {}).get("class_labels", []) if str(item).strip()]
        if not class_labels:
            raise ValueError("multiclass.class_labels is empty in config.")
        labels = labels[labels[paths.label_column].astype(str).isin(class_labels)].copy()
        label_to_index = {label: index for index, label in enumerate(class_labels)}

    if labels.empty:
        raise RuntimeError("No labeled rows available for evaluation.")

    test_paths = read_split_file(paths.test_split)
    eval_rows = apply_split(labels, test_paths, paths.image_column) if test_paths else pd.DataFrame()

    if eval_rows.empty:
        train_rows, eval_rows = random_train_val_split(labels, train_ratio=0.8, seed=42)
        _ = train_rows
        print("Test split not found. Using random 20% holdout for evaluation.")

    print_dataset_summary("eval", eval_rows, paths.label_column)
    return config, eval_rows, label_to_index


def evaluate_onnx(onnx_path: Path, loader: DataLoader, provider: str) -> dict:
    try:
        import onnxruntime as ort
    except ModuleNotFoundError as exc:
        raise RuntimeError("onnxruntime is not installed. Run: pip install -r ml/requirements.txt") from exc

    session = ort.InferenceSession(str(onnx_path), providers=[provider])
    input_name = session.get_inputs()[0].name

    y_true: list[int] = []
    y_pred: list[int] = []
    for images, targets in loader:
        logits = session.run(None, {input_name: images.numpy().astype(np.float32)})[0]
        predictions = np.argmax(logits, axis=1)
        y_true.extend(targets.numpy().tolist())
        y_pred.extend(predictions.tolist())

    return {
        "accuracy": float(accuracy_score(y_true, y_pred)),
        "precision": float(precision_score(y_true, y_pred, average="macro", zero_division=0)),
        "recall": float(recall_score(y_true, y_pred, average="macro", zero_division=0)),
        "f1": float(f1_score(y_true, y_pred, average="macro", zero_division=0)),
    }


def main() -> None:
    args = parse_args()
    config = load_config(args.config)
    config, eval_rows, label_to_index = build_eval_rows(config, args.task)

    training_cfg = config.get("training", {})
    output_dir = Path(str(training_cfg.get("output_dir", "ml/artifacts"))).resolve()
    image_size = int(training_cfg.get("image_size", 224))
    batch_size = int(training_cfg.get("batch_size", 32))
    num_workers = int(training_cfg.get("num_workers", 2))
    paths = read_dataset_paths(config)

    eval_dataset = CsvImageDataset(
        eval_rows,
        paths.root,
        paths.image_column,
        paths.label_column,
        label_to_index,
        create_transforms(image_size, is_train=False),
    )
    eval_loader = create_loader(eval_dataset, batch_size, num_workers, shuffle=False)

    if args.onnx:
        model_name = str(config.get(args.task, {}).get("onnx_name", "model.onnx"))
        onnx_path = output_dir / model_name
        if not onnx_path.exists():
            raise FileNotFoundError(f"ONNX file not found: {onnx_path}. Run ml/export_onnx.py first.")
        provider = str(config.get("evaluation", {}).get("onnx_runtime_provider", "CPUExecutionProvider"))
        metrics = evaluate_onnx(onnx_path, eval_loader, provider)
        report_path = output_dir / f"{args.task}-onnx-eval.json"
        save_json(report_path, {"task": args.task, "runtime": "onnxruntime", "onnx_path": str(onnx_path), **metrics})
        print(f"[onnx eval] {metrics}")
        print(f"Saved: {report_path}")
        return

    checkpoint_name = str(config.get(args.task, {}).get("checkpoint_name", f"{args.task}-best.pt"))
    checkpoint_path = output_dir / checkpoint_name
    if not checkpoint_path.exists():
        raise FileNotFoundError(
            f"Checkpoint not found: {checkpoint_path}. "
            f"Run ml/train_{args.task}.py first."
        )

    checkpoint = torch.load(checkpoint_path, map_location="cpu")
    model = create_model(
        model_name=str(checkpoint.get("model_name", "mobilenet_v3_small")),
        num_classes=int(checkpoint.get("num_classes", len(label_to_index))),
        use_pretrained=False,
    )
    model.load_state_dict(checkpoint["model_state_dict"])

    device = get_device()
    model = model.to(device)
    criterion = nn.CrossEntropyLoss()
    metrics = evaluate_model(model, eval_loader, device, criterion)

    report_path = output_dir / f"{args.task}-torch-eval.json"
    save_json(report_path, {"task": args.task, "runtime": "torch", "checkpoint": str(checkpoint_path), **metrics})
    print(f"[torch eval] {metrics}")
    print(f"Saved: {report_path}")


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print(f"\nERROR: {exc}")
        raise
