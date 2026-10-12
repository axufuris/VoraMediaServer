import { describe, it, expect, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Link } from 'react-router-dom';
import PhoneNavDrawer from './PhoneNavDrawer';

const renderDrawer = (open: boolean, onClose = vi.fn()) => {
    render(
        <MemoryRouter>
            <PhoneNavDrawer open={open} onClose={onClose}>
                <nav>
                    <Link to="/music">Music</Link>
                    <button type="button">Edit Navigation</button>
                </nav>
            </PhoneNavDrawer>
        </MemoryRouter>,
    );
    return onClose;
};

describe('PhoneNavDrawer', () => {
    it('keeps the sidebar in the page and shows no scrim while closed', () => {
        renderDrawer(false);

        expect(screen.getByRole('link', { name: 'Music' })).toBeInTheDocument();
        expect(screen.getByTestId('phone-nav-drawer')).toHaveAttribute('data-open', 'false');
        expect(screen.queryByTestId('phone-nav-scrim')).not.toBeInTheDocument();
    });

    it('closes when a link inside it is followed', () => {
        const onClose = renderDrawer(true);

        fireEvent.click(screen.getByRole('link', { name: 'Music' }));

        expect(onClose).toHaveBeenCalledTimes(1);
    });

    it('stays open for controls that are not links', () => {
        const onClose = renderDrawer(true);

        fireEvent.click(screen.getByRole('button', { name: 'Edit Navigation' }));

        expect(onClose).not.toHaveBeenCalled();
    });

    it('closes from the scrim and from Escape', () => {
        const onClose = renderDrawer(true);

        fireEvent.click(screen.getByTestId('phone-nav-scrim'));
        fireEvent.keyDown(window, { key: 'Escape' });

        expect(onClose).toHaveBeenCalledTimes(2);
    });

    it('ignores Escape and link clicks while closed', () => {
        const onClose = renderDrawer(false);

        fireEvent.keyDown(window, { key: 'Escape' });
        fireEvent.click(screen.getByRole('link', { name: 'Music' }));

        expect(onClose).not.toHaveBeenCalled();
    });
});
