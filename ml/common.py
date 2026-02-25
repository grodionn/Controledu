from __future__ import annotations

import json
import os
import random
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, Iterable, List, Tuple

import numpy as np
import pandas as pd
import torch
import yaml
from PIL import Image
from sklearn.metrics import accuracy_score, f1_score, precision_score, recall_score
from torch import nn
from torch.utils.data import DataLoader, Dataset
from torchvision import models, transforms


DOC_HINT = (
    "Dataset is missing or invalid.\n"
    "Read:\n"
    "  - docs/ml/02-data-collection.md\n"
    "  - docs/ml/03-labeling-guide.md\n"
    "  - docs/ml/dataset-template/*\n"
)


@dataclass(frozen=True)
class DatasetPaths:
    root: Path
    labels_csv: Path
    train_split: Path
    val_split: Path
    test_split: Path
    image_column: str
    label_column: str


class CsvImageDataset(Dataset):
    def __init__(
        self,
        rows: pd.DataFrame,
        root: Path,
        image_column: str,
        label_column: str,
        label_to_index: Dict[str, int],
        transform: transforms.Compose | None,
    ) -> None:
        self._rows = rows.reset_index(drop=True)
        self._root = root
        self._image_column = image_column
        self._label_column = label_column
        self._label_to_index = label_to_index
        self._transform = transform

    def __len__(self) -> int:
        return len(self._rows)

    def __getitem__(self, index: int) -> Tuple[torch.Tensor, int]:
        row = self._rows.iloc[index]
        image_path = self._root / str(row[self._image_column])
        label = str(row[self._label_column]).strip()

        if label not in self._label_to_index:
            raise KeyError(f"Label '{label}' is not mapped. Check config labels.")
        if not image_path.exists():
            raise FileNotFoundError(f"Image file is missing: {image_path}")

        image = Image.open(image_path).convert("RGB")
        if self._transform is not None:
            image = self._transform(image)
        return image, self._label_to_index[label]


def load_config(path: str) -> dict:
    config_path = Path(path)
    if not config_path.exists():
        raise FileNotFoundError(f"Config file does not exist: {config_path}")
    with config_path.open("r", encoding="utf-8") as file:
        return yaml.safe_load(file)


def read_dataset_paths(config: dict) -> DatasetPaths:
    dataset = config.get("dataset", {})
    root = Path(str(dataset.get("root", "dataset"))).resolve()
    labels_csv = root / str(dataset.get("labels_csv", "labels/classification.csv"))
    train_split = root / str(dataset.get("train_split", "splits/train.txt"))
    val_split = root / str(dataset.get("val_split", "splits/val.txt"))
    test_split = root / str(dataset.get("test_split", "splits/test.txt"))
    image_column = str(dataset.get("image_column", "image_path"))
    label_column = str(dataset.get("label_column", "label"))
    return DatasetPaths(root, labels_csv, train_split, val_split, test_split, image_column, label_column)


def ensure_dataset_ready(paths: DatasetPaths) -> None:
    if not paths.root.exists():
        raise FileNotFoundError(f"{DOC_HINT}\nDataset root not found: {paths.root}")
    if not paths.labels_csv.exists():
        raise FileNotFoundError(f"{DOC_HINT}\nLabels CSV not found: {paths.labels_csv}")


def read_labels_dataframe(paths: DatasetPaths) -> pd.DataFrame:
    frame = pd.read_csv(paths.labels_csv)
    required = {paths.image_column, paths.label_column}
    missing = required - set(frame.columns)
    if missing:
        raise ValueError(f"CSV is missing required columns: {sorted(missing)}")
    return frame


def read_split_file(path: Path) -> set[str]:
    if not path.exists():
        return set()
    return {
        line.strip().replace("\\", "/")
        for line in path.read_text(encoding="utf-8").splitlines()
        if line.strip()
    }


def apply_split(rows: pd.DataFrame, split_paths: set[str], image_column: str) -> pd.DataFrame:
    if not split_paths:
        return rows
    normalized = rows[image_column].astype(str).str.replace("\\", "/", regex=False)
    mask = normalized.isin(split_paths)
    return rows.loc[mask].copy()


