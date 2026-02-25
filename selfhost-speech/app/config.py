from __future__ import annotations

import os
from dataclasses import dataclass
from functools import lru_cache
from pathlib import Path


def _get_bool(name: str, default: bool) -> bool:
    raw = os.getenv(name)
    if raw is None:
        return default
    return raw.strip().lower() in {"1", "true", "yes", "on"}


def _get_int(name: str, default: int) -> int:
    raw = os.getenv(name)
    if raw is None or not raw.strip():
        return default
    return int(raw)


def _get_float(name: str, default: float) -> float:
    raw = os.getenv(name)
    if raw is None or not raw.strip():
        return default
    return float(raw)


def _get_optional_int(name: str) -> int | None:
    raw = os.getenv(name)
    if raw is None or not raw.strip():
        return None
    return int(raw)


def _get_list(name: str) -> list[str]:
    raw = os.getenv(name, "")
    return [part.strip() for part in raw.split(",") if part.strip()]


@dataclass(frozen=True)
class Settings:
    host: str
    port: int
    log_level: str
    api_token: str
    cors_origins: list[str]
    ip_allowlist: list[str]
    allow_unauth_health: bool
    docs_enabled: bool

    max_upload_mb: int
    temp_dir: Path

    tts_enabled: bool
    tts_max_chars: int
    piper_bin_path: Path
    piper_models_dir: Path
    piper_default_voice: str
    piper_default_speaker_id: int | None
    piper_timeout_seconds: int
    piper_default_length_scale: float
    piper_default_noise_scale: float | None
    piper_default_noise_w: float | None
    piper_default_sentence_silence: float | None

    stt_enabled: bool
    whisper_model: str
    whisper_device: str
    whisper_compute_type: str
    whisper_models_dir: Path
    whisper_cpu_threads: int
    whisper_num_workers: int
    stt_default_language: str | None
    stt_default_task: str
    stt_default_beam_size: int
    stt_default_vad_filter: bool
    stt_default_word_timestamps: bool
    stt_max_concurrency: int
    stt_timeout_seconds: int


@lru_cache(maxsize=1)
def get_settings() -> Settings:
    host = os.getenv("SPEECH_API_HOST", "0.0.0.0")
    port = _get_int("SPEECH_API_PORT", 8088)
    temp_dir = Path(os.getenv("SPEECH_API_TEMP_DIR", "/tmp/controledu-speech")).resolve()
    piper_models_dir = Path(os.getenv("PIPER_MODELS_DIR", "/models/piper")).resolve()
    whisper_models_dir = Path(os.getenv("WHISPER_MODELS_DIR", "/models/whisper")).resolve()

    return Settings(
        host=host,
        port=port,
        log_level=os.getenv("SPEECH_API_LOG_LEVEL", "info"),
        api_token=os.getenv("SPEECH_API_TOKEN", "").strip(),
        cors_origins=_get_list("SPEECH_API_CORS_ORIGINS"),
        ip_allowlist=_get_list("SPEECH_API_IP_ALLOWLIST"),
        allow_unauth_health=_get_bool("SPEECH_API_ALLOW_UNAUTH_HEALTH", True),
        docs_enabled=_get_bool("SPEECH_API_ENABLE_DOCS", True),
        max_upload_mb=_get_int("SPEECH_API_MAX_UPLOAD_MB", 64),
        temp_dir=temp_dir,
        tts_enabled=_get_bool("SPEECH_TTS_ENABLED", True),
        tts_max_chars=_get_int("SPEECH_TTS_MAX_CHARS", 4000),
        piper_bin_path=Path(os.getenv("PIPER_BIN_PATH", "/runtime/piper/piper")).resolve(),
        piper_models_dir=piper_models_dir,
        piper_default_voice=os.getenv("PIPER_DEFAULT_VOICE", "ru_RU-ruslan-medium").strip(),
        piper_default_speaker_id=_get_optional_int("PIPER_DEFAULT_SPEAKER_ID"),
        piper_timeout_seconds=_get_int("PIPER_TIMEOUT_SECONDS", 30),
        piper_default_length_scale=_get_float("PIPER_DEFAULT_LENGTH_SCALE", 1.0),
        piper_default_noise_scale=float(os.getenv("PIPER_DEFAULT_NOISE_SCALE")) if os.getenv("PIPER_DEFAULT_NOISE_SCALE") else None,
        piper_default_noise_w=float(os.getenv("PIPER_DEFAULT_NOISE_W")) if os.getenv("PIPER_DEFAULT_NOISE_W") else None,
        piper_default_sentence_silence=float(os.getenv("PIPER_DEFAULT_SENTENCE_SILENCE")) if os.getenv("PIPER_DEFAULT_SENTENCE_SILENCE") else None,
        stt_enabled=_get_bool("SPEECH_STT_ENABLED", True),
        whisper_model=os.getenv("WHISPER_MODEL", "small").strip(),
        whisper_device=os.getenv("WHISPER_DEVICE", "cpu").strip(),
        whisper_compute_type=os.getenv("WHISPER_COMPUTE_TYPE", "int8").strip(),
        whisper_models_dir=whisper_models_dir,
        whisper_cpu_threads=_get_int("WHISPER_CPU_THREADS", 4),
        whisper_num_workers=_get_int("WHISPER_NUM_WORKERS", 1),
        stt_default_language=(os.getenv("SPEECH_STT_DEFAULT_LANGUAGE", "").strip() or None),
        stt_default_task=os.getenv("SPEECH_STT_DEFAULT_TASK", "transcribe").strip(),
        stt_default_beam_size=_get_int("SPEECH_STT_DEFAULT_BEAM_SIZE", 5),
        stt_default_vad_filter=_get_bool("SPEECH_STT_DEFAULT_VAD_FILTER", True),
        stt_default_word_timestamps=_get_bool("SPEECH_STT_DEFAULT_WORD_TIMESTAMPS", False),
        stt_max_concurrency=_get_int("SPEECH_STT_MAX_CONCURRENCY", 1),
        stt_timeout_seconds=_get_int("SPEECH_STT_TIMEOUT_SECONDS", 300),
    )
