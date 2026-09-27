import { type ReactNode } from 'react';

// The top-right corner of every tile in the client, and the mirror of
// `MediaPoster.kt` in the Android client, so a viewer moving between phone and
// browser sees one thing.
//
// One box for every poster: an episode's box carries its number, with the check
// folded in to its left once watched (Plex's treatment); anything else that has
// been watched gets the same box holding just the check. It used to be a circle
// for movies and a box for episodes, which read as two different badges.
//
// Everything that draws a watched state imports from here, the admin overlay
// editor included, so the collision an admin judges there is the one the client
// draws. `scale` lets the editor draw a card-proportioned copy on its larger
// canvas; 1 is the size on a card.

// The same `M5 13l4 4L19 7` path the Android CheckGlyph rasterises.
export function CheckGlyph({ size, className, color }: { size: number; className?: string; color?: string }) {
    return (
        <svg
            width={size}
            height={size}
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth={3}
            className={className}
            style={color ? { color } : undefined}
            aria-hidden="true"
        >
            <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
        </svg>
    );
}

const FONT_SIZE = 10;

// Places a badge in the tile's top-right corner, 6px in as Android does. A flex
// box rather than a plain block: an inline-flex chip inside a block sits on the
// block's text baseline, which pushed it several pixels below the corner.
export function PosterCorner({ children, scale = 1, zIndex = 10 }: { children: ReactNode; scale?: number; zIndex?: number }) {
    return (
        <div
            className="pointer-events-none absolute flex items-start justify-end"
            style={{ top: 6 * scale, right: 6 * scale, zIndex }}
        >
            {children}
        </div>
    );
}

export function CornerChip({ label, watched, scale = 1 }: { label?: string | null; watched?: boolean; scale?: number }) {
    const fontSize = FONT_SIZE * scale;
    const checkOnly = !label;
    return (
        <span
            className="inline-flex items-center backdrop-blur-sm"
            style={{
                gap: 4 * scale,
                borderRadius: 4 * scale,
                background: 'var(--vora-bg-overlay)',
                border: `${Math.max(1, scale)}px solid var(--vora-border-subtle)`,
                padding: checkOnly ? `${2 * scale}px ${4 * scale}px` : `${2 * scale}px ${6 * scale}px`,
                fontSize,
                fontWeight: 700,
                lineHeight: 1.2,
                color: 'var(--vora-text-primary)',
            }}
        >
            {watched && <CheckGlyph size={checkOnly ? fontSize * 1.2 : fontSize} color="var(--vora-accent-500)" />}
            {label}
        </span>
    );
}

// The watched box for anything without an episode number.
export function WatchedBadge({ scale = 1 }: { scale?: number }) {
    return <CornerChip watched scale={scale} />;
}

// A show's or season's count of unwatched episodes.
export function UnplayedCountBadge({ count, scale = 1 }: { count: number; scale?: number }) {
    return (
        <span
            className="inline-flex items-center justify-center font-bold"
            style={{
                minWidth: 18 * scale,
                borderRadius: 4 * scale,
                padding: `${2 * scale}px ${5 * scale}px`,
                fontSize: FONT_SIZE * scale,
                lineHeight: 1.2,
                background: 'var(--vora-accent-500)',
                color: 'var(--vora-accent-contrast)',
            }}
        >
            {count}
        </span>
    );
}
