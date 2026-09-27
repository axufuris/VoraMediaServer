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
