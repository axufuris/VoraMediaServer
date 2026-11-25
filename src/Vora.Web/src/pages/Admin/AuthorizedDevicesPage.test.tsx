import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { AxiosError, AxiosHeaders } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import AuthorizedDevicesPage from './AuthorizedDevicesPage';
import { DialogProvider } from '../../dialogs';
import type { ClientDeviceVM } from '../../api/Users/deviceService';
import type { UserVM } from '../../api/Users/userService';

const mocks = vi.hoisted(() => ({
    getDevices: vi.fn(),
    setNickname: vi.fn(),
    blockDevice: vi.fn(),
    unblockDevice: vi.fn(),
    deleteDevice: vi.fn(),
    getAllUsers: vi.fn(),
}));

vi.mock('../../api/Users/deviceService', () => ({
    deviceService: {
        getDevices: mocks.getDevices,
        setNickname: mocks.setNickname,
        blockDevice: mocks.blockDevice,
        unblockDevice: mocks.unblockDevice,
        deleteDevice: mocks.deleteDevice,
    },
}));
vi.mock('../../api/Users/userService', () => ({ userService: { getAllUsers: mocks.getAllUsers } }));

const users = [
    { id: 'u-sam', displayName: 'Sam', profiles: [{ id: 'p-sam', name: 'Sam' }, { id: 'p-kids', name: 'Kids' }] },
    { id: 'u-alex', displayName: 'Alex', profiles: [{ id: 'p-alex', name: 'Alex' }] },
] as unknown as UserVM[];

const device = (patch: Partial<ClientDeviceVM>): ClientDeviceVM => ({
    id: `id-${patch.deviceId}`,
    deviceId: 'device',
    clientName: 'Vora Android',
    deviceName: 'Device',
    nickname: '',
    deviceType: 'TV',
    lastIpAddress: '10.0.0.2',
    operatingSystem: 'Android',
    location: 'Local Network',
    lastConnectedAt: '2026-11-24T10:00:00Z',
    isBlocked: false,
    ...patch,
});

const tv = device({ deviceId: 'tv-1', deviceName: 'Shield', nickname: 'Living room', lastUserId: 'u-sam', lastProfileId: 'p-kids' });
const phone = device({ deviceId: 'phone-1', deviceName: 'Pixel 8', deviceType: 'Phone', lastUserId: 'u-alex', lastProfileId: 'p-alex' });
const second = device({ deviceId: 'tablet-1', deviceName: 'Galaxy Tab', deviceType: 'Tablet', lastUserId: 'u-sam', lastProfileId: 'p-sam', isBlocked: true });
const stray = device({ deviceId: 'roku-1', deviceName: 'Roku Ultra', clientName: 'Vora Roku' });

const problem = (status: number, detail: string) => new AxiosError('bad', String(status), undefined, undefined, { status, statusText: 'x', headers: {}, config: { headers: new AxiosHeaders() }, data: { title: 'Bad', detail } });

const renderPage = () => render(
    <DialogProvider>
        <MemoryRouter initialEntries={['/admin/server/s1/devices']}>
            <Routes>
                <Route path="/admin/server/:serverId/devices" element={<AuthorizedDevicesPage />} />
            </Routes>
        </MemoryRouter>
    </DialogProvider>,
);

const rowFor = (name: string) => screen.getByText(name).closest('tr') as HTMLElement;

