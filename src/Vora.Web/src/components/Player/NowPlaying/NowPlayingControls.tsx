import type { ReactNode, Ref } from 'react';

interface NowPlayingControlRowProps {
    transport: ReactNode;
    actions: ReactNode;
}

export function NowPlayingControlRow({ transport, actions }: NowPlayingControlRowProps) {
    return (
        <div className="flex flex-col items-center gap-3 md:grid md:grid-cols-[1fr_auto_1fr] md:items-center md:gap-6">
            <div className="hidden md:block" />
            <div className="flex items-center justify-center gap-6">{transport}</div>
            <div className="flex flex-wrap items-center justify-center gap-2 md:justify-end">{actions}</div>
        </div>
    );
}

interface NowPlayingPillProps {
    label: string;
    icon: ReactNode;
    active: boolean;
    onClick: () => void;
    title?: string;
    controls?: string;
    disabled?: boolean;
}

export function NowPlayingPill({ label, icon, active, onClick, title, controls, disabled }: NowPlayingPillProps) {
    return (
        <button
            type="button"
            onClick={onClick}
            title={title ?? label}
            aria-pressed={active}
            aria-controls={controls}
            disabled={disabled}
            data-active={active}
            className="vora-pill inline-flex h-9 cursor-pointer items-center gap-2 rounded-full px-3.5 text-sm font-medium disabled:cursor-default disabled:opacity-80"
        >
            {icon}
            {label}
        </button>
    );
}

interface NowPlayingIconButtonProps {
    label: string;
    onClick: () => void;
    children: ReactNode;
    active?: boolean;
    pressed?: boolean;
    disabled?: boolean;
    emphasis?: 'primary' | 'secondary';
}

export function NowPlayingIconButton({ label, onClick, children, active = false, pressed, disabled, emphasis = 'secondary' }: NowPlayingIconButtonProps) {
    return (
        <button
            type="button"
            onClick={onClick}
            title={label}
            aria-label={label}
            aria-pressed={pressed}
            disabled={disabled}
            className="vora-icon-button inline-flex h-10 w-10 cursor-pointer items-center justify-center rounded-full disabled:cursor-default disabled:opacity-30"
            style={{ color: active ? 'var(--vora-accent-text)' : emphasis === 'primary' ? 'var(--vora-text-primary)' : 'var(--vora-text-muted)' }}
        >
            {children}
        </button>
    );
}

interface NowPlayingPlayButtonProps {
    isPlaying: boolean;
    onClick: () => void;
    buttonRef?: Ref<HTMLButtonElement>;
}

export function NowPlayingPlayButton({ isPlaying, onClick, buttonRef }: NowPlayingPlayButtonProps) {
    return (
        <button
            ref={buttonRef}
            type="button"
            onClick={onClick}
            title={isPlaying ? 'Pause' : 'Play'}
            aria-label={isPlaying ? 'Pause' : 'Play'}
            className="flex h-16 w-16 cursor-pointer items-center justify-center rounded-full transition-transform hover:scale-105"
            style={{ background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)', boxShadow: 'var(--vora-shadow-lg)' }}
        >
            {isPlaying ? (
                <svg width="28" height="28" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M6 4h4v16H6zM14 4h4v16h-4z" /></svg>
            ) : (
                <svg width="28" height="28" viewBox="0 0 24 24" fill="currentColor" style={{ marginLeft: 3 }} aria-hidden="true"><path d="M8 5v14l11-7z" /></svg>
            )}
        </button>
    );
}

interface NowPlayingVolumeProps {
    value: number;
    onChange: (value: number) => void;
}

export function NowPlayingVolume({ value, onChange }: NowPlayingVolumeProps) {
    const isMuted = value === 0;
    return (
        <div className="flex items-center gap-1">
            <NowPlayingIconButton label={isMuted ? 'Unmute' : 'Mute'} onClick={() => onChange(isMuted ? 0.7 : 0)}>
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
                    <polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5" />
                    {isMuted ? (
                        <>
                            <line x1="23" y1="9" x2="17" y2="15" />
                            <line x1="17" y1="9" x2="23" y2="15" />
                        </>
                    ) : (
                        <>
                            {value >= 0.5 && <path d="M19.07 4.93a10 10 0 010 14.14" />}
                            <path d="M15.54 8.46a5 5 0 010 7.07" />
                        </>
                    )}
                </svg>
            </NowPlayingIconButton>
            <input
                type="range"
                min={0}
                max={1}
                step={0.05}
                value={value}
                onChange={e => onChange(parseFloat(e.target.value))}
                aria-label="Volume"
                className="w-24 cursor-pointer accent-[var(--vora-accent-500)]"
            />
        </div>
    );
}
