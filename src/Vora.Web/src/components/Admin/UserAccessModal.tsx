import { useState, useEffect } from 'react';
import { type LibrarySummary } from '../../api/Media/libraryService';
import { type IptvPlaylistVM } from '../../api/Iptv/iptvAdminService';
import { type UserVM } from '../../api/Users/userService';
import { Modal, ModalHeader } from '../Common/Modal';

type AccessTab = 'libraries' | 'liveTv' | 'podcasts' | 'requests';

interface UserAccessModalProps {
    user: UserVM;
    libraries: LibrarySummary[];
    iptvPlaylists: IptvPlaylistVM[];
    onSave: (hasAllLibs: boolean, allowedLibs: string[], canRequest: boolean, autoApprove: boolean, enableAi: boolean, hasAllIptv: boolean, allowedIptv: string[], canRecordLiveTv: boolean, dvrQuotaBytes: number, canTimeshiftIptv: boolean, canAddCustomPodcastFeeds: boolean) => Promise<void>;
    onClose: () => void;
}

function Checkbox({ checked, onChange, label, hint, disabled }: { checked: boolean, onChange: (v: boolean) => void, label: string, hint?: string, disabled?: boolean }) {
    return (
        <label className={`flex items-start gap-3 ${disabled ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'} select-none`}>
            <input
                type="checkbox"
                checked={checked}
                onChange={e => onChange(e.target.checked)}
                disabled={disabled}
                className="w-4 h-4 accent-[var(--vora-accent-500)] mt-0.5"
            />
            <span className="flex flex-col">
                <span className="text-sm font-medium text-[var(--vora-text-primary)]">{label}</span>
                {hint && <span className="text-xs text-[var(--vora-text-muted)] mt-0.5">{hint}</span>}
            </span>
        </label>
    );
}

