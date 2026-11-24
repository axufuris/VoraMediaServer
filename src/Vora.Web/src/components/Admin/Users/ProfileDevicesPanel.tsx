import { useEffect, useState } from 'react';
import DeviceSettingsEditor from '../../Common/ClientSettings/DeviceSettingsEditor';
import { profileService, type DeviceSettingsVM, type ProfileDeviceVM } from '../../../api/Users/profileService';
import { BITRATE_CHOICES, RESOLUTION_CHOICES } from '../../../utils/playbackChoices';
import { resolveReason } from '../../../utils/apiError';

interface ProfileDevicesPanelProps {
    profileId: string;
    serverId?: string;
}

const labelOf = (choices: readonly { value: number; label: string }[], value: number) =>
    choices.find(choice => choice.value === value)?.label ?? String(value);

function deviceSummary(settings: DeviceSettingsVM): string {
    return `${labelOf(BITRATE_CHOICES, settings.bitrate)} · ${labelOf(RESOLUTION_CHOICES, settings.maxResolution)} · skip ${settings.seekStepSeconds} s · volume ${settings.volumeStepPercent}%`;
}

function describe(device: ProfileDeviceVM): string {
    const parts = [device.clientName, device.deviceType, device.operatingSystem].filter(part => part && part !== device.name);
    return parts.join(' · ');
}

export default function ProfileDevicesPanel({ profileId, serverId }: ProfileDevicesPanelProps) {
    const [devices, setDevices] = useState<ProfileDeviceVM[] | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [editing, setEditing] = useState<string | 'all' | null>(null);
    const [notice, setNotice] = useState<string | null>(null);

    useEffect(() => {
        let cancelled = false;
        profileService.adminGetDeviceSettings(profileId, serverId)
            .then(result => { if (!cancelled) setDevices(result); })
            .catch(err => { if (!cancelled) setError(resolveReason(err) ?? "Could not read this profile's devices."); });
        return () => { cancelled = true; };
    }, [profileId, serverId]);

    const save = async (deviceIds: string[] | null, settings: DeviceSettingsVM, message: string) => {
        const saved = await profileService.adminSaveDeviceSettings(profileId, { deviceIds, settings }, serverId);
        setDevices(saved);
        setEditing(null);
        setNotice(message);
    };

    if (error) return <p role="alert" className="m-0 text-sm text-[var(--vora-danger-text)]">{error}</p>;
    if (!devices) return <div className="vora-skeleton h-24" />;
    if (devices.length === 0) {
        return <p className="m-0 text-sm text-[var(--vora-text-muted)]">This profile has not used a device yet. Its devices appear here once it has.</p>;
    }

    return (
        <div className="space-y-3">
            {notice && <p role="status" className="m-0 text-sm text-[var(--vora-success-text)]">{notice}</p>}
            <ul className="m-0 list-none space-y-3 p-0">
                {devices.map(device => (
                    <li key={device.deviceId} className="rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)] p-4">
                        <div className="flex items-start justify-between gap-4 max-md:flex-col">
                            <div className="min-w-0">
                                <p className="m-0 truncate text-sm font-semibold text-[var(--vora-text-primary)]">{device.name}</p>
                                {describe(device) && <p className="m-0 text-xs text-[var(--vora-text-muted)]">{describe(device)}</p>}
                                <p className="m-0 mt-1 text-xs text-[var(--vora-text-secondary)]">{deviceSummary(device.settings)}</p>
                            </div>
                            {editing !== device.deviceId && (
                                <button type="button" onClick={() => { setNotice(null); setEditing(device.deviceId); }} className="vora-button-secondary shrink-0 cursor-pointer text-xs" aria-label={`Edit ${device.name}`}>Edit</button>
                            )}
                        </div>
                        {editing === device.deviceId && (
                            <div className="mt-4 border-t border-[var(--vora-border-subtle)] pt-4">
                                <DeviceSettingsEditor
                                    settings={device.settings}
                                    saveLabel="Save device"
                                    onCancel={() => setEditing(null)}
                                    onSave={settings => save([device.deviceId], settings, `Saved the settings for ${device.name}.`)}
                                />
                            </div>
                        )}
                    </li>
                ))}
            </ul>
            {editing === 'all' ? (
                <div className="rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)] p-4">
                    <p className="m-0 mb-4 text-xs text-[var(--vora-text-muted)]">This replaces the settings on all {devices.length} {devices.length === 1 ? 'device' : 'devices'}, starting from the one used most recently.</p>
                    <DeviceSettingsEditor
                        settings={devices[0].settings}
                        saveLabel="Apply to every device"
                        onCancel={() => setEditing(null)}
                        onSave={settings => save(null, settings, 'Saved the settings on every device.')}
                    />
                </div>
            ) : (
                devices.length > 1 && <button type="button" onClick={() => { setNotice(null); setEditing('all'); }} className="vora-button-secondary cursor-pointer text-xs">Set the same on every device…</button>
            )}
        </div>
    );
}
