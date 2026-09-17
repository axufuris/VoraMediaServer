import { useEffect, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { serverVault, type VoraServer } from '../../../utils/serverVault';

export default function ServerSwitcher() {
    const { serverId } = useParams<{ serverId?: string }>();
    const navigate = useNavigate();
    const [open, setOpen] = useState(false);
    const wrapperRef = useRef<HTMLDivElement>(null);

    const servers = serverVault.getServers();
    const activeId = serverId ?? serverVault.getActiveServerId();
    const active = servers.find(s => s.id === activeId);

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

    const switchTo = (s: VoraServer) => {
        serverVault.setActiveServerId(s.id);
        setOpen(false);
        navigate(serverId ? `/server/${s.id}/admin` : '/admin');
        // Reload so all server-scoped state (active token, hub connection, in-memory caches) resets.
        window.location.reload();
    };

    const displayName = active?.name ?? 'No server selected';

    return (
        <div ref={wrapperRef} className="relative">
            <button
                type="button"
                onClick={() => setOpen(o => !o)}
                className="flex items-center gap-2 px-3 py-1.5 rounded-[var(--vora-radius-md)] hover:bg-[var(--vora-bg-sunken)] transition-colors cursor-pointer text-sm font-semibold text-[var(--vora-text-primary)]"
                title="Switch server"
            >
                <span className="w-6 h-6 rounded-md bg-[var(--vora-accent-soft)] text-[var(--vora-accent-text)] flex items-center justify-center text-xs font-bold uppercase">
                    {displayName.charAt(0)}
                </span>
                <span className="truncate max-w-[160px]">{displayName}</span>
                <svg className="w-3.5 h-3.5 text-[var(--vora-text-muted)]" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M8 9l4-4 4 4m0 6l-4 4-4-4" /></svg>
            </button>

            {open && (
                <div className="absolute left-0 top-full mt-1 w-64 vora-card shadow-[var(--vora-shadow-lg)] z-50 overflow-hidden p-1">
                    <div className="px-3 py-2 text-[10px] font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">Your servers</div>
                    {servers.length === 0 && (
                        <div className="px-3 py-4 text-sm text-[var(--vora-text-muted)]">No servers added.</div>
                    )}
                    {servers.map(s => {
                        const isActive = s.id === activeId;
                        return (
                            <button
                                key={s.id}
                                type="button"
                                onClick={() => switchTo(s)}
                                className={`w-full text-left flex items-center gap-3 px-3 py-2 rounded-[var(--vora-radius-md)] transition-colors cursor-pointer ${isActive ? 'bg-[var(--vora-accent-soft)] text-[var(--vora-accent-text)]' : 'hover:bg-[var(--vora-bg-sunken)] text-[var(--vora-text-primary)]'}`}
                            >
                                <span className="w-7 h-7 rounded-md bg-[var(--vora-bg-sunken)] flex items-center justify-center text-xs font-bold uppercase shrink-0">
                                    {s.name.charAt(0)}
                                </span>
                                <div className="flex-1 min-w-0">
                                    <div className="text-sm font-semibold truncate">{s.name}</div>
                                    <div className="text-[11px] text-[var(--vora-text-muted)] truncate">{s.url}</div>
                                </div>
                                {isActive && (
                                    <svg className="w-4 h-4 shrink-0 text-[var(--vora-accent-text)]" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M5 13l4 4L19 7" /></svg>
                                )}
                            </button>
                        );
                    })}
                </div>
            )}
        </div>
    );
}
