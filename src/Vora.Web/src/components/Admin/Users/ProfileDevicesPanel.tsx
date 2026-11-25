import { useEffect, useMemo, useState } from 'react';
import DeviceSettingsEditor from '../../Common/ClientSettings/DeviceSettingsEditor';
import { profileService, type DeviceSettingsVM, type ProfileDeviceVM } from '../../../api/Users/profileService';
import { BITRATE_CHOICES, RESOLUTION_CHOICES } from '../../../utils/playbackChoices';
import { resolveReason } from '../../../utils/apiError';

interface ProfileDevicesPanelProps {
    profileId: string;
    serverId?: string;
    openDeviceId?: string | null;
}

type Editing = { kind: 'device'; deviceId: string } | { kind: 'selection' } | null;

const FILTER_FROM = 6;

const labelOf = (choices: readonly { value: number; label: string }[], value: number) =>
    choices.find(choice => choice.value === value)?.label ?? String(value);

function deviceSummary(settings: DeviceSettingsVM): string {
    return `${labelOf(BITRATE_CHOICES, settings.bitrate)} · ${labelOf(RESOLUTION_CHOICES, settings.maxResolution)} · skip ${settings.seekStepSeconds} s · volume ${settings.volumeStepPercent}%`;
}

const displayName = (device: ProfileDeviceVM) => device.nickname.trim() || device.name;

function describe(device: ProfileDeviceVM): string {
    const own = device.nickname.trim() ? [device.name] : [];
    const rest = [device.clientName, device.deviceType, device.operatingSystem].filter(part => part && part !== device.name);
    return [...own, ...rest].join(' · ');
}

function matches(device: ProfileDeviceVM, query: string): boolean {
    const terms = query.toLowerCase().split(/\s+/).filter(Boolean);
    if (terms.length === 0) return true;
    const text = [device.nickname, device.name, device.clientName, device.deviceType, device.operatingSystem].join('\n').toLowerCase();
    return terms.every(term => text.includes(term));
}

const countOf = (count: number) => `${count} ${count === 1 ? 'device' : 'devices'}`;

export default function ProfileDevicesPanel({ profileId, serverId, openDeviceId = null }: ProfileDevicesPanelProps) {
    const [devices, setDevices] = useState<ProfileDeviceVM[] | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [query, setQuery] = useState('');
    const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());
    const [editing, setEditing] = useState<Editing>(openDeviceId ? { kind: 'device', deviceId: openDeviceId } : null);
    const [notice, setNotice] = useState<string | null>(null);

    useEffect(() => {
        let cancelled = false;
        profileService.adminGetDeviceSettings(profileId, serverId)
            .then(result => { if (!cancelled) setDevices(result); })
            .catch(err => { if (!cancelled) setError(resolveReason(err) ?? "Could not read this profile's devices."); });
        return () => { cancelled = true; };
    }, [profileId, serverId]);

    const shown = useMemo(() => (devices ?? []).filter(device => matches(device, query)), [devices, query]);
    const chosen = shown.filter(device => selected.has(device.deviceId));
    const allShownChosen = shown.length > 0 && chosen.length === shown.length;

    const save = async (deviceIds: string[], settings: DeviceSettingsVM, message: string) => {
        const saved = await profileService.adminSaveDeviceSettings(profileId, { deviceIds, settings }, serverId);
        setDevices(saved);
        setEditing(null);
        setSelected(new Set());
        setNotice(message);
    };

    const toggle = (deviceId: string) => setSelected(current => {
        const next = new Set(current);
        if (!next.delete(deviceId)) next.add(deviceId);
        return next;
    });

    const toggleAllShown = () => setSelected(allShownChosen ? new Set() : new Set(shown.map(device => device.deviceId)));

    if (error) return <p role="alert" className="m-0 text-sm text-[var(--vora-danger-text)]">{error}</p>;
    if (!devices) return <div className="vora-skeleton h-24" />;
    if (devices.length === 0) {
        return <p className="m-0 text-sm text-[var(--vora-text-muted)]">This profile has not used a device yet. Its devices appear here once it has.</p>;
    }

    const choosing = editing?.kind === 'selection';

    return (
        <div className="flex flex-col gap-4">
            {notice && <p role="status" className="m-0 text-sm text-[var(--vora-success-text)]">{notice}</p>}

            <div className="flex flex-wrap items-center gap-3">
                {devices.length >= FILTER_FROM && (
                    <input
                        type="search"
                        value={query}
                        onChange={event => setQuery(event.target.value)}
                        placeholder="Filter devices"
                        aria-label="Filter devices"
                        className="vora-input max-w-xs"
                    />
                )}
                <p className="m-0 text-xs text-[var(--vora-text-muted)]">
                    {query.trim() ? `Showing ${shown.length} of ${countOf(devices.length)}` : countOf(devices.length)}
                </p>
                <div className="ml-auto flex items-center gap-2">
                    {chosen.length > 0 && !choosing && (
                        <>
                            <span className="text-xs text-[var(--vora-text-secondary)]">{chosen.length} selected</span>
                            <button type="button" onClick={() => setSelected(new Set())} className="vora-button-secondary cursor-pointer text-xs">Clear</button>
                            <button type="button" onClick={() => { setNotice(null); setEditing({ kind: 'selection' }); }} className="vora-button-primary cursor-pointer text-xs">
                                Set the same on {chosen.length === 1 ? 'this device' : `these ${chosen.length}`}…
                            </button>
                        </>
                    )}
                </div>
            </div>

            {choosing && (
                <div className="rounded-[var(--vora-radius-md)] border border-[var(--vora-border-strong)] p-4">
                    <p className="m-0 mb-1 text-sm font-semibold text-[var(--vora-text-primary)]">Set the same on {countOf(chosen.length)}</p>
                    <p className="m-0 mb-4 text-xs text-[var(--vora-text-muted)]">
                        This replaces these settings on {chosen.map(displayName).join(', ')}, starting from what the most recently used one has now.
                    </p>
                    <DeviceSettingsEditor
                        settings={chosen[0].settings}
                        saveLabel={`Apply to ${chosen.length === 1 ? 'this device' : `all ${chosen.length}`}`}
                        onCancel={() => setEditing(null)}
                        onSave={settings => save(
                            chosen.map(device => device.deviceId),
                            settings,
                            chosen.length === devices.length ? 'Saved the settings on every device.' : `Saved the settings on ${countOf(chosen.length)}.`,
                        )}
                    />
                </div>
            )}

            {shown.length === 0 ? (
                <p className="m-0 text-sm text-[var(--vora-text-muted)]">No device matches “{query.trim()}”.</p>
            ) : (
                <div className="overflow-x-auto rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)]">
                    <table className="w-full text-left text-sm">
                        <thead className="border-b border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] text-[11px] uppercase tracking-wider text-[var(--vora-text-muted)]">
                            <tr>
                                <th className="w-10 px-4 py-3">
                                    <input
                                        type="checkbox"
                                        aria-label="Select all shown devices"
                                        checked={allShownChosen}
                                        ref={element => { if (element) element.indeterminate = chosen.length > 0 && !allShownChosen; }}
                                        onChange={toggleAllShown}
                                        disabled={choosing}
                                    />
                                </th>
                                <th className="px-4 py-3 font-semibold">Device</th>
                                <th className="px-4 py-3 font-semibold">Last used</th>
                                <th className="px-4 py-3 font-semibold">Uses</th>
                                <th className="px-4 py-3 text-right font-semibold"><span className="sr-only">Actions</span></th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-[var(--vora-border-subtle)]">
                            {shown.map(device => {
                                const name = displayName(device);
                                const isEditing = editing?.kind === 'device' && editing.deviceId === device.deviceId;
                                return (
                                    <DeviceRows
                                        key={device.deviceId}
                                        device={device}
                                        name={name}
                                        selected={selected.has(device.deviceId)}
                                        selectDisabled={choosing}
                                        onToggle={() => toggle(device.deviceId)}
                                        isEditing={isEditing}
                                        onEdit={() => { setNotice(null); setEditing({ kind: 'device', deviceId: device.deviceId }); }}
                                        onCancel={() => setEditing(null)}
                                        onSave={settings => save([device.deviceId], settings, `Saved the settings for ${name}.`)}
                                    />
                                );
                            })}
                        </tbody>
                    </table>
                </div>
            )}
        </div>
    );
}

