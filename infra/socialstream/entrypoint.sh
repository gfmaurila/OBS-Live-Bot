#!/bin/sh
set -eu

if [ "${SSAPP_DBUS_SESSION_ACTIVE:-0}" != "1" ]; then
  exec /usr/bin/dbus-run-session -- env SSAPP_DBUS_SESSION_ACTIVE=1 "$0" "$@"
fi

log() {
  printf '{"component":"socialstream","event":"%s"}\n' "$1"
}

shutdown() {
  log "shutdown_requested"
  kill -TERM "${app_pid:-}" "${relay_pid:-}" "${oauth_8080_relay_pid:-}" "${oauth_8181_relay_pid:-}" 2>/dev/null || true
  wait "${app_pid:-}" 2>/dev/null || true
}

trap shutdown INT TERM

keyring_password_file="${SSAPP_KEYRING_PASSWORD_FILE:-/run/secrets/socialstream-keyring-unlock}"
if [ ! -r "$keyring_password_file" ] || [ ! -s "$keyring_password_file" ]; then
  log "secure_storage_unlock_secret_unavailable"
  exit 1
fi

export XDG_RUNTIME_DIR=/tmp/runtime-socialstream
umask 077
mkdir -p "$XDG_RUNTIME_DIR/keyring" /tmp/cache/fontconfig /var/lib/socialstream/.local/share/pki/nssdb
chmod 0700 "$XDG_RUNTIME_DIR" "$XDG_RUNTIME_DIR/keyring"

keyring_environment="$(/usr/bin/gnome-keyring-daemon --unlock --control-directory="$XDG_RUNTIME_DIR/keyring" < "$keyring_password_file")"
eval "$keyring_environment"
unset keyring_environment
keyring_environment="$(/usr/bin/gnome-keyring-daemon --start --components=secrets --control-directory="$XDG_RUNTIME_DIR/keyring")"
eval "$keyring_environment"
unset keyring_environment

if ! /usr/bin/dbus-send --session --print-reply \
  --dest=org.freedesktop.DBus /org/freedesktop/DBus \
  org.freedesktop.DBus.NameHasOwner string:org.freedesktop.secrets \
  | grep -q 'boolean true'; then
  log "secure_storage_secret_service_unavailable"
  exit 1
fi
log "secure_storage_secret_service_ready"

log "xvfb_starting"
/usr/bin/xvfb-run -a \
  -s "-screen 0 1920x1080x24 -nolisten tcp -extension GLX" \
  /opt/socialstream/app/socialstreamninja \
  --ozone-platform=x11 \
  --ssapp-headless-control \
  --ssapp-control-api \
  --password-store=gnome-libsecret \
  --no-sandbox \
  --no-hwa &
app_pid=$!

log "control_relay_starting"
socat TCP-LISTEN:17778,reuseaddr,fork,bind=0.0.0.0 TCP:127.0.0.1:17777 &
relay_pid=$!

log "oauth_callback_relays_starting"
socat TCP-LISTEN:18080,reuseaddr,fork,bind=0.0.0.0 TCP:127.0.0.1:8080 &
oauth_8080_relay_pid=$!
socat TCP-LISTEN:18181,reuseaddr,fork,bind=0.0.0.0 TCP:127.0.0.1:8181 &
oauth_8181_relay_pid=$!

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

kill -TERM "$app_pid" "$relay_pid" "$oauth_8080_relay_pid" "$oauth_8181_relay_pid" 2>/dev/null || true
wait "$app_pid" 2>/dev/null || true
exit "$status"
