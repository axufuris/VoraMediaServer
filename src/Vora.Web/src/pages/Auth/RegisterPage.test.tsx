import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import RegisterPage from './RegisterPage';
import type { SignInProviders } from '../../api/Auth/externalSignInService';
import type { DevicePairingPoll, DevicePairingStart } from '../../api/Auth/devicePairingService';
import { fakeDesktopBridge } from '../../test/fakeDesktopBridge';

const mocks = vi.hoisted(() => ({
    probeServer: vi.fn(),
    validateInvitation: vi.fn(),
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
    authService: { probeServer: mocks.probeServer, validateInvitation: mocks.validateInvitation, register: vi.fn() },
}));
vi.mock('../../api/Auth/externalSignInService', async importOriginal => {
    const actual = await importOriginal<typeof import('../../api/Auth/externalSignInService')>();
    return { externalSignInService: { ...actual.externalSignInService, getProviders: mocks.getProviders } };
});

const authentik = { id: 'p1', slug: 'authentik', displayName: 'Authentik', type: 'Oidc' as const, iconUrl: null };

const providers = (overrides: Partial<SignInProviders> = {}): SignInProviders => ({
    providers: [authentik],
    passwordSignInEnabled: true,
    directorySignInEnabled: false,
    registrationMode: 'Simple',
    ...overrides,
});

const renderAt = (search = '') => render(<MemoryRouter initialEntries={[`/register${search}`]}><RegisterPage /></MemoryRouter>);

describe('RegisterPage with sign-in providers', () => {
    const originalLocation = window.location;

    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.probeServer.mockResolvedValue({ isClaimed: true, registrationMode: 1, serverName: 'Home' });
        Object.defineProperty(window, 'location', {
            configurable: true,
            value: { ...originalLocation, origin: 'http://vora.test', assign: mocks.assign },
        });
    });

    afterEach(() => {
        cleanup();
        Object.defineProperty(window, 'location', { configurable: true, value: originalLocation });
    });

    it('shows no provider button when none is set up', async () => {
        mocks.getProviders.mockResolvedValue(providers({ providers: [] }));
        renderAt();

        expect(await screen.findByRole('button', { name: 'Register' })).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /Continue with/ })).not.toBeInTheDocument();
    });

    it('puts the provider beside the form and starts its sign-in on this server', async () => {
        mocks.getProviders.mockResolvedValue(providers());
        renderAt();

        const button = await screen.findByRole('button', { name: 'Continue with Authentik' });
        expect(screen.getByRole('button', { name: 'Register' })).toBeInTheDocument();
        fireEvent.click(button);

        expect(mocks.assign).toHaveBeenCalledWith('http://vora.test/api/auth/oidc/authentik/start');
    });

    it('carries the invitation through to the provider', async () => {
        mocks.probeServer.mockResolvedValue({ isClaimed: true, registrationMode: 3, serverName: 'Home' });
        mocks.validateInvitation.mockResolvedValue({ email: 'sam@example.com', expiresAt: '2030-01-01T00:00:00Z' });
        mocks.getProviders.mockResolvedValue(providers({ registrationMode: 'Invitation' }));
        renderAt('?invite=tok-123');

        fireEvent.click(await screen.findByRole('button', { name: 'Continue with Authentik' }));

        expect(mocks.assign).toHaveBeenCalledWith('http://vora.test/api/auth/oidc/authentik/start?invite=tok-123');
        expect(screen.getAllByText('sam@example.com').length).toBeGreaterThan(0);
    });

    it('tells people the invite PIN comes after the provider when the server uses PINs', async () => {
        mocks.probeServer.mockResolvedValue({ isClaimed: true, registrationMode: 2, serverName: 'Home' });
        mocks.getProviders.mockResolvedValue(providers({ registrationMode: 'SecretWord' }));
        renderAt();

        expect(await screen.findByText(/asked for the invite PIN after you sign in/)).toBeInTheDocument();
    });

    it('with password sign-in off shows only the providers', async () => {
        mocks.getProviders.mockResolvedValue(providers({ passwordSignInEnabled: false }));
        renderAt();

        expect(await screen.findByRole('button', { name: 'Continue with Authentik' })).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Register' })).not.toBeInTheDocument();
        expect(screen.queryByLabelText('Display Name')).not.toBeInTheDocument();
    });
});

