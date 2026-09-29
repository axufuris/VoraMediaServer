import { useEffect, useRef, useState } from 'react';
import { NavLink } from 'react-router-dom';
import type { NavItem } from '../MainLayout';
import { renderNavIcon } from './navIcons';
import { dropIndex } from '../../utils/navOrder';

interface MainLayoutSidebarProps {
    navItems: NavItem[];
    pinnedItems: NavItem[];
    unpinnedItems: NavItem[];
    isEditingNav: boolean;
    showUnpinned: boolean;
    onToggleEditNav: (editing: boolean) => void;
    onToggleShowUnpinned: () => void;
    onMoveItem: (from: number, to: number) => void;
    onTogglePin: (id: string, itemServerId?: string) => void;
}

const navItemStyle = (isActive: boolean): React.CSSProperties => ({
    background: isActive ? 'var(--vora-accent-soft)' : 'transparent',
    color: isActive ? 'var(--vora-accent-text)' : 'var(--vora-text-muted)',
});

export default function MainLayoutSidebar({
    navItems,
    pinnedItems,
    unpinnedItems,
    isEditingNav,
    showUnpinned,
    onToggleEditNav,
    onToggleShowUnpinned,
    onMoveItem,
    onTogglePin,
}: MainLayoutSidebarProps) {
    const [dragIndex, setDragIndex] = useState<number | null>(null);
    const [drop, setDrop] = useState<{ over: number; after: boolean } | null>(null);
    const refocusKey = useRef<string | null>(null);
    const handles = useRef(new Map<string, HTMLButtonElement>());

    const keyOf = (item: NavItem) => `${item.serverId || 'local'}-${item.id}`;

    useEffect(() => {
        if (!refocusKey.current) return;
        handles.current.get(refocusKey.current)?.focus();
        refocusKey.current = null;
    }, [navItems]);

    const endDrag = () => {
        setDragIndex(null);
        setDrop(null);
    };

    const moveWithKeyboard = (e: React.KeyboardEvent, item: NavItem, index: number) => {
        const to = e.key === 'ArrowUp' ? index - 1 : e.key === 'ArrowDown' ? index + 1 : null;
        if (to === null || to < 0 || to >= navItems.length) return;
        e.preventDefault();
        refocusKey.current = keyOf(item);
        onMoveItem(index, to);
    };

    return (
        <div
            className="z-20 flex w-64 flex-col transition-all duration-300"
            style={{ background: 'var(--vora-bg-sunken)', borderRight: '1px solid var(--vora-border-subtle)' }}
        >
            <div className="flex items-center justify-between p-6">
                <div className="flex items-center">
                    <svg width="28" height="28" viewBox="0 0 64 64" aria-hidden="true">
                        <defs>
                            <linearGradient id="sidebar-vora-v" x1="0.1" y1="0" x2="0.55" y2="1">
                                <stop offset="0" stopColor="var(--vora-accent-text)" />
                                <stop offset="1" stopColor="var(--vora-accent-500)" />
                            </linearGradient>
                        </defs>
                        <path d="M6 8 L18 8 L32 40 L46 8 L58 8 L32 60 Z" fill="url(#sidebar-vora-v)" />
                    </svg>
                    <h1
                        className="m-0 text-2xl font-semibold tracking-wide"
                        style={{ color: 'var(--vora-text-primary)', letterSpacing: '-0.02em' }}
                        aria-label="Vora"
                    >
                        ora
                    </h1>
                </div>
            </div>

            {!isEditingNav ? (
                <>
                    <div className="mb-4 space-y-1 px-4">
                        <NavLink
                            to="/"
                            end
                            className={({ isActive }) => `flex items-center gap-3 rounded-md px-3 py-2 text-sm font-semibold transition-colors ${isActive ? '' : 'hover:bg-white/5 hover:text-[var(--vora-text-primary)]'}`}
                            style={({ isActive }) => navItemStyle(isActive)}
                        >
                            <svg className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth={1.75} viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" d="M3 12 12 3l9 9" />
                                <path strokeLinecap="round" strokeLinejoin="round" d="M5 10v10h4v-6h6v6h4V10" />
                            </svg>
                            Home
                        </NavLink>
                    </div>

                    <div className="flex-1 overflow-y-auto px-4">
                        <div className="space-y-1">
                            {[...pinnedItems].sort((a, b) => a.order - b.order).map(item => {
                                const icon = renderNavIcon(item.type === 'library' ? item.mediaType : item.id, 'w-5 h-5');
                                return (
                                    <NavLink
                                        key={`${item.serverId || 'local'}-${item.id}`}
                                        to={item.path}
                                        className={({ isActive }) => `flex items-center gap-3 rounded-md px-3 py-2.5 text-sm font-medium transition-colors ${isActive ? '' : 'hover:bg-white/5 hover:text-[var(--vora-text-primary)]'}`}
                                        style={({ isActive }) => navItemStyle(isActive)}
                                    >
                                        {icon}
                                        <span className="min-w-0 flex-1">
                                            <span className="block">{item.title}</span>
                                            {item.serverName && <span className="block text-[10px] font-normal" style={{ color: 'var(--vora-text-disabled)' }}>{item.serverName}</span>}
                                        </span>
                                    </NavLink>
                                );
                            })}

                            {unpinnedItems.length > 0 && (
                                <div className="mt-4">
                                    <button
                                        type="button"
                                        onClick={onToggleShowUnpinned}
                                        className="flex w-full cursor-pointer items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors hover:bg-white/5 hover:text-[var(--vora-text-primary)]"
                                        style={{ color: 'var(--vora-text-muted)' }}
                                    >
                                        <svg className={`h-4 w-4 transition-transform ${showUnpinned ? 'rotate-180' : ''}`} fill="none" stroke="currentColor" strokeWidth={2} viewBox="0 0 24 24">
                                            <polyline points="6 9 12 15 18 9" />
                                        </svg>
                                        More
                                    </button>

                                    {showUnpinned && (
                                        <div
                                            className="ml-4 mt-1 space-y-1 pl-4"
                                            style={{ borderLeft: '2px solid var(--vora-border-subtle)' }}
                                        >
                                            {unpinnedItems.map(item => {
                                                const icon = renderNavIcon(item.type === 'library' ? item.mediaType : item.id, 'w-5 h-5');
                                                return (
                                                    <NavLink
                                                        key={`${item.serverId || 'local'}-${item.id}`}
                                                        to={item.path}
                                                        className={({ isActive }) => `flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors ${isActive ? '' : 'hover:text-[var(--vora-text-secondary)]'}`}
                                                        style={({ isActive }) => ({
                                                            color: isActive ? 'var(--vora-accent-text)' : 'var(--vora-text-disabled)',
                                                        })}
                                                    >
                                                        {icon}
                                                        <span>{item.title}</span>
                                                    </NavLink>
                                                );
                                            })}
                                        </div>
                                    )}
                                </div>
                            )}
                        </div>
                    </div>

                    <div className="p-4" style={{ borderTop: '1px solid var(--vora-border-subtle)' }}>
                        <button
                            type="button"
                            onClick={() => onToggleEditNav(true)}
                            className="flex w-full cursor-pointer items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors hover:bg-white/5 hover:text-[var(--vora-text-primary)]"
                            style={{ color: 'var(--vora-text-muted)' }}
                        >
                            <svg className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth={1.75} viewBox="0 0 24 24">
                                <line x1="4" y1="21" x2="4" y2="14" />
                                <line x1="4" y1="10" x2="4" y2="3" />
                                <line x1="12" y1="21" x2="12" y2="12" />
                                <line x1="12" y1="8" x2="12" y2="3" />
                                <line x1="20" y1="21" x2="20" y2="16" />
                                <line x1="20" y1="12" x2="20" y2="3" />
                                <line x1="1" y1="14" x2="7" y2="14" />
                                <line x1="9" y1="8" x2="15" y2="8" />
                                <line x1="17" y1="16" x2="23" y2="16" />
                            </svg>
                            Edit Navigation
                        </button>
                    </div>
                </>
            ) : (
                <div className="flex flex-1 flex-col overflow-hidden" style={{ background: 'color-mix(in srgb, var(--vora-bg-surface) 60%, transparent)' }}>
                    <div
                        className="px-6 py-2 text-xs font-semibold uppercase tracking-widest"
                        style={{
                            background: 'var(--vora-bg-raised)',
                            color: 'var(--vora-text-muted)',
                            borderBottom: '1px solid var(--vora-border-subtle)',
                        }}
                    >
                        Manage sidebar
                    </div>
                    <p className="px-4 pt-3 text-xs" style={{ color: 'var(--vora-text-disabled)' }}>Drag to reorder. Pin to show in the sidebar.</p>
                    <div className="flex-1 space-y-1 overflow-y-auto p-2" onDragLeave={e => { if (!e.currentTarget.contains(e.relatedTarget as Node | null)) setDrop(null); }}>
                        {navItems.map((item, index) => {
                            const icon = renderNavIcon(item.type === 'library' ? item.mediaType : item.id, 'w-4 h-4');
                            const key = keyOf(item);
                            const showLine = drop !== null && dragIndex !== null && drop.over === index && dropIndex(dragIndex, index, drop.after) !== dragIndex;
                            return (
                                <div
                                    key={key}
                                    data-testid={`nav-row-${item.id}`}
                                    draggable
                                    onDragStart={e => {
                                        e.dataTransfer.effectAllowed = 'move';
                                        e.dataTransfer.setData('text/plain', key);
                                        setDragIndex(index);
                                    }}
                                    onDragOver={e => {
                                        if (dragIndex === null) return;
                                        e.preventDefault();
                                        e.dataTransfer.dropEffect = 'move';
                                        const box = e.currentTarget.getBoundingClientRect();
                                        const after = e.clientY > box.top + box.height / 2;
                                        if (drop?.over !== index || drop.after !== after) setDrop({ over: index, after });
                                    }}
                                    onDrop={e => {
                                        e.preventDefault();
                                        if (dragIndex !== null && drop !== null) {
                                            const to = dropIndex(dragIndex, drop.over, drop.after);
                                            if (to !== dragIndex) onMoveItem(dragIndex, to);
                                        }
                                        endDrag();
                                    }}
                                    onDragEnd={endDrag}
                                    className="group relative flex cursor-grab items-center justify-between rounded-md p-2 transition-colors active:cursor-grabbing"
                                    style={{
                                        background: 'var(--vora-bg-sunken)',
                                        border: '1px solid var(--vora-border-subtle)',
                                        opacity: dragIndex === index ? 0.4 : 1,
                                    }}
                                >
                                    {showLine && (
                                        <span
                                            aria-hidden="true"
                                            className="pointer-events-none absolute left-1 right-1 h-0.5 rounded-full"
                                            style={{ background: 'var(--vora-accent-500)', [drop.after ? 'bottom' : 'top']: -3 }}
                                        />
                                    )}
                                    <div className="flex items-center gap-2">
                                        <button
                                            type="button"
                                            ref={node => { if (node) handles.current.set(key, node); else handles.current.delete(key); }}
                                            onKeyDown={e => moveWithKeyboard(e, item, index)}
                                            aria-label={`Move ${item.title}. Use the up and down arrow keys.`}
                                            title="Drag to reorder"
                                            className="vora-icon-button cursor-grab rounded p-0.5"
                                            style={{ color: 'var(--vora-text-disabled)' }}
                                        >
                                            <svg className="h-4 w-4" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                                                <circle cx="9" cy="6" r="1.6" /><circle cx="15" cy="6" r="1.6" />
                                                <circle cx="9" cy="12" r="1.6" /><circle cx="15" cy="12" r="1.6" />
                                                <circle cx="9" cy="18" r="1.6" /><circle cx="15" cy="18" r="1.6" />
                                            </svg>
                                        </button>
                                        <div className="flex w-4 shrink-0 justify-center" style={{ color: item.isPinned ? 'var(--vora-text-secondary)' : 'var(--vora-text-disabled)' }}>
                                            {icon}
                                        </div>
                                        <div className="flex w-28 flex-col">
                                            <span className="truncate text-sm font-medium" title={item.title} style={{ color: item.isPinned ? 'var(--vora-text-primary)' : 'var(--vora-text-muted)' }}>
                                                {item.title}
                                            </span>
                                            {item.serverName && <span className="truncate text-[10px]" title={item.serverName} style={{ color: 'var(--vora-text-disabled)' }}>{item.serverName}</span>}
                                        </div>
                                    </div>
                                    <button
                                        type="button"
                                        onClick={() => onTogglePin(item.id, item.serverId)}
                                        className="cursor-pointer rounded p-1.5 transition-colors hover:bg-white/5"
                                        title={item.isPinned ? 'Unpin' : 'Pin'}
                                        style={{
                                            color: item.isPinned ? 'var(--vora-accent-text)' : 'var(--vora-text-disabled)',
                                            background: item.isPinned ? 'var(--vora-accent-soft)' : 'transparent',
                                        }}
                                    >
                                        <svg className="h-4 w-4" fill="currentColor" viewBox="0 0 20 20"><path d="M5 4a2 2 0 012-2h6a2 2 0 012 2v14l-5-2.5L5 18V4z" /></svg>
                                    </button>
                                </div>
                            );
                        })}
                    </div>
                    <div className="p-4" style={{ borderTop: '1px solid var(--vora-border-subtle)', background: 'var(--vora-bg-sunken)' }}>
                        <button
                            type="button"
                            onClick={() => onToggleEditNav(false)}
                            className="vora-button-primary w-full cursor-pointer"
                        >
                            Done
                        </button>
                    </div>
                </div>
            )}
        </div>
    );
}
