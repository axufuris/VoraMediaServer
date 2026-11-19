import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import SidebarV2 from './SidebarV2';

const pending = vi.hoisted(() => ({ value: 0 }));

vi.mock('../../../hooks/usePendingRequestCount', () => ({ usePendingRequestCount: () => pending.value }));
vi.mock('../../../api/System/pluginAdminService', () => ({ pluginAdminService: { getPlugins: () => Promise.resolve([]) } }));
vi.mock('./ServerVersionBadge', () => ({ default: () => null }));

const renderSidebar = () => render(<MemoryRouter><SidebarV2 /></MemoryRouter>);

describe('the admin sidebar', () => {
    beforeEach(() => { pending.value = 0; });

    it('puts a red count on Request Queue while requests are waiting', async () => {
        pending.value = 9;
        renderSidebar();

        const link = await screen.findByRole('link', { name: /Request Queue/ });

        const badge = within(link).getByLabelText('9 pending');
        expect(badge).toHaveTextContent('9');
        expect(badge.className).toContain('--vora-danger-500');
    });

    it('draws nothing beside Request Queue when none is waiting', async () => {
        renderSidebar();

        const link = await screen.findByRole('link', { name: /Request Queue/ });

        expect(within(link).queryByLabelText(/pending/)).not.toBeInTheDocument();
    });

    it('caps a long count at 99+', async () => {
        pending.value = 150;
        renderSidebar();

        const link = await screen.findByRole('link', { name: /Request Queue/ });

        expect(within(link).getByLabelText('150 pending')).toHaveTextContent('99+');
    });

    it('lists External Connections under Server, after Plugins', async () => {
        renderSidebar();

        const heading = await screen.findByRole('heading', { name: 'Server' });
        const section = within(heading.parentElement as HTMLElement);
        const link = section.getByRole('link', { name: /External Connections/ });

        expect(link).toHaveAttribute('href', '/admin/external-connections');
        const labels = section.getAllByRole('link').map(l => l.textContent ?? '');
        const plugins = labels.findIndex(text => /Plugins/.test(text));
        expect(labels[plugins + 1]).toMatch(/External Connections/);
    });

    it('has no Library Updates entry any more', async () => {
        renderSidebar();
        await screen.findByRole('link', { name: /External Connections/ });

        expect(screen.queryByRole('link', { name: /Library Updates/ })).not.toBeInTheDocument();
    });

    it('opens External Connections under the server on the multi-server form', async () => {
        render(
            <MemoryRouter initialEntries={['/admin/server/s1']}>
                <Routes>
                    <Route path="/admin/server/:serverId" element={<SidebarV2 />} />
                </Routes>
            </MemoryRouter>
        );

        expect(await screen.findByRole('link', { name: /External Connections/ })).toHaveAttribute('href', '/admin/server/s1/external-connections');
    });

    describe('highlighting External Connections', () => {
        const renderAt = (path: string, route: string) => render(
            <MemoryRouter initialEntries={[path]}>
                <Routes>
                    <Route path={route} element={<SidebarV2 />} />
                </Routes>
            </MemoryRouter>
        );

        it.each([
            ['/admin/external-connections/request-servers'],
            ['/admin/external-connections/webhooks'],
            ['/admin/external-connections'],
        ])('stays highlighted at %s', async path => {
            renderAt(path, '/admin/*');

            const link = await screen.findByRole('link', { name: /External Connections/ });

            expect(link).toHaveAttribute('aria-current', 'page');
            expect(screen.getByRole('link', { name: /System Settings/ })).not.toHaveAttribute('aria-current');
            expect(screen.getByRole('link', { name: /Plugins/ })).not.toHaveAttribute('aria-current');
        });

        it.each([
            ['/admin/server/s1/external-connections/request-servers'],
            ['/admin/server/s1/external-connections/webhooks'],
        ])('stays highlighted at %s on the multi-server form', async path => {
            renderAt(path, '/admin/server/:serverId/*');

            expect(await screen.findByRole('link', { name: /External Connections/ })).toHaveAttribute('aria-current', 'page');
        });

        it('is not highlighted on System Settings', async () => {
            renderAt('/admin/settings', '/admin/*');

            expect(await screen.findByRole('link', { name: /System Settings/ })).toHaveAttribute('aria-current', 'page');
            expect(screen.getByRole('link', { name: /External Connections/ })).not.toHaveAttribute('aria-current');
        });
    });

    it('counts on no other entry', async () => {
        pending.value = 4;
        renderSidebar();
        await screen.findByRole('link', { name: /Request Queue/ });

        expect(screen.getAllByLabelText(/pending/)).toHaveLength(1);
    });
});
