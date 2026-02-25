# Controledu

> Windows-first платформа для управления и мониторинга учебных устройств с явной индикацией активности и без скрытых механизмов.

Controledu построен по архитектуре **Web UI + .NET Desktop Host**:
- интерфейсы написаны на **React** (`apps/*`),
- десктопные приложения работают как **WinForms + WebView2** оболочки,
- системная логика и локальные/сетевые API реализованы на **.NET 8**.

## Что входит в проект

- **Приложение учителя**: `src/Controledu.Teacher.Host`
- **Приложение ученика**: `src/Controledu.Student.Host`
- **Фоновый агент ученика**: `src/Controledu.Student.Agent`
- **Frontend (React)**:
  - `apps/teacher-ui`
  - `apps/student-ui`
- **Self-host speech сервис (опционально)**:
  - `selfhost-speech` (TTS/STT для озвучки и субтитров)
- **Библиотеки детекции / ML-интеграции**:
  - `src/Controledu.Detection.Abstractions`
  - `src/Controledu.Detection.Core`
  - `src/Controledu.Detection.Onnx`

## Ключевые возможности MVP

- Подключение ученических устройств к учителю по PIN (с коротким TTL)
- Мониторинг устройств и предпросмотр экранов
- Журнал событий и алертов
- Отправка команд и файлов на устройства
- Teacher Chat / TTS-команды для взаимодействия с учеником
- STT (распознавание речи) и live-субтитры для ученика (через self-host speech)
- Оверлей у ученика с инклюзивными настройками (профиль доступности, субтитры)
- Синхронизация профиля доступности между учителем и учеником
- Фоновый агент с авто-переподключением и восстановлением связи
- Подготовленная ML-цепочка детекции (правила + ONNX-адаптеры)
- Локализация UI (`ru`, `en`, `kz`)

## Важные принципы (этика и безопасность)

- На устройстве ученика всегда есть **видимая индикация** подключения/мониторинга.
- Нет скрытых обходов ОС и «анти-тампера».
- Используются только легитимные способы запуска:
  - служба Windows,
  - автозапуск,
  - корпоративное развёртывание (GPO / Intune).
- Локальный API ученика остаётся:
  - только `localhost`,
  - защищён токеном `X-Controledu-LocalToken`.
- Отвязка ученика требует локальную проверку пароля администратора.

## Архитектура (кратко)

```text
Teacher.Host (.exe)
  ├─ поднимает Teacher.Server (ASP.NET Core + SignalR) в процессе
  └─ открывает teacher-ui в WebView2

Student.Host (.exe)
  ├─ поднимает локальный API (loopback only)
  ├─ открывает student-ui в WebView2
  └─ управляет жизненным циклом Student.Agent

Student.Agent
  ├─ подключается к Teacher.Server по SignalR
  ├─ отправляет heartbeat / статусы / кадры / алерты
  └─ принимает команды и файлы
```

## Стек технологий

**Backend / Desktop**
- C# / .NET 8
- ASP.NET Core
- SignalR
- SQLite + EF Core
- WinForms + WebView2
- Serilog

**Frontend**
- React + TypeScript + Vite
- Tailwind CSS
- TanStack Query
- SignalR JS client

**Speech (опционально)**
- Piper (TTS)
- faster-whisper (STT)

## Структура репозитория

```text
apps/
  teacher-ui/                  # React UI учителя
  student-ui/                  # React UI ученика (в т.ч. overlay)
src/
  Controledu.Teacher.Host      # Desktop host учителя (WebView2)
  Controledu.Teacher.Server    # LAN API/SignalR сервер учителя
  Controledu.Student.Host      # Desktop host ученика + local API
  Controledu.Student.Agent     # Фоновый агент ученика
  Controledu.Transport         # DTO и контракты SignalR/transport
  Controledu.Storage           # EF Core / SQLite
  Controledu.Common            # Общие модели/утилиты
  Controledu.Detection.*       # Детекция и ONNX-интеграции
selfhost-speech/               # Self-host TTS/STT сервис (опционально)
installer/inno/                # Inno Setup скрипты
scripts/                       # build/publish/version утилиты
ml/                            # Скрипты обучения / экспорта ONNX
docs/ml/                       # Документация по ML пайплайну
```

## Требования

Для разработки/запуска на Windows:
- Windows 10/11
- .NET SDK 8
- Node.js 20+
- WebView2 Runtime (обычно уже установлен)

Для сборки инсталляторов:
- Inno Setup 6

Для self-host speech (опционально):
- Python 3.11+
- зависимости из `selfhost-speech`
- для GPU STT на Windows: CUDA/cuBLAS/cuDNN (совместимые версии)

