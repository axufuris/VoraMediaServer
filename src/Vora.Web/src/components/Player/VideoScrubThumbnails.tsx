import type { ThumbnailCue } from '../../hooks/useVideoThumbnails';

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
                height,
                pointerEvents: 'none',
                zIndex: 250
            }}
        >
            <div
                style={{
                    position: 'relative',
                    width,
                    height,
                    backgroundImage: `url(${spriteUrl})`,
                    backgroundPosition: `-${cue.x}px -${cue.y}px`,
                    backgroundRepeat: 'no-repeat',
                    backgroundSize: 'auto',
                    border: '2px solid color-mix(in srgb, var(--vora-text-primary) 60%, transparent)',
                    borderRadius: 4,
                    boxShadow: 'var(--vora-shadow-lg)'
                }}
            >
                <span
                    data-testid="scrub-thumbnail-time"
                    style={{
                        position: 'absolute',
                        bottom: 6,
                        left: '50%',
                        transform: 'translateX(-50%)',
                        padding: '1px 8px',
                        borderRadius: 999,
                        background: 'color-mix(in srgb, var(--vora-bg-canvas) 80%, transparent)',
                        color: 'var(--vora-text-primary)',
                        fontSize: 12,
                        fontWeight: 600,
                        fontVariantNumeric: 'tabular-nums',
                        lineHeight: '18px'
                    }}
                >
                    {stamp}
                </span>
            </div>
        </div>
    );
}
