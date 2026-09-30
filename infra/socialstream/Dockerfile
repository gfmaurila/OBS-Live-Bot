FROM debian:bookworm-slim AS fetch

ARG SSN_VERSION=0.4.18
ARG SSN_SHA256=1ed1406de5a75faae6bb3fc4572ac76bde44f78585c01f77c2eb888d98ff8c14

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /opt/socialstream
RUN curl --fail --location --silent --show-error \
      "https://github.com/steveseguin/social_stream/releases/download/v${SSN_VERSION}/socialstreamninja_linux_v${SSN_VERSION}_x86_64.AppImage" \
      --output socialstreamninja.AppImage \
    && echo "${SSN_SHA256}  socialstreamninja.AppImage" | sha256sum --check --strict \
    && chmod 0755 socialstreamninja.AppImage \
    && ./socialstreamninja.AppImage --appimage-extract >/dev/null

FROM debian:bookworm-slim

ARG SSN_VERSION=0.4.18
LABEL org.opencontainers.image.title="GFM StudioOS Social Stream Ninja" \
      org.opencontainers.image.version="${SSN_VERSION}" \
      org.opencontainers.image.source="https://github.com/steveseguin/ssn_app" \
      org.opencontainers.image.licenses="GPL-3.0-only"

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
      ca-certificates curl fonts-noto-core fonts-noto-color-emoji \
      dbus gnome-keyring libsecret-1-0 \
      libasound2 libatk-bridge2.0-0 libatspi2.0-0 libcups2 libdrm2 libgbm1 \
      libgtk-3-0 libnss3 libx11-6 libx11-xcb1 libxcb1 libxcomposite1 \
      libxdamage1 libxext6 libxfixes3 libxkbcommon0 libxrandr2 libxss1 \
      procps socat tini xauth xvfb \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10001 socialstream \
    && useradd --uid 10001 --gid 10001 --create-home --shell /usr/sbin/nologin socialstream \
    && install -d -o socialstream -g socialstream /var/lib/socialstream \
    && install -d -m 1777 /tmp/.X11-unix

COPY --from=fetch /opt/socialstream/squashfs-root /opt/socialstream/app
COPY infra/socialstream/entrypoint.sh /usr/local/bin/socialstream-entrypoint
COPY infra/socialstream/open-external.sh /usr/local/bin/xdg-open

RUN perl -0777 -i -pe \
      'BEGIN { $from = "tokenStoreUrl: tokenStorePage?.url || \x27\x27"; $to = "tokenStoreUrl: \x27\x27"; $to .= q{ } x (length($from) - length($to)); } $count += s/\Q$from\E/$to/g; END { die "Unexpected SSN YouTube token-store patch count: $count\n" unless $count == 1; }' \
      /opt/socialstream/app/resources/app.asar \
    && chmod -R a+rX /opt/socialstream/app \
    && chmod 0755 /usr/local/bin/socialstream-entrypoint /usr/local/bin/xdg-open \
    && if [ -f /opt/socialstream/app/chrome-sandbox ]; then chmod 4755 /opt/socialstream/app/chrome-sandbox; fi

ENV DISPLAY=:99 \
    SSAPP_USER_DATA_DIR=/var/lib/socialstream \
    SSAPP_KEYRING_PASSWORD_FILE=/run/secrets/socialstream-keyring-unlock \
    SSAPP_CONTROL_API=1 \
    SSAPP_CONTROL_PORT=17777

USER socialstream:socialstream
WORKDIR /var/lib/socialstream
EXPOSE 17778

HEALTHCHECK --interval=10s --timeout=5s --retries=12 --start-period=45s \
  CMD curl --fail --silent --show-error http://127.0.0.1:17777/api/v1/capabilities >/dev/null || exit 1

ENTRYPOINT ["/usr/bin/tini", "--", "/usr/local/bin/socialstream-entrypoint"]
