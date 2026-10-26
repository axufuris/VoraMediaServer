import { describe, it, expect, vi, afterEach } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import ActionMenu, { ActionMenuList } from './ActionMenu';
import { menuAction, menuChoice, menuSeparator, menuSubmenu, tidyMenu, type MenuEntry } from './menuEntries';
import { PHONE_MEDIA_QUERY } from '../../../hooks/useIsPhone';

const onPhone = (matches: boolean) => {
    vi.stubGlobal('matchMedia', (query: string) => ({
        matches: matches && query === PHONE_MEDIA_QUERY,
        media: query,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
    }));
};

const entries = (calls: string[] = []): MenuEntry[] => [
    menuAction('info', 'Media info', () => calls.push('info')),
    menuSeparator('one'),
    menuAction('edit', 'Edit metadata', () => calls.push('edit')),
    menuAction('delete', 'Delete item', () => calls.push('delete'), 'danger'),
];

describe('ActionMenuList', () => {
    it('swaps a submenu in under a Back row without closing, and back again', () => {
        const calls: string[] = [];
        render(<ActionMenuList entries={[
            menuAction('info', 'Media info', () => calls.push('info')),
            menuSubmenu('manage', 'Manage movie…', [menuAction('refresh', 'Refresh metadata', () => calls.push('refresh'))]),
        ]} onSelected={() => calls.push('closed')} />);

        fireEvent.click(screen.getByRole('menuitem', { name: 'Manage movie…' }));

        expect(calls).toEqual([]);
        expect(screen.getByText('Manage movie')).toBeInTheDocument();
        expect(screen.queryByRole('menuitem', { name: 'Media info' })).toBeNull();
        expect(screen.getByRole('menuitem', { name: 'Back' })).toHaveFocus();

        fireEvent.click(screen.getByRole('menuitem', { name: 'Refresh metadata' }));
        expect(calls).toEqual(['closed', 'refresh']);
    });

    it('returns from a submenu with Back', () => {
        render(<ActionMenuList entries={[
            menuAction('info', 'Media info', () => undefined),
            menuSubmenu('manage', 'Manage movie…', [menuAction('refresh', 'Refresh metadata', () => undefined)]),
        ]} onSelected={() => undefined} />);

        fireEvent.click(screen.getByRole('menuitem', { name: 'Manage movie…' }));
        fireEvent.click(screen.getByRole('menuitem', { name: 'Back' }));

        expect(screen.getByRole('menuitem', { name: 'Media info' })).toHaveFocus();
        expect(screen.queryByRole('menuitem', { name: 'Refresh metadata' })).toBeNull();
    });

    it('marks the choice in use and shows what each one is', () => {
        const calls: string[] = [];
        render(<ActionMenuList entries={[
            menuChoice('auto', 'Automatic', false, () => calls.push('auto')),
            menuChoice('jpn', 'Japanese · AAC 2.0', true, () => calls.push('jpn'), 'Direct Play'),
        ]} onSelected={() => calls.push('closed')} />);

        expect(screen.getByRole('menuitemradio', { name: /Japanese/ })).toHaveAttribute('aria-checked', 'true');
        expect(screen.getByRole('menuitemradio', { name: 'Automatic' })).toHaveAttribute('aria-checked', 'false');
        expect(screen.getByText('Direct Play')).toBeInTheDocument();

        fireEvent.click(screen.getByRole('menuitemradio', { name: 'Automatic' }));
        expect(calls).toEqual(['closed', 'auto']);
    });

    it('closes the menu before running what was picked', () => {
        const calls: string[] = [];
        render(<ActionMenuList entries={entries(calls)} onSelected={() => calls.push('closed')} />);

        fireEvent.click(screen.getByRole('menuitem', { name: 'Edit metadata' }));

        expect(calls).toEqual(['closed', 'edit']);
    });

    it('draws a line for a separator and colours a dangerous entry', () => {
        render(<ActionMenuList entries={entries()} onSelected={vi.fn()} />);

        expect(screen.getAllByRole('separator')).toHaveLength(1);
        expect(screen.getByRole('menuitem', { name: 'Delete item' })).toHaveStyle({ color: 'var(--vora-danger-text)' });
        expect(screen.getByRole('menuitem', { name: 'Media info' })).toHaveStyle({ color: 'var(--vora-text-primary)' });
    });
});

