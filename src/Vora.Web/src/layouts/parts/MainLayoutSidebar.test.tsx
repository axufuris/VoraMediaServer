import { describe, it, expect, vi } from 'vitest';
import { act, createEvent, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import MainLayoutSidebar from './MainLayoutSidebar';
import type { NavItem } from '../MainLayout';

const nav = (id: string, order: number): NavItem => ({ id, title: id[0].toUpperCase() + id.slice(1), path: `/${id}`, type: 'system', isPinned: true, order });
const items = [nav('music', 0), nav('movies', 1), nav('shows', 2)];

const renderEditor = (onMoveItem = vi.fn()) => {
    render(
        <MemoryRouter>
            <MainLayoutSidebar
                navItems={items}
                pinnedItems={items}
                isEditingNav
                onToggleEditNav={vi.fn()}
                onMoveItem={onMoveItem}
                onTogglePin={vi.fn()}
                collapsed={false}
                onToggleCollapsed={vi.fn()}
            />
        </MemoryRouter>,
    );
    return onMoveItem;
};

const renderSidebar = ({ collapsed, editing = false, onToggleCollapsed = vi.fn() }: { collapsed: boolean; editing?: boolean; onToggleCollapsed?: () => void }) => {
    const unpinned: NavItem = { ...nav('podcasts', 3), isPinned: false };
    render(
        <MemoryRouter>
            <MainLayoutSidebar
                navItems={[...items, unpinned]}
                pinnedItems={items}
                isEditingNav={editing}
                onToggleEditNav={vi.fn()}
                onMoveItem={vi.fn()}
                onTogglePin={vi.fn()}
                collapsed={collapsed}
                onToggleCollapsed={onToggleCollapsed}
            />
        </MemoryRouter>,
    );
    return { panel: screen.getByTestId('sidebar-panel'), onToggleCollapsed };
};

const renderInstalled = ({ collapsed, pinned = false, editing = false, onToggle = vi.fn() }: { collapsed: boolean; pinned?: boolean; editing?: boolean; onToggle?: () => void }) => {
    render(
        <MemoryRouter>
            <MainLayoutSidebar
                navItems={items}
                pinnedItems={items}
                isEditingNav={editing}
                onToggleEditNav={vi.fn()}
                onMoveItem={vi.fn()}
                onTogglePin={vi.fn()}
                collapsed={collapsed}
                onToggleCollapsed={vi.fn()}
                pin={{ pinned, onToggle }}
            />
        </MemoryRouter>,
    );
    const panel = screen.getByTestId('sidebar-panel');
    return { panel, slot: panel.parentElement as HTMLElement, onToggle };
};

const dataTransfer = () => ({ setData: vi.fn(), effectAllowed: '', dropEffect: '' });

// jsdom's drag events carry no pointer position and rows have no size, so give
// the row a 40px box and the event a clientY inside its upper or lower half.
const dragOverHalf = (row: HTMLElement, half: 'upper' | 'lower') => {
    row.getBoundingClientRect = () => ({ top: 0, height: 40, bottom: 40, left: 0, right: 200, width: 200, x: 0, y: 0, toJSON: () => ({}) });
    const event = createEvent.dragOver(row, { dataTransfer: dataTransfer() });
    Object.defineProperty(event, 'clientY', { value: half === 'upper' ? 10 : 30 });
    fireEvent(row, event);
};

describe('Edit Navigation', () => {
    it('has no up/down buttons, only drag handles', () => {
        renderEditor();

        expect(screen.getAllByRole('button', { name: /^Move / })).toHaveLength(3);
        expect(screen.getByText('Drag to reorder. Pin to show in the sidebar.')).toBeInTheDocument();
    });

    it('drops a dragged row below the row when released on its lower half', () => {
        const onMoveItem = renderEditor();
        const shows = screen.getByTestId('nav-row-shows');

        fireEvent.dragStart(screen.getByTestId('nav-row-music'), { dataTransfer: dataTransfer() });
        dragOverHalf(shows, 'lower');
        fireEvent.drop(shows, { dataTransfer: dataTransfer() });

        expect(onMoveItem).toHaveBeenCalledWith(0, 2);
    });

    it('drops a dragged row above the row when released on its upper half', () => {
        const onMoveItem = renderEditor();
        const shows = screen.getByTestId('nav-row-shows');

        fireEvent.dragStart(screen.getByTestId('nav-row-music'), { dataTransfer: dataTransfer() });
        dragOverHalf(shows, 'upper');
        fireEvent.drop(shows, { dataTransfer: dataTransfer() });

        expect(onMoveItem).toHaveBeenCalledWith(0, 1);
    });

    it('does nothing when a row is dropped back where it started', () => {
        const onMoveItem = renderEditor();
        const movies = screen.getByTestId('nav-row-movies');

        fireEvent.dragStart(movies, { dataTransfer: dataTransfer() });
        dragOverHalf(movies, 'lower');
        fireEvent.drop(movies, { dataTransfer: dataTransfer() });

        expect(onMoveItem).not.toHaveBeenCalled();
    });

    it('moves with the arrow keys on the handle, and not past either end', () => {
        const onMoveItem = renderEditor();

        fireEvent.keyDown(screen.getByRole('button', { name: /^Move Movies/ }), { key: 'ArrowUp' });
        fireEvent.keyDown(screen.getByRole('button', { name: /^Move Shows/ }), { key: 'ArrowDown' });

        expect(onMoveItem).toHaveBeenCalledTimes(1);
        expect(onMoveItem).toHaveBeenCalledWith(1, 0);
    });
});

describe('Unpinned entries', () => {
    it('are not drawn in the sidebar, and there is no More section to open', () => {
        renderSidebar({ collapsed: false });

        expect(screen.queryByRole('link', { name: 'Podcasts' })).toBeNull();
        expect(screen.queryByRole('button', { name: 'More' })).toBeNull();
        expect(screen.getByRole('link', { name: 'Shows' })).toBeInTheDocument();
    });

    it('are still listed in Edit Navigation so they can be pinned again', () => {
        renderSidebar({ collapsed: false, editing: true });

        expect(screen.getByTestId('nav-row-podcasts')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Pin' })).toBeInTheDocument();
    });
});

describe('Collapsing the sidebar', () => {
    it('folds to its icons from tablet width up, keeping every link named and titled', () => {
        const { panel } = renderSidebar({ collapsed: true });

        expect(panel.className).toContain('md:w-[4.5rem]');
        for (const name of ['Home', 'Music', 'Movies', 'Shows']) {
            const link = screen.getByRole('link', { name });
            expect(link).toHaveAttribute('title', name);
            expect(link.querySelector('.md\\:sr-only')).not.toBeNull();
        }
        expect(screen.getByRole('button', { name: 'Edit Navigation' })).toHaveAttribute('title', 'Edit Navigation');
        expect(screen.getByRole('button', { name: 'Expand sidebar' })).toHaveAttribute('aria-expanded', 'false');
    });

    it('shows the full sidebar with labels and no tooltips when expanded', () => {
        const { panel } = renderSidebar({ collapsed: false });

        expect(panel.className).not.toContain('md:w-[4.5rem]');
        expect(screen.getByRole('link', { name: 'Home' })).not.toHaveAttribute('title');
        expect(screen.getByRole('button', { name: 'Collapse sidebar' })).toHaveAttribute('aria-expanded', 'true');
    });

    it('collapses and expands from its button, which a phone never shows', () => {
        const { onToggleCollapsed } = renderSidebar({ collapsed: false });
        const toggle = screen.getByRole('button', { name: 'Collapse sidebar' });

        fireEvent.click(toggle);

        expect(onToggleCollapsed).toHaveBeenCalledTimes(1);
        expect(toggle.className).toContain('max-md:hidden');
    });

    it('opens Edit Navigation at full width even while folded', () => {
        const { panel } = renderSidebar({ collapsed: true, editing: true });

        expect(panel.className).not.toContain('md:w-[4.5rem]');
        expect(screen.queryByRole('button', { name: 'Expand sidebar' })).toBeNull();
        expect(screen.getByText('Manage sidebar')).toBeInTheDocument();
    });
});

describe('Folding away on its own, in an installed app', () => {
    it('draws no pin in a browser tab, where only the fold button is offered', () => {
        renderSidebar({ collapsed: false });

        expect(screen.queryByRole('button', { name: 'Pin sidebar' })).toBeNull();
        expect(screen.getByRole('button', { name: 'Collapse sidebar' })).toBeInTheDocument();
    });

    it('offers the pin instead of the fold button while it folds itself', () => {
        const { onToggle } = renderInstalled({ collapsed: false });

        const pin = screen.getByRole('button', { name: 'Pin sidebar' });
        expect(pin).toHaveAttribute('aria-pressed', 'false');
        expect(pin.className).toContain('max-md:hidden');
        expect(screen.queryByRole('button', { name: 'Collapse sidebar' })).toBeNull();
        expect(screen.queryByRole('button', { name: 'Expand sidebar' })).toBeNull();

        fireEvent.click(pin);

        expect(onToggle).toHaveBeenCalledTimes(1);
    });

    it('brings the fold button back once pinned, and the pin shows as pressed', () => {
        const { panel, slot } = renderInstalled({ collapsed: false, pinned: true });

        expect(screen.getByRole('button', { name: 'Pin sidebar' })).toHaveAttribute('aria-pressed', 'true');
        expect(screen.getByRole('button', { name: 'Collapse sidebar' })).toBeInTheDocument();
        expect(slot.className).toBe('contents');
        expect(panel.className).not.toContain('md:absolute');
    });

    it('stays as the viewer folded it while pinned', () => {
        const { panel } = renderInstalled({ collapsed: true, pinned: true });

        fireEvent.pointerEnter(panel.parentElement as HTMLElement);

        expect(panel.className).toContain('md:w-[4.5rem]');
        expect(screen.getByRole('link', { name: 'Home' })).toHaveAttribute('title', 'Home');
    });

    it('is a rail that holds only its icons, keeping the room of the rail beside the page', () => {
        const { panel, slot } = renderInstalled({ collapsed: true });

        expect(slot.className).toContain('md:w-[4.5rem]');
        expect(panel.className).toContain('md:w-[4.5rem]');
        expect(panel.className).toContain('md:absolute');
        expect(screen.getByRole('link', { name: 'Music' })).toHaveAttribute('title', 'Music');
    });

    it('opens over the page under a mouse and folds back when it leaves', () => {
        const { panel, slot } = renderInstalled({ collapsed: true });

        fireEvent.pointerEnter(slot);

        expect(panel.className).not.toContain('md:w-[4.5rem]');
        expect(panel.className).toContain('md:shadow-[var(--vora-shadow-overlay)]');
        expect(slot.className).toContain('md:w-[4.5rem]');
        expect(slot.className).toContain('md:z-[101]');
        expect(screen.getByRole('link', { name: 'Music' })).not.toHaveAttribute('title');

        fireEvent.pointerLeave(slot);

        expect(panel.className).toContain('md:w-[4.5rem]');
        expect(panel.className).not.toContain('md:shadow-');
    });

    it('does not open for a touch, which would leave it stuck open', () => {
        const { panel, slot } = renderInstalled({ collapsed: true });

        fireEvent(slot, createEvent.pointerEnter(slot, { pointerType: 'touch' }));

        expect(panel.className).toContain('md:w-[4.5rem]');
    });

    it('opens for a keyboard moving into it and folds back when focus leaves', () => {
        const { panel } = renderInstalled({ collapsed: true });
        const music = screen.getByRole('link', { name: 'Music' });

        act(() => music.focus());

        expect(panel.className).not.toContain('md:w-[4.5rem]');

        act(() => music.blur());

        expect(panel.className).toContain('md:w-[4.5rem]');
    });

    it('is full width on Home, with the room of the full sidebar', () => {
        const { panel, slot } = renderInstalled({ collapsed: false });

        expect(slot.className).toContain('md:w-64');
        expect(panel.className).not.toContain('md:w-[4.5rem]');
    });

    it('opens Edit Navigation at full width, and holds the room for it', () => {
        const { panel, slot } = renderInstalled({ collapsed: true, editing: true });

        expect(panel.className).not.toContain('md:w-[4.5rem]');
        expect(slot.className).toContain('md:w-64');
        expect(screen.queryByRole('button', { name: 'Pin sidebar' })).toBeNull();
    });

    it('fills with the accent on focus instead of drawing a ring', () => {
        renderInstalled({ collapsed: false });

        const classes = screen.getByRole('button', { name: 'Pin sidebar' }).className;

        expect(classes).toContain('focus-visible:bg-[var(--vora-accent-500)]!');
        expect(classes).toContain('focus-visible:text-[var(--vora-accent-contrast)]!');
        expect(classes).toContain('focus-visible:outline-none!');
    });
});
