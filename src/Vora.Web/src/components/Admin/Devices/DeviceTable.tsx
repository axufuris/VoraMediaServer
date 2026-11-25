import { Link } from 'react-router-dom';
import type { ClientDeviceVM } from '../../../api/Users/deviceService';
import type { UserVM } from '../../../api/Users/userService';
import HealthBadge from '../Primitives/HealthBadge';
import { resolveAdminPath } from '../Shell/adminNavData';
import DeviceNicknameEditor from './DeviceNicknameEditor';
import { deviceLabel, lastProfileOf } from './deviceList';

interface DeviceTableProps {
    devices: ClientDeviceVM[];
    users: UserVM[];
    serverId?: string;
    showUser: boolean;
    renamingId: string | null;
    onStartRename: (id: string | null) => void;
    onRename: (device: ClientDeviceVM, nickname: string) => Promise<void>;
    onToggleBlock: (device: ClientDeviceVM) => void;
    onDelete: (device: ClientDeviceVM) => void;
}

const MUTED = 'text-[var(--vora-text-muted)]';

function LastUser({ device, users, showUser }: { device: ClientDeviceVM; users: UserVM[]; showUser: boolean }) {
    const user = users.find(candidate => candidate.id === device.lastUserId);
    const profile = lastProfileOf(device, users);

    if (!showUser) {
        return profile
            ? <span className="font-semibold text-[var(--vora-text-primary)]">{profile.name}</span>
            : <span className={MUTED}>—</span>;
    }
    if (!device.lastUserId) return <span className={`${MUTED} italic`}>Anonymous</span>;
    if (!user) return <span className={`${MUTED} italic`}>Unknown user</span>;
    if (profile) {
        return (
            <>
                <span className="font-semibold text-[var(--vora-text-primary)]">{profile.name}</span>
                {profile.name !== user.displayName && <span className={`${MUTED} ml-1 text-xs`}>({user.displayName})</span>}
            </>
        );
    }
    return <span className="font-semibold text-[var(--vora-text-primary)]">{user.displayName}</span>;
}

