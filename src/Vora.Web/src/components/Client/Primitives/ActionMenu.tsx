import { useEffect, useLayoutEffect, useRef, useState, type KeyboardEvent, type MouseEvent } from 'react';
import { useIsPhone } from '../../../hooks/useIsPhone';
import type { MenuEntry, MenuPoint } from './menuEntries';

const EDGE_MARGIN = 8;

interface ActionMenuListProps {
    entries: MenuEntry[];
    // Runs before the entry's own action, so the menu is gone by the time a
    // dialog or a confirmation opens over it.
    onSelected: () => void;
    // Taller rows for a menu that is used with a finger.
    roomy?: boolean;
}

type SubmenuEntry = Extract<MenuEntry, { kind: 'submenu' }>;

function Chevron({ back }: { back?: boolean }) {
    return (
        <svg aria-hidden="true" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.25" strokeLinecap="round" strokeLinejoin="round" className="shrink-0">
            <path d={back ? 'M15 18l-6-6 6-6' : 'M9 18l6-6-6-6'} />
        </svg>
    );
}

// The rows of a menu, without the box around them. The title page's "more"
// button and the menu on a poster both draw their entries with this. A submenu
// row swaps the rows for its own, under a Back row, and Back (or Left) swaps
// them back; the menu stays open meanwhile.
export function ActionMenuList({ entries, onSelected, roomy }: ActionMenuListProps) {
    const [openKey, setOpenKey] = useState<string | null>(null);
    const listRef = useRef<HTMLDivElement>(null);
    const switched = useRef(false);
    const opened = entries.find((entry): entry is SubmenuEntry => entry.kind === 'submenu' && entry.key === openKey);
    const rows = opened ? opened.entries : entries;
    const rowPadding = roomy ? 'py-3.5' : 'py-2.5';

    useEffect(() => {
        if (!switched.current) return;
        listRef.current?.querySelector<HTMLButtonElement>('button[role="menuitem"], button[role="menuitemradio"]')?.focus();
    }, [openKey]);

    const show = (key: string | null) => {
        switched.current = true;
        setOpenKey(key);
    };

    const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
        if (event.key === 'ArrowLeft' && opened) {
            event.preventDefault();
            show(null);
            return;
        }
        const row = (event.target as HTMLElement).closest<HTMLElement>('[data-submenu]');
        if (event.key === 'ArrowRight' && row?.dataset.submenu) {
            event.preventDefault();
            show(row.dataset.submenu);
        }
    };

    return (
        <div ref={listRef} role="none" onKeyDown={handleKeyDown}>
            {opened && (
                <>
                    <button
                        type="button"
                        role="menuitem"
                        onClick={() => show(null)}
                        className={`vora-row-interactive flex w-full cursor-pointer items-center gap-2 px-4 text-left text-sm ${rowPadding}`}
                        style={{ color: 'var(--vora-text-secondary)' }}
                    >
                        <Chevron back />
                        Back
                    </button>
                    <div role="presentation" className="px-4 pb-1 pt-3 text-[11px] font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>
                        {opened.label.replace(/…$/, '')}
                    </div>
                </>
            )}
            {rows.map(entry => entry.kind === 'separator' ? (
                <div key={entry.key} role="separator" className="border-t" style={{ borderColor: 'var(--vora-border-subtle)' }} />
            ) : entry.kind === 'heading' ? (
                <div key={entry.key} role="presentation" className="px-4 pb-1 pt-3 text-[11px] font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>
                    {entry.label}
                </div>
            ) : entry.kind === 'submenu' ? (
                <button
                    key={entry.key}
                    type="button"
                    role="menuitem"
                    aria-haspopup="menu"
                    data-submenu={entry.key}
                    onClick={() => show(entry.key)}
                    className={`vora-row-interactive flex w-full cursor-pointer items-center justify-between gap-2 px-4 text-left text-sm ${rowPadding}`}
                    style={{ color: 'var(--vora-text-primary)' }}
                >
                    {entry.label}
                    <span style={{ color: 'var(--vora-text-muted)' }}><Chevron /></span>
                </button>
            ) : entry.kind === 'choice' ? (
                <button
                    key={entry.key}
                    type="button"
                    role="menuitemradio"
                    aria-checked={entry.selected}
                    onClick={() => { onSelected(); entry.onSelect(); }}
                    className={`vora-row-interactive flex w-full cursor-pointer items-start gap-2.5 px-4 text-left text-sm ${rowPadding}`}
                    style={{ color: 'var(--vora-text-primary)' }}
                >
                    <svg aria-hidden="true" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round"
                        className="mt-0.5 shrink-0" style={{ color: 'var(--vora-accent-text)', visibility: entry.selected ? 'visible' : 'hidden' }}>
                        <path d="M20 6 9 17l-5-5" />
                    </svg>
                    <span className="min-w-0">
                        <span className={`block ${entry.selected ? 'font-semibold' : ''}`}>{entry.label}</span>
                        {entry.detail && <span className="mt-0.5 block text-xs" style={{ color: 'var(--vora-text-muted)' }}>{entry.detail}</span>}
                    </span>
                </button>
            ) : (
                <button
                    key={entry.key}
                    type="button"
                    role="menuitem"
                    onClick={() => { onSelected(); entry.onSelect(); }}
                    className={`vora-row-interactive block w-full cursor-pointer px-4 text-left text-sm ${rowPadding} ${entry.tone === 'danger' ? 'font-medium' : ''}`}
                    style={{ color: entry.tone === 'danger' ? 'var(--vora-danger-text)' : 'var(--vora-text-primary)' }}
                >
                    {entry.label}
                </button>
            ))}
        </div>
    );
}

