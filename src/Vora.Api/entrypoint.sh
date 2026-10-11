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

for node in /dev/dri/renderD*; do
    [ -e "$node" ] || continue
    gid="$(stat -c %g "$node")"
    [ "$gid" = "0" ] && continue
    group="$(getent group "$gid" | cut -d: -f1)"
    if [ -z "$group" ]; then
        group="vora-gpu-$gid"
        groupadd -o -g "$gid" "$group"
    fi
    usermod -a -G "$group" vora
done

chown -R "$PUID":"$PGID" /app/data /transcode

for node in /dev/dri/renderD*; do
    [ -e "$node" ] || continue
    if ! gosu vora sh -c "[ -r '$node' ] && [ -w '$node' ]"; then
        echo "Vora can't open $node, so it can't use that GPU for transcoding. Give the container's user read and write access to it." >&2
    fi
done

exec gosu vora dotnet Vora.Api.dll
