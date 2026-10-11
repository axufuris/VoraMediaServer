import type { GpuVendor, HardwareAccelerationType, HardwareCapabilities, HardwareDevice } from '../../../api/System/systemSettingsAdminService';

export const HARDWARE_ACCELERATION_TYPES: { value: HardwareAccelerationType; label: string }[] = [
    { value: 'Auto', label: 'Auto' },
    { value: 'Nvidia', label: 'NVIDIA (NVENC)' },
    { value: 'QuickSync', label: 'Intel Quick Sync' },
    { value: 'Vaapi', label: 'VAAPI (Intel or AMD)' },
    { value: 'Off', label: 'Off' },
];

const DETECTED: Record<HardwareAccelerationType, string> = {
    Auto: 'Auto',
    Nvidia: 'NVIDIA',
    QuickSync: 'Intel Quick Sync',
    Vaapi: 'VAAPI',
    Off: 'Off',
};

const VENDORS_FOR: Record<HardwareAccelerationType, GpuVendor[]> = {
    Auto: ['Nvidia', 'Intel', 'Amd', 'Unknown'],
    Nvidia: ['Nvidia'],
    QuickSync: ['Intel', 'Unknown'],
    Vaapi: ['Intel', 'Amd', 'Unknown'],
    Off: [],
};

export const devicesFor = (type: HardwareAccelerationType, devices: HardwareDevice[]): HardwareDevice[] =>
    devices.filter(d => VENDORS_FOR[type].includes(d.vendor));

export const deviceLabel = (device: HardwareDevice): string =>
    device.id.startsWith('/dev/') ? `${device.name} · ${device.id}` : `${device.name} · GPU ${device.id}`;

export const autoLabel = (hardware: HardwareCapabilities | null): string => {
    if (!hardware) return 'Auto';
    return hardware.detected ? `Auto (detected: ${DETECTED[hardware.detected]})` : 'Auto (no GPU found, uses the CPU)';
};
