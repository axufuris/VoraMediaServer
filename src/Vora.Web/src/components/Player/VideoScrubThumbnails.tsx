import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useSignalREvent } from '../../hooks/useSignalREvent';
import { serverVault } from '../../utils/serverVault';

interface ThumbnailCue {
    start: number;
    end: number;
    x: number;
    y: number;
    width: number;
    height: number;
}

interface ParsedThumbnails {
    cues: ThumbnailCue[];
    width: number;
    height: number;
    spriteBlobUrl: string;
}

const parseCueTime = (raw: string): number => {
    const parts = raw.trim().split(':');
    if (parts.length !== 3) return 0;
    const [h, m, sMs] = parts;
    const [s, ms] = sMs.split('.');
    return parseInt(h, 10) * 3600 + parseInt(m, 10) * 60 + parseInt(s, 10) + (ms ? parseInt(ms, 10) / 1000 : 0);
};

const parseVtt = (vtt: string): { cues: ThumbnailCue[]; width: number; height: number } | null => {
    const lines = vtt.split(/\r?\n/);
    const cues: ThumbnailCue[] = [];
    let width = 0;
    let height = 0;

    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        if (!line.includes('-->')) continue;
        const [startRaw, endRaw] = line.split('-->').map(s => s.trim());
        const start = parseCueTime(startRaw);
        const end = parseCueTime(endRaw);

        let payload = '';
        for (let j = i + 1; j < lines.length; j++) {
            if (lines[j].trim() === '') break;
            payload += lines[j].trim();
        }
        if (!payload) continue;

        const hashIdx = payload.indexOf('#xywh=');
        if (hashIdx === -1) continue;
        const [xRaw, yRaw, wRaw, hRaw] = payload.substring(hashIdx + 6).split(',').map(s => parseInt(s, 10));
        if ([xRaw, yRaw, wRaw, hRaw].some(n => Number.isNaN(n))) continue;

        cues.push({ start, end, x: xRaw, y: yRaw, width: wRaw, height: hRaw });
        width = wRaw;
        height = hRaw;
    }

    if (cues.length === 0) return null;
    return { cues, width, height };
};

const resolveApiBase = (serverId?: string): { baseUrl: string; token: string | null } => {
    let server = serverId ? serverVault.getServer(serverId) : serverVault.getActiveServer();
    let baseUrl = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000/api';
    if (server) baseUrl = `${server.url}/api`;
    const token = server?.token || localStorage.getItem('profile_token') || localStorage.getItem('account_token');
    return { baseUrl, token: token && token !== 'undefined' && token !== 'null' ? token : null };
};

export function useVideoThumbnails(mediaItemId: string | undefined | null, serverId?: string) {
    const [data, setData] = useState<ParsedThumbnails | null>(null);
    const currentBlobRef = useRef<string | null>(null);

    const load = useCallback(async () => {
        if (!mediaItemId) {
            if (currentBlobRef.current) {
                URL.revokeObjectURL(currentBlobRef.current);
                currentBlobRef.current = null;
            }
            setData(null);
            return;
        }

        try {
            const { baseUrl, token } = resolveApiBase(serverId);
            const headers: Record<string, string> = token ? { Authorization: `Bearer ${token}` } : {};

            const vttRes = await fetch(`${baseUrl}/media/${mediaItemId}/thumbnails.vtt`, { headers });
            if (!vttRes.ok) {
                if (currentBlobRef.current) URL.revokeObjectURL(currentBlobRef.current);
                currentBlobRef.current = null;
                setData(null);
                return;
            }
            const vttText = await vttRes.text();
            const parsed = parseVtt(vttText);
            if (!parsed) { setData(null); return; }

            const spriteRes = await fetch(`${baseUrl}/media/${mediaItemId}/thumbnails.jpg`, { headers });
            if (!spriteRes.ok) { setData(null); return; }
            const blob = await spriteRes.blob();
            const blobUrl = URL.createObjectURL(blob);

            if (currentBlobRef.current) URL.revokeObjectURL(currentBlobRef.current);
            currentBlobRef.current = blobUrl;

            setData({ cues: parsed.cues, width: parsed.width, height: parsed.height, spriteBlobUrl: blobUrl });
        } catch {
            setData(null);
        }
    }, [mediaItemId, serverId]);

    useEffect(() => { void load(); }, [load]);

    useEffect(() => () => {
        if (currentBlobRef.current) {
            URL.revokeObjectURL(currentBlobRef.current);
            currentBlobRef.current = null;
        }
    }, []);

    useSignalREvent<string>('VideoThumbnailsReady', useCallback((updatedId: string) => {
        if (!mediaItemId) return;
        if (updatedId === mediaItemId) void load();
    }, [mediaItemId, load]));

    const findCue = useCallback((timeSec: number): ThumbnailCue | null => {
        if (!data || data.cues.length === 0) return null;
        let lo = 0, hi = data.cues.length - 1;
        while (lo <= hi) {
            const mid = (lo + hi) >> 1;
            const c = data.cues[mid];
            if (timeSec < c.start) hi = mid - 1;
            else if (timeSec >= c.end) lo = mid + 1;
            else return c;
        }
        return data.cues[Math.min(lo, data.cues.length - 1)] ?? null;
    }, [data]);

    return useMemo(() => ({
        available: !!data,
        width: data?.width ?? 0,
        height: data?.height ?? 0,
        spriteUrl: data?.spriteBlobUrl ?? '',
        findCue
    }), [data, findCue]);
}

interface ScrubThumbnailProps {
    hoverPercent: number | null;
    duration: number;
    barRect: DOMRect | null;
    cue: ThumbnailCue | null;
    spriteUrl: string;
    width: number;
    height: number;
}

export function ScrubThumbnail({ hoverPercent, duration, barRect, cue, spriteUrl, width, height }: ScrubThumbnailProps) {
    if (hoverPercent === null || !cue || !barRect || !spriteUrl || width === 0 || height === 0) return null;

    const tileLeft = barRect.left + barRect.width * hoverPercent - width / 2;
    const clamped = Math.max(barRect.left, Math.min(tileLeft, barRect.left + barRect.width - width));
    const tileTop = barRect.top - height - 14;

    const hoverSec = duration * hoverPercent;
    const h = Math.floor(hoverSec / 3600);
    const m = Math.floor((hoverSec % 3600) / 60);
    const s = Math.floor(hoverSec % 60);
    const stamp = h > 0
        ? `${h}:${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`
        : `${m}:${s.toString().padStart(2, '0')}`;

    return (
        <div
            style={{
                position: 'fixed',
                left: clamped,
                top: tileTop,
                width,
                height: height + 22,
                pointerEvents: 'none',
                zIndex: 250
            }}
        >
            <div
                style={{
                    width,
                    height,
                    backgroundImage: `url(${spriteUrl})`,
                    backgroundPosition: `-${cue.x}px -${cue.y}px`,
                    backgroundRepeat: 'no-repeat',
                    backgroundSize: 'auto',
                    border: '2px solid rgba(255, 255, 255, 0.6)',
                    borderRadius: 4,
                    boxShadow: '0 8px 24px rgba(0, 0, 0, 0.5)'
                }}
            />
            <div
                style={{
                    marginTop: 4,
                    textAlign: 'center',
                    color: '#fafafa',
                    fontSize: 12,
                    textShadow: '0 1px 2px rgba(0, 0, 0, 0.8)'
                }}
            >
                {stamp}
            </div>
        </div>
    );
}
