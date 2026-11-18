import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { AxiosError, AxiosHeaders } from 'axios';
import SignInProvidersPage from './SignInProvidersPage';
import { noDirectoryFields, savedDirectory } from '../../test/signInFixtures';
import type { AuthProviderAdmin, AuthSettings, LdapTestResult, OidcTestResult, ProviderTestResult } from '../../api/Auth/signInProvidersAdminService';

const mocks = vi.hoisted(() => ({
    list: vi.fn<() => Promise<AuthProviderAdmin[]>>(),
    getSettings: vi.fn<() => Promise<AuthSettings>>(),
    create: vi.fn(),
    update: vi.fn(),
    remove: vi.fn(),
    test: vi.fn<() => Promise<ProviderTestResult>>(),
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
    ...noDirectoryFields,
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

describe('SignInProvidersPage directories', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.getSettings.mockResolvedValue({ passwordSignInEnabled: true, publicBaseUrl: null, effectiveBaseUrl: 'https://vora.example.com' });
        mocks.list.mockResolvedValue([authentik, savedDirectory]);
        mocks.confirm.mockResolvedValue(true);
    });

    afterEach(cleanup);

    const directoryOk: LdapTestResult = {
        ok: true, message: 'The directory answered and the user search found 5 entries.', connected: true, serviceBound: true, entriesFound: 5, searchLimited: false,
        entriesWithMail: 4, entriesWithoutMail: 1, entriesWithUniqueId: 5, sampleDns: ['uid=alice,ou=people,dc=example,dc=com'],
        warnings: ['1 of the 5 entries checked have no value in the mail attribute (mail). They cannot sign in.'],
    };

    it('offers the directory presets next to the provider ones', async () => {
        mocks.list.mockResolvedValue([]);
        renderPage();

        const directories = await screen.findByRole('group', { name: 'Sign in with a directory (LDAP)' });
        expect(within(directories).getByRole('button', { name: /LDAP directory/ })).toBeInTheDocument();
        expect(within(directories).getByRole('button', { name: /Active Directory/ })).toBeInTheDocument();
        expect(within(directories).getByRole('button', { name: /OpenLDAP, LLDAP, Authentik/ })).toBeInTheDocument();
        const providers = screen.getByRole('group', { name: 'Sign in with a provider' });
        expect(within(providers).getAllByRole('button')).toHaveLength(3);
    });

    it('lists a directory with its host, service account and search base and no redirect address', async () => {
        renderPage();

        const card = await screen.findByRole('article', { name: 'Company directory' });
        expect(within(card).getByText('LDAP')).toBeInTheDocument();
        expect(within(card).getByText('ldap.example.com')).toBeInTheDocument();
        expect(within(card).getByText('LDAPS')).toBeInTheDocument();
        expect(within(card).getByText('cn=vora,ou=service,dc=example,dc=com')).toBeInTheDocument();
        expect(within(card).getByText('ou=people,dc=example,dc=com')).toBeInTheDocument();
        expect(within(card).queryByText('Redirect address')).not.toBeInTheDocument();
        expect(within(card).queryByText(/api\/auth\/oidc/)).not.toBeInTheDocument();
    });

    it('tests a directory and reports what was found and what to fix', async () => {
        mocks.test.mockResolvedValue(directoryOk);
        renderPage();

        const card = await screen.findByRole('article', { name: 'Company directory' });
        fireEvent.click(within(card).getByRole('button', { name: 'Test connection' }));

        const status = await within(card).findByRole('status');
        expect(status).toHaveTextContent('Connected');
        expect(status).toHaveTextContent('Reached the directory and signed in with the service account');
        expect(status).toHaveTextContent('5 people found: 4 with an email, 1 without');
        expect(status).toHaveTextContent('uid=alice,ou=people,dc=example,dc=com');
        expect(status).toHaveTextContent('They cannot sign in.');
        expect(mocks.test).toHaveBeenCalledWith('d1', undefined);
    });

    it('says when the service account was refused', async () => {
        mocks.test.mockResolvedValue({ ...directoryOk, ok: false, serviceBound: false, entriesFound: 0, sampleDns: [], warnings: [], message: 'The directory refused the bind account.' });
        renderPage();

        const card = await screen.findByRole('article', { name: 'Company directory' });
        fireEvent.click(within(card).getByRole('button', { name: 'Test connection' }));

        const alert = await within(card).findByRole('alert');
        expect(alert).toHaveTextContent('The test did not pass');
        expect(alert).toHaveTextContent('refused the bind account');
        expect(alert).not.toHaveTextContent('people found');
    });

    it('shows a failed test run as a directory result', async () => {
        mocks.test.mockRejectedValue(new Error('network down'));
        renderPage();

        const card = await screen.findByRole('article', { name: 'Company directory' });
        fireEvent.click(within(card).getByRole('button', { name: 'Test connection' }));

        expect(await within(card).findByRole('alert')).toHaveTextContent('The test did not pass');
    });

    it('creates a directory from a preset and tests it straight away', async () => {
        mocks.list.mockResolvedValue([]);
        mocks.create.mockResolvedValue(savedDirectory);
        mocks.test.mockResolvedValue(directoryOk);
        renderPage();

        fireEvent.click(await screen.findByRole('button', { name: /LDAP directory/ }));
        fireEvent.change(screen.getByLabelText('Host'), { target: { value: 'ldap.example.com' } });
        fireEvent.change(screen.getByLabelText('User search base'), { target: { value: 'ou=people,dc=example,dc=com' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save provider' }));

        await waitFor(() => expect(mocks.create).toHaveBeenCalled());
        expect(mocks.create.mock.calls[0][0]).toMatchObject({ type: 'Ldap', slug: 'ldap', ldapHost: 'ldap.example.com', ldapUserBaseDn: 'ou=people,dc=example,dc=com', enabled: false });
        await waitFor(() => expect(mocks.test).toHaveBeenCalledWith('d1', undefined));
    });

    it('does not run the test for a directory that has no host yet', async () => {
        mocks.list.mockResolvedValue([]);
        mocks.create.mockResolvedValue({ ...savedDirectory, ldapHost: null });
        renderPage();

        fireEvent.click(await screen.findByRole('button', { name: /LDAP directory/ }));
        fireEvent.click(screen.getByRole('button', { name: 'Save provider' }));

        await waitFor(() => expect(mocks.create).toHaveBeenCalled());
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
        expect(mocks.test).not.toHaveBeenCalled();
    });

    it('edits a saved directory with its fields filled in', async () => {
        renderPage();

        const card = await screen.findByRole('article', { name: 'Company directory' });
        fireEvent.click(within(card).getByRole('button', { name: 'Edit' }));

        expect(await screen.findByLabelText('Host')).toHaveValue('ldap.example.com');
        expect(screen.getByLabelText('User filter')).toHaveValue('(&(objectClass=person)(uid={username}))');
        expect(screen.getByLabelText('Bind password')).toHaveValue('');
    });
});
