import type { ThumbnailCue } from '../../hooks/useVideoThumbnails';

interface ScrubPreviewProps {
    hoverPercent: number | null;
    duration: number;
    barRect: DOMRect | null;
    cue: ThumbnailCue | null;
    spriteUrl: string;
    width: number;
    height: number;
}

export const SCRUB_TIME_GAP = 10;
export const SCRUB_TIME_HEIGHT = 20;
const THUMBNAIL_GAP = 6;
const TIME_EDGE = 32;

const clamp = (value: number, min: number, max: number) => Math.max(min, Math.min(value, max));

function formatStamp(seconds: number): string {
    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    const s = Math.floor(seconds % 60);
    return h > 0
        ? `${h}:${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`
        : `${m}:${s.toString().padStart(2, '0')}`;
}

export function ScrubPreview({ hoverPercent, duration, barRect, cue, spriteUrl, width, height }: ScrubPreviewProps) {
    if (hoverPercent === null || !barRect || !Number.isFinite(duration) || duration <= 0) return null;

    const pointerX = barRect.left + barRect.width * hoverPercent;
    const timeTop = barRect.top - SCRUB_TIME_GAP - SCRUB_TIME_HEIGHT;
    const timeLeft = clamp(pointerX, barRect.left + TIME_EDGE, barRect.left + barRect.width - TIME_EDGE);
    const tileLeft = clamp(pointerX - width / 2, barRect.left, barRect.left + barRect.width - width);

    return (
        <>
            {cue && spriteUrl && width > 0 && height > 0 && (
                <div
                    data-testid="scrub-thumbnail"
                    style={{
                        position: 'fixed',
                        left: tileLeft,
                        top: timeTop - THUMBNAIL_GAP - height,
                        width,
                        height,
                        backgroundImage: `url(${spriteUrl})`,
                        backgroundPosition: `-${cue.x}px -${cue.y}px`,
                        backgroundRepeat: 'no-repeat',
                        backgroundSize: 'auto',
                        border: '2px solid color-mix(in srgb, var(--vora-text-primary) 60%, transparent)',
                        borderRadius: 4,
                        boxShadow: 'var(--vora-shadow-lg)',
                        pointerEvents: 'none',
                        zIndex: 250
                    }}
                />
            )}
            <span
                data-testid="scrub-time"
                style={{
                    position: 'fixed',
                    left: timeLeft,
                    top: timeTop,
                    transform: 'translateX(-50%)',
                    padding: '1px 8px',
                    borderRadius: 999,
                    background: 'color-mix(in srgb, var(--vora-bg-canvas) 80%, transparent)',
                    color: 'var(--vora-text-primary)',
                    fontSize: 12,
                    fontWeight: 600,
                    fontVariantNumeric: 'tabular-nums',
                    lineHeight: '18px',
                    whiteSpace: 'nowrap',
                    pointerEvents: 'none',
                    zIndex: 250
                }}
            >
                {formatStamp(duration * hoverPercent)}
            </span>
        </>
    );
}
