#!/bin/sh
set -eu

url="${1:-}"
case "$url" in
  https://sso.socialstream.ninja/youtube/*|https://ytauth.socialstream.ninja/*|https://accounts.google.com/*)
    ;;
  *)
    exit 1
    ;;
esac

request_path="${SSAPP_BROWSER_REQUEST_FILE:-/var/lib/socialstream/browser-open.request}"
temporary_path="${request_path}.$$"
umask 077
printf '%s' "$url" > "$temporary_path"
mv -f "$temporary_path" "$request_path"
