// Third-party rating platforms render as their mark plus the score, not as a
// spelled-out provider name — "Internet Movie Database 5.9" is the provider's
// database name, not something a viewer reads. The name strings come from the
// ratings provider and are matched the same way the server-side poster overlay
// matches them (see BadgeResolver.ResolveRatingPlatformFile), so the badge on a
// poster and the badge on the detail page always agree.
//
// Rotten Tomatoes has two marks: a fresh tomato at or above 60, a green splat
// below it. Anything unrecognised falls back to the provider's own name so a
// new ratings plugin still renders something sensible.

import { resolveRatingPlatform, formatRatingValue, ROTTEN_TOMATOES_FRESH_THRESHOLD, type RatingPlatform } from '../../../utils/ratings';

function FreshTomato() {
    return (
        <svg width="16" height="16" viewBox="0 0 24 24" aria-hidden="true">
            <path d="M12 4c1 0 1.6-.6 2.2-1.4.3.9.1 1.7-.4 2.3 1.2-.5 2-.2 2.7.4-.7.1-1.2.5-1.5 1.1" fill="#3a7d3a" />
            <circle cx="12" cy="14" r="7.4" fill="#fa320a" />
            <path d="M12 6.6a7.4 7.4 0 0 0-4.6 1.6c1.1-.4 2.3-.3 3 .5.6-1.1 1.4-1.6 2.5-1.9a7.5 7.5 0 0 0-.9-.2z" fill="#ff5a33" />
        </svg>
    );
}

function RottenSplat() {
    return (
        <svg width="16" height="16" viewBox="0 0 24 24" aria-hidden="true">
            <path
                d="M12 2.6l1.9 2.6 3-1.1-.5 3.2 3.1.9-2 2.5 2.3 2.2-3 1.3.7 3.2-3.2-.6-1.4 2.9-1.9-2.6-3 1.2.4-3.2-3.1-1 2-2.4-2.2-2.3 3-1.2-.6-3.2 3.1.7z"
                fill="#00a44b"
            />
            <circle cx="10.4" cy="11.2" r="1.1" fill="#0b3d22" />
            <circle cx="14" cy="13.4" r="1.1" fill="#0b3d22" />
        </svg>
    );
}

function ImdbMark() {
    return (
        <svg width="30" height="15" viewBox="0 0 60 30" aria-hidden="true">
            <rect width="60" height="30" rx="5" fill="#f5c518" />
            <text x="30" y="21" textAnchor="middle" fontSize="15" fontWeight="700" fontFamily="Arial, Helvetica, sans-serif" fill="#000">IMDb</text>
        </svg>
    );
}

function TmdbMark() {
    return (
        <svg width="30" height="15" viewBox="0 0 60 30" aria-hidden="true">
            <defs>
                <linearGradient id="vora-tmdb" x1="0" y1="0" x2="1" y2="0">
                    <stop offset="0%" stopColor="#90cea1" />
                    <stop offset="100%" stopColor="#01b4e4" />
                </linearGradient>
            </defs>
            <rect width="60" height="30" rx="5" fill="url(#vora-tmdb)" />
            <text x="30" y="21" textAnchor="middle" fontSize="14" fontWeight="700" fontFamily="Arial, Helvetica, sans-serif" fill="#0d253f">TMDB</text>
        </svg>
    );
}

// Metacritic's square is colour-coded by score: green from 61, yellow 40–60,
// red below 40.
function MetacriticMark({ score }: { score: number }) {
    const fill = score >= 61 ? '#00ce7a' : score >= 40 ? '#ffbd3f' : '#ff6874';
    return (
        <svg width="16" height="16" viewBox="0 0 24 24" aria-hidden="true">
            <rect width="24" height="24" rx="4" fill={fill} />
            <path d="M12 5.5l1.9 3.9 4.3.6-3.1 3 .7 4.2-3.8-2-3.8 2 .7-4.2-3.1-3 4.3-.6z" fill="#0b1a2b" />
        </svg>
    );
}

function DotMark({ color }: { color: string }) {
    return (
        <svg width="14" height="14" viewBox="0 0 24 24" aria-hidden="true">
            <circle cx="12" cy="12" r="9" fill={color} />
        </svg>
    );
}

function PlatformMark({ platform, score }: { platform: RatingPlatform; score: number }) {
    switch (platform) {
        case 'imdb': return <ImdbMark />;
        case 'tmdb': return <TmdbMark />;
        case 'rt-critic':
        case 'rt-audience':
            return score >= ROTTEN_TOMATOES_FRESH_THRESHOLD ? <FreshTomato /> : <RottenSplat />;
        case 'metacritic': return <MetacriticMark score={score} />;
        case 'trakt': return <DotMark color="#ed1c24" />;
        case 'letterboxd': return <DotMark color="#00e054" />;
        case 'mal': return <DotMark color="#2e51a2" />;
        case 'anidb': return <DotMark color="#3d5a80" />;
        case 'mdblist': return <DotMark color="#d8a24a" />;
    }
}

export default function RatingBadge({ value, name }: { value: number; name?: string }) {
    const platform = resolveRatingPlatform(name);
    const display = formatRatingValue(value, name);
    const label = name ? `${name}: ${display}` : display;

    return (
        <div className="flex items-center gap-1.5" title={label} aria-label={label}>
            {platform
                ? <PlatformMark platform={platform} score={value} />
                : <span className="text-xs font-semibold" style={{ color: 'var(--vora-text-muted)' }}>{name}</span>}
            <span className="text-sm font-semibold tabular-nums" style={{ color: 'var(--vora-text-primary)' }}>{display}</span>
        </div>
    );
}
