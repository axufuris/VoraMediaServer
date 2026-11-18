import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { AxiosError, AxiosHeaders } from 'axios';
import SignInProvidersPage from './SignInProvidersPage';
import type { AuthProviderAdmin, AuthSettings, OidcTestResult } from '../../api/Auth/signInProvidersAdminService';

const mocks = vi.hoisted(() => ({
    list: vi.fn<() => Promise<AuthProviderAdmin[]>>(),
    getSettings: vi.fn<() => Promise<AuthSettings>>(),
    create: vi.fn(),
    update: vi.fn(),
    remove: vi.fn(),
    test: vi.fn<() => Promise<OidcTestResult>>(),
    confirm: vi.fn(),
    alert: vi.fn(),
}));

vi.mock('../../api/Auth/signInProvidersAdminService', () => ({
    signInProvidersAdminService: {
        list: mocks.list, getSettings: mocks.getSettings, create: mocks.create, update: mocks.update, remove: mocks.remove, test: mocks.test, updateSettings: vi.fn(),
    },
}));
vi.mock('../../dialogs', () => ({
    useDialog: () => ({ alert: (m: unknown) => mocks.alert(m), confirm: (o: unknown) => mocks.confirm(o), prompt: vi.fn() }),
}));

const authentik: AuthProviderAdmin = {
    id: 'p1', type: 'Oidc', slug: 'authentik', displayName: 'Authentik', enabled: true, iconUrl: null, sortOrder: 0,
    issuer: 'https://auth.example.com/application/o/vora/', clientId: 'client-1', hasSecret: true, scopes: 'openid email profile',
    emailClaim: 'email', nameClaim: 'name', groupsClaim: 'groups', newUserPolicy: 'FollowRegistrationMode', linkByEmail: 'ConfirmWithPassword',
    allowedEmailDomains: [], requiredGroup: null, adminGroup: null, redirectUri: 'https://vora.example.com/api/auth/oidc/authentik/callback',
};

const ok: OidcTestResult = {
    ok: true, message: 'The provider answered.', issuer: authentik.issuer, authorizationEndpoint: 'https://auth.example.com/authorize', tokenEndpoint: 'https://auth.example.com/token',
    jwksUri: 'https://auth.example.com/jwks', userInfoEndpoint: null, signingKeyCount: 2, advertisesPkceS256: true, warnings: ['The issuer uses http.'],
};

const conflict = (detail: string) => new AxiosError('conflict', '409', undefined, undefined, {
    status: 409, statusText: 'Conflict', headers: {}, config: { headers: new AxiosHeaders() }, data: { title: 'Conflict', detail },
});

const renderPage = () => render(<MemoryRouter><SignInProvidersPage /></MemoryRouter>);

describe('SignInProvidersPage', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.getSettings.mockResolvedValue({ passwordSignInEnabled: true, publicBaseUrl: null, effectiveBaseUrl: 'https://vora.example.com' });
        mocks.list.mockResolvedValue([authentik]);
        mocks.confirm.mockResolvedValue(true);
    });

    afterEach(cleanup);

    it('lists each provider with its redirect address and what it does for new people', async () => {
        renderPage();

        const card = await screen.findByRole('article', { name: 'Authentik' });
        expect(within(card).getByText('https://vora.example.com/api/auth/oidc/authentik/callback')).toBeInTheDocument();
        expect(within(card).getByText('Follows the registration mode')).toBeInTheDocument();
        expect(within(card).getByText("Asks for the account's password once")).toBeInTheDocument();
        expect(within(card).getByText('On')).toBeInTheDocument();
    });

    it('offers the three presets and says plainly when nothing is set up', async () => {
        mocks.list.mockResolvedValue([]);
        renderPage();

        expect(await screen.findByText('No providers yet')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /OpenID Connect/ })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /Google/ })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /Microsoft/ })).toBeInTheDocument();
    });

    it('tests the connection and reports the endpoints and warnings', async () => {
        mocks.test.mockResolvedValue(ok);
        renderPage();

        fireEvent.click(await screen.findByRole('button', { name: 'Test connection' }));

        const status = await screen.findByRole('status');
        expect(status).toHaveTextContent('Connected');
        expect(status).toHaveTextContent('2 signing keys found, PKCE supported');
        expect(status).toHaveTextContent('The issuer uses http.');
        expect(mocks.test).toHaveBeenCalledWith('p1', undefined);
    });

    it('reports a failed test with the reason', async () => {
        mocks.test.mockResolvedValue({ ...ok, ok: false, message: 'The issuer in the discovery document does not match.', warnings: [] });
        renderPage();

        fireEvent.click(await screen.findByRole('button', { name: 'Test connection' }));

        expect(await screen.findByRole('alert')).toHaveTextContent('does not match');
    });

    it('creates a provider from a preset and tests it straight away', async () => {
        mocks.list.mockResolvedValue([]);
        mocks.create.mockResolvedValue(authentik);
        mocks.test.mockResolvedValue(ok);
        renderPage();

        fireEvent.click(await screen.findByRole('button', { name: /OpenID Connect/ }));
        fireEvent.change(screen.getByLabelText('Issuer address'), { target: { value: 'https://auth.example.com/application/o/vora/' } });
        fireEvent.change(screen.getByLabelText('Client id'), { target: { value: 'client-1' } });
        fireEvent.change(screen.getByLabelText('Client secret'), { target: { value: 'hush' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save provider' }));

        await waitFor(() => expect(mocks.create).toHaveBeenCalled());
        expect(mocks.create.mock.calls[0][0]).toMatchObject({ slug: 'sso', issuer: 'https://auth.example.com/application/o/vora/', clientId: 'client-1', clientSecret: 'hush', enabled: false });
        await waitFor(() => expect(mocks.test).toHaveBeenCalledWith('p1', undefined));
    });

    it('asks before removing a provider', async () => {
        mocks.remove.mockResolvedValue(undefined);
        renderPage();

        fireEvent.click(await screen.findByRole('button', { name: 'Remove' }));

        await waitFor(() => expect(mocks.remove).toHaveBeenCalledWith('p1', false, undefined));
        expect(mocks.confirm).toHaveBeenCalledWith(expect.objectContaining({ title: 'Remove Authentik?' }));
    });

    it('keeps the provider when the question is declined', async () => {
        mocks.confirm.mockResolvedValue(false);
        renderPage();

        fireEvent.click(await screen.findByRole('button', { name: 'Remove' }));

        await waitFor(() => expect(mocks.confirm).toHaveBeenCalled());
        expect(mocks.remove).not.toHaveBeenCalled();
    });

    it('warns when accounts would be locked out and only then removes it with force', async () => {
        mocks.remove
            .mockRejectedValueOnce(conflict('2 account(s) can only sign in through this provider and would be locked out.'))
            .mockResolvedValueOnce(undefined);
        renderPage();

        fireEvent.click(await screen.findByRole('button', { name: 'Remove' }));

        await waitFor(() => expect(mocks.remove).toHaveBeenCalledTimes(2));
        expect(mocks.remove).toHaveBeenNthCalledWith(1, 'p1', false, undefined);
        expect(mocks.remove).toHaveBeenNthCalledWith(2, 'p1', true, undefined);
        expect(mocks.confirm).toHaveBeenLastCalledWith(expect.objectContaining({ title: 'Some accounts would be locked out', confirmText: 'Remove anyway' }));
    });
});
