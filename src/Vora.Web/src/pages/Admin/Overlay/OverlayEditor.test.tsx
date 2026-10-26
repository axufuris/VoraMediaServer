import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import OverlayEditor from './OverlayEditor';

const mocks = vi.hoisted(() => ({ getTemplateByMediaType: vi.fn(), saveTemplate: vi.fn(), triggerGlobalSync: vi.fn(), alert: vi.fn(), confirm: vi.fn() }));

vi.mock('../../../api/Streaming/overlayService', () => ({
    overlayService: { getTemplateByMediaType: mocks.getTemplateByMediaType, saveTemplate: mocks.saveTemplate, deleteTemplate: vi.fn(), triggerGlobalSync: mocks.triggerGlobalSync },
}));
vi.mock('../../../dialogs', () => ({ useDialog: () => ({ confirm: mocks.confirm, alert: mocks.alert }) }));

class NoopResizeObserver {
    observe() {}
    unobserve() {}
    disconnect() {}
}

const renderEditor = () => render(<MemoryRouter><OverlayEditor /></MemoryRouter>);

describe('the overlay editor', () => {
    beforeEach(() => {
        mocks.getTemplateByMediaType.mockReset();
        mocks.getTemplateByMediaType.mockResolvedValue(null);
        mocks.saveTemplate.mockReset().mockResolvedValue(undefined);
        mocks.triggerGlobalSync.mockReset().mockResolvedValue(undefined);
        mocks.alert.mockReset().mockResolvedValue(undefined);
        mocks.confirm.mockReset().mockResolvedValue(false);
        vi.stubGlobal('ResizeObserver', NoopResizeObserver);
    });

    it('previews the poster at the sizes the clients draw it', async () => {
        renderEditor();
        await waitFor(() => expect(mocks.getTemplateByMediaType).toHaveBeenCalled());
        const canvas = screen.getByTestId('overlay-canvas');

        expect(screen.getByRole('button', { name: 'Large' })).toHaveAttribute('aria-pressed', 'true');
        expect(canvas.style.width).toBe('');

        fireEvent.click(screen.getByRole('button', { name: 'Small' }));
        expect(canvas.style.width).toBe('104px');
        expect(screen.getByText(/TV poster or a phone card/)).toBeInTheDocument();

        fireEvent.click(screen.getByRole('button', { name: 'Card' }));
        expect(canvas.style.width).toBe('192px');
        expect(screen.getByRole('button', { name: 'Card' })).toHaveAttribute('aria-pressed', 'true');
    });

    it('opens with the corner box a viewer would see, so badges are placed clear of it', async () => {
        renderEditor();
        await waitFor(() => expect(mocks.getTemplateByMediaType).toHaveBeenCalled());
        const canvas = screen.getByTestId('overlay-canvas');

        expect(screen.getByLabelText('Watched Checkmark')).toBeChecked();
        expect(canvas.querySelector('svg path')).not.toBeNull();

        fireEvent.change(screen.getByDisplayValue('Movie Poster (2:3)'), { target: { value: 'Season' } });

        expect(screen.getByLabelText('Unplayed Count')).toBeChecked();
        expect(canvas).toHaveTextContent('3');

        fireEvent.change(screen.getByDisplayValue('Season Poster (2:3)'), { target: { value: 'Episode' } });

        expect(screen.getByLabelText('Watched Checkmark')).toBeChecked();
        expect(canvas).toHaveTextContent('E1');
    });

    it('draws the episode canvas box at three quarters, as a still card does', async () => {
        renderEditor();
        await waitFor(() => expect(mocks.getTemplateByMediaType).toHaveBeenCalled());
        const corner = () => screen.getByTestId('overlay-canvas').querySelector('svg path')?.closest('div[style*="container-type"]') as HTMLElement | null;

        expect(corner()?.style.left).toBe('');

        fireEvent.change(screen.getByDisplayValue('Movie Poster (2:3)'), { target: { value: 'Episode' } });

        expect(corner()?.style.left).toBe('25%');
    });

    it("draws the watched box with the client's own corner, measured from the canvas", async () => {
        renderEditor();
        await waitFor(() => expect(mocks.getTemplateByMediaType).toHaveBeenCalled());

        fireEvent.click(screen.getByLabelText('Watched Checkmark'));

        const corner = screen.getByTestId('overlay-canvas').querySelector('svg path')?.closest('div[style*="container-type"]') as HTMLElement | null;
        expect(corner).not.toBeNull();
        expect(corner?.className).toContain('inset-0');
    });

    describe('copying a layout to the other portrait posters', () => {
        const layout = JSON.stringify([{ type: 'composite_ratings', xPct: 0.8, yPct: 0.5, widthPct: 0.15, heightPct: 0.35 }]);

        beforeEach(() => {
            mocks.getTemplateByMediaType.mockImplementation((type: string) =>
                Promise.resolve(type === 'Movie' ? { id: 'movie-template', targetMediaType: 'Movie', configurationJson: layout } : null));
        });

        const copiedTo = () => mocks.saveTemplate.mock.calls.map(([payload]: [{ targetMediaType: string; configurationJson: string }]) => payload);

        it('offers the other portrait canvases, all picked, and saves the same badges to each', async () => {
            renderEditor();
            fireEvent.click(await screen.findByRole('button', { name: 'Copy layout…' }));

            const dialog = screen.getByRole('dialog');
            expect(within(dialog).getByText('Copy layout to portrait posters')).toBeInTheDocument();
            expect(within(dialog).getByLabelText('TV show posters')).toBeChecked();
            expect(within(dialog).getByLabelText('Season posters')).toBeChecked();
            expect(within(dialog).queryByLabelText('Movie posters')).toBeNull();
            expect(within(dialog).queryByText(/Episode thumbnails are landscape/)).toBeInTheDocument();

            fireEvent.click(within(dialog).getByRole('button', { name: 'Copy layout' }));

            await waitFor(() => expect(mocks.alert).toHaveBeenCalledWith('Layout copied to TV show posters and season posters.'));
            const saved = copiedTo();
            expect(saved.map(payload => payload.targetMediaType)).toEqual(['Movie', 'TvShow', 'Season']);
            expect(new Set(saved.map(payload => payload.configurationJson)).size).toBe(1);
            expect(JSON.parse(saved[1].configurationJson)).toEqual(JSON.parse(layout));
            expect(mocks.triggerGlobalSync).not.toHaveBeenCalled();
        });

        it('copies only to the posters left picked, and updates the library when asked', async () => {
            mocks.confirm.mockResolvedValue(true);
            renderEditor();
            fireEvent.click(await screen.findByRole('button', { name: 'Copy layout…' }));
            const dialog = screen.getByRole('dialog');

            fireEvent.click(within(dialog).getByLabelText('Season posters'));
            fireEvent.click(within(dialog).getByRole('button', { name: 'Copy layout' }));

            await waitFor(() => expect(mocks.triggerGlobalSync).toHaveBeenCalled());
            expect(copiedTo().map(payload => payload.targetMediaType)).toEqual(['Movie', 'TvShow']);
        });

        it('cannot copy with nothing picked', async () => {
            renderEditor();
            fireEvent.click(await screen.findByRole('button', { name: 'Copy layout…' }));
            const dialog = screen.getByRole('dialog');

            fireEvent.click(within(dialog).getByLabelText('TV show posters'));
            fireEvent.click(within(dialog).getByLabelText('Season posters'));

            expect(within(dialog).getByRole('button', { name: 'Copy layout' })).toBeDisabled();
        });

        it('is not offered on the landscape episode canvas', async () => {
            mocks.getTemplateByMediaType.mockResolvedValue({ id: 'episode-template', targetMediaType: 'Episode', configurationJson: layout });
            renderEditor();
            await screen.findByRole('button', { name: 'Copy layout…' });

            fireEvent.change(screen.getByDisplayValue('Movie Poster (2:3)'), { target: { value: 'Episode' } });

            await waitFor(() => expect(screen.queryByRole('button', { name: 'Copy layout…' })).toBeNull());
        });
    });
});

