import { describe, it, expect, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import LibraryCollectionGrid from './LibraryCollectionGrid';
import MinimumCollectionSizeField from '../Admin/Libraries/MinimumCollectionSizeField';
import type { CollectionSummary } from '../../api/Collections/collectionService';

const collection = (title: string, overrides: Partial<CollectionSummary> = {}): CollectionSummary => ({
    id: title, title, itemCount: 3, systemGenerated: true, ...overrides,
});

const collections = [
    collection('The Bourne Collection'),
    collection('Legacy of Bourne', { itemCount: 1, hiddenReason: 'BelowMinimumSize' }),
    collection('Alien Collection', { itemCount: 4, hiddenReason: 'AutomaticCollectionsHidden' }),
];

describe('LibraryCollectionGrid', () => {
    it('shows an admin only the visible ones until they ask for the hidden ones', () => {
        render(<LibraryCollectionGrid collections={collections} isAdmin onOpen={vi.fn()} empty={<p>none</p>} />);

        expect(screen.getByText('The Bourne Collection')).toBeInTheDocument();
        expect(screen.queryByText('Legacy of Bourne')).toBeNull();

        fireEvent.click(screen.getByRole('switch', { name: 'Show hidden (2)' }));

        expect(screen.getByText('Legacy of Bourne')).toBeInTheDocument();
        expect(screen.getByText('Hidden · below minimum')).toBeInTheDocument();
        expect(screen.getByText('Hidden · automatic off')).toBeInTheDocument();
    });

    it('gives a viewer no hidden toggle', () => {
        render(<LibraryCollectionGrid collections={[collections[0]]} isAdmin={false} onOpen={vi.fn()} empty={<p>none</p>} />);

        expect(screen.queryByRole('switch')).toBeNull();
    });

    it('shows the empty state when everything is hidden and the admin has not asked to see it', () => {
        render(<LibraryCollectionGrid collections={collections.slice(1)} isAdmin onOpen={vi.fn()} empty={<p>none</p>} />);

        expect(screen.getByText('none')).toBeInTheDocument();
        expect(screen.getByRole('switch', { name: 'Show hidden (2)' })).toBeInTheDocument();
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
