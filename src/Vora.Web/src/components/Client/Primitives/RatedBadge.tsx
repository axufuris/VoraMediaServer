interface RatedBadgeProps {
    value: number;
    title?: string;
    color?: string;
    background?: string;
}

export default function RatedBadge({ value, title, color, background }: RatedBadgeProps) {
    const stars = (value / 2).toFixed(1);
    return (
        <span
            className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10px] font-bold backdrop-blur-md"
            style={{
                background: background ?? 'rgba(8, 8, 11, 0.72)',
                color: color ?? 'var(--vora-accent-text)',
                border: '1px solid rgba(255, 255, 255, 0.18)',
                lineHeight: 1,
            }}
            title={title ?? `Rated ${stars} of 5 stars`}
        >
            <svg width="10" height="10" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                <path d="M12 17.27L18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z" />
            </svg>
            <span className="tabular-nums">{stars}</span>
        </span>
    );
}
