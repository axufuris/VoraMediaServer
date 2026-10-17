import { expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import ServerSwitcher from './ServerSwitcher';

vi.mock('../../../utils/serverVault', () => ({
    serverVault: {
        getServers: () => [
            { id: 'home', name: 'Home', url: 'https://home.example.com', token: 'token', profileId: 'p1', isAdmin: true },
            { id: 'cabin', name: 'Cabin', url: 'https://cabin.example.com', token: 'token', profileId: 'p2', isAdmin: true },
        ],
        getActiveServerId: () => 'home',
        setActiveServerId: vi.fn(),
    },
}));

function Where() {
    return <span data-testid="path">{useLocation().pathname}</span>;
}

const renderAt = (path: string) => render(
    <MemoryRouter initialEntries={[path]}>
        <Routes>
            <Route path="/admin" element={<><ServerSwitcher /><Where /></>} />
            <Route path="/admin/server/:serverId" element={<><ServerSwitcher /><Where /></>} />
        </Routes>
    </MemoryRouter>,
);

it('switches to the other server\'s admin pages, which live under /admin/server', () => {
    renderAt('/admin/server/home');

    fireEvent.click(screen.getByTitle('Switch server'));
    fireEvent.click(screen.getByText('Cabin'));

    expect(screen.getByTestId('path')).toHaveTextContent('/admin/server/cabin');
});

it('stays on /admin when no server is in the address', () => {
    renderAt('/admin');

    fireEvent.click(screen.getByTitle('Switch server'));
    fireEvent.click(screen.getByText('Cabin'));

    expect(screen.getByTestId('path')).toHaveTextContent(/^\/admin$/);
});
