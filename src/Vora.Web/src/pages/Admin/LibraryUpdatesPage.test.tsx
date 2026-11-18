import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { useEffect } from 'react';
import LibraryUpdatesPage from './LibraryUpdatesPage';
import { formatDateTime } from '../../utils/serverTime';
import type { GeneratedWebhookSecret, WebhookDeliveryVM, WebhookProviderVM } from '../../api/System/webhookAdminService';

const mocks = vi.hoisted(() => {
    const alert = vi.fn();
    const confirm = vi.fn();
    return {
        getProviders: vi.fn<(serverId?: string) => Promise<WebhookProviderVM[]>>(),
        getDeliveries: vi.fn<(limit?: number, serverId?: string) => Promise<WebhookDeliveryVM[]>>(),
        generateSecret: vi.fn<(providerId: string, serverId?: string) => Promise<GeneratedWebhookSecret>>(),
        copyText: vi.fn<(text: string) => Promise<boolean>>(),
        alert,
        confirm,
        dialog: { alert, confirm, prompt: vi.fn() },
        formMounts: {} as Record<string, number>,
        formServers: {} as Record<string, string | undefined>,
    };
});

vi.mock('../../api/System/webhookAdminService', () => ({
    webhookAdminService: {
        getProviders: mocks.getProviders,
        getDeliveries: mocks.getDeliveries,
        generateSecret: mocks.generateSecret,
    },
}));
vi.mock('../../utils/copyText', () => ({ copyText: mocks.copyText }));
vi.mock('../../dialogs', () => ({ useDialog: () => mocks.dialog }));
vi.mock('../../components/Admin/Settings/PluginSettingsForm', () => ({
    default: function PluginSettingsFormStub({ pluginId, serverId }: { pluginId: string; serverId?: string }) {
        useEffect(() => {
            mocks.formMounts[pluginId] = (mocks.formMounts[pluginId] ?? 0) + 1;
            mocks.formServers[pluginId] = serverId;
        }, [pluginId, serverId]);
        return <div data-testid={`form-${pluginId}`}>Settings form for {pluginId}</div>;
    },
}));

const radarr: WebhookProviderVM = {
    id: 'radarr_webhook', name: 'Radarr Webhook', description: 'Updates movie libraries when Radarr changes a file.',
    libraryKind: 'Movie', receivePath: '/api/webhooks/radarr_webhook', isConfigured: true, isEnabled: true,
};
const sonarr: WebhookProviderVM = {
    id: 'sonarr_webhook', name: 'Sonarr Webhook', description: 'Updates TV libraries when Sonarr changes a file.',
    libraryKind: 'TvShow', receivePath: '/api/webhooks/sonarr_webhook', isConfigured: false, isEnabled: true,
};
const lidarr: WebhookProviderVM = {
    id: 'lidarr_webhook', name: 'Lidarr Webhook', description: 'Updates music libraries when Lidarr changes a file.',
    libraryKind: 'Music', receivePath: '/api/webhooks/lidarr_webhook', isConfigured: true, isEnabled: false,
};

const imported: WebhookDeliveryVM = {
    id: 'd1', at: '2026-11-18T10:15:00Z', providerId: 'radarr_webhook', instance: '4K', event: 'Download',
    path: '/movies/Heat (1995)/Heat (1995).mkv', outcome: 'Scan queued', ok: true,
};
const refused: WebhookDeliveryVM = {
    id: 'd2', at: '2026-11-18T10:10:00Z', providerId: 'sonarr_webhook', instance: null, event: 'Test',
    path: '', outcome: 'Wrong secret', ok: false,
};

const FOCUS_FILL = [
    'focus-visible:bg-[var(--vora-accent-500)]!',
    'focus-visible:text-[var(--vora-accent-contrast)]!',
    'focus-visible:outline-none!',
];

const renderPage = (path = '/admin/library-updates') => render(
    <MemoryRouter initialEntries={[path]}>
        <Routes>
            <Route path="/admin/library-updates" element={<LibraryUpdatesPage />} />
            <Route path="/admin/server/:serverId/library-updates" element={<LibraryUpdatesPage />} />
        </Routes>
    </MemoryRouter>
);

const cardFor = async (name: string) => screen.findByRole('article', { name });

