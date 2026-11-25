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

const tv: ProfileDeviceVM = { deviceId: 'tv', name: 'Living room', nickname: 'Big screen', clientName: 'Vora Android', deviceType: 'TV', operatingSystem: 'Android', lastConnectedAt: '2026-11-24T10:00:00Z', settings: { ...shipped, bitrate: 20, seekStepSeconds: 30 } };
const phone: ProfileDeviceVM = { deviceId: 'phone', name: 'Sam phone', nickname: '', clientName: 'Vora Android', deviceType: 'Phone', operatingSystem: 'Android', lastConnectedAt: '2026-11-23T10:00:00Z', settings: shipped };
const ghost: ProfileDeviceVM = { deviceId: 'ghost', name: 'ghost', nickname: '', clientName: '', deviceType: '', operatingSystem: '', lastConnectedAt: null, settings: shipped };

const renderPanel = (openDeviceId?: string) => render(<DialogProvider><ProfileDevicesPanel profileId="p-sam" serverId="s1" openDeviceId={openDeviceId} /></DialogProvider>);

const rowOf = (name: string) => screen.getByText(name).closest('tr') as HTMLElement;

describe('ProfileDevicesPanel', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.adminGetDeviceSettings.mockResolvedValue([tv, phone]);
    });

    it('shows each device by its nickname, with its own name beneath, and what it uses today', async () => {
        renderPanel();

        expect(await screen.findByText('Big screen')).toBeInTheDocument();
        expect(screen.getByText('Living room · Vora Android · TV · Android')).toBeInTheDocument();
        expect(within(rowOf('Big screen')).getByText('20 Mbps · Original · skip 30 s · volume 5%')).toBeInTheDocument();
        expect(within(rowOf('Sam phone')).getByText('Original (no limit) · Original · skip 10 s · volume 5%')).toBeInTheDocument();
        expect(mocks.adminGetDeviceSettings).toHaveBeenCalledWith('p-sam', 's1');
    });

    it('says a device the server only knows from its settings was not seen', async () => {
        mocks.adminGetDeviceSettings.mockResolvedValue([tv, ghost]);
        renderPanel();

        await screen.findByText('Big screen');

        expect(within(rowOf('ghost')).getByText('Not seen')).toBeInTheDocument();
    });

    it('edits one device, named by its nickname, and sends only that device', async () => {
        mocks.adminSaveDeviceSettings.mockResolvedValue([{ ...tv, settings: { ...tv.settings, maxResolution: 720 } }, phone]);
        renderPanel();

        fireEvent.click(await screen.findByRole('button', { name: 'Edit Big screen' }));
        expect(screen.getByText('Settings for Big screen')).toBeInTheDocument();
        fireEvent.change(screen.getByLabelText('Maximum resolution'), { target: { value: '720' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save device' }));

        await waitFor(() => expect(mocks.adminSaveDeviceSettings).toHaveBeenCalledWith('p-sam', {
            deviceIds: ['tv'],
            settings: { bitrate: 20, maxResolution: 720, seekStepSeconds: 30, volumeStepPercent: 5, soundOnClick: true, soundOnNavOpen: true, soundOnMove: true },
        }, 's1'));
        expect(await screen.findByRole('status')).toHaveTextContent('Saved the settings for Big screen.');
        expect(screen.getByText('20 Mbps · HD (720p) · skip 30 s · volume 5%')).toBeInTheDocument();
        expect(screen.queryByText('Settings for Big screen')).toBeNull();
    });

    it('opens the editor for the device it was sent to', async () => {
        renderPanel('phone');

        expect(await screen.findByText('Settings for Sam phone')).toBeInTheDocument();
        expect(screen.queryByText('Settings for Big screen')).toBeNull();
    });

    describe('setting the same on several devices', () => {
        it('applies one set of settings to the selected devices, starting from the first of them', async () => {
            mocks.adminSaveDeviceSettings.mockResolvedValue([tv, phone]);
            renderPanel();

            fireEvent.click(await screen.findByRole('checkbox', { name: 'Select Big screen' }));
            fireEvent.click(screen.getByRole('checkbox', { name: 'Select Sam phone' }));
            expect(screen.getByText('2 selected')).toBeInTheDocument();
            fireEvent.click(screen.getByRole('button', { name: 'Set the same on these 2…' }));

            expect(screen.getByText('Set the same on 2 devices')).toBeInTheDocument();
            expect(screen.getByLabelText('Maximum bitrate')).toHaveValue('20');
            fireEvent.change(screen.getByLabelText('Maximum bitrate'), { target: { value: '4' } });
            fireEvent.click(screen.getByRole('button', { name: 'Apply to all 2' }));

            await waitFor(() => expect(mocks.adminSaveDeviceSettings).toHaveBeenCalledWith('p-sam', {
                deviceIds: ['tv', 'phone'],
                settings: expect.objectContaining({ bitrate: 4, seekStepSeconds: 30 }),
            }, 's1'));
            expect(await screen.findByRole('status')).toHaveTextContent('Saved the settings on every device.');
            expect(screen.queryByText('2 selected')).toBeNull();
        });

        it('sends only the devices that were ticked, and says how many', async () => {
            mocks.adminGetDeviceSettings.mockResolvedValue([tv, phone, ghost]);
            mocks.adminSaveDeviceSettings.mockResolvedValue([tv, phone, ghost]);
            renderPanel();

            fireEvent.click(await screen.findByRole('checkbox', { name: 'Select Sam phone' }));
            fireEvent.click(screen.getByRole('checkbox', { name: 'Select ghost' }));
            fireEvent.click(screen.getByRole('button', { name: 'Set the same on these 2…' }));
            fireEvent.change(screen.getByLabelText('Maximum bitrate'), { target: { value: '8' } });
            fireEvent.click(screen.getByRole('button', { name: 'Apply to all 2' }));

            await waitFor(() => expect(mocks.adminSaveDeviceSettings).toHaveBeenCalledWith('p-sam', expect.objectContaining({ deviceIds: ['phone', 'ghost'] }), 's1'));
            expect(await screen.findByRole('status')).toHaveTextContent('Saved the settings on 2 devices.');
        });

        it('selects every device at once and offers the action for them all', async () => {
            renderPanel();

            fireEvent.click(await screen.findByRole('checkbox', { name: 'Select all shown devices' }));

            expect(screen.getByText('2 selected')).toBeInTheDocument();
            expect(screen.getByRole('checkbox', { name: 'Select Big screen' })).toBeChecked();
            fireEvent.click(screen.getByRole('checkbox', { name: 'Select all shown devices' }));
            expect(screen.queryByText('2 selected')).toBeNull();
        });

        it('words the action for one device as that device', async () => {
            renderPanel();

            fireEvent.click(await screen.findByRole('checkbox', { name: 'Select Sam phone' }));

            expect(screen.getByRole('button', { name: 'Set the same on this device…' })).toBeInTheDocument();
        });

        it('keeps the selection still while its settings are being chosen, and Cancel returns to it', async () => {
            renderPanel();
            fireEvent.click(await screen.findByRole('checkbox', { name: 'Select Big screen' }));
            fireEvent.click(screen.getByRole('button', { name: 'Set the same on this device…' }));

            expect(screen.getByRole('checkbox', { name: 'Select Sam phone' })).toBeDisabled();
            expect(screen.getByRole('checkbox', { name: 'Select all shown devices' })).toBeDisabled();
            fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

            expect(screen.queryByText('Set the same on 1 device')).toBeNull();
            expect(screen.getByRole('checkbox', { name: 'Select Big screen' })).toBeChecked();
            expect(screen.getByRole('checkbox', { name: 'Select Sam phone' })).not.toBeDisabled();
        });
    });

    describe('a long list', () => {
        const many = Array.from({ length: 7 }, (_, index): ProfileDeviceVM => ({
            ...phone, deviceId: `d${index}`, name: `Device ${index}`, nickname: index === 3 ? 'Kitchen' : '', clientName: 'Vora Roku',
        }));

        it('offers a filter only when there are enough devices to need one', async () => {
            renderPanel();
            await screen.findByText('Big screen');
            expect(screen.queryByLabelText('Filter devices')).toBeNull();
        });

        it('filters by nickname or name and counts what it shows', async () => {
            mocks.adminGetDeviceSettings.mockResolvedValue(many);
            renderPanel();

            fireEvent.change(await screen.findByLabelText('Filter devices'), { target: { value: 'kitchen' } });

            expect(screen.getByText('Showing 1 of 7 devices')).toBeInTheDocument();
            expect(screen.getByText('Kitchen')).toBeInTheDocument();
            expect(screen.queryByText('Device 0')).toBeNull();
        });

        it('says when nothing matches', async () => {
            mocks.adminGetDeviceSettings.mockResolvedValue(many);
            renderPanel();

            fireEvent.change(await screen.findByLabelText('Filter devices'), { target: { value: 'zzz' } });

            expect(screen.getByText('No device matches “zzz”.')).toBeInTheDocument();
        });

        it('applies a selection only to the devices still shown', async () => {
            mocks.adminGetDeviceSettings.mockResolvedValue(many);
            mocks.adminSaveDeviceSettings.mockResolvedValue(many);
            renderPanel();

            fireEvent.click(await screen.findByRole('checkbox', { name: 'Select Device 0' }));
            fireEvent.click(screen.getByRole('checkbox', { name: 'Select Kitchen' }));
            fireEvent.change(screen.getByLabelText('Filter devices'), { target: { value: 'kitchen' } });
            expect(screen.getByText('1 selected')).toBeInTheDocument();
            fireEvent.click(screen.getByRole('button', { name: 'Set the same on this device…' }));
            fireEvent.change(screen.getByLabelText('Maximum bitrate'), { target: { value: '8' } });
            fireEvent.click(screen.getByRole('button', { name: 'Apply to this device' }));

            await waitFor(() => expect(mocks.adminSaveDeviceSettings).toHaveBeenCalledWith('p-sam', expect.objectContaining({ deviceIds: ['d3'] }), 's1'));
        });
    });

    it('keeps the form open and says why when a save is refused', async () => {
        mocks.adminSaveDeviceSettings.mockRejectedValue(new AxiosError('bad', '400', undefined, undefined, { status: 400, statusText: 'Bad', headers: {}, config: { headers: new AxiosHeaders() }, data: { title: 'Bad request', detail: 'That is not a skip step.' } }));
        renderPanel();

        fireEvent.click(await screen.findByRole('button', { name: 'Edit Big screen' }));
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
