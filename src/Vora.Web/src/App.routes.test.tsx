import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import App from './App';
import { serverVault } from './utils/serverVault';
import { StorageKeys } from './utils/storageKeys';

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
});
