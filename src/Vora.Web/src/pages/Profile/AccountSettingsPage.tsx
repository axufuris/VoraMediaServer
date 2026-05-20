import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { libraryService, type LibrarySummary } from '../../api/Media/libraryService';
import { iptvClientService } from '../../api/Iptv/iptvClientService';
import { type IptvPlaylistVM } from '../../api/Iptv/iptvAdminService';
import { useDialog } from '../../dialogs';

import { profileService, type ProfileScheduleVM, type UserProfileVM } from '../../api/Users/profileService';
import { userImageService } from '../../api/Users/userImageService';
import { type UserVM, userService } from '../../api/Users/userService';
import { musicService } from '../../api/Music/musicService';
export default function AccountSettingsPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const [user, setUser] = useState<UserVM | null>(null);
    const [libraries, setLibraries] = useState<LibrarySummary[]>([]);
    const [iptvPlaylists, setIptvPlaylists] = useState<IptvPlaylistVM[]>([]);

    const [email, setEmail] = useState('');
    const [displayName, setDisplayName] = useState('');
    const [newPassword, setNewPassword] = useState('');
    const [accountMsg, setAccountMsg] = useState('');

    const [editingProfile, setEditingProfile] = useState<UserProfileVM | null>(null);
    const [refreshTrigger, setRefreshTrigger] = useState(0); // Safely triggers re-fetches

    const isProfileAdmin = localStorage.getItem('is_profile_admin') === 'true';
    const profileToken = localStorage.getItem('profile_token');
    const activeProfileId = profileToken ? JSON.parse(atob(profileToken.split('.')[1])).sub : '';

    useEffect(() => {
        let isMounted = true;

        const fetchData = async () => {
            const userId = localStorage.getItem('user_id');
            if (!userId) return;

            try {
                const [userData, libs, iptvData] = await Promise.all([
                    userService.getUserAccount(userId, serverId),
                    libraryService.getLibraries(serverId),
                    iptvClientService.getPlaylists(userId, serverId)
                ]);

                if (!isMounted) return;

                setUser(userData);
                setEmail(userData.email);
                setDisplayName(userData.displayName);
                setLibraries(libs);
                setIptvPlaylists(iptvData);

                if (!isProfileAdmin) {
                    const myProfile = userData.profiles.find(p => p.id === activeProfileId);
                    if (myProfile) setEditingProfile(myProfile);
                }
            } catch {
                if (isMounted) console.error("Failed to load account settings.");
            }
        };

        void fetchData();

        return () => {
            isMounted = false;
        };
    }, [serverId, isProfileAdmin, activeProfileId, refreshTrigger]);

    const handleAccountSave = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!user) return;
        try {
            await userService.updateAccount(user.id, email, displayName, newPassword || undefined, serverId);
            setAccountMsg("Account updated successfully.");
            setNewPassword('');
            setRefreshTrigger(prev => prev + 1);
        } catch {
            setAccountMsg("Failed to update account.");
        }
    };

    const openProfileEditor = (profile: UserProfileVM | 'NEW') => {
        if (profile === 'NEW') {
            setEditingProfile({
                id: 'NEW', name: '', isAdmin: false, hasPin: false,
                allowedMovieRatings: [], allowedTvRatings: [], allowedMusicRatings: [],
                blockUnratedContent: false, hasAllLibraryAccess: true, allowedLibraryIds: [],
                hasAllIptvAccess: true, allowedIptvPlaylistIds: [], accessSchedules: [],
                canRecordLiveTv: false,
                canAddCustomPodcastFeeds: true
            });
        } else {
            setEditingProfile(profile);
        }
    };

    if (!isProfileAdmin) {
        return (
            <div className="p-8 pt-24 max-w-lg mx-auto text-[var(--vora-text-primary)] pb-20">
                <h1 className="text-3xl font-bold mb-8 text-[var(--vora-accent-text)]">My Profile</h1>
                {editingProfile && (
                    <ProfileEditor
                        profile={editingProfile}
                        user={user!}
                        libraries={libraries}
                        iptvPlaylists={iptvPlaylists}
                        serverId={serverId}
                        onClose={() => setEditingProfile(null)}
                        onRefresh={() => setRefreshTrigger(prev => prev + 1)}
                        isProfileAdmin={isProfileAdmin}
                    />
                )}
            </div>
        );
    }

    if (!user) return <div className="p-8 text-[var(--vora-text-primary)]">Loading...</div>;

    return (
        <div className="p-8 pt-24 max-w-4xl mx-auto text-[var(--vora-text-primary)] pb-20">
            <h1 className="text-3xl font-bold mb-8 text-[var(--vora-accent-text)]">Account Settings</h1>

            <div className="bg-[var(--vora-bg-surface)] rounded-lg p-6 mb-8 border border-[var(--vora-border-subtle)] shadow-lg">
                <h2 className="text-xl font-bold mb-4 border-b border-[var(--vora-border-subtle)] pb-2">Primary Details</h2>
                {accountMsg && <p className="text-green-400 mb-4 text-sm">{accountMsg}</p>}
                <form onSubmit={handleAccountSave} className="space-y-4">
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                        <div>
                            <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">Email</label>
                            <input required type="email" value={email} onChange={e => setEmail(e.target.value)} className="w-full bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-md p-2.5 text-[var(--vora-text-primary)] outline-none focus:border-[var(--vora-accent-500)]" />
                        </div>
                        <div>
                            <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">Display Name</label>
                            <input required type="text" value={displayName} onChange={e => setDisplayName(e.target.value)} className="w-full bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-md p-2.5 text-[var(--vora-text-primary)] outline-none focus:border-[var(--vora-accent-500)]" />
                        </div>
                        <div>
                            <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">New Password (leave blank to keep)</label>
                            <input type="password" value={newPassword} onChange={e => setNewPassword(e.target.value)} className="w-full bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-md p-2.5 text-[var(--vora-text-primary)] outline-none focus:border-[var(--vora-accent-500)]" />
                        </div>
                    </div>
                    <button type="submit" className="bg-[var(--vora-bg-raised)] hover:bg-[var(--vora-bg-raised)] px-6 py-2 rounded-md font-bold transition-colors">Save Account Changes</button>
                </form>
            </div>

            <div className="bg-[var(--vora-bg-surface)] rounded-lg p-6 border border-[var(--vora-border-subtle)] shadow-lg">
                <h2 className="text-xl font-bold mb-4 border-b border-[var(--vora-border-subtle)] pb-2">Managed Profiles</h2>
                <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                    {[...user.profiles].sort((a, b) => (a.isAdmin === b.isAdmin ? 0 : a.isAdmin ? -1 : 1)).map(profile => (
                        <div key={profile.id} onClick={() => openProfileEditor(profile)} className="bg-[var(--vora-bg-sunken)] p-4 rounded-lg flex flex-col items-center border border-[var(--vora-border-subtle)] hover:border-[var(--vora-accent-500)] cursor-pointer transition-colors group relative">
                            <div className="w-16 h-16 rounded-full bg-[var(--vora-bg-surface)] mb-3 overflow-hidden relative">
                                {profile.profileImageUrl ? (
                                    <img src={profile.profileImageUrl} className="w-full h-full object-cover" />
                                ) : (
                                    <div className="w-full h-full flex items-center justify-center text-xl font-bold text-[var(--vora-text-disabled)] group-hover:text-[var(--vora-accent-text)]">{profile.name.charAt(0)}</div>
                                )}
                            </div>
                            <div className="flex items-center gap-1.5">
                                <span className="font-bold text-[var(--vora-text-primary)]">{profile.name}</span>
                                {profile.isAdmin && (
                                    <svg className="w-4 h-4 text-[var(--vora-accent-text)]" fill="currentColor" viewBox="0 0 20 20">
                                        <title>Account Admin</title>
                                        <path d="M9.049 2.927c.3-.921 1.603-.921 1.902 0l1.07 3.292a1 1 0 00.95.69h3.462c.969 0 1.371 1.24.588 1.81l-2.8 2.034a1 1 0 00-.364 1.118l1.07 3.292c.3.921-.755 1.688-1.54 1.118l-2.8-2.034a1 1 0 00-1.175 0l-2.8 2.034c-.784.57-1.838-.197-1.539-1.118l1.07-3.292a1 1 0 00-.364-1.118L2.98 8.72c-.783-.57-.38-1.81.588-1.81h3.461a1 1 0 00.951-.69l1.07-3.292z" />
                                    </svg>
                                )}
                            </div>
                        </div>
                    ))}
                    <div onClick={() => openProfileEditor('NEW')} className="bg-[var(--vora-bg-sunken)] p-4 rounded-lg flex flex-col items-center border border-dashed border-[var(--vora-border-subtle)] hover:border-[var(--vora-accent-500)] cursor-pointer transition-colors group justify-center">
                        <div className="w-16 h-16 rounded-full bg-[var(--vora-bg-surface)] mb-3 flex items-center justify-center text-[var(--vora-text-disabled)] group-hover:text-[var(--vora-accent-text)] transition-colors">
                            <svg className="w-8 h-8" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" /></svg>
                        </div>
                        <span className="font-bold text-[var(--vora-text-muted)] group-hover:text-[var(--vora-text-primary)]">Add Profile</span>
                    </div>
                </div>
            </div>

            {editingProfile && (
                <div className="fixed inset-0 bg-black/80 flex items-center justify-center z-50 p-4">
                    <div className="bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-xl p-8 max-w-2xl w-full max-h-[90vh] overflow-y-auto custom-scrollbar">
                        <div className="flex justify-between items-center mb-6 border-b border-[var(--vora-border-subtle)] pb-4">
                            <h2 className="text-2xl font-bold">{editingProfile.id === 'NEW' ? 'Create Profile' : `Edit Profile: ${editingProfile.name}`}</h2>
                            <button onClick={() => setEditingProfile(null)} className="text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)]">✕</button>
                        </div>
                        <ProfileEditor
                            profile={editingProfile}
                            user={user}
                            libraries={libraries}
                            iptvPlaylists={iptvPlaylists}
                            serverId={serverId}
                            onClose={() => setEditingProfile(null)}
                            onRefresh={() => setRefreshTrigger(prev => prev + 1)}
                            isProfileAdmin={isProfileAdmin}
                        />
                    </div>
                </div>
            )}
        </div>
    );
}

