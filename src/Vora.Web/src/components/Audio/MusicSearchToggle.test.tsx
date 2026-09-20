import { describe, it, expect, vi } from 'vitest';
import { useState } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import MusicSearchToggle from './MusicSearchToggle';

function Harness({ onQuery = vi.fn() }: { onQuery?: (q: string) => void }) {
    const [isOpen, setIsOpen] = useState(false);
    const [query, setQuery] = useState('');
    return (
        <MusicSearchToggle
            isOpen={isOpen}
            query={query}
            onOpenChange={(open) => { setIsOpen(open); if (!open) setQuery(''); }}
            onQueryChange={(q) => { setQuery(q); onQuery(q); }}
        />
    );
}

describe('MusicSearchToggle', () => {
    it('keeps the search field out of the page until the button is pressed', () => {
        render(<Harness />);

        expect(screen.queryByRole('searchbox')).toBeNull();
        expect(screen.getByRole('button', { name: 'Search music' })).toHaveAttribute('aria-expanded', 'false');
    });

    it('reveals and focuses the field when opened', () => {
        render(<Harness />);

        fireEvent.click(screen.getByRole('button', { name: 'Search music' }));

        const field = screen.getByRole('searchbox', { name: 'Search music' });
        expect(field).toHaveFocus();
        expect(screen.getByRole('button', { name: 'Close music search' })).toHaveAttribute('aria-expanded', 'true');
    });

    it('passes typing through to the existing search', () => {
        const onQuery = vi.fn();
        render(<Harness onQuery={onQuery} />);
        fireEvent.click(screen.getByRole('button', { name: 'Search music' }));

        fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'blink' } });

        expect(onQuery).toHaveBeenLastCalledWith('blink');
    });

    it('closes on Escape and on the button, hiding the field again', () => {
        render(<Harness />);
        fireEvent.click(screen.getByRole('button', { name: 'Search music' }));

        fireEvent.keyDown(screen.getByRole('searchbox'), { key: 'Escape' });
        expect(screen.queryByRole('searchbox')).toBeNull();

        fireEvent.click(screen.getByRole('button', { name: 'Search music' }));
        fireEvent.click(screen.getByRole('button', { name: 'Close music search' }));
        expect(screen.queryByRole('searchbox')).toBeNull();
    });

    it('clears the query without closing', () => {
        render(<Harness />);
        fireEvent.click(screen.getByRole('button', { name: 'Search music' }));
        fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'blink' } });

        fireEvent.click(screen.getByRole('button', { name: 'Clear search' }));

        expect(screen.getByRole('searchbox')).toHaveValue('');
    });
});