## Быстрый старт (разработка)

### 1. Запуск приложений

```bat
dotnet run --project src\Controledu.Teacher.Host
dotnet run --project src\Controledu.Student.Host
```

Опционально отдельно агент:

```bat
dotnet run --project src\Controledu.Student.Agent
```

### 2. UI-only режим (если нужен)

```bat
cd apps\teacher-ui && npm run dev
cd apps\student-ui && npm run dev
```

## Сборка и публикация

### Полная сборка проекта

```bat
scripts\build.cmd
```

### Публикация артефактов и инсталляторов

```bat
scripts\publish.cmd
```

Результаты сборки:

```text
artifacts/publish/teacher-host/
artifacts/publish/student-host/
artifacts/publish/student-agent/
artifacts/installers/TeacherInstaller.exe
artifacts/installers/StudentInstaller.exe
```

## Тесты

```bat
dotnet test
```

## Self-Host Speech (TTS/STT) — опционально

Папка: `selfhost-speech`

Используется для:
- **TTS** (озвучка сообщений)
- **STT** (распознавание речи учителя и live-субтитры у ученика)

### Типовой запуск на Windows через `.env` файл

Пример (PowerShell) с подхватом `.env.gpu-vpn`:

```powershell
Set-Location "C:\path\to\Controledu-hackathon\selfhost-speech"
$env:PATH = "$PWD\.venv\Scripts;$env:PATH"
Get-Content .env.gpu-vpn | ForEach-Object {
  if ($_ -match '^\s*#' -or $_ -match '^\s*$') { return }
  $k, $v = $_ -split '=', 2
  [Environment]::SetEnvironmentVariable($k.Trim(), $v, 'Process')
}
.\.venv\Scripts\python.exe -m app.main
```

### Что важно помнить

- `SPEECH_API_IP_ALLOWLIST` может давать `403`, если IP/подсеть не совпадают.
- Для PowerShell 5.1 русский текст в TTS нужно отправлять как **UTF-8 bytes**, иначе возможна поломка кодировки.
- Для STT на GPU в Windows нужны доступные CUDA DLL (`cublas64_12.dll`, `cudnn*.dll` и др.) в `PATH` или рядом с `python.exe`.

## Порты

- `40555` — UDP discovery
- `40556` — Teacher LAN server
- `40557` — Student local host (только loopback)

## Инсталляторы (Inno Setup)

Основные скрипты:
- `installer/inno/controledu-teacher.iss`
- `installer/inno/controledu-student.iss`

Обычно вручную запускать не нужно: `scripts\publish.cmd` выполняет это автоматически.

## CI / релизы

Workflow:
- `.github/workflows/release.yml`

Пайплайн собирает:
- React UI,
- .NET host/agent,
- Inno installers,
- portable-архивы,
- и публикует артефакты в GitHub Release.

## ML / AI-детекция

Материалы и скрипты для подготовки и обучения моделей находятся в:
- `docs/ml/*`
- `ml/*`

Пример команд:

```bat
copy ml\config.example.yaml ml\config.yaml
python ml\train_binary.py --config ml\config.yaml
python ml\train_multiclass.py --config ml\config.yaml
python ml\eval.py --config ml\config.yaml --task binary
python ml\export_onnx.py --config ml\config.yaml --task binary --verify
```

## Частые проблемы

1. **Не находится сервер учителя по сети**
- Проверьте firewall и UDP `40555`
- Попробуйте ручной ввод `IP:port` в UI ученика

2. **Не получается спарить устройство**
- Проверьте доступность `http://<teacher-ip>:40556/api/server/health`
- Пересоздайте PIN (срок жизни короткий)

3. **Внешний speech-сервис отвечает `403`**
- Проверьте `SPEECH_API_IP_ALLOWLIST`
- Убедитесь, что загружен правильный `.env` файл перед запуском

4. **GPU STT не стартует на Windows (`cublas64_12.dll` / `cudnn`)**
- Установите совместимые CUDA/cuBLAS/cuDNN
- Добавьте путь к DLL в `PATH` или положите DLL рядом с `selfhost-speech\.venv\Scripts\python.exe`

5. **Сборка инсталляторов падает**
- Проверьте установку Inno Setup и путь к `ISCC.exe`

## Лицензионные и организационные заметки

- Проект ориентирован на прозрачное использование в образовательной среде.
- Изменения UI живут в `apps/*`, системная логика — в `src/*`.
- Для функциональных изменений в коде соблюдайте правило репозитория: повышать patch-версию в `Directory.Build.props`.
