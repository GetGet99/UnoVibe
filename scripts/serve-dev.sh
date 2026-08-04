#!/usr/bin/env bash
# Start the dev `opencode serve` on http://localhost:4196 if it isn't already running.
# See AGENTS.md "How to Build & Run".
set -u

PORT=4196
LOG=/mnt/LinuxProgramData/tmp/opencode/serve_dev.log

is_running() {
  if ps aux | grep "opencode serve" | grep -v grep > /dev/null 2>&1; then
    return 0
  fi
  # Fallback: a process may exist but be unhealthy; probe the health endpoint.
  if curl -fsS "http://localhost:${PORT}/global/health" > /dev/null 2>&1; then
    return 0
  fi
  return 1
}

if is_running; then
  echo "opencode serve already running on http://localhost:${PORT}"
  exit 0
fi

echo "Starting opencode serve on port ${PORT}..."
nohup opencode serve --port "${PORT}" > "${LOG}" 2>&1 &
disown

# Wait for readiness.
for i in $(seq 1 30); do
  if curl -fsS "http://localhost:${PORT}/global/health" > /dev/null 2>&1; then
    echo "Readiness confirmed on http://localhost:${PORT} after ${i}s"
    exit 0
  fi
  sleep 1
done

echo "ERROR: server did not become healthy within 30s. Check ${LOG}" >&2
exit 1