import type { ReactNode } from 'react';

interface NowPlayingArtworkProps {
    artworkKey: string;
    posterUrl?: string;
    title: string;
    subtitle?: string;
    meta?: ReactNode;
    fallbackIcon: ReactNode;
    fit?: 'cover' | 'contain';
}

export function NowPlayingArtwork({ artworkKey, posterUrl, title, subtitle, meta, fallbackIcon, fit = 'cover' }: NowPlayingArtworkProps) {
    return (
        <>
            <div className="flex flex-1 shrink-0 items-end pb-6 max-md:pb-4">
                <div
                    key={artworkKey}
                    className="aspect-square w-[min(420px,55vh)] overflow-hidden max-md:w-[min(calc(100vw-2rem),40vh)]"
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

            <div className="mt-5 w-full max-w-[640px] shrink-0 text-center max-md:mt-2">
                <h1
                    className="m-0 truncate text-3xl font-semibold max-md:text-2xl"
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
