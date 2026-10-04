import { useEffect, useRef, type ReactNode } from 'react';
import type { SynthColors, SynthStyle } from '../../../utils/nowPlayingView';

interface SynthStyleMenuProps {
    id: string;
    style: SynthStyle;
    colors: SynthColors;
    onStyle: (style: SynthStyle) => void;
    onColors: (colors: SynthColors) => void;
    onClose: () => void;
}

const STYLES: { value: SynthStyle; label: string; icon: ReactNode }[] = [
    {
        value: 'bars',
        label: 'Bars',
        icon: <svg width="36" height="20" viewBox="0 0 36 20" fill="currentColor" aria-hidden="true"><rect x="2" y="8" width="3" height="4" rx="1.5" /><rect x="8" y="5" width="3" height="10" rx="1.5" /><rect x="14" y="1" width="3" height="18" rx="1.5" /><rect x="20" y="3" width="3" height="14" rx="1.5" /><rect x="26" y="6" width="3" height="8" rx="1.5" /><rect x="32" y="8" width="2" height="4" rx="1" /></svg>,
    },
    {
        value: 'waves',
        label: 'Waves',
        icon: <svg width="36" height="20" viewBox="0 0 36 20" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true"><path d="M1 10c4-7 7-7 10 0s7 7 10 0 7-7 14 0" /><path d="M1 10c5-4 9-4 12 0s8 4 11 0 7-4 11 0" opacity=".5" /></svg>,
    },
    {
        value: 'ring',
        label: 'Ring',
        icon: <svg width="36" height="20" viewBox="0 0 36 20" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true"><circle cx="18" cy="10" r="5" /><path d="M18 1v2M18 17v2M9 10h2M25 10h2M12 4l1.5 1.5M24 16l-1.5-1.5M24 4l-1.5 1.5M12 16l1.5-1.5" /></svg>,
    },
];

const COLORS: { value: SynthColors; label: string }[] = [
    { value: 'art', label: 'From album art' },
    { value: 'theme', label: 'Theme accent' },
];

const optionClass = 'vora-pill flex cursor-pointer flex-col items-center justify-center gap-1.5 rounded-lg px-2 py-2.5 text-xs font-medium';

export default function SynthStyleMenu({ id, style, colors, onStyle, onColors, onClose }: SynthStyleMenuProps) {
    const panelRef = useRef<HTMLDivElement>(null);

    // Focus lands on the current choice so a remote can move straight between
    // the options instead of hunting for the panel.
    useEffect(() => {
        panelRef.current?.querySelector<HTMLButtonElement>('button[aria-pressed="true"]')?.focus();
    }, []);

    return (
        <div
            id={id}
            ref={panelRef}
            role="dialog"
            aria-label="Synth style"
            onKeyDown={e => {
                if (e.key === 'Escape') {
                    e.stopPropagation();
                    onClose();
                }
            }}
            className="absolute bottom-full right-4 z-20 mb-2 w-80 max-w-[calc(100vw-2rem)] rounded-xl p-4 md:right-8"
            style={{ background: 'var(--vora-bg-raised)', border: '1px solid var(--vora-border-strong)', boxShadow: 'var(--vora-shadow-overlay)' }}
        >
            <div className="mb-1.5 text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Style</div>
            <div className="grid grid-cols-3 gap-2">
                {STYLES.map(option => (
                    <button
                        key={option.value}
                        type="button"
                        aria-pressed={style === option.value}
                        data-active={style === option.value}
                        onClick={() => onStyle(option.value)}
                        className={optionClass}
                    >
                        {option.icon}
                        {option.label}
                    </button>
                ))}
            </div>
            <div className="mb-1.5 mt-4 text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Colours</div>
            <div className="grid grid-cols-2 gap-2">
                {COLORS.map(option => (
                    <button
                        key={option.value}
                        type="button"
                        aria-pressed={colors === option.value}
                        data-active={colors === option.value}
                        onClick={() => onColors(option.value)}
                        className={optionClass}
                    >
                        {option.label}
                    </button>
                ))}
            </div>
        </div>
    );
}