interface ProfileEditorProps {
    profile: UserProfileVM;
    user: UserVM;
    libraries: LibrarySummary[];
    iptvPlaylists: IptvPlaylistVM[];
    serverId?: string;
    onClose: () => void;
    onRefresh: () => void;
    isProfileAdmin: boolean;
}

type ProfileEditTab = 'basics' | 'parental' | 'access' | 'permissions' | 'schedule' | 'connections';

function ProfileEditor({ profile, user, libraries, iptvPlaylists, serverId, onClose, onRefresh, isProfileAdmin }: ProfileEditorProps) {
    const dialog = useDialog();
    const [activeTab, setActiveTab] = useState<ProfileEditTab>('basics');
    const [editName, setEditName] = useState(profile.name);
    const [editImageUrl, setEditImageUrl] = useState(profile.profileImageUrl || '');
    const [editPin, setEditPin] = useState('');
    const [editAllowAllRatings, setEditAllowAllRatings] = useState(
        (profile.allowedMovieRatings?.length ?? 0) === 0
        && (profile.allowedTvRatings?.length ?? 0) === 0
        && (profile.allowedMusicRatings?.length ?? 0) === 0
    );
    const [editAllowedMovieRatings, setEditAllowedMovieRatings] = useState([...(profile.allowedMovieRatings || [])]);
    const [editAllowedTvRatings, setEditAllowedTvRatings] = useState([...(profile.allowedTvRatings || [])]);
    const [editAllowedMusicRatings, setEditAllowedMusicRatings] = useState([...(profile.allowedMusicRatings || [])]);
    const [editBlockUnrated, setEditBlockUnrated] = useState(profile.blockUnratedContent);
    const [editHasAllLibs, setEditHasAllLibs] = useState(profile.hasAllLibraryAccess);
    const [editAllowedLibs, setEditAllowedLibs] = useState([...(profile.allowedLibraryIds || [])]);
    const [editHasAllIptv, setEditHasAllIptv] = useState(profile.hasAllIptvAccess ?? true);
    const [editAllowedIptv, setEditAllowedIptv] = useState([...(profile.allowedIptvPlaylistIds || [])]);
    const [editCanRecordLiveTv, setEditCanRecordLiveTv] = useState(profile.canRecordLiveTv || false); // <-- NEW
    const [editCanAddCustomPodcastFeeds, setEditCanAddCustomPodcastFeeds] = useState(profile.canAddCustomPodcastFeeds ?? true);
    const [editSchedules, setEditSchedules] = useState([...profile.accessSchedules]);
    const [uploadingImage, setUploadingImage] = useState(false);

    const handleProfileSave = async (e: React.FormEvent) => {
        e.preventDefault();
        try {
            let finalPin: string | null = editPin;
            if (editPin === "CLEAR") finalPin = "";
            else if (editPin === "") finalPin = null;

            const finalMovieRatings = editAllowAllRatings ? [] : editAllowedMovieRatings;
            const finalTvRatings = editAllowAllRatings ? [] : editAllowedTvRatings;
            const finalMusicRatings = editAllowAllRatings ? [] : editAllowedMusicRatings;

            if (profile.id === 'NEW') {
                await profileService.createProfile(user.id, editName, editImageUrl, finalPin || undefined, finalMovieRatings, finalTvRatings, finalMusicRatings, editHasAllLibs, editBlockUnrated, editAllowedLibs, editHasAllIptv, editAllowedIptv, editSchedules, editCanRecordLiveTv, editCanAddCustomPodcastFeeds, serverId);
            } else {
                await profileService.updateProfile(profile.id, editName, editImageUrl, finalPin, finalMovieRatings, finalTvRatings, finalMusicRatings, editHasAllLibs, editBlockUnrated, editAllowedLibs, editHasAllIptv, editAllowedIptv, editSchedules, editCanRecordLiveTv, editCanAddCustomPodcastFeeds, serverId);
            }
            onClose();
            onRefresh();
            if (!isProfileAdmin) await dialog.alert("Profile saved successfully.");
        } catch {
            await dialog.alert("Failed to save profile.");
        }
    };

    const handleProfileDelete = async () => {
        if (profile.id === 'NEW') return;
        if (await dialog.confirm(`Are you sure you want to delete the profile ${profile.name}?`)) {
            await profileService.deleteProfile(profile.id, serverId);
            onClose();
            onRefresh();
        }
    };

    const handleImageUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
        if (e.target.files && e.target.files[0]) {
            setUploadingImage(true);
            try {
                const newUrl = await userImageService.uploadImage(e.target.files[0], editImageUrl, serverId);
                setEditImageUrl(newUrl);
            } catch {
                await dialog.alert("Failed to upload image.");
            } finally {
                setUploadingImage(false);
            }
        }
    };

    const updateSchedule = (index: number, field: keyof ProfileScheduleVM, value: string | number) => {
        setEditSchedules(prev => {
            const copy = [...prev];
            copy[index] = { ...copy[index], [field]: value };
            return copy;
        });
    };

    const showParentalTabs = isProfileAdmin && !profile.isAdmin;
    const currentProfileToken = localStorage.getItem('profile_token');
    const currentProfileId = currentProfileToken
        ? (() => { try { return JSON.parse(atob(currentProfileToken.split('.')[1])).sub as string; } catch { return ''; } })()
        : '';
    const isOwnProfile = profile.id !== 'NEW' && profile.id === currentProfileId;
    const tabs: { id: ProfileEditTab; label: string; show: boolean }[] = [
        { id: 'basics', label: 'Basics', show: true },
        { id: 'parental', label: 'Parental Controls', show: showParentalTabs },
        { id: 'access', label: 'Library & IPTV', show: showParentalTabs },
        { id: 'permissions', label: 'Permissions', show: showParentalTabs },
        { id: 'schedule', label: 'Schedule', show: showParentalTabs },
        { id: 'connections', label: 'Connections', show: isOwnProfile }
    ];

    return (
        <form onSubmit={handleProfileSave} className="space-y-6">
            {tabs.filter(t => t.show).length > 1 && (
                <div className="flex gap-1 border-b border-[var(--vora-border-subtle)] -mt-2 pb-0 flex-wrap">
                    {tabs.filter(t => t.show).map(tab => (
                        <button
                            key={tab.id}
                            type="button"
                            onClick={() => setActiveTab(tab.id)}
                            className={`px-4 py-2 text-sm font-bold rounded-t-md transition-colors cursor-pointer border-b-2 -mb-px ${activeTab === tab.id ? 'border-[var(--vora-accent-500)] text-[var(--vora-accent-text)]' : 'border-transparent text-[var(--vora-text-disabled)] hover:text-[var(--vora-text-primary)]'}`}
                        >
                            {tab.label}
                        </button>
                    ))}
                </div>
            )}

            {activeTab === 'basics' && (
                <>
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                        <div>
                            <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">Profile Name</label>
                            <input required type="text" value={editName} onChange={e => setEditName(e.target.value)} className="w-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-2.5 outline-none focus:border-[var(--vora-accent-500)]" />
                        </div>
                        {isProfileAdmin && (
                            <div>
                                <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">4-Digit PIN Lock</label>
                                <div className="flex gap-2">
                                    <input type="text" maxLength={4} value={editPin === "CLEAR" ? "" : editPin} onChange={e => setEditPin(e.target.value.replace(/[^0-9]/g, ''))} className="flex-1 bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-2.5 outline-none focus:border-[var(--vora-accent-500)]" placeholder={profile.hasPin ? "Enter new PIN to change" : "Leave blank for no PIN"} disabled={editPin === "CLEAR"} />
                                    {profile.hasPin && profile.id !== 'NEW' && (
                                        <button type="button" onClick={() => setEditPin(editPin === "CLEAR" ? "" : "CLEAR")} className={`px-4 rounded-md font-bold text-sm ${editPin === "CLEAR" ? 'bg-[var(--vora-accent-500)]' : 'bg-[var(--vora-bg-surface)] hover:bg-[var(--vora-bg-raised)]'}`}>
                                            {editPin === "CLEAR" ? 'Undo' : 'Remove'}
                                        </button>
                                    )}
                                </div>
                            </div>
                        )}
                    </div>

                    <div>
                        <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">Profile Image</label>
                        <div className="flex gap-3">
                            <input type="text" value={editImageUrl} onChange={e => setEditImageUrl(e.target.value)} className="flex-1 bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-2.5 outline-none focus:border-[var(--vora-accent-500)]" placeholder="https://... or Upload an Image" />
                            <label className={`bg-[var(--vora-bg-surface)] border border-[var(--vora-border-subtle)] rounded-md px-4 py-2.5 flex items-center justify-center font-bold text-sm cursor-pointer hover:bg-[var(--vora-bg-raised)] transition-colors ${uploadingImage ? 'opacity-50 cursor-wait' : ''}`}>
                                {uploadingImage ? 'Uploading...' : 'Upload File'}
                                <input type="file" accept="image/jpeg, image/png, image/webp" className="hidden" onChange={handleImageUpload} disabled={uploadingImage} />
                            </label>
                        </div>
                    </div>
                </>
            )}

            {activeTab === 'parental' && showParentalTabs && (
                <>
                    <div>

                        <div className="mb-6">
                            <div className="flex justify-between items-end mb-2">
                                <label className="block text-sm font-medium text-[var(--vora-text-muted)]">Allowed Content Ratings</label>
                                <label className="flex items-center gap-2 cursor-pointer">
                                    <input type="checkbox" checked={editAllowAllRatings} onChange={e => {
                                        setEditAllowAllRatings(e.target.checked);
                                        if (e.target.checked) {
                                            setEditAllowedMovieRatings([]);
                                            setEditAllowedTvRatings([]);
                                            setEditAllowedMusicRatings([]);
                                        }
                                    }} className="accent-orange-500 w-4 h-4" />
                                    <span className="text-sm font-bold text-[var(--vora-accent-text)]">Allow All</span>
                                </label>
                            </div>

                            {!editAllowAllRatings && (
                                <div className="space-y-4">
                                    <div>
                                        <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-disabled)] mb-2">Movies</label>
                                        <div className="grid grid-cols-3 sm:grid-cols-5 gap-2 bg-[var(--vora-bg-canvas)] p-3 rounded-md border border-[var(--vora-border-subtle)]">
                                            {['G', 'PG', 'PG-13', 'R', 'NC-17'].map(rating => (
                                                <label key={rating} className={`flex items-center justify-center p-2 rounded border cursor-pointer select-none transition-colors ${editAllowedMovieRatings.includes(rating) ? 'bg-[var(--vora-accent-soft)] border-[var(--vora-accent-500)] text-[var(--vora-accent-text)] font-bold' : 'bg-[var(--vora-bg-sunken)] border-[var(--vora-border-subtle)] text-[var(--vora-text-muted)] hover:bg-[var(--vora-bg-surface)] hover:text-[var(--vora-text-primary)]'}`}>
                                                    <input type="checkbox" className="hidden" checked={editAllowedMovieRatings.includes(rating)} onChange={(e) => setEditAllowedMovieRatings(prev => e.target.checked ? [...prev, rating] : prev.filter(r => r !== rating))} />
                                                    {rating}
                                                </label>
                                            ))}
                                        </div>
                                    </div>

                                    <div>
                                        <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-disabled)] mb-2">TV Shows</label>
                                        <div className="grid grid-cols-3 sm:grid-cols-6 gap-2 bg-[var(--vora-bg-canvas)] p-3 rounded-md border border-[var(--vora-border-subtle)]">
                                            {['TV-Y', 'TV-Y7', 'TV-G', 'TV-PG', 'TV-14', 'TV-MA'].map(rating => (
                                                <label key={rating} className={`flex items-center justify-center p-2 rounded border cursor-pointer select-none transition-colors ${editAllowedTvRatings.includes(rating) ? 'bg-[var(--vora-accent-soft)] border-[var(--vora-accent-500)] text-[var(--vora-accent-text)] font-bold' : 'bg-[var(--vora-bg-sunken)] border-[var(--vora-border-subtle)] text-[var(--vora-text-muted)] hover:bg-[var(--vora-bg-surface)] hover:text-[var(--vora-text-primary)]'}`}>
                                                    <input type="checkbox" className="hidden" checked={editAllowedTvRatings.includes(rating)} onChange={(e) => setEditAllowedTvRatings(prev => e.target.checked ? [...prev, rating] : prev.filter(r => r !== rating))} />
                                                    {rating}
                                                </label>
                                            ))}
                                        </div>
                                    </div>

                                    <div>
                                        <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-disabled)] mb-2">Music</label>
                                        <div className="grid grid-cols-2 gap-2 bg-[var(--vora-bg-canvas)] p-3 rounded-md border border-[var(--vora-border-subtle)]">
                                            {['Clean', 'Explicit'].map(rating => (
                                                <label key={rating} className={`flex items-center justify-center p-2 rounded border cursor-pointer select-none transition-colors ${editAllowedMusicRatings.includes(rating) ? 'bg-[var(--vora-accent-soft)] border-[var(--vora-accent-500)] text-[var(--vora-accent-text)] font-bold' : 'bg-[var(--vora-bg-sunken)] border-[var(--vora-border-subtle)] text-[var(--vora-text-muted)] hover:bg-[var(--vora-bg-surface)] hover:text-[var(--vora-text-primary)]'}`}>
                                                    <input type="checkbox" className="hidden" checked={editAllowedMusicRatings.includes(rating)} onChange={(e) => setEditAllowedMusicRatings(prev => e.target.checked ? [...prev, rating] : prev.filter(r => r !== rating))} />
                                                    {rating}
                                                </label>
                                            ))}
                                        </div>
                                    </div>
                                </div>
                            )}

                            <div className="mt-4 flex items-center gap-2">
                                <label className="flex items-center gap-2 cursor-pointer">
                                    <input type="checkbox" checked={editBlockUnrated} onChange={e => setEditBlockUnrated(e.target.checked)} className="accent-orange-500 w-4 h-4" />
                                    <span className="text-sm text-[var(--vora-text-secondary)] font-medium">Block Unrated Content</span>
                                </label>
                                <span className="text-xs text-[var(--vora-text-disabled)] ml-2">(Hides media missing a content rating)</span>
                            </div>
                        </div>
                    </div>
                </>
            )}

            {activeTab === 'access' && showParentalTabs && (
                <>
                        <div className="mb-4">
                            <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-2">Library Access</label>
                            <div className="flex gap-6 mb-3">
                                <label className="flex items-center gap-2 cursor-pointer">
                                    <input type="radio" checked={editHasAllLibs} onChange={() => setEditHasAllLibs(true)} className="accent-orange-500 w-4 h-4" />
                                    <span className="text-sm text-[var(--vora-text-secondary)]">All Libraries</span>
                                </label>
                                <label className="flex items-center gap-2 cursor-pointer">
                                    <input type="radio" checked={!editHasAllLibs} onChange={() => setEditHasAllLibs(false)} className="accent-orange-500 w-4 h-4" />
                                    <span className="text-sm text-[var(--vora-text-secondary)]">Selected Libraries</span>
                                </label>
                            </div>

                            {!editHasAllLibs && (
                                <div className="bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-3 space-y-2 max-h-40 overflow-y-auto custom-scrollbar">
                                    {libraries.map(lib => (
                                        <label key={lib.id} className="flex items-center gap-3 cursor-pointer">
                                            <input type="checkbox" checked={editAllowedLibs.includes(lib.id)} onChange={() => setEditAllowedLibs(prev => prev.includes(lib.id) ? prev.filter(id => id !== lib.id) : [...prev, lib.id])} className="w-4 h-4 accent-orange-500" />
                                            <span className="text-sm text-[var(--vora-text-secondary)]">{lib.name}</span>
                                        </label>
                                    ))}
                                </div>
                            )}
                        </div>

                        {/* NEW: Profile-Level IPTV Provider Access */}
                        <div className="mb-4">
                            <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-2">IPTV Provider Access</label>
                            <div className="flex gap-6 mb-3">
                                <label className="flex items-center gap-2 cursor-pointer">
                                    <input type="radio" checked={editHasAllIptv} onChange={() => setEditHasAllIptv(true)} className="accent-orange-500 w-4 h-4" />
                                    <span className="text-sm text-[var(--vora-text-secondary)]">All Providers</span>
                                </label>
                                <label className="flex items-center gap-2 cursor-pointer">
                                    <input type="radio" checked={!editHasAllIptv} onChange={() => setEditHasAllIptv(false)} className="accent-orange-500 w-4 h-4" />
                                    <span className="text-sm text-[var(--vora-text-secondary)]">Selected Providers</span>
                                </label>
                            </div>

                            {!editHasAllIptv && (
                                <div className="bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-3 space-y-2 max-h-40 overflow-y-auto custom-scrollbar">
                                    {iptvPlaylists.map(playlist => (
                                        <label key={playlist.id} className="flex items-center gap-3 cursor-pointer">
                                            <input type="checkbox" checked={editAllowedIptv.includes(playlist.id)} onChange={() => setEditAllowedIptv(prev => prev.includes(playlist.id) ? prev.filter(id => id !== playlist.id) : [...prev, playlist.id])} className="w-4 h-4 accent-orange-500" />
                                            <span className="text-sm text-[var(--vora-text-secondary)]">{playlist.name}</span>
                                        </label>
                                    ))}
                                    {iptvPlaylists.length === 0 && <p className="text-xs text-[var(--vora-text-disabled)] text-center py-2">No IPTV playlists exist yet.</p>}
                                </div>
                            )}
                        </div>
                </>
            )}

            {activeTab === 'permissions' && showParentalTabs && (
                <>
                        <div className="mb-6">
                            <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-2">DVR Permissions</label>
                            <label className="flex items-center gap-3 cursor-pointer group">
                                <input
                                    type="checkbox"
                                    checked={editCanRecordLiveTv}
                                    onChange={e => setEditCanRecordLiveTv(e.target.checked)}
                                    className="w-5 h-5 accent-orange-500 cursor-pointer"
                                />
                                <span className="text-sm text-[var(--vora-text-primary)] group-hover:text-[var(--vora-text-primary)] transition-colors">Allow this profile to record Live TV</span>
                            </label>
                        </div>

                        <div className="mb-6 pt-4 border-t border-[var(--vora-border-subtle)]">
                            <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-2">Podcast Permissions</label>
                            <label className="flex items-start gap-3 cursor-pointer group">
                                <input
                                    type="checkbox"
                                    checked={editCanAddCustomPodcastFeeds}
                                    onChange={e => setEditCanAddCustomPodcastFeeds(e.target.checked)}
                                    className="w-5 h-5 accent-orange-500 mt-0.5 cursor-pointer"
                                />
                                <span className="flex flex-col">
                                    <span className="text-sm text-[var(--vora-text-primary)] group-hover:text-[var(--vora-text-primary)] transition-colors">Allow this profile to add custom podcast feeds</span>
                                    <span className="text-xs text-[var(--vora-text-disabled)] mt-1">When off, this profile can only subscribe to podcasts from the server's curated catalog.</span>
                                </span>
                            </label>
                        </div>
                </>
            )}

            {activeTab === 'connections' && isOwnProfile && (
                <LastFmConnectionPanel
                    serverId={serverId}
                    lastFmUsername={profile.lastFmUsername}
                    onChanged={onRefresh}
                />
            )}

            {activeTab === 'schedule' && showParentalTabs && (
                <>
                        <div>
                            <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-2">Allowed Access Times</label>
                            {editSchedules.length > 0 ? (
                                <div className="space-y-2 bg-[var(--vora-bg-canvas)] p-3 rounded-md border border-[var(--vora-border-subtle)]">
                                    {editSchedules.map((schedule, idx) => (
                                        <div key={idx} className="flex gap-3 items-center">
                                            <select value={schedule.dayOfWeek} onChange={e => updateSchedule(idx, 'dayOfWeek', parseInt(e.target.value))} className="bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] text-[var(--vora-text-primary)] rounded p-1.5 text-sm outline-none focus:border-[var(--vora-accent-500)]">
                                                <option value={0}>Sunday</option><option value={1}>Monday</option><option value={2}>Tuesday</option>
                                                <option value={3}>Wednesday</option><option value={4}>Thursday</option><option value={5}>Friday</option><option value={6}>Saturday</option>
                                            </select>
                                            <input type="time" value={schedule.startTime} onChange={e => updateSchedule(idx, 'startTime', e.target.value)} className="bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] text-[var(--vora-text-primary)] rounded p-1 outline-none text-sm focus:border-[var(--vora-accent-500)]" />
                                            <span className="text-[var(--vora-text-disabled)] text-sm font-bold">to</span>
                                            <input type="time" value={schedule.endTime} onChange={e => updateSchedule(idx, 'endTime', e.target.value)} className="bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] text-[var(--vora-text-primary)] rounded p-1 outline-none text-sm focus:border-[var(--vora-accent-500)]" />
                                            <button type="button" onClick={() => setEditSchedules(prev => prev.filter((_, i) => i !== idx))} className="text-[var(--vora-danger-text)] hover:text-[var(--vora-danger-text)] ml-auto p-1 font-bold text-lg leading-none cursor-pointer">✕</button>
                                        </div>
                                    ))}
                                </div>
                            ) : (
                                <p className="text-xs text-[var(--vora-text-disabled)] mb-2">No restrictions. Profile has 24/7 access.</p>
                            )}
                            <button type="button" onClick={() => setEditSchedules(prev => [...prev, { dayOfWeek: 0, startTime: "08:00", endTime: "20:00" }])} className="text-sm font-bold text-[var(--vora-accent-text)] hover:text-[var(--vora-accent-text)] mt-2 transition-colors">+ Add Time Block</button>
                        </div>
                </>
            )}

            <div className="flex gap-3 pt-6 border-t border-[var(--vora-border-subtle)]">
                <button type="submit" className="flex-1 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-active)] py-2.5 rounded-md font-bold transition-colors cursor-pointer">Save Profile</button>
                {profile.id !== 'NEW' && !profile.isAdmin && (
                    <button type="button" onClick={handleProfileDelete} className="px-4 bg-[var(--vora-danger-soft)] text-[var(--vora-danger-text)] hover:bg-[var(--vora-danger-500)] hover:text-[var(--vora-text-primary)] rounded-md font-bold transition-colors text-sm cursor-pointer">Delete</button>
                )}
            </div>
        </form>
    );
}

