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

    it("draws the watched box with the client's own corner, measured from the canvas", async () => {
        renderEditor();
        await waitFor(() => expect(mocks.getTemplateByMediaType).toHaveBeenCalled());

        fireEvent.click(screen.getByLabelText('Watched Checkmark'));

        const corner = screen.getByTestId('overlay-canvas').querySelector('svg path')?.closest('div[style*="container-type"]') as HTMLElement | null;
        expect(corner).not.toBeNull();
        expect(corner?.className).toContain('inset-0');
    });
});
