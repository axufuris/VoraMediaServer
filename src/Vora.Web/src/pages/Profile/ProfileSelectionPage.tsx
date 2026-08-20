import { useEffect, useState, useCallback } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { authService } from '../../api/Auth/authService';
import { serverVault } from '../../utils/serverVault';
import { StorageKeys, SessionKeys, decodeJwtPayload, getProfileIdFromToken } from '../../utils/storageKeys';
import { defaultApiBaseUrl } from '../../utils/apiBase';
import { useDialog } from '../../dialogs';

import { profileService, type UserProfileVM } from '../../api/Users/profileService';
import { type UserVM, userService } from '../../api/Users/userService';
export default function ProfileSelectionPage() {
    const dialog = useDialog();
    const navigate = useNavigate();
    const location = useLocation();
    const [user, setUser] = useState<UserVM | null>(null);
    const [loading, setLoading] = useState(true);

    const [selectedProfile, setSelectedProfile] = useState<UserProfileVM | null>(null);
    const [pin, setPin] = useState('');
    const [pinError, setPinError] = useState(false);

    const [isCreateOpen, setIsCreateOpen] = useState(false);
    const [newName, setNewName] = useState('');
    const [newPin, setNewPin] = useState('');

    const [autoLogin, setAutoLogin] = useState(localStorage.getItem(StorageKeys.autoLoginProfileId) !== null);

    const isManualSwitch = location.state?.manualSwitch === true;

    const authenticateProfile = useCallback(async (profileId: string, enteredPin: string | null, isAutoLoginFlag = false, prefetchedUser: UserVM | null = null) => {
        try {
            if (enteredPin) {
                await profileService.validatePin(profileId, enteredPin);
            }

            const userId = sessionStorage.getItem('pending_user_id') || localStorage.getItem(StorageKeys.userId)!;
            const profileToken = await authService.exchangeProfileToken(userId, profileId);

            let fullUser = prefetchedUser;
            if (!fullUser) fullUser = await userService.getUserAccount(userId);

            const activeProfile = fullUser.profiles.find(p => p.id === profileId);

            localStorage.setItem(StorageKeys.userId, userId);
            localStorage.setItem(StorageKeys.profileToken, profileToken);
            localStorage.setItem(StorageKeys.profileName, activeProfile?.name || 'User');
            localStorage.setItem(StorageKeys.isServerAdmin, fullUser.isAdmin ? 'true' : 'false');
            localStorage.setItem(StorageKeys.isProfileAdmin, activeProfile?.isAdmin ? 'true' : 'false');

            const activeServer = serverVault.getActiveServer();
            if (activeServer) {
                serverVault.addOrUpdateServer({
                    ...activeServer,
                    token: profileToken,
                    profileId,
                    isAdmin: fullUser.isAdmin
                });
            }

            if (autoLogin && !isAutoLoginFlag) {
                localStorage.setItem(StorageKeys.autoLoginProfileId, profileId);
            } else if (!autoLogin && !isAutoLoginFlag) {
                localStorage.removeItem(StorageKeys.autoLoginProfileId);
            }

            const pendingUrl = sessionStorage.getItem(SessionKeys.pendingServerUrl);
            const pendingServerName = sessionStorage.getItem('pending_server_name') || 'Vora Server'; // <-- Pull the real name

            if (pendingUrl) {
                const decodedToken = decodeJwtPayload(profileToken);
                const tokenProfileId = typeof decodedToken?.sub === 'string' ? decodedToken.sub : null;
                if (!tokenProfileId) throw new Error('Profile token missing sub claim');

                // Reuse the existing vault entry for this URL if one exists. Otherwise we'd
                // accumulate a new server_<timestamp> row on every login, which then renders
                // each library multiple times in the nav.
                const existing = serverVault.getServers().find(s => s.url === pendingUrl);
                const newServerId = existing?.id ?? `server_${Date.now()}`;

                serverVault.addOrUpdateServer({
                    id: newServerId,
                    name: pendingServerName, // <-- USE IT HERE INSTEAD OF THE HARDCODED STRING!
                    url: pendingUrl,
                    token: profileToken,
                    profileId: tokenProfileId,
                    isAdmin: fullUser.isAdmin
                });

                serverVault.setActiveServerId(newServerId);

                sessionStorage.removeItem(SessionKeys.pendingServerUrl);
                sessionStorage.removeItem('pending_server_name'); // Cleanup
                sessionStorage.removeItem(SessionKeys.pendingUserToken);
                sessionStorage.removeItem('pending_user_id');
            } else if (serverVault.getServers().length === 0) {
                const newServerId = 'legacy_server';
                const localUrl = defaultApiBaseUrl().replace('/api', '');

                let sName = 'Vora Server';
                try {
                    const status = await authService.probeServer(localUrl);
                    if (status.serverName) sName = status.serverName;
                } catch (err) {
                    console.debug("Could not fetch server name during auto-login bypass", err);
                }

                const legacyProfileId = getProfileIdFromToken(profileToken);
                if (!legacyProfileId) throw new Error('Profile token missing sub claim');
                serverVault.addOrUpdateServer({
                    id: newServerId,
                    name: sName, // <-- USE IT HERE
                    url: localUrl,
                    token: profileToken,
                    profileId: legacyProfileId,
                    isAdmin: fullUser.isAdmin
                });
                serverVault.setActiveServerId(newServerId);
            }

            const isFreshSetup = sessionStorage.getItem(SessionKeys.freshServerSetup) === 'true';
            if (isFreshSetup) {
                sessionStorage.removeItem(SessionKeys.freshServerSetup);
                if (fullUser.isAdmin) {
                    navigate('/admin/settings');
                    return;
                }
            }

            navigate('/');
        } catch {
            setPinError(true);
            setPin('');
        }
    }, [autoLogin, navigate]);

    useEffect(() => {
        let isMounted = true;

        const fetchData = async () => {
            const userId = sessionStorage.getItem('pending_user_id') || localStorage.getItem(StorageKeys.userId);
            if (!userId) {
                navigate('/login');
                return;
            }
            try {
                const userData = await userService.getUserAccount(userId);
                if (!isMounted) return;

                const savedAutoProfileId = localStorage.getItem(StorageKeys.autoLoginProfileId);

                if (savedAutoProfileId && !isManualSwitch) {
                    const autoProfile = userData.profiles.find(p => p.id === savedAutoProfileId);
                    if (autoProfile) {
                        if (autoProfile.hasPin) {
                            setUser(userData);
                            setLoading(false);
                            setSelectedProfile(autoProfile);
                            return;
                        } else {
                            await authenticateProfile(autoProfile.id, null, true, userData);
                            return;
                        }
                    } else {
                        localStorage.removeItem(StorageKeys.autoLoginProfileId);
                    }
                } else if (!isManualSwitch && userData.profiles.length === 1 && !userData.profiles[0].hasPin) {
                    await authenticateProfile(userData.profiles[0].id, null, true, userData);
                    return;
                }

                setUser(userData);
                setLoading(false);
            } catch {
                if (!isMounted) return;
                sessionStorage.removeItem(SessionKeys.pendingServerUrl);
                sessionStorage.removeItem(SessionKeys.pendingUserToken);
                sessionStorage.removeItem('pending_user_id');
                navigate('/login');
            }
        };

        void fetchData();

        return () => {
            isMounted = false;
        };
    }, [isManualSwitch, navigate, authenticateProfile]);

    const handleProfileClick = async (profile: UserProfileVM) => {
        if (profile.hasPin) {
            setSelectedProfile(profile);
            setPin('');
            setPinError(false);
            return;
        }
        await authenticateProfile(profile.id, null);
    };

    const handlePinSubmit = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        if (!selectedProfile) return;
        await authenticateProfile(selectedProfile.id, pin);
    };

    const handleCreateProfile = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        const userId = sessionStorage.getItem('pending_user_id') || localStorage.getItem(StorageKeys.userId);
        if (!userId || !newName.trim()) return;

        try {
            await profileService.createProfile(userId, newName, undefined, newPin || undefined, [], [], [], true);
            setIsCreateOpen(false);
            setNewName('');
            setNewPin('');
            window.location.reload();
        } catch {
            await dialog.alert("Failed to create profile");
        }
    };

    if (loading) {
        return (
            <div
                data-vora-client=""
                className="min-h-screen flex items-center justify-center"
                style={{ background: 'var(--vora-bg-canvas)' }}
            >
                <div className="text-xl" style={{ color: 'var(--vora-text-muted)' }}>Loading profiles…</div>
            </div>
        );
    }

    return (
        <div
            data-vora-client=""
            className="relative min-h-screen flex flex-col items-center justify-center p-4 overflow-hidden"
            style={{ background: 'var(--vora-bg-canvas)', color: 'var(--vora-text-primary)' }}
        >
            <div
                aria-hidden="true"
                className="pointer-events-none absolute inset-0"
                style={{
                    background: 'radial-gradient(ellipse at top, color-mix(in srgb, var(--vora-accent-500) 12%, transparent) 0%, transparent 55%)',
                }}
            />

            <h1
                className="relative text-4xl font-bold mb-12 tracking-wide"
                style={{ color: 'var(--vora-text-primary)' }}
            >
                Who's watching?
            </h1>

            <div className="relative flex flex-wrap justify-center gap-8 max-w-4xl">
                {user?.profiles.map(profile => (
                    <div key={profile.id} onClick={() => handleProfileClick(profile)} className="flex flex-col items-center group cursor-pointer w-32">
                        <div
                            className="w-32 h-32 rounded-xl overflow-hidden transition-all relative"
                            style={{
                                background: 'var(--vora-bg-surface)',
                                border: '3px solid transparent',
                                boxShadow: 'var(--vora-shadow-overlay)',
                            }}
                            onMouseEnter={e => {
                                e.currentTarget.style.borderColor = 'var(--vora-accent-500)';
                                e.currentTarget.style.transform = 'translateY(-3px)';
                            }}
                            onMouseLeave={e => {
                                e.currentTarget.style.borderColor = 'transparent';
                                e.currentTarget.style.transform = 'translateY(0)';
                            }}
                        >
                            {profile.profileImageUrl ? (
                                <img src={profile.profileImageUrl} alt={profile.name} className="w-full h-full object-cover" />
                            ) : (
                                <div
                                    className="w-full h-full flex items-center justify-center text-4xl font-bold"
                                    style={{ color: 'var(--vora-text-muted)', background: 'var(--vora-bg-surface)' }}
                                >
                                    {profile.name.charAt(0).toUpperCase()}
                                </div>
                            )}
                            {profile.hasPin && (
                                <div
                                    className="absolute bottom-2 right-2 p-1.5 rounded-full"
                                    style={{ background: 'rgba(0,0,0,0.75)', border: '1px solid var(--vora-border-subtle)' }}
                                >
                                    <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 20 20" style={{ color: 'var(--vora-text-secondary)' }}>
                                        <path fillRule="evenodd" d="M5 9V7a5 5 0 0110 0v2a2 2 0 012 2v5a2 2 0 01-2 2H5a2 2 0 01-2-2v-5a2 2 0 012-2zm8-2v2H7V7a3 3 0 016 0z" clipRule="evenodd" />
                                    </svg>
                                </div>
                            )}
                        </div>
                        <span
                            className="mt-4 font-medium text-lg transition-colors group-hover:text-[var(--vora-text-primary)]"
                            style={{ color: 'var(--vora-text-muted)' }}
                        >
                            {profile.name}
                        </span>
                    </div>
                ))}

                <div onClick={() => setIsCreateOpen(true)} className="flex flex-col items-center group cursor-pointer w-32">
                    <div
                        className="w-32 h-32 rounded-xl flex items-center justify-center transition-all"
                        style={{
                            border: '3px dashed var(--vora-border-subtle)',
                            background: 'color-mix(in srgb, var(--vora-bg-surface) 50%, transparent)',
                        }}
                        onMouseEnter={e => { e.currentTarget.style.borderColor = 'var(--vora-accent-500)'; }}
                        onMouseLeave={e => { e.currentTarget.style.borderColor = 'var(--vora-border-subtle)'; }}
                    >
                        <svg className="w-12 h-12" fill="none" stroke="currentColor" viewBox="0 0 24 24" style={{ color: 'var(--vora-text-disabled)' }}>
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
                        </svg>
                    </div>
                    <span className="mt-4 font-medium text-lg transition-colors group-hover:text-[var(--vora-text-muted)]" style={{ color: 'var(--vora-text-disabled)' }}>Add Profile</span>
                </div>
            </div>

            {user && user.profiles.length > 1 && (
                <div
                    className="relative mt-16 flex items-center gap-3 px-5 py-3 rounded-full"
                    style={{
                        background: 'color-mix(in srgb, var(--vora-bg-surface) 50%, transparent)',
                        border: '1px solid var(--vora-border-subtle)',
                        color: 'var(--vora-text-muted)',
                    }}
                >
                    <input
                        type="checkbox"
                        id="autoLogin"
                        checked={autoLogin}
                        onChange={(e) => setAutoLogin(e.target.checked)}
                        className="w-4 h-4 rounded cursor-pointer"
                        style={{ accentColor: 'var(--vora-accent-500)' }}
                    />
                    <label htmlFor="autoLogin" className="cursor-pointer select-none font-medium text-sm">
                        Sign in automatically on this device
                    </label>
                </div>
            )}

            {isCreateOpen && (
                <div
                    className="fixed inset-0 flex items-center justify-center z-[200] p-4"
                    style={{ background: 'rgba(0,0,0,0.85)', backdropFilter: 'blur(8px)' }}
                >
                    <div
                        className="rounded-xl p-8 max-w-sm w-full"
                        style={{
                            background: 'var(--vora-bg-raised)',
                            border: '1px solid var(--vora-border-strong)',
                            boxShadow: 'var(--vora-shadow-overlay)',
                        }}
                    >
                        <h2 className="text-2xl font-bold mb-6" style={{ color: 'var(--vora-text-primary)' }}>Create Profile</h2>
                        <form onSubmit={handleCreateProfile} className="space-y-4">
                            <div>
                                <label className="block text-sm font-medium mb-1" style={{ color: 'var(--vora-text-muted)' }}>Name</label>
                                <input autoFocus required type="text" value={newName} onChange={e => setNewName(e.target.value)} className="vora-input w-full" placeholder="e.g. Kids" />
                            </div>
                            <div>
                                <label className="block text-sm font-medium mb-1" style={{ color: 'var(--vora-text-muted)' }}>PIN (Optional)</label>
                                <input type="password" maxLength={4} inputMode="numeric" pattern="[0-9]*" value={newPin} onChange={e => setNewPin(e.target.value.replace(/[^0-9]/g, ''))} className="vora-input w-full tracking-widest" placeholder="••••" />
                            </div>
                            <div className="flex gap-3 mt-8">
                                <button type="button" onClick={() => setIsCreateOpen(false)} className="vora-button-secondary flex-1 cursor-pointer">Cancel</button>
                                <button type="submit" className="vora-button-primary flex-1 cursor-pointer">Create</button>
                            </div>
                        </form>
                    </div>
                </div>
            )}

            {selectedProfile && (
                <div
                    className="fixed inset-0 flex items-center justify-center z-[200] p-4"
                    style={{ background: 'rgba(0,0,0,0.85)', backdropFilter: 'blur(8px)' }}
                >
                    <div
                        className="rounded-xl p-8 max-w-sm w-full text-center"
                        style={{
                            background: 'var(--vora-bg-raised)',
                            border: '1px solid var(--vora-border-strong)',
                            boxShadow: 'var(--vora-shadow-overlay)',
                        }}
                    >
                        <h2 className="text-xl font-bold mb-2" style={{ color: 'var(--vora-text-primary)' }}>Profile Lock</h2>
                        <p className="text-sm mb-6" style={{ color: 'var(--vora-text-muted)' }}>Enter the PIN for {selectedProfile.name}</p>

                        <form onSubmit={handlePinSubmit}>
                            <input
                                autoFocus type="password" pattern="[0-9]*" inputMode="numeric" maxLength={4}
                                value={pin} onChange={e => setPin(e.target.value)}
                                className="vora-input w-full text-center text-2xl tracking-widest"
                                style={pinError ? { borderColor: 'var(--vora-danger-500)' } : undefined}
                                placeholder="••••"
                            />
                            {pinError && <p className="text-xs mt-2 font-medium" style={{ color: 'var(--vora-danger-text)' }}>Incorrect PIN. Please try again.</p>}
                            <div className="flex gap-3 mt-8">
                                <button type="button" onClick={() => setSelectedProfile(null)} className="vora-button-secondary flex-1 cursor-pointer">Cancel</button>
                                <button type="submit" className="vora-button-primary flex-1 cursor-pointer">Enter</button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    );
}