interface ActionMenuProps {
    at: MenuPoint;
    title?: string;
    entries: MenuEntry[];
    // Shown in place of the entries while they are being worked out, or when
    // they could not be.
    note?: string;
    onClose: () => void;
}

const menuButtons = (menu: HTMLElement) => Array.from(menu.querySelectorAll<HTMLButtonElement>('button[role="menuitem"], button[role="menuitemradio"]'));

// A menu opened at a point: beside the pointer on a desktop, and as a sheet
// rising from the bottom edge on a phone, where a small box under a thumb is
// hard to read and harder to hit.
export default function ActionMenu({ at, title, entries, note, onClose }: ActionMenuProps) {
    const isPhone = useIsPhone();
    const menuRef = useRef<HTMLDivElement>(null);
    // Lifting the finger that opened the menu with a long press still produces a
    // click, and by then the menu or its backdrop is what lies under the finger.
    // So a click only counts once a press has started on the menu itself. A click
    // from the keyboard has no press and reports a detail of 0.
    const pressedHere = useRef(false);
    const press = () => { pressedHere.current = true; };
    const isStray = (event: MouseEvent) => !pressedHere.current && event.detail !== 0;

    useLayoutEffect(() => {
        const menu = menuRef.current;
        if (!menu) return;

        if (!isPhone) {
            const { width, height } = menu.getBoundingClientRect();
            const left = Math.max(EDGE_MARGIN, Math.min(at.x, window.innerWidth - width - EDGE_MARGIN));
            const top = Math.max(EDGE_MARGIN, Math.min(at.y, window.innerHeight - height - EDGE_MARGIN));
            menu.style.left = `${left}px`;
            menu.style.top = `${top}px`;
        }
        menu.style.visibility = 'visible';
        if (!menu.contains(document.activeElement)) menu.focus();
    }, [at, isPhone, entries.length, note]);

    useEffect(() => {
        window.addEventListener('resize', onClose);
        return () => window.removeEventListener('resize', onClose);
    }, [onClose]);

    const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
        if (event.key === 'Escape') {
            event.preventDefault();
            onClose();
            return;
        }

        const buttons = menuRef.current ? menuButtons(menuRef.current) : [];
        if (buttons.length === 0) return;
        const current = buttons.indexOf(document.activeElement as HTMLButtonElement);
        const target = event.key === 'ArrowDown' ? buttons[(current + 1) % buttons.length]
            : event.key === 'ArrowUp' ? buttons[(current - 1 + buttons.length) % buttons.length]
            : event.key === 'Home' ? buttons[0]
            : event.key === 'End' ? buttons[buttons.length - 1]
            : null;
        if (!target) return;
        event.preventDefault();
        target.focus();
    };

    return (
        <>
            <div
                data-testid="action-menu-backdrop"
                className="fixed inset-0 z-[150]"
                style={isPhone ? { background: 'color-mix(in srgb, var(--vora-bg-canvas) 60%, transparent)' } : undefined}
                onPointerDown={press}
                onClick={event => { if (!isStray(event)) onClose(); }}
                onContextMenu={event => { event.preventDefault(); onClose(); }}
            />
            <div
                ref={menuRef}
                role="menu"
                aria-label={title ?? 'Actions'}
                tabIndex={-1}
                onKeyDown={handleKeyDown}
                onPointerDown={press}
                onClickCapture={event => {
                    if (!isStray(event)) return;
                    event.preventDefault();
                    event.stopPropagation();
                }}
                className={isPhone
                    ? 'fixed inset-x-0 bottom-0 z-[151] max-h-[75dvh] overflow-y-auto rounded-t-2xl pb-[env(safe-area-inset-bottom)] outline-none'
                    : 'fixed z-[151] max-h-[calc(100dvh-1rem)] w-56 overflow-y-auto rounded-xl outline-none'}
                style={{
                    visibility: 'hidden',
                    background: 'var(--vora-bg-raised)',
                    border: '1px solid var(--vora-border-strong)',
                    boxShadow: 'var(--vora-shadow-lg)',
                }}
            >
                {title && (
                    <div
                        className={`truncate border-b px-4 text-xs font-semibold ${isPhone ? 'py-3.5' : 'py-2.5'}`}
                        style={{ color: 'var(--vora-text-muted)', borderColor: 'var(--vora-border-subtle)' }}
                    >
                        {title}
                    </div>
                )}
                {note
                    ? <div className="px-4 py-3 text-sm" style={{ color: 'var(--vora-text-muted)' }}>{note}</div>
                    : <ActionMenuList entries={entries} onSelected={onClose} roomy={isPhone} />}
            </div>
        </>
    );
}
