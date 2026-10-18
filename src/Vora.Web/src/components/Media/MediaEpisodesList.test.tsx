import { describe, it, expect, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import MediaEpisodesList from './MediaEpisodesList';
import type { Episode } from '../../api/Media/mediaService';

vi.mock('../../api/Media/mediaService', () => ({ mediaService: { setRating: () => Promise.resolve() } }));

const episodes: Episode[] = [
    { id: 'ep-1', episodeNumber: 1, title: 'Anjin', durationMinutes: 70, isPlayed: true, resumePositionSeconds: 0 },
    { id: 'ep-2', episodeNumber: 2, title: 'Servants of Two Masters', durationMinutes: 60, resumePositionSeconds: 1800 },
    { id: 'ep-3', episodeNumber: 3, title: 'Tomorrow Is Tomorrow', durationMinutes: 0, resumePositionSeconds: 0 },
];

function renderList(onPlay?: (episode: Episode) => void) {
    render(
        <MemoryRouter initialEntries={['/media/season-1']}>
            <Routes>
                <Route path="/media/season-1" element={<MediaEpisodesList episodes={episodes} onPlay={onPlay} />} />
                <Route path="/media/:id" element={<p>Episode page</p>} />
            </Routes>
        </MemoryRouter>,
    );
}

describe('MediaEpisodesList', () => {
    it("plays an episode from its picture and opens its page from its title", () => {
        const onPlay = vi.fn();
        renderList(onPlay);

        fireEvent.click(screen.getByRole('button', { name: 'Resume Servants of Two Masters' }));
        expect(onPlay).toHaveBeenCalledWith(expect.objectContaining({ id: 'ep-2' }));

        fireEvent.click(screen.getByText(/Tomorrow Is Tomorrow/));
        expect(screen.getByText('Episode page')).toBeInTheDocument();
    });

    it('shows progress only on an episode left part-way, and never a stray 0', () => {
        const { container } = render(
            <MemoryRouter><MediaEpisodesList episodes={episodes} onPlay={vi.fn()} /></MemoryRouter>,
        );

        expect(screen.getAllByTestId('episode-progress')).toHaveLength(1);
        expect(screen.getByTestId('episode-progress').firstElementChild).toHaveStyle({ width: '50%' });
        const pictures = container.querySelectorAll('button[aria-label]');
        pictures.forEach(picture => expect(picture.textContent).not.toMatch(/^0|0$/));
    });

    it('opens the episode page from its picture where the page offers no play', () => {
        renderList();

        fireEvent.click(screen.getByRole('button', { name: 'Open Anjin' }));

        expect(screen.getByText('Episode page')).toBeInTheDocument();
    });
});
