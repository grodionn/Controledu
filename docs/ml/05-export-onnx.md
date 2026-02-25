# Экспорт в ONNX и интеграция в рантайм

## 1) Экспортируйте чекпоинты в ONNX

Бинарная модель:

```bat
python ml\export_onnx.py --config ml\config.yaml --task binary --verify
```

Мультиклассовая модель:

```bat
python ml\export_onnx.py --config ml\config.yaml --task multiclass --verify
```

`--verify` выполняет быстрый onnxruntime-инференс на dummy-входе.

## 2) Сгенерированные файлы

Папка по умолчанию: `ml/artifacts/`

- `ai-ui-binary.onnx`
- `ai-ui-multiclass.onnx`
- `labels.txt` (генерируется из маппинга меток обучения)

## 3) Поместите файлы в рантайм Controledu

Скопируйте файлы модели в директорию рантайма (пример):

`src/Controledu.Student.Agent/bin/.../models/`

или в любой путь, доступный `Student.Agent`.

## 4) Настройте Student.Agent

Отредактируйте `src/Controledu.Student.Agent/appsettings.json`:

- `StudentAgent:Onnx:EnableBinary`
- `StudentAgent:Onnx:EnableMulticlass`
- `StudentAgent:Onnx:BinaryModelPath`
- `StudentAgent:Onnx:MulticlassModelPath`
- `StudentAgent:Onnx:ClassLabelsPath`
- `StudentAgent:Onnx:BinaryThreshold`
- `StudentAgent:Onnx:MulticlassThreshold`

## 5) Проверьте поведение в рантайме

1. Запустите Teacher + Student.
2. Откройте целевой AI UI в тестовой среде.
3. Проверьте логи Student.Agent:
- модель успешно загружена,
- нет предупреждений "detector disabled".
4. Проверьте live-ленту в Teacher UI (`AI Detection`).

## 6) Безопасное поведение по fallback-сценарию

Если файлы модели отсутствуют или повреждены:

- ONNX-детекторы автоматически отключаются,
- metadata-детектор продолжает работать,
- пайплайн не падает.

Это ожидаемое поведение и полезно для поэтапного внедрения.
