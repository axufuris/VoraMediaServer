// A song's Clean / Explicit, the way music apps mark it: a small "E" for
// explicit and a quieter "C" for clean, each named for screen readers. Every
// track list uses this, so a rating reads the same wherever the song appears.
export default function ContentRatingBadge({ rating }: { rating?: string | null }) {
    if (rating === 'Explicit') {
        return (
            <span
                role="img"
                aria-label="Explicit"
                title="Explicit"
                className="inline-flex h-4 min-w-4 shrink-0 items-center justify-center rounded-sm px-0.5 text-[10px] font-bold leading-none"
                style={{ background: 'var(--vora-text-secondary)', color: 'var(--vora-bg-canvas)' }}
            >
                E
            </span>
        );
    }

    if (rating === 'Clean') {
        return (
            <span
                role="img"
                aria-label="Clean"
                title="Clean"
                className="inline-flex h-4 min-w-4 shrink-0 items-center justify-center rounded-sm border px-0.5 text-[10px] font-bold leading-none"
                style={{ borderColor: 'var(--vora-border-strong)', color: 'var(--vora-text-muted)' }}
            >
                C
            </span>
        );
    }

    return null;
}
