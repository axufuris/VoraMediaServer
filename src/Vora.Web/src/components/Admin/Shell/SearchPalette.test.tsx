import { describe, it, expect, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import SearchPalette from './SearchPalette';

vi.mock('../../../api/System/pluginAdminService', () => ({ pluginAdminService: { getPlugins: () => Promise.resolve([]) } }));

const Where = () => <div data-testid="where">{useLocation().pathname}</div>;

const renderPalette = (path = '/admin') => render(
    <MemoryRouter initialEntries={[path]}>
        <Routes>
            <Route path="/admin/server/:serverId/*" element={<><SearchPalette isOpen onClose={() => { }} /><Where /></>} />
            <Route path="/admin/*" element={<><SearchPalette isOpen onClose={() => { }} /><Where /></>} />
        </Routes>
    </MemoryRouter>
);

const search = (text: string) => fireEvent.change(screen.getByPlaceholderText('Jump to an admin page…'), { target: { value: text } });

describe('the Cmd-K palette and External Connections', () => {
    it.each([
        ['external'],
        ['connections'],
        ['request servers'],
        ['webhooks'],
        ['webhook'],
        ['radarr'],
        ['sonarr'],
        ['lidarr'],
        ['integration'],
    ])('finds it for "%s"', text => {
        renderPalette();

        search(text);

        expect(screen.getByRole('button', { name: /External Connections/ })).toBeInTheDocument();
    });

    it('shows no Library Updates entry', () => {
        renderPalette();

        expect(screen.getByRole('button', { name: /External Connections/ })).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /Library Updates/ })).not.toBeInTheDocument();
    });

    it('no longer offers System Settings for "request servers"', () => {
        renderPalette();

        search('request servers');

        expect(screen.queryByRole('button', { name: /System Settings/ })).not.toBeInTheDocument();
    });

    it('opens the page, which then picks its default tab', () => {
        renderPalette();

        search('external');
        fireEvent.click(screen.getByRole('button', { name: /External Connections/ }));

        expect(screen.getByTestId('where')).toHaveTextContent('/admin/external-connections');
    });

    it('opens it under the server on the multi-server form', () => {
        renderPalette('/admin/server/s1');

        search('external');
        fireEvent.click(screen.getByRole('button', { name: /External Connections/ }));

        expect(screen.getByTestId('where')).toHaveTextContent('/admin/server/s1/external-connections');
    });
});
