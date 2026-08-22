#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${NEWT_ID:-}" || -z "${NEWT_SECRET:-}" || -z "${PANGOLIN_ENDPOINT:-}" ]]; then
  echo "[newt] skipped: set NEWT_ID, NEWT_SECRET and PANGOLIN_ENDPOINT to enable Pangolin."
  exec sleep infinity
fi

echo "[newt] connecting to ${PANGOLIN_ENDPOINT}"
exec /usr/local/bin/newt
