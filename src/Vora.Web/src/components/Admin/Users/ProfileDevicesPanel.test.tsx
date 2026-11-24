import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { AxiosError, AxiosHeaders } from 'axios';
import ProfileDevicesPanel from './ProfileDevicesPanel';
import { DialogProvider } from '../../../dialogs';
import type { DeviceSettingsVM, ProfileDeviceVM } from '../../../api/Users/profileService';

const mocks = vi.hoisted(() => ({
    adminGetDeviceSettings: vi.fn(),
    adminSaveDeviceSettings: vi.fn(),
}));

vi.mock('../../../api/Users/profileService', () => ({
    profileService: { adminGetDeviceSettings: mocks.adminGetDeviceSettings, adminSaveDeviceSettings: mocks.adminSaveDeviceSettings },
}));

const shipped: DeviceSettingsVM = { bitrate: 0, maxResolution: 0, seekStepSeconds: 10, volumeStepPercent: 5, soundOnClick: true, soundOnNavOpen: true, soundOnMove: true };

const tv: ProfileDeviceVM = { deviceId: 'tv', name: 'Living room', clientName: 'Vora Android', deviceType: 'TV', operatingSystem: 'Android', settings: { ...shipped, bitrate: 20, seekStepSeconds: 30 } };
const phone: ProfileDeviceVM = { deviceId: 'phone', name: 'Sam phone', clientName: 'Vora Android', deviceType: 'Phone', operatingSystem: 'Android', settings: shipped };

const renderPanel = () => render(<DialogProvider><ProfileDevicesPanel profileId="p-sam" serverId="s1" /></DialogProvider>);

describe('ProfileDevicesPanel', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.adminGetDeviceSettings.mockResolvedValue([tv, phone]);
    });

    it('shows each device with what it uses today', async () => {
        renderPanel();

        expect(await screen.findByText('Living room')).toBeInTheDocument();
        expect(screen.getByText('20 Mbps · Original · skip 30 s · volume 5%')).toBeInTheDocument();
        expect(screen.getByText('Original (no limit) · Original · skip 10 s · volume 5%')).toBeInTheDocument();
        expect(mocks.adminGetDeviceSettings).toHaveBeenCalledWith('p-sam', 's1');
    });

    it('edits one device and sends only that device', async () => {
        mocks.adminSaveDeviceSettings.mockResolvedValue([{ ...tv, settings: { ...tv.settings, maxResolution: 720 } }, phone]);
        renderPanel();

        fireEvent.click(await screen.findByRole('button', { name: 'Edit Living room' }));
        fireEvent.change(screen.getByLabelText('Maximum resolution'), { target: { value: '720' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save device' }));

        await waitFor(() => expect(mocks.adminSaveDeviceSettings).toHaveBeenCalledWith('p-sam', {
            deviceIds: ['tv'],
            settings: { bitrate: 20, maxResolution: 720, seekStepSeconds: 30, volumeStepPercent: 5, soundOnClick: true, soundOnNavOpen: true, soundOnMove: true },
        }, 's1'));
        expect(await screen.findByRole('status')).toHaveTextContent('Saved the settings for Living room.');
        expect(screen.getByText('20 Mbps · HD (720p) · skip 30 s · volume 5%')).toBeInTheDocument();
    });

    it('sets the same on every device, starting from the most recently used one', async () => {
        mocks.adminSaveDeviceSettings.mockResolvedValue([tv, phone]);
        renderPanel();

        fireEvent.click(await screen.findByRole('button', { name: 'Set the same on every device…' }));
        const all = screen.getByText(/replaces the settings on all 2 devices/).parentElement as HTMLElement;
        expect(within(all).getByLabelText('Maximum bitrate')).toHaveValue('20');
        fireEvent.change(within(all).getByLabelText('Maximum bitrate'), { target: { value: '4' } });
        fireEvent.click(within(all).getByRole('button', { name: 'Apply to every device' }));

        await waitFor(() => expect(mocks.adminSaveDeviceSettings).toHaveBeenCalledWith('p-sam', {
            deviceIds: null,
            settings: expect.objectContaining({ bitrate: 4, seekStepSeconds: 30 }),
        }, 's1'));
        expect(await screen.findByRole('status')).toHaveTextContent('Saved the settings on every device.');
    });

    it('offers the same-on-every-device action only when there is more than one device', async () => {
        mocks.adminGetDeviceSettings.mockResolvedValue([tv]);
        renderPanel();

        await screen.findByText('Living room');

        expect(screen.queryByRole('button', { name: 'Set the same on every device…' })).toBeNull();
    });

    it('keeps the form open and says why when a save is refused', async () => {
        mocks.adminSaveDeviceSettings.mockRejectedValue(new AxiosError('bad', '400', undefined, undefined, { status: 400, statusText: 'Bad', headers: {}, config: { headers: new AxiosHeaders() }, data: { title: 'Bad request', detail: 'That is not a skip step.' } }));
        renderPanel();

        fireEvent.click(await screen.findByRole('button', { name: 'Edit Living room' }));
        fireEvent.change(screen.getByLabelText('Maximum bitrate'), { target: { value: '10' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save device' }));

        expect(await screen.findByText('Could not save the device settings: That is not a skip step.')).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'OK' }));
        expect(await screen.findByRole('button', { name: 'Save device' })).toBeInTheDocument();
    });

    it('says when a profile has used no device yet', async () => {
        mocks.adminGetDeviceSettings.mockResolvedValue([]);
        renderPanel();

        expect(await screen.findByText(/has not used a device yet/)).toBeInTheDocument();
    });

    it('says when the devices cannot be read', async () => {
        mocks.adminGetDeviceSettings.mockRejectedValue(new AxiosError('bad', '404', undefined, undefined, { status: 404, statusText: 'Not Found', headers: {}, config: { headers: new AxiosHeaders() }, data: { title: 'Not found', detail: 'Profile not found.' } }));
        renderPanel();

        expect(await screen.findByRole('alert')).toHaveTextContent('Profile not found.');
    });
});
