#!/bin/sh
set -eu

log() {
  printf '{"component":"socialstream","event":"%s"}\n' "$1"
}

shutdown() {
  log "shutdown_requested"
  kill -TERM "${app_pid:-}" "${relay_pid:-}" 2>/dev/null || true
  wait "${app_pid:-}" 2>/dev/null || true
}

trap shutdown INT TERM

mkdir -p /tmp/cache/fontconfig /var/lib/socialstream/.local/share/pki/nssdb

log "xvfb_starting"
/usr/bin/xvfb-run -a \
  -s "-screen 0 1920x1080x24 -nolisten tcp -extension GLX" \
  /opt/socialstream/app/socialstreamninja \
  --ozone-platform=x11 \
  --ssapp-headless-control \
  --ssapp-control-api \
  --no-sandbox \
  --no-hwa &
app_pid=$!

log "control_relay_starting"
socat TCP-LISTEN:17778,reuseaddr,fork,bind=0.0.0.0 TCP:127.0.0.1:17777 &
relay_pid=$!

log "ssapp_starting"
while kill -0 "$app_pid" 2>/dev/null && kill -0 "$relay_pid" 2>/dev/null; do
  sleep 5
done

status=0
if ! kill -0 "$app_pid" 2>/dev/null; then
  wait "$app_pid" || status=$?
  log "ssapp_or_xvfb_stopped"
else
  log "control_relay_failed"
  status=1
fi

kill -TERM "$app_pid" "$relay_pid" 2>/dev/null || true
wait "$app_pid" 2>/dev/null || true
exit "$status"
