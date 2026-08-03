import { useEffect, useState, useCallback, useRef } from 'react';
import { Outlet, useNavigate, useParams } from 'react-router-dom';
import { libraryService } from '../api/Media/libraryService';
import { authService } from '../api/Auth/authService';
import { useSignalREvent, disconnectSignalR } from '../hooks/useSignalREvent';
import GlobalVideoPlayer from '../components/Player/GlobalVideoPlayer';
import { scanDeviceCapabilities } from '../utils/hardwareScanner';
import { deviceService } from '../api/Users/deviceService';
import { usePlayer } from '../contexts/usePlayer';
import { serverVault } from '../utils/serverVault';
import ServerManagerModal from '../components/Layout/ServerManagerModal';
import LiveTvPlayer from '../components/Player/LiveTvPlayer';
import LiveRadioPlayer from '../components/Player/LiveRadioPlayer';
import NowPlayingFullscreen from '../components/Player/NowPlayingFullscreen';
import { useDialog } from '../dialogs';

import { profileDeviceSettingsService } from '../api/Users/profileDeviceSettingsService';
import MainLayoutSidebar from './parts/MainLayoutSidebar';
import MainLayoutUserMenu from './parts/MainLayoutUserMenu';
import { useFeatureFlags } from '../hooks/useFeatureFlags';
import type { FeatureFlagsVM } from '../api/System/featureFlagsService';
import { youtubeService } from '../api/YouTube/youtubeService';

const isNavItemEnabled = (item: NavItem, flags: FeatureFlagsVM, youtubeAvailable: boolean): boolean => {
    if (item.type !== 'system') return true;
    switch (item.id) {
        case 'discovery': return flags.discover || flags.forYou || flags.releaseCalendar;
        case 'podcasts': return flags.podcasts;
        case 'radio': return flags.internetRadio;
        case 'livetv': return flags.liveTv;
        case 'youtube': return youtubeAvailable;
        default: return true;
    }
};

export interface NavItem {
    id: string;
    serverId?: string;
    serverName?: string;
    title: string;
    path: string;
    type: 'system' | 'library' | 'plugin';
    mediaType?: string;
    isPinned: boolean;
    order: number;
}

interface ParsedSchedule {
    dayOfWeek: number;
    startTime: string;
    endTime: string;
}

const getInitialBlockedState = (): boolean => {
    const token = localStorage.getItem('profile_token');
    if (!token) return false;

    try {
        const payload = JSON.parse(atob(token.split('.')[1]));
        const schedules = JSON.parse(payload.accessSchedules || "[]");

        if (!schedules || schedules.length === 0) return false;

        const now = new Date();
        const currentDay = now.getDay();
        const currentMinutes = now.getHours() * 60 + now.getMinutes();

        const todaySchedules = schedules.filter((s: ParsedSchedule) => s.dayOfWeek === currentDay);
        if (todaySchedules.length === 0) return true;

        const isWithin = todaySchedules.some((s: ParsedSchedule) => {
            const [sh, sm] = s.startTime.split(':').map(Number);
            const [eh, em] = s.endTime.split(':').map(Number);
            const startMins = sh * 60 + sm;
            const endMins = eh * 60 + em;
            return currentMinutes >= startMins && currentMinutes < endMins;
        });

        return !isWithin;
    } catch {
        return false;
    }
};

