# Controledu Self-Host Speech API

Self-host `TTS + STT` service for `Controledu` (separate daemon/app) with:

- `TTS`: `Piper` (offline, local)
- `STT`: `faster-whisper` (offline, local)
- HTTP API (`FastAPI`) for easy integration with `Teacher.Server`, `Student.Agent`, or your own tools
- Docker deployment + `nginx` reverse proxy config for `tts.kilocraft.org`

This folder is intentionally standalone and can be deployed on a separate Linux server/VPS.

## MVP topology (recommended for your case)

If your public server is weak, but your home/office PC has a strong GPU:

- `tts.kilocraft.org` stays on your server (`nginx` + TLS)
- `Self-Host Speech API` runs on your PC with GPU
- server proxies requests to your PC over existing VPN tunnel

This gives you public HTTPS endpoint without moving heavy `STT` compute to the VPS.

## What is included

- `docker-compose.yml` - daemonized container (`restart: unless-stopped`)
- `Dockerfile` - API container (`FastAPI + faster-whisper`)
- `.env.example` - runtime config
- `.env.gpu-vpn.example` - config for GPU worker on your PC (behind VPN)
- `scripts/download-piper-and-voice.sh` - download Piper binary + voice
- `scripts/run-gpu-worker.ps1` - run service on Windows PC (MVP)
- `scripts/install-windows-startup-task.ps1` - autostart via Windows Task Scheduler
- `deploy/nginx/tts.kilocraft.org.conf` - reverse proxy config
- `deploy/nginx/tts.kilocraft.org.vpn-gpu-upstream.conf` - reverse proxy to VPN IP of your GPU PC
- `deploy/systemd/controledu-speech-compose.service` - autostart with systemd

## API endpoints

- `GET /healthz` - health/info
- `GET /v1/info` - runtime configuration summary (auth)
- `GET /v1/voices` - available Piper voices (auth)
- `POST /v1/tts/synthesize` - synthesize text to WAV (auth)
- `POST /v1/stt/transcribe` - transcribe uploaded audio file (auth)

Auth: `Authorization: Bearer <SPEECH_API_TOKEN>` (configurable in `.env`)

## Quick start (Linux server)

1. Copy folder to server (recommended path):
   - `/opt/controledu/selfhost-speech`
2. Create config:
   - `cp .env.example .env`
   - set `SPEECH_API_TOKEN` to a long random value
3. Download Piper binary and one voice model:
   - example (replace URLs if needed):

```bash
cd /opt/controledu/selfhost-speech
export PIPER_VOICE_MODEL_URL="https://huggingface.co/rhasspy/piper-voices/resolve/main/ru/ru_RU/ruslan/medium/ru_RU-ruslan-medium.onnx?download=true"
export PIPER_VOICE_CONFIG_URL="https://huggingface.co/rhasspy/piper-voices/resolve/main/ru/ru_RU/ruslan/medium/ru_RU-ruslan-medium.onnx.json?download=true"
bash scripts/download-piper-and-voice.sh
```

4. Start daemon:

```bash
docker compose up -d --build
```

5. Check:

```bash
curl http://127.0.0.1:8088/healthz
```

6. Put behind `nginx` (see `deploy/nginx/tts.kilocraft.org.conf`)

## Reverse proxy (`tts.kilocraft.org`)

1. Copy `deploy/nginx/tts.kilocraft.org.conf` to `/etc/nginx/sites-available/tts.kilocraft.org.conf`
2. Adjust certificate paths
3. Enable site + reload `nginx`

## VPN + GPU worker mode (your PC does the heavy lifting)

### Architecture

- Public clients -> `https://tts.kilocraft.org`
- `nginx` on server -> `http://<your-pc-vpn-ip>:8088`
- Speech API on your PC (GPU)

### Server (public edge)

Use `deploy/nginx/tts.kilocraft.org.vpn-gpu-upstream.conf` and replace:

- `10.66.0.2:8088` with your PC VPN IP and port

Then reload `nginx`.

### PC (Windows + GPU, MVP)

1. Copy `selfhost-speech/` to your PC
2. Prepare config:
   - copy `.env.gpu-vpn.example` to `.env.gpu-vpn`
   - set:
     - `SPEECH_API_TOKEN`
     - `SPEECH_API_IP_ALLOWLIST` (server VPN IP)
     - `WHISPER_DEVICE=cuda`
     - `WHISPER_COMPUTE_TYPE=float16`
3. Download Piper voice and binary (PowerShell script available, but easiest on Windows is to place files manually):
   - `runtime/piper/piper.exe`
   - `models/piper/<voice>.onnx`
   - `models/piper/<voice>.onnx.json`
