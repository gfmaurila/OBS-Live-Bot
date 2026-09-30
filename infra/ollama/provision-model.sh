#!/bin/sh
set -eu

model="${OLLAMA_MODEL:-qwen3:4b-instruct-2507-q4_K_M}"
ready_attempts="${OLLAMA_READY_ATTEMPTS:-60}"
pull_attempts="${OLLAMA_PULL_ATTEMPTS:-3}"

attempt=1
while ! ollama list >/dev/null 2>&1; do
  if [ "$attempt" -ge "$ready_attempts" ]; then
    echo "OLLAMA_MODEL_PROVISION_FAILED reason=readiness_timeout attempts=$attempt" >&2
    exit 1
  fi

  echo "OLLAMA_MODEL_WAITING attempt=$attempt maxAttempts=$ready_attempts"
  attempt=$((attempt + 1))
  sleep 2
done

if ollama show "$model" >/dev/null 2>&1; then
  echo "OLLAMA_MODEL_READY model=$model action=existing"
  exit 0
fi

attempt=1
while [ "$attempt" -le "$pull_attempts" ]; do
  echo "OLLAMA_MODEL_PROVISIONING model=$model attempt=$attempt maxAttempts=$pull_attempts"
  if ollama pull "$model"; then
    echo "OLLAMA_MODEL_READY model=$model action=downloaded"
    exit 0
  fi

  if [ "$attempt" -lt "$pull_attempts" ]; then
    sleep $((attempt * 5))
  fi
  attempt=$((attempt + 1))
done

echo "OLLAMA_MODEL_PROVISION_FAILED model=$model reason=pull_failed attempts=$pull_attempts" >&2
exit 1
