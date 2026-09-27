#!/bin/sh
set -eu

PUID="${PUID:-99}"
PGID="${PGID:-100}"
RUN_UMASK="${UMASK:-022}"

umask "$RUN_UMASK" 2>/dev/null || umask 022

if getent group vora >/dev/null 2>&1; then
    groupmod -o -g "$PGID" vora
else
    groupadd -o -g "$PGID" vora
fi

if getent passwd vora >/dev/null 2>&1; then
    usermod -o -u "$PUID" -g "$PGID" vora
else
    useradd -o -u "$PUID" -g "$PGID" -M -s /usr/sbin/nologin vora
fi

chown -R "$PUID":"$PGID" /app/data /transcode

exec gosu vora:vora dotnet Vora.Api.dll