export default function DeviceTable({ devices, users, serverId, showUser, renamingId, onStartRename, onRename, onToggleBlock, onDelete }: DeviceTableProps) {
    return (
        <div className="overflow-x-auto">
            <table className="w-full text-left text-sm whitespace-nowrap">
                <thead className="bg-[var(--vora-bg-sunken)] border-b border-[var(--vora-border-subtle)] text-[var(--vora-text-muted)] uppercase tracking-wider text-[11px]">
                    <tr>
                        <th className="px-5 py-3 font-semibold">Status</th>
                        <th className="px-5 py-3 font-semibold">Device & OS</th>
                        <th className="px-5 py-3 font-semibold">{showUser ? 'Last User' : 'Last Profile'}</th>
                        <th className="px-5 py-3 font-semibold">Network</th>
                        <th className="px-5 py-3 font-semibold">Last Seen</th>
                        <th className="px-5 py-3 font-semibold text-right">Actions</th>
                    </tr>
                </thead>
                <tbody className="divide-y divide-[var(--vora-border-subtle)]">
                    {devices.map(device => {
                        const label = deviceLabel(device);
                        const hasNickname = device.nickname.trim() !== '';
                        const profile = lastProfileOf(device, users);
                        const details = [hasNickname ? device.deviceName : null, device.operatingSystem, device.deviceType].filter(Boolean).join(' · ');
                        return (
                            <tr key={device.id} className="hover:bg-[var(--vora-bg-sunken)]/50 transition-colors">
                                <td className="px-5 py-3">
                                    {device.isBlocked
                                        ? <HealthBadge tone="error">Blocked</HealthBadge>
                                        : <HealthBadge tone="ok">Active</HealthBadge>}
                                </td>
                                <td className="px-5 py-3">
                                    {renamingId === device.id ? (
                                        <DeviceNicknameEditor
                                            label={label}
                                            initial={device.nickname}
                                            onSave={nickname => onRename(device, nickname)}
                                            onCancel={() => onStartRename(null)}
                                        />
                                    ) : (
                                        <div className="flex min-w-0 flex-col">
                                            <div className="flex items-center gap-1.5">
                                                <span className="font-semibold text-[var(--vora-text-primary)]">{label}</span>
                                                <button
                                                    type="button"
                                                    onClick={() => onStartRename(device.id)}
                                                    className={`shrink-0 cursor-pointer rounded-[var(--vora-radius-md)] p-0.5 ${MUTED} transition-colors hover:bg-[var(--vora-bg-sunken)] hover:text-[var(--vora-text-primary)]`}
                                                    aria-label={`Rename ${label}`}
                                                    title={hasNickname ? 'Change the nickname' : 'Give this device a nickname'}
                                                >
                                                    <svg className="h-3.5 w-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15.232 5.232l3.536 3.536m-2.036-5.036a2.5 2.5 0 113.536 3.536L6.5 21.036H3v-3.572L16.732 3.732z" /></svg>
                                                </button>
                                            </div>
                                            <span className={`${MUTED} text-xs`}>{details}</span>
                                        </div>
                                    )}
                                </td>
                                <td className="px-5 py-3">
                                    <LastUser device={device} users={users} showUser={showUser} />
                                </td>
                                <td className="px-5 py-3">
                                    <div className="flex flex-col">
                                        <span className="font-mono text-xs text-[var(--vora-text-secondary)]">{device.lastIpAddress}</span>
                                        <span className={`${MUTED} max-w-[200px] truncate text-xs`} title={device.location}>{device.location}</span>
                                    </div>
                                </td>
                                <td className="px-5 py-3 text-[var(--vora-text-secondary)]">
                                    {new Date(device.lastConnectedAt).toLocaleString()}
                                </td>
                                <td className="px-5 py-3 text-right">
                                    <div className="flex items-center justify-end gap-2">
                                        {profile && (
                                            <Link
                                                to={resolveAdminPath(`/admin/users/${profile.id}/client-settings?tab=devices&device=${encodeURIComponent(device.deviceId)}`, serverId)}
                                                className="rounded-[var(--vora-radius-md)] bg-[var(--vora-bg-sunken)] px-3 py-1 text-xs font-semibold text-[var(--vora-text-primary)] transition-colors hover:bg-[var(--vora-border-strong)]"
                                                title={`Change what ${label} uses for ${profile.name}`}
                                            >
                                                Settings
                                            </Link>
                                        )}
                                        <button
                                            type="button"
                                            onClick={() => onToggleBlock(device)}
                                            className={`px-3 py-1 rounded-[var(--vora-radius-md)] text-xs font-semibold transition-colors cursor-pointer ${device.isBlocked
                                                ? 'bg-[var(--vora-bg-sunken)] text-[var(--vora-text-primary)] hover:bg-[var(--vora-border-strong)]'
                                                : 'bg-[var(--vora-danger-soft)] text-[var(--vora-danger-text)] hover:bg-[var(--vora-danger-500)] hover:text-[var(--vora-text-primary)]'}`}
                                        >
                                            {device.isBlocked ? 'Unblock' : 'Block'}
                                        </button>
                                        <button
                                            type="button"
                                            onClick={() => onDelete(device)}
                                            className="p-1.5 rounded-[var(--vora-radius-md)] text-[var(--vora-text-muted)] hover:text-[var(--vora-danger-text)] hover:bg-[var(--vora-danger-soft)] transition-colors cursor-pointer"
                                            title="Remove device"
                                            aria-label={`Remove ${label}`}
                                        >
                                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" /></svg>
                                        </button>
                                    </div>
                                </td>
                            </tr>
                        );
                    })}
                </tbody>
            </table>
        </div>
    );
}
