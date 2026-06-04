import { useEffect, useState, useMemo, useRef, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { StorageKeys, getProfileIdFromToken } from '../../../utils/storageKeys';
import { iptvClientService } from '../../../api/Iptv/iptvClientService';
import { type IptvChannelVM } from '../../../api/Iptv/iptvAdminService';
import { profileDeviceSettingsService } from '../../../api/Users/profileDeviceSettingsService';
import { usePlayer } from '../../../contexts/usePlayer';
import { useFeatureFlags } from '../../../hooks/useFeatureFlags';
import { useSignalREvent } from '../../../hooks/useSignalREvent';
import PodcastsTab from './PodcastsTab';
import MusicTab from './MusicTab';
import PageHeader from '../../../components/Client/Primitives/PageHeader';
import Tabs from '../../../components/Client/Primitives/Tabs';
import EmptyState from '../../../components/Client/Primitives/EmptyState';

type AudioTab = 'Radio' | 'Podcasts' | 'Music';

const ACTIVE_TAB_STORAGE_KEY = 'audio_active_tab';

const readSavedTab = (): AudioTab => {
    const saved = sessionStorage.getItem(ACTIVE_TAB_STORAGE_KEY);
    return saved === 'Radio' || saved === 'Podcasts' || saved === 'Music' ? saved : 'Radio';
};

interface RadioPrefs {
    favoriteIds: string[];
    hiddenIds: string[];
    countryFilter: string;
}

const DEFAULT_RADIO_PREFS: RadioPrefs = {
    favoriteIds: [],
    hiddenIds: [],
    countryFilter: 'All',
};

const FAVORITES_GROUP = '★ Favorites';

const radioPrefsCacheKey = (profileId: string, deviceId: string) => `radio_prefs_${profileId}_${deviceId}`;

export default function AudioHubPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const { playMedia } = usePlayer();
    const flags = useFeatureFlags();
    const visibleTabs = useMemo((): AudioTab[] => {
        const tabs: AudioTab[] = [];
        if (flags.internetRadio) tabs.push('Radio');
        if (flags.podcasts) tabs.push('Podcasts');
        tabs.push('Music');
        return tabs;
    }, [flags.internetRadio, flags.podcasts]);
    const [activeTab, setActiveTab] = useState<AudioTab>(readSavedTab);

    useEffect(() => {
        if (!visibleTabs.includes(activeTab)) {
            setActiveTab(visibleTabs[0]);
            sessionStorage.setItem(ACTIVE_TAB_STORAGE_KEY, visibleTabs[0]);
        }
    }, [visibleTabs, activeTab]);

    const handleTabChange = (tab: AudioTab) => {
        setActiveTab(tab);
        sessionStorage.setItem(ACTIVE_TAB_STORAGE_KEY, tab);
    };

    const [channels, setChannels] = useState<IptvChannelVM[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [search, setSearch] = useState('');
    const [prefs, setPrefs] = useState<RadioPrefs>(DEFAULT_RADIO_PREFS);
    const [prefsLoaded, setPrefsLoaded] = useState(false);
    const [showHidden, setShowHidden] = useState(false);
    const [contextMenu, setContextMenu] = useState<{ x: number, y: number, channelId: string } | null>(null);

    const profileId = useMemo(() => {
        const token = localStorage.getItem(StorageKeys.profileToken);
        if (!token) return '';
        try {
            return getProfileIdFromToken(token) ?? '';
        } catch {
            return '';
        }
    }, []);
    const deviceId = useMemo(() => localStorage.getItem(StorageKeys.deviceId) || 'unknown', []);

    useEffect(() => {
        const load = async () => {
            try {
                const userId = localStorage.getItem(StorageKeys.userId) || profileId;
                if (!userId || !profileId) return;

                const cacheKey = radioPrefsCacheKey(profileId, deviceId);
                const cached = localStorage.getItem(cacheKey);
                if (cached) {
                    try {
                        const parsedCached = JSON.parse(cached) as Partial<RadioPrefs>;
                        setPrefs({
                            favoriteIds: parsedCached.favoriteIds ?? [],
                            hiddenIds: parsedCached.hiddenIds ?? [],
                            countryFilter: parsedCached.countryFilter ?? 'All',
                        });
                    } catch {
                        // ignore corrupted cache
                    }
                }

                const [providers, savedPrefs] = await Promise.all([
                    iptvClientService.getPlaylists(userId, profileId, serverId),
                    profileDeviceSettingsService.getRadioPrefs(profileId, deviceId, serverId).catch(() => null),
                ]);

                const radio = providers
                    .flatMap(p => p.channels || [])
                    .filter(c => c.kind === 'Radio' && !c.isHiddenByAdmin);
                setChannels(radio);

                if (savedPrefs) {
                    try {
                        const parsed = JSON.parse(savedPrefs) as Partial<RadioPrefs>;
                        const merged: RadioPrefs = {
                            favoriteIds: parsed.favoriteIds ?? [],
                            hiddenIds: parsed.hiddenIds ?? [],
                            countryFilter: parsed.countryFilter ?? 'All',
                        };
                        setPrefs(merged);
                        localStorage.setItem(cacheKey, JSON.stringify(merged));
                    } catch {
                        // ignore corrupted server payload, cache wins
                    }
                }
            } catch (error) {
                console.error('Failed to load radio channels', error);
            } finally {
                setIsLoading(false);
                setPrefsLoaded(true);
            }
        };
        load();
    }, [serverId, profileId, deviceId]);

    useEffect(() => {
        const close = () => setContextMenu(null);
        document.addEventListener('click', close);
        return () => document.removeEventListener('click', close);
    }, []);

    const saveTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
    // Skip-window for SignalR echoes: when we've just sent our own save, the
    // server fires RadioPrefsUpdated back to all of this profile's sessions
    // including ours. Within 2s of a local save we ignore the event to avoid
    // a refetch that would race with our in-flight optimistic state.
    const lastLocalSaveAt = useRef<number>(0);
    useEffect(() => {
        if (!prefsLoaded || !profileId) return;
        const serialized = JSON.stringify(prefs);
        localStorage.setItem(radioPrefsCacheKey(profileId, deviceId), serialized);
        if (saveTimeoutRef.current) clearTimeout(saveTimeoutRef.current);
        saveTimeoutRef.current = setTimeout(() => {
            lastLocalSaveAt.current = Date.now();
            profileDeviceSettingsService
                .saveRadioPrefs(profileId, deviceId, serialized, serverId)
                .catch(err => console.error('Failed to save radio prefs', err));
        }, 500);
        return () => {
            if (saveTimeoutRef.current) clearTimeout(saveTimeoutRef.current);
        };
    }, [prefs, prefsLoaded, profileId, deviceId, serverId]);

    // Cross-device sync: another session (Android, second browser, etc.)
    // saved radio prefs for this profile, so refetch and replace local
    // state. The 2s skip-window guards against echoing our own save.
    useSignalREvent("RadioPrefsUpdated", useCallback((eventProfileId: string) => {
        if (!profileId || eventProfileId.toLowerCase() !== profileId.toLowerCase()) return;
        if (Date.now() - lastLocalSaveAt.current < 2000) return;
        profileDeviceSettingsService
            .getRadioPrefs(profileId, deviceId, serverId)
            .then(json => {
                if (!json) return;
                try {
                    const parsed = JSON.parse(json) as Partial<RadioPrefs>;
                    setPrefs({
                        favoriteIds: parsed.favoriteIds ?? [],
                        hiddenIds: parsed.hiddenIds ?? [],
                        countryFilter: parsed.countryFilter ?? 'All',
                    });
                } catch { /* ignore malformed payload */ }
            })
            .catch(err => console.error('Failed to refresh radio prefs after SignalR', err));
    }, [profileId, deviceId, serverId]));

    const dedupedChannels = useMemo(() => {
        const seen = new Set<string>();
        const result: IptvChannelVM[] = [];
        for (const c of channels) {
            const key = c.externalChannelId || c.streamUrl;
            if (seen.has(key)) continue;
            seen.add(key);
            result.push(c);
        }
        return result;
    }, [channels]);

    const availableCountries = useMemo(() => {
        const set = new Set<string>();
        for (const c of dedupedChannels) {
            if (c.countryCode) set.add(c.countryCode);
        }
        return Array.from(set).sort();
    }, [dedupedChannels]);

    const handleCountryChange = (next: string) => {
        setPrefs(p => ({ ...p, countryFilter: next }));
    };

    const toggleFavorite = useCallback((channelId: string) => {
        setPrefs(p => ({
            ...p,
            favoriteIds: p.favoriteIds.includes(channelId)
                ? p.favoriteIds.filter(id => id !== channelId)
                : [...p.favoriteIds, channelId],
        }));
    }, []);

    const hideStation = useCallback((channelId: string) => {
        setPrefs(p => ({
            ...p,
            hiddenIds: p.hiddenIds.includes(channelId) ? p.hiddenIds : [...p.hiddenIds, channelId],
            favoriteIds: p.favoriteIds.filter(id => id !== channelId),
        }));
    }, []);

    const restoreStation = useCallback((channelId: string) => {
        setPrefs(p => ({
            ...p,
            hiddenIds: p.hiddenIds.filter(id => id !== channelId),
        }));
    }, []);

    const normalizeGroup = (g: string | undefined): string => {
        const raw = (g || 'Other').replace(/['’]/g, '').trim();
        if (raw.length === 0) return 'Other';
        return raw.charAt(0).toUpperCase() + raw.slice(1).toLowerCase();
    };

    const visibleChannels = useMemo(() => {
        const hiddenSet = new Set(prefs.hiddenIds);
        const lowerSearch = search.toLowerCase();
        return dedupedChannels.filter(c =>
            (showHidden || !hiddenSet.has(c.id)) &&
            (prefs.countryFilter === 'All' || c.countryCode === prefs.countryFilter) &&
            (c.name.toLowerCase().includes(lowerSearch) ||
                (c.groupTitle || '').toLowerCase().includes(lowerSearch))
        );
    }, [dedupedChannels, search, prefs.countryFilter, prefs.hiddenIds, showHidden]);

    const groupedRadio = useMemo(() => {
        const favoriteSet = new Set(prefs.favoriteIds);
        const groups: Record<string, IptvChannelVM[]> = {};
        const favorites: IptvChannelVM[] = [];

        for (const c of visibleChannels) {
            if (favoriteSet.has(c.id)) {
                favorites.push(c);
                continue;
            }
            const key = normalizeGroup(c.groupTitle);
            if (!groups[key]) groups[key] = [];
            groups[key].push(c);
        }

        if (favorites.length > 0) {
            favorites.sort((a, b) => a.name.localeCompare(b.name));
            groups[FAVORITES_GROUP] = favorites;
        }

        for (const key of Object.keys(groups)) {
            if (key === FAVORITES_GROUP) continue;
            groups[key].sort((a, b) => a.name.localeCompare(b.name));
        }
        return groups;
    }, [visibleChannels, prefs.favoriteIds]);

    const sortedGroupKeys = useMemo(() => {
        const keys = Object.keys(groupedRadio);
        return keys.sort((a, b) => {
            if (a === FAVORITES_GROUP) return -1;
            if (b === FAVORITES_GROUP) return 1;
            return a.localeCompare(b);
        });
    }, [groupedRadio]);

    const hiddenCount = prefs.hiddenIds.length;

    const handlePlay = (channel: IptvChannelVM) => {
        playMedia({
            id: channel.id,
            title: channel.name,
            subtitle: channel.groupTitle || 'Live Radio',
            posterUrl: channel.logoUrl,
            streamUrl: channel.streamUrl,
            serverId: serverId ?? undefined,
            container: 'hls',
            playbackContextType: 'LiveRadio',
        });
    };

    const tabDefinitions = visibleTabs.map(tab => ({ key: tab, label: tab }));

    return (
        <div className="min-h-full pb-16">
            {contextMenu && (
                <div
                    style={{
                        top: contextMenu.y,
                        left: contextMenu.x,
                        background: 'var(--vora-bg-raised)',
                        border: '1px solid var(--vora-border-strong)',
                        boxShadow: 'var(--vora-shadow-lg)',
                    }}
                    className="fixed z-[9999] w-52 overflow-hidden rounded-xl text-sm"
                    onClick={e => e.stopPropagation()}
                >
                    <button
                        type="button"
                        onClick={() => { toggleFavorite(contextMenu.channelId); setContextMenu(null); }}
                        className="block w-full cursor-pointer px-4 py-2.5 text-left transition-colors hover:bg-white/5"
                        style={{ color: 'var(--vora-text-primary)' }}
                    >
                        {prefs.favoriteIds.includes(contextMenu.channelId) ? 'Remove favorite' : 'Add to favorites'}
                    </button>
                    {prefs.hiddenIds.includes(contextMenu.channelId) ? (
                        <button
                            type="button"
                            onClick={() => { restoreStation(contextMenu.channelId); setContextMenu(null); }}
                            className="block w-full cursor-pointer px-4 py-2.5 text-left transition-colors hover:bg-white/5"
                            style={{ color: 'var(--vora-accent-text)' }}
                        >
                            Restore station
                        </button>
                    ) : (
                        <button
                            type="button"
                            onClick={() => { hideStation(contextMenu.channelId); setContextMenu(null); }}
                            className="block w-full cursor-pointer px-4 py-2.5 text-left font-medium transition-colors hover:bg-white/5"
                            style={{ color: 'var(--vora-danger-text)' }}
                        >
                            Hide station
                        </button>
                    )}
                </div>
            )}

            <PageHeader
                title="Audio"
                subtitle="Live radio, podcasts, and music — all in one place."
            />

            <div className="px-8">
                <Tabs<AudioTab>
                    tabs={tabDefinitions}
                    active={activeTab}
                    onChange={handleTabChange}
                />
            </div>

            <div className="px-8 pt-6">
                {activeTab === 'Radio' && (
                    <>
                        <div className="mb-6 flex flex-col gap-3 sm:flex-row">
                            <input
                                type="text"
                                placeholder="Search stations by name or genre..."
                                value={search}
                                onChange={e => setSearch(e.target.value)}
                                className="flex-1 rounded-md px-4 py-2.5 text-sm outline-none transition-colors"
                                style={{
                                    background: 'var(--vora-bg-surface)',
                                    border: '1px solid var(--vora-border-subtle)',
                                    color: 'var(--vora-text-primary)',
                                }}
                            />
                            <select
                                value={prefs.countryFilter}
                                onChange={e => handleCountryChange(e.target.value)}
                                className="min-w-[160px] cursor-pointer rounded-md px-3 py-2.5 text-sm outline-none transition-colors"
                                style={{
                                    background: 'var(--vora-bg-surface)',
                                    border: '1px solid var(--vora-border-subtle)',
                                    color: 'var(--vora-text-primary)',
                                }}
                            >
                                <option value="All">All countries</option>
                                {availableCountries.map(code => (
                                    <option key={code} value={code}>{code}</option>
                                ))}
                            </select>
                        </div>

                        {hiddenCount > 0 && (
                            <div className="mb-6 flex items-center">
                                <label className="inline-flex cursor-pointer items-center gap-2 text-xs transition-colors" style={{ color: 'var(--vora-text-muted)' }}>
                                    <input
                                        type="checkbox"
                                        checked={showHidden}
                                        onChange={e => setShowHidden(e.target.checked)}
                                        className="h-4 w-4 cursor-pointer accent-[var(--vora-accent-500)]"
                                    />
                                    <span>Show {hiddenCount} hidden station{hiddenCount === 1 ? '' : 's'}</span>
                                </label>
                            </div>
                        )}

                        {isLoading ? (
                            <div className="space-y-8">
                                {[1, 2, 3].map(i => (
                                    <div key={i}>
                                        <div className="vora-skeleton mb-4 h-4 w-32" />
                                        <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-6">
                                            {Array.from({ length: 6 }).map((_, j) => <div key={j} className="vora-skeleton h-40" />)}
                                        </div>
                                    </div>
                                ))}
                            </div>
                        ) : channels.length === 0 ? (
                            <EmptyState
                                title="No radio stations yet"
                                description="An admin can mark IPTV channels as Radio from Server Settings → IPTV → Manage Channels."
                                icon={(
                                    <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                                        <path d="M3.5 12a8.5 8.5 0 0117 0" />
                                        <path d="M7 12a5 5 0 0110 0" />
                                        <circle cx="12" cy="12" r="1" />
                                    </svg>
                                )}
                            />
                        ) : visibleChannels.length === 0 ? (
                            <EmptyState
                                title="No stations match"
                                description="Try clearing the search or country filter."
                            />
                        ) : (
                            sortedGroupKeys.map(group => (
                                <section key={group} className="mb-10">
                                    <h2
                                        className="mb-4 text-xs font-semibold uppercase tracking-widest"
                                        style={{ color: group === FAVORITES_GROUP ? 'var(--vora-accent-text)' : 'var(--vora-text-muted)' }}
                                    >
                                        {group}
                                    </h2>
                                    <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-6">
                                        {groupedRadio[group].map(c => {
                                            const isFavorite = prefs.favoriteIds.includes(c.id);
                                            const isHidden = prefs.hiddenIds.includes(c.id);
                                            return (
                                                <button
                                                    key={c.id}
                                                    type="button"
                                                    onClick={() => handlePlay(c)}
                                                    onContextMenu={(e) => {
                                                        e.preventDefault();
                                                        setContextMenu({ x: e.pageX, y: e.pageY, channelId: c.id });
                                                    }}
                                                    className="vora-card vora-card-interactive group relative flex cursor-pointer flex-col items-center p-4 text-left"
                                                    style={{ opacity: isHidden ? 0.45 : 1 }}
                                                >
                                                    {isFavorite && (
                                                        <span
                                                            className="absolute left-2 top-2 flex h-5 w-5 items-center justify-center rounded-full"
                                                            style={{ background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)' }}
                                                            title="Favorite"
                                                        >
                                                            <svg width="11" height="11" viewBox="0 0 24 24" fill="currentColor">
                                                                <path d="M11.049 2.927c.3-.921 1.603-.921 1.902 0l1.519 4.674a1 1 0 00.95.69h4.915c.969 0 1.371 1.24.588 1.81l-3.976 2.888a1 1 0 00-.363 1.118l1.518 4.674c.3.922-.755 1.688-1.538 1.118l-3.976-2.888a1 1 0 00-1.176 0l-3.976 2.888c-.783.57-1.838-.197-1.538-1.118l1.518-4.674a1 1 0 00-.363-1.118l-3.976-2.888c-.784-.57-.38-1.81.588-1.81h4.914a1 1 0 00.951-.69l1.519-4.674z" />
                                                            </svg>
                                                        </span>
                                                    )}

                                                    <div
                                                        className="mb-3 flex h-24 w-24 items-center justify-center overflow-hidden rounded-full transition-transform group-hover:scale-105"
                                                        style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)' }}
                                                    >
                                                        {c.logoUrl ? (
                                                            <img src={c.logoUrl} alt={c.name} className="max-h-[80%] max-w-[80%] object-contain" />
                                                        ) : (
                                                            <svg width="36" height="36" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" style={{ color: 'var(--vora-text-disabled)' }}>
                                                                <path d="M3.5 12a8.5 8.5 0 0117 0" />
                                                                <path d="M7 12a5 5 0 0110 0" />
                                                                <circle cx="12" cy="12" r="1" />
                                                            </svg>
                                                        )}
                                                    </div>
                                                    <span
                                                        className="line-clamp-2 w-full text-center text-sm font-medium"
                                                        style={{ color: 'var(--vora-text-primary)' }}
                                                    >
                                                        {c.name}
                                                    </span>
                                                    {c.countryCode && c.countryCode !== 'Unknown' && (
                                                        <span className="mt-1 text-[10px] font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>
                                                            {c.countryCode}
                                                        </span>
                                                    )}
                                                </button>
                                            );
                                        })}
                                    </div>
                                </section>
                            ))
                        )}
                    </>
                )}

                {activeTab === 'Podcasts' && <PodcastsTab />}

                {activeTab === 'Music' && <MusicTab />}
            </div>
        </div>
    );
}