describe('AuthorizedDevicesPage', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        localStorage.clear();
        mocks.getDevices.mockResolvedValue([tv, phone, second, stray]);
        mocks.getAllUsers.mockResolvedValue(users);
    });

    it('lists every device with its nickname first and its own name beneath', async () => {
        renderPage();

        await screen.findByText('Living room');
        const row = within(rowFor('Living room'));
        expect(row.getByText('Shield · Android · TV')).toBeInTheDocument();
        expect(row.getByText('Kids')).toBeInTheDocument();
        expect(row.getByText('(Sam)')).toBeInTheDocument();
        expect(within(rowFor('Galaxy Tab')).queryByText('(Sam)')).toBeNull();
        expect(screen.getByText('4 devices')).toBeInTheDocument();
        expect(mocks.getDevices).toHaveBeenCalledWith('s1');
    });

    it('says when no device has connected yet', async () => {
        mocks.getDevices.mockResolvedValue([]);
        renderPage();

        expect(await screen.findByText('No devices yet')).toBeInTheDocument();
        expect(screen.queryByLabelText('Device')).toBeNull();
    });

    describe('filtering', () => {
        it('narrows the list to the devices that match what was typed', async () => {
            renderPage();

            fireEvent.change(await screen.findByLabelText('Device'), { target: { value: 'pixel' } });

            expect(screen.getByText('Pixel 8')).toBeInTheDocument();
            expect(screen.queryByText('Living room')).toBeNull();
            expect(screen.getByText('Showing 1 of 4 devices')).toBeInTheDocument();
        });

        it('narrows the list to one last user, with a count beside each name', async () => {
            renderPage();

            const select = await screen.findByLabelText('Last user');
            expect(within(select).getByRole('option', { name: 'Sam (2)' })).toBeInTheDocument();
            expect(within(select).getByRole('option', { name: 'Anonymous (1)' })).toBeInTheDocument();
            fireEvent.change(select, { target: { value: 'u-sam' } });

            expect(screen.getByText('Living room')).toBeInTheDocument();
            expect(screen.getByText('Galaxy Tab')).toBeInTheDocument();
            expect(screen.queryByText('Pixel 8')).toBeNull();
            expect(screen.getByText('Showing 2 of 4 devices')).toBeInTheDocument();
        });

        it('says so when nothing matches, and clears the filters on request', async () => {
            renderPage();

            fireEvent.change(await screen.findByLabelText('Device'), { target: { value: 'nothing like it' } });
            expect(screen.getByText('No devices match')).toBeInTheDocument();

            fireEvent.click(screen.getByRole('button', { name: 'Clear filters' }));

            expect(screen.getByText('Living room')).toBeInTheDocument();
            expect(screen.queryByRole('button', { name: 'Clear filters' })).toBeNull();
        });
    });

    describe('grouping by user', () => {
        it('puts each device under the account that used it last, in name order', async () => {
            renderPage();

            fireEvent.click(await screen.findByRole('button', { name: 'By user' }));

            const groups = screen.getAllByRole('region');
            expect(groups.map(group => group.getAttribute('aria-label'))).toEqual(['Alex', 'Sam', 'Anonymous']);
            expect(within(groups[1]).getByText('2 devices · 1 blocked')).toBeInTheDocument();
            expect(within(groups[1]).getByText('Living room')).toBeInTheDocument();
            expect(within(groups[1]).getByText('Kids')).toBeInTheDocument();
            expect(within(groups[1]).getByRole('columnheader', { name: 'Last Profile' })).toBeInTheDocument();
        });

        it('folds one account away and back', async () => {
            renderPage();
            fireEvent.click(await screen.findByRole('button', { name: 'By user' }));

            const header = within(screen.getByRole('region', { name: 'Sam' })).getByRole('button', { expanded: true });
            fireEvent.click(header);
            expect(screen.queryByText('Living room')).toBeNull();
            expect(header).toHaveAttribute('aria-expanded', 'false');

            fireEvent.click(header);
            expect(screen.getByText('Living room')).toBeInTheDocument();
        });

        it('applies the filters to the groups and drops the ones left empty', async () => {
            renderPage();
            fireEvent.click(await screen.findByRole('button', { name: 'By user' }));

            fireEvent.change(screen.getByLabelText('Device'), { target: { value: 'pixel' } });

            expect(screen.getAllByRole('region').map(group => group.getAttribute('aria-label'))).toEqual(['Alex']);
        });

        it('remembers the layout for next time', async () => {
            const first = renderPage();
            fireEvent.click(await screen.findByRole('button', { name: 'By user' }));
            first.unmount();

            renderPage();

            expect(await screen.findByRole('button', { name: 'By user' })).toHaveAttribute('aria-pressed', 'true');
            expect(screen.getAllByRole('region')).toHaveLength(3);
        });
    });

    describe('nicknames', () => {
        it('names a device and shows the nickname straight away', async () => {
            mocks.setNickname.mockResolvedValue(undefined);
            renderPage();

            fireEvent.click(await screen.findByRole('button', { name: 'Rename Pixel 8' }));
            fireEvent.change(screen.getByLabelText('Nickname for Pixel 8'), { target: { value: '  Alex   phone ' } });
            fireEvent.click(screen.getByRole('button', { name: 'Save' }));

            await waitFor(() => expect(mocks.setNickname).toHaveBeenCalledWith('id-phone-1', 'Alex phone', 's1'));
            expect(await screen.findByText('Alex phone')).toBeInTheDocument();
            expect(screen.getByText('Pixel 8 · Android · Phone')).toBeInTheDocument();
            expect(screen.getByRole('status')).toHaveTextContent('Saved the nickname “Alex phone”.');
            expect(screen.queryByLabelText('Nickname for Pixel 8')).toBeNull();
        });

        it('takes a nickname off by saving it empty', async () => {
            mocks.setNickname.mockResolvedValue(undefined);
            renderPage();

            fireEvent.click(await screen.findByRole('button', { name: 'Rename Living room' }));
            fireEvent.change(screen.getByLabelText('Nickname for Living room'), { target: { value: '' } });
            fireEvent.click(screen.getByRole('button', { name: 'Save' }));

            await waitFor(() => expect(mocks.setNickname).toHaveBeenCalledWith('id-tv-1', '', 's1'));
            expect(await screen.findByText('Shield')).toBeInTheDocument();
            expect(screen.queryByText('Living room')).toBeNull();
            expect(screen.getByRole('status')).toHaveTextContent('Removed the nickname from Shield.');
        });

        it('offers the current nickname to change', async () => {
            renderPage();

            fireEvent.click(await screen.findByRole('button', { name: 'Rename Living room' }));

            expect(screen.getByLabelText('Nickname for Living room')).toHaveValue('Living room');
        });

        it('leaves the editor open and says why when the server refuses', async () => {
            mocks.setNickname.mockRejectedValue(problem(400, 'A nickname can be at most 64 characters.'));
            renderPage();

            fireEvent.click(await screen.findByRole('button', { name: 'Rename Pixel 8' }));
            fireEvent.change(screen.getByLabelText('Nickname for Pixel 8'), { target: { value: 'x' } });
            fireEvent.click(screen.getByRole('button', { name: 'Save' }));

            expect(await screen.findByRole('alert')).toHaveTextContent('A nickname can be at most 64 characters.');
            expect(screen.getByLabelText('Nickname for Pixel 8')).toBeInTheDocument();
        });

        it('stops without saving on Cancel or Escape', async () => {
            renderPage();

            fireEvent.click(await screen.findByRole('button', { name: 'Rename Pixel 8' }));
            fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
            expect(screen.queryByLabelText('Nickname for Pixel 8')).toBeNull();

            fireEvent.click(screen.getByRole('button', { name: 'Rename Pixel 8' }));
            fireEvent.keyDown(screen.getByLabelText('Nickname for Pixel 8'), { key: 'Escape' });
            expect(screen.queryByLabelText('Nickname for Pixel 8')).toBeNull();
            expect(mocks.setNickname).not.toHaveBeenCalled();
        });
    });

    it("links a device to its last profile's settings, on the Devices tab with that device open", async () => {
        renderPage();

        await screen.findByText('Living room');
        const settings = within(rowFor('Living room')).getByRole('link', { name: 'Settings' });

        expect(settings).toHaveAttribute('href', '/admin/server/s1/users/p-kids/client-settings?tab=devices&device=tv-1');
        expect(within(rowFor('Roku Ultra')).queryByRole('link', { name: 'Settings' })).toBeNull();
    });

    it('blocks a device after confirming, naming it by its nickname', async () => {
        mocks.blockDevice.mockResolvedValue(undefined);
        renderPage();

        await screen.findByText('Living room');
        fireEvent.click(within(rowFor('Living room')).getByRole('button', { name: 'Block' }));
        expect(await screen.findByText('Are you sure you want to block Living room?')).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Confirm' }));

        await waitFor(() => expect(mocks.blockDevice).toHaveBeenCalledWith('id-tv-1', 's1'));
    });

    it('removes a device after confirming', async () => {
        mocks.deleteDevice.mockResolvedValue(undefined);
        renderPage();

        fireEvent.click(await screen.findByRole('button', { name: 'Remove Pixel 8' }));
        fireEvent.click(await screen.findByRole('button', { name: 'Confirm' }));

        await waitFor(() => expect(mocks.deleteDevice).toHaveBeenCalledWith('id-phone-1', 's1'));
    });
});
