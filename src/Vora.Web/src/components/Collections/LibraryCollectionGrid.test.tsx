import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import LibraryCollectionGrid from './LibraryCollectionGrid';
import { withHiddenByAdmin } from './hiddenByAdmin';
import MinimumCollectionSizeField from '../Admin/Libraries/MinimumCollectionSizeField';
import type { CollectionSummary } from '../../api/Collections/collectionService';

const mocks = vi.hoisted(() => ({
    setHidden: vi.fn(),
    alert: vi.fn(),
}));

vi.mock('../../api/Collections/collectionAdminService', () => ({ collectionAdminService: { setHidden: mocks.setHidden } }));
vi.mock('../../dialogs', () => ({
    useDialog: () => ({ alert: mocks.alert, confirm: () => Promise.resolve(true), prompt: () => Promise.resolve(null) }),
}));

const collection = (title: string, overrides: Partial<CollectionSummary> = {}): CollectionSummary => ({
    id: title, title, itemCount: 3, systemGenerated: true, ...overrides,
});

const collections = [
    collection('The Bourne Collection'),
    collection('Legacy of Bourne', { itemCount: 1, hiddenReason: 'BelowMinimumSize' }),
    collection('Alien Collection', { itemCount: 4, hiddenReason: 'AutomaticCollectionsHidden' }),
];

const tile = (title: string) => screen.getByText(title).closest('[role="button"]') as HTMLElement;

describe('LibraryCollectionGrid', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.alert.mockResolvedValue(undefined);
    });

    it('shows an admin only the visible ones until they ask for the hidden ones', () => {
        render(<LibraryCollectionGrid collections={collections} isAdmin onOpen={vi.fn()} onHiddenChanged={vi.fn()} empty={<p>none</p>} />);

        expect(screen.getByText('The Bourne Collection')).toBeInTheDocument();
        expect(screen.queryByText('Legacy of Bourne')).toBeNull();

        fireEvent.click(screen.getByRole('switch', { name: 'Show hidden (2)' }));

        expect(screen.getByText('Legacy of Bourne')).toBeInTheDocument();
        expect(screen.getByText('Below minimum')).toHaveTextContent('Hidden · Below minimum');
        expect(screen.getByText('Automatic off')).toHaveTextContent('Hidden · Automatic off');
    });

    it('marks a collection an admin hid', () => {
        render(<LibraryCollectionGrid collections={[collections[0], collection('John Wick Collection', { hiddenReason: 'Hidden' })]} isAdmin onOpen={vi.fn()} onHiddenChanged={vi.fn()} empty={<p>none</p>} />);

        fireEvent.click(screen.getByRole('switch', { name: 'Show hidden (1)' }));

        expect(screen.getByText('John Wick Collection')).toBeInTheDocument();
        expect(screen.getByText('Marked collections are hidden from viewers.')).toBeInTheDocument();
    });

    it('gives a viewer no hidden toggle', () => {
        render(<LibraryCollectionGrid collections={[collections[0]]} isAdmin={false} onOpen={vi.fn()} onHiddenChanged={vi.fn()} empty={<p>none</p>} />);

        expect(screen.queryByRole('switch')).toBeNull();
    });

    it('shows the empty state when everything is hidden and the admin has not asked to see it', () => {
        render(<LibraryCollectionGrid collections={collections.slice(1)} isAdmin onOpen={vi.fn()} onHiddenChanged={vi.fn()} empty={<p>none</p>} />);

        expect(screen.getByText('none')).toBeInTheDocument();
        expect(screen.getByRole('switch', { name: 'Show hidden (2)' })).toBeInTheDocument();
    });
});