interface DeviceRowsProps {
    device: ProfileDeviceVM;
    name: string;
    selected: boolean;
    selectDisabled: boolean;
    onToggle: () => void;
    isEditing: boolean;
    onEdit: () => void;
    onCancel: () => void;
    onSave: (settings: DeviceSettingsVM) => Promise<void>;
}

function DeviceRows({ device, name, selected, selectDisabled, onToggle, isEditing, onEdit, onCancel, onSave }: DeviceRowsProps) {
    return (
        <>
            <tr className="align-top transition-colors hover:bg-[var(--vora-bg-sunken)]/50">
                <td className="px-4 py-3">
                    <input type="checkbox" aria-label={`Select ${name}`} checked={selected} onChange={onToggle} disabled={selectDisabled} />
                </td>
                <td className="px-4 py-3">
                    <p className="m-0 text-sm font-semibold text-[var(--vora-text-primary)]">{name}</p>
                    {describe(device) && <p className="m-0 text-xs text-[var(--vora-text-muted)]">{describe(device)}</p>}
                </td>
                <td className="whitespace-nowrap px-4 py-3 text-xs text-[var(--vora-text-secondary)]">
                    {device.lastConnectedAt ? new Date(device.lastConnectedAt).toLocaleString() : 'Not seen'}
                </td>
                <td className="px-4 py-3 text-xs text-[var(--vora-text-secondary)]">{deviceSummary(device.settings)}</td>
                <td className="px-4 py-3 text-right">
                    {!isEditing && (
                        <button type="button" onClick={onEdit} className="vora-button-secondary cursor-pointer text-xs" aria-label={`Edit ${name}`}>Edit</button>
                    )}
                </td>
            </tr>
            {isEditing && (
                <tr className="bg-[var(--vora-bg-sunken)]/30">
                    <td />
                    <td colSpan={4} className="px-4 py-4">
                        <p className="m-0 mb-4 text-sm font-semibold text-[var(--vora-text-primary)]">Settings for {name}</p>
                        <DeviceSettingsEditor settings={device.settings} saveLabel="Save device" onCancel={onCancel} onSave={onSave} />
                    </td>
                </tr>
            )}
        </>
    );
}
