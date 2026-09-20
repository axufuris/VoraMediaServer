interface GlobalSearchTriggerProps {
    onClick?: () => void;
}

const IS_MAC = typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform);

export default function GlobalSearchTrigger({ onClick }: GlobalSearchTriggerProps) {
    const isMac = IS_MAC;

    return (
        <button
            type="button"
            onClick={onClick}
            className="flex items-center gap-2 px-3 py-1.5 rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-surface)] hover:border-[var(--vora-border-strong)] transition-colors cursor-pointer text-sm text-[var(--vora-text-muted)] min-w-[220px] justify-between"
            title="Jump to a page (⌘K / Ctrl+K)"
        >
            <span className="flex items-center gap-2">
                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M21 21l-4.35-4.35m0 0A7.5 7.5 0 1010.5 18a7.46 7.46 0 006.15-3.35z" /></svg>
                <span>Search…</span>
            </span>
            <kbd className="text-[10px] font-mono font-semibold text-[var(--vora-text-disabled)] border border-[var(--vora-border-subtle)] rounded px-1 py-0.5 bg-[var(--vora-bg-surface)]">
                {isMac ? '⌘K' : 'Ctrl K'}
            </kbd>
        </button>
    );
}
