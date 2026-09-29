import { describe, it, expect, vi } from 'vitest';
import { createEvent, fireEvent, render, screen } from '@testing-library/react';
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
                unpinnedItems={[]}
                isEditingNav
                showUnpinned={false}
                onToggleEditNav={vi.fn()}
                onToggleShowUnpinned={vi.fn()}
                onMoveItem={onMoveItem}
                onTogglePin={vi.fn()}
            />
        </MemoryRouter>,
    );
    return onMoveItem;
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
