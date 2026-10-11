import { describe, it, expect, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import FolderPathInput from './FolderPathInput';

vi.mock('./FolderBrowserModal', () => ({
    default: ({ isOpen, onSelect, onClose }: { isOpen: boolean; onSelect: (path: string) => void; onClose: () => void }) =>
        isOpen ? <button type="button" onClick={() => { onSelect('/media/movies'); onClose(); }}>Select /media/movies</button> : null,
}));

const pickMovies = () => {
    fireEvent.click(screen.getByRole('button', { name: 'Browse' }));
    fireEvent.click(screen.getByRole('button', { name: 'Select /media/movies' }));
};

describe('FolderPathInput', () => {
    it('puts the picked folder in the box by default', () => {
        const onChange = vi.fn();
        render(<FolderPathInput value="" onChange={onChange} />);

        pickMovies();

        expect(onChange).toHaveBeenCalledWith('/media/movies');
    });

    it('hands the picked folder over instead when the form takes it directly', () => {
        const onChange = vi.fn();
        const onBrowseSelect = vi.fn();
        render(<FolderPathInput value="" onChange={onChange} onBrowseSelect={onBrowseSelect} />);

        pickMovies();

        expect(onBrowseSelect).toHaveBeenCalledWith('/media/movies');
        expect(onChange).not.toHaveBeenCalled();
        expect(screen.queryByRole('button', { name: 'Select /media/movies' })).not.toBeInTheDocument();
    });
});
