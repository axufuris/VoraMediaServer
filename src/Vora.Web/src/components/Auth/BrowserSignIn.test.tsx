import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import BrowserSignIn from './BrowserSignIn';
import type { DevicePairingPoll, DevicePairingStart } from '../../api/Auth/devicePairingService';
import type { VoraDesktopBridge } from '../../desktop/desktopBridge';

const mocks = vi.hoisted(() => ({
    start: vi.fn<() => Promise<DevicePairingStart>>(),
    poll: vi.fn<(baseUrl: string, deviceCode: string) => Promise<DevicePairingPoll>>(),
    openExternal: vi.fn<(url: string) => Promise<boolean>>(),
}));

vi.mock('../../api/Auth/devicePairingService', () => ({
    devicePairingService: { start: mocks.start, poll: mocks.poll },
}));

const pairing = (overrides: Partial<DevicePairingStart> = {}): DevicePairingStart => ({
    deviceCode: 'device-secret',
    userCode: 'BCDF-GHJK',
    verificationUrl: 'https://vora.test/link',
    verificationUrlComplete: 'https://vora.test/link?code=BCDFGHJK',
    expiresInSeconds: 600,
    intervalSeconds: 5,
    ...overrides,
});

const auth = { accessToken: 'account-token', userId: 'user-1', displayName: 'Andy', isAdmin: false, refreshToken: 'refresh' };

const flush = () => act(async () => { await vi.advanceTimersByTimeAsync(0); });
const wait = (ms: number) => act(async () => { await vi.advanceTimersByTimeAsync(ms); });

const begin = async (onApproved = vi.fn()) => {
    render(<BrowserSignIn serverUrl="https://vora.test" onApproved={onApproved} />);
    fireEvent.click(screen.getByRole('button', { name: 'Sign in with your browser' }));
    await flush();
    return onApproved;
};

