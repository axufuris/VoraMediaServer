import { describe, it, expect, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import LocalFilesFields from './LocalFilesFields';

const renderFields = (mode = 0, preferLocalArtwork = false) => {
    const onModeChange = vi.fn<(mode: number) => void>();
    const onPreferLocalArtworkChange = vi.fn<(prefer: boolean) => void>();
    render(
        <LocalFilesFields
            mode={mode}
            preferLocalArtwork={preferLocalArtwork}
            onModeChange={onModeChange}
            onPreferLocalArtworkChange={onPreferLocalArtworkChange}
        />,
    );
    return { onModeChange, onPreferLocalArtworkChange };
};

describe('LocalFilesFields', () => {
    it('offers the three ways to read NFO files, with nothing read until asked', () => {
        renderFields();

        const select = screen.getByLabelText('Read NFO files');
        expect(select).toHaveValue('0');
        expect(Array.from(select.querySelectorAll('option')).map(option => option.textContent))
            .toEqual(['Off', 'For matching only', 'Prefer NFO details']);
        expect(screen.getByText(/NFO files are ignored/)).toBeInTheDocument();
        expect(screen.getByLabelText('Prefer local artwork')).not.toBeChecked();
    });

    it('says what matching only does, and that a manual match is kept', () => {
        renderFields(1);

        expect(screen.getByText(/Every detail still comes from the metadata provider/)).toBeInTheDocument();
        expect(screen.getByText(/A match you set with Fix match is never changed by an NFO/)).toBeInTheDocument();
    });

    it('says the provider still fills in what an NFO leaves out', () => {
        renderFields(2);

        expect(screen.getByText(/The metadata provider fills in whatever the NFO leaves out/)).toBeInTheDocument();
    });

    it('reports the choice as the number the server stores', () => {
        const { onModeChange } = renderFields();

        fireEvent.change(screen.getByLabelText('Read NFO files'), { target: { value: '2' } });

        expect(onModeChange).toHaveBeenCalledWith(2);
    });

    it('reports the artwork preference, and says the images are not copied', () => {
        const { onPreferLocalArtworkChange } = renderFields();

        fireEvent.click(screen.getByLabelText('Prefer local artwork'));

        expect(onPreferLocalArtworkChange).toHaveBeenCalledWith(true);
        expect(screen.getByText(/does not copy them/)).toBeInTheDocument();
    });

    it('shows a value it does not know as off', () => {
        renderFields(9);

        expect(screen.getByLabelText('Read NFO files')).toHaveValue('0');
    });
});