export default function MainLayout() {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const navigate = useNavigate();

    const [isProfileMenuOpen, setIsProfileMenuOpen] = useState(false);
    const isServerAdmin = localStorage.getItem('is_server_admin') === 'true';
    const profileName = localStorage.getItem('profile_name') || 'User';
    const currentUserId = localStorage.getItem('user_id');
    const profileToken = localStorage.getItem('profile_token');
    const activeProfileId = profileToken ? JSON.parse(atob(profileToken.split('.')[1])).sub : '';

    const [isBlockedBySchedule, setIsBlockedBySchedule] = useState<boolean>(getInitialBlockedState);

    const { currentMedia, isMinimized } = usePlayer();

    const [navItems, setNavItems] = useState<NavItem[]>([]);
    const [isEditingNav, setIsEditingNav] = useState(false);
    const [showUnpinned, setShowUnpinned] = useState(false);

    const [isServerManagerOpen, setIsServerManagerOpen] = useState(false);

    const flags = useFeatureFlags();
    const [youtubeAvailable, setYoutubeAvailable] = useState(false);

    const refreshYouTubeAvailability = useCallback(() => {
        if (!activeProfileId) {
            setYoutubeAvailable(false);
            return;
        }
        youtubeService.getProfileSettings(serverId)
            .then((settings) => setYoutubeAvailable(settings.isAvailable && settings.isEnabled))
            .catch(() => setYoutubeAvailable(false));
    }, [activeProfileId, serverId]);

    const refreshYouTubeAvailabilityRef = useRef(refreshYouTubeAvailability);
    useEffect(() => {
        refreshYouTubeAvailabilityRef.current = refreshYouTubeAvailability;
    });

    useEffect(() => {
        refreshYouTubeAvailabilityRef.current();
    }, [activeProfileId, serverId]);

    useSignalREvent("YouTubeAccessChanged", useCallback((changedUserId: string) => {
        if (currentUserId && changedUserId.toLowerCase() === currentUserId.toLowerCase()) {
            refreshYouTubeAvailabilityRef.current();
        }
    }, [currentUserId]));

    useSignalREvent("UserAccessUpdated", useCallback(async (updatedUserId: string) => {
        if (currentUserId && updatedUserId.toLowerCase() === currentUserId.toLowerCase()) {
            await dialog.alert("Your account permissions have been updated by an administrator. Please re-select your profile to apply the changes.");
            localStorage.removeItem('profile_token');
            navigate('/profiles', { state: { manualSwitch: true } });
        }
    }, [currentUserId, navigate, dialog]));

    useSignalREvent("ProfileAccessUpdated", useCallback(async (updatedProfileId: string) => {
        if (activeProfileId && updatedProfileId.toLowerCase() === activeProfileId.toLowerCase()) {
            await dialog.alert("Your profile permissions have been updated by an administrator. Please re-select your profile to apply the changes.");
            localStorage.removeItem('profile_token');
            navigate('/profiles', { state: { manualSwitch: true } });
        }
    }, [activeProfileId, navigate, dialog]));

    const loadNav = useCallback(async () => {
        // Defensive dedupe: legacy vault entries may include multiple rows pointing at
        // the same URL (one per login session before the dedupe fix). Without this,
        // every duplicate server contributes its libraries again.
        const seenUrls = new Set<string>();
        const connectedServers = serverVault.getServers().filter(s => {
            if (seenUrls.has(s.url)) return false;
            seenUrls.add(s.url);
            return true;
        });
        if (connectedServers.length === 0) return;

        try {
            const serverPromises = connectedServers.map(async (server) => {
                try {
                    const libs = await libraryService.getLibraries(server.id);
                    return libs
                        .filter(lib => lib.type !== 'Music')
                        .map(lib => ({
                            id: lib.id,
                            serverId: server.id,
                            serverName: server.name,
                            title: lib.name,
                            path: `/server/${server.id}/library/${lib.id}`,
                            type: 'library' as const,
                            mediaType: lib.type,
                            isPinned: true,
                            order: 999
                        }));
                } catch (error) {
                    console.error(`Failed to fetch libraries for server: ${server.name}`, error);
                    return [];
                }
            });

            const results = await Promise.all(serverPromises);
            const allLibraries = results.flat();

            const deviceId = localStorage.getItem('device_id') || 'unknown';
            const prefsKey = `nav_prefs_global_${deviceId}`;

            let savedPrefsJson: string | null = null;
            if (activeProfileId && deviceId !== 'unknown') {
                try {
                    savedPrefsJson = await profileDeviceSettingsService.getNavPrefs(activeProfileId, deviceId, serverId);
                } catch (e) {
                    console.error("Failed to fetch nav prefs from DB", e);
                }
            }

            if (!savedPrefsJson) {
                savedPrefsJson = localStorage.getItem(prefsKey);
            } else {
                localStorage.setItem(prefsKey, savedPrefsJson);
            }

            const savedPrefs: NavItem[] | null = savedPrefsJson ? JSON.parse(savedPrefsJson) : null;

            const primaryServerName = connectedServers[0]?.name;

            const baseItems: NavItem[] = [
                { id: 'music', title: 'Music', path: '/music', type: 'system', serverName: primaryServerName, isPinned: true, order: 0 },
                { id: 'podcasts', title: 'Podcasts', path: '/podcasts', type: 'system', serverName: primaryServerName, isPinned: true, order: 1 },
                { id: 'livetv', title: 'Live TV', path: '/livetv', type: 'system', isPinned: true, order: 2 },
                { id: 'radio', title: 'Radio', path: '/radio', type: 'system', isPinned: true, order: 3 },
                { id: 'youtube', title: 'YouTube', path: '/youtube', type: 'system', isPinned: true, order: 4 },
                { id: 'discovery', title: 'Discover', path: '/discovery', type: 'system', isPinned: true, order: 5 },
                { id: 'collections', title: 'Collections', path: '/collections', type: 'system', isPinned: true, order: 6 }
            ];

            const combinedItems = [...baseItems, ...allLibraries];

            if (savedPrefs && Array.isArray(savedPrefs)) {
                const merged = combinedItems.map(item => {
                    const saved = savedPrefs.find(p => p.id === item.id && p.serverId === item.serverId);
                    if (saved) return { ...item, isPinned: saved.isPinned, order: saved.order };
                    return item;
                });

                merged.sort((a, b) => a.order - b.order).forEach((m, i) => m.order = i);
                setNavItems(merged);
            } else {
                combinedItems.forEach((m, i) => m.order = i);
                setNavItems(combinedItems);
            }
        } catch (error) {
            console.error("Critical error loading cross-server navigation", error);
        }
    }, [activeProfileId, serverId]);

    useEffect(() => {
        let isMounted = true;

        const initializeData = async () => {
            if (!activeProfileId) return;

            await loadNav();

            if (profileDeviceSettingsService.getPlaybackPrefs) {
                const deviceId = localStorage.getItem('device_id') || 'unknown';
                const prefsKey = `playback_prefs_${activeProfileId}_${deviceId}`;
                const saved = localStorage.getItem(prefsKey);

                if (!saved) {
                    try {
                        const serverSaved = await profileDeviceSettingsService.getPlaybackPrefs(activeProfileId, deviceId, serverId);
                        if (serverSaved && isMounted) {
                            localStorage.setItem(prefsKey, serverSaved);
                        } else if (isMounted) {
                            localStorage.setItem(prefsKey, "0");
                        }
                    } catch (e: unknown) {
                        console.error("Failed to fetch playback prefs", e);
                    }
                }
            }
        };

        initializeData();

        return () => {
            isMounted = false;
        };
    }, [activeProfileId, serverId, loadNav]);

    const saveNavPrefs = (items: NavItem[]) => {
        const deviceId = localStorage.getItem('device_id') || 'unknown';
        const prefsKey = `nav_prefs_global_${deviceId}`;
        const jsonString = JSON.stringify(items);

        localStorage.setItem(prefsKey, jsonString);
        setNavItems(items);

        if (activeProfileId && deviceId !== 'unknown') {
            profileDeviceSettingsService.saveNavPrefs(activeProfileId, deviceId, jsonString, serverId).catch(console.error);
        }
    };

    const togglePin = (id: string, itemServerId?: string) => {
        const updated = navItems.map(item => (item.id === id && item.serverId === itemServerId) ? { ...item, isPinned: !item.isPinned } : item);
        saveNavPrefs(updated);
    };

    const moveItem = (index: number, direction: 'up' | 'down') => {
        if (direction === 'up' && index === 0) return;
        if (direction === 'down' && index === navItems.length - 1) return;

        const updated = [...navItems];
        const swapIndex = direction === 'up' ? index - 1 : index + 1;

        const tempOrder = updated[index].order;
        updated[index].order = updated[swapIndex].order;
        updated[swapIndex].order = tempOrder;

        updated.sort((a, b) => a.order - b.order).forEach((m, i) => m.order = i);
        saveNavPrefs(updated);
    };

    useEffect(() => {
        const interval = setInterval(() => {
            setIsBlockedBySchedule(getInitialBlockedState());
        }, 30000);
        return () => clearInterval(interval);
    }, []);

    useEffect(() => {
        const checkCapabilities = async () => {
            const currentUserId = localStorage.getItem('user_id') || 'anon';
            const cacheKey = `hardware_scanned_${serverId || 'local'}_${currentUserId}`;

            if (sessionStorage.getItem(cacheKey)) return;

            sessionStorage.setItem(cacheKey, 'pending');

            try {
                const capabilities = await scanDeviceCapabilities();
                await deviceService.updateCapabilities(capabilities, serverId);

                sessionStorage.setItem(cacheKey, 'true');
            } catch (e: unknown) {
                sessionStorage.removeItem(cacheKey);
                console.error("Failed to report hardware capabilities", e);
            }
        };

        checkCapabilities();
    }, [serverId]);

    const handleSignOut = async () => {
        if (await dialog.confirm("Are you sure you want to sign out? This will disconnect all servers from this client.")) {
            serverVault.clearVault();
            authService.logout();
        }
    };

    const handleSwitchProfile = () => {
        localStorage.removeItem('profile_token');
        localStorage.removeItem('auto_login_profile_id');
        // Tear down the shared SignalR connection — otherwise it keeps the
        // outgoing profile's token and fires events for the wrong identity
        // until the next full page reload.
        disconnectSignalR();
        navigate('/profiles', { state: { manualSwitch: true } });
    };

    if (isBlockedBySchedule) {
        return (
            <div className="flex flex-col h-screen bg-gray-950 text-white items-center justify-center p-8 text-center z-50">
                <svg className="w-24 h-24 text-orange-500 mb-6" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" /></svg>
                <h1 className="text-4xl font-bold mb-4">Outside Allowed Timeframe</h1>
                <p className="text-gray-400 text-lg mb-8 max-w-md">Your profile is currently restricted by a time schedule. Please come back later or switch to a different profile.</p>
                <div className="flex gap-4">
                    <button onClick={handleSwitchProfile} className="px-6 py-3 bg-gray-800 hover:bg-gray-700 rounded-md font-bold transition-colors cursor-pointer">Switch Profile</button>
                    <button onClick={handleSignOut} className="px-6 py-3 bg-gray-800 hover:bg-gray-700 rounded-md font-bold transition-colors cursor-pointer">Sign Out of Client</button>
                </div>
            </div>
        );
    }

    const gatedNavItems = navItems.filter(i => isNavItemEnabled(i, flags, youtubeAvailable));
    const pinnedItems = gatedNavItems.filter(i => i.isPinned);
    const unpinnedItems = gatedNavItems.filter(i => !i.isPinned);

    return (
        <div
            data-vora-client=""
            className={`flex h-screen overflow-hidden transition-all duration-300 ${currentMedia && isMinimized ? 'pb-24' : ''}`}
            style={{ background: 'var(--vora-bg-canvas)', color: 'var(--vora-text-primary)' }}
        >

            <ServerManagerModal
                isOpen={isServerManagerOpen}
                onClose={() => setIsServerManagerOpen(false)}
                onServerAdded={loadNav}
            />

            <MainLayoutSidebar
                navItems={gatedNavItems}
                pinnedItems={pinnedItems}
                unpinnedItems={unpinnedItems}
                isEditingNav={isEditingNav}
                showUnpinned={showUnpinned}
                onToggleEditNav={setIsEditingNav}
                onToggleShowUnpinned={() => setShowUnpinned(!showUnpinned)}
                onMoveItem={moveItem}
                onTogglePin={togglePin}
            />

            <div className="flex-1 flex flex-col h-full overflow-hidden relative">
                <MainLayoutUserMenu
                    profileName={profileName}
                    isProfileMenuOpen={isProfileMenuOpen}
                    isServerAdmin={isServerAdmin}
                    onToggleMenu={() => setIsProfileMenuOpen(!isProfileMenuOpen)}
                    onCloseMenu={() => setIsProfileMenuOpen(false)}
                    onManageServers={() => { setIsProfileMenuOpen(false); setIsServerManagerOpen(true); }}
                    onServerSettings={() => { setIsProfileMenuOpen(false); navigate(serverId ? `/admin/server/${serverId}` : '/admin'); }}
                    onClientSettings={() => { setIsProfileMenuOpen(false); navigate(serverId ? `/server/${serverId}/settings` : '/settings'); }}
                    onAccountSettings={() => { setIsProfileMenuOpen(false); navigate(serverId ? `/server/${serverId}/account` : '/account'); }}
                    onPlayHistory={() => { setIsProfileMenuOpen(false); navigate(serverId ? `/server/${serverId}/history` : '/history'); }}
                    onSwitchProfile={handleSwitchProfile}
                    onSignOut={handleSignOut}
                />

                <main className="flex-1 overflow-y-auto">
                    <Outlet />
                </main>
            </div>

            {currentMedia?.playbackContextType === 'LiveTv'
                ? <LiveTvPlayer />
                : (currentMedia?.playbackContextType === 'LiveRadio' || currentMedia?.playbackContextType === 'Podcast' || currentMedia?.playbackContextType === 'Music')
                    ? <LiveRadioPlayer />
                    : <GlobalVideoPlayer />}

            <NowPlayingFullscreen />
        </div>
    );
}