def random_train_val_split(rows: pd.DataFrame, train_ratio: float = 0.8, seed: int = 42) -> Tuple[pd.DataFrame, pd.DataFrame]:
    shuffled = rows.sample(frac=1.0, random_state=seed).reset_index(drop=True)
    cutoff = max(1, int(len(shuffled) * train_ratio))
    train_rows = shuffled.iloc[:cutoff].copy()
    val_rows = shuffled.iloc[cutoff:].copy()
    if val_rows.empty:
        val_rows = train_rows.sample(frac=0.2, random_state=seed).copy()
    return train_rows, val_rows


def create_transforms(image_size: int, is_train: bool) -> transforms.Compose:
    if is_train:
        return transforms.Compose(
            [
                transforms.Resize((image_size, image_size)),
                transforms.RandomHorizontalFlip(p=0.5),
                transforms.ColorJitter(brightness=0.2, contrast=0.2, saturation=0.15),
                transforms.ToTensor(),
                transforms.Normalize(mean=[0.485, 0.456, 0.406], std=[0.229, 0.224, 0.225]),
            ]
        )
    return transforms.Compose(
        [
            transforms.Resize((image_size, image_size)),
            transforms.ToTensor(),
            transforms.Normalize(mean=[0.485, 0.456, 0.406], std=[0.229, 0.224, 0.225]),
        ]
    )


def create_model(model_name: str, num_classes: int, use_pretrained: bool) -> nn.Module:
    name = model_name.strip().lower()
    if name != "mobilenet_v3_small":
        raise ValueError(f"Unsupported model_name '{model_name}'. Use 'mobilenet_v3_small' for MVP.")

    weights = models.MobileNet_V3_Small_Weights.DEFAULT if use_pretrained else None
    model = models.mobilenet_v3_small(weights=weights)
    in_features = model.classifier[-1].in_features
    model.classifier[-1] = nn.Linear(in_features, num_classes)
    return model


def set_seed(seed: int) -> None:
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    if torch.cuda.is_available():
        torch.cuda.manual_seed_all(seed)


def create_loader(dataset: Dataset, batch_size: int, num_workers: int, shuffle: bool) -> DataLoader:
    return DataLoader(dataset, batch_size=batch_size, shuffle=shuffle, num_workers=num_workers, pin_memory=torch.cuda.is_available())


def evaluate_model(model: nn.Module, loader: DataLoader, device: torch.device, criterion: nn.Module) -> Dict[str, float]:
    model.eval()
    losses: List[float] = []
    y_true: List[int] = []
    y_pred: List[int] = []
    with torch.no_grad():
        for images, labels in loader:
            images = images.to(device)
            labels = labels.to(device)
            logits = model(images)
            loss = criterion(logits, labels)
            losses.append(float(loss.item()))
            predictions = torch.argmax(logits, dim=1)
            y_true.extend(labels.cpu().tolist())
            y_pred.extend(predictions.cpu().tolist())

    if not y_true:
        return {"loss": 0.0, "accuracy": 0.0, "precision": 0.0, "recall": 0.0, "f1": 0.0}

    return {
        "loss": float(np.mean(losses)) if losses else 0.0,
        "accuracy": float(accuracy_score(y_true, y_pred)),
        "precision": float(precision_score(y_true, y_pred, average="macro", zero_division=0)),
        "recall": float(recall_score(y_true, y_pred, average="macro", zero_division=0)),
        "f1": float(f1_score(y_true, y_pred, average="macro", zero_division=0)),
    }


def save_json(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, ensure_ascii=False), encoding="utf-8")


def get_device() -> torch.device:
    return torch.device("cuda" if torch.cuda.is_available() else "cpu")


def print_dataset_summary(name: str, rows: pd.DataFrame, label_column: str) -> None:
    counts = rows[label_column].value_counts().to_dict()
    print(f"[{name}] samples: {len(rows)} | class distribution: {counts}")
