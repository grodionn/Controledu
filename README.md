# Controledu (Web UI + .NET 8 Hosts)

Controledu — Windows-first monitoring and device orchestration platform for managed endpoints (with explicit consent).

- `Console` app: one desktop `.exe` (`Controledu.Teacher.Host`) with embedded ASP.NET Core server + React UI in WebView2.
- `Endpoint` app: one desktop `.exe` (`Controledu.Student.Host`) with local ASP.NET Core API + React UI in WebView2, plus background `Controledu.Student.Agent`.

## Legal / Ethics / Safety

- Endpoint UI always shows visible state: **Connected to Server / Monitoring active**.
- Server-side audit log is mandatory and enabled (`connects`, `pairing`, `alerts`, `file dispatch`).
- No stealth persistence or OS bypass. Only legitimate options:
  - Windows Service for `Student.Agent`
  - autostart
  - enterprise deployment via GPO/Intune

## Tech Stack

Backend/system:
- C# / .NET 8
- ASP.NET Core API + SignalR
- SQLite + EF Core
- Serilog
- Password hashing: PBKDF2-SHA256
- Secret protection: DPAPI on Windows (`ProtectedData`)

Frontend:
- React + TypeScript + Vite
- TailwindCSS
- shadcn-style component layer (cards/buttons/badges/inputs)
- TanStack Query
- SignalR JS client

Desktop shell:
- WinForms + WebView2 (`Microsoft.Web.WebView2`)

## Repository Layout

```text
Controledu.sln
apps/
  teacher-ui/                 # React UI for teacher (built to dist)
  student-ui/                 # React UI for student (built to dist)
src/
  Controledu.Common           # crypto, hashing/chunk helpers, models
  Controledu.Detection.Abstractions
  Controledu.Detection.Core
  Controledu.Detection.Onnx
  Controledu.Discovery        # UDP discovery client/server helpers
  Controledu.Storage          # EF Core + SQLite stores
  Controledu.Transport        # SignalR routes/methods + DTOs
  Controledu.Teacher.Server   # ASP.NET Core teacher LAN server
  Controledu.Teacher.Host     # desktop host (WebView2 + in-process server)
  Controledu.Student.Agent    # background worker (capture, detectors, file receive)
  Controledu.Student.Host     # desktop host + local API + agent control
installer/
  inno/
    controledu-teacher.iss
    controledu-student.iss
scripts/
  build.cmd
  publish.cmd
  version.cmd
tests/
  Controledu.Tests
docs/
  ml/
ml/
  train_binary.py
  train_multiclass.py
  export_onnx.py
  eval.py
.github/workflows/
  release.yml
AGENTS.md
```

## Component Map (Who Talks To Whom)

```text
Teacher.Host (.exe)
  +- runs Teacher.Server in-process (40556)
  L- opens WebView2 -> http://127.0.0.1:40556 (console React UI)

Teacher.Server
  +- UDP discovery responder (40555)
  +- SignalR /hubs/student  <- Student.Agent
  +- SignalR /hubs/teacher  <- Console UI
  L- file APIs + pairing APIs + audit APIs

Student.Host (.exe)
  +- runs local API host (127.0.0.1:40557)
  +- opens WebView2 -> http://127.0.0.1:40557 (endpoint React UI)
  L- controls Student.Agent process (start/stop/autostart)

Student.Agent
  +- reads encrypted binding from SQLite (DPAPI-protected token)
  +- connects SignalR to Teacher.Server
  +- sends heartbeat/frames/alerts
  L- receives file transfer with chunk resume + SHA256 verify
```

## Required MVP Features Implemented

- Endpoint first-run wizard:
  - set admin password
  - configure agent autostart
  - discover control servers by UDP broadcast
  - pair by PIN (60s lifetime)
- Secure binding persistence:
  - `serverId`, `fingerprint`, `clientId`, `token`
  - token stored protected via DPAPI (Windows)
- Network resilience:
  - SignalR auto-reconnect/backoff
  - heartbeat
  - chunked file transfer with resume (`missing chunk indexes`)
  - screen stream continues after reconnect, adaptive FPS/JPEG quality
