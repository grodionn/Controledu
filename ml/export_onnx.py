from __future__ import annotations

import argparse
from pathlib import Path

try:
    import numpy as np
    import torch

    from common import create_model, load_config
except ModuleNotFoundError as exc:
    print(f"Missing Python package '{exc.name}'. Install dependencies with: pip install -r ml/requirements.txt")
    raise SystemExit(1) from exc


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Export trained model checkpoint to ONNX.")
    parser.add_argument("--config", required=True, help="Path to YAML config file.")
    parser.add_argument("--task", required=True, choices=["binary", "multiclass"], help="Model task.")
    parser.add_argument("--verify", action="store_true", help="Run a quick onnxruntime inference check.")
    return parser.parse_args()


def verify_onnx(onnx_path: Path, image_size: int) -> None:
    try:
        import onnxruntime as ort
    except ModuleNotFoundError as exc:
        raise RuntimeError("onnxruntime is not installed. Run: pip install -r ml/requirements.txt") from exc

    session = ort.InferenceSession(str(onnx_path), providers=["CPUExecutionProvider"])
    input_name = session.get_inputs()[0].name
    dummy = np.random.randn(1, 3, image_size, image_size).astype(np.float32)
    output = session.run(None, {input_name: dummy})[0]
    print(f"ONNX verification passed. Output shape: {output.shape}")


def main() -> None:
    args = parse_args()
    config = load_config(args.config)
    training_cfg = config.get("training", {})
    task_cfg = config.get(args.task, {})

    output_dir = Path(str(training_cfg.get("output_dir", "ml/artifacts"))).resolve()
    checkpoint_name = str(task_cfg.get("checkpoint_name", f"{args.task}-best.pt"))
    onnx_name = str(task_cfg.get("onnx_name", f"{args.task}.onnx"))
    checkpoint_path = output_dir / checkpoint_name
    onnx_path = output_dir / onnx_name

    if not checkpoint_path.exists():
        raise FileNotFoundError(f"Checkpoint not found: {checkpoint_path}. Train model first.")

    checkpoint = torch.load(checkpoint_path, map_location="cpu")
    model_name = str(checkpoint.get("model_name", "mobilenet_v3_small"))
    num_classes = int(checkpoint.get("num_classes", 2))
    image_size = int(checkpoint.get("image_size", training_cfg.get("image_size", 224)))

    model = create_model(model_name=model_name, num_classes=num_classes, use_pretrained=False)
    model.load_state_dict(checkpoint["model_state_dict"])
    model.eval()

    dummy_input = torch.randn(1, 3, image_size, image_size, dtype=torch.float32)
    onnx_path.parent.mkdir(parents=True, exist_ok=True)
    torch.onnx.export(
        model,
        dummy_input,
        str(onnx_path),
        input_names=["input"],
        output_names=["logits"],
        dynamic_axes={"input": {0: "batch"}, "logits": {0: "batch"}},
        opset_version=17,
    )

    print(f"Exported ONNX: {onnx_path}")

    labels = checkpoint.get("label_to_index", {})
    if labels:
        labels_path = output_dir / "labels.txt"
        ordered = [label for label, _ in sorted(labels.items(), key=lambda item: item[1])]
        labels_path.write_text("\n".join(ordered), encoding="utf-8")
        print(f"Saved labels: {labels_path}")

    if args.verify:
        verify_onnx(onnx_path, image_size)


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print(f"\nERROR: {exc}")
        raise
