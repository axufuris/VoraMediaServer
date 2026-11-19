import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { RouterProvider, createMemoryRouter, useLocation } from 'react-router-dom';
import ExternalConnectionsPage from './ExternalConnectionsPage';
import type { RequestServerVM } from '../../api/Discovery/requestAdminService';
import type { WebhookConnectionVM, WebhookDeliveryVM, WebhookTemplateVM } from '../../api/System/webhookAdminService';
import { allTemplates, makeConnection } from '../../test/webhookFixtures';

const mocks = vi.hoisted(() => {
    const alert = vi.fn();
    return {
        getServers: vi.fn<(serverId?: string) => Promise<RequestServerVM[]>>(),
        getProviders: vi.fn<(serverId?: string) => Promise<WebhookTemplateVM[]>>(),
        getConnections: vi.fn<(serverId?: string) => Promise<WebhookConnectionVM[]>>(),
        getDeliveries: vi.fn<(limit?: number, connectionId?: string, serverId?: string) => Promise<WebhookDeliveryVM[]>>(),
        alert,
        dialog: { alert, confirm: vi.fn(), prompt: vi.fn() },
    };
});

vi.mock('../../api/Discovery/requestAdminService', () => ({
    requestAdminService: { getServers: mocks.getServers, getProviderOptions: vi.fn(), saveServer: vi.fn(), deleteServer: vi.fn() },
}));
vi.mock('../../api/System/webhookAdminService', () => ({
    webhookAdminService: { getProviders: mocks.getProviders, getConnections: mocks.getConnections, getDeliveries: mocks.getDeliveries },
}));
vi.mock('../../dialogs', () => ({ useDialog: () => mocks.dialog }));

const Where = () => <div data-testid="where">{useLocation().pathname}</div>;

const HubWithLocation = () => (
    <>
        <ExternalConnectionsPage />
        <Where />
    </>
);

const renderHub = (path: string) => {
    const router = createMemoryRouter(
        [
            { path: '/admin/external-connections/:tab?', element: <HubWithLocation /> },
            { path: '/admin/server/:serverId/external-connections/:tab?', element: <HubWithLocation /> },
        ],
        { initialEntries: [path] }
    );
    render(<RouterProvider router={router} />);
    return router;
};

const tabButton = (name: string) => screen.getByRole('button', { name });
const isActive = (name: string) => tabButton(name).className.includes('border-[var(--vora-accent-500)]');
const location = () => screen.getByTestId('where').textContent;

const server: RequestServerVM = {
    id: 'r1', name: 'Movies Radarr', providerId: 'radarr_requester', mediaType: 'Movie', hostname: 'radarr.local', port: 7878, useSsl: false,
    apiKey: 'k', urlBase: '', isDefault: true, is4K: false, isEnabled: true, providesReleaseCalendar: false, providerSettingsJson: '{}',
};

