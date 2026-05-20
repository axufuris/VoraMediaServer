interface PlayPauseButtonProps {
    isPlaying: boolean;
    onClick: () => void;
    size?: 'sm' | 'lg';
    tone?: 'accent' | 'neutral';
}

export function PlayPauseButton({ isPlaying, onClick, size = 'lg', tone = 'accent' }: PlayPauseButtonProps) {
    const isSmall = size === 'sm';
    const dimension = isSmall ? 40 : 64;
    const iconSize = isSmall ? 18 : 28;

    const background = tone === 'accent' ? 'var(--vora-accent-500)' : '#ffffff';
    const color = tone === 'accent' ? 'var(--vora-accent-contrast)' : '#0a0a0e';

    return (
        <button
            type="button"
            onClick={onClick}
            aria-label={isPlaying ? 'Pause' : 'Play'}
            className="flex cursor-pointer items-center justify-center rounded-full transition-transform hover:scale-105"
            style={{
                width: dimension,
                height: dimension,
                background,
                color,
                boxShadow: 'var(--vora-shadow-lg)',
                border: 'none',
            }}
        >
            {isPlaying ? (
                <svg width={iconSize} height={iconSize} viewBox="0 0 24 24" fill="currentColor"><path d="M6 4h4v16H6zM14 4h4v16h-4z" /></svg>
            ) : (
                <svg width={iconSize} height={iconSize} viewBox="0 0 24 24" fill="currentColor" style={{ marginLeft: 3 }}><path d="M8 5v14l11-7z" /></svg>
            )}
        </button>
    );
}

interface SkipButtonProps {
    seconds: number;
    onClick: () => void;
    size?: 'sm' | 'lg';
    direction: 'back' | 'forward';
}

export function SkipButton({ seconds, onClick, size = 'lg', direction }: SkipButtonProps) {
    const path = direction === 'back'
        ? 'M12 5V1L7 6l5 5V7c3.31 0 6 2.69 6 6s-2.69 6-6 6-6-2.69-6-6H4c0 4.42 3.58 8 8 8s8-3.58 8-8-3.58-8-8-8z'
        : 'M12 5V1l5 5-5 5V7c-3.31 0-6 2.69-6 6s2.69 6 6 6 6-2.69 6-6h2c0 4.42-3.58 8-8 8s-8-3.58-8-8 3.58-8 8-8z';
    const title = direction === 'back' ? `Back ${seconds}s` : `Forward ${seconds}s`;

    if (size === 'sm') {
        return (
            <button
                type="button"
                onClick={onClick}
                title={title}
                aria-label={title}
                className="cursor-pointer rounded-full p-1.5 transition-colors hover:bg-white/10"
                style={{ color: 'var(--vora-text-secondary)' }}
            >
                <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><path d={path} /></svg>
            </button>
        );
    }
    return (
        <button
            type="button"
            onClick={onClick}
            title={title}
            aria-label={title}
            className="relative cursor-pointer rounded-full p-1.5 transition-colors hover:bg-white/10"
            style={{ color: '#fafafa' }}
        >
            <svg width="36" height="36" viewBox="0 0 24 24" fill="currentColor"><path d={path} /></svg>
            <span className="pointer-events-none absolute inset-0 flex items-center justify-center text-[10px] font-bold" style={{ marginTop: 4 }}>{seconds}</span>
        </button>
    );
}

interface VolumeControlProps {
    value: number;
    onChange: (value: number) => void;
}

export function VolumeControl({ value, onChange }: VolumeControlProps) {
    const isMuted = value === 0;
    return (
        <div className="group relative flex items-center gap-2">
            <button
                type="button"
                aria-label={isMuted ? 'Unmute' : 'Mute'}
                onClick={() => onChange(isMuted ? 0.7 : 0)}
                className="cursor-pointer rounded-full p-1.5 transition-colors hover:bg-white/10"
                style={{ color: '#fafafa' }}
            >
                {isMuted ? (
                    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                        <polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5" />
                        <line x1="23" y1="9" x2="17" y2="15" />
                        <line x1="17" y1="9" x2="23" y2="15" />
                    </svg>
                ) : value < 0.5 ? (
                    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                        <polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5" />
                        <path d="M15.54 8.46a5 5 0 010 7.07" />
                    </svg>
                ) : (
                    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                        <polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5" />
                        <path d="M19.07 4.93a10 10 0 010 14.14" />
                        <path d="M15.54 8.46a5 5 0 010 7.07" />
                    </svg>
                )}
            </button>
            <div className="w-0 overflow-hidden transition-all duration-300 ease-in-out group-hover:w-24">
                <input
                    type="range"
                    min="0"
                    max="1"
                    step="0.05"
                    value={value}
                    onChange={(e) => onChange(parseFloat(e.target.value))}
                    aria-label="Volume"
                    className="h-1.5 w-full cursor-pointer accent-[var(--vora-accent-500)]"
                />
            </div>
        </div>
    );
}