4. Create virtual env + install dependencies:

```powershell
cd selfhost-speech
powershell -ExecutionPolicy Bypass -File .\scripts\run-gpu-worker.ps1 -EnvFile .env.gpu-vpn -CreateVenv -InstallDeps
```

5. Next launches:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-gpu-worker.ps1 -EnvFile .env.gpu-vpn
```

6. Optional startup service (Task Scheduler):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-windows-startup-task.ps1 -ProjectPath "C:\path\to\selfhost-speech" -EnvFile ".env.gpu-vpn"
```

### Security for VPN mode (important)

- Keep Windows firewall rule restricted to your **server VPN IP only**
- Set `SPEECH_API_IP_ALLOWLIST` to server VPN IP (`127.0.0.1` can stay for local checks)
- Use strong `SPEECH_API_TOKEN`
- Keep docs disabled in production (`SPEECH_API_ENABLE_DOCS=false`)

### Latency / reliability expectations

- `TTS` over VPN is usually fine
- `STT` file upload + processing depends on audio length and uplink speed from server to your PC
- If your PC sleeps/reboots, public endpoint will return upstream errors until worker is back

## systemd autostart (optional)

If you want boot-time startup through `systemd`:

```bash
sudo cp deploy/systemd/controledu-speech-compose.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now controledu-speech-compose.service
```

Update `WorkingDirectory=` in the unit if you use another path.

## Example requests

### TTS (Piper -> WAV)

```bash
curl -X POST "https://tts.kilocraft.org/v1/tts/synthesize" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"text":"Привет! Это тест озвучивания.","voice":"ru_RU-ruslan-medium"}' \
  --output out.wav
```

### STT (faster-whisper)

```bash
curl -X POST "https://tts.kilocraft.org/v1/stt/transcribe" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -F "file=@lesson.wav" \
  -F "language=ru" \
  -F "task=transcribe" \
  -F "vad_filter=true"
```

## Performance / hardware requirements (practical)

These numbers are realistic starting points for `1 concurrent transcription` + light TTS usage.

### Minimal (commands / short phrases)

- `CPU`: 2 vCPU
- `RAM`: 4 GB
- `Disk`: 10-20 GB SSD
- `STT model`: `tiny` or `base` (`WHISPER_MODEL=base`)
- Use case: voice commands, short utterances, not ideal for long lessons

### Recommended for school pilot (CPU only)

- `CPU`: 4-8 vCPU (modern x86)
- `RAM`: 8-16 GB
- `Disk`: 20-40 GB SSD
- `STT model`: `small` (`WHISPER_MODEL=small`)
- Good balance for subtitles/teacher speech on CPU

### Better quality / more concurrency

- `CPU`: 8-16 vCPU
- `RAM`: 16-32 GB
- `GPU` (optional but strongly recommended): NVIDIA T4 / RTX 3060+ (6-12 GB VRAM)
- `STT model`: `medium` or `distil-large-v3`
- Set `WHISPER_DEVICE=cuda` and `WHISPER_COMPUTE_TYPE=float16`

### TTS (Piper) load

Piper is lightweight compared to STT:

- usually `1 CPU core` is enough for moderate TTS volume
- RAM impact depends on voice model, often `~200-800 MB`

### If compute is on your GPU PC over VPN (your current plan)

Recommended baseline for comfortable MVP:

- `GPU`: NVIDIA RTX 2060/3060+ (6+ GB VRAM)
- `CPU`: 6+ cores
- `RAM`: 16 GB
- `Disk`: SSD, 20+ GB free
- `Network`: stable VPN tunnel, ideally 50+ Mbps between server and PC

If you use `WHISPER_MODEL=small` and `SPEECH_STT_MAX_CONCURRENCY=1`, this is usually enough for pilot load.

## Notes / limitations

- STT endpoint is file-based (`multipart upload`) in this version. Real-time streaming captions can be added later via WebSocket.
- `faster-whisper` may download the Whisper model on first request (stored in `./models/whisper`).
- `Piper` binary and voice model are external assets and are not committed to the repo.
- Protect the service with a strong bearer token and keep it behind `nginx` + TLS.
- In VPN mode, also restrict inbound access on the PC to the **server VPN IP**.

## Integration direction with Controledu (next step)

This service is ready to be used as a backend for:

- `Student.Agent` TTS fallback (`Piper`)
- server-side STT for teacher audio processing/subtitles
- voice commands recognition pipeline (with a lighter model or a separate endpoint later)
