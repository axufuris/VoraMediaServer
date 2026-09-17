import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { pluginAdminService } from '../../../api/System/pluginAdminService';
import { ADMIN_NAV, Icons, resolveAdminPath, type AdminNavEntry } from './adminNavData';

function score(entry: AdminNavEntry, query: string): number {
    if (!query) return 0;
    const q = query.toLowerCase();
    const label = entry.label.toLowerCase();

    if (label === q) return 1000;
    if (label.startsWith(q)) return 500;
    if (label.includes(q)) return 250;

    // Match against keywords with a smaller weight.
    if (entry.keywords?.some(k => k.toLowerCase().includes(q))) return 100;

    // Match against the section name (so "library" surfaces all library pages).
    if (entry.section.toLowerCase().includes(q)) return 50;

    return 0;
}

interface SearchPaletteProps {
    isOpen: boolean;
    onClose: () => void;
}

export default function SearchPalette({ isOpen, onClose }: SearchPaletteProps) {
    const navigate = useNavigate();
    const { serverId } = useParams<{ serverId?: string }>();
    const [query, setQuery] = useState('');
    const [selectedIndex, setSelectedIndex] = useState(0);
    const [hasAiPlugin, setHasAiPlugin] = useState(false);
    const inputRef = useRef<HTMLInputElement>(null);

    // Same runtime gate as the sidebar: don't surface AI pages in search if no
    // AI plugin is installed. Fetched when the palette opens (and re-fetched
    // when the active server changes) rather than on every keystroke.
    useEffect(() => {
        if (!isOpen) return;
        let cancelled = false;
        pluginAdminService.getPlugins(serverId)
            .then(plugins => {
                if (cancelled) return;
                setHasAiPlugin(plugins.some(p => p.isAiPlugin && p.isEnabled));
            })
            .catch(() => { /* unauthenticated or offline — hide AI entries by default */ });
        return () => { cancelled = true; };
    }, [isOpen, serverId]);

    // Reset state on open; focus the input.
    useEffect(() => {
        if (isOpen) {
            queueMicrotask(() => {
                setQuery('');
                setSelectedIndex(0);
                inputRef.current?.focus();
            });
        }
    }, [isOpen]);

    // Apply runtime visibility (mirrors the sidebar's filtering).
    const visibleEntries = useMemo(() => ADMIN_NAV.filter(e => {
        if (e.requires === 'ai' && !hasAiPlugin) return false;
        return true;
    }), [hasAiPlugin]);

    // Empty query shows everything; a query filters + sorts by score.
    const results = useMemo(() => {
        if (!query.trim()) return visibleEntries;
        return visibleEntries
            .map(e => ({ entry: e, score: score(e, query.trim()) }))
            .filter(r => r.score > 0)
            .sort((a, b) => b.score - a.score)
            .map(r => r.entry);
    }, [visibleEntries, query]);

    useEffect(() => {
        if (selectedIndex >= results.length) {
            queueMicrotask(() => setSelectedIndex(Math.max(0, results.length - 1)));
        }
    }, [results.length, selectedIndex]);

    const navigateToEntry = (entry: AdminNavEntry) => {
        navigate(resolveAdminPath(entry.pathTemplate, serverId));
        onClose();
    };

    const handleKeyDown = (e: React.KeyboardEvent) => {
        if (e.key === 'Escape') {
            e.preventDefault();
            onClose();
        } else if (e.key === 'ArrowDown') {
            e.preventDefault();
            setSelectedIndex(prev => Math.min(results.length - 1, prev + 1));
        } else if (e.key === 'ArrowUp') {
            e.preventDefault();
            setSelectedIndex(prev => Math.max(0, prev - 1));
        } else if (e.key === 'Enter') {
            e.preventDefault();
            const target = results[selectedIndex];
            if (target) navigateToEntry(target);
        }
    };

    if (!isOpen) return null;

    return (
        <div
            className="fixed inset-0 z-[300] flex items-start justify-center pt-[15vh] bg-[var(--vora-bg-overlay)] backdrop-blur-sm p-4"
            onClick={onClose}
        >
            <div
                className="vora-card shadow-[var(--vora-shadow-overlay)] w-full max-w-xl overflow-hidden"
                onClick={e => e.stopPropagation()}
            >
                <div className="relative border-b border-[var(--vora-border-subtle)]">
                    <svg className="w-4 h-4 absolute left-4 top-1/2 -translate-y-1/2 text-[var(--vora-text-muted)] pointer-events-none" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M21 21l-4.35-4.35m0 0A7.5 7.5 0 1010.5 18a7.46 7.46 0 006.15-3.35z" />
                    </svg>
                    <input
                        ref={inputRef}
                        type="text"
                        value={query}
                        onChange={e => { setQuery(e.target.value); setSelectedIndex(0); }}
                        onKeyDown={handleKeyDown}
                        placeholder="Jump to an admin page…"
                        className="w-full bg-transparent border-none pl-11 pr-4 py-4 text-base text-[var(--vora-text-primary)] placeholder:text-[var(--vora-text-muted)] focus:outline-none"
                    />
                </div>

                <div className="max-h-[50vh] overflow-y-auto py-2">
                    {results.length === 0 ? (
                        <div className="px-4 py-8 text-center text-sm text-[var(--vora-text-muted)]">
                            No matching pages.
                        </div>
                    ) : (
                        results.map((entry, index) => {
                            const isSelected = index === selectedIndex;
                            return (
                                <button
                                    key={entry.pathTemplate}
                                    type="button"
                                    onClick={() => navigateToEntry(entry)}
                                    onMouseEnter={() => setSelectedIndex(index)}
                                    className={`w-full text-left px-4 py-2.5 flex items-center gap-3 transition-colors ${isSelected ? 'bg-[var(--vora-accent-soft)] text-[var(--vora-accent-text)]' : 'text-[var(--vora-text-primary)]'}`}
                                >
                                    <svg className="w-4 h-4 shrink-0 opacity-70" fill="none" stroke="currentColor" viewBox="0 0 24 24">{Icons[entry.icon]}</svg>
                                    <span className="text-sm font-semibold truncate flex-1">{entry.label}</span>
                                    <span className={`text-[10px] font-bold uppercase tracking-widest shrink-0 ${isSelected ? 'text-[var(--vora-accent-active)]' : 'text-[var(--vora-text-muted)]'}`}>
                                        {entry.section}
                                    </span>
                                </button>
                            );
                        })
                    )}
                </div>

                <div className="border-t border-[var(--vora-border-subtle)] px-4 py-2 flex items-center justify-between text-[11px] text-[var(--vora-text-muted)]">
                    <div className="flex items-center gap-3">
                        <span><kbd className="font-mono px-1.5 py-0.5 rounded border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)]">↑↓</kbd> navigate</span>
                        <span><kbd className="font-mono px-1.5 py-0.5 rounded border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)]">↵</kbd> open</span>
                        <span><kbd className="font-mono px-1.5 py-0.5 rounded border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)]">esc</kbd> close</span>
                    </div>
                    <span>{results.length} result{results.length === 1 ? '' : 's'}</span>
                </div>
            </div>
        </div>
    );
}
