import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import PersonCard from './PersonCard';
import CastRow from './CastRow';

describe('person tile size', () => {
    it('uses the standard width by default', () => {
        const { container } = render(<PersonCard name="Paddy Considine" role="Actor" />);

        expect(container.firstElementChild).toHaveStyle({ width: 'var(--vora-person-w)' });
    });

    it('uses the compact width when asked', () => {
        const { container } = render(<PersonCard name="Paddy Considine" role="Actor" size="sm" />);

        expect(container.firstElementChild).toHaveStyle({ width: 'var(--vora-person-w-sm)' });
    });

    // Cast & Crew on every details page is the compact row; search keeps the
    // standard tile.
    it('renders cast rows with compact tiles', () => {
        render(<CastRow cast={[{ id: '1', name: 'Paddy Considine', role: 'Actor', characterName: 'Viserys Targaryen' }]} />);

        const tile = screen.getByText('Paddy Considine').parentElement;
        expect(tile).toHaveStyle({ width: 'var(--vora-person-w-sm)' });
    });
});
