import { useCallback, useEffect, useState } from 'react';
import { Outlet } from 'react-router-dom';
import SidebarV2 from './SidebarV2';
import TopAppBar from './TopAppBar';
import SearchPalette from './SearchPalette';

export default function AdminShell() {
    const [isPaletteOpen, setIsPaletteOpen] = useState(false);

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

    return (
        <div
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
