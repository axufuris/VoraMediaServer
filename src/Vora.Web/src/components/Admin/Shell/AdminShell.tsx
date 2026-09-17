import { useCallback, useEffect, useRef, useState } from 'react';
import { Outlet } from 'react-router-dom';
import SidebarV2 from './SidebarV2';
import TopAppBar from './TopAppBar';
import SearchPalette from './SearchPalette';

export default function AdminShell() {
    const [isPaletteOpen, setIsPaletteOpen] = useState(false);
    const rootRef = useRef<HTMLDivElement>(null);

    const openPalette = useCallback(() => setIsPaletteOpen(true), []);
    const closePalette = useCallback(() => setIsPaletteOpen(false), []);

    // ⌘K (Mac) / Ctrl-K (everywhere else) toggles the search palette.
    // We listen at the window level so the shortcut works from any focused
    // element inside the admin, including form inputs.
    useEffect(() => {
        const handler = (e: KeyboardEvent) => {
            if (e.key === 'k' && (e.metaKey || e.ctrlKey)) {
                e.preventDefault();
                setIsPaletteOpen(open => !open);
            }
        };
        window.addEventListener('keydown', handler);
        return () => window.removeEventListener('keydown', handler);
    }, []);

    // Suppress browser password managers (LastPass, 1Password, Bitwarden, etc.)
    // across the entire admin. Admin pages never contain login credentials, so
    // the autofill popups are always wrong. A single MutationObserver tags every
    // input/textarea/select with the manager-specific ignore attributes.
    useEffect(() => {
        const root = rootRef.current;
        if (!root) return;
        const tag = (el: Element) => {
            if (el instanceof HTMLInputElement || el instanceof HTMLTextAreaElement || el instanceof HTMLSelectElement) {
                if (el instanceof HTMLInputElement && el.type === 'password') return; // leave real password fields alone
                if (!el.hasAttribute('data-lpignore')) el.setAttribute('data-lpignore', 'true');
                if (!el.hasAttribute('data-1p-ignore')) el.setAttribute('data-1p-ignore', 'true');
                if (!el.hasAttribute('data-bwignore')) el.setAttribute('data-bwignore', 'true');
                if (!el.hasAttribute('autocomplete')) el.setAttribute('autocomplete', 'off');
            }
        };
        root.querySelectorAll('input, textarea, select').forEach(tag);
        const observer = new MutationObserver(mutations => {
            for (const m of mutations) {
                m.addedNodes.forEach(node => {
                    if (node instanceof Element) {
                        tag(node);
                        node.querySelectorAll?.('input, textarea, select').forEach(tag);
                    }
                });
            }
        });
        observer.observe(root, { childList: true, subtree: true });
        return () => observer.disconnect();
    }, []);

    return (
        <div
            ref={rootRef}
            data-vora-admin
            className="flex flex-col h-screen w-full overflow-hidden bg-[var(--vora-bg-canvas)] text-[var(--vora-text-primary)]"
        >
            <TopAppBar onOpenSearch={openPalette} />
            <div className="flex flex-1 overflow-hidden">
                <SidebarV2 />
                <main className="flex-1 overflow-y-auto">
                    <Outlet />
                </main>
            </div>
            <SearchPalette isOpen={isPaletteOpen} onClose={closePalette} />
        </div>
    );
}
