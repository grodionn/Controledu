from __future__ import annotations

import asyncio
import shutil
import subprocess
import tempfile
from pathlib import Path

from fastapi import HTTPException, status

from .config import Settings
from .models import TtsSynthesizeRequest


def _safe_voice_name(raw: str) -> str:
    name = raw.strip()
    if not name:
        raise ValueError("Voice name is empty.")
    if ".." in name or "/" in name or "\\" in name:
        raise ValueError("Voice name contains invalid path characters.")
    return name.replace(".onnx", "")


class PiperTtsService:
    def __init__(self, settings: Settings) -> None:
        self._settings = settings

    def is_ready(self) -> bool:
        if not self._settings.tts_enabled:
            return False
        if not self._settings.piper_bin_path.exists():
            return False
        try:
            model_path = self._resolve_model_path(self._settings.piper_default_voice)
        except ValueError:
            return False
        return model_path.exists()

    def _resolve_model_path(self, voice: str | None) -> Path:
        selected = _safe_voice_name(voice or self._settings.piper_default_voice)
        return (self._settings.piper_models_dir / f"{selected}.onnx").resolve()

    async def synthesize(self, request: TtsSynthesizeRequest) -> bytes:
        if not self._settings.tts_enabled:
            raise HTTPException(status_code=status.HTTP_503_SERVICE_UNAVAILABLE, detail="TTS is disabled.")

        if len(request.text) > self._settings.tts_max_chars:
            raise HTTPException(
                status_code=status.HTTP_400_BAD_REQUEST,
                detail=f"TTS text too long. Max {self._settings.tts_max_chars} characters.",
            )

        if not self._settings.piper_bin_path.exists():
            raise HTTPException(
                status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
                detail=f"Piper binary not found at {self._settings.piper_bin_path}.",
            )

        try:
            model_path = self._resolve_model_path(request.voice)
        except ValueError as exc:
            raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(exc)) from exc
        if not model_path.exists():
            raise HTTPException(
                status_code=status.HTTP_404_NOT_FOUND,
                detail=f"Piper voice model not found: {model_path.name}",
            )

        with tempfile.TemporaryDirectory(prefix="controledu-piper-") as temp_dir:
            temp_dir_path = Path(temp_dir)
            output_file = temp_dir_path / "tts.wav"
            input_file = temp_dir_path / "input.txt"
            input_file.write_text(request.text, encoding="utf-8")

            command = [
                str(self._settings.piper_bin_path),
                "-m",
                str(model_path),
                "-i",
                str(input_file),
                "-f",
                str(output_file),
            ]

            speaker_id = request.speaker_id if request.speaker_id is not None else self._settings.piper_default_speaker_id
            if speaker_id is not None:
                command.extend(["--speaker", str(speaker_id)])

            length_scale = request.length_scale if request.length_scale is not None else self._settings.piper_default_length_scale
            if length_scale and length_scale > 0:
                command.extend(["--length-scale", str(length_scale)])

            noise_scale = request.noise_scale if request.noise_scale is not None else self._settings.piper_default_noise_scale
            if noise_scale is not None:
                command.extend(["--noise-scale", str(noise_scale)])

            noise_w = request.noise_w if request.noise_w is not None else self._settings.piper_default_noise_w
            if noise_w is not None:
                command.extend(["--noise_w", str(noise_w)])

            sentence_silence = (
                request.sentence_silence
                if request.sentence_silence is not None
                else self._settings.piper_default_sentence_silence
            )
            if sentence_silence is not None:
                command.extend(["--sentence-silence", str(sentence_silence)])

            try:
                process = await asyncio.create_subprocess_exec(
                    *command,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.PIPE,
                )
            except FileNotFoundError as exc:
                raise HTTPException(
                    status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
                    detail=f"Failed to start Piper binary: {exc}",
                ) from exc

            try:
                stdout, stderr = await asyncio.wait_for(
                    process.communicate(),
                    timeout=self._settings.piper_timeout_seconds,
                )
            except TimeoutError as exc:
                process.kill()
                raise HTTPException(
                    status_code=status.HTTP_504_GATEWAY_TIMEOUT,
                    detail="Piper TTS timed out.",
                ) from exc

            if process.returncode != 0:
                stderr_text = (stderr or b"").decode("utf-8", errors="ignore").strip()
                stdout_text = (stdout or b"").decode("utf-8", errors="ignore").strip()
                raise HTTPException(
                    status_code=status.HTTP_502_BAD_GATEWAY,
                    detail=f"Piper failed ({process.returncode}). {stderr_text or stdout_text or 'No error output.'}",
                )

            if not output_file.exists():
                raise HTTPException(
                    status_code=status.HTTP_502_BAD_GATEWAY,
                    detail="Piper completed without producing output audio.",
                )

            return output_file.read_bytes()

    def list_voices(self) -> list[str]:
        if not self._settings.piper_models_dir.exists():
            return []
        return sorted(
            path.stem
            for path in self._settings.piper_models_dir.glob("*.onnx")
            if path.is_file()
        )

    def validate_environment(self) -> list[str]:
        issues: list[str] = []
        if not self._settings.tts_enabled:
            return issues
        if not self._settings.piper_bin_path.exists():
            issues.append(f"Piper binary missing: {self._settings.piper_bin_path}")
        if shutil.which("ffmpeg") is None:
            # TTS itself does not require ffmpeg, but STT endpoint often will.
            issues.append("ffmpeg not found in PATH (recommended for STT audio decoding).")
        try:
            default_model_path = self._resolve_model_path(self._settings.piper_default_voice)
        except ValueError as exc:
            issues.append(f"Invalid PIPER_DEFAULT_VOICE: {exc}")
            default_model_path = None
        if default_model_path is not None and not default_model_path.exists():
            issues.append(
                f"Default Piper voice model missing: {self._settings.piper_default_voice}.onnx in {self._settings.piper_models_dir}"
            )
        return issues
