import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { useUpNextEnding } from './useUpNextEnding';
import UpNextOverlay from './Panels/UpNextOverlay';
import type { UpNextResultVM } from '../../api/Media/mediaService';

const upNext: UpNextResultVM = {
    nextItem: {
        id: 'ep-2',
        title: 'The Second One',
        type: 'Episode',
        tvShowTitle: 'A Show',
        seasonNumber: 1,
        episodeNumber: 2,
        overview: 'What happens next.',
    },
    relatedLists: [],
};

// Stands in for the player: the same inputs the real one passes, and the
// overlay it renders when the hook says the item is ending.
function Player({ mediaId, currentTime, duration, eligible = true, onEnterEnding }: {
    mediaId: string;
    currentTime: number;
    duration: number;
    eligible?: boolean;
    onEnterEnding?: () => void;
}) {
    const isEnding = useUpNextEnding({ mediaId, eligible, currentTime, duration, onEnterEnding });
    if (!isEnding) return <div>playing</div>;
    return (
        <UpNextOverlay
            currentMedia={{ title: 'The First One', subtitle: 'S1 · E1' }}
            upNextData={upNext}
            onPlayNext={() => Promise.resolve()}
            onClose={() => { }}
        />
    );
}

const overlay = () => screen.queryByText('Up next');

describe('useUpNextEnding', () => {
    it('opens the overlay once playback reaches the last fifteen seconds', () => {
        const { rerender } = render(<Player mediaId="ep-1" currentTime={80} duration={100} />);
        expect(overlay()).toBeNull();

        rerender(<Player mediaId="ep-1" currentTime={84} duration={100} />);
        expect(overlay()).toBeNull();

        rerender(<Player mediaId="ep-1" currentTime={86} duration={100} />);
        expect(overlay()).toBeInTheDocument();
        expect(screen.getByText('The Second One')).toBeInTheDocument();
    });

    it('closes again when the viewer seeks back', () => {
        const { rerender } = render(<Player mediaId="ep-1" currentTime={86} duration={100} />);
        expect(overlay()).toBeInTheDocument();

        rerender(<Player mediaId="ep-1" currentTime={50} duration={100} />);
        expect(overlay()).toBeNull();

        rerender(<Player mediaId="ep-1" currentTime={92} duration={100} />);
        expect(overlay()).toBeInTheDocument();
    });

    it('resets for the next item and opens again near its end', () => {
        const { rerender } = render(<Player mediaId="ep-1" currentTime={97} duration={100} />);
        expect(overlay()).toBeInTheDocument();

        rerender(<Player mediaId="ep-2" currentTime={0} duration={100} />);
        expect(overlay()).toBeNull();

        rerender(<Player mediaId="ep-2" currentTime={90} duration={100} />);
        expect(overlay()).toBeInTheDocument();
    });

    it('stays up while the item runs out', () => {
        const { rerender } = render(<Player mediaId="ep-1" currentTime={95} duration={100} />);
        expect(overlay()).toBeInTheDocument();

        rerender(<Player mediaId="ep-1" currentTime={100} duration={100} />);
        expect(overlay()).toBeInTheDocument();
    });

    it('stays shut for live TV, extras and a minimised player', () => {
        render(<Player mediaId="live-1" currentTime={95} duration={100} eligible={false} />);

        expect(overlay()).toBeNull();
    });

    it('stays shut before a duration is known', () => {
        render(<Player mediaId="ep-1" currentTime={0} duration={0} />);

        expect(overlay()).toBeNull();
    });

    it('refreshes what comes next once per entry, not on every tick', () => {
        const onEnterEnding = vi.fn();
        const { rerender } = render(<Player mediaId="ep-1" currentTime={80} duration={100} onEnterEnding={onEnterEnding} />);
        expect(onEnterEnding).not.toHaveBeenCalled();

        rerender(<Player mediaId="ep-1" currentTime={86} duration={100} onEnterEnding={onEnterEnding} />);
        rerender(<Player mediaId="ep-1" currentTime={88} duration={100} onEnterEnding={onEnterEnding} />);
        rerender(<Player mediaId="ep-1" currentTime={90} duration={100} onEnterEnding={onEnterEnding} />);
        expect(onEnterEnding).toHaveBeenCalledTimes(1);

        rerender(<Player mediaId="ep-1" currentTime={40} duration={100} onEnterEnding={onEnterEnding} />);
        rerender(<Player mediaId="ep-1" currentTime={87} duration={100} onEnterEnding={onEnterEnding} />);
        expect(onEnterEnding).toHaveBeenCalledTimes(2);
    });
});
