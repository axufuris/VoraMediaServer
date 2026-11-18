import { describe, it, expect, afterEach } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import LinkDeviceCard from './LinkDeviceCard';

describe('LinkDeviceCard', () => {
    afterEach(cleanup);

    it('offers to link a TV or app and opens the code page', async () => {
        const user = userEvent.setup();
        render(
            <MemoryRouter initialEntries={['/account']}>
                <Routes>
                    <Route path="/account" element={<LinkDeviceCard />} />
                    <Route path="/link" element={<h1>Link page</h1>} />
                </Routes>
            </MemoryRouter>,
        );

        expect(screen.getByRole('heading', { name: 'Link a TV or app' })).toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: 'Enter a code' }));

        expect(await screen.findByRole('heading', { name: 'Link page' })).toBeInTheDocument();
    });
});
