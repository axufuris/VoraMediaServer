import { useCallback, useEffect, useMemo, useState } from 'react';
import { useParams } from 'react-router-dom';
import { deviceService, type ClientDeviceVM } from '../../api/Users/deviceService';
import { useDialog } from '../../dialogs';
import { type UserVM, userService } from '../../api/Users/userService';
import PageHeader from '../../components/Admin/Primitives/PageHeader';
import EmptyState from '../../components/Admin/Primitives/EmptyState';
import { useActionToast } from '../../components/Admin/Primitives/ActionToast';
import DeviceTable from '../../components/Admin/Devices/DeviceTable';
import {
    ALL_USERS,
    NO_FILTERS,
    deviceLabel,
    filterDevices,
    filtersApply,
    groupByLastUser,
    normalizeNickname,
    userChoices,
    type DeviceFilters,
} from '../../components/Admin/Devices/deviceList';

const GROUPED_KEY = 'admin_devices_grouped';

function readGrouped(): boolean {
    try {
        return localStorage.getItem(GROUPED_KEY) === 'true';
    } catch {
        return false;
    }
}

function rememberGrouped(grouped: boolean) {
    try {
        localStorage.setItem(GROUPED_KEY, String(grouped));
    } catch {
        // The choice just is not remembered.
    }
}

const SEGMENT = 'px-3 py-1.5 text-xs font-semibold transition-colors cursor-pointer';
const segmentTone = (active: boolean) => active
    ? 'bg-[var(--vora-accent-500)] text-[var(--vora-accent-contrast)]'
    : 'bg-[var(--vora-bg-surface)] text-[var(--vora-text-secondary)] hover:bg-[var(--vora-bg-sunken)]';