interface FullscreenButtonProps {
    onClick: () => void;
}

export function FullscreenButton({ onClick }: FullscreenButtonProps) {
    return (
        <button
            type="button"
            onClick={onClick}
            title="Toggle fullscreen"
            aria-label="Toggle fullscreen"
            className="cursor-pointer rounded-full p-1.5 transition-colors hover:bg-white/10"
            style={{ color: '#fafafa' }}
        >
            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <path d="M8 3H5a2 2 0 0 0-2 2v3" />
                <path d="M21 8V5a2 2 0 0 0-2-2h-3" />
                <path d="M3 16v3a2 2 0 0 0 2 2h3" />
                <path d="M16 21h3a2 2 0 0 0 2-2v-3" />
            </svg>
        </button>
    );
}

interface MaximizeButtonProps {
    onClick: () => void;
}

export function MaximizeButton({ onClick }: MaximizeButtonProps) {
    return (
        <button
            type="button"
            onClick={onClick}
            title="Expand player"
            aria-label="Expand player"
            className="cursor-pointer rounded-full p-1.5 transition-colors hover:bg-white/10"
            style={{ color: 'var(--vora-text-secondary)' }}
        >
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <polyline points="7 14 12 9 17 14" />
            </svg>
        </button>
    );
}

interface CloseButtonProps {
    onClick: () => void;
}

interface EpisodeNavButtonProps {
    direction: 'previous' | 'next';
    onClick: () => void;
    disabled?: boolean;
    title?: string;
    size?: 'sm' | 'lg';
}

export function EpisodeNavButton({ direction, onClick, disabled, title, size = 'lg' }: EpisodeNavButtonProps) {
    const dimension = size === 'sm' ? 'h-9 w-9' : 'h-11 w-11';
    const iconSize = size === 'sm' ? 18 : 22;
    const isPrev = direction === 'previous';
    return (
        <button
            type="button"
            onClick={onClick}
            disabled={disabled}
            title={title ?? (isPrev ? 'Previous episode' : 'Next episode')}
            aria-label={title ?? (isPrev ? 'Previous episode' : 'Next episode')}
            className={`flex ${dimension} cursor-pointer items-center justify-center rounded-full transition-all disabled:cursor-not-allowed disabled:opacity-30`}
            style={{ color: 'var(--vora-text-primary)' }}
            onMouseEnter={(e) => { if (!disabled) e.currentTarget.style.background = 'rgba(255,255,255,0.1)'; }}
            onMouseLeave={(e) => { e.currentTarget.style.background = 'transparent'; }}
        >
            <svg width={iconSize} height={iconSize} viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                {isPrev ? (
                    <>
                        <path d="M6 6h2v12H6z" />
                        <path d="M19.5 5.5v13l-11-6.5z" />
                    </>
                ) : (
                    <>
                        <path d="M16 6h2v12h-2z" />
                        <path d="M4.5 5.5v13l11-6.5z" />
                    </>
                )}
            </svg>
        </button>
    );
}

export function CloseButton({ onClick }: CloseButtonProps) {
    return (
        <button
            type="button"
            onClick={onClick}
            title="Close player"
            aria-label="Close player"
            className="cursor-pointer rounded-full p-1.5 transition-colors"
            style={{ color: 'var(--vora-text-secondary)' }}
            onMouseEnter={(e) => { e.currentTarget.style.color = 'var(--vora-danger-text)'; }}
            onMouseLeave={(e) => { e.currentTarget.style.color = 'var(--vora-text-secondary)'; }}
        >
            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <line x1="18" y1="6" x2="6" y2="18" />
                <line x1="6" y1="6" x2="18" y2="18" />
            </svg>
        </button>
    );
}
