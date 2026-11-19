import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import App from './App';
import { serverVault } from './utils/serverVault';
import { StorageKeys } from './utils/storageKeys';
import { rememberPostSignIn } from './utils/postSignInRedirect';

vi.mock('./layouts/MainLayout', async () => {
    const { Outlet } = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
    return { default: () => <Outlet /> };
});
vi.mock('./components/Admin/Shell/AdminShell', async () => {
    const { Outlet } = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
    return { default: () => <Outlet /> };
});
vi.mock('./contexts/PlayerContext', () => ({ PlayerProvider: ({ children }: { children: ReactNode }) => <>{children}</> }));
vi.mock('./theme/ThemeProvider', () => ({ ThemeProvider: ({ children }: { children: ReactNode }) => <>{children}</> }));
vi.mock('./theme/ClientTemplateProvider', () => ({ ClientTemplateProvider: ({ children }: { children: ReactNode }) => <>{children}</> }));
vi.mock('./pages/Client/HomePage', () => ({ default: () => <h1>Home page</h1> }));
vi.mock('./pages/Client/SearchPage', () => ({ default: () => <h1>Search page</h1> }));
vi.mock('./pages/Auth/LinkDevicePage', () => ({ default: () => <h1>Link page</h1> }));
vi.mock('./pages/Admin/ExternalConnectionsPage', async () => {
    const { useLocation, useParams } = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
    const ExternalConnectionsStub = () => {
        const { tab } = useParams<{ tab?: string }>();
        return <h1>External Connections page at {useLocation().pathname} tab {tab ?? 'none'}</h1>;
    };
    return { default: ExternalConnectionsStub };
});

describe('client routes for a server', () => {
    beforeEach(() => {
        localStorage.clear();
        serverVault.addOrUpdateServer({ id: 'home', name: 'Home', url: 'https://vora.example.com', token: 'profile-token', profileId: 'profile', isAdmin: true });
        localStorage.setItem(StorageKeys.profileToken, 'profile-token');
    });

    it('opens Home at the server address that Back to client goes to', async () => {
        window.history.pushState({}, '', '/server/home');

        render(<App />);

        expect(await screen.findByRole('heading', { name: 'Home page' })).toBeInTheDocument();
    });

    it('still opens the pages under the server', async () => {
        window.history.pushState({}, '', '/server/home/search');

        render(<App />);

        expect(await screen.findByRole('heading', { name: 'Search page' })).toBeInTheDocument();
    });

    it('takes a visitor back to the link page they were signing in for', async () => {
        sessionStorage.clear();
        rememberPostSignIn('/link?code=BCDFGHJK');
        window.history.pushState({}, '', '/');

        render(<App />);

        expect(await screen.findByRole('heading', { name: 'Link page' })).toBeInTheDocument();
        sessionStorage.clear();
    });

    it('leaves other pages alone when a link page is remembered', async () => {
        sessionStorage.clear();
        rememberPostSignIn('/link');
        window.history.pushState({}, '', '/server/home/search');

        render(<App />);

        expect(await screen.findByRole('heading', { name: 'Search page' })).toBeInTheDocument();
        sessionStorage.clear();
    });
});

describe('admin routes', () => {
    beforeEach(() => {
        localStorage.clear();
        serverVault.addOrUpdateServer({ id: 'home', name: 'Home', url: 'https://vora.example.com', token: 'profile-token', profileId: 'profile', isAdmin: true });
        localStorage.setItem(StorageKeys.profileToken, 'profile-token');
        localStorage.setItem(StorageKeys.isServerAdmin, 'true');
    });

    it('opens External Connections on a tab', async () => {
        window.history.pushState({}, '', '/admin/external-connections/webhooks');

        render(<App />);

        expect(await screen.findByRole('heading', { name: 'External Connections page at /admin/external-connections/webhooks tab webhooks' })).toBeInTheDocument();
    });

    it('opens External Connections with no tab, for the page to pick one', async () => {
        window.history.pushState({}, '', '/admin/external-connections');

        render(<App />);

        expect(await screen.findByRole('heading', { name: 'External Connections page at /admin/external-connections tab none' })).toBeInTheDocument();
    });

    it('opens External Connections on a tab on a server', async () => {
        window.history.pushState({}, '', '/admin/server/home/external-connections/request-servers');

        render(<App />);

        expect(await screen.findByRole('heading', { name: 'External Connections page at /admin/server/home/external-connections/request-servers tab request-servers' })).toBeInTheDocument();
    });

    it('opens External Connections with no tab on a server', async () => {
        window.history.pushState({}, '', '/admin/server/home/external-connections');

        render(<App />);

        expect(await screen.findByRole('heading', { name: 'External Connections page at /admin/server/home/external-connections tab none' })).toBeInTheDocument();
    });

    it('sends the old Library Updates address to the Webhooks tab', async () => {
        window.history.pushState({}, '', '/admin/library-updates');

        render(<App />);

        expect(await screen.findByRole('heading', { name: 'External Connections page at /admin/external-connections/webhooks tab webhooks' })).toBeInTheDocument();
        expect(window.location.pathname).toBe('/admin/external-connections/webhooks');
    });

    it('sends the old Library Updates address on a server to the Webhooks tab on that server', async () => {
        window.history.pushState({}, '', '/admin/server/home/library-updates');

        render(<App />);

        expect(await screen.findByRole('heading', { name: 'External Connections page at /admin/server/home/external-connections/webhooks tab webhooks' })).toBeInTheDocument();
        expect(window.location.pathname).toBe('/admin/server/home/external-connections/webhooks');
    });

    it('replaces the old address, so Back does not bounce through it again', async () => {
        window.history.pushState({}, '', '/admin/library-updates');
        const entriesBefore = window.history.length;

        render(<App />);

        await screen.findByRole('heading', { name: /External Connections page/ });
        expect(window.history.length).toBe(entriesBefore);
    });
});
