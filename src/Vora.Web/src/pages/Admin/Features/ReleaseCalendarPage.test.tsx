import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import ReleaseCalendarPage from './ReleaseCalendarPage';

vi.mock('../../../components/Admin/Features/FeatureToggle', () => ({ default: () => null }));

const renderPage = (path: string) => render(
    <MemoryRouter initialEntries={[path]}>
        <Routes>
            <Route path="/admin/release-calendar" element={<ReleaseCalendarPage />} />
            <Route path="/admin/server/:serverId/release-calendar" element={<ReleaseCalendarPage />} />
        </Routes>
    </MemoryRouter>
);

describe('ReleaseCalendarPage', () => {
    it('sends the admin to the Request Servers tab of External Connections for the Radarr and Sonarr credentials', () => {
        renderPage('/admin/release-calendar');

        const link = screen.getByRole('link', { name: 'External Connections → Request Servers' });

        expect(link).toHaveAttribute('href', '/admin/external-connections/request-servers');
        expect(screen.queryByRole('link', { name: /System Settings/ })).not.toBeInTheDocument();
    });

    it('keeps the server in that link on the multi-server form', () => {
        renderPage('/admin/server/s1/release-calendar');

        expect(screen.getByRole('link', { name: 'External Connections → Request Servers' })).toHaveAttribute('href', '/admin/server/s1/external-connections/request-servers');
    });

    it('still links to Plugins for the individual calendar sources', () => {
        renderPage('/admin/server/s1/release-calendar');

        expect(screen.getByRole('link', { name: 'Plugins' })).toHaveAttribute('href', '/admin/server/s1/plugins');
    });
});
