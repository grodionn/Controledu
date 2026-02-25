#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUNTIME_DIR="${ROOT_DIR}/runtime/piper"
MODELS_DIR="${ROOT_DIR}/models/piper"
TMP_DIR="${ROOT_DIR}/tmp/downloads"

mkdir -p "${RUNTIME_DIR}" "${MODELS_DIR}" "${TMP_DIR}"

: "${PIPER_TAR_URL:=https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_linux_x86_64.tar.gz}"
: "${PIPER_VOICE_MODEL_URL:=}"
: "${PIPER_VOICE_CONFIG_URL:=}"

echo "[1/3] Download Piper binary from: ${PIPER_TAR_URL}"
curl -L --fail "${PIPER_TAR_URL}" -o "${TMP_DIR}/piper.tar.gz"
tar -xzf "${TMP_DIR}/piper.tar.gz" -C "${RUNTIME_DIR}" --strip-components=1
chmod +x "${RUNTIME_DIR}/piper" || true

if [[ -n "${PIPER_VOICE_MODEL_URL}" ]]; then
  echo "[2/3] Download Piper voice model"
  curl -L --fail "${PIPER_VOICE_MODEL_URL}" -o "${MODELS_DIR}/$(basename "${PIPER_VOICE_MODEL_URL}")"
else
  echo "[2/3] Skip voice model download (set PIPER_VOICE_MODEL_URL to download automatically)"
fi

if [[ -n "${PIPER_VOICE_CONFIG_URL}" ]]; then
  echo "[3/3] Download Piper voice config"
  curl -L --fail "${PIPER_VOICE_CONFIG_URL}" -o "${MODELS_DIR}/$(basename "${PIPER_VOICE_CONFIG_URL}")"
else
  echo "[3/3] Skip voice config download (optional; same basename .onnx.json recommended)"
fi

echo "Done. Runtime: ${RUNTIME_DIR}, models: ${MODELS_DIR}"

