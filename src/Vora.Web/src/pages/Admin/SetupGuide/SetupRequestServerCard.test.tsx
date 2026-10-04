import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import SetupRequestServerCard from './SetupRequestServerCard';
import type { RequestServerVM } from '../../../api/Discovery/requestAdminService';

const mocks = vi.hoisted(() => ({ getProviderOptions: vi.fn(), saveServer: vi.fn() }));
vi.mock('../../../api/Discovery/requestAdminService', () => ({ requestAdminService: { getProviderOptions: mocks.getProviderOptions, saveServer: mocks.saveServer } }));

const renderCard = (kind: 'radarr' | 'sonarr', existing?: RequestServerVM, onSaved = vi.fn()) => {
    render(<MemoryRouter><SetupRequestServerCard kind={kind} existing={existing} onSaved={onSaved} /></MemoryRouter>);
    return onSaved;
};

describe('SetupRequestServerCard', () => {
    beforeEach(() => {
        mocks.getProviderOptions.mockReset();
        mocks.saveServer.mockReset();
        mocks.saveServer.mockResolvedValue(undefined);
        mocks.getProviderOptions.mockImplementation((req: { optionType: string }) => Promise.resolve(
            req.optionType === 'qualityProfiles' ? [{ id: '4', name: 'HD-1080p' }, { id: '5', name: 'Ultra-HD' }] : [{ id: '1', name: '/data/movies' }]
        ));
    });

    it('connects, then saves the profile, folder, availability, search and calendar choices', async () => {
        const onSaved = renderCard('radarr');

        fireEvent.change(screen.getByLabelText('Address'), { target: { value: '192.168.1.20' } });
        fireEvent.change(screen.getByLabelText('API key'), { target: { value: 'secret-key' } });
        fireEvent.click(screen.getByRole('button', { name: 'Test connection' }));

        expect(await screen.findByLabelText('Quality profile')).toHaveValue('4');
        expect(screen.getByLabelText('Root folder')).toHaveValue('/data/movies');
        expect(screen.getByLabelText('Minimum availability')).toHaveValue('released');
        expect(screen.getByText(/the request is added to Radarr but nothing is downloaded/)).toBeInTheDocument();

        fireEvent.click(screen.getByLabelText(/Search automatically/));
        fireEvent.click(screen.getByRole('button', { name: 'Save Radarr' }));

        await waitFor(() => expect(onSaved).toHaveBeenCalled());
        const saved: RequestServerVM = mocks.saveServer.mock.calls[0][0];
        expect(saved).toEqual(expect.objectContaining({ providerId: 'radarr_requester', hostname: '192.168.1.20', port: 7878, providesReleaseCalendar: true }));
        expect(JSON.parse(saved.providerSettingsJson)).toEqual({ qualityProfileId: 4, rootFolderPath: '/data/movies', minimumAvailability: 'released', searchOnAdd: false });
    });

    it('asks for the address and key before trying to connect', async () => {
        renderCard('sonarr');

        fireEvent.click(screen.getByRole('button', { name: 'Test connection' }));

        expect(await screen.findByRole('alert')).toHaveTextContent('Enter the address and the API key first.');
        expect(mocks.getProviderOptions).not.toHaveBeenCalled();
    });

    it('has no minimum availability for Sonarr', async () => {
        renderCard('sonarr');

        fireEvent.change(screen.getByLabelText('Address'), { target: { value: 'sonarr.local' } });
        fireEvent.change(screen.getByLabelText('API key'), { target: { value: 'secret-key' } });
        fireEvent.click(screen.getByRole('button', { name: 'Test connection' }));

        expect(await screen.findByLabelText('Quality profile')).toBeInTheDocument();
        expect(screen.queryByLabelText('Minimum availability')).not.toBeInTheDocument();
    });

    it('shows a server that is already set up instead of a form', () => {
        renderCard('sonarr', { id: 's1', name: 'Sonarr', providerId: 'sonarr_requester', mediaType: 'TvShow', hostname: 'sonarr.local', port: 8989, useSsl: false, apiKey: 'k', urlBase: '', isDefault: true, is4K: false, providesReleaseCalendar: true, providerSettingsJson: '{}', isEnabled: true });

        expect(screen.getByText('http://sonarr.local:8989')).toBeInTheDocument();
        expect(screen.getByText('Release Calendar')).toBeInTheDocument();
        expect(screen.queryByLabelText('API key')).not.toBeInTheDocument();
    });
});