describe('LibraryUpdatesPage', () => {
    beforeEach(() => {
        mocks.getProviders.mockReset();
        mocks.getDeliveries.mockReset();
        mocks.generateSecret.mockReset();
        mocks.copyText.mockReset();
        mocks.alert.mockReset();
        mocks.confirm.mockReset();
        mocks.formMounts = {};
        mocks.formServers = {};
        mocks.getProviders.mockResolvedValue([radarr, sonarr, lidarr]);
        mocks.getDeliveries.mockResolvedValue([]);
        mocks.copyText.mockResolvedValue(true);
        mocks.confirm.mockResolvedValue(true);
        mocks.alert.mockResolvedValue(undefined);
    });

    afterEach(cleanup);

    it('explains what the page is for', async () => {
        renderPage();

        expect(await screen.findByRole('heading', { name: 'Library Updates' })).toBeInTheDocument();
        expect(screen.getByText(/imported, upgraded, renamed or deleted/)).toBeInTheDocument();
        expect(screen.getByText(/real-time watching be turned off/)).toBeInTheDocument();
    });

    it('draws a card per provider, titled without the word Webhook, with its description', async () => {
        renderPage();

        for (const [title, description] of [
            ['Radarr', radarr.description],
            ['Sonarr', sonarr.description],
            ['Lidarr', lidarr.description],
        ]) {
            const card = await cardFor(title);
            expect(within(card).getByRole('heading', { name: title })).toBeInTheDocument();
            expect(within(card).getByText(description)).toBeInTheDocument();
        }
        expect(screen.queryByText('Radarr Webhook')).not.toBeInTheDocument();
    });

    it('marks each card Ready, Needs a secret or Off', async () => {
        renderPage();

        expect(within(await cardFor('Radarr')).getByText('Ready')).toBeInTheDocument();
        expect(within(await cardFor('Sonarr')).getByText('Needs a secret')).toBeInTheDocument();
        expect(within(await cardFor('Lidarr')).getByText('Off')).toBeInTheDocument();
    });

    it('says an Off provider can be turned on under Plugins, and only that one', async () => {
        renderPage();

        expect(within(await cardFor('Lidarr')).getByText('This connection is off. Turn it on under Plugins.')).toBeInTheDocument();
        expect(within(await cardFor('Radarr')).queryByText(/Turn it on under Plugins/)).not.toBeInTheDocument();
        expect(within(await cardFor('Sonarr')).queryByText(/Turn it on under Plugins/)).not.toBeInTheDocument();
    });

    it('shows the address to paste, built from this page\'s origin and the receive path', async () => {
        renderPage();

        const card = await cardFor('Radarr');
        const field = within(card).getByLabelText('Address to paste into Radarr');
        expect(field).toHaveValue(`${window.location.origin}/api/webhooks/radarr_webhook`);
        expect(field).toHaveAttribute('readonly');
        expect(within(card).getByText(/container name and port, not localhost/)).toBeInTheDocument();
    });

    it('lists the setup steps for the app', async () => {
        renderPage();

        const steps = within(await cardFor('Sonarr')).getAllByRole('listitem').map(item => item.textContent);
        expect(steps).toEqual([
            'In Sonarr, go to Settings, Connect, and add a Webhook.',
            'Paste the address above into the URL field.',
            'Set Method to POST.',
            'Username can be anything, for example vora.',
            'Set Password to the secret.',
            'Tick the triggers for import and upgrade, rename, and delete.',
            'Press Test.',
        ]);
    });

    it('copies the address', async () => {
        renderPage();

        const card = await cardFor('Radarr');
        fireEvent.click(within(card).getByRole('button', { name: 'Copy Radarr address' }));

        await waitFor(() => expect(mocks.copyText).toHaveBeenCalledWith(`${window.location.origin}/api/webhooks/radarr_webhook`));
        expect(await within(card).findByRole('button', { name: 'Radarr address copied' })).toHaveTextContent('Copied');
    });

    it('mounts each provider\'s settings form for its own plugin', async () => {
        renderPage();

        expect(await screen.findByTestId('form-radarr_webhook')).toBeInTheDocument();
        expect(screen.getByTestId('form-sonarr_webhook')).toBeInTheDocument();
        expect(screen.getByTestId('form-lidarr_webhook')).toBeInTheDocument();
    });

    describe('generating a secret', () => {
        it('shows the new secret with a Copy button, remounts the settings form and refreshes the badge', async () => {
            mocks.getProviders.mockResolvedValueOnce([radarr, sonarr, lidarr]).mockResolvedValue([radarr, { ...sonarr, isConfigured: true }, lidarr]);
            mocks.generateSecret.mockResolvedValue({ secret: 'fake-secret-for-tests' });
            renderPage();

            const card = await cardFor('Sonarr');
            await waitFor(() => expect(mocks.formMounts['sonarr_webhook']).toBe(1));
            expect(within(card).queryByText('fake-secret-for-tests')).not.toBeInTheDocument();

            fireEvent.click(within(card).getByRole('button', { name: 'Generate secret' }));

            expect(await within(card).findByText('fake-secret-for-tests')).toBeInTheDocument();
            expect(mocks.generateSecret).toHaveBeenCalledWith('sonarr_webhook', undefined);
            expect(mocks.confirm).not.toHaveBeenCalled();
            expect(within(card).getByText(/only shown here once/)).toBeInTheDocument();
            expect(within(card).getByText(/already saved, and it is also editable below/)).toBeInTheDocument();
            await waitFor(() => expect(mocks.formMounts['sonarr_webhook']).toBe(2));
            expect(mocks.formMounts['radarr_webhook']).toBe(1);
            await waitFor(() => expect(within(card).getByText('Ready')).toBeInTheDocument());
        });

        it('copies the new secret', async () => {
            mocks.generateSecret.mockResolvedValue({ secret: 'fake-secret-for-tests' });
            renderPage();

            const card = await cardFor('Sonarr');
            fireEvent.click(within(card).getByRole('button', { name: 'Generate secret' }));
            await within(card).findByText('fake-secret-for-tests');
            fireEvent.click(within(card).getByRole('button', { name: 'Copy Sonarr secret' }));

            await waitFor(() => expect(mocks.copyText).toHaveBeenCalledWith('fake-secret-for-tests'));
            expect(await within(card).findByRole('button', { name: 'Sonarr secret copied' })).toBeInTheDocument();
        });

        it('asks first when a secret already exists, and generates once confirmed', async () => {
            mocks.generateSecret.mockResolvedValue({ secret: 'fake-secret-for-tests' });
            renderPage();

            const card = await cardFor('Radarr');
            fireEvent.click(within(card).getByRole('button', { name: 'Generate secret' }));

            expect(await within(card).findByText('fake-secret-for-tests')).toBeInTheDocument();
            expect(mocks.confirm).toHaveBeenCalledTimes(1);
            expect(mocks.confirm.mock.calls[0][0]).toMatchObject({
                title: 'Generate a new secret?',
                message: expect.stringContaining("Radarr's connection must be updated"),
            });
            expect(mocks.generateSecret).toHaveBeenCalledWith('radarr_webhook', undefined);
        });

        it('does nothing when the question is declined', async () => {
            mocks.confirm.mockResolvedValue(false);
            renderPage();

            const card = await cardFor('Radarr');
            await waitFor(() => expect(mocks.formMounts['radarr_webhook']).toBe(1));
            fireEvent.click(within(card).getByRole('button', { name: 'Generate secret' }));

            await waitFor(() => expect(mocks.confirm).toHaveBeenCalledTimes(1));
            expect(mocks.generateSecret).not.toHaveBeenCalled();
            expect(within(card).queryByText('New secret')).not.toBeInTheDocument();
            expect(mocks.formMounts['radarr_webhook']).toBe(1);
        });

        it('says so when the secret could not be generated, and shows none', async () => {
            mocks.generateSecret.mockRejectedValue(new Error('nope'));
            renderPage();

            const card = await cardFor('Sonarr');
            fireEvent.click(within(card).getByRole('button', { name: 'Generate secret' }));

            await waitFor(() => expect(mocks.alert).toHaveBeenCalledWith({
                title: 'Error', message: 'Failed to generate a secret for Sonarr.', tone: 'danger',
            }));
            expect(within(card).queryByText('New secret')).not.toBeInTheDocument();
        });
    });

    describe('recent deliveries', () => {
        it('lists what the server received, newest first, with a marker beyond colour', async () => {
            mocks.getDeliveries.mockResolvedValue([imported, refused]);
            renderPage();

            const table = await screen.findByRole('table');
            const rows = within(table).getAllByRole('row');
            expect(rows).toHaveLength(3);

            const first = within(rows[1]);
            expect(first.getByRole('cell', { name: formatDateTime(imported.at) })).toBeInTheDocument();
            expect(first.getByRole('cell', { name: 'Radarr (4K)' })).toBeInTheDocument();
            expect(first.getByText('Download')).toBeInTheDocument();
            expect(first.getByText(imported.path)).toBeInTheDocument();
            expect(first.getByText('OK')).toBeInTheDocument();
            expect(first.getByText('Scan queued')).toBeInTheDocument();

            const second = within(rows[2]);
            expect(second.getByRole('cell', { name: 'Sonarr' })).toBeInTheDocument();
            expect(second.getByText('Failed')).toBeInTheDocument();
            expect(second.getByText('Wrong secret')).toBeInTheDocument();
            expect(second.queryByText('OK')).not.toBeInTheDocument();
        });

        it('asks the server for 100 and falls back to the provider id for an unknown source', async () => {
            mocks.getDeliveries.mockResolvedValue([{ ...imported, providerId: 'other_webhook', instance: null }]);
            renderPage();

            expect(await screen.findByRole('cell', { name: 'other_webhook' })).toBeInTheDocument();
            expect(mocks.getDeliveries).toHaveBeenCalledWith(100, undefined);
        });

        it('says so when nothing has been received', async () => {
            renderPage();

            expect(await screen.findByText('Nothing received yet. Press Test in Radarr, Sonarr or Lidarr.')).toBeInTheDocument();
            expect(screen.queryByRole('table')).not.toBeInTheDocument();
        });

        it('reloads the list when Refresh is pressed', async () => {
            mocks.getDeliveries.mockResolvedValueOnce([]).mockResolvedValue([imported]);
            renderPage();
            await screen.findByText(/Nothing received yet/);

            fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));

            expect(await screen.findByRole('cell', { name: 'Radarr (4K)' })).toBeInTheDocument();
            expect(mocks.getDeliveries).toHaveBeenCalledTimes(2);
            expect(screen.queryByText(/Nothing received yet/)).not.toBeInTheDocument();
            expect(screen.getByRole('button', { name: 'Refresh' })).toBeEnabled();
        });

        it('keeps the list and says so when a refresh fails', async () => {
            mocks.getDeliveries.mockResolvedValueOnce([imported]).mockRejectedValue(new Error('down'));
            renderPage();
            await screen.findByRole('cell', { name: 'Radarr (4K)' });

            fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));

            await waitFor(() => expect(mocks.alert).toHaveBeenCalledWith('Failed to refresh recent deliveries.'));
            expect(screen.getByRole('cell', { name: 'Radarr (4K)' })).toBeInTheDocument();
        });

        it('scrolls sideways on a phone', async () => {
            mocks.getDeliveries.mockResolvedValue([imported]);
            renderPage();

            const table = await screen.findByRole('table');
            expect(table.className).toContain('max-md:min-w-[44rem]');
            expect(table.parentElement?.className).toContain('overflow-x-auto');
        });
    });

    it('says so when the connections cannot be loaded', async () => {
        mocks.getProviders.mockRejectedValue(new Error('down'));
        renderPage();

        await waitFor(() => expect(mocks.alert).toHaveBeenCalledWith('Failed to load the library update connections.'));
        expect(screen.queryByRole('article')).not.toBeInTheDocument();
    });

    it('passes the server of the multi-server route to every call', async () => {
        mocks.generateSecret.mockResolvedValue({ secret: 'fake-secret-for-tests' });
        renderPage('/admin/server/s1/library-updates');

        const card = await cardFor('Sonarr');
        fireEvent.click(within(card).getByRole('button', { name: 'Generate secret' }));
        await within(card).findByText('fake-secret-for-tests');

        expect(mocks.getProviders).toHaveBeenCalledWith('s1');
        expect(mocks.getDeliveries).toHaveBeenCalledWith(100, 's1');
        expect(mocks.generateSecret).toHaveBeenCalledWith('sonarr_webhook', 's1');
        await waitFor(() => expect(mocks.formServers['sonarr_webhook']).toBe('s1'));
    });

    it('shows keyboard focus as an accent fill on every button, never a ring', async () => {
        mocks.generateSecret.mockResolvedValue({ secret: 'fake-secret-for-tests' });
        renderPage();

        const card = await cardFor('Sonarr');
        fireEvent.click(within(card).getByRole('button', { name: 'Generate secret' }));
        await within(card).findByText('fake-secret-for-tests');

        const buttons = [
            ...within(card).getAllByRole('button'),
            screen.getByRole('button', { name: 'Refresh' }),
        ];
        expect(buttons.length).toBeGreaterThanOrEqual(4);
        for (const button of buttons) {
            for (const cls of FOCUS_FILL) expect(button.className).toContain(cls);
        }
    });

    it('lets the cards and the secret wrap on a phone', async () => {
        mocks.generateSecret.mockResolvedValue({ secret: 'fake-secret-for-tests' });
        renderPage();

        const card = await cardFor('Sonarr');
        fireEvent.click(within(card).getByRole('button', { name: 'Generate secret' }));
        const secret = await within(card).findByText('fake-secret-for-tests');

        expect(secret.className).toContain('break-all');
        expect(secret.parentElement?.className).toContain('max-md:flex-wrap');
        expect(card.querySelector('header')?.className).toContain('flex-wrap');
    });
});
