import type { ReactNode } from 'react';

interface NowPlayingSongBarProps {
    posterUrl?: string;
    title: string;
    subtitle?: string;
    showArt: boolean;
    fallbackIcon: ReactNode;
    details?: ReactNode;
}

// The song's details in one row, used while Lyrics or the Synth fills the
// screen: cover on the left, title centred, quality and mood on the right.
// The three columns keep the title on the screen's centre line whatever the
// widths of the two sides.
export function NowPlayingSongBar({ posterUrl, title, subtitle, showArt, fallbackIcon, details }: NowPlayingSongBarProps) {
    return (
        <div data-testid="now-playing-song-bar" className="grid w-full max-w-[1080px] shrink-0 grid-cols-[minmax(0,1fr)_minmax(0,auto)_minmax(0,1fr)] items-center gap-4 pt-2 md:gap-7">
            <div className="flex justify-end">
                {showArt && (
                    <div
                        className="h-16 w-16 overflow-hidden md:h-[88px] md:w-[88px]"
                        style={{
                            borderRadius: 'var(--vora-radius-md)',
                            boxShadow: 'var(--vora-shadow-overlay)',
                            border: '1px solid var(--vora-border-subtle)',
                            background: 'var(--vora-bg-sunken)',
                        }}
                    >
                        {posterUrl ? (
                            <img src={posterUrl} alt={title} className="h-full w-full object-cover" />
                        ) : (
                            <div className="flex h-full w-full items-center justify-center [&>svg]:h-8 [&>svg]:w-8" style={{ color: 'var(--vora-text-disabled)' }}>
                                {fallbackIcon}
                            </div>
                        )}
                    </div>
                )}
            </div>
            <div className="min-w-0 max-w-[560px] text-center">
                <h1 className="m-0 truncate text-xl font-semibold md:text-2xl" style={{ color: 'var(--vora-text-primary)', letterSpacing: '-0.01em' }} title={title}>
                    {title}
                </h1>
                {subtitle && (
                    <p className="mt-1 truncate text-sm md:text-base" style={{ color: 'var(--vora-text-secondary)' }} title={subtitle}>
                        {subtitle}
                    </p>
                )}
            </div>
            <div className="flex min-w-0 justify-start">{details}</div>
        </div>
    );
}
