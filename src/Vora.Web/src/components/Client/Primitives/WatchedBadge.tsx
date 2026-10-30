import { type ReactNode } from 'react';
import { cornerLength, LANDSCAPE_CORNER_SHARE, POSTER_CORNER_REFERENCE_WIDTH } from './posterCornerLength';

// The top-right corner of every tile in the client, and the mirror of
// `MediaPoster.kt` in the Android client and `PosterCard.bs` on the Roku, so a
// viewer moving between them sees one thing.
//
// One box for every poster: an episode's box carries its number, with the check
// folded in to its left once watched (Plex's treatment); anything else that has
// been watched gets the same box holding just the check. It used to be a circle
// for movies and a box for episodes, which read as two different badges.
//
// Every size is a share of the tile's width: the numbers below are the box on a
// tile 128 wide, and it grows and shrinks with the tile, never held at a
// minimum (not even its border). The overlay icons baked into the artwork
// scale with it the same way, so the box sits where the overlay editor shows
// it on every client and at every size. The editor draws its canvas with these
// same components.

const FONT_SIZE = 10;

// The same `M5 13l4 4L19 7` path the Android CheckGlyph rasterises.
export function CheckGlyph({ size, className, color }: { size: string; className?: string; color?: string }) {
    return (
        <svg
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth={3}
            className={className}
            style={{ width: size, height: size, flexShrink: 0, ...(color ? { color } : {}) }}
            aria-hidden="true"
        >
            <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
        </svg>
    );
}

// Measures the tile (a container the size of its artwork) and places the badge
// in its top-right corner, 6 in on a 128-wide tile. A landscape tile measures
// the right-hand LANDSCAPE_CORNER_SHARE of itself, so its box is that much
// smaller. A flex box rather than a plain block: an inline-flex chip inside a
// block sits on the block's text baseline, which pushed it several pixels
// below the corner.
export function PosterCorner({ children, landscape = false, zIndex = 10 }: { children: ReactNode; landscape?: boolean; zIndex?: number }) {
    return (
        <div
            className="pointer-events-none absolute inset-0"
            style={{ containerType: 'inline-size', zIndex, ...(landscape ? { left: `${(1 - LANDSCAPE_CORNER_SHARE) * 100}%` } : {}) }}
        >
            <div className="absolute flex items-start justify-end" style={{ top: cornerLength(6), right: cornerLength(6) }}>
                {children}
            </div>
        </div>
    );
}

// A crossed-out eye: a title an admin can see that viewers cannot.
export function HiddenGlyph({ size }: { size: string }) {
    return (
        <svg
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth={2.4}
            style={{ width: size, height: size, flexShrink: 0 }}
            aria-hidden="true"
        >
            <path strokeLinecap="round" strokeLinejoin="round" d="M13.875 18.825A10.05 10.05 0 0112 19c-4.478 0-8.268-2.943-9.543-7a9.97 9.97 0 011.563-3.029m5.858.908a3 3 0 114.243 4.243M9.878 9.878l4.242 4.242M9.88 9.88l-3.29-3.29m7.532 7.532l3.29 3.29M3 3l3.59 3.59m0 0A9.953 9.953 0 0112 5c4.478 0 8.268 2.943 9.543 7a10.025 10.025 0 01-4.132 5.411m0 0L21 21" />
        </svg>
    );
}

// Never wider than the tile less its 6-in margin on each side, so a label too
// long for one line wraps inside the tile instead of running off its left edge.
export function CornerChip({ label, watched, hiddenFromViewers }: { label?: string | null; watched?: boolean; hiddenFromViewers?: boolean }) {
    const checkOnly = !label;
    return (
        <span
            className="inline-flex items-center backdrop-blur-sm"
            style={{
                gap: cornerLength(4),
                maxWidth: cornerLength(POSTER_CORNER_REFERENCE_WIDTH - 12),
                borderRadius: cornerLength(4),
                background: 'var(--vora-bg-overlay)',
                border: `${cornerLength(1)} solid var(--vora-border-subtle)`,
                padding: checkOnly ? `${cornerLength(2)} ${cornerLength(4)}` : `${cornerLength(2)} ${cornerLength(6)}`,
                fontSize: cornerLength(FONT_SIZE),
                fontWeight: 700,
                lineHeight: 1.2,
                color: 'var(--vora-text-primary)',
            }}
        >
            {watched && <CheckGlyph size={cornerLength(checkOnly ? FONT_SIZE * 1.2 : FONT_SIZE)} color="var(--vora-accent-500)" />}
            {hiddenFromViewers && <HiddenGlyph size={cornerLength(FONT_SIZE * 1.1)} />}
            {hiddenFromViewers && <span className="sr-only">Hidden · </span>}
            {label}
        </span>
    );
}

// The watched box for anything without an episode number.
export function WatchedBadge() {
    return <CornerChip watched />;
}

// A show's or season's count of unwatched episodes.
export function UnplayedCountBadge({ count }: { count: number }) {
    return (
        <span
            className="inline-flex items-center justify-center whitespace-nowrap font-bold"
            style={{
                minWidth: cornerLength(18),
                borderRadius: cornerLength(4),
                padding: `${cornerLength(2)} ${cornerLength(5)}`,
                fontSize: cornerLength(FONT_SIZE),
                lineHeight: 1.2,
                background: 'var(--vora-accent-500)',
                color: 'var(--vora-accent-contrast)',
            }}
        >
            {count}
        </span>
    );
}
