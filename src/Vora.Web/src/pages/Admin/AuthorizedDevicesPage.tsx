import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { deviceService, type ClientDeviceVM } from '../../api/Users/deviceService';
import { useDialog } from '../../dialogs';
import { type UserVM, userService } from '../../api/Users/userService';
import PageHeader from '../../components/Admin/Primitives/PageHeader';
import HealthBadge from '../../components/Admin/Primitives/HealthBadge';
import EmptyState from '../../components/Admin/Primitives/EmptyState';

export default function AuthorizedDevicesPage() {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const [devices, setDevices] = useState<ClientDeviceVM[]>([]);
    const [users, setUsers] = useState<UserVM[]>([]);
    const [loading, setLoading] = useState(true);

    const loadData = useCallback(async () => {
        setLoading(true);
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

    const handleToggleBlock = async (device: ClientDeviceVM) => {
        const action = device.isBlocked ? 'unblock' : 'block';
        if (!await dialog.confirm(`Are you sure you want to ${action} this device?`)) return;

        try {
            if (device.isBlocked) {
                await deviceService.unblockDevice(device.id, serverId);
            } else {
                await deviceService.blockDevice(device.id, serverId);
            }
            await loadData();
        } catch {
            await dialog.alert(`Failed to ${action} device.`);
        }
    };

    const handleDelete = async (id: string) => {
        if (!await dialog.confirm('Are you sure you want to completely remove this device? If the device reconnects it will generate a new record.')) return;

        try {
            await deviceService.deleteDevice(id, serverId);
            await loadData();
        } catch {
            await dialog.alert('Failed to delete device.');
        }
    };

    const renderUserCell = (userId?: string, profileId?: string) => {
        if (!userId) return <span className="text-[var(--vora-text-muted)] italic">Anonymous</span>;

        const user = users.find(u => u.id === userId);
        if (!user) return <span className="text-[var(--vora-text-muted)] italic">Unknown user</span>;

        if (profileId) {
            const profile = user.profiles.find(p => p.id === profileId);
            if (profile) {
                return (
                    <>
                        <span className="font-semibold text-[var(--vora-text-primary)]">{profile.name}</span>
                        <span className="text-[var(--vora-text-muted)] text-xs ml-1">({user.displayName})</span>
                    </>
                );
            }
        }
        return <span className="font-semibold text-[var(--vora-text-primary)]">{user.displayName}</span>;
    };

    return (
        <div data-vora-page="">
            <PageHeader
                title="Authorized Devices"
                description="Monitor and manage every client that's connected to this server."
                actions={
                    <button type="button" onClick={loadData} className="vora-button-secondary flex items-center gap-2">
                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" /></svg>
                        Refresh
                    </button>
                }
            />

            <div className="p-8 max-w-7xl mx-auto">
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
                    <div className="vora-card overflow-hidden">
                        <div className="overflow-x-auto">
                            <table className="w-full text-left text-sm whitespace-nowrap">
                                <thead className="bg-[var(--vora-bg-sunken)] border-b border-[var(--vora-border-subtle)] text-[var(--vora-text-muted)] uppercase tracking-wider text-[11px]">
                                    <tr>
                                        <th className="px-5 py-3 font-semibold">Status</th>
                                        <th className="px-5 py-3 font-semibold">Device & OS</th>
                                        <th className="px-5 py-3 font-semibold">Last User</th>
                                        <th className="px-5 py-3 font-semibold">Network</th>
                                        <th className="px-5 py-3 font-semibold">Last Seen</th>
                                        <th className="px-5 py-3 font-semibold text-right">Actions</th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-[var(--vora-border-subtle)]">
                                    {devices.map(device => (
                                        <tr key={device.id} className="hover:bg-[var(--vora-bg-sunken)]/50 transition-colors">
                                            <td className="px-5 py-3">
                                                {device.isBlocked
                                                    ? <HealthBadge tone="error">Blocked</HealthBadge>
                                                    : <HealthBadge tone="ok">Active</HealthBadge>}
                                            </td>
                                            <td className="px-5 py-3">
                                                <div className="flex flex-col">
                                                    <span className="font-semibold text-[var(--vora-text-primary)]">{device.deviceName}</span>
                                                    <span className="text-[var(--vora-text-muted)] text-xs">{device.operatingSystem} · {device.deviceType}</span>
                                                </div>
                                            </td>
                                            <td className="px-5 py-3">
                                                {renderUserCell(device.lastUserId, device.lastProfileId)}
                                            </td>
                                            <td className="px-5 py-3">
                                                <div className="flex flex-col">
                                                    <span className="font-mono text-xs text-[var(--vora-text-secondary)]">{device.lastIpAddress}</span>
                                                    <span className="text-[var(--vora-text-muted)] text-xs truncate max-w-[200px]" title={device.location}>{device.location}</span>
                                                </div>
                                            </td>
                                            <td className="px-5 py-3 text-[var(--vora-text-secondary)]">
                                                {new Date(device.lastConnectedAt).toLocaleString()}
                                            </td>
                                            <td className="px-5 py-3 text-right">
                                                <div className="flex justify-end items-center gap-2">
                                                    <button
                                                        type="button"
                                                        onClick={() => handleToggleBlock(device)}
                                                        className={`px-3 py-1 rounded-[var(--vora-radius-md)] text-xs font-semibold transition-colors cursor-pointer ${device.isBlocked
                                                            ? 'bg-[var(--vora-bg-sunken)] text-[var(--vora-text-primary)] hover:bg-[var(--vora-border-strong)]'
                                                            : 'bg-[var(--vora-danger-soft)] text-[var(--vora-danger-text)] hover:bg-[var(--vora-danger-500)] hover:text-white'}`}
                                                    >
                                                        {device.isBlocked ? 'Unblock' : 'Block'}
                                                    </button>
                                                    <button
                                                        type="button"
                                                        onClick={() => handleDelete(device.id)}
                                                        className="p-1.5 rounded-[var(--vora-radius-md)] text-[var(--vora-text-muted)] hover:text-[var(--vora-danger-text)] hover:bg-[var(--vora-danger-soft)] transition-colors cursor-pointer"
                                                        title="Remove device"
                                                    >
                                                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" /></svg>
                                                    </button>
                                                </div>
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    </div>
                )}
            </div>
        </div>
    );
}