export default function UserAccessModal({ user, libraries, iptvPlaylists, onSave, onClose }: UserAccessModalProps) {
    const [activeTab, setActiveTab] = useState<AccessTab>('libraries');

    const [editHasAllLibs, setEditHasAllLibs] = useState(user.hasAllLibraryAccess);
    const [editAllowedLibs, setEditAllowedLibs] = useState<string[]>([...user.allowedLibraryIds]);
    const [editHasAllIptv, setEditHasAllIptv] = useState(user.hasAllIptvAccess);
    const [editAllowedIptv, setEditAllowedIptv] = useState<string[]>([...user.allowedIptvPlaylistIds]);
    const [editCanRequest, setEditCanRequest] = useState(user.canRequestMedia);
    const [editAutoApprove, setEditAutoApprove] = useState(user.autoApproveRequests);
    const [editEnableAi, setEditEnableAi] = useState(user.enableAiRecommendations);
    const [editCanRecordLiveTv, setEditCanRecordLiveTv] = useState(user.canRecordLiveTv);
    const [editDvrQuotaGb, setEditDvrQuotaGb] = useState(user.dvrStorageQuotaBytes / (1024 * 1024 * 1024) || 0);
    const [editCanTimeshiftIptv, setEditCanTimeshiftIptv] = useState(user.canTimeshiftIptv);
    const [editCanAddCustomPodcastFeeds, setEditCanAddCustomPodcastFeeds] = useState(user.canAddCustomPodcastFeeds);
    const [isSaving, setIsSaving] = useState(false);

    useEffect(() => {
        setEditHasAllLibs(user.hasAllLibraryAccess);
        setEditAllowedLibs([...user.allowedLibraryIds]);
        setEditHasAllIptv(user.hasAllIptvAccess);
        setEditAllowedIptv([...user.allowedIptvPlaylistIds]);
        setEditCanRequest(user.canRequestMedia);
        setEditAutoApprove(user.autoApproveRequests);
        setEditEnableAi(user.enableAiRecommendations);
        setEditCanRecordLiveTv(user.canRecordLiveTv);
        setEditDvrQuotaGb(user.dvrStorageQuotaBytes / (1024 * 1024 * 1024) || 0);
        setEditCanTimeshiftIptv(user.canTimeshiftIptv);
        setEditCanAddCustomPodcastFeeds(user.canAddCustomPodcastFeeds);
    }, [user]);

    const toggleLibraryAccess = (libId: string) => {
        setEditAllowedLibs(prev =>
            prev.includes(libId) ? prev.filter(id => id !== libId) : [...prev, libId]
        );
    };

    const handleSubmit = async (e: React.SyntheticEvent<HTMLFormElement>) => {
        e.preventDefault();
        setIsSaving(true);
        try {
            const quotaBytes = Math.round(editDvrQuotaGb * 1024 * 1024 * 1024);
            const timeshiftValue = user.isAdmin ? true : editCanTimeshiftIptv;
            const recordValue = user.isAdmin ? true : editCanRecordLiveTv;
            await onSave(editHasAllLibs, editAllowedLibs, editCanRequest, editAutoApprove, editEnableAi, editHasAllIptv, editAllowedIptv, recordValue, quotaBytes, timeshiftValue, editCanAddCustomPodcastFeeds);
        } finally {
            setIsSaving(false);
        }
    };

    const tabs: { id: AccessTab; label: string }[] = [
        { id: 'libraries', label: 'Libraries' },
        { id: 'liveTv', label: 'Live TV' },
        { id: 'podcasts', label: 'Podcasts' },
        { id: 'requests', label: 'Requests & AI' },
    ];

    return (
        <Modal
            isOpen={true}
            onClose={onClose}
            size="2xl"
            surface="light"
            cardClassName="p-8"
        >
            <ModalHeader
                title={`Account Access: ${user.displayName}`}
                onClose={onClose}
                closeDisabled={isSaving}
                bordered={false}
                surface="light"
            />
            <div className="border-b border-[var(--vora-border-subtle)] mb-4" />

            <div className="flex gap-2 mb-6 -mt-2 flex-wrap">
                {tabs.map(tab => {
                    const isActive = activeTab === tab.id;
                    return (
                        <button
                            key={tab.id}
                            type="button"
                            onClick={() => setActiveTab(tab.id)}
                            className={`px-4 py-2 text-sm font-semibold rounded-[var(--vora-radius-md)] transition-colors cursor-pointer ${isActive ? 'bg-[var(--vora-accent-soft)] text-[var(--vora-accent-text)]' : 'bg-[var(--vora-bg-sunken)] text-[var(--vora-text-secondary)] hover:bg-[var(--vora-bg-raised)] hover:text-[var(--vora-text-primary)]'}`}
                            style={isActive ? { border: '1px solid var(--vora-accent-500)' } : { border: '1px solid var(--vora-border-subtle)' }}
                        >
                            {tab.label}
                        </button>
                    );
                })}
            </div>

            <form onSubmit={handleSubmit}>
                <div className="mb-6 min-h-[280px]">
                    {activeTab === 'libraries' && (
                        <>
                            <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-3">What libraries can this household see?</label>
                            <div className="flex gap-6 mb-4">
                                <label className={`flex items-center gap-2 ${user.isAdmin ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'}`}>
                                    <input type="radio" checked={user.isAdmin || editHasAllLibs} onChange={() => setEditHasAllLibs(true)} disabled={user.isAdmin} className="accent-[var(--vora-accent-500)]" />
                                    <span className="text-sm text-[var(--vora-text-primary)]">All Libraries</span>
                                </label>
                                <label className={`flex items-center gap-2 ${user.isAdmin ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'}`}>
                                    <input type="radio" checked={!user.isAdmin && !editHasAllLibs} onChange={() => setEditHasAllLibs(false)} disabled={user.isAdmin} className="accent-[var(--vora-accent-500)]" />
                                    <span className="text-sm text-[var(--vora-text-primary)]">Selected Libraries</span>
                                </label>
                            </div>

                            {!user.isAdmin && !editHasAllLibs && (
                                <div className="bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-[var(--vora-radius-md)] p-3 space-y-2.5">
                                    {libraries.map(lib => (
                                        <label key={lib.id} className="flex items-center gap-3 cursor-pointer select-none">
                                            <input type="checkbox" checked={editAllowedLibs.includes(lib.id)} onChange={() => toggleLibraryAccess(lib.id)} className="w-4 h-4 accent-[var(--vora-accent-500)]" />
                                            <span className="text-sm font-medium text-[var(--vora-text-primary)]">{lib.name}</span>
                                        </label>
                                    ))}
                                </div>
                            )}
                        </>
                    )}

                    {activeTab === 'liveTv' && (
                        <>
                            <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-3">What IPTV playlists can this household see?</label>
                            <div className="flex gap-6 mb-4">
                                <label className={`flex items-center gap-2 ${user.isAdmin ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'}`}>
                                    <input type="radio" checked={user.isAdmin || editHasAllIptv} onChange={() => setEditHasAllIptv(true)} disabled={user.isAdmin} className="accent-[var(--vora-accent-500)]" />
                                    <span className="text-sm text-[var(--vora-text-primary)]">All Playlists</span>
                                </label>
                                <label className={`flex items-center gap-2 ${user.isAdmin ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'}`}>
                                    <input type="radio" checked={!user.isAdmin && !editHasAllIptv} onChange={() => setEditHasAllIptv(false)} disabled={user.isAdmin} className="accent-[var(--vora-accent-500)]" />
                                    <span className="text-sm text-[var(--vora-text-primary)]">Selected Playlists</span>
                                </label>
                            </div>

                            {!user.isAdmin && !editHasAllIptv && (
                                <div className="bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-[var(--vora-radius-md)] p-3 space-y-2.5 mb-4">
                                    {iptvPlaylists.map(playlist => (
                                        <label key={playlist.id} className="flex items-center gap-3 cursor-pointer select-none">
                                            <input
                                                type="checkbox"
                                                checked={editAllowedIptv.includes(playlist.id)}
                                                onChange={() => setEditAllowedIptv(prev => prev.includes(playlist.id) ? prev.filter(id => id !== playlist.id) : [...prev, playlist.id])}
                                                className="w-4 h-4 accent-[var(--vora-accent-500)]"
                                            />
                                            <span className="text-sm font-medium text-[var(--vora-text-primary)]">{playlist.name}</span>
                                        </label>
                                    ))}
                                </div>
                            )}

                            <div className="pt-5 border-t border-[var(--vora-border-subtle)]">
                                <Checkbox
                                    checked={editCanTimeshiftIptv}
                                    onChange={setEditCanTimeshiftIptv}
                                    label={`Allow timeshift on IPTV streams${user.isAdmin ? ' (admins always have this)' : ''}`}
                                    hint="When on, streams are buffered server-side so the user can pause and seek backwards. When off, the channel plays live with no rewind — the server proxies the stream but doesn't transcode or buffer, which uses far less CPU and disk per active viewer."
                                    disabled={user.isAdmin}
                                />
                            </div>

                            <div className="mt-5 pt-5 border-t border-[var(--vora-border-subtle)]">
                                <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-3">DVR</label>
                                <div className="space-y-4">
                                    <Checkbox
                                        checked={editCanRecordLiveTv}
                                        onChange={setEditCanRecordLiveTv}
                                        label="Allow user to record Live TV"
                                    />
                                    <div className="ml-7 flex items-center gap-4">
                                        <div className="flex flex-col">
                                            <span className="text-sm font-semibold text-[var(--vora-text-primary)]">Storage quota (GB)</span>
                                            <span className="text-xs text-[var(--vora-text-muted)]">0 = unlimited</span>
                                        </div>
                                        <input
                                            type="number"
                                            min={0}
                                            value={editDvrQuotaGb}
                                            onChange={e => setEditDvrQuotaGb(parseFloat(e.target.value) || 0)}
                                            className="vora-input w-24 text-center font-semibold"
                                        />
                                    </div>
                                </div>
                            </div>
                        </>
                    )}

                    {activeTab === 'podcasts' && (
                        <Checkbox
                            checked={user.isAdmin || editCanAddCustomPodcastFeeds}
                            onChange={setEditCanAddCustomPodcastFeeds}
                            label="Allow this household to add custom podcast feeds"
                            hint="When on, members of this household can paste any RSS URL and use iTunes search to subscribe to podcasts. When off, they can only subscribe to shows in the server's curated catalog. The household's primary user can also restrict individual profiles further from Account Settings."
                            disabled={user.isAdmin}
                        />
                    )}

                    {activeTab === 'requests' && (
                        <>
                            <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-3">Request permissions</label>
                            <div className="space-y-4 mb-6">
                                <Checkbox
                                    checked={editCanRequest}
                                    onChange={setEditCanRequest}
                                    label="Allow user to request media"
                                />
                                <div className={`${!editCanRequest ? 'opacity-50' : ''}`}>
                                    <Checkbox
                                        checked={editAutoApprove}
                                        onChange={setEditAutoApprove}
                                        label="Automatically approve their requests"
                                        disabled={!editCanRequest}
                                    />
                                </div>
                            </div>

                            <div className="pt-5 border-t border-[var(--vora-border-subtle)]">
                                <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-3">AI Features</label>
                                <Checkbox
                                    checked={editEnableAi}
                                    onChange={setEditEnableAi}
                                    label="Enable AI recommendations"
                                />
                            </div>
                        </>
                    )}
                </div>

                <div className="pt-4">
                    <button type="submit" disabled={isSaving} className="vora-button-primary w-full">
                        {isSaving ? 'Saving…' : 'Save access levels'}
                    </button>
                </div>
            </form>
        </Modal>
    );
}
