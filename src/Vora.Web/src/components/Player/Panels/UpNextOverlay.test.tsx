import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import UpNextOverlay from './UpNextOverlay';
import type { UpNextResultVM } from '../../../api/Media/mediaService';

const data: UpNextResultVM = {
    relatedLists: [
        {
            title: 'More from this Director',
            items: [
                { id: 'm1', title: 'The Marvels', type: 'Movie', posterUrl: '/marvels.jpg' },
                { id: 'm2', title: 'Karate Kid: Legends', type: 'Movie', posterUrl: '/karate.jpg' },
            ],
        },
    ],
};

describe('UpNextOverlay', () => {
    it('shows related titles in the same rows and cards as the home screen and plays the one picked', () => {
        const onPlayNext = vi.fn().mockResolvedValue(undefined);
        const { container } = render(
            <MemoryRouter>
                <UpNextOverlay currentMedia={{ title: 'Spider-Man: Brand New Day' }} upNextData={data} onPlayNext={onPlayNext} onClose={vi.fn()} />
            </MemoryRouter>,
        );

        expect(screen.getByText('More from this Director')).toBeInTheDocument();
        expect(container.querySelector('.overflow-x-auto.pb-4')).toBeNull();
        fireEvent.click(screen.getByText('Karate Kid: Legends'));

        expect(onPlayNext).toHaveBeenCalledWith(expect.objectContaining({ id: 'm2' }));
    });
});
