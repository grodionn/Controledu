# AGENTS.md

## Purpose

This repository contains Controledu MVP in **Web UI + .NET Host** architecture.

- Teacher desktop: `src/Controledu.Teacher.Host`
- Student desktop: `src/Controledu.Student.Host`
- Background agent: `src/Controledu.Student.Agent`
- Frontends: `apps/teacher-ui`, `apps/student-ui`
- Detection libraries:
  - `src/Controledu.Detection.Abstractions`
  - `src/Controledu.Detection.Core`
  - `src/Controledu.Detection.Onnx`
- ML docs and starter scripts:
  - `docs/ml/*`
  - `ml/*`

## Canonical Commands

### Build everything

```bat
scripts\build.cmd
```

### Publish release artifacts

```bat
scripts\publish.cmd
```

### Run apps in development

```bat
dotnet run --project src\Controledu.Teacher.Host
dotnet run --project src\Controledu.Student.Host
```

Optional agent standalone:

```bat
dotnet run --project src\Controledu.Student.Agent
```

### Tests

```bat
dotnet test
```

### ML starter scripts

```bat
copy ml\config.example.yaml ml\config.yaml
python ml\train_binary.py --config ml\config.yaml
python ml\train_multiclass.py --config ml\config.yaml
python ml\eval.py --config ml\config.yaml --task binary
python ml\export_onnx.py --config ml\config.yaml --task binary --verify
```

## Ports

- UDP discovery: `40555`
- Teacher LAN server: `40556`
- Student local host: `40557` (loopback only)

## Installer Pipeline

- Inno scripts:
  - `installer/inno/controledu-teacher.iss`
  - `installer/inno/controledu-student.iss`
- CI release workflow:
  - `.github/workflows/release.yml`

## Key Architecture Rules

1. UI remains React-based in `apps/*`; desktop shells render via WebView2.
2. Business/system logic remains in C#/.NET projects under `src/*`.
3. Student unpair must always require local admin password verification.
4. No stealth anti-tamper techniques. Only legitimate service/autostart/deployment methods.
5. Student local API must stay loopback-only and token-protected (`X-Controledu-LocalToken`).

## Versioning Rule

- For every functional change (new feature or behavior change), bump the patch version immediately in `Directory.Build.props` (`ControleduDisplayVersion`, `Version`, `AssemblyVersion`, `FileVersion`) before or along with the code change. Do not postpone version bumps.

## FAQ

### Why are there build warnings for `WindowsBase`?
WebView2 package pulls additional assemblies for desktop stacks; these warnings are expected in this setup and do not block build/publish.

### Where are distributables after publish?

```text
artifacts/publish/teacher-host/
artifacts/publish/student-host/
artifacts/publish/student-agent/
```

### How does UI get embedded in hosts?
`apps/*/dist` is copied into host `wwwroot` during build/publish via project file content rules.
