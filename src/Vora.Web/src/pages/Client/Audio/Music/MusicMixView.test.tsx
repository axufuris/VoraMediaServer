import { describe, it, expect, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import MusicMixView from './MusicMixView';
import type { GeneratedMixDetailVM } from '../../../../api/Music/musicService';

vi.mock('../../../../components/Collections/SaveMixButton', () => ({ default: () => null }));

const mix = (overrides: Partial<GeneratedMixDetailVM> = {}): GeneratedMixDetailVM => ({
    id: 'm1',
    slot: 1,
    name: 'Nostalgic Vibes of the 90s',
    kind: 'Requested',
    descriptionTag: 'Your request',
    artworkUrl: '/art/cover.jpg',
    generatedAt: '2026-09-26',
    tracks: [
        { id: 't1', title: '9 Teen 90 Nine', artist: 'Limp Bizkit', trackNumber: 1, isLiked: false, lockedFields: [], globalListeners: 250_000 },
        { id: 't2', title: 'Deep Cut', artist: '311', trackNumber: 2, isLiked: false, lockedFields: [] },
    ],
    ...overrides,
});

const renderMix = (value: GeneratedMixDetailVM) => render(
    <MemoryRouter>
        <MusicMixView
            isLoading={false}
            currentMix={value}
            isShuffled={false}
            toggleShuffle={vi.fn()}
            playMixFromIndex={vi.fn()}
            formatDuration={() => '3:00'}
        />
    </MemoryRouter>,
);

describe('MusicMixView', () => {
    it("shows each track's Last.fm listeners where known", () => {
        renderMix(mix());

        const figure = screen.getByTitle('250,000 listeners on Last.fm');
        expect(figure).toHaveTextContent('250K');
        expect(within(figure).getByRole('img', { name: 'Last.fm' })).toBeInTheDocument();
        expect(screen.getAllByRole('img', { name: 'Last.fm' })).toHaveLength(1);
    });

    it('does not label an AI playlist as a Daily Mix', () => {
        renderMix(mix());

        expect(screen.queryByText(/Daily Mix/)).toBeNull();
    });

    it('still labels a Daily Mix by its slot', () => {
        renderMix(mix({ kind: 'DailyMix', slot: 3, descriptionTag: 'Rock' }));

        expect(screen.getByText('Daily Mix 3')).toBeInTheDocument();
    });
});
