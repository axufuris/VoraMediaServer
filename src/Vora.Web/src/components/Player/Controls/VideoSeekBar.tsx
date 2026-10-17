import { useRef, useState, type PointerEvent, type ReactNode } from 'react';

interface VideoSeekBarProps {
    currentTime: number;
    duration: number;
    onSeek: (seconds: number) => void;
    // Where the scrub preview belongs: under the pointer while it hovers or
    // drags, or null when it should go away.
    onPointAt: (percent: number | null, track: DOMRect | null) => void;
    variant: 'mini' | 'full';
    children?: ReactNode;
}

const clampToTrack = (value: number) => Math.max(0, Math.min(1, value));

function seekPercentAt(clientX: number, track: DOMRect): number {
    return track.width > 0 ? clampToTrack((clientX - track.left) / track.width) : 0;
}

function spokenTime(seconds: number): string {
    const whole = Math.max(0, Math.floor(seconds));
    const h = Math.floor(whole / 3600);
    const m = Math.floor((whole % 3600) / 60);
    const s = whole % 60;
    return h > 0 ? `${h}:${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}` : `${m}:${s.toString().padStart(2, '0')}`;
}

// Press, drag and release, with a mouse, a pen or a finger. The fill and the
// preview follow the pointer, and the video seeks once, on release: a seek
// outside what is already transcoded restarts the transcode, so seeking on
// every move would restart it dozens of times in one drag.
export default function VideoSeekBar({ currentTime, duration, onSeek, onPointAt, variant, children }: VideoSeekBarProps) {
    const trackRef = useRef<HTMLDivElement>(null);
    const dragRef = useRef<number | null>(null);
    const [dragPercent, setDragPercent] = useState<number | null>(null);
    const seekable = Number.isFinite(duration) && duration > 0;
    const shownPercent = dragPercent ?? (seekable ? clampToTrack(currentTime / duration) : 0);
    const dragging = dragPercent !== null;

    const pointAt = (clientX: number): number | null => {
        const track = trackRef.current?.getBoundingClientRect();
        if (!track) return null;
        const percent = seekPercentAt(clientX, track);
        onPointAt(percent, track);
        return percent;
    };

    const drag = (percent: number | null) => {
        dragRef.current = percent;
        setDragPercent(percent);
    };

    const onPointerDown = (e: PointerEvent<HTMLDivElement>) => {
        if (!seekable || (e.pointerType === 'mouse' && e.button !== 0)) return;
        e.currentTarget.setPointerCapture?.(e.pointerId);
        drag(pointAt(e.clientX));
    };

    const onPointerMove = (e: PointerEvent<HTMLDivElement>) => {
        if (!seekable) return;
        if (e.pointerType !== 'mouse' && dragRef.current === null) return;
        const percent = pointAt(e.clientX);
        if (dragRef.current !== null && percent !== null) drag(percent);
    };

    const finish = (e: PointerEvent<HTMLDivElement>, seeks: boolean) => {
        const last = dragRef.current;
        if (last === null) return;
        const percent = seeks ? (pointAt(e.clientX) ?? last) : last;
        drag(null);
        if (e.currentTarget.hasPointerCapture?.(e.pointerId)) e.currentTarget.releasePointerCapture(e.pointerId);
        if (e.pointerType !== 'mouse' || !seeks) onPointAt(null, null);
        if (seeks) onSeek(percent * duration);
    };

    const onPointerLeave = () => {
        if (dragRef.current === null) onPointAt(null, null);
    };

    const full = variant === 'full';

    return (
        <div
            role="slider"
            tabIndex={0}
            aria-label="Playback position"
            aria-valuemin={0}
            aria-valuemax={seekable ? Math.round(duration) : 0}
            aria-valuenow={seekable ? Math.round(shownPercent * duration) : 0}
            aria-valuetext={seekable ? `${spokenTime(shownPercent * duration)} of ${spokenTime(duration)}` : undefined}
            className="group relative flex-1 cursor-pointer py-2 outline-none focus-visible:rounded-full focus-visible:ring-2 focus-visible:ring-[var(--vora-accent-500)]"
            style={{ touchAction: 'none' }}
            onPointerDown={onPointerDown}
            onPointerMove={onPointerMove}
            onPointerUp={e => finish(e, true)}
            onPointerCancel={e => finish(e, false)}
            onPointerLeave={onPointerLeave}
        >
            <div
                ref={trackRef}
                className="relative h-1.5 rounded-full"
                style={{ background: `color-mix(in srgb, var(--vora-text-primary) ${full ? 18 : 12}%, transparent)` }}
            >
                {children}
                <div
                    data-testid="seek-fill"
                    className={`absolute left-0 top-0 h-full rounded-full ${full && !dragging ? 'transition-all' : ''}`}
                    style={{ width: `${shownPercent * 100}%`, background: 'var(--vora-accent-500)' }}
                />
                <div
                    className={`absolute top-1/2 -mt-1.5 h-3 w-3 rounded-full ${dragging ? 'opacity-100' : 'opacity-0 group-hover:opacity-100 group-focus-visible:opacity-100'} ${full ? 'shadow-[var(--vora-shadow-lg)] transition-opacity' : 'shadow-[var(--vora-shadow-sm)]'}`}
                    style={{
                        left: `calc(${shownPercent * 100}% - 6px)`,
                        background: 'var(--vora-text-primary)',
                        boxShadow: full ? '0 0 0 1.5px color-mix(in srgb, var(--vora-bg-canvas) 45%, transparent)' : undefined,
                    }}
                />
            </div>
        </div>
    );
}
