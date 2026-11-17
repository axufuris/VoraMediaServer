import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import RequestsPage from './RequestsPage';
import type { MediaRequestVM, RequestServerVM } from '../../api/Discovery/requestAdminService';

const api = vi.hoisted(() => ({
    getRequests: vi.fn<() => Promise<MediaRequestVM[]>>(),
    getServers: vi.fn<() => Promise<RequestServerVM[]>>(),
    getProviderOptions: vi.fn(),
    approveRequest: vi.fn<(id: string) => Promise<void>>(),
    deleteRequest: vi.fn(),
    alert: vi.fn<(message: string) => Promise<void>>(),
    confirm: vi.fn(),
}));

vi.mock('../../api/Discovery/requestAdminService', () => ({
    requestAdminService: {
        getRequests: () => api.getRequests(),
        getServers: () => api.getServers(),
        getProviderOptions: () => api.getProviderOptions(),
        approveRequest: (id: string) => api.approveRequest(id),
        deleteRequest: () => api.deleteRequest(),
    },
}));
vi.mock('../../dialogs', () => ({
    useDialog: () => ({ alert: (message: string) => api.alert(message), confirm: () => api.confirm(), prompt: () => Promise.resolve(null) }),
}));

const request = (id: string, title: string, type: 'Movie' | 'TvShow', status = 0): MediaRequestVM => ({
    id,
    providerId: 'tmdb_discovery',
    externalId: id,
    type,
    title,
    status,
    createdAt: '2026-11-15T22:57:00Z',
    requesters: [{ profileId: 'p1', requestedAt: '2026-11-15T22:57:00Z', profile: { name: 'Andy' } }],
});

const server = (name: string, mediaType: 'Movie' | 'TvShow'): RequestServerVM => ({
    id: `server-${name}`, name, providerId: name.toLowerCase(), mediaType, hostname: '', port: 7878, useSsl: false, apiKey: '', urlBase: '',
    isDefault: true, is4K: false, providesReleaseCalendar: false, providerSettingsJson: '{}', isEnabled: true,
});

const renderPage = () => render(<MemoryRouter><RequestsPage /></MemoryRouter>);

const typeTab = (name: RegExp) => screen.getByRole('tab', { name });
const statusPill = (name: RegExp) => screen.getByRole('button', { name });

beforeEach(() => {
    api.getRequests.mockReset().mockResolvedValue([
        request('1', 'The Godfather', 'Movie'),
        request('2', 'Hexed', 'Movie'),
        request('3', 'Dune: Part Three', 'Movie', 3),
        request('4', 'MobLand', 'TvShow'),
    ]);
    api.getServers.mockReset().mockResolvedValue([server('Radarr', 'Movie'), server('Sonarr', 'TvShow')]);
    api.getProviderOptions.mockReset().mockResolvedValue([]);
    api.approveRequest.mockReset().mockResolvedValue(undefined);
    api.deleteRequest.mockReset().mockResolvedValue(undefined);
    api.alert.mockReset().mockResolvedValue(undefined);
    api.confirm.mockReset().mockResolvedValue(true);
});

describe('the request queue', () => {
    it('keeps movie requests and show requests apart, one tab each', async () => {
        renderPage();
        await screen.findByText('The Godfather');

        expect(screen.getByText('Hexed')).toBeInTheDocument();
        expect(screen.queryByText('MobLand')).not.toBeInTheDocument();

        fireEvent.click(typeTab(/Shows/));

        expect(await screen.findByText('MobLand')).toBeInTheDocument();
        expect(screen.queryByText('The Godfather')).not.toBeInTheDocument();
        expect(screen.queryByText('Hexed')).not.toBeInTheDocument();
    });

    it('counts what is pending on each tab, and counts the statuses for the type on show', async () => {
        renderPage();
        await screen.findByText('The Godfather');

        expect(typeTab(/Movies/)).toHaveTextContent('(2)');
        expect(typeTab(/Shows/)).toHaveTextContent('(1)');
        expect(statusPill(/Pending/)).toHaveTextContent('(2)');
        expect(statusPill(/Processing/)).toHaveTextContent('(1)');

        fireEvent.click(typeTab(/Shows/));

        expect(statusPill(/Pending/)).toHaveTextContent('(1)');
        expect(statusPill(/Processing/)).toHaveTextContent('(0)');
    });

    it('opens on Movies when both have something waiting', async () => {
        renderPage();
        await screen.findByText('The Godfather');

        expect(typeTab(/Movies/)).toHaveAttribute('aria-selected', 'true');
    });

    it('opens on Shows when only shows are waiting, so the first thing seen is what needs a decision', async () => {
        api.getRequests.mockResolvedValue([request('3', 'Dune: Part Three', 'Movie', 3), request('4', 'MobLand', 'TvShow')]);
        renderPage();

        expect(await screen.findByText('MobLand')).toBeInTheDocument();
        expect(typeTab(/Shows/)).toHaveAttribute('aria-selected', 'true');
    });

    it('stays on the tab it was left on when an approval reloads the page', async () => {
        api.getRequests.mockResolvedValue([request('4', 'MobLand', 'TvShow'), request('1', 'The Godfather', 'Movie')]);
        renderPage();
        await screen.findByText('The Godfather');
        fireEvent.click(typeTab(/Shows/));
        await screen.findByText('MobLand');

        api.getRequests.mockResolvedValue([request('4', 'MobLand', 'TvShow', 3), request('1', 'The Godfather', 'Movie')]);
        fireEvent.click(screen.getByRole('button', { name: 'Approve & Send' }));

        await waitFor(() => expect(api.approveRequest).toHaveBeenCalledWith('4'));
        await waitFor(() => expect(typeTab(/Shows/)).toHaveAttribute('aria-selected', 'true'));
    });
});

describe('approving a request', () => {
    it('says it was approved and sent, naming the title and the server it went to', async () => {
        renderPage();
        await screen.findByText('The Godfather');

        fireEvent.click(within(screen.getByText('The Godfather').closest('.vora-card') as HTMLElement).getByRole('button', { name: 'Approve & Send' }));

        const toast = await screen.findByRole('status');
        await waitFor(() => expect(toast).toHaveTextContent('Approved "The Godfather" and sent to Radarr.'));
        expect(api.approveRequest).toHaveBeenCalledWith('1');
        expect(api.alert).not.toHaveBeenCalled();
    });

    it('names Sonarr for a show', async () => {
        renderPage();
        await screen.findByText('The Godfather');
        fireEvent.click(typeTab(/Shows/));
        await screen.findByText('MobLand');

        fireEvent.click(screen.getByRole('button', { name: 'Approve & Send' }));

        await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Approved "MobLand" and sent to Sonarr.'));
    });

    it('says only that it was sent when no default server is configured for the type', async () => {
        api.getServers.mockResolvedValue([]);
        renderPage();
        await screen.findByText('The Godfather');

        fireEvent.click(within(screen.getByText('The Godfather').closest('.vora-card') as HTMLElement).getByRole('button', { name: 'Approve & Send' }));

        await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Approved "The Godfather" and sent.'));
    });

    it('shows no success message when sending fails, and says so in an alert', async () => {
        api.approveRequest.mockRejectedValue(new Error('bad gateway'));
        renderPage();
        await screen.findByText('The Godfather');

        fireEvent.click(within(screen.getByText('The Godfather').closest('.vora-card') as HTMLElement).getByRole('button', { name: 'Approve & Send' }));

        await waitFor(() => expect(api.alert).toHaveBeenCalledWith('Failed to send request to the provider. Check your server settings!'));
        expect(screen.getByRole('status')).toBeEmptyDOMElement();
    });
});
