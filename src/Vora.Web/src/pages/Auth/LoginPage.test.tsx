import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import LoginPage from './LoginPage';
import type { SignInProviders } from '../../api/Auth/externalSignInService';

const mocks = vi.hoisted(() => ({
    probeServer: vi.fn(),
    loginToServer: vi.fn(),
    getProviders: vi.fn<() => Promise<SignInProviders>>(),
    assign: vi.fn<(url: string) => void>(),
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
