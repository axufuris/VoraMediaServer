import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError, AxiosHeaders } from 'axios';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import LoginPage from './LoginPage';
import { StorageKeys } from '../../utils/storageKeys';
import type { SignInProviders } from '../../api/Auth/externalSignInService';
import type { DevicePairingPoll, DevicePairingStart } from '../../api/Auth/devicePairingService';
import type { VoraDesktopBridge } from '../../desktop/desktopBridge';

const mocks = vi.hoisted(() => ({
    probeServer: vi.fn(),
    loginToServer: vi.fn(),
    getProviders: vi.fn<() => Promise<SignInProviders>>(),
    assign: vi.fn<(url: string) => void>(),
    startPairing: vi.fn<() => Promise<DevicePairingStart>>(),
    pollPairing: vi.fn<() => Promise<DevicePairingPoll>>(),
    openExternal: vi.fn<(url: string) => Promise<boolean>>(),
}));

vi.mock('../../api/Auth/devicePairingService', () => ({
    devicePairingService: { start: mocks.startPairing, poll: mocks.pollPairing },
}));

vi.mock('../../api/Auth/authService', () => ({
    authService: { probeServer: mocks.probeServer, loginToServer: mocks.loginToServer },
}));
vi.mock('../../api/Auth/externalSignInService', async importOriginal => {
    const actual = await importOriginal<typeof import('../../api/Auth/externalSignInService')>();
    return { externalSignInService: { ...actual.externalSignInService, getProviders: mocks.getProviders } };
});

const authentik = { id: 'p1', slug: 'authentik', displayName: 'Authentik', type: 'Oidc' as const, iconUrl: null };
const google = { id: 'p2', slug: 'google', displayName: 'Google', type: 'Oidc' as const, iconUrl: null };

const providers = (overrides: Partial<SignInProviders> = {}): SignInProviders => ({
    providers: [authentik],
    passwordSignInEnabled: true,
    directorySignInEnabled: false,
    registrationMode: 'Simple',
    ...overrides,
});

const renderPage = () => render(<MemoryRouter><LoginPage /></MemoryRouter>);

describe('LoginPage sign-in options', () => {
    const originalLocation = window.location;

    beforeEach(() => {
        localStorage.clear();
        sessionStorage.clear();
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.probeServer.mockResolvedValue({ isClaimed: true, registrationMode: 1, serverName: 'Home', emailEnabled: false });
        Object.defineProperty(window, 'location', {
            configurable: true,
            value: { ...originalLocation, origin: 'http://vora.test', assign: mocks.assign },
        });
    });

    afterEach(() => {
        cleanup();
        Object.defineProperty(window, 'location', { configurable: true, value: originalLocation });
    });

    it('shows only the email and password form when no provider is set up', async () => {
        mocks.getProviders.mockResolvedValue(providers({ providers: [] }));
        renderPage();

        expect(await screen.findByLabelText('Email')).toBeInTheDocument();
        await waitFor(() => expect(mocks.getProviders).toHaveBeenCalled());
        expect(screen.queryByRole('button', { name: /Continue with/ })).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Admin sign-in' })).not.toBeInTheDocument();
    });

    it('keeps the form when the server cannot list providers', async () => {
        mocks.getProviders.mockRejectedValue(new Error('404'));
        renderPage();

        expect(await screen.findByLabelText('Email')).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /Continue with/ })).not.toBeInTheDocument();
    });

    it('offers each provider under the password form, split by an "or"', async () => {
        mocks.getProviders.mockResolvedValue(providers({ providers: [authentik, google] }));
        renderPage();

        expect(await screen.findByRole('button', { name: 'Continue with Authentik' })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Continue with Google' })).toBeInTheDocument();
        expect(screen.getByLabelText('Email')).toBeInTheDocument();
        expect(screen.getByRole('separator', { name: 'or' })).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Admin sign-in' })).not.toBeInTheDocument();
    });

    it("sends the browser to the provider's start address on this server", async () => {
        mocks.getProviders.mockResolvedValue(providers());
        renderPage();

        fireEvent.click(await screen.findByRole('button', { name: 'Continue with Authentik' }));

        expect(mocks.assign).toHaveBeenCalledWith('http://vora.test/api/auth/oidc/authentik/start');
    });

    it('with password sign-in off shows only the providers and tucks the form behind Admin sign-in', async () => {
        mocks.getProviders.mockResolvedValue(providers({ passwordSignInEnabled: false }));
        renderPage();

        expect(await screen.findByRole('button', { name: 'Continue with Authentik' })).toBeInTheDocument();
        expect(screen.queryByLabelText('Email')).not.toBeInTheDocument();
        expect(screen.queryByLabelText('Password')).not.toBeInTheDocument();

        fireEvent.click(screen.getByRole('button', { name: 'Admin sign-in' }));

        expect(await screen.findByLabelText('Email')).toBeInTheDocument();
        expect(screen.getByLabelText('Password')).toBeInTheDocument();
        expect(screen.getByText(/turned off for everyone except admins/)).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Admin sign-in' })).not.toBeInTheDocument();
    });
});