describe('ExternalConnectionsPage', () => {
    beforeEach(() => {
        vi.resetAllMocks();
        mocks.getServers.mockResolvedValue([server]);
        mocks.getProviders.mockResolvedValue(allTemplates());
        mocks.getConnections.mockResolvedValue([makeConnection({ id: 'c1', name: 'Movies 4K' })]);
        mocks.getDeliveries.mockResolvedValue([]);
        mocks.alert.mockResolvedValue(undefined);
    });

    afterEach(cleanup);

    it('is one page with a Request Servers tab and a Webhooks tab', async () => {
        renderHub('/admin/external-connections/request-servers');

        expect(screen.getByRole('heading', { level: 1, name: 'External Connections' })).toBeInTheDocument();
        expect(screen.getByText(/The services Vora talks to/)).toBeInTheDocument();
        expect(tabButton('Request Servers')).toBeInTheDocument();
        expect(tabButton('Webhooks')).toBeInTheDocument();
        expect(screen.getAllByRole('button', { name: /^(Request Servers|Webhooks)$/ })).toHaveLength(2);
        expect(await screen.findByText('Movies Radarr')).toBeInTheDocument();
    });

    it('opens on Request Servers when no tab is given, by replacing the address', async () => {
        const router = renderHub('/admin/external-connections');

        await waitFor(() => expect(location()).toBe('/admin/external-connections/request-servers'));
        expect(router.state.historyAction).toBe('REPLACE');
        expect(isActive('Request Servers')).toBe(true);
        expect(isActive('Webhooks')).toBe(false);
        expect(await screen.findByText('Movies Radarr')).toBeInTheDocument();
        expect(screen.queryByText(/These are incoming webhooks/)).not.toBeInTheDocument();
    });

    it('opens on Request Servers for a tab it does not know, by replacing the address', async () => {
        const router = renderHub('/admin/external-connections/library-updates');

        await waitFor(() => expect(location()).toBe('/admin/external-connections/request-servers'));
        expect(router.state.historyAction).toBe('REPLACE');
        expect(isActive('Request Servers')).toBe(true);
        expect(await screen.findByText('Movies Radarr')).toBeInTheDocument();
    });

    it('follows the address to the Webhooks tab', async () => {
        renderHub('/admin/external-connections/webhooks');

        expect(await screen.findByText(/These are incoming webhooks/)).toBeInTheDocument();
        expect(isActive('Webhooks')).toBe(true);
        expect(isActive('Request Servers')).toBe(false);
        expect(screen.queryByText('Movies Radarr')).not.toBeInTheDocument();
        expect(await screen.findByRole('button', { name: /^Movies 4K/ })).toBeInTheDocument();
        expect(location()).toBe('/admin/external-connections/webhooks');
    });

    it('changes the address when a tab is clicked, and back again', async () => {
        const router = renderHub('/admin/external-connections/request-servers');
        await screen.findByText('Movies Radarr');

        fireEvent.click(tabButton('Webhooks'));

        expect(location()).toBe('/admin/external-connections/webhooks');
        expect(await screen.findByText(/These are incoming webhooks/)).toBeInTheDocument();
        expect(isActive('Webhooks')).toBe(true);
        expect(router.state.historyAction).toBe('PUSH');

        fireEvent.click(tabButton('Request Servers'));

        expect(location()).toBe('/admin/external-connections/request-servers');
        expect(await screen.findByText('Movies Radarr')).toBeInTheDocument();
        expect(screen.queryByText(/These are incoming webhooks/)).not.toBeInTheDocument();
        expect(isActive('Request Servers')).toBe(true);
    });

    it('lets the Back button return to the tab that was open', async () => {
        const router = renderHub('/admin/external-connections/request-servers');
        await screen.findByText('Movies Radarr');
        fireEvent.click(tabButton('Webhooks'));
        await screen.findByText(/These are incoming webhooks/);

        await router.navigate(-1);

        await waitFor(() => expect(location()).toBe('/admin/external-connections/request-servers'));
        expect(await screen.findByText('Movies Radarr')).toBeInTheDocument();
    });

    describe('on the multi-server form', () => {
        it('keeps the server in the address when it picks the default tab', async () => {
            const router = renderHub('/admin/server/s1/external-connections');

            await waitFor(() => expect(location()).toBe('/admin/server/s1/external-connections/request-servers'));
            expect(router.state.historyAction).toBe('REPLACE');
            expect(await screen.findByText('Movies Radarr')).toBeInTheDocument();
        });

        it('keeps the server in the address for a tab it does not know', async () => {
            renderHub('/admin/server/s1/external-connections/nope');

            await waitFor(() => expect(location()).toBe('/admin/server/s1/external-connections/request-servers'));
        });

        it('keeps the server in the address when a tab is clicked, and asks that server for everything', async () => {
            renderHub('/admin/server/s1/external-connections/request-servers');
            await screen.findByText('Movies Radarr');
            expect(mocks.getServers).toHaveBeenCalledWith('s1');

            fireEvent.click(tabButton('Webhooks'));

            expect(location()).toBe('/admin/server/s1/external-connections/webhooks');
            await screen.findByText(/These are incoming webhooks/);
            await waitFor(() => expect(mocks.getConnections).toHaveBeenCalledWith('s1'));
            expect(mocks.getProviders).toHaveBeenCalledWith('s1');
            expect(mocks.getDeliveries).toHaveBeenCalledWith(100, undefined, 's1');
        });
    });

    it('reports a Request Servers failure in the same notice System Settings used', async () => {
        mocks.getServers.mockRejectedValue(new Error('down'));
        renderHub('/admin/external-connections/request-servers');

        expect(await screen.findByRole('heading', { name: 'Error' })).toBeInTheDocument();
        expect(screen.getByText('Failed to load request servers.')).toBeInTheDocument();

        fireEvent.click(within(screen.getByRole('heading', { name: 'Error' }).parentElement as HTMLElement).getByRole('button', { name: 'OK' }));

        await waitFor(() => expect(screen.queryByRole('heading', { name: 'Error' })).not.toBeInTheDocument());
    });
});
