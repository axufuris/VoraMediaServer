// The "fully played" treatment for every tile in the client, and the mirror of
// `MediaPoster.kt` in the Android client — the two are deliberately the same
// shape so a viewer moving between phone and browser sees one thing.
//
// Following Plex: a card that carries a corner label (an episode number) folds
// the check INTO that label's chip as a leading glyph, rather than putting a
// second circle beside it. Cards with no corner label — movies, shows, seasons,
// collections — get the standalone circular badge.
//
// Everything that draws a watched state imports from here. Three separate
// hand-rolled versions had already drifted apart: the poster card used an
// accent check, the episode list a white one on a different background, and the
// admin overlay mockup copied the episode list — so the admin previewing badge
// collisions was looking at something the client never drew.

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

// Standalone circular badge: translucent chip, subtle border, accent check.
export function WatchedBadge({ diameter = 24 }: { diameter?: number }) {
    return (
        <span
            className="inline-flex items-center justify-center rounded-full backdrop-blur-sm"
            style={{
                width: diameter,
                height: diameter,
                background: 'var(--vora-bg-overlay)',
                border: '1px solid var(--vora-border-subtle)',
                color: 'var(--vora-accent-500)',
            }}
        >
            <CheckGlyph size={Math.round(diameter * 0.55)} />
        </span>
    );
}

// The corner chip an episode gets: its number, with the check to its left only
// once the episode has been watched. The label stays put either way, so the
// chip does not jump position when an episode is marked played.
export function CornerLabelChip({ label, watched, fontSize = 10 }: { label: string; watched?: boolean; fontSize?: number }) {
    return (
        <span
            className="inline-flex items-center gap-1 rounded backdrop-blur-sm"
            style={{
                background: 'var(--vora-bg-overlay)',
                border: '1px solid var(--vora-border-subtle)',
                padding: '2px 6px',
                fontSize,
                fontWeight: 700,
                lineHeight: 1.2,
                color: 'var(--vora-text-primary)',
            }}
        >
            {watched && <CheckGlyph size={fontSize} color="var(--vora-accent-500)" />}
            {label}
        </span>
    );
}
