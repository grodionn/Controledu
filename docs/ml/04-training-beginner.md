# Обучение для новичка (пошагово)

Это руководство рассчитано на пользователей без опыта в ML.

## 0) Подготовьте окружение

В корне проекта:

```bat
python -m venv .venv
.venv\Scripts\activate
pip install -r ml\requirements.txt
```

## 1) Подготовьте датасет

Ожидаемая корневая папка:

`dataset/`

Обязательные файлы:

- `dataset/labels/classification.csv`
- файлы изображений, на которые ссылается `image_path`

Необязательно, но рекомендуется:

- `dataset/splits/train.txt`
- `dataset/splits/val.txt`
- `dataset/splits/test.txt`

Если split-файлы отсутствуют, скрипты автоматически создадут случайное разбиение для первых экспериментов.

## 2) Подготовьте конфиг

```bat
copy ml\config.example.yaml ml\config.yaml
```

Отредактируйте `ml/config.yaml`:

- `dataset.root`
- `training.batch_size`
- `training.epochs`        
- `training.learning_rate`
- списки меток для binary/multiclass

## 3) Сначала обучите бинарную модель

```bat
python ml\train_binary.py --config ml\config.yaml
```

Ожидаемые артефакты:

- `ml/artifacts/binary-best.pt`
- `ml/artifacts/binary-metrics.json`

## 4) Оцените бинарную модель

```bat
python ml\eval.py --config ml\config.yaml --task binary
```

Проверьте:

- precision,
- recall,
- f1,
- false positives.

Если качество низкое, вернитесь к разметке и сбору сложных негативов.

## 5) Обучите мультиклассовую модель

```bat
python ml\train_multiclass.py --config ml\config.yaml
python ml\eval.py --config ml\config.yaml --task multiclass
```

Ожидаемые артефакты:

- `ml/artifacts/multiclass-best.pt`
- `ml/artifacts/multiclass-metrics.json`

## 6) Экспортируйте ONNX

```bat
python ml\export_onnx.py --config ml\config.yaml --task binary --verify
python ml\export_onnx.py --config ml\config.yaml --task multiclass --verify
```

## 7) Подключите модели к Student.Agent

Скопируйте файлы в runtime-папку моделей и настройте:

- `StudentAgent:Onnx:BinaryModelPath`
- `StudentAgent:Onnx:MulticlassModelPath`
- `StudentAgent:Onnx:ClassLabelsPath`

Затем установите:

- `StudentAgent:Onnx:EnableBinary=true`
- `StudentAgent:Onnx:EnableMulticlass=true`

## Рекомендации по железу

- Только CPU: подходит для небольших MVP-датасетов, но медленно.
- NVIDIA GPU: обучение значительно быстрее.
- Google Colab: хороший первый вариант с GPU, если локально GPU нет.

## Типичный план на первую неделю

1. День 1-2: сбор и разметка бинарного датасета.
2. День 3: обучение/оценка бинарного baseline.
3. День 4-5: исправление false positives через сложные негативы.
4. День 6-7: старт multiclass и экспорт ONNX.
