import { useEffect, useRef } from 'react';

interface MusicSearchToggleProps {
    isOpen: boolean;
    query: string;
    onOpenChange: (open: boolean) => void;
    onQueryChange: (query: string) => void;
}

// A round search button that sits beside the Music title and reveals the search
// field inline. The field is only rendered while open, so a closed search costs
// no vertical space above the page.
export default function MusicSearchToggle({ isOpen, query, onOpenChange, onQueryChange }: MusicSearchToggleProps) {
    const inputRef = useRef<HTMLInputElement | null>(null);

    useEffect(() => {
        if (isOpen) inputRef.current?.focus();
    }, [isOpen]);

    return (
        <div className="flex items-center gap-2">
            <button
                type="button"
                onClick={() => onOpenChange(!isOpen)}
                aria-expanded={isOpen}
                aria-controls="music-search-input"
                aria-label={isOpen ? 'Close music search' : 'Search music'}
                title={isOpen ? 'Close search' : 'Search music'}
                data-active={isOpen}
                className="vora-pill inline-flex h-9 w-9 shrink-0 cursor-pointer items-center justify-center rounded-full"
            >
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                    <circle cx="11" cy="11" r="7" />
                    <line x1="21" y1="21" x2="16" y2="16" />
                </svg>
            </button>
            {isOpen && (
                <div className="relative">
                    <input
                        id="music-search-input"
                        ref={inputRef}
                        type="search"
                        value={query}
                        onChange={e => onQueryChange(e.target.value)}
                        onKeyDown={e => {
                            if (e.key === 'Escape') {
                                e.preventDefault();
                                onOpenChange(false);
                            }
                        }}
                        placeholder="Search artists, albums, tracks…"
                        aria-label="Search music"
                        className="vora-search-field h-9 w-[min(22rem,60vw)] rounded-full border pl-4 pr-9 text-sm outline-none transition-colors focus:border-[var(--vora-accent-500)]"
                        style={{
                            background: 'var(--vora-bg-sunken)',
                            borderColor: 'var(--vora-border-subtle)',
                            color: 'var(--vora-text-primary)',
                        }}
                    />
                    {query && (
                        <button
                            type="button"
                            onClick={() => { onQueryChange(''); inputRef.current?.focus(); }}
                            aria-label="Clear search"
                            title="Clear"
                            className="vora-icon-button absolute right-1.5 top-1/2 inline-flex h-6 w-6 -translate-y-1/2 cursor-pointer items-center justify-center rounded-full"
                            style={{ color: 'var(--vora-text-muted)' }}
                        >
                            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2.5} strokeLinecap="round" aria-hidden="true">
                                <line x1="18" y1="6" x2="6" y2="18" />
                                <line x1="6" y1="6" x2="18" y2="18" />
                            </svg>
                        </button>
                    )}
                </div>
            )}
        </div>
    );
}
