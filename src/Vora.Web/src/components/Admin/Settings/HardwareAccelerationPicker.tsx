import { useState } from 'react';
import {
    systemSettingsAdminService,
    type HardwareAccelerationType,
    type HardwareCapabilities,
    type HardwareTestResult,
} from '../../../api/System/systemSettingsAdminService';
import { HARDWARE_ACCELERATION_TYPES, autoLabel, devicesFor, deviceLabel } from './hardwareAccelerationText';

const LABEL = 'mb-1.5 block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)]';

interface HardwareAccelerationPickerProps {
    idPrefix: string;
    type: HardwareAccelerationType;
    device: string;
    hardware: HardwareCapabilities | null;
    allowOff?: boolean;
    serverId?: string;
    onChange: (type: HardwareAccelerationType, device: string) => void;
}

export default function HardwareAccelerationPicker({ idPrefix, type, device, hardware, allowOff = true, serverId, onChange }: HardwareAccelerationPickerProps) {
    const [testing, setTesting] = useState(false);
    const [result, setResult] = useState<HardwareTestResult | null>(null);
    const allDevices = hardware?.devices ?? [];
    const devices = devicesFor(type, allDevices);
    const deviceListed = device === 'Auto' || devices.some(d => d.id === device);
    const types = HARDWARE_ACCELERATION_TYPES.filter(t => allowOff || t.value !== 'Off');

    const changeType = (value: string) => {
        const next = types.find(t => t.value === value)?.value ?? 'Auto';
        const keepDevice = next !== 'Off' && devicesFor(next, allDevices).some(d => d.id === device);
        setResult(null);
        onChange(next, keepDevice ? device : 'Auto');
    };

    const changeDevice = (value: string) => {
        setResult(null);
        onChange(type, value);
    };

    const runTest = async () => {
        setTesting(true);
        setResult(null);
        try {
            setResult(await systemSettingsAdminService.testHardware(type, device, serverId));
        } catch {
            setResult({ success: false, type: null, device: null, message: "Vora couldn't run the test. Check that the server is reachable and try again." });
        } finally {
            setTesting(false);
        }
    };

    return (
        <div className="space-y-3">
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                <div>
                    <label htmlFor={`${idPrefix}-type`} className={LABEL}>Hardware acceleration</label>
                    <select id={`${idPrefix}-type`} value={type} onChange={e => changeType(e.target.value)} className="vora-input cursor-pointer">
                        {types.map(t => <option key={t.value} value={t.value}>{t.value === 'Auto' ? autoLabel(hardware) : t.label}</option>)}
                    </select>
                </div>
                <div>
                    <label htmlFor={`${idPrefix}-device`} className={LABEL}>Device</label>
                    <select
                        id={`${idPrefix}-device`}
                        value={device}
                        disabled={type === 'Off'}
                        onChange={e => changeDevice(e.target.value)}
                        className="vora-input cursor-pointer disabled:cursor-not-allowed disabled:opacity-50"
                    >
                        <option value="Auto">Auto</option>
                        {devices.map(d => <option key={d.id} value={d.id}>{deviceLabel(d)}</option>)}
                        {!deviceListed && <option value={device}>{device} (not found)</option>}
                    </select>
                </div>
            </div>
            <div className="flex flex-wrap items-center gap-3">
                <button
                    type="button"
                    onClick={() => { void runTest(); }}
                    disabled={testing || type === 'Off'}
                    className="vora-button-secondary shrink-0 text-sm disabled:cursor-not-allowed disabled:opacity-50"
                >
                    {testing ? 'Testing…' : 'Test hardware'}
                </button>
                <p role="status" className={`min-w-0 flex-1 text-sm ${result?.success ? 'text-[var(--vora-success-text)]' : 'text-[var(--vora-danger-text)]'}`}>
                    {result?.message}
                </p>
            </div>
        </div>
    );
}
