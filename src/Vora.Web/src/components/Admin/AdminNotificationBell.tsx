import { useEffect, useState, useRef, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { adminNotificationService, type AdminNotificationVM, type AdminAlertEvent } from '../../api/System/adminNotificationService';
import { useSignalREvent } from '../../hooks/useSignalREvent';

function severityClasses(severity: string) {
    switch (severity) {
        case 'Error': return { dot: 'bg-[var(--vora-danger-500)]', text: 'text-[var(--vora-danger-text)]', bar: 'border-[var(--vora-danger-500)]' };
        case 'Warning': return { dot: 'bg-[var(--vora-warning-500)]', text: 'text-[var(--vora-warning-text)]', bar: 'border-[var(--vora-warning-500)]' };
        default: return { dot: 'bg-[var(--vora-info-500)]', text: 'text-[var(--vora-info-text)]', bar: 'border-[var(--vora-info-500)]' };
    }
}

function formatRelative(iso: string): string {
    const date = new Date(iso);
    const diffMs = Date.now() - date.getTime();
    const mins = Math.floor(diffMs / 60000);
    if (mins < 1) return 'just now';
    if (mins < 60) return `${mins}m ago`;
    const hours = Math.floor(mins / 60);
    if (hours < 24) return `${hours}h ago`;
    const days = Math.floor(hours / 24);
    if (days < 7) return `${days}d ago`;
    return date.toLocaleDateString();
}

export default function AdminNotificationBell() {
    const { serverId } = useParams<{ serverId?: string }>();
    const [isOpen, setIsOpen] = useState(false);
    const [unreadCount, setUnreadCount] = useState(0);
    const [items, setItems] = useState<AdminNotificationVM[]>([]);
    const [toasts, setToasts] = useState<AdminAlertEvent[]>([]);
    const wrapperRef = useRef<HTMLDivElement>(null);

    const refreshCount = useCallback(async () => {
        try {
            const count = await adminNotificationService.getUnreadCount(serverId);
            setUnreadCount(count);
        } catch {
            /* ignore */
        }
    }, [serverId]);

    const refreshList = useCallback(async () => {
        try {
            const list = await adminNotificationService.getRecent(50, false, serverId);
            setItems(list);
        } catch {
            /* ignore */
        }
    }, [serverId]);

    useEffect(() => { refreshCount(); }, [refreshCount]);

    useSignalREvent<AdminAlertEvent>('AdminAlert', useCallback((evt: AdminAlertEvent) => {
        if (!evt) return;
        setToasts(prev => [...prev, evt]);
        setUnreadCount(c => c + 1);
        if (isOpen) refreshList();
        setTimeout(() => {
            setToasts(prev => prev.slice(1));
        }, 8000);
    }, [isOpen, refreshList]));

    useSignalREvent('AdminAlertUnreadChanged', useCallback(() => {
        refreshCount();
        if (isOpen) refreshList();
    }, [isOpen, refreshCount, refreshList]));

    useEffect(() => {
        if (isOpen) refreshList();
    }, [isOpen, refreshList]);

    useEffect(() => {
        if (!isOpen) return;
        const handler = (e: MouseEvent) => {
            if (wrapperRef.current && !wrapperRef.current.contains(e.target as Node)) {
                setIsOpen(false);
            }
        };
        document.addEventListener('mousedown', handler);
        return () => document.removeEventListener('mousedown', handler);
    }, [isOpen]);

    const dismissToast = (idx: number) => {
        setToasts(prev => prev.filter((_, i) => i !== idx));
    };

    const handleMarkRead = async (id: string) => {
        try {
            await adminNotificationService.markRead(id, serverId);
            setItems(prev => prev.map(n => n.id === id ? { ...n, isRead: true } : n));
        } catch {
            /* ignore */
        }
    };

    const handleMarkAllRead = async () => {
        try {
            await adminNotificationService.markAllRead(serverId);
            setItems(prev => prev.map(n => ({ ...n, isRead: true })));
            setUnreadCount(0);
        } catch {
            /* ignore */
        }
    };

    return (
        <>
            <div ref={wrapperRef} className="relative">
                <button
                    type="button"
                    onClick={() => setIsOpen(o => !o)}
                    className="relative p-2 rounded-[var(--vora-radius-md)] text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)] hover:bg-[var(--vora-bg-sunken)] transition-colors cursor-pointer"
                    title="Admin notifications"
                >
                    <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9" /></svg>
                    {unreadCount > 0 && (
                        <span className="absolute top-1 right-1 min-w-[16px] h-4 px-1 rounded-full bg-[var(--vora-accent-500)] text-[var(--vora-accent-contrast)] text-[10px] font-bold flex items-center justify-center">
                            {unreadCount > 99 ? '99+' : unreadCount}
                        </span>
                    )}
                </button>

                {isOpen && (
                    <div className="absolute right-0 top-full mt-1 w-[380px] max-w-[90vw] vora-card shadow-[var(--vora-shadow-lg)] z-50 overflow-hidden">
                        <div className="flex items-center justify-between px-4 py-3 border-b border-[var(--vora-border-subtle)]">
                            <h3 className="text-sm font-semibold text-[var(--vora-text-primary)]">Notifications</h3>
                            {items.some(n => !n.isRead) && (
                                <button onClick={handleMarkAllRead} className="text-xs font-medium text-[var(--vora-accent-text)] hover:text-[var(--vora-accent-active)] cursor-pointer">Mark all read</button>
                            )}
                        </div>
                        <div className="max-h-[60vh] overflow-y-auto">
                            {items.length === 0 ? (
                                <div className="p-8 text-center text-[var(--vora-text-muted)] text-sm">No notifications yet.</div>
                            ) : (
                                items.map(n => {
                                    const cls = severityClasses(n.severity);
                                    return (
                                        <div
                                            key={n.id}
                                            onClick={() => !n.isRead && handleMarkRead(n.id)}
                                            className={`flex items-start gap-3 px-4 py-3 border-b border-[var(--vora-border-subtle)] last:border-b-0 transition-colors cursor-pointer ${n.isRead ? 'opacity-70 hover:bg-[var(--vora-bg-sunken)]' : 'bg-[var(--vora-bg-sunken)]/40 hover:bg-[var(--vora-bg-sunken)]'}`}
                                        >
                                            <span className={`w-2 h-2 rounded-full mt-1.5 shrink-0 ${cls.dot}`}></span>
                                            <div className="flex-1 min-w-0">
                                                <div className={`text-sm font-semibold truncate ${cls.text}`}>{n.title}</div>
                                                <div className="text-xs text-[var(--vora-text-secondary)] mt-0.5 break-words">{n.message}</div>
                                                <div className="text-[10px] text-[var(--vora-text-muted)] mt-1">{formatRelative(n.createdAt)}</div>
                                            </div>
                                        </div>
                                    );
                                })
                            )}
                        </div>
                    </div>
                )}
            </div>

            <div className="fixed bottom-6 right-6 z-[9999] flex flex-col gap-2 pointer-events-none">
                {toasts.map((t, i) => {
                    const cls = severityClasses(t.severity);
                    return (
                        <div
                            key={`${t.timestamp}-${i}`}
                            className={`pointer-events-auto vora-card border-l-4 ${cls.bar} shadow-[var(--vora-shadow-lg)] p-4 max-w-sm flex items-start gap-3`}
                        >
                            <span className={`w-2 h-2 rounded-full mt-1.5 shrink-0 ${cls.dot}`}></span>
                            <div className="flex-1 min-w-0">
                                <div className={`text-sm font-semibold ${cls.text}`}>{t.title}</div>
                                <div className="text-xs text-[var(--vora-text-secondary)] mt-0.5">{t.message}</div>
                            </div>
                            <button onClick={() => dismissToast(i)} className="text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)] shrink-0 cursor-pointer" title="Dismiss">
                                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" /></svg>
                            </button>
                        </div>
                    );
                })}
            </div>
        </>
    );
}
