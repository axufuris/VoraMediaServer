import type { PlayerKeyIndicatorState } from './usePlayerKeyboard';

function SeekIcon({ forward }: { forward: boolean }) {
    return (
        <svg width="22" height="22" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true" style={forward ? undefined : { transform: 'scaleX(-1)' }}>
            <path d="M4 18l8.5-6L4 6v12zm9-12v12l8.5-6L13 6z" />
        </svg>
    );
}

function VolumeIcon({ muted }: { muted: boolean }) {
    return (
        <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
            <polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5" />
            {muted
                ? <><line x1="23" y1="9" x2="17" y2="15" /><line x1="17" y1="9" x2="23" y2="15" /></>
                : <><path d="M15.54 8.46a5 5 0 010 7.07" /><path d="M19.07 4.93a10 10 0 010 14.14" /></>}
        </svg>
    );
}

function PlayPauseIcon({ playing }: { playing: boolean }) {
    return (
        <svg width="22" height="22" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
            {playing ? <polygon points="6 4 20 12 6 20 6 4" /> : <><rect x="6" y="4" width="4" height="16" rx="1" /><rect x="14" y="4" width="4" height="16" rx="1" /></>}
        </svg>
    );
}

function describe(indicator: PlayerKeyIndicatorState): string {
    if (indicator.kind === 'playPause') return indicator.playing ? 'Playing' : 'Paused';
    if (indicator.kind === 'volume') return `Volume ${Math.round(indicator.volume * 100)}%`;
    const seconds = Math.abs(indicator.seconds);
    return `${indicator.seconds > 0 ? 'Forward' : 'Back'} ${seconds} second${seconds === 1 ? '' : 's'}`;
}

// The brief readout in the middle of the picture after Space, or an arrow-key
// seek or volume change. The status region stays mounted so a screen reader hears each
// change rather than only the first.
export default function PlayerKeyIndicator({ indicator }: { indicator: PlayerKeyIndicatorState | null }) {
    return (
        <div className="pointer-events-none absolute inset-0 z-40 flex items-center justify-center">
            <div role="status" aria-live="polite" className="sr-only">{indicator ? describe(indicator) : ''}</div>
            {indicator && (
                <div
                    data-testid="player-key-indicator"
                    aria-hidden="true"
                    className="flex items-center gap-3 rounded-xl px-5 py-3 text-lg font-semibold tabular-nums backdrop-blur-md"
                    style={{
                        background: 'color-mix(in srgb, var(--vora-bg-canvas) 72%, transparent)',
                        border: '1px solid color-mix(in srgb, var(--vora-text-primary) 16%, transparent)',
                        color: 'var(--vora-text-primary)',
                        boxShadow: 'var(--vora-shadow-overlay)',
                    }}
                >
                    {indicator.kind === 'playPause' ? (
                        <>
                            <PlayPauseIcon playing={indicator.playing} />
                            <span>{indicator.playing ? 'Playing' : 'Paused'}</span>
                        </>
                    ) : indicator.kind === 'seek' ? (
                        <>
                            <SeekIcon forward={indicator.seconds > 0} />
                            <span>{indicator.seconds > 0 ? '+' : '−'}{Math.abs(indicator.seconds)}s</span>
                        </>
                    ) : (
                        <>
                            <VolumeIcon muted={indicator.volume === 0} />
                            <span className="relative block h-1.5 w-24 overflow-hidden rounded-full" style={{ background: 'color-mix(in srgb, var(--vora-text-primary) 20%, transparent)' }}>
                                <span className="absolute inset-y-0 left-0 block rounded-full" style={{ width: `${Math.round(indicator.volume * 100)}%`, background: 'var(--vora-accent-500)' }} />
                            </span>
                            <span className="min-w-[3ch] text-right">{Math.round(indicator.volume * 100)}%</span>
                        </>
                    )}
                </div>
            )}
        </div>
    );
}
