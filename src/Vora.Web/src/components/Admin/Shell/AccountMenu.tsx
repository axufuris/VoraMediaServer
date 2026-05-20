import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { serverVault } from '../../../utils/serverVault';

export default function AccountMenu() {
    const navigate = useNavigate();
    const [open, setOpen] = useState(false);
    const wrapperRef = useRef<HTMLDivElement>(null);

    const active = serverVault.getActiveServer();
    const initial = (active?.name ?? 'V').charAt(0).toUpperCase();

    useEffect(() => {
        if (!open) return;
        const handler = (e: MouseEvent) => {
            if (wrapperRef.current && !wrapperRef.current.contains(e.target as Node)) {
                setOpen(false);
            }
        };
        document.addEventListener('mousedown', handler);
        return () => document.removeEventListener('mousedown', handler);
    }, [open]);

    const signOut = () => {
        serverVault.clearVault();
        localStorage.removeItem('profile_token');
        navigate('/');
        window.location.reload();
    };

    return (
        <div ref={wrapperRef} className="relative">
            <button
                type="button"
                onClick={() => setOpen(o => !o)}
                className="w-8 h-8 rounded-full bg-[var(--vora-accent-500)] text-[var(--vora-accent-contrast)] font-semibold text-sm flex items-center justify-center cursor-pointer hover:ring-2 hover:ring-[var(--vora-accent-soft)] transition-all"
                title="Account"
            >
                {initial}
            </button>

            {open && (
                <div className="absolute right-0 top-full mt-1 w-56 vora-card shadow-[var(--vora-shadow-lg)] z-50 overflow-hidden p-1">
                    {active && (
                        <div className="px-3 py-2.5 border-b border-[var(--vora-border-subtle)] mb-1">
                            <div className="text-sm font-semibold text-[var(--vora-text-primary)] truncate">{active.name}</div>
                            <div className="text-[11px] text-[var(--vora-text-muted)] truncate">
                                {active.isAdmin ? 'Server admin' : 'Profile'}
                            </div>
                        </div>
                    )}
                    <button
                        type="button"
                        onClick={() => { setOpen(false); navigate(active ? `/server/${active.id}` : '/'); }}
                        className="w-full text-left px-3 py-2 rounded-[var(--vora-radius-md)] text-sm text-[var(--vora-text-primary)] hover:bg-[var(--vora-bg-sunken)] cursor-pointer transition-colors"
                    >
                        Back to client
                    </button>
                    <button
                        type="button"
                        onClick={() => { setOpen(false); signOut(); }}
                        className="w-full text-left px-3 py-2 rounded-[var(--vora-radius-md)] text-sm text-[var(--vora-danger-text)] hover:bg-[var(--vora-danger-soft)] cursor-pointer transition-colors"
                    >
                        Sign out
                    </button>
                </div>
            )}
        </div>
    );
}