export default function AuthorizedDevicesPage() {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const { toast, showToast } = useActionToast();
    const [devices, setDevices] = useState<ClientDeviceVM[]>([]);
    const [users, setUsers] = useState<UserVM[]>([]);
    const [loading, setLoading] = useState(true);
    const [filters, setFilters] = useState<DeviceFilters>(NO_FILTERS);
    const [grouped, setGrouped] = useState(readGrouped);
    const [collapsed, setCollapsed] = useState<ReadonlySet<string>>(new Set());
    const [renamingId, setRenamingId] = useState<string | null>(null);

    const loadData = useCallback(async (showSkeleton = true) => {
        if (showSkeleton) setLoading(true);
        try {
            const [deviceData, userData] = await Promise.all([
                deviceService.getDevices(serverId),
                userService.getAllUsers(serverId),
            ]);
            setDevices(deviceData);
            setUsers(userData);
        } catch (error) {
            console.error('Failed to load devices', error);
        } finally {
            setLoading(false);
        }
    }, [serverId]);

    useEffect(() => {
        loadData();
    }, [loadData]);

    const visible = useMemo(() => filterDevices(devices, users, filters), [devices, users, filters]);
    const groups = useMemo(() => groupByLastUser(visible, users), [visible, users]);
    const choices = useMemo(() => userChoices(devices, users), [devices, users]);
    const filtered = filtersApply(filters);

    const chooseGrouping = (next: boolean) => {
        setGrouped(next);
        rememberGrouped(next);
    };

    const toggleGroup = (key: string) => setCollapsed(current => {
        const next = new Set(current);
        if (!next.delete(key)) next.add(key);
        return next;
    });

    const handleToggleBlock = async (device: ClientDeviceVM) => {
        const action = device.isBlocked ? 'unblock' : 'block';
        if (!await dialog.confirm(`Are you sure you want to ${action} ${deviceLabel(device)}?`)) return;

        try {
            if (device.isBlocked) {
                await deviceService.unblockDevice(device.id, serverId);
            } else {
                await deviceService.blockDevice(device.id, serverId);
            }
            await loadData(false);
        } catch {
            await dialog.alert(`Failed to ${action} device.`);
        }
    };

    const handleDelete = async (device: ClientDeviceVM) => {
        if (!await dialog.confirm(`Are you sure you want to completely remove ${deviceLabel(device)}? If the device reconnects it will generate a new record.`)) return;

        try {
            await deviceService.deleteDevice(device.id, serverId);
            await loadData(false);
        } catch {
            await dialog.alert('Failed to delete device.');
        }
    };

    const handleRename = async (device: ClientDeviceVM, nickname: string) => {
        const normalized = normalizeNickname(nickname);
        await deviceService.setNickname(device.id, normalized, serverId);
        setDevices(current => current.map(entry => entry.id === device.id ? { ...entry, nickname: normalized } : entry));
        setRenamingId(null);
        showToast(normalized
            ? `Saved the nickname “${normalized}”.`
            : `Removed the nickname from ${device.deviceName || 'the device'}.`);
    };

    const table = (rows: ClientDeviceVM[], showUser: boolean) => (
        <DeviceTable
            devices={rows}
            users={users}
            serverId={serverId}
            showUser={showUser}
            renamingId={renamingId}
            onStartRename={setRenamingId}
            onRename={handleRename}
            onToggleBlock={handleToggleBlock}
            onDelete={handleDelete}
        />
    );

    return (
        <div data-vora-page="">
            <PageHeader
                title="Authorized Devices"
                description="Monitor and manage every client that's connected to this server."
                actions={
                    <button type="button" onClick={() => loadData()} className="vora-button-secondary flex items-center gap-2">
                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" /></svg>
                        Refresh
                    </button>
                }
            />

            <div className="p-8 max-md:p-4 max-w-7xl mx-auto space-y-4">
                {loading ? (
                    <div className="vora-skeleton h-64" />
                ) : devices.length === 0 ? (
                    <div className="vora-card">
                        <EmptyState
                            title="No devices yet"
                            description="When a client connects to this server, it'll be tracked here."
                            icon={<svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M9 17v-1.5a1.5 1.5 0 011.5-1.5h3a1.5 1.5 0 011.5 1.5V17M3 7a2 2 0 012-2h14a2 2 0 012 2v8a2 2 0 01-2 2H5a2 2 0 01-2-2V7z" /></svg>}
                        />
                    </div>
                ) : (
                    <>
                        <div className="flex flex-wrap items-end gap-3">
                            <div className="min-w-[14rem] flex-1 max-w-sm">
                                <label htmlFor="device-search" className="mb-1 block text-[10px] font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">Device</label>
                                <input
                                    id="device-search"
                                    type="search"
                                    value={filters.query}
                                    onChange={event => setFilters(current => ({ ...current, query: event.target.value }))}
                                    placeholder="Nickname, name, client, address…"
                                    className="vora-input"
                                />
                            </div>
                            <div className="min-w-[12rem]">
                                <label htmlFor="device-user" className="mb-1 block text-[10px] font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">Last user</label>
                                <select
                                    id="device-user"
                                    value={filters.userKey}
                                    onChange={event => setFilters(current => ({ ...current, userKey: event.target.value }))}
                                    className="vora-input cursor-pointer"
                                >
                                    <option value={ALL_USERS}>Everyone ({devices.length})</option>
                                    {choices.map(choice => <option key={choice.key} value={choice.key}>{choice.label} ({choice.count})</option>)}
                                </select>
                            </div>
                            <div role="group" aria-label="Layout" className="flex overflow-hidden rounded-[var(--vora-radius-md)] border border-[var(--vora-border-strong)]">
                                <button type="button" aria-pressed={!grouped} onClick={() => chooseGrouping(false)} className={`${SEGMENT} border-r border-[var(--vora-border-strong)] ${segmentTone(!grouped)}`}>All devices</button>
                                <button type="button" aria-pressed={grouped} onClick={() => chooseGrouping(true)} className={`${SEGMENT} ${segmentTone(grouped)}`}>By user</button>
                            </div>
                            {filtered && (
                                <button type="button" onClick={() => setFilters(NO_FILTERS)} className="vora-button-secondary text-xs">Clear filters</button>
                            )}
                        </div>

                        <p className="m-0 text-xs text-[var(--vora-text-muted)]">
                            {filtered
                                ? `Showing ${visible.length} of ${devices.length} ${devices.length === 1 ? 'device' : 'devices'}`
                                : `${devices.length} ${devices.length === 1 ? 'device' : 'devices'}`}
                        </p>

                        {visible.length === 0 ? (
                            <div className="vora-card">
                                <EmptyState title="No devices match" description="Try a different name, or clear the filters." />
                            </div>
                        ) : grouped ? (
                            <div className="space-y-4">
                                {groups.map(group => {
                                    const isCollapsed = collapsed.has(group.key);
                                    const blocked = group.devices.filter(device => device.isBlocked).length;
                                    return (
                                        <section key={group.key} className="vora-card overflow-hidden" aria-label={group.label}>
                                            <button
                                                type="button"
                                                onClick={() => toggleGroup(group.key)}
                                                aria-expanded={!isCollapsed}
                                                className="flex w-full cursor-pointer items-center gap-3 px-5 py-3 text-left"
                                            >
                                                <svg className={`h-4 w-4 shrink-0 text-[var(--vora-text-muted)] transition-transform ${isCollapsed ? '' : 'rotate-90'}`} fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" /></svg>
                                                <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-[var(--vora-bg-sunken)] text-xs font-bold text-[var(--vora-text-secondary)]" aria-hidden="true">{group.label.charAt(0).toUpperCase()}</span>
                                                <span className="min-w-0 flex-1">
                                                    <span className="block truncate text-sm font-semibold text-[var(--vora-text-primary)]">{group.label}</span>
                                                    <span className="block text-xs text-[var(--vora-text-muted)]">
                                                        {group.devices.length} {group.devices.length === 1 ? 'device' : 'devices'}{blocked > 0 ? ` · ${blocked} blocked` : ''}
                                                    </span>
                                                </span>
                                            </button>
                                            {!isCollapsed && (
                                                <div className="border-t border-[var(--vora-border-subtle)]">
                                                    {table(group.devices, false)}
                                                </div>
                                            )}
                                        </section>
                                    );
                                })}
                            </div>
                        ) : (
                            <div className="vora-card overflow-hidden">
                                {table(visible, true)}
                            </div>
                        )}
                    </>
                )}
            </div>
            {toast}
        </div>
    );
}
