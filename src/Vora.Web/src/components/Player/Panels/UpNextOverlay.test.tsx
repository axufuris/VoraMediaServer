import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import UpNextOverlay from './UpNextOverlay';
import type { UpNextItemVM, UpNextResultVM } from '../../../api/Media/mediaService';

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

const nextEpisode: UpNextResultVM = {
    nextItem: { id: 'ep-2', title: 'The Second One', type: 'Episode', tvShowTitle: 'A Show', seasonNumber: 1, episodeNumber: 2 },
    relatedLists: [],
};

function renderOverlay(props: { autoPlay?: boolean; finished?: boolean; secondsLeft?: number; upNextData?: UpNextResultVM }) {
    const onPlayNext = vi.fn().mockResolvedValue(undefined);
    const onCancelAutoPlay = vi.fn();
    const view = render(
        <MemoryRouter>
            <UpNextOverlay
                currentMedia={{ title: 'The First One' }}
                upNextData={props.upNextData ?? nextEpisode}
                onPlayNext={onPlayNext}
                onClose={vi.fn()}
                autoPlay={props.autoPlay}
                finished={props.finished}
                secondsLeft={props.secondsLeft}
                onCancelAutoPlay={onCancelAutoPlay}
            />
        </MemoryRouter>,
    );
    return { ...view, onPlayNext, onCancelAutoPlay };
}

describe('UpNextOverlay autoplay', () => {
    beforeEach(() => { vi.useFakeTimers(); });
    afterEach(() => { vi.useRealTimers(); });

    function finishedOverlay(onPlayNext: (item: UpNextItemVM) => Promise<void>, onCancelAutoPlay: () => void) {
        return (
            <MemoryRouter>
                <UpNextOverlay currentMedia={{ title: 'The First One' }} upNextData={nextEpisode} onPlayNext={onPlayNext} onClose={vi.fn()} autoPlay finished secondsLeft={0} onCancelAutoPlay={onCancelAutoPlay} />
            </MemoryRouter>
        );
    }

    it('counts down to the next episode while this one ends, and starts it when it finishes', () => {
        const { onPlayNext, rerender, onCancelAutoPlay } = renderOverlay({ autoPlay: true, secondsLeft: 11.2 });

        expect(screen.getByRole('status')).toHaveTextContent('Playing in 12s');
        expect(onPlayNext).not.toHaveBeenCalled();

        act(() => { vi.advanceTimersByTime(11_200); });
        rerender(finishedOverlay(onPlayNext, onCancelAutoPlay));

        expect(screen.getByRole('status')).toHaveTextContent('Starting…');
        expect(onPlayNext).toHaveBeenCalledTimes(1);
        expect(onPlayNext).toHaveBeenCalledWith(expect.objectContaining({ id: 'ep-2' }));
    });

    it('counts down from ten after Skip Credits, and only then starts the next episode', () => {
        const { onPlayNext, rerender, onCancelAutoPlay } = renderOverlay({ autoPlay: true, secondsLeft: 2 });
        expect(screen.getByRole('status')).toHaveTextContent('Playing in 10s');

        act(() => { vi.advanceTimersByTime(2_000); });
        rerender(finishedOverlay(onPlayNext, onCancelAutoPlay));

        expect(screen.getByRole('status')).toHaveTextContent('Playing in 8s');
        expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
        expect(onPlayNext).not.toHaveBeenCalled();

        act(() => { vi.advanceTimersByTime(8_000); });

        expect(screen.getByRole('status')).toHaveTextContent('Starting…');
        expect(screen.queryByRole('button', { name: 'Cancel' })).toBeNull();
        expect(onPlayNext).toHaveBeenCalledTimes(1);
    });

    it('starts the next episode once, however often the player redraws', () => {
        const { onPlayNext, rerender, onCancelAutoPlay } = renderOverlay({ autoPlay: true, finished: true });
        act(() => { vi.advanceTimersByTime(10_000); });

        for (let i = 0; i < 3; i++) {
            rerender(finishedOverlay(onPlayNext, onCancelAutoPlay));
        }

        expect(onPlayNext).toHaveBeenCalledTimes(1);
    });

    it('lets the viewer cancel it', () => {
        const { onCancelAutoPlay } = renderOverlay({ autoPlay: true, secondsLeft: 8 });

        fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

        expect(onCancelAutoPlay).toHaveBeenCalledTimes(1);
    });

    it('waits for a pick when autoplay is off or was cancelled', () => {
        const { onPlayNext } = renderOverlay({ autoPlay: false, finished: true });

        expect(screen.queryByRole('status')).toBeNull();
        expect(onPlayNext).not.toHaveBeenCalled();
        expect(screen.getByText('The Second One')).toBeInTheDocument();
    });

    it('has nothing to count down to after a film with no next item', () => {
        const { onPlayNext } = renderOverlay({ autoPlay: true, finished: true, upNextData: data });

        expect(screen.queryByRole('status')).toBeNull();
        expect(onPlayNext).not.toHaveBeenCalled();
    });
});