describe('LibraryCollectionGrid tile menu', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.alert.mockResolvedValue(undefined);
        mocks.setHidden.mockResolvedValue(undefined);
    });

    it('lets an admin hide a collection from viewers with a right-click', async () => {
        const onOpen = vi.fn();
        const onHiddenChanged = vi.fn();
        render(<LibraryCollectionGrid collections={[collections[0]]} isAdmin serverId="server-2" onOpen={onOpen} onHiddenChanged={onHiddenChanged} empty={<p>none</p>} />);

        const prevented = !fireEvent.contextMenu(tile('The Bourne Collection'), { clientX: 30, clientY: 40 });

        expect(prevented).toBe(true);
        expect(onOpen).not.toHaveBeenCalled();
        expect(screen.getByRole('menu', { name: 'The Bourne Collection' })).toBeInTheDocument();
        expect(screen.queryByRole('menuitem', { name: 'Show to viewers' })).toBeNull();
        fireEvent.click(screen.getByRole('menuitem', { name: 'Hide from viewers' }));

        expect(screen.queryByRole('menu')).toBeNull();
        await waitFor(() => expect(onHiddenChanged).toHaveBeenCalledWith(collections[0], true));
        expect(mocks.setHidden).toHaveBeenCalledWith('The Bourne Collection', true, 'server-2');
    });

    it('offers to show a collection an admin hid to viewers again', async () => {
        const hidden = collection('John Wick Collection', { hiddenReason: 'Hidden' });
        const onHiddenChanged = vi.fn();
        render(<LibraryCollectionGrid collections={[hidden]} isAdmin onOpen={vi.fn()} onHiddenChanged={onHiddenChanged} empty={<p>none</p>} />);

        fireEvent.click(screen.getByRole('switch', { name: 'Show hidden (1)' }));
        fireEvent.contextMenu(tile('John Wick Collection'), { clientX: 30, clientY: 40 });

        expect(screen.queryByRole('menuitem', { name: 'Hide from viewers' })).toBeNull();
        fireEvent.click(screen.getByRole('menuitem', { name: 'Show to viewers' }));

        await waitFor(() => expect(onHiddenChanged).toHaveBeenCalledWith(hidden, false));
        expect(mocks.setHidden).toHaveBeenCalledWith('John Wick Collection', false, undefined);
    });

    it('offers to hide a collection that is hidden for another reason', () => {
        render(<LibraryCollectionGrid collections={[collections[1]]} isAdmin onOpen={vi.fn()} onHiddenChanged={vi.fn()} empty={<p>none</p>} />);

        fireEvent.click(screen.getByRole('switch', { name: 'Show hidden (1)' }));
        fireEvent.contextMenu(tile('Legacy of Bourne'), { clientX: 30, clientY: 40 });

        expect(screen.getByRole('menuitem', { name: 'Hide from viewers' })).toBeInTheDocument();
    });

    it("leaves a viewer the browser's own menu", () => {
        render(<LibraryCollectionGrid collections={[collections[0]]} isAdmin={false} onOpen={vi.fn()} onHiddenChanged={vi.fn()} empty={<p>none</p>} />);

        expect(fireEvent.contextMenu(tile('The Bourne Collection'), { clientX: 30, clientY: 40 })).toBe(true);

        expect(screen.queryByRole('menu')).toBeNull();
        expect(screen.queryByRole('menuitem', { name: 'Hide from viewers' })).toBeNull();
    });

    it('says why when the server refuses, and leaves the list alone', async () => {
        mocks.setHidden.mockRejectedValue(new Error('Request failed with status code 403'));
        const onHiddenChanged = vi.fn();
        render(<LibraryCollectionGrid collections={[collections[0]]} isAdmin onOpen={vi.fn()} onHiddenChanged={onHiddenChanged} empty={<p>none</p>} />);

        fireEvent.contextMenu(tile('The Bourne Collection'), { clientX: 30, clientY: 40 });
        fireEvent.click(screen.getByRole('menuitem', { name: 'Hide from viewers' }));

        await waitFor(() => expect(mocks.alert).toHaveBeenCalledWith("Couldn't hide The Bourne Collection: Request failed with status code 403"));
        expect(onHiddenChanged).not.toHaveBeenCalled();
    });
});

describe('withHiddenByAdmin', () => {
    it('marks the one collection hidden by an admin, and clears the mark when shown again', () => {
        const hidden = withHiddenByAdmin(collections, 'Legacy of Bourne', true);

        expect(hidden.map(c => c.hiddenReason)).toEqual([undefined, 'Hidden', 'AutomaticCollectionsHidden']);
        expect(withHiddenByAdmin(hidden, 'Legacy of Bourne', false)[1].hiddenReason).toBeNull();
    });
});

describe('MinimumCollectionSizeField', () => {
    it('offers hiding every automatic collection as well as 1 to 25', () => {
        const onChange = vi.fn();
        render(<MinimumCollectionSizeField value={3} onChange={onChange} />);

        const select = screen.getByLabelText('Minimum Collection Size');
        expect(screen.getByRole('option', { name: 'Hide automatic collections' })).toHaveValue('0');
        expect(screen.getAllByRole('option')).toHaveLength(26);

        fireEvent.change(select, { target: { value: '0' } });
        expect(onChange).toHaveBeenCalledWith(0);
    });
});
