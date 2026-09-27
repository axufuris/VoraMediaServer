import { describe, it, expect, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import RateDialog from './RateDialog';

describe('RateDialog', () => {
    it('saves nothing until Save, then saves the picked rating', async () => {
        const onSave = vi.fn();
        const onClose = vi.fn();
        render(<RateDialog title="Idiots" value={null} onSave={onSave} onClose={onClose} />);

        expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
        expect(screen.queryByRole('button', { name: 'Clear rating' })).toBeNull();
        fireEvent.click(screen.getByRole('button', { name: 'Rate 8 of 10' }));
        expect(onSave).not.toHaveBeenCalled();
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));

        await waitFor(() => expect(onClose).toHaveBeenCalled());
        expect(onSave).toHaveBeenCalledWith(8);
    });

    it('clears an existing rating', async () => {
        const onSave = vi.fn();
        render(<RateDialog title="Idiots" value={8} onSave={onSave} onClose={vi.fn()} />);

        fireEvent.click(screen.getByRole('button', { name: 'Clear rating' }));

        await waitFor(() => expect(onSave).toHaveBeenCalledWith(null));
    });
});