describe('LoginPage inside Vora Desktop', () => {
    const originalLocation = window.location;

    beforeEach(() => {
        localStorage.clear();
        sessionStorage.clear();
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.probeServer.mockResolvedValue({ isClaimed: true, registrationMode: 1, serverName: 'Home', emailEnabled: false });
        mocks.startPairing.mockResolvedValue({
            deviceCode: 'device-secret',
            userCode: 'BCDF-GHJK',
            verificationUrl: 'http://vora.test/link',
            verificationUrlComplete: 'http://vora.test/link?code=BCDFGHJK',
            expiresInSeconds: 600,
            intervalSeconds: 1,
        });
        mocks.pollPairing.mockResolvedValue({ status: 'pending' });
        mocks.openExternal.mockResolvedValue(true);
        window.voraDesktop = { openExternal: mocks.openExternal } as unknown as VoraDesktopBridge;
        Object.defineProperty(window, 'location', {
            configurable: true,
            value: { ...originalLocation, origin: 'http://vora.test', assign: mocks.assign },
        });
    });

    afterEach(() => {
        cleanup();
        delete window.voraDesktop;
        Object.defineProperty(window, 'location', { configurable: true, value: originalLocation });
    });

    const renderRouted = () => render(
        <MemoryRouter initialEntries={['/login']}>
            <Routes>
                <Route path="/login" element={<LoginPage />} />
                <Route path="/profiles" element={<div>Who is watching</div>} />
            </Routes>
        </MemoryRouter>,
    );

    it('offers signing in with the browser beside the form, in place of the provider buttons', async () => {
        mocks.getProviders.mockResolvedValue(providers({ providers: [authentik, google] }));
        renderRouted();

        expect(await screen.findByRole('button', { name: 'Sign in with your browser' })).toBeInTheDocument();
        expect(screen.getByLabelText('Email')).toBeInTheDocument();
        expect(screen.getByRole('separator', { name: 'or' })).toBeInTheDocument();
        await waitFor(() => expect(mocks.getProviders).toHaveBeenCalled());
        expect(screen.queryByRole('button', { name: /Continue with/ })).not.toBeInTheDocument();
    });

    it('offers it even when the server has no provider, since the browser approval works for any account', async () => {
        mocks.getProviders.mockResolvedValue(providers({ providers: [] }));
        renderRouted();

        expect(await screen.findByRole('button', { name: 'Sign in with your browser' })).toBeInTheDocument();
    });

    it('with password sign-in off offers it above the Admin sign-in link', async () => {
        mocks.getProviders.mockResolvedValue(providers({ passwordSignInEnabled: false }));
        renderRouted();

        expect(await screen.findByRole('button', { name: 'Admin sign-in' })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Sign in with your browser' })).toBeInTheDocument();
        expect(screen.queryByLabelText('Email')).not.toBeInTheDocument();
    });

    it('signs in with the approved pairing and goes on to choose a profile', async () => {
        mocks.getProviders.mockResolvedValue(providers({ providers: [] }));
        mocks.pollPairing.mockResolvedValue({
            status: 'approved',
            auth: { accessToken: 'account-token', userId: 'user-1', displayName: 'Andy', isAdmin: false },
        });
        renderRouted();

        fireEvent.click(await screen.findByRole('button', { name: 'Sign in with your browser' }));

        expect(await screen.findByText('Who is watching', {}, { timeout: 4000 })).toBeInTheDocument();
        expect(mocks.openExternal).toHaveBeenCalledWith('http://vora.test/link?code=BCDFGHJK');
        expect(localStorage.getItem('account_token')).toBe('account-token');
        expect(localStorage.getItem('user_id')).toBe('user-1');
        expect(sessionStorage.getItem('pending_user_id')).toBe('user-1');
    });
});

describe('LoginPage with a directory', () => {
    const originalLocation = window.location;

    const failure = (status: number, data: unknown) => new AxiosError('failed', String(status), undefined, undefined, {
        status, statusText: '', headers: {}, config: { headers: new AxiosHeaders() }, data,
    });

    const renderRoutes = () => render(
        <MemoryRouter initialEntries={['/login']}>
            <Routes>
                <Route path="/login" element={<LoginPage />} />
                <Route path="/login/external" element={<Where label="external" />} />
                <Route path="/profiles" element={<Where label="profiles" />} />
            </Routes>
        </MemoryRouter>,
    );

    function Where({ label }: { label: string }) {
        const location = useLocation();
        return <p data-testid="where">{label}{location.search}</p>;
    }

    const typeAndSubmit = async (name: string, password: string) => {
        fireEvent.change(await screen.findByLabelText('Email or username'), { target: { value: name } });
        fireEvent.change(screen.getByLabelText('Password'), { target: { value: password } });
        fireEvent.click(screen.getByRole('button', { name: 'Sign In' }));
    };

    beforeEach(() => {
        localStorage.clear();
        sessionStorage.clear();
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.probeServer.mockResolvedValue({ isClaimed: true, registrationMode: 1, serverName: 'Home', emailEnabled: false });
        mocks.getProviders.mockResolvedValue(providers({ providers: [], directorySignInEnabled: true }));
        Object.defineProperty(window, 'location', { configurable: true, value: { ...originalLocation, origin: 'http://vora.test', assign: mocks.assign } });
    });

    afterEach(() => {
        cleanup();
        Object.defineProperty(window, 'location', { configurable: true, value: originalLocation });
    });

    it('asks for an email or a username and accepts a name that is not an email address', async () => {
        renderRoutes();

        const field = await screen.findByLabelText('Email or username');
        expect(field).toHaveAttribute('type', 'text');
        expect(field).toHaveAttribute('autocomplete', 'username');
        expect(field).toHaveAttribute('autocapitalize', 'none');
        expect(screen.queryByLabelText('Email')).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /Continue with/ })).not.toBeInTheDocument();
    });

    it('keeps the email-only field and the email wording when no directory is on', async () => {
        mocks.getProviders.mockResolvedValue(providers({ providers: [] }));
        renderRoutes();

        const field = await screen.findByLabelText('Email');
        await waitFor(() => expect(mocks.getProviders).toHaveBeenCalled());
        expect(field).toHaveAttribute('type', 'email');
        expect(screen.queryByLabelText('Email or username')).not.toBeInTheDocument();
    });

    it('sends the typed name as it is and goes on to choose a profile', async () => {
        mocks.loginToServer.mockResolvedValue({ accessToken: 'token', userId: 'u1', displayName: 'Alice', isAdmin: false });
        renderRoutes();

        await typeAndSubmit(' alice ', 'alice-pw');

        expect(await screen.findByTestId('where')).toHaveTextContent('profiles');
        expect(mocks.loginToServer).toHaveBeenCalledWith('http://vora.test', 'alice', 'alice-pw');
    });

    it('goes on to the page that finishes the sign-in when the server answers 409 with a handle', async () => {
        mocks.loginToServer.mockRejectedValue(failure(409, { title: 'More is needed to finish signing in', pending: 'handle/1', outcome: 'NeedsCreateOrLink' }));
        renderRoutes();

        await typeAndSubmit('alice', 'alice-pw');

        expect(await screen.findByTestId('where')).toHaveTextContent('external?pending=handle%2F1');
        expect(localStorage.getItem(StorageKeys.accountToken)).toBeNull();
    });

    it('does not follow a 409 that has no handle and shows its message instead', async () => {
        mocks.loginToServer.mockRejectedValue(failure(409, { title: 'Conflict', detail: 'Something else is going on.' }));
        renderRoutes();

        await typeAndSubmit('alice', 'alice-pw');

        expect(await screen.findByText('Something else is going on.')).toBeInTheDocument();
        expect(screen.queryByTestId('where')).not.toBeInTheDocument();
    });

    it('shows the reason a directory sign-in was refused', async () => {
        mocks.loginToServer.mockRejectedValue(failure(403, {
            title: 'Sign-in refused', detail: 'Your directory entry has no email address, which Vora needs to make your account. Ask your administrator to add one.',
        }));
        renderRoutes();

        await typeAndSubmit('carol', 'carol-pw');

        expect(await screen.findByText(/has no email address/)).toBeInTheDocument();
    });

    it('says the email, the username or the password was wrong and never mentions the directory', async () => {
        mocks.loginToServer.mockRejectedValue(failure(401, { title: 'Unauthorized' }));
        renderRoutes();

        await typeAndSubmit('alice', 'wrong');

        const message = await screen.findByText(/Invalid email, username or password/);
        expect(message).toHaveTextContent(/wait a few minutes/);
        expect(message).not.toHaveTextContent(/directory|LDAP/i);
    });

    it('keeps the form for directory people when passwords kept on this server are turned off', async () => {
        mocks.getProviders.mockResolvedValue(providers({ providers: [], directorySignInEnabled: true, passwordSignInEnabled: false }));
        renderRoutes();

        expect(await screen.findByLabelText('Email or username')).toBeInTheDocument();
        expect(screen.getByLabelText('Password')).toBeInTheDocument();
        expect(screen.getByText(/only work for admins/)).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Admin sign-in' })).not.toBeInTheDocument();
    });

    it('shows the form and the provider buttons together when a directory and a provider are both on', async () => {
        mocks.getProviders.mockResolvedValue(providers({ directorySignInEnabled: true, passwordSignInEnabled: false }));
        renderRoutes();

        expect(await screen.findByLabelText('Email or username')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Continue with Authentik' })).toBeInTheDocument();
        expect(screen.getByRole('separator', { name: 'or' })).toBeInTheDocument();
    });
});
