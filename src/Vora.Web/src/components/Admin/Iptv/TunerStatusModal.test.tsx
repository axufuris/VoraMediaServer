import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { iptvAdminService, type HdHomeRunStatusVM } from '../../../api/Iptv/iptvAdminService';
import TunerStatusModal from './TunerStatusModal';

vi.mock('../../../api/Iptv/iptvAdminService', () => ({
    iptvAdminService: { getTunerStatus: vi.fn() },
}));

const getTunerStatus = vi.mocked(iptvAdminService.getTunerStatus);

const working: HdHomeRunStatusVM = {
    succeeded: true,
    deviceName: 'HDHomeRun FLEX 4K',
    model: 'HDHR5-4K',
    firmwareVersion: '20240101',
    tunerCount: 4,
    tunersInUse: 1,
    voraStreams: 1,
    tuners: [
        { index: 0, inUse: true, channelNumber: '7.1', channelName: 'WABC-DT', frequencyMhz: 473, bitsPerSecond: 19_393_088, targetAddress: '192.168.1.20', signalStrengthPercent: 84, signalQualityPercent: 100, symbolQualityPercent: 45 },
        { index: 1, inUse: false },
    ],
};

function open(onClose = vi.fn()) {
    render(<TunerStatusModal isOpen onClose={onClose} playlistId="p1" playlistName="Antenna" serverId="s1" />);
    return onClose;
}

describe('TunerStatusModal', () => {
    beforeEach(() => {
        getTunerStatus.mockReset();
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it('shows each tuner, what it is tuned to and how well', async () => {
        getTunerStatus.mockResolvedValue(working);

        open();

        expect(await screen.findByText('1 of 4 tuners in use. Vora is streaming 1 channel.')).toBeInTheDocument();
        expect(getTunerStatus).toHaveBeenCalledWith('p1', 's1');
        expect(screen.getByText('HDHomeRun FLEX 4K')).toBeInTheDocument();
        expect(screen.getByText('HDHR5-4K · firmware 20240101')).toBeInTheDocument();
        const items = screen.getAllByRole('listitem');
        expect(within(items[0]).getByText('Tuner 1')).toBeInTheDocument();
        expect(within(items[0]).getByText('In use')).toBeInTheDocument();
        expect(within(items[0]).getByText('7.1 WABC-DT')).toBeInTheDocument();
        expect(within(items[0]).getByText('473 MHz · 19.4 Mbps · to 192.168.1.20')).toBeInTheDocument();
        expect(within(items[0]).getByRole('meter', { name: 'Signal strength' })).toHaveAttribute('aria-valuenow', '84');
        expect(within(items[0]).getByRole('meter', { name: 'Symbol quality' })).toHaveAttribute('aria-valuenow', '45');
        expect(within(items[1]).getByText('Tuner 2')).toBeInTheDocument();
        expect(within(items[1]).getByText('Idle')).toBeInTheDocument();
        expect(within(items[1]).queryByRole('meter')).not.toBeInTheDocument();
    });

    it('gives the reason when the device cannot be reached', async () => {
        getTunerStatus.mockResolvedValue({ succeeded: false, error: 'The HDHomeRun did not answer in time.', tunerCount: 0, tunersInUse: 0, voraStreams: 0, tuners: [] });

        open();

        expect(await screen.findByRole('alert')).toHaveTextContent('The HDHomeRun did not answer in time.');
        expect(screen.queryAllByRole('listitem')).toHaveLength(0);
    });

    it('says so when the server itself cannot be asked', async () => {
        getTunerStatus.mockRejectedValue(new Error('offline'));

        open();

        expect(await screen.findByRole('alert')).toHaveTextContent('The server could not be asked for the tuner status.');
    });

    it('asks again every few seconds and stops when it is closed', async () => {
        vi.useFakeTimers({ shouldAdvanceTime: true });
        getTunerStatus.mockResolvedValueOnce(working).mockResolvedValue({ ...working, tunersInUse: 2, voraStreams: 2 });

        const { unmount } = render(<TunerStatusModal isOpen onClose={() => undefined} playlistId="p1" playlistName="Antenna" />);
        await screen.findByText('1 of 4 tuners in use. Vora is streaming 1 channel.');

        await act(async () => {
            await vi.advanceTimersByTimeAsync(5000);
        });

        await waitFor(() => expect(screen.getByText('2 of 4 tuners in use. Vora is streaming 2 channels.')).toBeInTheDocument());
        const calls = getTunerStatus.mock.calls.length;

        unmount();
        await vi.advanceTimersByTimeAsync(20000);

        expect(getTunerStatus.mock.calls.length).toBe(calls);
    });

    it('closes from the header', async () => {
        getTunerStatus.mockResolvedValue(working);
        const onClose = open();
        await screen.findByText('Tuner 1');

        fireEvent.click(screen.getByRole('button', { name: 'Close' }));

        expect(onClose).toHaveBeenCalled();
    });
});
