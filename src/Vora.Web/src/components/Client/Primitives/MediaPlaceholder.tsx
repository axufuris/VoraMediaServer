import { useId } from 'react';

export type PlaceholderVariant = 'poster' | 'still' | 'actor';

// Branded fallback shown when a poster/still/photo is missing OR fails to load,
// instead of the browser's broken-image icon. Uses tokens so it recolors per
// template. The chevron-V mark echoes the Vora wordmark. No title text — the
// title is already shown next to/under every poster, so it isn't duplicated.
export default function MediaPlaceholder({ title, variant = 'poster' }: { title?: string; variant?: PlaceholderVariant }) {
    const gradientId = useId();

    if (variant === 'actor') {
        return (
            <div
                role="img"
                aria-label={title}
                className="flex h-full w-full items-center justify-center"
                style={{ background: 'radial-gradient(circle at 50% 35%, var(--vora-bg-surface), var(--vora-bg-sunken))' }}
            >
                <svg width="46%" height="46%" viewBox="0 0 24 24" fill="currentColor" style={{ color: 'var(--vora-text-disabled)', opacity: 0.7 }} aria-hidden="true">
                    <path d="M12 12a5 5 0 1 0 0-10 5 5 0 0 0 0 10Zm0 2c-4.42 0-8 2.24-8 5v1h16v-1c0-2.76-3.58-5-8-5Z" />
                </svg>
            </div>
        );
    }

    return (
        <div
            role="img"
            aria-label={title}
            className="flex h-full w-full items-center justify-center"
            style={{ background: 'linear-gradient(155deg, var(--vora-bg-surface), var(--vora-bg-sunken))' }}
        >
            <svg viewBox="0 0 64 64" style={{ width: variant === 'still' ? '24%' : '38%', opacity: 0.3 }} aria-hidden="true">
                <defs>
                    <linearGradient id={gradientId} x1="0.1" y1="0" x2="0.55" y2="1">
                        <stop offset="0" stopColor="var(--vora-accent-text, var(--vora-accent-400))" />
                        <stop offset="1" stopColor="var(--vora-accent-500)" />
                    </linearGradient>
                </defs>
                <path d="M6 8 L18 8 L32 40 L46 8 L58 8 L32 60 Z" fill={`url(#${gradientId})`} />
            </svg>
        </div>
    );
}