describe('BrowserSignIn', () => {
    beforeEach(() => {
        vi.useFakeTimers();
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.start.mockResolvedValue(pairing());
        mocks.poll.mockResolvedValue({ status: 'pending' });
        mocks.openExternal.mockResolvedValue(true);
        window.voraDesktop = { openExternal: mocks.openExternal } as unknown as VoraDesktopBridge;
    });

    afterEach(() => {
        cleanup();
        vi.useRealTimers();
        delete window.voraDesktop;
    });

    it('asks the server for a code, opens the approval page in the browser and shows the code', async () => {
        await begin();

        expect(mocks.start).toHaveBeenCalledWith('https://vora.test');
        expect(mocks.openExternal).toHaveBeenCalledWith('https://vora.test/link?code=BCDFGHJK');
        expect(screen.getByRole('status')).toHaveTextContent('Waiting for approval in your browser');
        expect(screen.getByLabelText('Code BCDF-GHJK')).toHaveTextContent('BCDF-GHJK');
        expect(screen.getByText('https://vora.test/link')).toBeInTheDocument();
    });

    it('polls once the interval has passed, not before, and keeps waiting while nobody has answered', async () => {
        await begin();

        await wait(4900);
        expect(mocks.poll).not.toHaveBeenCalled();
        await wait(200);
        expect(mocks.poll).toHaveBeenCalledTimes(1);
        expect(mocks.poll).toHaveBeenCalledWith('https://vora.test', 'device-secret');
        await wait(5000);
        expect(mocks.poll).toHaveBeenCalledTimes(2);
        expect(screen.getByRole('status')).toBeInTheDocument();
    });

    it('signs in with what the server hands over once approved, then stops polling', async () => {
        const onApproved = await begin();
        mocks.poll.mockResolvedValueOnce({ status: 'pending' }).mockResolvedValueOnce({ status: 'approved', auth });

        await wait(5000);
        await wait(5000);

        expect(onApproved).toHaveBeenCalledTimes(1);
        expect(onApproved).toHaveBeenCalledWith(auth);
        await wait(30000);
        expect(mocks.poll).toHaveBeenCalledTimes(2);
    });

    it('waits the longer interval the server asks for after slow_down', async () => {
        await begin();
        mocks.poll.mockResolvedValueOnce({ status: 'slow_down', intervalSeconds: 10 });

        await wait(5000);
        expect(mocks.poll).toHaveBeenCalledTimes(1);
        await wait(9000);
        expect(mocks.poll).toHaveBeenCalledTimes(1);
        await wait(1100);
        expect(mocks.poll).toHaveBeenCalledTimes(2);
    });

    it('says so when the sign-in is declined and offers a new code', async () => {
        const onApproved = await begin();
        mocks.poll.mockResolvedValueOnce({ status: 'denied' });
        await wait(5000);

        expect(screen.getByRole('alert')).toHaveTextContent('That sign-in was declined');
        expect(onApproved).not.toHaveBeenCalled();

        mocks.start.mockResolvedValueOnce(pairing({ userCode: 'MNPQ-RSTV', deviceCode: 'second' }));
        fireEvent.click(screen.getByRole('button', { name: 'Get a new code' }));
        await flush();
        expect(screen.getByLabelText('Code MNPQ-RSTV')).toBeInTheDocument();
        await wait(5000);
        expect(mocks.poll).toHaveBeenLastCalledWith('https://vora.test', 'second');
    });

    it('says so when the code expires', async () => {
        await begin();
        mocks.poll.mockResolvedValueOnce({ status: 'expired' });
        await wait(5000);

        expect(screen.getByRole('alert')).toHaveTextContent('That code expired');
        expect(screen.getByRole('button', { name: 'Get a new code' })).toBeInTheDocument();
    });

    it('stops asking when Cancel is pressed and goes back to the button', async () => {
        await begin();

        fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
        await wait(30000);

        expect(mocks.poll).not.toHaveBeenCalled();
        expect(screen.getByRole('button', { name: 'Sign in with your browser' })).toBeInTheDocument();
    });

    it('opens the approval page again on request', async () => {
        await begin();
        mocks.openExternal.mockClear();

        fireEvent.click(screen.getByRole('button', { name: 'Open again' }));
        await flush();

        expect(mocks.openExternal).toHaveBeenCalledWith('https://vora.test/link?code=BCDFGHJK');
    });

    it('shows the address to type when the browser could not be opened', async () => {
        mocks.openExternal.mockResolvedValue(false);
        await begin();

        expect(screen.getByRole('status')).toHaveTextContent('could not open your browser');
        expect(screen.getByLabelText('Code BCDF-GHJK')).toBeInTheDocument();
    });

    it('explains a blocked computer and a server that cannot be reached', async () => {
        mocks.start.mockRejectedValueOnce({ isAxiosError: true, response: { status: 403 } });
        render(<BrowserSignIn serverUrl="https://vora.test" onApproved={vi.fn()} />);
        fireEvent.click(screen.getByRole('button', { name: 'Sign in with your browser' }));
        await flush();
        expect(screen.getByRole('alert')).toHaveTextContent('blocked from signing in');

        mocks.start.mockRejectedValueOnce(new Error('offline'));
        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
        await flush();
        expect(screen.getByRole('alert')).toHaveTextContent('Could not reach the server');
    });

    it('retries a poll that failed on the network, and gives up after five in a row', async () => {
        await begin();
        mocks.poll.mockRejectedValue(new Error('offline'));

        await wait(5000);
        await wait(5000);
        await wait(5000);
        await wait(5000);
        expect(screen.getByRole('status')).toBeInTheDocument();
        await wait(5000);

        expect(mocks.poll).toHaveBeenCalledTimes(5);
        expect(screen.getByRole('alert')).toHaveTextContent('Lost contact with the server');
    });

    it('stops asking when the page goes away', async () => {
        await begin();

        cleanup();
        await wait(30000);

        expect(mocks.poll).not.toHaveBeenCalled();
    });
});
