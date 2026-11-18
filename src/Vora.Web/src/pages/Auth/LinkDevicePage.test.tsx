import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { AxiosError, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios';
import LinkDevicePage from './LinkDevicePage';
import type { DevicePairingRequest } from '../../api/Auth/devicePairingService';
import { serverVault } from '../../utils/serverVault';
import { SessionKeys, StorageKeys } from '../../utils/storageKeys';
import { peekPostSignIn } from '../../utils/postSignInRedirect';

const lookup = vi.fn<(userCode: string) => Promise<DevicePairingRequest>>();
const approve = vi.fn<(userCode: string) => Promise<{ status: 'approved' }>>();
const deny = vi.fn<(userCode: string) => Promise<{ status: 'denied' }>>();

vi.mock('../../api/Auth/devicePairingService', () => ({
    devicePairingService: {
        lookup: (userCode: string) => lookup(userCode),
        approve: (userCode: string) => approve(userCode),
        deny: (userCode: string) => deny(userCode),
    },
}));

const livingRoom: DevicePairingRequest = {
    userCode: 'BCDF-GHJK',
    clientName: 'Vora Roku',
    deviceName: 'Living Room',
    deviceType: 'TV',
    operatingSystem: 'Roku OS',
    location: 'Local Network',
    sameNetwork: true,
    requestedAt: '2026-11-18T12:00:00Z',
    expiresAt: '2026-11-18T12:10:00Z',
};

function problem(status: number, detail: string): AxiosError {
    const config = { headers: {} } as InternalAxiosRequestConfig;
    const response = { status, statusText: '', headers: {}, config, data: { title: 'Problem', detail } } as AxiosResponse;
    return new AxiosError(detail, undefined, config, undefined, response);
}

function Where() {
    const location = useLocation();
    return <p data-testid="where">{location.pathname}</p>;
}

function renderAt(path: string) {
    render(
        <MemoryRouter initialEntries={[path]}>
            <Routes>
                <Route path="/link" element={<LinkDevicePage />} />
                <Route path="/login" element={<><h1>Login</h1><Where /></>} />
                <Route path="/profiles" element={<><h1>Profiles</h1><Where /></>} />
                <Route path="/" element={<h1>Home</h1>} />
            </Routes>
        </MemoryRouter>,
    );
}

function signIn() {
    serverVault.addOrUpdateServer({ id: 'home', name: 'Home', url: 'https://vora.example.com', token: 'profile-token', profileId: 'profile', isAdmin: false });
    localStorage.setItem(StorageKeys.profileToken, 'profile-token');
}

describe('LinkDevicePage', () => {
    beforeEach(() => {
        localStorage.clear();
        sessionStorage.clear();
        lookup.mockReset();
        approve.mockReset();
        deny.mockReset();
    });
    afterEach(cleanup);

    it('sends a visitor with no server to the login page and remembers the code for after sign-in', async () => {
        renderAt('/link?code=BCDFGHJK');

        expect(await screen.findByRole('heading', { name: 'Login' })).toBeInTheDocument();
        expect(peekPostSignIn()).toBe('/link?code=BCDFGHJK');
        expect(lookup).not.toHaveBeenCalled();
    });

    it('sends a signed-in account that has not picked a profile to profile selection, remembering the code', async () => {
        serverVault.addOrUpdateServer({ id: 'home', name: 'Home', url: 'https://vora.example.com', token: 'account-token', profileId: '', isAdmin: false });

        renderAt('/link?code=BCDFGHJK');

        expect(await screen.findByRole('heading', { name: 'Profiles' })).toBeInTheDocument();
        expect(sessionStorage.getItem(SessionKeys.postSignInRedirect)).toContain('/link?code=BCDFGHJK');
    });

    it('opens the code from the address, shows which device asked, and approves it', async () => {
        signIn();
        lookup.mockResolvedValue(livingRoom);
        approve.mockResolvedValue({ status: 'approved' });
        const user = userEvent.setup();

        renderAt('/link?code=bcdf-ghjk');

        expect(await screen.findByRole('heading', { name: 'Approve sign-in for Living Room?' })).toBeInTheDocument();
        expect(lookup).toHaveBeenCalledWith('BCDFGHJK');
        expect(screen.getByText('Vora Roku')).toBeInTheDocument();
        expect(screen.getByText('TV · Roku OS')).toBeInTheDocument();
        expect(screen.getByText('Local Network')).toBeInTheDocument();
        expect(screen.queryByText(/not on the same network/)).not.toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: 'Approve' }));

        expect(await screen.findByRole('heading', { name: 'Living Room is signed in' })).toBeInTheDocument();
        expect(approve).toHaveBeenCalledWith('BCDFGHJK');
        expect(peekPostSignIn()).toBeNull();
    });

    it('takes a typed code, and only enables Continue once it is complete', async () => {
        signIn();
        lookup.mockResolvedValue(livingRoom);
        const user = userEvent.setup();

        renderAt('/link');
        const input = await screen.findByLabelText('Code shown on your TV or app');
        const next = screen.getByRole('button', { name: 'Continue' });
        await user.type(input, 'bcdfg');
        expect(next).toBeDisabled();
        expect(input).toHaveValue('BCDF-G');
        await user.type(input, 'hjk');
        expect(next).toBeEnabled();
        await user.click(next);

        expect(await screen.findByRole('heading', { name: 'Approve sign-in for Living Room?' })).toBeInTheDocument();
        expect(lookup).toHaveBeenCalledWith('BCDFGHJK');
    });

    it('warns when the device is on another network', async () => {
        signIn();
        lookup.mockResolvedValue({ ...livingRoom, sameNetwork: false, location: 'Austin, TX, US' });

        renderAt('/link?code=BCDFGHJK');

        expect(await screen.findByText(/not on the same network as this browser/)).toBeInTheDocument();
        expect(screen.getByText('Austin, TX, US')).toBeInTheDocument();
    });

    it('denies a device and says it was not signed in', async () => {
        signIn();
        lookup.mockResolvedValue(livingRoom);
        deny.mockResolvedValue({ status: 'denied' });
        const user = userEvent.setup();

        renderAt('/link?code=BCDFGHJK');
        await user.click(await screen.findByRole('button', { name: 'Deny' }));

        expect(await screen.findByRole('heading', { name: 'Not signed in' })).toBeInTheDocument();
        expect(deny).toHaveBeenCalledWith('BCDFGHJK');
        expect(approve).not.toHaveBeenCalled();
    });

    it('shows the server\'s explanation when the code is not found and lets the person try again', async () => {
        signIn();
        lookup.mockRejectedValue(problem(404, 'That code is not waiting for approval. Check it on the device\'s screen, or start again on the device.'));

        renderAt('/link?code=BCDFGHJK');

        expect(await screen.findByRole('alert')).toHaveTextContent('That code is not waiting for approval');
        expect(screen.getByLabelText('Code shown on your TV or app')).toHaveValue('BCDF-GHJK');
    });

    it('says so when too many wrong codes have been entered', async () => {
        signIn();
        lookup.mockRejectedValue(problem(429, 'Too many codes that did not match. Wait a few minutes and try again.'));

        renderAt('/link?code=BCDFGHJK');

        expect(await screen.findByRole('alert')).toHaveTextContent('Too many codes that did not match');
    });

    it('keeps the device on the confirmation when approving is refused', async () => {
        signIn();
        lookup.mockResolvedValue(livingRoom);
        approve.mockRejectedValue(problem(403, 'Password sign-in is turned off on this server. Sign in with one of its sign-in providers to link a device.'));
        const user = userEvent.setup();

        renderAt('/link?code=BCDFGHJK');
        await user.click(await screen.findByRole('button', { name: 'Approve' }));

        await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Password sign-in is turned off'));
        expect(screen.getByRole('heading', { name: 'Approve sign-in for Living Room?' })).toBeInTheDocument();
    });
});
