from __future__ import annotations

import os
from pathlib import Path
from typing import Annotated

from fastapi import Depends, FastAPI, File, Form, HTTPException, UploadFile, status
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse, Response

from .audio_utils import save_upload_to_temp_file
from .config import get_settings
from .models import ApiInfoResponse, ErrorResponse, TtsSynthesizeRequest
from .security import require_bearer_token, require_ip_allowlist
from .stt import FasterWhisperSttService, SttTranscriptionOptions
from .tts import PiperTtsService

settings = get_settings()
auth_dependency = require_bearer_token(settings)
ip_allowlist_dependency = require_ip_allowlist(settings)

app = FastAPI(
    title="Controledu Self-Host Speech API",
    version="1.0.0",
    docs_url="/docs" if settings.docs_enabled else None,
    redoc_url="/redoc" if settings.docs_enabled else None,
    openapi_url="/openapi.json" if settings.docs_enabled else None,
)

if settings.cors_origins:
    app.add_middleware(
        CORSMiddleware,
        allow_origins=settings.cors_origins,
        allow_credentials=True,
        allow_methods=["GET", "POST", "OPTIONS"],
        allow_headers=["*"],
    )

tts_service = PiperTtsService(settings)
stt_service = FasterWhisperSttService(settings)


@app.on_event("startup")
async def _startup() -> None:
    settings.temp_dir.mkdir(parents=True, exist_ok=True)


@app.get("/healthz")
async def healthz(_: None = Depends(ip_allowlist_dependency)) -> dict[str, object]:
    if not settings.allow_unauth_health and settings.api_token:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Not found.")

    tts_issues = tts_service.validate_environment()
    return {
        "ok": True,
        "tts_enabled": settings.tts_enabled,
        "stt_enabled": settings.stt_enabled,
        "tts_ready": tts_service.is_ready(),
        "stt_ready": stt_service.is_ready(),
        "tts_issues": tts_issues,
        "ip_allowlist_configured": bool(settings.ip_allowlist),
        "temp_dir": str(settings.temp_dir),
    }


@app.get(
    "/v1/info",
    response_model=ApiInfoResponse,
    dependencies=[Depends(ip_allowlist_dependency), Depends(auth_dependency)],
)
async def info() -> ApiInfoResponse:
    ready = (not settings.tts_enabled or tts_service.is_ready()) and (not settings.stt_enabled or stt_service.is_ready())
    return ApiInfoResponse(
        service="controledu-selfhost-speech",
        tts_enabled=settings.tts_enabled,
        stt_enabled=settings.stt_enabled,
        piper_default_voice=settings.piper_default_voice,
        whisper_model=settings.whisper_model,
        whisper_device=settings.whisper_device,
        whisper_compute_type=settings.whisper_compute_type,
        ready=ready,
    )


@app.get("/v1/voices", dependencies=[Depends(ip_allowlist_dependency), Depends(auth_dependency)])
async def list_voices() -> dict[str, object]:
    return {
        "voices": tts_service.list_voices(),
        "default_voice": settings.piper_default_voice,
    }


@app.post(
    "/v1/tts/synthesize",
    responses={
        200: {"content": {"audio/wav": {}}},
        400: {"model": ErrorResponse},
        401: {"model": ErrorResponse},
        404: {"model": ErrorResponse},
        502: {"model": ErrorResponse},
        503: {"model": ErrorResponse},
        504: {"model": ErrorResponse},
    },
    dependencies=[Depends(ip_allowlist_dependency), Depends(auth_dependency)],
)
async def synthesize_tts(request: TtsSynthesizeRequest) -> Response:
    audio = await tts_service.synthesize(request)
    return Response(content=audio, media_type="audio/wav")


@app.post(
    "/v1/stt/transcribe",
    responses={
        200: {},
        400: {"model": ErrorResponse},
        401: {"model": ErrorResponse},
        413: {"model": ErrorResponse},
        503: {"model": ErrorResponse},
        504: {"model": ErrorResponse},
    },
    dependencies=[Depends(ip_allowlist_dependency), Depends(auth_dependency)],
)
async def transcribe_stt(
    file: Annotated[UploadFile, File(description="Audio file (wav/mp3/m4a/ogg/flac, ffmpeg required for non-wav).")],
    language: Annotated[str | None, Form()] = None,
    task: Annotated[str | None, Form()] = None,
    beam_size: Annotated[int | None, Form()] = None,
    vad_filter: Annotated[bool | None, Form()] = None,
    word_timestamps: Annotated[bool | None, Form()] = None,
) -> JSONResponse:
    max_bytes = max(1, settings.max_upload_mb) * 1024 * 1024
    temp_path: Path | None = None
    try:
        temp_path = await save_upload_to_temp_file(file, settings.temp_dir, max_bytes=max_bytes)
        normalized_language = (language or settings.stt_default_language or "").strip() or None
        if normalized_language and normalized_language.lower() == "auto":
            normalized_language = None

        result = await stt_service.transcribe_file(
            temp_path,
            SttTranscriptionOptions(
                language=normalized_language,
                task=(task or settings.stt_default_task or "transcribe"),
                beam_size=beam_size if beam_size is not None else settings.stt_default_beam_size,
                vad_filter=vad_filter if vad_filter is not None else settings.stt_default_vad_filter,
                word_timestamps=(
                    word_timestamps if word_timestamps is not None else settings.stt_default_word_timestamps
                ),
            ),
        )
        return JSONResponse(content=result.model_dump())
    finally:
        await file.close()
        if temp_path is not None:
            try:
                os.remove(temp_path)
            except FileNotFoundError:
                pass


if __name__ == "__main__":
    import uvicorn

    uvicorn.run(
        "app.main:app",
        host=settings.host,
        port=settings.port,
        log_level=settings.log_level.lower(),
    )