describe('ActionMenu', () => {
    afterEach(() => vi.unstubAllGlobals());

    it('opens at the pointer and stays inside the window', () => {
        onPhone(false);
        vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue(DOMRect.fromRect({ width: 224, height: 300 }));
        Object.defineProperty(window, 'innerWidth', { value: 1000, configurable: true });
        Object.defineProperty(window, 'innerHeight', { value: 700, configurable: true });

        const { rerender } = render(<ActionMenu at={{ x: 100, y: 120 }} entries={entries()} onClose={vi.fn()} />);
        expect(screen.getByRole('menu')).toHaveStyle({ left: '100px', top: '120px', visibility: 'visible' });

        rerender(<ActionMenu at={{ x: 990, y: 690 }} entries={entries()} onClose={vi.fn()} />);
        expect(screen.getByRole('menu')).toHaveStyle({ left: '768px', top: '392px' });

        vi.restoreAllMocks();
    });

    it('rises from the bottom edge on a phone, with taller rows', () => {
        onPhone(true);

        render(<ActionMenu at={{ x: 100, y: 120 }} title="Arrival" entries={entries()} onClose={vi.fn()} />);

        const menu = screen.getByRole('menu', { name: 'Arrival' });
        expect(menu.className).toContain('bottom-0');
        expect(menu.style.left).toBe('');
        expect(screen.getByRole('menuitem', { name: 'Media info' }).className).toContain('py-3.5');
    });

    it('moves through its entries with the arrow keys and closes on Escape', () => {
        onPhone(false);
        const onClose = vi.fn();
        render(<ActionMenu at={{ x: 10, y: 10 }} entries={entries()} onClose={onClose} />);
        const menu = screen.getByRole('menu');

        expect(menu).toHaveFocus();
        fireEvent.keyDown(menu, { key: 'ArrowDown' });
        expect(screen.getByRole('menuitem', { name: 'Media info' })).toHaveFocus();
        fireEvent.keyDown(menu, { key: 'ArrowUp' });
        expect(screen.getByRole('menuitem', { name: 'Delete item' })).toHaveFocus();
        fireEvent.keyDown(menu, { key: 'Home' });
        expect(screen.getByRole('menuitem', { name: 'Media info' })).toHaveFocus();

        fireEvent.keyDown(menu, { key: 'Escape' });
        expect(onClose).toHaveBeenCalledTimes(1);
    });

    it('closes when the window is resized or the page behind it is right-clicked', () => {
        onPhone(false);
        const onClose = vi.fn();
        render(<ActionMenu at={{ x: 10, y: 10 }} entries={entries()} onClose={onClose} />);

        fireEvent(window, new Event('resize'));
        expect(fireEvent.contextMenu(screen.getByTestId('action-menu-backdrop'))).toBe(false);

        expect(onClose).toHaveBeenCalledTimes(2);
    });

    it('ignores the tap left over from the long press that opened it', () => {
        onPhone(true);
        const calls: string[] = [];
        const onClose = vi.fn();
        render(<ActionMenu at={{ x: 10, y: 10 }} entries={entries(calls)} onClose={onClose} />);

        fireEvent.click(screen.getByRole('menuitem', { name: 'Delete item' }), { detail: 1 });
        fireEvent.click(screen.getByTestId('action-menu-backdrop'), { detail: 1 });

        expect(calls).toEqual([]);
        expect(onClose).not.toHaveBeenCalled();
    });

    it('acts on a tap that starts on the menu', () => {
        onPhone(true);
        const calls: string[] = [];
        render(<ActionMenu at={{ x: 10, y: 10 }} entries={entries(calls)} onClose={() => calls.push('closed')} />);
        const edit = screen.getByRole('menuitem', { name: 'Edit metadata' });

        fireEvent.pointerDown(edit);
        fireEvent.click(edit, { detail: 1 });

        expect(calls).toEqual(['closed', 'edit']);
    });

    it('closes on a tap that starts on the page behind it', () => {
        onPhone(true);
        const onClose = vi.fn();
        render(<ActionMenu at={{ x: 10, y: 10 }} entries={entries()} onClose={onClose} />);
        const backdrop = screen.getByTestId('action-menu-backdrop');

        fireEvent.pointerDown(backdrop);
        fireEvent.click(backdrop, { detail: 1 });

        expect(onClose).toHaveBeenCalledTimes(1);
    });

    it('shows a note in place of its entries', () => {
        onPhone(false);

        render(<ActionMenu at={{ x: 10, y: 10 }} entries={[]} note="Loading…" onClose={vi.fn()} />);

        expect(screen.getByRole('menu')).toHaveTextContent('Loading…');
        expect(screen.queryByRole('menuitem')).not.toBeInTheDocument();
    });
});

describe('tidyMenu', () => {
    const keys = (list: MenuEntry[]) => list.map(entry => entry.key);

    it('drops separators that open, close or repeat', () => {
        const tidy = tidyMenu([
            menuSeparator('a'),
            menuAction('one', 'One', vi.fn()),
            menuSeparator('b'),
            menuSeparator('c'),
            menuAction('two', 'Two', vi.fn()),
            menuSeparator('d'),
        ]);

        expect(keys(tidy)).toEqual(['one', 'b', 'two']);
    });

    it('leaves nothing of a menu that is only separators', () => {
        expect(tidyMenu([menuSeparator('a'), menuSeparator('b')])).toEqual([]);
    });
});
