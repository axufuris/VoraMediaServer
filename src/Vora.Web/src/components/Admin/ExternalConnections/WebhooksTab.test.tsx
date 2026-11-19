import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import WebhooksTab from './WebhooksTab';
import { formatDateTime } from '../../../utils/serverTime';
import type { LibrarySummary } from '../../../api/Media/libraryService';
import type {
    CreateWebhookConnection,
    SaveWebhookConnection,
    WebhookConnectionVM,
    WebhookConnectionWithSecret,
    WebhookDeliveryVM,
    WebhookRequestServerVM,
    WebhookTemplateVM
} from '../../../api/System/webhookAdminService';
import {
    FAKE_SECRET,
    FOCUS_FILL,
    allTemplates,
    makeConnection,
    makeDelivery,
    minutesAgo,
    requestServers,
    withSecret
} from '../../../test/webhookFixtures';

const mocks = vi.hoisted(() => {
    const alert = vi.fn();
    const confirm = vi.fn();
    return {
        getProviders: vi.fn<(serverId?: string) => Promise<WebhookTemplateVM[]>>(),
        getConnections: vi.fn<(serverId?: string) => Promise<WebhookConnectionVM[]>>(),
        getConnection: vi.fn<(id: string, serverId?: string) => Promise<WebhookConnectionWithSecret>>(),
        createConnection: vi.fn<(body: CreateWebhookConnection, serverId?: string) => Promise<WebhookConnectionWithSecret>>(),
        updateConnection: vi.fn<(id: string, body: SaveWebhookConnection, serverId?: string) => Promise<WebhookConnectionVM>>(),
        deleteConnection: vi.fn<(id: string, serverId?: string) => Promise<void>>(),
        getDeliveries: vi.fn<(limit?: number, connectionId?: string, serverId?: string) => Promise<WebhookDeliveryVM[]>>(),
        getRequestServers: vi.fn<(serverId?: string) => Promise<WebhookRequestServerVM[]>>(),
        getLibraries: vi.fn<(serverId?: string) => Promise<LibrarySummary[]>>(),
        copyText: vi.fn<(text: string) => Promise<boolean>>(),
        alert,
        confirm,
        dialog: { alert, confirm, prompt: vi.fn() }
    };
});

vi.mock('../../../api/System/webhookAdminService', () => ({
    webhookAdminService: {
        getProviders: mocks.getProviders,
        getConnections: mocks.getConnections,
        getConnection: mocks.getConnection,
        createConnection: mocks.createConnection,
        updateConnection: mocks.updateConnection,
        deleteConnection: mocks.deleteConnection,
        getDeliveries: mocks.getDeliveries,
        getRequestServers: mocks.getRequestServers
    }
}));
vi.mock('../../../api/Media/libraryService', () => ({ libraryService: { getLibraries: mocks.getLibraries } }));
vi.mock('../../../utils/copyText', () => ({ copyText: mocks.copyText }));
vi.mock('../../../dialogs', () => ({ useDialog: () => mocks.dialog }));

const connectionList = (): WebhookConnectionVM[] => [
    makeConnection({ id: 'c1', name: 'Movies 4K', lastDeliveryAt: minutesAgo(5), lastDeliveryOk: true }),
    makeConnection({
        id: 'c2', name: 'Family shows', providerId: 'sonarr_webhook', providerName: 'Sonarr', receivePath: '/api/webhooks/c2',
        lastDeliveryAt: minutesAgo(12), lastDeliveryOk: false
    }),
    makeConnection({ id: 'c3', name: 'Home grown', providerId: 'custom_webhook', providerName: 'Custom', receivePath: '/api/webhooks/c3', isEnabled: false })
];

const imported = makeDelivery({ id: 'd1', instance: '4K' });
const refused = makeDelivery({
    id: 'd2', at: '2026-11-18T10:10:00Z', connectionId: 'c2', connectionName: 'Family shows', providerId: 'sonarr_webhook',
    event: 'Test', path: '', outcome: 'Wrong secret', ok: false
});

