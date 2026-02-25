from __future__ import annotations

import tempfile
from pathlib import Path

from fastapi import HTTPException, UploadFile, status


async def save_upload_to_temp_file(
    upload: UploadFile,
    directory: Path,
    max_bytes: int,
) -> Path:
    directory.mkdir(parents=True, exist_ok=True)
    suffix = Path(upload.filename or "upload.bin").suffix or ".bin"

    total = 0
    with tempfile.NamedTemporaryFile(delete=False, dir=directory, suffix=suffix) as handle:
        while True:
            chunk = await upload.read(1024 * 1024)
            if not chunk:
                break
            total += len(chunk)
            if total > max_bytes:
                Path(handle.name).unlink(missing_ok=True)
                raise HTTPException(
                    status_code=status.HTTP_413_REQUEST_ENTITY_TOO_LARGE,
                    detail=f"Upload is too large. Limit is {max_bytes // (1024 * 1024)} MB.",
                )
            handle.write(chunk)

        return Path(handle.name)
