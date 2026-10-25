import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import OverlayEditor from './OverlayEditor';

const mocks = vi.hoisted(() => ({ getTemplateByMediaType: vi.fn() }));

vi.mock('../../../api/Streaming/overlayService', () => ({
    overlayService: { getTemplateByMediaType: mocks.getTemplateByMediaType, saveTemplate: vi.fn(), deleteTemplate: vi.fn(), triggerGlobalSync: vi.fn() },
}));
vi.mock('../../../dialogs', () => ({ useDialog: () => ({ confirm: () => Promise.resolve(false), alert: () => Promise.resolve() }) }));

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

    it("draws the watched box with the client's own corner, measured from the canvas", async () => {
        renderEditor();
        await waitFor(() => expect(mocks.getTemplateByMediaType).toHaveBeenCalled());

        fireEvent.click(screen.getByLabelText('Watched Checkmark'));

        const corner = screen.getByTestId('overlay-canvas').querySelector('svg path')?.closest('div[style*="container-type"]') as HTMLElement | null;
        expect(corner).not.toBeNull();
        expect(corner?.className).toContain('inset-0');
    });
});
