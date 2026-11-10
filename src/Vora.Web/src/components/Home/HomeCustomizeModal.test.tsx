import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import HomeCustomizeModal, { type HomeLayoutItem } from './HomeCustomizeModal';
import type { SmartListClientDto } from '../../api/Collections/smartListService';

const list = (id: string, title: string) => ({ id, title }) as SmartListClientDto;
const lists = [list('movies', 'Recently Added Movies'), list('shows', 'Recently Added Shows'), list('music', 'Recently Added Music')];

const show = (onSave: (layout: HomeLayoutItem[]) => void) =>
    render(<HomeCustomizeModal isOpen onClose={() => { }} activeLists={lists} savedLayout={[]} onSave={onSave} />);

const dataTransfer = () => ({ effectAllowed: '', setData: vi.fn(), getData: vi.fn() });

afterEach(cleanup);

describe('Customize Home', () => {
    it('reorders by dragging a row onto another, with no arrows', async () => {
        const onSave = vi.fn();
        show(onSave);
        await screen.findByText('Recently Added Movies');

        expect(screen.queryByText('▲')).toBeNull();
        expect(screen.queryByText('▼')).toBeNull();

        const transfer = dataTransfer();
        fireEvent.dragStart(screen.getByTestId('home-layout-row-movies'), { dataTransfer: transfer });
        fireEvent.drop(screen.getByTestId('home-layout-row-music'), { dataTransfer: transfer });
        fireEvent.click(screen.getByText('Save Layout'));

        const saved: HomeLayoutItem[] = onSave.mock.calls[0][0];
        expect(saved.map(i => i.listId)).toEqual(['shows', 'music', 'movies']);
        expect(saved.map(i => i.orderIndex)).toEqual([0, 1, 2]);
    });

    it('still hides a list from its checkbox', async () => {
        const onSave = vi.fn();
        show(onSave);

        fireEvent.click(await screen.findByLabelText('Show Recently Added Shows'));
        fireEvent.click(screen.getByText('Save Layout'));

        const saved: HomeLayoutItem[] = onSave.mock.calls[0][0];
        expect(saved.find(i => i.listId === 'shows')?.isEnabled).toBe(false);
    });
});
