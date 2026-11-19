import { describe, it, expect, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import Breadcrumb from './Breadcrumb';

vi.mock('../../../api/Media/libraryService', () => ({ libraryService: { getLibraryById: () => Promise.resolve(null) } }));

const renderAt = (path: string, route = '/admin/*') => render(
    <MemoryRouter initialEntries={[path]}>
        <Routes>
            <Route path={route} element={<Breadcrumb />} />
        </Routes>
    </MemoryRouter>
);

describe('the admin breadcrumb for External Connections', () => {
    it('reads Admin, External Connections, Request Servers on the Request Servers tab', () => {
        renderAt('/admin/external-connections/request-servers');

        const trail = within(screen.getByRole('navigation', { name: 'Breadcrumb' }));
        expect(trail.getByRole('link', { name: 'Admin' })).toHaveAttribute('href', '/admin');
        expect(trail.getByRole('link', { name: 'External Connections' })).toHaveAttribute('href', '/admin/external-connections');
        expect(trail.getByText('Request Servers')).toBeInTheDocument();
        expect(trail.queryByRole('link', { name: 'Request Servers' })).not.toBeInTheDocument();
    });

    it('ends on Webhooks on the Webhooks tab', () => {
        renderAt('/admin/external-connections/webhooks');

        const trail = within(screen.getByRole('navigation', { name: 'Breadcrumb' }));
        expect(trail.getByRole('link', { name: 'External Connections' })).toBeInTheDocument();
        expect(trail.getByText('Webhooks')).toBeInTheDocument();
    });

    it('leaves the server out of the trail on the multi-server form', () => {
        renderAt('/admin/server/s1/external-connections/webhooks', '/admin/server/:serverId/*');

        const trail = within(screen.getByRole('navigation', { name: 'Breadcrumb' }));
        expect(trail.getByRole('link', { name: 'External Connections' })).toHaveAttribute('href', '/admin/server/s1/external-connections');
        expect(trail.getByText('Webhooks')).toBeInTheDocument();
        expect(trail.queryByText('S1')).not.toBeInTheDocument();
    });
});
