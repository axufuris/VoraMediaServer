import { describe, it, expect, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import MusicMixView from './MusicMixView';
import type { GeneratedMixDetailVM } from '../../../../api/Music/musicService';

vi.mock('../../../../components/Collections/SaveMixButton', () => ({ default: () => null }));

const mocks = vi.hoisted(() => ({ confirm: vi.fn(), alert: vi.fn(), remove: vi.fn(), regenerate: vi.fn() }));
vi.mock('../../../../dialogs', () => ({ useDialog: () => ({ confirm: mocks.confirm, alert: mocks.alert }) }));
vi.mock('../../../../api/Music/aiPlaylistService', () => ({ aiPlaylistService: { remove: mocks.remove, regenerate: mocks.regenerate } }));

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

const renderMix = (value: GeneratedMixDetailVM, onDeleted = vi.fn(), onRegenerated = vi.fn()) => render(
    <MemoryRouter>
        <MusicMixView
            onDeleted={onDeleted}
            onRegenerated={onRegenerated}
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

    it('shows an AI playlist as a mosaic of its covers', () => {
        const { container } = renderMix(mix({ artworkUrls: ['/a.jpg', '/b.jpg', '/c.jpg', '/d.jpg'] }));

        const covers = Array.from(container.querySelectorAll('img')).map(img => img.getAttribute('src'));
        expect(covers).toEqual(expect.arrayContaining(['/a.jpg', '/b.jpg', '/c.jpg', '/d.jpg']));
        expect(covers).not.toContain('/art/cover.jpg');
    });

    it('keeps the single cover when there is nothing to make a mosaic from', () => {
        const { container } = renderMix(mix({ artworkUrls: ['/a.jpg'] }));

        const covers = Array.from(container.querySelectorAll('img')).map(img => img.getAttribute('src'));
        expect(covers).toContain('/art/cover.jpg');
    });

    it('still labels a Daily Mix by its slot', () => {
        renderMix(mix({ kind: 'DailyMix', slot: 3, descriptionTag: 'Rock' }));

        expect(screen.getByText('Daily Mix 3')).toBeInTheDocument();
    });

    it('offers Regenerate and Delete on a request, and neither on a Daily Mix', () => {
        const { unmount } = renderMix(mix({ prompt: 'Cruising in the car' }));
        expect(screen.getByRole('button', { name: 'Regenerate' })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Delete playlist' })).toBeInTheDocument();
        unmount();

        renderMix(mix({ kind: 'DailyMix' }));
        expect(screen.queryByRole('button', { name: 'Regenerate' })).toBeNull();
        expect(screen.queryByRole('button', { name: 'Delete playlist' })).toBeNull();
    });

    it('deletes only after the viewer confirms', async () => {
        mocks.confirm.mockResolvedValueOnce(false).mockResolvedValueOnce(true);
        mocks.remove.mockResolvedValue(undefined);
        const onDeleted = vi.fn();
        renderMix(mix({ prompt: 'x' }), onDeleted);

        fireEvent.click(screen.getByRole('button', { name: 'Delete playlist' }));
        await waitFor(() => expect(mocks.confirm).toHaveBeenCalledTimes(1));
        expect(mocks.remove).not.toHaveBeenCalled();

        fireEvent.click(screen.getByRole('button', { name: 'Delete playlist' }));
        await waitFor(() => expect(onDeleted).toHaveBeenCalled());
        expect(mocks.remove).toHaveBeenCalledWith('m1', undefined);
    });

    it('regenerates with the saved words and a new length', async () => {
        mocks.regenerate.mockResolvedValue({ mixId: 'm1' });
        const onRegenerated = vi.fn();
        renderMix(mix({ prompt: 'Cruising in the car' }), vi.fn(), onRegenerated);

        fireEvent.click(screen.getByRole('button', { name: 'Regenerate' }));
        expect(screen.getByLabelText('What the playlist is for')).toHaveValue('Cruising in the car');
        fireEvent.click(screen.getByRole('radio', { name: '30 songs' }));
        fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Regenerate' }));

        await waitFor(() => expect(onRegenerated).toHaveBeenCalled());
        expect(mocks.regenerate).toHaveBeenCalledWith('m1', 'Cruising in the car', 30, undefined);
    });
});
