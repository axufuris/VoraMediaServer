import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { useState } from 'react';
import HardwareAccelerationPicker from './HardwareAccelerationPicker';
import type { HardwareAccelerationType, HardwareCapabilities, HardwareTestResult } from '../../../api/System/systemSettingsAdminService';

const mocks = vi.hoisted(() => ({ testHardware: vi.fn<(type: HardwareAccelerationType, device: string, serverId?: string) => Promise<HardwareTestResult>>() }));
vi.mock('../../../api/System/systemSettingsAdminService', () => ({
    systemSettingsAdminService: { testHardware: (type: HardwareAccelerationType, device: string, serverId?: string) => mocks.testHardware(type, device, serverId) },
}));

const hardware: HardwareCapabilities = {
    devices: [
        { id: '0', name: 'NVIDIA GeForce RTX 3060', vendor: 'Nvidia' },
        { id: '/dev/dri/renderD128', name: 'Intel GPU', vendor: 'Intel' },
        { id: '/dev/dri/renderD129', name: 'AMD GPU', vendor: 'Amd' },
    ],
    detected: 'QuickSync',
    detectedDevice: '/dev/dri/renderD128',
};

function Harness({ initialType = 'Auto', initialDevice = 'Auto', allowOff = true, caps = hardware }: { initialType?: HardwareAccelerationType; initialDevice?: string; allowOff?: boolean; caps?: HardwareCapabilities | null }) {
    const [value, setValue] = useState({ type: initialType, device: initialDevice });
    return (
        <HardwareAccelerationPicker
            idPrefix="hw"
            type={value.type}
            device={value.device}
            hardware={caps}
            allowOff={allowOff}
            onChange={(type, device) => setValue({ type, device })}
        />
    );
}

const optionLabels = (select: HTMLElement) => within(select).getAllByRole('option').map(o => o.textContent);

describe('HardwareAccelerationPicker', () => {
    beforeEach(() => {
        mocks.testHardware.mockReset();
    });

    it('says what Auto found and lists every gpu with its maker', () => {
        render(<Harness />);

        expect(optionLabels(screen.getByLabelText('Hardware acceleration'))).toEqual(['Auto (detected: Intel Quick Sync)', 'NVIDIA (NVENC)', 'Intel Quick Sync', 'VAAPI (Intel or AMD)', 'Off']);
        expect(optionLabels(screen.getByLabelText('Device'))).toEqual(['Auto', 'NVIDIA GeForce RTX 3060 · GPU 0', 'Intel GPU · /dev/dri/renderD128', 'AMD GPU · /dev/dri/renderD129']);
    });

    it('says Auto uses the cpu when no gpu passed the test', () => {
        render(<Harness caps={{ devices: [], detected: null, detectedDevice: null }} />);

        expect(screen.getByRole('option', { name: 'Auto (no GPU found, uses the CPU)' })).toBeInTheDocument();
    });

    it('only offers the gpus the chosen kind can drive', () => {
        render(<Harness />);

        fireEvent.change(screen.getByLabelText('Hardware acceleration'), { target: { value: 'QuickSync' } });

        expect(optionLabels(screen.getByLabelText('Device'))).toEqual(['Auto', 'Intel GPU · /dev/dri/renderD128']);
    });

    it('drops a device the new kind cannot use', () => {
        render(<Harness initialType="Vaapi" initialDevice="/dev/dri/renderD129" />);

        fireEvent.change(screen.getByLabelText('Hardware acceleration'), { target: { value: 'Nvidia' } });

        expect(screen.getByLabelText('Device')).toHaveValue('Auto');
    });

    it('keeps a saved device that is no longer present so it can be changed', () => {
        render(<Harness initialType="Vaapi" initialDevice="/dev/dri/renderD130" />);

        expect(screen.getByLabelText('Device')).toHaveValue('/dev/dri/renderD130');
        expect(screen.getByRole('option', { name: '/dev/dri/renderD130 (not found)' })).toBeInTheDocument();
    });

    it('turns the device and the test off with hardware acceleration', () => {
        render(<Harness initialType="Off" />);

        expect(screen.getByLabelText('Device')).toBeDisabled();
        expect(screen.getByRole('button', { name: 'Test hardware' })).toBeDisabled();
    });

    it('leaves Off out where a switch already covers it', () => {
        render(<Harness allowOff={false} />);

        expect(screen.queryByRole('option', { name: 'Off' })).toBeNull();
    });

    it('tests the unsaved choice and shows what the server said', async () => {
        mocks.testHardware.mockResolvedValue({ success: true, type: 'QuickSync', device: '/dev/dri/renderD128', message: 'Intel Quick Sync on /dev/dri/renderD128. It encoded a 2-second test clip in 0.6 s.' });
        render(<Harness />);
        fireEvent.change(screen.getByLabelText('Hardware acceleration'), { target: { value: 'QuickSync' } });
        fireEvent.change(screen.getByLabelText('Device'), { target: { value: '/dev/dri/renderD128' } });

        fireEvent.click(screen.getByRole('button', { name: 'Test hardware' }));

        expect(await screen.findByText('Intel Quick Sync on /dev/dri/renderD128. It encoded a 2-second test clip in 0.6 s.')).toBeInTheDocument();
        expect(mocks.testHardware).toHaveBeenCalledWith('QuickSync', '/dev/dri/renderD128', undefined);
    });

    it('explains when the test could not reach the server', async () => {
        mocks.testHardware.mockRejectedValue(new Error('offline'));
        render(<Harness />);

        fireEvent.click(screen.getByRole('button', { name: 'Test hardware' }));

        await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent("Vora couldn't run the test."));
    });
});
