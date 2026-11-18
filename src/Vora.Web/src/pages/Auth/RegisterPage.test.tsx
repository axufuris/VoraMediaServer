import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import RegisterPage from './RegisterPage';
import type { SignInProviders } from '../../api/Auth/externalSignInService';

const mocks = vi.hoisted(() => ({
    probeServer: vi.fn(),
    validateInvitation: vi.fn(),
    getProviders: vi.fn<() => Promise<SignInProviders>>(),
    assign: vi.fn<(url: string) => void>(),
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
