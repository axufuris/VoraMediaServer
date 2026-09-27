import { useEffect, useRef, type ReactNode, type RefObject } from 'react';
import { useSpatialNavigation } from '../../../hooks/useSpatialNavigation';

// The full-screen frame shared by every audio "now playing" screen: blurred
// artwork backdrop, glass header, a main area and a control bar. Music and live
// radio render through it so the two screens cannot drift into different
// looks, and so D-pad focus, Escape-to-minimize and template colours are handled
// once.
interface NowPlayingShellProps {
    artworkKey: string;
    posterUrl?: string;
    label: string;
    onMinimize: () => void;
    onClose: () => void;
    headerActions?: ReactNode;
    controls: ReactNode;
    children: ReactNode;
    overlays?: ReactNode;
    initialFocusRef?: RefObject<HTMLElement | null>;
}

export function NowPlayingShell({
    artworkKey,
    posterUrl,
    label,
    onMinimize,
    onClose,
    headerActions,
    controls,
    children,
    overlays,
    initialFocusRef,
}: NowPlayingShellProps) {
    const screenRef = useRef<HTMLDivElement>(null);

    useSpatialNavigation(screenRef, true);

    // A D-pad can only move focus that already exists, and a remote cannot
    // click to create it, so the screen hands focus to its main control on open.
    useEffect(() => {
        initialFocusRef?.current?.focus();
    }, [initialFocusRef]);

    useEffect(() => {
        const onKey = (e: KeyboardEvent) => {
            if (e.key === 'Escape') onMinimize();
        };
        window.addEventListener('keydown', onKey);
        return () => window.removeEventListener('keydown', onKey);
    }, [onMinimize]);

    return (
        <div ref={screenRef} className="fixed inset-0 z-[100000] flex flex-col overflow-hidden" style={{ background: 'var(--vora-bg-canvas)', color: 'var(--vora-text-primary)' }}>
            <div className="absolute inset-0 z-0">
                {posterUrl ? (
                    <img
                        key={artworkKey}
                        src={posterUrl}
                        alt=""
                        className="h-full w-full object-cover transition-opacity duration-700"
                        style={{ filter: 'blur(60px) saturate(140%)', opacity: 0.5, transform: 'scale(1.15)' }}
                    />
                ) : (
                    <div className="h-full w-full" style={{ background: 'radial-gradient(circle at 30% 20%, color-mix(in srgb, var(--vora-accent-500) 25%, transparent), transparent 60%), var(--vora-bg-canvas)' }} />
                )}
                <div className="absolute inset-0" style={{ background: 'linear-gradient(180deg, color-mix(in srgb, var(--vora-bg-canvas) 30%, transparent) 0%, color-mix(in srgb, var(--vora-bg-canvas) 60%, transparent) 50%, var(--vora-bg-canvas) 100%)' }} />
            </div>

            <header
                className="relative z-20 grid shrink-0 grid-cols-3 items-center px-6 py-4 vora-glass"
                style={{ borderBottom: '1px solid var(--vora-border-subtle)' }}
            >
                <div className="flex justify-start">
                    <button
                        type="button"
                        onClick={onMinimize}
                        aria-label="Minimize"
                        title="Minimize (Esc)"
                        className="vora-icon-button inline-flex h-9 w-9 cursor-pointer items-center justify-center rounded-full"
                        style={{ color: 'var(--vora-text-secondary)' }}
                    >
                        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true"><polyline points="6 9 12 15 18 9" /></svg>
                    </button>
                </div>
                <div className="truncate text-center text-xs font-semibold uppercase tracking-widest" style={{ color: 'var(--vora-text-muted)' }}>
                    {label}
                </div>
                <div className="flex items-center justify-end gap-1.5">
                    {headerActions}
                    <button
                        type="button"
                        onClick={onClose}
                        aria-label="Stop and close player"
                        title="Stop & close player"
                        className="vora-icon-button ml-1 inline-flex h-9 w-9 cursor-pointer items-center justify-center rounded-full"
                        style={{ color: 'var(--vora-text-secondary)' }}
                    >
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true"><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></svg>
                    </button>
                </div>
            </header>

            <div className="relative z-10 flex min-h-0 flex-1">
                {children}
            </div>

            <div className="relative z-10 shrink-0 px-8 pb-7 pt-2">
                {controls}
            </div>

            {overlays}
        </div>
    );
}
