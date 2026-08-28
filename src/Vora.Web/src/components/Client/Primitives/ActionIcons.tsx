// The glyphs used by the detail-page action row. Kept together so the row reads
// as a set and a button's icon can't drift from the same icon used elsewhere.
// All are 24x24 stroke icons unless noted, sized by the caller.

const stroke = {
    fill: 'none',
    stroke: 'currentColor',
    strokeWidth: 2,
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
};

export function PlayIcon({ size = 18 }: { size?: number }) {
    return <svg width={size} height={size} viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><polygon points="5 3 19 12 5 21 5 3" /></svg>;
}

// A circular arrow returning to the start — the previous double-chevron read as
// "skip backwards", not "start over".
export function RestartIcon({ size = 18 }: { size?: number }) {
    return (
        <svg width={size} height={size} viewBox="0 0 24 24" {...stroke} aria-hidden="true">
            <path d="M3 12a9 9 0 1 0 3-6.7" />
            <polyline points="3 4 3 9 8 9" />
        </svg>
    );
}

export function GearIcon({ size = 18 }: { size?: number }) {
    return (
        <svg width={size} height={size} viewBox="0 0 24 24" {...stroke} aria-hidden="true">
            <circle cx="12" cy="12" r="3.2" />
            <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.6h.09A1.65 1.65 0 0 0 10.09 3V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82v.09a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z" />
        </svg>
    );
}

// Film reel — the trailer affordance. Solid, not outlined: the reel body is a
// filled disc and the holes are punched out of it with evenodd, which is what
// makes it read as a reel rather than a ring of dots. The arm at the lower
// right is part of the silhouette.
export function FilmReelIcon({ size = 18 }: { size?: number }) {
    return (
        <svg width={size} height={size} viewBox="0 0 24 24" aria-hidden="true">
            <path
                fill="currentColor"
                fillRule="evenodd"
                clipRule="evenodd"
                d="M2.2 10.5a8.3 8.3 0 1 0 16.6 0 8.3 8.3 0 1 0-16.6 0Z
                   M8.9 10.5a1.6 1.6 0 1 0 3.2 0 1.6 1.6 0 1 0-3.2 0Z
                   M8.75 6.15a1.75 1.75 0 1 0 3.5 0 1.75 1.75 0 1 0-3.5 0Z
                   M8.75 14.85a1.75 1.75 0 1 0 3.5 0 1.75 1.75 0 1 0-3.5 0Z
                   M4.4 10.5a1.75 1.75 0 1 0 3.5 0 1.75 1.75 0 1 0-3.5 0Z
                   M13.1 10.5a1.75 1.75 0 1 0 3.5 0 1.75 1.75 0 1 0-3.5 0Z"
            />
            <path
                d="M15.2 15.2 20.4 20.4"
                stroke="currentColor"
                strokeWidth={2.6}
                strokeLinecap="round"
                fill="none"
            />
        </svg>
    );
}

// Outlined bookmark, matching the watchlist affordance viewers know from Plex.
export function BookmarkIcon({ size = 18, filled = false }: { size?: number; filled?: boolean }) {
    return (
        <svg width={size} height={size} viewBox="0 0 24 24" {...stroke} fill={filled ? 'currentColor' : 'none'} aria-hidden="true">
            <path d="M6 4.8A1.8 1.8 0 0 1 7.8 3h8.4A1.8 1.8 0 0 1 18 4.8V21l-6-4.2L6 21z" />
        </svg>
    );
}

export function PencilIcon({ size = 18 }: { size?: number }) {
    return (
        <svg width={size} height={size} viewBox="0 0 24 24" {...stroke} aria-hidden="true">
            <path d="M12 20h9" />
            <path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4z" />
        </svg>
    );
}

export function CheckIcon({ size = 18, bold = false }: { size?: number; bold?: boolean }) {
    return <svg width={size} height={size} viewBox="0 0 24 24" {...stroke} strokeWidth={bold ? 3 : 2} aria-hidden="true"><polyline points="20 6 9 17 4 12" /></svg>;
}

export function MoreIcon({ size = 18 }: { size?: number }) {
    return (
        <svg width={size} height={size} viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
            <path d="M12 8c1.1 0 2-.9 2-2s-.9-2-2-2-2 .9-2 2 .9 2 2 2zm0 2c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2zm0 6c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2z" />
        </svg>
    );
}