interface LastFmConnectionPanelProps {
    serverId?: string;
    lastFmUsername?: string;
    onChanged: () => void;
}

function LastFmConnectionPanel({ serverId, lastFmUsername, onChanged }: LastFmConnectionPanelProps) {
    const dialog = useDialog();
    const [busy, setBusy] = useState(false);
    const [pendingToken, setPendingToken] = useState<string | null>(null);
    const [authWindowOpened, setAuthWindowOpened] = useState(false);
    const [errorMsg, setErrorMsg] = useState<string | null>(null);

    const handleConnect = async () => {
        setBusy(true);
        setErrorMsg(null);
        try {
            const start = await musicService.startLastFmAuth(serverId);
            setPendingToken(start.token);
            window.open(start.authUrl, '_blank', 'noopener,noreferrer');
            setAuthWindowOpened(true);
        } catch (err: unknown) {
            const status = (err as { response?: { status?: number; data?: { error?: string } } })?.response;
            const msg = status?.data?.error || 'Failed to start Last.fm authorization.';
            setErrorMsg(msg);
        } finally {
            setBusy(false);
        }
    };

    const handleComplete = async () => {
        if (!pendingToken) return;
        setBusy(true);
        setErrorMsg(null);
        try {
            await musicService.completeLastFmAuth(pendingToken, serverId);
            setPendingToken(null);
            setAuthWindowOpened(false);
            onChanged();
        } catch (err: unknown) {
            const status = (err as { response?: { status?: number; data?: { error?: string } } })?.response;
            const msg = status?.data?.error || 'Could not finish authorization. Make sure you clicked Allow in the Last.fm tab.';
            setErrorMsg(msg);
        } finally {
            setBusy(false);
        }
    };

    const handleDisconnect = async () => {
        const ok = await dialog.confirm('Disconnect this profile from Last.fm? Scrobbles from this profile will stop.');
        if (!ok) return;
        setBusy(true);
        try {
            await musicService.disconnectLastFm(serverId);
            onChanged();
        } catch {
            setErrorMsg('Disconnect failed.');
        } finally {
            setBusy(false);
        }
    };

    return (
        <div className="space-y-4">
            <div className="bg-[color-mix(in_srgb,var(--vora-bg-canvas)_60%,transparent)] border border-[var(--vora-border-subtle)] rounded-lg p-5">
                <div className="flex items-start gap-4">
                    <div className="w-12 h-12 rounded bg-red-900/40 border border-red-500/30 flex items-center justify-center shrink-0">
                        <svg className="w-7 h-7 text-[var(--vora-danger-text)]" fill="currentColor" viewBox="0 0 24 24"><path d="M10.584 17.21l-.88-2.392s-1.44 1.601-3.6 1.601c-1.92 0-3.28-1.652-3.28-4.296 0-3.385 1.696-4.59 3.36-4.59 2.4 0 3.16 1.546 3.84 3.605l.88 2.752c.92 2.806 2.661 5.06 7.665 5.06 3.595 0 6.034-1.106 6.034-4.022 0-2.354-1.336-3.589-3.826-4.17l-1.853-.434c-1.276-.295-1.657-.829-1.657-1.71 0-1.001.778-1.59 2.055-1.59 1.396 0 2.139.529 2.258 1.78l2.879-.353c-.234-2.604-2.018-3.687-4.99-3.687-2.62 0-5.19.99-5.19 4.176 0 1.99 1.078 3.243 2.879 3.69l1.973.476c1.476.352 1.97 1.022 1.97 1.91 0 1.137-1.07 1.602-3.097 1.602-3.018 0-4.275-1.589-4.98-3.747l-.91-2.751c-1.16-3.621-3.04-4.92-6.74-4.92C2.382 6.494 0 8.85 0 12.83c0 3.831 1.937 5.871 5.43 5.871 2.815 0 4.182-1.323 5.154-2.491z" /></svg>
                    </div>
                    <div className="flex-1">
                        <div className="flex items-start justify-between gap-3">
                            <div>
                                <h3 className="text-lg font-bold text-[var(--vora-text-primary)]">Last.fm</h3>
                                <p className="text-sm text-[var(--vora-text-muted)] mt-0.5">
                                    {lastFmUsername
                                        ? <>Connected as <span className="text-[var(--vora-accent-text)] font-bold">{lastFmUsername}</span> — plays from this profile will be scrobbled.</>
                                        : 'Scrobble plays from this profile to your Last.fm account and update Now Playing while you listen.'}
                                </p>
                            </div>
                            {lastFmUsername ? (
                                <button
                                    type="button"
                                    onClick={handleDisconnect}
                                    disabled={busy}
                                    className="text-sm px-3 py-1.5 bg-[var(--vora-bg-surface)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-secondary)] hover:text-[var(--vora-danger-text)] rounded font-bold transition-colors cursor-pointer disabled:opacity-50 shrink-0"
                                >
                                    Disconnect
                                </button>
                            ) : pendingToken ? null : (
                                <button
                                    type="button"
                                    onClick={handleConnect}
                                    disabled={busy}
                                    className="text-sm px-4 py-1.5 bg-red-600 hover:bg-red-500 text-[var(--vora-text-primary)] rounded font-bold transition-colors cursor-pointer disabled:opacity-50 shrink-0"
                                >
                                    {busy ? 'Working...' : 'Connect'}
                                </button>
                            )}
                        </div>

                        {pendingToken && !lastFmUsername && (
                            <div className="mt-4 border-t border-[var(--vora-border-subtle)] pt-4">
                                <p className="text-sm text-[var(--vora-text-secondary)] mb-2">
                                    {authWindowOpened
                                        ? 'A new tab opened to Last.fm. Click Allow there, then come back and finish.'
                                        : 'Awaiting authorization.'}
                                </p>
                                <div className="flex gap-2">
                                    <button
                                        type="button"
                                        onClick={handleComplete}
                                        disabled={busy}
                                        className="text-sm px-4 py-1.5 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-active)] text-[var(--vora-text-primary)] rounded font-bold transition-colors cursor-pointer disabled:opacity-50"
                                    >
                                        {busy ? 'Finishing...' : 'I clicked Allow — finish'}
                                    </button>
                                    <button
                                        type="button"
                                        onClick={() => { setPendingToken(null); setAuthWindowOpened(false); setErrorMsg(null); }}
                                        disabled={busy}
                                        className="text-sm px-3 py-1.5 bg-[var(--vora-bg-surface)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)] rounded font-bold transition-colors cursor-pointer disabled:opacity-50"
                                    >
                                        Cancel
                                    </button>
                                </div>
                            </div>
                        )}

                        {errorMsg && (
                            <div className="mt-3 text-sm text-[var(--vora-danger-text)] bg-[var(--vora-danger-soft)] border border-[var(--vora-danger-500)] rounded p-2.5">{errorMsg}</div>
                        )}
                    </div>
                </div>
            </div>
            <p className="text-xs text-[var(--vora-text-disabled)]">
                Need a key? The server admin sets the Last.fm API key + secret once in the plugin settings page; each profile authorizes their own Last.fm account here.
            </p>
        </div>
    );
}
