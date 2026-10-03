import type { ReactNode } from 'react';

interface NowPlayingArtworkProps {
    artworkKey: string;
    posterUrl?: string;
    title: string;
    subtitle?: string;
    meta?: ReactNode;
    fallbackIcon: ReactNode;
    size?: 'full' | 'compact' | 'mini';
    fit?: 'cover' | 'contain';
}

const ART_WIDTH = { full: 'w-[min(420px,55vh)]', compact: 'w-[200px]', mini: 'w-[min(120px,14vh)]' } as const;

export function NowPlayingArtwork({ artworkKey, posterUrl, title, subtitle, meta, fallbackIcon, size = 'full', fit = 'cover' }: NowPlayingArtworkProps) {
    const compact = size !== 'full';
    return (
        <>
            <div className={`shrink-0 transition-all duration-500 ${compact ? 'mt-2' : 'flex flex-1 items-end pb-6'}`}>
                <div
                    key={artworkKey}
                    className={`aspect-square overflow-hidden transition-all duration-500 ${ART_WIDTH[size]}`}
                    style={{
                        borderRadius: 'var(--vora-radius-lg)',
                        boxShadow: 'var(--vora-shadow-overlay)',
                        border: '1px solid var(--vora-border-subtle)',
                        background: 'var(--vora-bg-sunken)',
                    }}
                >
                    {posterUrl ? (
                        <img src={posterUrl} alt={title} className={`h-full w-full ${fit === 'contain' ? 'object-contain p-6' : 'object-cover'}`} />
                    ) : (
                        <div className="flex h-full w-full items-center justify-center" style={{ color: 'var(--vora-text-disabled)' }}>
                            {fallbackIcon}
                        </div>
                    )}
                </div>
            </div>

            <div className={`${size === 'mini' ? 'mt-3' : 'mt-5'} w-full max-w-[640px] shrink-0 text-center`}>
                <h1
                    className={`m-0 truncate font-semibold transition-all duration-300 ${size === 'mini' ? 'text-xl' : compact ? 'text-2xl' : 'text-3xl'}`}
                    style={{ color: 'var(--vora-text-primary)', letterSpacing: '-0.01em' }}
                    title={title}
                >
                    {title}
                </h1>
                {subtitle && (
                    <p className="mt-1.5 truncate text-base" style={{ color: 'var(--vora-text-secondary)' }} title={subtitle}>
                        {subtitle}
                    </p>
                )}
                {meta}
            </div>
        </>
    );
}