describe('RegisterPage inside Vora Desktop', () => {
    const originalLocation = window.location;

    beforeEach(() => {
        localStorage.clear();
        sessionStorage.clear();
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.probeServer.mockResolvedValue({ isClaimed: true, registrationMode: 1, serverName: 'Home' });
        mocks.getProviders.mockResolvedValue(providers());
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
        window.voraDesktop = fakeDesktopBridge({ openExternal: mocks.openExternal }).bridge;
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

    const renderRouted = (search = '') => render(
        <MemoryRouter initialEntries={[`/register${search}`]}>
            <Routes>
                <Route path="/register" element={<RegisterPage />} />
                <Route path="/profiles" element={<div>Who is watching</div>} />
            </Routes>
        </MemoryRouter>,
    );

    it('opens the provider in the system browser and never navigates the window', async () => {
        renderRouted();

        fireEvent.click(await screen.findByRole('button', { name: 'Continue with Authentik' }));

        await waitFor(() => expect(mocks.openExternal).toHaveBeenCalledWith('http://vora.test/api/auth/oidc/authentik/start'));
        expect(mocks.assign).not.toHaveBeenCalled();
    });

    it('carries the invitation to the provider in the browser', async () => {
        mocks.probeServer.mockResolvedValue({ isClaimed: true, registrationMode: 3, serverName: 'Home' });
        mocks.validateInvitation.mockResolvedValue({ email: 'sam@example.com', expiresAt: '2030-01-01T00:00:00Z' });
        renderRouted('?invite=tok-123');

        fireEvent.click(await screen.findByRole('button', { name: 'Continue with Authentik' }));

        await waitFor(() => expect(mocks.openExternal).toHaveBeenCalledWith('http://vora.test/api/auth/oidc/authentik/start?invite=tok-123'));
        expect(mocks.assign).not.toHaveBeenCalled();
    });

    it('then tells the person to finish in the browser and offers signing in with it, without starting a pairing by itself', async () => {
        renderRouted();

        fireEvent.click(await screen.findByRole('button', { name: 'Continue with Authentik' }));

        expect(await screen.findByText(/Finish creating your account with Authentik in your browser, then come back and sign in/)).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Sign in with your browser' })).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Register' })).not.toBeInTheDocument();
        expect(mocks.startPairing).not.toHaveBeenCalled();
        expect(mocks.openExternal).toHaveBeenCalledTimes(1);
    });

    it('signs in with the approved pairing, which the browser approves on its own page', async () => {
        mocks.pollPairing.mockResolvedValue({
            status: 'approved',
            auth: { accessToken: 'account-token', userId: 'user-1', displayName: 'Sam', isAdmin: false },
        });
        renderRouted();

        fireEvent.click(await screen.findByRole('button', { name: 'Continue with Authentik' }));
        fireEvent.click(await screen.findByRole('button', { name: 'Sign in with your browser' }));

        expect(await screen.findByText('Who is watching', {}, { timeout: 4000 })).toBeInTheDocument();
        expect(mocks.startPairing).toHaveBeenCalledTimes(1);
        expect(mocks.openExternal).toHaveBeenLastCalledWith('http://vora.test/link?code=BCDFGHJK');
        expect(localStorage.getItem('account_token')).toBe('account-token');
        expect(localStorage.getItem('user_id')).toBe('user-1');
        expect(sessionStorage.getItem('pending_server_name')).toBe('Home');
    });

    it('goes back to the form from the browser step', async () => {
        renderRouted();

        fireEvent.click(await screen.findByRole('button', { name: 'Continue with Authentik' }));
        fireEvent.click(await screen.findByRole('button', { name: 'Back' }));

        expect(await screen.findByRole('button', { name: 'Register' })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Continue with Authentik' })).toBeInTheDocument();
    });

    it('says so, and stays on the form, when the browser could not be opened', async () => {
        mocks.openExternal.mockResolvedValue(false);
        renderRouted();

        fireEvent.click(await screen.findByRole('button', { name: 'Continue with Authentik' }));

        expect(await screen.findByText(/could not open your browser, so it cannot continue with Authentik/)).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Register' })).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Sign in with your browser' })).not.toBeInTheDocument();
        expect(mocks.assign).not.toHaveBeenCalled();
    });

    it('keeps sending the window to the provider when this Desktop cannot open a browser', async () => {
        window.voraDesktop = fakeDesktopBridge({ openExternal: undefined }).bridge;
        renderRouted();

        fireEvent.click(await screen.findByRole('button', { name: 'Continue with Authentik' }));

        expect(mocks.assign).toHaveBeenCalledWith('http://vora.test/api/auth/oidc/authentik/start');
        expect(mocks.openExternal).not.toHaveBeenCalled();
    });
});

describe('RegisterPage with a directory', () => {
    afterEach(cleanup);

    beforeEach(() => {
        localStorage.clear();
        sessionStorage.clear();
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.probeServer.mockResolvedValue({ isClaimed: true, registrationMode: 1, serverName: 'Home', emailEnabled: false });
    });

    it('points directory people to the sign-in page and keeps the password form while sign-up is open', async () => {
        mocks.getProviders.mockResolvedValue(providers({ providers: [], directorySignInEnabled: true }));
        renderAt();

        expect(await screen.findByText(/You do not need to register: sign in with your directory username/)).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Register' })).toBeInTheDocument();
    });

    it('with password sign-in off and only a directory, shows no form and says where to sign in', async () => {
        mocks.getProviders.mockResolvedValue(providers({ providers: [], directorySignInEnabled: true, passwordSignInEnabled: false }));
        renderAt();

        expect(await screen.findByText(/does not take new password accounts/)).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Register' })).not.toBeInTheDocument();
        expect(screen.queryByLabelText('Display Name')).not.toBeInTheDocument();
    });

    it('says nothing about a directory when there is none', async () => {
        mocks.getProviders.mockResolvedValue(providers({ providers: [] }));
        renderAt();

        expect(await screen.findByRole('button', { name: 'Register' })).toBeInTheDocument();
        expect(screen.queryByText(/directory username/)).not.toBeInTheDocument();
    });
});
