import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import ContinueListeningRow from './ContinueListeningRow';
import type { MusicResumeItemVM } from '../../../../api/Music/musicService';

class NoopResizeObserver {
    observe() {}
    unobserve() {}
    disconnect() {}
}

const item = (overrides: Partial<MusicResumeItemVM>): MusicResumeItemVM => ({
    kind: 'Playlist', id: 'p1', title: 'Road trip', subtitle: 'Karma Police', artworkUrl: '/cover.jpg', artworkUrls: [], trackCount: 24,
    playedAt: '2026-11-19T20:00:00Z', source: 'Playlist', sourceId: 'p1', trackIndex: 2, ...overrides,
});

describe('ContinueListeningRow', () => {
    it('draws nothing when there is nothing to continue', () => {
        vi.stubGlobal('ResizeObserver', NoopResizeObserver);

        const { container } = render(<ContinueListeningRow items={[]} onResume={vi.fn()} />);

        expect(container).toBeEmptyDOMElement();
    });

    it('shows each queue with the song it stopped on, and starts the one that is picked', () => {
        vi.stubGlobal('ResizeObserver', NoopResizeObserver);
        const onResume = vi.fn();
        const road = item({});
        const album = item({ kind: 'Track', id: 't9', title: 'Airbag', subtitle: 'OK Computer', source: 'Album', sourceId: 'al1' });

        render(<ContinueListeningRow items={[road, album]} onResume={onResume} />);

        expect(screen.getByText('Continue Listening')).toBeInTheDocument();
        expect(screen.getByText('Karma Police')).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: /Airbag/ }));

        expect(onResume).toHaveBeenCalledWith(album);
    });
});
