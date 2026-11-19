import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import SettingsPage from './SettingsPage';

vi.mock('../../api/client', () => ({ apiClient: { get: () => Promise.resolve({ data: [] }) } }));
vi.mock('../../api/System/systemSettingsAdminService', () => ({ systemSettingsAdminService: { getHardware: () => Promise.resolve(null) } }));
vi.mock('../../components/Admin/Settings/CoreSettingsTab', () => ({ default: () => <p>Core tab content</p> }));
vi.mock('../../components/Admin/Settings/RemoteAccessTab', () => ({ default: () => <p>Remote Access tab content</p> }));
vi.mock('../../components/Admin/Settings/EmailTab', () => ({ default: () => <p>Email tab content</p> }));
vi.mock('../../components/Admin/Settings/StorageTab', () => ({ default: () => <p>Storage tab content</p> }));

const renderPage = (path = '/admin/settings') => render(
    <MemoryRouter initialEntries={[path]}>
        <Routes>
            <Route path="/admin/settings" element={<SettingsPage />} />
            <Route path="/admin/server/:serverId/settings" element={<SettingsPage />} />
        </Routes>
    </MemoryRouter>
);

describe('System Settings tabs', () => {
    beforeEach(() => localStorage.clear());
    afterEach(cleanup);

    it('are Core, Remote Access, Email and Storage, with no Request Servers', () => {
        renderPage();

        const tabs = screen.getAllByRole('button', { name: /^(Core|Remote Access|Email|Storage|Request Servers)$/ }).map(tab => tab.textContent);

        expect(tabs).toEqual(['Core', 'Remote Access', 'Email', 'Storage']);
        expect(screen.queryByText(/request provider/i)).not.toBeInTheDocument();
    });

    it('open on Core by default', () => {
        renderPage();

        expect(screen.getByText('Core tab content')).toBeInTheDocument();
    });

    it('remember the tab that was open', () => {
        const { unmount } = renderPage();
        fireEvent.click(screen.getByRole('button', { name: 'Email' }));
        expect(screen.getByText('Email tab content')).toBeInTheDocument();
        unmount();

        renderPage();

        expect(screen.getByText('Email tab content')).toBeInTheDocument();
        expect(localStorage.getItem('admin_settings_tab_global')).toBe('email');
    });

    it('fall back to Core when the remembered tab was Request Servers, instead of showing nothing', () => {
        localStorage.setItem('admin_settings_tab_global', 'requests');

        renderPage();

        expect(screen.getByText('Core tab content')).toBeInTheDocument();
        expect(screen.queryByText('Remote Access tab content')).not.toBeInTheDocument();
        expect(localStorage.getItem('admin_settings_tab_global')).toBe('core');
    });

    it('fall back to Core for the same remembered tab on a server of the multi-server form', () => {
        localStorage.setItem('admin_settings_tab_s1', 'requests');

        renderPage('/admin/server/s1/settings');

        expect(screen.getByText('Core tab content')).toBeInTheDocument();
        expect(localStorage.getItem('admin_settings_tab_s1')).toBe('core');
    });

    it('fall back to Core for any value that is not a tab', () => {
        localStorage.setItem('admin_settings_tab_global', 'something-else');

        renderPage();

        expect(screen.getByText('Core tab content')).toBeInTheDocument();
    });

    it('keep a remembered tab that still exists', () => {
        localStorage.setItem('admin_settings_tab_global', 'storage');

        renderPage();

        expect(screen.getByText('Storage tab content')).toBeInTheDocument();
    });
});