- Console UI:
  - device list + online/offline + last frame thumbnails
  - selected large frame view
  - file send to all / selected
  - alerts + audit feed
  - remove device from managed list (token revoked + forced unpair command)
- Detection pipeline (student-side, ML-ready):
  - Stage A: `PerceptualHashChangeFilter` (frame-change prefilter)
  - Stage B: `WindowMetadataDetector` (keyword/whitelist rules)
  - Stage C: ONNX adapters (`OnnxBinaryAiDetector`, `OnnxMulticlassAiDetector`) with disabled-safe fallback
  - Stage D: `TemporalVotingSmoother` (`2 of 3` + cooldown)
- Detection operations UI (production mode):
  - Teacher `AI Detection` page with live feed and filters
  - Detection policy tuning and dataset collection controls removed from UI (hardcoded in backend)
  - Teacher `Settings` page includes:
    - enable/disable AI warning popups
    - enable/disable desktop notifications
    - enable/disable notification sound
    - notification volume slider
  - Student admin-only `Security/Monitoring` page (self-test, diagnostics export, read-only detection status)
- Unpair flow:
  - only with local admin password on Endpoint UI
- Agent hardening:
  - stopping agent requires local admin password
  - disabling autostart requires local admin password
  - Student UI exposes these controls as protected actions
  - Student.Host keeps agent running in background and can auto-restart it after unexpected exit
- Device alias:
  - persisted local student device name (`SQLite`) with admin-password-protected rename endpoint
  - updated alias is used for next agent registration to teacher
- Localization:
  - React UIs include built-in language switcher (`ru`, `en`, `kz`)

## Ports and Protocols

- UDP discovery: `40555`
- Teacher LAN server: `40556`
- Student local host: `40557` (`localhost` only)

SignalR:
- `/hubs/student` (agent -> teacher)
- `/hubs/teacher` (teacher UI -> teacher)

Files:
- chunk size: `256KB`
- integrity: SHA256
- resume: `missing chunk indexes`

## Migration Plan (Implemented)

1. Freeze contracts: DTO + transport constants (`Common/Transport`).
2. Build `Teacher.Server` (SignalR hubs + discovery + pairing + auth + file APIs).
3. Build `Student.Agent` (register, heartbeat, frame push, file receive, detectors).
4. Build `teacher-ui` (React + SignalR + REST for PIN/files/audit).
5. Build `student-ui` (React wizard + local API integration).
6. Build Desktop Hosts (WebView2 + localhost servers):
   - `Teacher.Host` in-process teacher server
   - `Student.Host` local API + agent control
7. Build release artifacts + installers + CI release workflow.

## Prerequisites

- Windows 10/11
- .NET SDK 8
- Node.js 20+
- WebView2 Runtime (usually present on modern Windows)
- Inno Setup 6 (for installer build)

## Build

### Fast local build

```bat
scripts\build.cmd
```

### Build release artifacts (`.exe` + installers)

```bat
scripts\publish.cmd
```

Outputs:

```text
artifacts/publish/teacher-host/
artifacts/publish/student-host/
artifacts/publish/student-agent/
artifacts/installers/TeacherInstaller.exe
artifacts/installers/StudentInstaller.exe
```

Notes:
- `publish.cmd` now also compiles Inno installers.
- If `ISCC.exe` is installed in a non-default path, set `INNO_ISCC` to full path before running publish.

## Run (Dev)

Desktop hosts:

```bat
dotnet run --project src\Controledu.Teacher.Host
dotnet run --project src\Controledu.Student.Host
```

Agent standalone (optional):

```bat
dotnet run --project src\Controledu.Student.Agent
```

UI-only dev mode (optional):

```bat
cd apps\teacher-ui && npm run dev
cd apps\student-ui && npm run dev
```

## Build Installers (Inno)

`scripts\publish.cmd` already runs this step automatically. Use manual commands below only when needed.

Teacher:

```bat
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\inno\controledu-teacher.iss /DMyAppVersion=0.1.0 /DSourceDir="%CD%\artifacts\publish\teacher-host" /DOutputDir="%CD%\artifacts\installers"
```

