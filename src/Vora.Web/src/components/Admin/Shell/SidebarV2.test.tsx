import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
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

    it('counts on no other entry', async () => {
        pending.value = 4;
        renderSidebar();
        await screen.findByRole('link', { name: /Request Queue/ });

        expect(screen.getAllByLabelText(/pending/)).toHaveLength(1);
    });
});
