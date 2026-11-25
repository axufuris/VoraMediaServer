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

describe('the admin breadcrumb for a page about one profile', () => {
    const profileId = '0b8f5b2a-4c1d-4e0a-9f3e-6d2c7a1b8e90';

    it('reads Admin, Users & Access, Profile, Client Settings, without a link for the id', () => {
        renderAt(`/admin/users/${profileId}/client-settings`);

        const trail = within(screen.getByRole('navigation', { name: 'Breadcrumb' }));
        expect(trail.getByRole('link', { name: 'Users & Access' })).toHaveAttribute('href', '/admin/users');
        expect(trail.getByText('Profile')).toBeInTheDocument();
        expect(trail.queryByRole('link', { name: 'Profile' })).not.toBeInTheDocument();
        expect(trail.getByText('Client Settings')).toBeInTheDocument();
        expect(trail.queryByText(profileId)).not.toBeInTheDocument();
    });

    it('keeps the multi-server form out of the trail', () => {
        renderAt(`/admin/server/s1/users/${profileId}/client-settings`, '/admin/server/:serverId/*');

        const trail = within(screen.getByRole('navigation', { name: 'Breadcrumb' }));
        expect(trail.getByRole('link', { name: 'Users & Access' })).toHaveAttribute('href', '/admin/server/s1/users');
        expect(trail.getByText('Client Settings')).toBeInTheDocument();
    });
});