Student:

```bat
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\inno\controledu-student.iss /DMyAppVersion=0.1.0 /DSourceHostDir="%CD%\artifacts\publish\student-host" /DSourceAgentDir="%CD%\artifacts\publish\student-agent" /DOutputDir="%CD%\artifacts\installers"
```

Result:

```text
artifacts/installers/TeacherInstaller.exe
artifacts/installers/StudentInstaller.exe
```

Installer license page source:
- `installer/inno/eula-ru.txt`

## Student.Agent as Windows Service (Legitimate Option)

Manual example:

```bat
sc create ControleduStudentAgent binPath= "\"C:\Program Files\Controledu\Student\StudentAgent\Controledu.Student.Agent.exe\" --service" start= auto
sc start ControleduStudentAgent
```

Remove service:

```bat
sc stop ControleduStudentAgent
sc delete ControleduStudentAgent
```

## CI Release

Workflow: `.github/workflows/release.yml`

Trigger: git tag `v*`.

Pipeline steps:
- build React UIs
- publish .NET hosts/agent
- compile Inno installers
- create portable zip archives
- upload assets to GitHub Release

## ML Workspace (New)

Documentation:

- `docs/ml/01-overview.md`
- `docs/ml/02-data-collection.md`
- `docs/ml/03-labeling-guide.md`
- `docs/ml/04-training-beginner.md`
- `docs/ml/05-export-onnx.md`
- `docs/ml/06-evaluation-and-thresholds.md`
- `docs/ml/dataset-template/*`

Training scripts:

```bat
copy ml\config.example.yaml ml\config.yaml
python ml\train_binary.py --config ml\config.yaml
python ml\train_multiclass.py --config ml\config.yaml
python ml\eval.py --config ml\config.yaml --task binary
python ml\eval.py --config ml\config.yaml --task multiclass
python ml\export_onnx.py --config ml\config.yaml --task binary --verify
python ml\export_onnx.py --config ml\config.yaml --task multiclass --verify
```

If dataset is missing, scripts fail with clear error and links to `docs/ml/*`.

## Tests

```bat
dotnet test
```

Covered:
- PIN expiry and one-time consume
- password hashing/verification
- chunk resume state machine + hash validation
- perceptual hash frame-change behavior
- metadata rule detector behavior (keywords/whitelist/null metadata)
- temporal voting smoothing and cooldown suppression
- detection pipeline fallback when ONNX model is missing
- detection pipeline cached-result reuse on unchanged frames

## Security Notes (MVP)

- Pairing PIN is one-time, default 60s.
- Teacher validates paired client token on registration, binds each StudentHub connection to that clientId for subsequent hub calls, and validates token on file resume/download endpoints.
- Student local API is bound to loopback and requires in-memory token header (`X-Controledu-LocalToken`) for all sensitive endpoints.
- Student local API now enforces admin password for `/api/agent/stop` and for `/api/agent/autostart` when disabling autostart.
- Student desktop host does not exit on normal window close: it minimizes to tray and continues running in background.
- Student UI is lock-screen protected: full interface unlock requires admin password, and auto-lock triggers after inactivity.
- Student host auto-restarts `Student.Agent` when it exits unexpectedly.
- Teacher can revoke a student pairing via `DELETE /api/students/{clientId}`; online student receives forced unpair command and local binding is cleared.

## Troubleshooting

1. Discovery returns no servers:
- check firewall for UDP `40555`
- try manual `IP:port` in Student UI

2. Student cannot pair:
- verify Teacher is reachable: `http://<teacher-ip>:40556/api/server/health`
- regenerate PIN (PIN TTL is short)

3. Empty screen preview:
- MVP capture is Windows-only (`Graphics.CopyFromScreen`)
- verify Student.Agent is running

4. Installer build fails:
- ensure Inno Setup is installed and `ISCC.exe` path is correct

5. Publish warnings about `WindowsBase` conflicts:
- known WebView2 transitive warning in WinForms projects; does not block build/publish.

## Notes

- Repository structure now matches the Web UI + .NET Host layout shown above.


