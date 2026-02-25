from __future__ import annotations

import asyncio
import threading
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from fastapi import HTTPException, status

from .config import Settings
from .models import SttSegment, SttTranscribeResponse, SttWord


@dataclass(frozen=True)
class SttTranscriptionOptions:
    language: str | None
    task: str
    beam_size: int
    vad_filter: bool
    word_timestamps: bool


class FasterWhisperSttService:
    def __init__(self, settings: Settings) -> None:
        self._settings = settings
        self._model: Any | None = None
        self._model_lock = threading.Lock()
        self._semaphore = asyncio.Semaphore(max(1, settings.stt_max_concurrency))

    def is_ready(self) -> bool:
        return self._settings.stt_enabled

    def _get_model(self):
        if self._model is not None:
            return self._model

        with self._model_lock:
            if self._model is not None:
                return self._model

            try:
                from faster_whisper import WhisperModel  # Imported lazily to allow TTS-only mode.
            except Exception as exc:  # pragma: no cover
                raise HTTPException(
                    status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
                    detail=f"faster-whisper import failed: {exc}",
                ) from exc

            self._settings.whisper_models_dir.mkdir(parents=True, exist_ok=True)
            self._model = WhisperModel(
                self._settings.whisper_model,
                device=self._settings.whisper_device,
                compute_type=self._settings.whisper_compute_type,
                download_root=str(self._settings.whisper_models_dir),
                cpu_threads=max(1, self._settings.whisper_cpu_threads),
                num_workers=max(1, self._settings.whisper_num_workers),
            )
            return self._model

    async def transcribe_file(self, audio_path: Path, options: SttTranscriptionOptions) -> SttTranscribeResponse:
        if not self._settings.stt_enabled:
            raise HTTPException(status_code=status.HTTP_503_SERVICE_UNAVAILABLE, detail="STT is disabled.")

        if options.task not in {"transcribe", "translate"}:
            raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail="task must be transcribe or translate.")

        model = self._get_model()

        async with self._semaphore:
            try:
                result = await asyncio.wait_for(
                    asyncio.to_thread(self._transcribe_sync, model, audio_path, options),
                    timeout=self._settings.stt_timeout_seconds,
                )
            except TimeoutError as exc:
                raise HTTPException(
                    status_code=status.HTTP_504_GATEWAY_TIMEOUT,
                    detail="STT transcription timed out.",
                ) from exc

        return result

    def _transcribe_sync(self, model, audio_path: Path, options: SttTranscriptionOptions) -> SttTranscribeResponse:
        segments_iter, info = model.transcribe(
            str(audio_path),
            language=options.language,
            task=options.task,
            beam_size=max(1, options.beam_size),
            vad_filter=options.vad_filter,
            word_timestamps=options.word_timestamps,
        )

        segments: list[SttSegment] = []
        full_text_parts: list[str] = []

        for index, segment in enumerate(segments_iter):
            text = (getattr(segment, "text", "") or "").strip()
            if text:
                full_text_parts.append(text)

            words = None
            if options.word_timestamps and getattr(segment, "words", None):
                words = [
                    SttWord(
                        start=getattr(word, "start", None),
                        end=getattr(word, "end", None),
                        word=(getattr(word, "word", "") or "").strip(),
                        probability=getattr(word, "probability", None),
                    )
                    for word in segment.words
                ]

            segments.append(
                SttSegment(
                    id=index,
                    start=float(getattr(segment, "start", 0.0) or 0.0),
                    end=float(getattr(segment, "end", 0.0) or 0.0),
                    text=text,
                    avg_logprob=getattr(segment, "avg_logprob", None),
                    no_speech_prob=getattr(segment, "no_speech_prob", None),
                    compression_ratio=getattr(segment, "compression_ratio", None),
                    words=words,
                )
            )

        return SttTranscribeResponse(
            text=" ".join(part for part in full_text_parts if part).strip(),
            language=getattr(info, "language", None),
            language_probability=getattr(info, "language_probability", None),
            duration=getattr(info, "duration", None),
            duration_after_vad=getattr(info, "duration_after_vad", None),
            model=self._settings.whisper_model,
            task=options.task,
            segments=segments,
        )
