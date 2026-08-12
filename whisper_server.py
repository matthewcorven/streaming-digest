"""
Whisper audio-to-text server using faster-whisper (Linux-compatible, no MLX).
Provides HTTP API matching the StreamingDigest contract:
  - GET /health — health check
  - POST /internal/audio-to-text/transcribe — transcription endpoint
"""

import os
import threading
import time
from pathlib import Path
from typing import Optional

import uvicorn
from faster_whisper import WhisperModel
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field

app = FastAPI(title="Whisper Server", version="1.2.0")

MODEL_NAME = os.getenv("WHISPER_MODEL", "base")
DEVICE = os.getenv("WHISPER_DEVICE", "cpu")
COMPUTE_TYPE = os.getenv("WHISPER_COMPUTE_TYPE", "int8")
BEAM_SIZE = int(os.getenv("WHISPER_BEAM_SIZE", "5"))

_model: Optional[WhisperModel] = None
_model_loaded_at: float = 0.0
_model_load_error: Optional[str] = None
_model_lock = threading.Lock()


def _load_model() -> None:
    """Load the Whisper model in a background thread."""
    global _model, _model_loaded_at, _model_load_error

    try:
        print(f"Loading whisper model: {MODEL_NAME} on device: {DEVICE} (compute={COMPUTE_TYPE})")
        started = time.time()
        model = WhisperModel(MODEL_NAME, device=DEVICE, compute_type=COMPUTE_TYPE)
        with _model_lock:
            _model = model
            _model_loaded_at = time.time()
            _model_load_error = None
        print(f"Model loaded successfully in {_model_loaded_at - started:.2f}s")
    except Exception as exc:  # noqa: BLE001
        message = f"Failed to load whisper model: {exc}"
        print(message)
        with _model_lock:
            _model_load_error = message


# Start model loading in the background so the HTTP server can accept requests
# (including health checks) immediately. This is required for zero-intervention
# startup where orchestrators health-check the service right away.
threading.Thread(target=_load_model, daemon=True).start()


def get_model() -> WhisperModel:
    """Return the loaded model, raising if loading failed or is incomplete."""
    with _model_lock:
        if _model is not None:
            return _model
        if _model_load_error is not None:
            raise RuntimeError(_model_load_error)
    raise RuntimeError("Whisper model is still loading")


class TranscriptionRequest(BaseModel):
    file_path: str = Field(..., description="Path to audio file inside the container")
    language: Optional[str] = Field(None, description="Language hint (e.g., 'en', 'fr')")


class TranscriptionCue(BaseModel):
    start_seconds: float
    end_seconds: Optional[float]
    text: str


class TranscriptionResponse(BaseModel):
    engine: str
    model: str
    language: Optional[str]
    duration_seconds: Optional[float]
    text: str
    cues: list[TranscriptionCue]


@app.get("/health")
async def health():
    """Health check endpoint.

    Returns HTTP 200 as soon as the server is reachable. The response includes
    a `ready` flag that is true once the model has finished loading. This lets
    orchestrators declare the container healthy while the model loads in the
    background, which is important for first-time startup when the model may
    need to be downloaded or initialized.
    """
    with _model_lock:
        ready = _model is not None
        error = _model_load_error

    if error is not None:
        raise HTTPException(status_code=503, detail=error)

    return {
        "status": "healthy" if ready else "loading",
        "ready": ready,
        "engine": "whisper",
        "model": MODEL_NAME,
        "device": DEVICE,
        "loaded_at": _model_loaded_at,
    }


@app.post("/internal/audio-to-text/transcribe")
async def transcribe(request: TranscriptionRequest):
    """
    Transcribe an audio file at the given container path.
    """
    file_path = Path(request.file_path)
    if not file_path.exists():
        raise HTTPException(status_code=404, detail=f"Audio file not found: {request.file_path}")

    model = get_model()

    segments, info = model.transcribe(
        str(file_path),
        language=request.language,
        beam_size=BEAM_SIZE,
    )

    cues: list[TranscriptionCue] = []
    text_parts: list[str] = []
    duration_seconds: Optional[float] = None

    for seg in segments:
        cues.append(
            TranscriptionCue(
                start_seconds=seg.start,
                end_seconds=seg.end,
                text=seg.text.strip(),
            )
        )
        text_parts.append(seg.text.strip())
        duration_seconds = seg.end

    return TranscriptionResponse(
        engine="whisper",
        model=MODEL_NAME,
        language=info.language or request.language,
        duration_seconds=duration_seconds,
        text=" ".join(text_parts),
        cues=cues,
    )


@app.get("/api/models")
async def list_models():
    """List available whisper models."""
    return {
        "default": MODEL_NAME,
        "available": ["tiny", "base", "small", "medium", "large", "large-v2", "large-v3"],
    }


if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=8080)