const renderPage = (serverId?: string) => render(
    <MemoryRouter>
        <WebhooksTab serverId={serverId} />
    </MemoryRouter>
);

const tile = async (name: RegExp) => screen.findByRole('button', { name });

describe('WebhooksTab', () => {
    beforeEach(() => {
        vi.resetAllMocks();
        mocks.getProviders.mockResolvedValue(allTemplates());
        mocks.getConnections.mockImplementation(() => Promise.resolve(connectionList()));
        mocks.getConnection.mockImplementation(id => Promise.resolve(withSecret(connectionList().find(c => c.id === id) ?? makeConnection())));
        mocks.getDeliveries.mockResolvedValue([]);
        mocks.getRequestServers.mockResolvedValue(requestServers);
        mocks.getLibraries.mockResolvedValue([]);
        mocks.copyText.mockResolvedValue(true);
        mocks.confirm.mockResolvedValue(true);
        mocks.alert.mockResolvedValue(undefined);
    });

    afterEach(cleanup);

    it('explains what the tab is for, and that these webhooks come in to Vora', async () => {
        renderPage();

        expect(await screen.findByText(/These are incoming webhooks/)).toBeInTheDocument();
        expect(screen.queryByRole('heading', { level: 1 })).not.toBeInTheDocument();
        expect(screen.getByText(/imported, upgraded, renamed or deleted/)).toBeInTheDocument();
        expect(screen.getByText(/any system that can send a webhook/)).toBeInTheDocument();
        expect(screen.getByText(/real-time watching be turned off/)).toBeInTheDocument();
    });

    describe('the connection tiles', () => {
        it('draw one tile per connection with its name, template and status', async () => {
            renderPage();

            const movies = await tile(/^Movies 4K/);
            expect(within(movies).getByText('Radarr')).toBeInTheDocument();
            expect(within(movies).getByText('Ready')).toBeInTheDocument();

            const shows = await tile(/^Family shows/);
            expect(within(shows).getByText('Sonarr')).toBeInTheDocument();
            expect(within(shows).getByText('Ready')).toBeInTheDocument();

            const custom = await tile(/^Home grown/);
            expect(within(custom).getByText('Custom')).toBeInTheDocument();
            expect(within(custom).getByText('Off')).toBeInTheDocument();
        });

        it('say when the last delivery was, and whether it worked, in words', async () => {
            renderPage();

            expect(within(await tile(/^Movies 4K/)).getByText('Last: 5 min ago, ok')).toBeInTheDocument();
            expect(within(await tile(/^Family shows/)).getByText('Last: 12 min ago, failed')).toBeInTheDocument();
            expect(within(await tile(/^Home grown/)).getByText('Nothing received yet')).toBeInTheDocument();
        });

        it('wrap in a grid and end with an Add connection tile', async () => {
            renderPage();

            await tile(/^Movies 4K/);
            const list = screen.getByRole('list', { name: 'Connections' });
            expect(list.className).toContain('grid');
            expect(list.className).toContain('auto-fill');
            const items = within(list).getAllByRole('listitem');
            expect(items).toHaveLength(4);
            expect(within(items[3]).getByRole('button', { name: 'Add connection' })).toBeInTheDocument();
        });

        it('are compact', async () => {
            renderPage();

            const movies = await tile(/^Movies 4K/);
            expect(movies.className).toContain('p-3');
            expect(movies.className).toContain('min-h-[4.75rem]');
        });

        it('sit above Recent deliveries', async () => {
            renderPage();

            const movies = await tile(/^Movies 4K/);
            const deliveries = screen.getByRole('heading', { name: 'Recent deliveries' });
            expect(movies.compareDocumentPosition(deliveries) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
        });

        it('open the edit modal for the connection, loading its secret', async () => {
            renderPage();

            fireEvent.click(await tile(/^Family shows/));

            const dialog = await screen.findByRole('dialog');
            expect(within(dialog).getByRole('heading', { name: 'Edit connection' })).toBeInTheDocument();
            expect(within(dialog).getByLabelText(/^Name/)).toHaveValue('Family shows');
            await waitFor(() => expect(mocks.getConnection).toHaveBeenCalledWith('c2', undefined));
            await waitFor(() => expect(within(dialog).getByLabelText('Secret')).toHaveValue(FAKE_SECRET));
        });

        it('are reloaded after a save, and the modal closes', async () => {
            mocks.updateConnection.mockResolvedValue(connectionList()[0]);
            renderPage();
            fireEvent.click(await tile(/^Movies 4K/));
            const dialog = await screen.findByRole('dialog');
            mocks.getConnections.mockImplementation(() => Promise.resolve([
                makeConnection({ id: 'c1', name: 'Renamed movies' }), ...connectionList().slice(1)
            ]));

            fireEvent.change(within(dialog).getByLabelText(/^Name/), { target: { value: 'Renamed movies' } });
            fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }));

            expect(await tile(/^Renamed movies/)).toBeInTheDocument();
            expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
            expect(mocks.getConnections).toHaveBeenCalledTimes(2);
        });

        it('lose a connection that was deleted', async () => {
            mocks.deleteConnection.mockResolvedValue(undefined);
            renderPage();
            fireEvent.click(await tile(/^Movies 4K/));
            const dialog = await screen.findByRole('dialog');
            mocks.getConnections.mockImplementation(() => Promise.resolve(connectionList().slice(1)));

            fireEvent.click(within(dialog).getByRole('button', { name: 'Delete connection' }));

            await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
            await waitFor(() => expect(screen.queryByRole('button', { name: /^Movies 4K/ })).not.toBeInTheDocument());
            expect(mocks.deleteConnection).toHaveBeenCalledWith('c1', undefined);
        });

        it('say so when the connections cannot be loaded', async () => {
            mocks.getConnections.mockRejectedValue(new Error('down'));
            renderPage();

            await waitFor(() => expect(mocks.alert).toHaveBeenCalledWith('Failed to load the webhook connections.'));
            expect(await screen.findByRole('button', { name: 'Add connection' })).toBeInTheDocument();
        });
    });

    describe('adding a connection', () => {
        it('opens the Add connection modal from its tile', async () => {
            renderPage();

            fireEvent.click(await screen.findByRole('button', { name: 'Add connection' }));

            const dialog = await screen.findByRole('dialog');
            expect(within(dialog).getByRole('heading', { name: 'Add connection' })).toBeInTheDocument();
            expect(within(within(dialog).getByLabelText('Template')).getAllByRole('option').map(option => option.textContent))
                .toEqual(['Radarr', 'Sonarr', 'Lidarr', 'Custom']);
        });

        it('shows the new connection\'s secret, then lists it once the modal is done', async () => {
            const created = makeConnection({ id: 'c9', name: 'Radarr', receivePath: '/api/webhooks/c9' });
            mocks.createConnection.mockResolvedValue(withSecret(created));
            renderPage();
            fireEvent.click(await screen.findByRole('button', { name: 'Add connection' }));
            const dialog = await screen.findByRole('dialog');
            mocks.getConnections.mockImplementation(() => Promise.resolve([...connectionList(), created]));

            fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }));

            expect(await within(dialog).findByRole('heading', { name: 'Connection created' })).toBeInTheDocument();
            expect(within(dialog).getByLabelText('Secret')).toHaveValue(FAKE_SECRET);
            expect(await tile(/^Radarr/)).toBeInTheDocument();
            expect(screen.getByRole('dialog')).toBeInTheDocument();

            fireEvent.click(within(dialog).getByRole('button', { name: 'Done' }));
            expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        });

        it('says so when the server has no templates', async () => {
            mocks.getProviders.mockResolvedValue([]);
            renderPage();

            fireEvent.click(await screen.findByRole('button', { name: 'Add connection' }));

            await waitFor(() => expect(mocks.alert).toHaveBeenCalledWith('This server has no connection templates.'));
            expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        });
    });

    describe('recent deliveries', () => {
        it('lists what the server received, newest first, with a marker beyond colour', async () => {
            mocks.getDeliveries.mockResolvedValue([imported, refused]);
            renderPage();

            const table = await screen.findByRole('table');
            const rows = within(table).getAllByRole('row');
            expect(rows).toHaveLength(3);
            expect(within(table).getAllByRole('columnheader').map(header => header.textContent))
                .toEqual(['Time', 'Connection', 'Event', 'Path', 'Outcome']);

            const first = within(rows[1]);
            expect(first.getByRole('cell', { name: formatDateTime(imported.at) })).toBeInTheDocument();
            expect(first.getByRole('cell', { name: 'Movies 4K (4K)' })).toBeInTheDocument();
            expect(first.getByText('Download')).toBeInTheDocument();
            expect(first.getByText(imported.path)).toBeInTheDocument();
            expect(first.getByText('OK')).toBeInTheDocument();
            expect(first.getByText('Scan queued')).toBeInTheDocument();

            const second = within(rows[2]);
            expect(second.getByRole('cell', { name: 'Family shows' })).toBeInTheDocument();
            expect(second.getByText('Failed')).toBeInTheDocument();
            expect(second.getByText('Wrong secret')).toBeInTheDocument();
            expect(second.queryByText('OK')).not.toBeInTheDocument();
        });

        it('asks the server for 100, from every connection', async () => {
            renderPage();

            await waitFor(() => expect(mocks.getDeliveries).toHaveBeenCalledWith(100, undefined, undefined));
        });

        it('says so when nothing has been received', async () => {
            renderPage();

            expect(await screen.findByText('Nothing received yet. Press Test in the app, or send a sample.')).toBeInTheDocument();
            expect(screen.queryByRole('table')).not.toBeInTheDocument();
        });

        it('says the list clears when the server restarts', async () => {
            renderPage();

            expect(await screen.findByText(/The list clears when the server restarts\./)).toBeInTheDocument();
        });

        it('filters by connection, starting on All connections, and asks the server again', async () => {
            mocks.getDeliveries.mockImplementation((_limit, connectionId) => Promise.resolve(connectionId === 'c2' ? [refused] : [imported, refused]));
            renderPage();
            await screen.findByRole('table');

            const filter = screen.getByLabelText('Filter by connection');
            expect(filter).toHaveValue('');
            expect(within(filter).getAllByRole('option').map(option => option.textContent))
                .toEqual(['All connections', 'Movies 4K', 'Family shows', 'Home grown']);

            fireEvent.change(filter, { target: { value: 'c2' } });

            await waitFor(() => expect(mocks.getDeliveries).toHaveBeenCalledWith(100, 'c2', undefined));
            await waitFor(() => expect(within(screen.getByRole('table')).getAllByRole('row')).toHaveLength(2));
            expect(screen.getByLabelText('Filter by connection')).toHaveValue('c2');

            fireEvent.change(screen.getByLabelText('Filter by connection'), { target: { value: '' } });

            await waitFor(() => expect(within(screen.getByRole('table')).getAllByRole('row')).toHaveLength(3));
            expect(mocks.getDeliveries).toHaveBeenLastCalledWith(100, undefined, undefined);
        });

        it('reloads the list, and the tiles\' last delivery, when Refresh is pressed', async () => {
            mocks.getDeliveries.mockResolvedValueOnce([]).mockResolvedValue([imported]);
            renderPage();
            await screen.findByText(/Nothing received yet\. Press Test/);

            fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));

            expect(await screen.findByRole('cell', { name: 'Movies 4K (4K)' })).toBeInTheDocument();
            expect(mocks.getDeliveries).toHaveBeenCalledTimes(2);
            expect(mocks.getConnections).toHaveBeenCalledTimes(2);
            expect(screen.queryByText(/Nothing received yet\. Press Test/)).not.toBeInTheDocument();
            expect(screen.getByRole('button', { name: 'Refresh' })).toBeEnabled();
        });

        it('refreshes with the filter that is chosen', async () => {
            renderPage();
            await screen.findByText(/Nothing received yet\. Press Test/);
            fireEvent.change(screen.getByLabelText('Filter by connection'), { target: { value: 'c1' } });
            await waitFor(() => expect(mocks.getDeliveries).toHaveBeenCalledWith(100, 'c1', undefined));
            mocks.getDeliveries.mockClear();

            fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));

            await waitFor(() => expect(mocks.getDeliveries).toHaveBeenCalledWith(100, 'c1', undefined));
        });

        it('keeps the list and says so when a refresh fails', async () => {
            mocks.getDeliveries.mockResolvedValueOnce([imported]).mockRejectedValue(new Error('down'));
            renderPage();
            await screen.findByRole('cell', { name: 'Movies 4K (4K)' });

            fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));

            await waitFor(() => expect(mocks.alert).toHaveBeenCalledWith('Failed to refresh recent deliveries.'));
            expect(screen.getByRole('cell', { name: 'Movies 4K (4K)' })).toBeInTheDocument();
        });

        it('says so when the first load fails', async () => {
            mocks.getDeliveries.mockRejectedValue(new Error('down'));
            renderPage();

            await waitFor(() => expect(mocks.alert).toHaveBeenCalledWith('Failed to load recent deliveries.'));
            expect(await screen.findByText(/Nothing received yet\. Press Test/)).toBeInTheDocument();
        });

        it('scrolls sideways on a phone', async () => {
            mocks.getDeliveries.mockResolvedValue([imported]);
            renderPage();

            const table = await screen.findByRole('table');
            expect(table.className).toContain('max-md:min-w-[44rem]');
            expect(table.parentElement?.className).toContain('overflow-x-auto');
        });
    });

    it('passes the server of the multi-server route to every call', async () => {
        mocks.getDeliveries.mockResolvedValue([imported]);
        mocks.updateConnection.mockResolvedValue(connectionList()[0]);
        renderPage('s1');

        fireEvent.click(await tile(/^Movies 4K/));
        const dialog = await screen.findByRole('dialog');
        await waitFor(() => expect(mocks.getConnection).toHaveBeenCalledWith('c1', 's1'));
        fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }));
        await waitFor(() => expect(mocks.updateConnection).toHaveBeenCalledWith('c1', expect.anything(), 's1'));

        expect(mocks.getProviders).toHaveBeenCalledWith('s1');
        expect(mocks.getConnections).toHaveBeenCalledWith('s1');
        expect(mocks.getDeliveries).toHaveBeenCalledWith(100, undefined, 's1');
    });

    it('shows keyboard focus as an accent fill on every tile and button, never a ring', async () => {
        renderPage();

        const tiles = [await tile(/^Movies 4K/), await tile(/^Family shows/), await tile(/^Home grown/), screen.getByRole('button', { name: 'Add connection' })];
        for (const control of [...tiles, screen.getByRole('button', { name: 'Refresh' })]) {
            for (const cls of FOCUS_FILL) expect(control.className).toContain(cls);
        }
    });

    it('draws the connection filter as a plain dropdown like every other, with no fill that its open list would inherit', async () => {
        renderPage();
        await tile(/^Movies 4K/);

        const filter = screen.getByLabelText('Filter by connection');

        expect(filter.className).toContain('vora-input');
        expect(filter.className).not.toContain('accent');
    });

    it('marks a failed last delivery in words and in the danger colour', async () => {
        renderPage();

        const failed = within(await tile(/^Family shows/)).getByText('Last: 12 min ago, failed');
        expect(failed.className).toContain('text-[var(--vora-danger-text)]');
        expect(failed.className).toContain('font-semibold');
        const fine = within(await tile(/^Movies 4K/)).getByText('Last: 5 min ago, ok');
        expect(fine.className).not.toContain('text-[var(--vora-danger-text)]');
    });
});
