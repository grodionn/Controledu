from __future__ import annotations

from typing import Literal

from pydantic import BaseModel, Field, field_validator


class ErrorResponse(BaseModel):
    detail: str


class TtsSynthesizeRequest(BaseModel):
    text: str = Field(min_length=1, max_length=4000)
    voice: str | None = Field(default=None, description="Voice model basename without .onnx")
    speaker_id: int | None = Field(default=None, ge=0)
    length_scale: float | None = Field(default=None, gt=0)
    noise_scale: float | None = Field(default=None, ge=0)
    noise_w: float | None = Field(default=None, ge=0)
    sentence_silence: float | None = Field(default=None, ge=0)
    output_format: Literal["wav"] = "wav"

    @field_validator("text")
    @classmethod
    def strip_text(cls, value: str) -> str:
        stripped = value.strip()
        if not stripped:
            raise ValueError("Text must not be empty.")
        return stripped


class SttWord(BaseModel):
    start: float | None = None
    end: float | None = None
    word: str
    probability: float | None = None


class SttSegment(BaseModel):
    id: int
    start: float
    end: float
    text: str
    avg_logprob: float | None = None
    no_speech_prob: float | None = None
    compression_ratio: float | None = None
    words: list[SttWord] | None = None


class SttTranscribeResponse(BaseModel):
    text: str
    language: str | None = None
    language_probability: float | None = None
    duration: float | None = None
    duration_after_vad: float | None = None
    model: str
    task: str
    segments: list[SttSegment]


class ApiInfoResponse(BaseModel):
    service: str
    tts_enabled: bool
    stt_enabled: bool
    piper_default_voice: str
    whisper_model: str
    whisper_device: str
    whisper_compute_type: str
    ready: bool
