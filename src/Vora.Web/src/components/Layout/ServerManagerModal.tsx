import React, { useState, useEffect } from 'react';
import { isAxiosError } from 'axios';
import { serverVault, type VoraServer } from '../../utils/serverVault';
import { Modal } from '../Common/Modal';
import { authService } from '../../api/Auth/authService';
import { useDialog } from '../../dialogs';

import { profileService, type UserProfileVM } from '../../api/Users/profileService';
import { type UserVM, userService } from '../../api/Users/userService';
interface Props {
    isOpen: boolean;
    onClose: () => void;
    onServerAdded: () => void;
}

export default function ServerManagerModal({
    isOpen, onClose, onServerAdded }: Props) {
    const dialog = useDialog();
    const [mode, setMode] = useState<'list' | 'add' | 'profiles' | 'pin'>('list');
    const [servers, setServers] = useState<VoraServer[]>([]);

    const [protocol, setProtocol] = useState('http://');
    const [url, setUrl] = useState('');
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [error, setError] = useState('');
    const [isLoading, setIsLoading] = useState(false);

    const [tempAccountData, setTempAccountData] = useState<{ url: string, accountToken: string, userId: string, serverId?: string } | null>(null);
    const [userAccount, setUserAccount] = useState<UserVM | null>(null);
    const [selectedProfile, setSelectedProfile] = useState<UserProfileVM | null>(null);
    const [pin, setPin] = useState('');
    const [pinError, setPinError] = useState(false);

    const [profileNames, setProfileNames] = useState<Record<string, string>>({});

    useEffect(() => {
        if (isOpen) {
            const s = serverVault.getServers();
            setServers(s);
            setMode('list');
            resetForms();

            const fetchProfileNames = async () => {
                const fetchedNames: Record<string, string> = {};

                const fetchPromises = s.map(async (server) => {
                    try {
                        const decoded = JSON.parse(atob(server.token.split('.')[1]));
                        const userId = decoded.accountId || decoded.UserId || decoded.userId || decoded.nameid || localStorage.getItem('user_id');

                        if (userId) {
                            const account = await userService.getUserAccountWithToken(server.url, server.token, userId);
                            const activeProfile = account.profiles.find(p => p.id === server.profileId);
                            if (activeProfile) {
                                fetchedNames[server.id] = activeProfile.name;
                                localStorage.setItem(`profile_name_${server.id}`, activeProfile.name);
                            }
                        }
                    } catch (e) {
                        console.error("Failed to fetch profile name for server", server.name, e);
                    }
                });

                await Promise.all(fetchPromises);
                setProfileNames(prev => ({ ...prev, ...fetchedNames }));
            };

            fetchProfileNames();
        }
    }, [isOpen]);

    const resetForms = () => {
        setProtocol('http://'); setUrl(''); setEmail(''); setPassword(''); setError('');
        setTempAccountData(null); setUserAccount(null); setSelectedProfile(null); setPin('');
    };

    const handleRemoveServer = async (serverId: string) => {
        if (!await dialog.confirm("Are you sure you want to disconnect this server from your client?")) return;
        serverVault.removeServer(serverId);
        setServers(serverVault.getServers());
        onServerAdded();
    };

    const handleSwitchProfileForServer = async (server: VoraServer) => {
        setIsLoading(true);
        setError('');
        try {
            const decoded = JSON.parse(atob(server.token.split('.')[1]));
            const userId = decoded.accountId || decoded.UserId || decoded.userId || decoded.nameid || localStorage.getItem('user_id');

            if (!userId) throw new Error("Could not determine User ID for this server.");

            const account = await userService.getUserAccountWithToken(server.url, server.token, userId);
            setTempAccountData({ url: server.url, accountToken: server.token, userId: userId, serverId: server.id });
            setUserAccount(account);
            setMode('profiles');
        } catch (err) {
            console.error(err);
            await dialog.alert("Failed to load profiles for this server.");
        } finally {
            setIsLoading(false);
        }
    };

    const handleConnect = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        setError('');
        setIsLoading(true);

        try {
            let cleanUrl = url.trim().replace(/^https?:\/\//i, '');
            if (cleanUrl.endsWith('/')) cleanUrl = cleanUrl.slice(0, -1);
            if (cleanUrl.endsWith('/api')) cleanUrl = cleanUrl.slice(0, -4);

            const fullUrl = `${protocol}${cleanUrl}`;

            const auth = await authService.loginToServer(fullUrl, email, password);
            const rawToken = auth.accessToken;
            const rawUserId = auth.userId;

            if (!rawToken || !rawUserId) throw new Error("Unrecognized token format.");

            const account = await userService.getUserAccountWithToken(fullUrl, rawToken, rawUserId);

            setTempAccountData({ url: fullUrl, accountToken: rawToken, userId: rawUserId });
            setUserAccount(account);
            setMode('profiles');
        } catch (err) {
            if (isAxiosError(err)) {
                setError(err.response?.data?.message || err.response?.data || err.message || "Failed to connect to server.");
            } else if (err instanceof Error) {
                setError(err.message);
            } else {
                setError("An unexpected error occurred.");
            }
        } finally {
            setIsLoading(false);
        }
    };

    const handleProfileSelect = async (profile: UserProfileVM) => {
        setSelectedProfile(profile);
        if (profile.hasPin) {
            setPin('');
            setPinError(false);
            setMode('pin');
            return;
        }
        await finalizeConnection(profile.id);
    };

    const handlePinSubmit = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        if (!selectedProfile || !tempAccountData) return;
        setIsLoading(true);
        try {
            await profileService.validatePinWithToken(tempAccountData.url, tempAccountData.accountToken, selectedProfile.id, pin);
            await finalizeConnection(selectedProfile.id);
        } catch {
            setPinError(true);
            setPin('');
        } finally {
            setIsLoading(false);
        }
    };

    const finalizeConnection = async (profileId: string) => {
        if (!tempAccountData || !userAccount) return;
        setIsLoading(true);
        try {
            const profileToken = await authService.exchangeProfileTokenWithToken(
                tempAccountData.url,
                tempAccountData.accountToken,
                tempAccountData.userId,
                profileId
            );

            const decodedToken = JSON.parse(atob(profileToken.split('.')[1]));
            const targetServerId = tempAccountData.serverId || `server_${Date.now()}`;
            const pName = userAccount.profiles.find(p => p.id === profileId)?.name || 'User';

            let serverName = new URL(tempAccountData.url).hostname;
            try {
                const status = await authService.probeServer(tempAccountData.url);
                if (status.serverName) serverName = status.serverName;
            } catch (err) {
                console.debug("Could not fetch server name for vault", err);
            }

            serverVault.addOrUpdateServer({
                id: targetServerId,
                name: serverName,
                url: tempAccountData.url,
                token: profileToken,
                profileId: decodedToken.sub,
                isAdmin: userAccount.isAdmin
            });

            if (serverVault.getActiveServerId() === targetServerId) {
                localStorage.setItem('profile_name', pName);
                localStorage.setItem('profile_token', profileToken);
                localStorage.setItem('is_server_admin', userAccount.isAdmin ? 'true' : 'false');
            }

            setProfileNames(prev => ({ ...prev, [targetServerId]: pName }));
            localStorage.setItem(`profile_name_${targetServerId}`, pName);

            setServers(serverVault.getServers());
            onServerAdded();
            setMode('list');
        } catch (error) {
            console.error("Finalize connection error:", error);
            await dialog.alert("Failed to finalize profile connection.");
        } finally {
            setIsLoading(false);
        }
    };

    return (
        <Modal
            isOpen={isOpen}
            onClose={onClose}
            size="md"
            zIndex="z-[200]"
            surface="gray-900"
            cardClassName="overflow-hidden flex flex-col max-h-[90vh]"
        >

                <div className="p-5 border-b border-[var(--vora-border-subtle)] flex justify-between items-center bg-[var(--vora-bg-canvas)] shrink-0">
                    <h2 className="text-xl font-bold text-[var(--vora-text-primary)]">
                        {mode === 'list' ? 'Manage Servers' : mode === 'add' ? 'Connect New Server' : mode === 'profiles' ? 'Select Profile' : 'Enter PIN'}
                    </h2>
                    <button onClick={onClose} className="text-[var(--vora-text-disabled)] hover:text-[var(--vora-text-primary)] text-2xl leading-none">&times;</button>
                </div>

                <div className="p-6 overflow-y-auto custom-scrollbar flex-1">
                    {mode === 'list' && (
                        <div className="space-y-4">
                            {servers.map(server => {
                                let pName = profileNames[server.id] || localStorage.getItem(`profile_name_${server.id}`) || 'Unknown Profile';

                                if (pName === 'Unknown Profile' && server.id === serverVault.getActiveServerId() && localStorage.getItem('profile_name')) {
                                    pName = localStorage.getItem('profile_name')!;
                                }

                                return (
                                    <div key={server.id} className="bg-[var(--vora-bg-surface)] border border-[var(--vora-border-subtle)] rounded-lg p-4 flex flex-col md:flex-row items-start md:items-center justify-between group shadow gap-4">
                                        <div className="overflow-hidden flex-1">
                                            <h3 className="font-bold text-[var(--vora-text-primary)] truncate flex items-center gap-2">
                                                <svg className="w-4 h-4 text-[var(--vora-success-text)] shrink-0" fill="currentColor" viewBox="0 0 20 20"><path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clipRule="evenodd" /></svg>
                                                {server.name}
                                            </h3>
                                            <p className="text-xs text-[var(--vora-text-disabled)] font-mono mt-1 truncate">{server.url}</p>
                                            <p className="text-xs text-[var(--vora-accent-text)] font-medium mt-1">Logged in as: {pName}</p>
                                        </div>

                                        <div className="flex gap-2 shrink-0">
                                            <button
                                                onClick={() => handleSwitchProfileForServer(server)}
                                                className="text-[var(--vora-accent-text)] hover:text-[var(--vora-accent-active)] px-3 py-2 bg-[var(--vora-bg-sunken)] rounded transition-colors flex items-center gap-1 text-xs font-bold uppercase tracking-wider cursor-pointer"
                                                title="Switch Profile"
                                            >
                                                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" /></svg>
                                                Switch Profile
                                            </button>
                                            <button
                                                onClick={() => handleRemoveServer(server.id)}
                                                className="text-[var(--vora-text-disabled)] hover:text-[var(--vora-danger-text)] p-2 bg-[var(--vora-bg-sunken)] rounded transition-colors cursor-pointer"
                                                title="Disconnect Server"
                                            >
                                                <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" /></svg>
                                            </button>
                                        </div>
                                    </div>
                                );
                            })}

                            <button
                                onClick={() => { resetForms(); setMode('add'); }}
                                className="w-full py-3 bg-[var(--vora-bg-canvas)] hover:bg-[var(--vora-bg-surface)] text-[var(--vora-text-secondary)] font-bold rounded shadow-sm transition-colors border border-[var(--vora-border-subtle)] border-dashed flex justify-center items-center gap-2 cursor-pointer"
                            >
                                <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" /></svg>
                                Add Content Source
                            </button>
                        </div>
                    )}

                    {mode === 'add' && (
                        <form onSubmit={handleConnect} className="space-y-4">
                            <div>
                                <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">Server URL</label>
                                <div className="flex">
                                    <select
                                        value={protocol}
                                        onChange={e => setProtocol(e.target.value)}
                                        className="bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] border-r-0 rounded-l-md p-3 text-[var(--vora-text-primary)] outline-none focus:border-[var(--vora-accent-500)] font-mono text-sm cursor-pointer"
                                    >
                                        <option value="http://">http://</option>
                                        <option value="https://">https://</option>
                                    </select>
                                    <input
                                        type="text"
                                        required
                                        value={url}
                                        onChange={e => setUrl(e.target.value)}
                                        placeholder="192.168.1.50:5000"
                                        className="w-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-r-md p-3 text-[var(--vora-text-primary)] outline-none focus:border-[var(--vora-accent-500)] font-mono text-sm"
                                    />
                                </div>
                            </div>
                            <div>
                                <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">Email</label>
                                <input type="email" required value={email} onChange={e => setEmail(e.target.value)} className="w-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-3 text-[var(--vora-text-primary)] outline-none focus:border-[var(--vora-accent-500)]" />
                            </div>
                            <div>
                                <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">Password</label>
                                <input type="password" required value={password} onChange={e => setPassword(e.target.value)} className="w-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-3 text-[var(--vora-text-primary)] outline-none focus:border-[var(--vora-accent-500)]" />
                            </div>

                            {error && <div className="text-[var(--vora-danger-text)] text-xs font-medium bg-[var(--vora-danger-soft)] border border-[var(--vora-danger-500)] p-3 rounded">{error}</div>}

                            <div className="flex gap-3 pt-4">
                                <button type="button" onClick={() => setMode('list')} className="flex-1 py-2.5 bg-[var(--vora-bg-surface)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-primary)] font-bold rounded transition-colors cursor-pointer">Cancel</button>
                                <button type="submit" disabled={isLoading} className="flex-[2] py-2.5 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-active)] disabled:opacity-50 text-[var(--vora-text-primary)] font-bold rounded transition-colors cursor-pointer">
                                    {isLoading ? 'Connecting...' : 'Connect'}
                                </button>
                            </div>
                        </form>
                    )}

                    {mode === 'profiles' && userAccount && (
                        <div className="flex flex-wrap justify-center gap-6">
                            {userAccount.profiles.map(profile => (
                                <div key={profile.id} onClick={() => handleProfileSelect(profile)} className="flex flex-col items-center group cursor-pointer w-24">
                                    <div className="w-20 h-20 rounded-full overflow-hidden bg-[var(--vora-bg-surface)] border-2 border-transparent group-hover:border-[var(--vora-accent-500)] transition-all shadow-xl relative">
                                        {profile.profileImageUrl ? (
                                            <img src={profile.profileImageUrl} alt={profile.name} className="w-full h-full object-cover" />
                                        ) : (
                                            <div className="w-full h-full flex items-center justify-center text-2xl font-bold text-[var(--vora-text-disabled)] bg-[var(--vora-bg-surface)]">
                                                {profile.name.charAt(0).toUpperCase()}
                                            </div>
                                        )}
                                        {profile.hasPin && (
                                            <div className="absolute bottom-0 right-0 bg-black/80 p-1 rounded-full border border-[var(--vora-border-subtle)]">
                                                <svg className="w-3 h-3 text-[var(--vora-text-secondary)]" fill="currentColor" viewBox="0 0 20 20"><path fillRule="evenodd" d="M5 9V7a5 5 0 0110 0v2a2 2 0 012 2v5a2 2 0 01-2 2H5a2 2 0 01-2-2v-5a2 2 0 012-2zm8-2v2H7V7a3 3 0 016 0z" clipRule="evenodd" /></svg>
                                            </div>
                                        )}
                                    </div>
                                    <span className="mt-2 text-[var(--vora-text-muted)] group-hover:text-[var(--vora-text-primary)] font-medium text-sm transition-colors text-center line-clamp-1">{profile.name}</span>
                                </div>
                            ))}
                            {tempAccountData?.serverId && (
                                <div className="w-full mt-4 text-center">
                                    <button onClick={() => setMode('list')} className="text-[var(--vora-text-disabled)] hover:text-[var(--vora-text-primary)] text-sm font-medium transition-colors cursor-pointer">Cancel Profile Switch</button>
                                </div>
                            )}
                        </div>
                    )}

                    {mode === 'pin' && selectedProfile && (
                        <div className="text-center">
                            <p className="text-[var(--vora-text-muted)] text-sm mb-6">Enter the PIN for {selectedProfile.name}</p>
                            <form onSubmit={handlePinSubmit}>
                                <input
                                    autoFocus type="password" pattern="[0-9]*" inputMode="numeric" maxLength={4}
                                    value={pin} onChange={e => setPin(e.target.value)}
                                    disabled={isLoading}
                                    className={`w-full bg-[var(--vora-bg-canvas)] border ${pinError ? 'border-[var(--vora-danger-500)]' : 'border-[var(--vora-border-subtle)]'} rounded-lg p-4 text-center text-2xl tracking-widest text-[var(--vora-text-primary)] outline-none focus:border-[var(--vora-accent-500)]`}
                                    placeholder="••••"
                                />
                                {pinError && <p className="text-[var(--vora-danger-text)] text-xs mt-2 font-medium">Incorrect PIN.</p>}
                                <div className="flex gap-3 mt-8">
                                    <button type="button" disabled={isLoading} onClick={() => setMode('profiles')} className="flex-1 px-4 py-2 bg-[var(--vora-bg-surface)] text-[var(--vora-text-secondary)] hover:bg-[var(--vora-bg-raised)] hover:text-[var(--vora-text-primary)] rounded font-medium transition-colors cursor-pointer">Cancel</button>
                                    <button type="submit" disabled={isLoading} className="flex-1 bg-white text-black hover:bg-gray-200 font-bold py-2 px-4 rounded transition-colors disabled:opacity-50 cursor-pointer">
                                        {isLoading ? 'Verifying...' : 'Enter'}
                                    </button>
                                </div>
                            </form>
                        </div>
                    )}
                </div>
        </Modal>
    );
}
