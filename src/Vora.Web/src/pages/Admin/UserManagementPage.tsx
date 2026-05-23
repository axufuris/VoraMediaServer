import { useEffect, useState, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { libraryService, type LibrarySummary } from '../../api/Media/libraryService';
import { authService } from '../../api/Auth/authService';
import { iptvAdminService, type IptvPlaylistVM } from '../../api/Iptv/iptvAdminService';
import UserAccessModal from '../../components/Admin/UserAccessModal';
import { useDialog } from '../../dialogs';
import { type UserVM, userService } from '../../api/Users/userService';
import { profileService, type UserProfileVM } from '../../api/Users/profileService';
import PageHeader from '../../components/Admin/Primitives/PageHeader';
import HealthBadge from '../../components/Admin/Primitives/HealthBadge';

export default function UserManagementPage() {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const [users, setUsers] = useState<UserVM[]>([]);
    const [libraries, setLibraries] = useState<LibrarySummary[]>([]);
    const [iptvPlaylists, setIptvPlaylists] = useState<IptvPlaylistVM[]>([]);
    const [inviteCode, setInviteCode] = useState<string | null>(null);
    const [loading, setLoading] = useState(true);

    const [editingUser, setEditingUser] = useState<UserVM | null>(null);
    const [editingShowtimes, setEditingShowtimes] = useState<UserProfileVM | null>(null);

    const loadData = useCallback(async () => {
        try {
            const [usersData, libsData, iptvData] = await Promise.all([
                userService.getAllUsers(serverId),
                libraryService.getLibraries(serverId),
                iptvAdminService.getPlaylists(serverId),
            ]);
            setUsers(usersData);
            setLibraries(libsData);
            setIptvPlaylists(iptvData);
        } catch (error) {
            console.error('Failed to load data', error);
        } finally {
            setLoading(false);
        }
    }, [serverId]);

    useEffect(() => {
        loadData();
    }, [loadData]);

    const handleGenerateInvite = async () => {
        try {
            const code = await authService.generateInviteCode(serverId);
            setInviteCode(code);
        } catch {
            await dialog.alert('Failed to generate invite code.');
        }
    };

    const handleAccessSave = async (hasAllLibs: boolean, allowedLibs: string[], canRequest: boolean, autoApprove: boolean, enableAi: boolean, hasAllIptv: boolean, allowedIptv: string[], canRecordLiveTv: boolean, dvrQuotaBytes: number, canTimeshiftIptv: boolean, canAddCustomPodcastFeeds: boolean) => {
        if (!editingUser) return;
        try {
            await userService.updateUserAccess(editingUser.id, hasAllLibs, allowedLibs, canRequest, autoApprove, enableAi, hasAllIptv, allowedIptv, canRecordLiveTv, dvrQuotaBytes, canTimeshiftIptv, canAddCustomPodcastFeeds, serverId);
            setEditingUser(null);
            await loadData();
        } catch {
            await dialog.alert('Failed to update user access.');
            throw new Error('Failed');
        }
    };

    if (loading) {
        return (
            <div data-vora-page="">
                <PageHeader title="Users & Access" description="Manage accounts, profiles, and library permissions." />
                <div className="p-8 max-w-6xl mx-auto">
                    <div className="vora-skeleton h-32" />
                </div>
            </div>
        );
    }

    return (
        <div data-vora-page="">
            <PageHeader
                title="Users & Access"
                description="Manage accounts, profiles, library permissions, and server invites."
            />

            <div className="p-8 max-w-6xl mx-auto space-y-10">
                <section className="vora-card p-6">
                    <div className="flex items-start justify-between gap-6 mb-1">
                        <div>
                            <h2 className="text-base font-semibold text-[var(--vora-text-primary)]">Server Invites</h2>
                            <p className="text-sm text-[var(--vora-text-muted)] mt-0.5">
                                Generate a temporary 4-digit PIN so a friend or family member can register an account on this server.
                            </p>
                        </div>
                        {!inviteCode && (
                            <button type="button" onClick={handleGenerateInvite} className="vora-button-secondary shrink-0">
                                Generate code
                            </button>
                        )}
                    </div>

                    {inviteCode && (
                        <div className="mt-5 p-5 border-2 border-dashed border-[var(--vora-accent-500)] bg-[var(--vora-accent-soft)] rounded-[var(--vora-radius-lg)] text-center max-w-md">
                            <p className="text-xs uppercase tracking-widest font-semibold text-[var(--vora-accent-text)] mb-2">
                                Expires in 30 minutes
                            </p>
                            <p className="text-5xl font-bold tracking-[0.4em] text-[var(--vora-accent-active)] mb-3 font-mono">{inviteCode}</p>
                            <button
                                type="button"
                                onClick={() => setInviteCode(null)}
                                className="text-xs font-semibold text-[var(--vora-accent-text)] hover:text-[var(--vora-accent-active)] cursor-pointer"
                            >
                                Clear
                            </button>
                        </div>
                    )}
                </section>

                <section className="space-y-4">
                    <div className="flex items-end justify-between">
                        <div>
                            <h2 className="text-base font-semibold text-[var(--vora-text-primary)]">Registered Accounts</h2>
                            <p className="text-sm text-[var(--vora-text-muted)]">{users.length} account{users.length === 1 ? '' : 's'}</p>
                        </div>
                    </div>

                    <div className="space-y-4">
                        {users.map(user => (
                            <div key={user.id} className="vora-card overflow-hidden">
                                <div className="flex items-center justify-between gap-4 px-5 py-4 border-b border-[var(--vora-border-subtle)]">
                                    <div className="flex items-center gap-3 min-w-0">
                                        <div className="w-10 h-10 rounded-full bg-[var(--vora-bg-sunken)] text-[var(--vora-text-secondary)] flex items-center justify-center font-bold text-sm shrink-0">
                                            {user.displayName.charAt(0).toUpperCase()}
                                        </div>
                                        <div className="min-w-0">
                                            <div className="flex items-center gap-2 mb-0.5">
                                                <span className="text-sm font-semibold text-[var(--vora-text-primary)] truncate">{user.displayName}</span>
                                                {user.isAdmin && <HealthBadge tone="warn" showDot={false}>Admin</HealthBadge>}
                                            </div>
                                            <p className="text-xs text-[var(--vora-text-muted)] truncate">{user.email}</p>
                                        </div>
                                    </div>

                                    <div className="flex items-center gap-5 shrink-0">
                                        <div className="text-right hidden sm:block">
                                            <p className="text-[10px] font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-0.5">Library access</p>
                                            <p className="text-xs text-[var(--vora-text-secondary)]">
                                                {user.hasAllLibraryAccess ? 'All libraries' : `${user.allowedLibraryIds.length} libraries`}
                                            </p>
                                        </div>
                                        <button
                                            type="button"
                                            onClick={() => setEditingUser(user)}
                                            className="vora-button-secondary text-xs"
                                        >
                                            Edit access
                                        </button>
                                    </div>
                                </div>

                                <div className="p-4 grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-6 gap-3 bg-[var(--vora-bg-sunken)]/40">
                                    {user.profiles.length === 0 ? (
                                        <div className="col-span-full text-center py-4 text-xs text-[var(--vora-text-muted)] italic">No profiles created.</div>
                                    ) : (
                                        user.profiles.map(profile => (
                                            <div key={profile.id} className="bg-[var(--vora-bg-surface)] p-3 rounded-[var(--vora-radius-md)] flex flex-col items-center border border-[var(--vora-border-subtle)] text-center relative">
                                                {profile.isAdmin && (
                                                    <div className="absolute top-1.5 right-1.5 text-[var(--vora-accent-500)]" title="Master Profile">
                                                        <svg className="w-3.5 h-3.5" fill="currentColor" viewBox="0 0 20 20"><path fillRule="evenodd" d="M10 2a1 1 0 011 1v1.323l3.954 1.582 1.599-.8a1 1 0 01.894 1.79l-1.233.616 1.738 5.42a1 1 0 01-.285 1.05A3.989 3.989 0 0115 15a3.989 3.989 0 01-2.667-1.019 1 1 0 01-.285-1.05l1.715-5.349L11 6.477V16h2a1 1 0 110 2H7a1 1 0 110-2h2V6.477L6.237 7.582l1.715 5.349a1 1 0 01-.285 1.05A3.989 3.989 0 015 15a3.989 3.989 0 01-2.667-1.019 1 1 0 01-.285-1.05l1.738-5.42-1.233-.617a1 1 0 01.894-1.788l1.599.799L9 4.323V3a1 1 0 011-1z" clipRule="evenodd" /></svg>
                                                    </div>
                                                )}
                                                <div className="w-11 h-11 rounded-full bg-[var(--vora-bg-sunken)] mb-2 overflow-hidden flex items-center justify-center text-[var(--vora-text-secondary)] font-bold text-base">
                                                    {profile.profileImageUrl ? (
                                                        <img src={profile.profileImageUrl} alt={profile.name} className="w-full h-full object-cover" />
                                                    ) : (
                                                        profile.name.charAt(0).toUpperCase()
                                                    )}
                                                </div>
                                                <span className="font-semibold text-[var(--vora-text-primary)] text-xs truncate w-full">{profile.name}</span>
                                                <div className="flex items-center gap-1 mt-1 flex-wrap justify-center">
                                                    {profile.hasPin && <span className="text-[9px] text-[var(--vora-text-muted)] uppercase tracking-widest font-bold">PIN</span>}
                                                    {profile.showtimesLocation && (
                                                        <span
                                                            className="text-[9px] text-[var(--vora-accent-text)] uppercase tracking-widest font-bold truncate max-w-full"
                                                            title={`Showtimes location: ${profile.showtimesLocation}`}
                                                        >
                                                            ⌖ {profile.showtimesLocation}
                                                        </span>
                                                    )}
                                                </div>
                                                <button
                                                    type="button"
                                                    onClick={() => setEditingShowtimes(profile)}
                                                    title="Set movie showtimes location"
                                                    className="mt-2 text-[10px] font-semibold px-2 py-0.5 rounded border border-[var(--vora-border-subtle)] text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)] hover:border-[var(--vora-accent-500)] cursor-pointer"
                                                >
                                                    Showtimes
                                                </button>
                                            </div>
                                        ))
                                    )}
                                </div>
                            </div>
                        ))}
                    </div>
                </section>
            </div>

            {editingUser && (
                <UserAccessModal
                    user={editingUser}
                    libraries={libraries}
                    iptvPlaylists={iptvPlaylists}
                    onSave={handleAccessSave}
                    onClose={() => setEditingUser(null)}
                />
            )}

            {editingShowtimes && (
                <ShowtimesLocationModal
                    profile={editingShowtimes}
                    serverId={serverId}
                    onClose={() => setEditingShowtimes(null)}
                    onSaved={async () => { setEditingShowtimes(null); await loadData(); }}
                />
            )}
        </div>
    );
}

interface ShowtimesLocationModalProps {
    profile: UserProfileVM;
    serverId?: string;
    onClose: () => void;
    onSaved: () => void | Promise<void>;
}

function ShowtimesLocationModal({ profile, serverId, onClose, onSaved }: ShowtimesLocationModalProps) {
    const dialog = useDialog();
    const [value, setValue] = useState(profile.showtimesLocation || '');
    const [saving, setSaving] = useState(false);

    const handleSave = async () => {
        setSaving(true);
        try {
            const trimmed = value.trim();
            await profileService.adminSetShowtimesLocation(profile.id, trimmed === '' ? null : trimmed, serverId);
            await onSaved();
        } catch {
            await dialog.alert('Failed to save showtimes location.');
        } finally {
            setSaving(false);
        }
    };

    const handleClear = async () => {
        setSaving(true);
        try {
            await profileService.adminSetShowtimesLocation(profile.id, null, serverId);
            await onSaved();
        } catch {
            await dialog.alert('Failed to clear showtimes location.');
        } finally {
            setSaving(false);
        }
    };

    return (
        <div className="fixed inset-0 z-[200] flex items-center justify-center bg-black/50 p-4">
            <div className="bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-[var(--vora-radius-md)] max-w-md w-full">
                <div className="p-5 border-b border-[var(--vora-border-subtle)]">
                    <h2 className="text-base font-semibold text-[var(--vora-text-primary)]">Movie showtimes location</h2>
                    <p className="text-xs text-[var(--vora-text-muted)] mt-1">
                        Set the ZIP code or city used to look up movie theaters for <span className="font-semibold text-[var(--vora-text-secondary)]">{profile.name}</span>. Leave blank to fall back to the server default.
                    </p>
                </div>
                <div className="p-5 space-y-3">
                    <input
                        type="text"
                        value={value}
                        onChange={e => setValue(e.target.value)}
                        placeholder="e.g. 90210, or Austin TX"
                        className="vora-input text-sm w-full"
                        maxLength={120}
                        autoFocus
                    />
                </div>
                <div className="p-5 border-t border-[var(--vora-border-subtle)] flex items-center justify-between gap-2">
                    <button
                        type="button"
                        onClick={handleClear}
                        disabled={saving || !profile.showtimesLocation}
                        className="text-xs font-semibold text-[var(--vora-text-muted)] hover:text-[var(--vora-danger-text)] cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed"
                    >
                        Clear
                    </button>
                    <div className="flex gap-2">
                        <button type="button" onClick={onClose} disabled={saving} className="vora-button-secondary text-xs">Cancel</button>
                        <button type="button" onClick={handleSave} disabled={saving} className="vora-button-primary text-xs">
                            {saving ? 'Saving…' : 'Save'}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
}
