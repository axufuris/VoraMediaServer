import { useState, useEffect, useRef, useMemo } from 'react';
import { useParams } from 'react-router-dom';
import { type IptvChannelVM } from '../../../api/Iptv/iptvAdminService';
import { iptvClientService, type IptvProgramDto } from '../../../api/Iptv/iptvClientService';
import { dvrService, type IptvRecordingSessionVM } from '../../../api/Iptv/dvrService';
import { usePlayer } from '../../../contexts/PlayerContext';
import { serverVault } from '../../../utils/serverVault';
import { useSignalREvent } from '../../../hooks/useSignalREvent';
import { useCallback } from 'react';

import { profileDeviceSettingsService } from '../../../api/Users/profileDeviceSettingsService';
import { useDialog } from '../../../dialogs';
import GuideProgramModal from '../../../components/Iptv/GuideProgramModal';
const ROW_HEIGHT = 80;
const PX_PER_MINUTE = 6;
const HOURS_TO_SHOW = 6;

const parseDate = (dateStr: string) => {
    if (!dateStr) return new Date();
    if (!dateStr.endsWith('Z') && !dateStr.includes('+') && !dateStr.includes('-')) return new Date(dateStr + 'Z');
    return new Date(dateStr);
};

export interface LiveTvGuideProps {
    isEmbedded?: boolean;
    currentPlayingChannelId?: string;
    onPlayChannel?: (channel: IptvChannelVM, program?: IptvProgramDto) => void;
    onHoverProgram?: (channel: IptvChannelVM, program: IptvProgramDto | null) => void;
}

export default function LiveTvGuide({ isEmbedded = false, currentPlayingChannelId, onPlayChannel, onHoverProgram }: LiveTvGuideProps) {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const { playMedia } = usePlayer();

    const canRecord = useMemo(() => {
        const token = localStorage.getItem('profile_token');
        if (!token) return false;
        try {
            const payload = JSON.parse(atob(token.split('.')[1]));
            return payload.canRecordLiveTv === 'True';
        } catch {
            return false;
        }
    }, []);

    const [channels, setChannels] = useState<IptvChannelVM[]>([]);
    const [guideData, setGuideData] = useState<Record<string, IptvProgramDto[]>>({});
    const [recordingSessions, setRecordingSessions] = useState<IptvRecordingSessionVM[]>([]); // <-- NEW
    const [isLoading, setIsLoading] = useState(true);

    const [scrollTop, setScrollTop] = useState(0);
    const scrollContainerRef = useRef<HTMLDivElement>(null);

    const [hoveredProgram, setHoveredProgram] = useState<{ channel: IptvChannelVM, program: IptvProgramDto | null } | null>(null);

    const [showRegionMenu, setShowRegionMenu] = useState(false);
    const [showResMenu, setShowResMenu] = useState(false);
    const [searchQuery, setSearchQuery] = useState('');
    const [activeCategory, setActiveCategory] = useState<string>('All');
    const [currentTimelineX, setCurrentTimelineX] = useState<number>(0);

    const [prefs, setPrefs] = useState({
        enabledProviders: [] as string[],
        hiddenChannels: [] as string[],
        favoriteChannels: [] as string[],
        regions: [] as string[], // FIXED: Defaults to 'All Regions' instead of 'US'
        resolutions: [] as string[],
        hideEmpty: false
    });

    const [contextMenu, setContextMenu] = useState<{ x: number, y: number, channelId: string } | null>(null);
    const [programModal, setProgramModal] = useState<{ isOpen: boolean, channel: IptvChannelVM, program: IptvProgramDto } | null>(null);
    const [hasAutoScrolled, setHasAutoScrolled] = useState(false);

    const { timelineStart, timelineEnd, timeMarkers } = useMemo(() => {
        const now = new Date();
        const startMinutes = now.getMinutes() < 30 ? 0 : 30;
        const start = new Date(now.getFullYear(), now.getMonth(), now.getDate(), now.getHours(), startMinutes, 0);
        start.setMinutes(start.getMinutes() - 30);
        const end = new Date(start.getTime() + HOURS_TO_SHOW * 60 * 60 * 1000);

        const markers = [];
        for (let i = 0; i <= HOURS_TO_SHOW * 2; i++) markers.push(new Date(start.getTime() + i * 30 * 60 * 1000));
        return { timelineStart: start, timelineEnd: end, timeMarkers: markers };
    }, []);

    useSignalREvent("DvrSessionsUpdated", useCallback(() => {
        const fetchFreshSessions = async () => {
            try {
                const activeServer = serverVault.getActiveServer();
                if (!activeServer) return;

                const profileToken = localStorage.getItem('profile_token');
                const activeProfileId = profileToken ? JSON.parse(atob(profileToken.split('.')[1])).sub : activeServer.profileId;

                const sessions = await dvrService.getRecordingSessions(activeProfileId, activeServer.id);
                setRecordingSessions(sessions);
            } catch (e) {
                console.error("SignalR: Failed to refresh DVR sessions", e);
            }
        };
        fetchFreshSessions();
    }, []));

    useEffect(() => {
        const updateTimeLine = () => {
            const now = new Date();
            const diffMins = (now.getTime() - timelineStart.getTime()) / 60000;
            setCurrentTimelineX(Math.max(0, diffMins * PX_PER_MINUTE));
        };
        updateTimeLine();
        const intervalId = setInterval(updateTimeLine, 60000);
        return () => clearInterval(intervalId);
    }, [timelineStart]);

    const updatePrefs = (newPrefs: typeof prefs) => {
        setPrefs(newPrefs);
        const activeServer = serverVault.getActiveServer();
        if (!activeServer) return;

        const deviceId = localStorage.getItem('device_id') || 'unknown';
        const json = JSON.stringify(newPrefs);
        localStorage.setItem(`iptv_prefs_${activeServer.profileId}_${deviceId}`, json);
        if (profileDeviceSettingsService.saveIptvPrefs) profileDeviceSettingsService.saveIptvPrefs(activeServer.profileId, deviceId, json, serverId).catch(console.error);
    };

    useEffect(() => {
        const loadGuide = async () => {
            try {
                const activeServer = serverVault.getActiveServer();
                if (!activeServer) return;

                const profileToken = localStorage.getItem('profile_token');
                const activeProfileId = profileToken ? JSON.parse(atob(profileToken.split('.')[1])).sub : activeServer.profileId;
                const userId = localStorage.getItem('user_id') || activeProfileId;
                const deviceId = localStorage.getItem('device_id') || 'unknown';

                const allProviders = await iptvClientService.getPlaylists(userId, activeProfileId, serverId);

                let currentPrefs = {
                    enabledProviders: [] as string[],
                    hiddenChannels: [] as string[],
                    favoriteChannels: [] as string[],
                    regions: [] as string[],
                    resolutions: [] as string[],
                    hideEmpty: false
                };

                let hasSavedSettings = false;
                const savedIptv = localStorage.getItem(`iptv_prefs_${activeProfileId}_${deviceId}`);

                if (savedIptv && savedIptv !== "[]" && savedIptv !== "") {
                    hasSavedSettings = true;
                    const raw = JSON.parse(savedIptv);
                    if (Array.isArray(raw)) currentPrefs.enabledProviders = raw;
                    else currentPrefs = { ...currentPrefs, ...raw };
                }

                currentPrefs.enabledProviders = currentPrefs.enabledProviders.filter(id => allProviders.some(p => p.id === id));

                if (!hasSavedSettings && currentPrefs.enabledProviders.length === 0 && allProviders.length > 0) {
                    currentPrefs.enabledProviders = allProviders.map(p => p.id);
                }
                setPrefs(currentPrefs);

                const activeChannels = allProviders.filter(p => currentPrefs.enabledProviders.includes(p.id)).flatMap(p => p.channels || []).filter(c => c.kind === 'Tv');
                setChannels(activeChannels);

                const channelIds = activeChannels.map(c => c.externalChannelId);
                const guide = await iptvClientService.getGuide(userId, activeProfileId, channelIds, timelineStart.toISOString(), timelineEnd.toISOString(), serverId);

                const normalizedGuide: Record<string, IptvProgramDto[]> = {};
                for (const [key, value] of Object.entries(guide)) normalizedGuide[key.toLowerCase()] = value;
                setGuideData(normalizedGuide);

                try {
                    const sessions = await dvrService.getRecordingSessions(activeProfileId, serverId);
                    setRecordingSessions(sessions);
                } catch (e) { console.error(e); }

            } catch (error) {
                console.error("Failed to load Live TV Guide", error);
            } finally {
                setIsLoading(false);
            }
        };
        loadGuide();
    }, [serverId, timelineStart, timelineEnd]);

    useEffect(() => {
        const closeMenu = () => setContextMenu(null);
        document.addEventListener('click', closeMenu);
        return () => document.removeEventListener('click', closeMenu);
    }, []);

    const categories = useMemo(() => {
        const canonicalize = (s: string): string => s.toLowerCase().replace(/['‘’]/g, '').replace(/\s+/g, ' ').trim();
        const CODEC_DENYLIST = new Set([
            'aac', 'aac+', 'he-aac', 'mp3', 'mp2', 'ac3', 'ac-3', 'eac3', 'e-ac3', 'flac', 'pcm', 'opus', 'vorbis', 'wma'
        ]);
        const isBitrate = (s: string): boolean => /^\d+\s*(k|kb|kbps|bps|hz|khz)$/i.test(s);

        const candidates = channels.filter(c => {
            if (prefs.hiddenChannels.includes(c.externalChannelId)) return false;
            if (prefs.regions.length > 0 && !prefs.regions.includes(c.countryCode || 'Unknown')) return false;
            if (prefs.resolutions.length > 0 && !prefs.resolutions.includes(c.resolution || 'Unknown')) return false;
            if (prefs.hideEmpty) {
                const hasPrograms = (guideData[(c.externalChannelId || '').toLowerCase()] || []).length > 0;
                if (!hasPrograms) return false;
            }
            return true;
        });

        const buckets = new Map<string, { displays: Map<string, number>; count: number }>();
        for (const c of candidates) {
            const groups = (c.groupTitle || '').split(';').map(g => g.trim()).filter(g => g !== '');
            const seenForChannel = new Set<string>();
            for (const g of groups) {
                const key = canonicalize(g);
                if (!key) continue;
                if (CODEC_DENYLIST.has(key)) continue;
                if (isBitrate(key)) continue;
                if (seenForChannel.has(key)) continue;
                seenForChannel.add(key);

                let bucket = buckets.get(key);
                if (!bucket) {
                    bucket = { displays: new Map(), count: 0 };
                    buckets.set(key, bucket);
                }
                bucket.count++;
                bucket.displays.set(g, (bucket.displays.get(g) ?? 0) + 1);
            }
        }

        const sorted = Array.from(buckets.entries()).map(([key, bucket]) => {
            let bestLabel = '';
            let bestCount = -1;
            for (const [label, cnt] of bucket.displays) {
                if (cnt > bestCount || (cnt === bestCount && label.length < bestLabel.length)) {
                    bestLabel = label;
                    bestCount = cnt;
                }
            }
            return { key, label: bestLabel, count: bucket.count };
        }).sort((a, b) => a.label.localeCompare(b.label));

        return [
            { key: 'All', label: 'All', count: candidates.length },
            { key: 'Favorites', label: 'Favorites', count: candidates.filter(c => prefs.favoriteChannels.includes(c.externalChannelId)).length },
            ...sorted
        ];
    }, [channels, prefs, guideData]);

    const regions = useMemo(() => Array.from(new Set(channels.map(c => c.countryCode || 'Unknown'))).sort(), [channels]);

    useEffect(() => {
        if (categories.length === 0) return;
        if (categories.some(c => c.key === activeCategory)) return;
        setActiveCategory('All');
    }, [categories, activeCategory]);

    const resolutions = useMemo(() => {
        const resSet = Array.from(new Set(channels.map(c => c.resolution || 'Unknown')));
        const order = ['4K', '1080P', 'HD', '720P', '480P', 'SD', 'Unknown'];
        return resSet.sort((a, b) => {
            let indexA = order.indexOf(a); let indexB = order.indexOf(b);
            if (indexA === -1) indexA = 99; if (indexB === -1) indexB = 99;
            return indexA - indexB;
        });
    }, [channels]);

    const filteredChannels = useMemo(() => {
        const canonicalize = (s: string): string => s.toLowerCase().replace(/['‘’]/g, '').replace(/\s+/g, ' ').trim();
        const filtered = channels.filter(c => {
            if (prefs.hiddenChannels.includes(c.externalChannelId)) return false;

            const groups = (c.groupTitle || '').split(';').map(g => g.trim());
            const hasPrograms = (guideData[(c.externalChannelId || '').toLowerCase()] || []).length > 0;
            const isFavorite = prefs.favoriteChannels.includes(c.externalChannelId);

            const matchesCategory = activeCategory === 'All' || (activeCategory === 'Favorites' ? isFavorite : groups.some(g => canonicalize(g) === activeCategory));
            const matchesSearch = c.name.toLowerCase().includes(searchQuery.toLowerCase());
            const matchesEmpty = prefs.hideEmpty ? hasPrograms : true;
            const matchesRegion = prefs.regions.length === 0 || prefs.regions.includes(c.countryCode || 'Unknown');
            const matchesResolution = prefs.resolutions.length === 0 || prefs.resolutions.includes(c.resolution || 'Unknown');

            return matchesCategory && matchesRegion && matchesResolution && matchesSearch && matchesEmpty;
        });

        return filtered.sort((a, b) => {
            const aFav = prefs.favoriteChannels.includes(a.externalChannelId);
            const bFav = prefs.favoriteChannels.includes(b.externalChannelId);
            if (aFav && !bFav) return -1;
            if (!aFav && bFav) return 1;

            const aHasPrograms = (guideData[(a.externalChannelId || '').toLowerCase()] || []).length > 0;
            const bHasPrograms = (guideData[(b.externalChannelId || '').toLowerCase()] || []).length > 0;
            if (aHasPrograms && !bHasPrograms) return -1;
            if (!aHasPrograms && bHasPrograms) return 1;
            return a.name.localeCompare(b.name);
        });
    }, [channels, activeCategory, prefs, searchQuery, guideData]);

    useEffect(() => {
        if (!hasAutoScrolled && !isLoading && filteredChannels.length > 0 && currentPlayingChannelId && scrollContainerRef.current) {
            const activeIndex = filteredChannels.findIndex(c => c.id === currentPlayingChannelId);

            if (activeIndex !== -1) {
                const containerHeight = scrollContainerRef.current.clientHeight;
                const targetY = Math.max(0, (activeIndex * ROW_HEIGHT) - (containerHeight / 2) + (ROW_HEIGHT / 2));

                const targetX = Math.max(0, currentTimelineX - 100);

                scrollContainerRef.current.scrollTo({ top: targetY, left: targetX, behavior: 'smooth' });

                setScrollTop(targetY); // Update virtualization state
                setHasAutoScrolled(true);
            }
        }
    }, [filteredChannels, currentPlayingChannelId, isLoading, hasAutoScrolled, currentTimelineX]);

    const handleScroll = (e: React.UIEvent<HTMLDivElement>) => setScrollTop(e.currentTarget.scrollTop);

    const visibleRows = Math.ceil(window.innerHeight / ROW_HEIGHT);
    const startIndex = Math.max(0, Math.floor(scrollTop / ROW_HEIGHT) - 3);
    const endIndex = Math.min(filteredChannels.length, startIndex + visibleRows + 6);
    const visibleChannels = filteredChannels.slice(startIndex, endIndex);
    const totalHeight = filteredChannels.length * ROW_HEIGHT;
    const offsetY = startIndex * ROW_HEIGHT;
    const timelineWidth = HOURS_TO_SHOW * 60 * PX_PER_MINUTE;

    const handlePlayChannel = (channel: IptvChannelVM, program?: IptvProgramDto) => {
        if (onPlayChannel) { onPlayChannel(channel, program); return; }
        playMedia({ id: channel.id, title: channel.name, subtitle: program ? program.title : 'Live TV', posterUrl: channel.logoUrl, streamUrl: channel.streamUrl, serverId: serverId ?? undefined, container: 'hls', playbackContextType: 'LiveTv' });
    };

    const handleScheduleRecording = async (channel: IptvChannelVM, program: IptvProgramDto, isSeries: boolean, keepMaxEpisodes: number = 0) => {
        try {
            const activeServer = serverVault.getActiveServer();
            if (!activeServer) return;
            const profileToken = localStorage.getItem('profile_token');
            const activeProfileId = profileToken ? JSON.parse(atob(profileToken.split('.')[1])).sub : activeServer.profileId;

            await dvrService.scheduleRecording(
                activeProfileId,
                channel.id,
                program.title,
                program.id,
                isSeries,
                keepMaxEpisodes,
                serverId
            );

            const newSessions = await dvrService.getRecordingSessions(activeProfileId, serverId);
            setRecordingSessions(newSessions);

            await dialog.alert("Recording scheduled successfully!", "Success");
            setProgramModal(null);
        } catch (error) {
            const err = error as { response?: { status: number } };

            if (err.response?.status === 403) {
                await dialog.alert({ title: "Permission Denied", message: "You do not have permission to record Live TV." });
            } else {
                await dialog.alert({ title: "Scheduling Failed", message: "Failed to schedule recording. Tuner limits may be full or storage quota exceeded." });
            }
        }
    };

    const handleRightClick = (e: React.MouseEvent, channelId: string) => {
        e.preventDefault();
        setContextMenu({ x: e.pageX, y: e.pageY, channelId });
    };

    const toggleFavorite = (channelId: string) => {
        const isFav = prefs.favoriteChannels.includes(channelId);
        updatePrefs({
            ...prefs,
            favoriteChannels: isFav ? prefs.favoriteChannels.filter(id => id !== channelId) : [...prefs.favoriteChannels, channelId]
        });
        setContextMenu(null);
    };

    const hideChannel = (channelId: string) => {
        updatePrefs({
            ...prefs,
            hiddenChannels: [...prefs.hiddenChannels, channelId]
        });
        setContextMenu(null);
    };

    const formatTime = (date: Date) => date.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });

    const toggleArrayItem = (item: string, currentArray: string[], key: 'regions' | 'resolutions') => {
        let newArr = [...currentArray];
        if (newArr.length === 0) newArr = [item];
        else if (newArr.includes(item)) newArr = newArr.filter(i => i !== item);
        else newArr.push(item);
        updatePrefs({ ...prefs, [key]: newArr });
    };

    if (isLoading) {
        return (
            <div className="flex h-full w-full flex-col" style={{ background: 'var(--vora-bg-canvas)' }}>
                <div className="vora-skeleton mx-6 mt-6 h-12" />
                <div className="vora-skeleton mx-6 mt-4 h-28" />
                <div className="vora-skeleton mx-6 mt-4 h-10" />
                <div className="vora-skeleton mx-6 my-4 flex-1" />
            </div>
        );
    }

    return (
        <div
            className={`flex h-full w-full flex-col overflow-hidden ${isEmbedded ? '' : ''}`}
            style={{ background: 'var(--vora-bg-canvas)', color: 'var(--vora-text-primary)', borderTop: isEmbedded ? '1px solid var(--vora-border-subtle)' : 'none' }}
        >

            {contextMenu && (
                <div
                    style={{
                        top: contextMenu.y,
                        left: contextMenu.x,
                        background: 'var(--vora-bg-raised)',
                        border: '1px solid var(--vora-border-strong)',
                        boxShadow: 'var(--vora-shadow-lg)',
                    }}
                    className="fixed z-[9999] w-48 overflow-hidden rounded-xl text-sm"
                >
                    <button
                        type="button"
                        onClick={() => toggleFavorite(contextMenu.channelId)}
                        className="block w-full cursor-pointer px-4 py-2.5 text-left transition-colors hover:bg-white/5"
                        style={{ color: 'var(--vora-text-primary)' }}
                    >
                        {prefs.favoriteChannels.includes(contextMenu.channelId) ? 'Remove favorite' : 'Add to favorites'}
                    </button>
                    <button
                        type="button"
                        onClick={() => hideChannel(contextMenu.channelId)}
                        className="block w-full cursor-pointer px-4 py-2.5 text-left font-medium transition-colors hover:bg-white/5"
                        style={{ color: 'var(--vora-danger-text)' }}
                    >
                        Hide channel
                    </button>
                </div>
            )}

            <div
                className={`relative z-[60] flex shrink-0 flex-wrap items-center justify-between gap-4 px-6 ${isEmbedded ? 'py-2' : 'py-4'}`}
                style={{ background: 'var(--vora-bg-surface)', borderBottom: '1px solid var(--vora-border-subtle)' }}
            >
                {!isEmbedded && <h1 className="m-0 text-2xl font-semibold" style={{ color: 'var(--vora-text-primary)', letterSpacing: '-0.01em' }}>Live TV</h1>}

                <div className="flex w-full flex-wrap items-center gap-2 lg:w-auto">
                    <button
                        type="button"
                        onClick={() => updatePrefs({ ...prefs, hideEmpty: !prefs.hideEmpty })}
                        className="inline-flex cursor-pointer items-center gap-1.5 rounded-full px-3 py-1.5 text-xs font-medium transition-colors"
                        style={{
                            background: prefs.hideEmpty ? 'var(--vora-accent-soft)' : 'rgba(255, 255, 255, 0.04)',
                            color: prefs.hideEmpty ? 'var(--vora-accent-text)' : 'var(--vora-text-secondary)',
                            border: `1px solid ${prefs.hideEmpty ? 'var(--vora-accent-soft-hover)' : 'var(--vora-border-subtle)'}`,
                        }}
                    >
                        {prefs.hideEmpty && (
                            <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3"><polyline points="20 6 9 17 4 12" /></svg>
                        )}
                        {prefs.hideEmpty ? 'Only channels with a guide' : 'Hide channels without a guide'}
                    </button>

                    <div className="relative">
                        <button
                            type="button"
                            onClick={() => setShowRegionMenu(!showRegionMenu)}
                            className="inline-flex min-w-[120px] cursor-pointer items-center justify-between gap-2 rounded-full px-3 py-1.5 text-xs font-medium transition-colors"
                            style={{
                                background: prefs.regions.length > 0 ? 'var(--vora-accent-soft)' : 'rgba(255, 255, 255, 0.04)',
                                color: prefs.regions.length > 0 ? 'var(--vora-accent-text)' : 'var(--vora-text-secondary)',
                                border: `1px solid ${prefs.regions.length > 0 ? 'var(--vora-accent-soft-hover)' : 'var(--vora-border-subtle)'}`,
                            }}
                        >
                            <span>Regions {prefs.regions.length > 0 && `· ${prefs.regions.length}`}</span>
                            <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="6 9 12 15 18 9" /></svg>
                        </button>
                        {showRegionMenu && (
                            <>
                                <div className="fixed inset-0 z-40" onClick={() => setShowRegionMenu(false)} />
                                <div
                                    className="absolute top-full z-50 mt-2 flex max-h-64 w-56 flex-col gap-0.5 overflow-y-auto rounded-xl p-1.5"
                                    style={{ background: 'var(--vora-bg-raised)', border: '1px solid var(--vora-border-strong)', boxShadow: 'var(--vora-shadow-lg)' }}
                                >
                                    <label className="flex cursor-pointer items-center gap-3 rounded-md p-2 text-sm transition-colors hover:bg-white/5" style={{ color: 'var(--vora-text-primary)' }}>
                                        <input type="checkbox" checked={prefs.regions.length === 0} onChange={() => updatePrefs({ ...prefs, regions: [] })} className="h-4 w-4 cursor-pointer accent-[var(--vora-accent-500)]" />
                                        All regions
                                    </label>
                                    <div style={{ height: 1, background: 'var(--vora-border-subtle)', margin: '4px 0' }} />
                                    {regions.map(r => (
                                        <label key={r} className="flex cursor-pointer items-center gap-3 rounded-md p-2 text-sm transition-colors hover:bg-white/5" style={{ color: 'var(--vora-text-primary)' }}>
                                            <input type="checkbox" checked={prefs.regions.includes(r)} onChange={() => toggleArrayItem(r, prefs.regions, 'regions')} className="h-4 w-4 cursor-pointer accent-[var(--vora-accent-500)]" />
                                            {r}
                                        </label>
                                    ))}
                                </div>
                            </>
                        )}
                    </div>

                    <div className="relative">
                        <button
                            type="button"
                            onClick={() => setShowResMenu(!showResMenu)}
                            className="inline-flex min-w-[120px] cursor-pointer items-center justify-between gap-2 rounded-full px-3 py-1.5 text-xs font-medium transition-colors"
                            style={{
                                background: prefs.resolutions.length > 0 ? 'var(--vora-accent-soft)' : 'rgba(255, 255, 255, 0.04)',
                                color: prefs.resolutions.length > 0 ? 'var(--vora-accent-text)' : 'var(--vora-text-secondary)',
                                border: `1px solid ${prefs.resolutions.length > 0 ? 'var(--vora-accent-soft-hover)' : 'var(--vora-border-subtle)'}`,
                            }}
                        >
                            <span>Resolutions {prefs.resolutions.length > 0 && `· ${prefs.resolutions.length}`}</span>
                            <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="6 9 12 15 18 9" /></svg>
                        </button>
                        {showResMenu && (
                            <>
                                <div className="fixed inset-0 z-40" onClick={() => setShowResMenu(false)} />
                                <div
                                    className="absolute top-full z-50 mt-2 flex max-h-64 w-56 flex-col gap-0.5 overflow-y-auto rounded-xl p-1.5"
                                    style={{ background: 'var(--vora-bg-raised)', border: '1px solid var(--vora-border-strong)', boxShadow: 'var(--vora-shadow-lg)' }}
                                >
                                    <label className="flex cursor-pointer items-center gap-3 rounded-md p-2 text-sm transition-colors hover:bg-white/5" style={{ color: 'var(--vora-text-primary)' }}>
                                        <input type="checkbox" checked={prefs.resolutions.length === 0} onChange={() => updatePrefs({ ...prefs, resolutions: [] })} className="h-4 w-4 cursor-pointer accent-[var(--vora-accent-500)]" />
                                        All resolutions
                                    </label>
                                    <div style={{ height: 1, background: 'var(--vora-border-subtle)', margin: '4px 0' }} />
                                    {resolutions.map(r => (
                                        <label key={r} className="flex cursor-pointer items-center gap-3 rounded-md p-2 text-sm transition-colors hover:bg-white/5" style={{ color: 'var(--vora-text-primary)' }}>
                                            <input type="checkbox" checked={prefs.resolutions.includes(r)} onChange={() => toggleArrayItem(r, prefs.resolutions, 'resolutions')} className="h-4 w-4 cursor-pointer accent-[var(--vora-accent-500)]" />
                                            {r}
                                        </label>
                                    ))}
                                </div>
                            </>
                        )}
                    </div>

                    <input
                        type="text"
                        placeholder="Search channels..."
                        value={searchQuery}
                        onChange={(e) => setSearchQuery(e.target.value)}
                        className="flex-1 rounded-full px-4 py-1.5 text-sm outline-none transition-colors lg:w-64 lg:flex-none"
                        style={{
                            background: 'rgba(255, 255, 255, 0.04)',
                            border: '1px solid var(--vora-border-subtle)',
                            color: 'var(--vora-text-primary)',
                        }}
                    />
                </div>
            </div>

            {!isEmbedded && (
                <div
                    className="z-50 flex h-28 shrink-0 items-center gap-6 px-6"
                    style={{ background: 'var(--vora-bg-canvas)', borderBottom: '1px solid var(--vora-border-subtle)' }}
                >
                    {hoveredProgram ? (
                        <>
                            <div
                                className="flex h-16 w-16 shrink-0 items-center justify-center overflow-hidden rounded-full"
                                style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)' }}
                            >
                                {hoveredProgram.channel.logoUrl ? (
                                    <img src={hoveredProgram.channel.logoUrl} alt="" className="max-h-[80%] max-w-[80%] object-contain" />
                                ) : (
                                    <span className="text-[10px]" style={{ color: 'var(--vora-text-disabled)' }}>No logo</span>
                                )}
                            </div>
                            <div className="min-w-0 flex-1">
                                <div className="mb-1 flex items-center gap-3">
                                    <h2 className="m-0 truncate text-lg font-semibold" style={{ color: 'var(--vora-text-primary)' }}>
                                        {hoveredProgram.program?.title || hoveredProgram.channel.name}
                                    </h2>
                                    {hoveredProgram.program && (
                                        <span className="whitespace-nowrap text-sm font-medium" style={{ color: 'var(--vora-accent-text)' }}>
                                            {formatTime(parseDate(hoveredProgram.program.startTime))} – {formatTime(parseDate(hoveredProgram.program.endTime))}
                                        </span>
                                    )}
                                    {hoveredProgram.program?.contentRating && hoveredProgram.program.contentRating !== 'NR' && (
                                        <span className="rounded-md px-1.5 py-0.5 text-[10px] font-semibold" style={{ border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-muted)' }}>
                                            {hoveredProgram.program.contentRating}
                                        </span>
                                    )}
                                </div>
                                <p className="m-0 line-clamp-2 text-sm leading-relaxed" style={{ color: 'var(--vora-text-secondary)' }}>
                                    {hoveredProgram.program?.description || 'No description available for this program.'}
                                </p>
                            </div>
                        </>
                    ) : (
                        <div className="flex h-full items-center text-sm italic" style={{ color: 'var(--vora-text-muted)' }}>
                            Hover over any program to view details…
                        </div>
                    )}
                </div>
            )}

            <div
                className="relative z-50 flex shrink-0 gap-2 overflow-x-auto px-6 py-3"
                style={{ background: 'var(--vora-bg-surface)', borderBottom: '1px solid var(--vora-border-subtle)', scrollbarWidth: 'none' }}
            >
                {categories.map(cat => {
                    const isActive = activeCategory === cat.key;
                    return (
                        <button
                            key={cat.key}
                            type="button"
                            onClick={() => { setActiveCategory(cat.key); setScrollTop(0); scrollContainerRef.current?.scrollTo(0, 0); }}
                            className="inline-flex shrink-0 cursor-pointer items-center gap-1.5 whitespace-nowrap rounded-full px-3.5 py-1.5 text-xs font-medium transition-colors"
                            style={{
                                background: isActive ? 'var(--vora-accent-500)' : 'rgba(255, 255, 255, 0.04)',
                                color: isActive ? 'var(--vora-accent-contrast)' : 'var(--vora-text-secondary)',
                                border: `1px solid ${isActive ? 'var(--vora-accent-500)' : 'var(--vora-border-subtle)'}`,
                            }}
                        >
                            {cat.key === 'Favorites' && (
                                <svg width="12" height="12" viewBox="0 0 20 20" fill="currentColor">
                                    <path fillRule="evenodd" d="M3.172 5.172a4 4 0 015.656 0L10 6.343l1.172-1.171a4 4 0 115.656 5.656L10 17.657l-6.828-6.829a4 4 0 010-5.656z" clipRule="evenodd" />
                                </svg>
                            )}
                            <span>{cat.label}</span>
                            {cat.key !== 'All' && cat.key !== 'Favorites' && (
                                <span
                                    className="rounded-full px-1.5 py-0.5 text-[9px] font-semibold"
                                    style={{
                                        background: isActive ? 'rgba(0,0,0,0.18)' : 'rgba(255,255,255,0.06)',
                                        color: isActive ? 'var(--vora-accent-contrast)' : 'var(--vora-text-muted)',
                                    }}
                                >
                                    {cat.count}
                                </span>
                            )}
                        </button>
                    );
                })}
            </div>

            <div
                ref={scrollContainerRef}
                onScroll={handleScroll}
                className="relative flex-1 overflow-auto"
                style={{ background: 'var(--vora-bg-canvas)' }}
            >
                <div style={{ width: `${256 + timelineWidth}px`, minHeight: '100%' }}>
                    <div
                        className="sticky top-0 z-40 flex h-12"
                        style={{ background: 'var(--vora-bg-surface)', borderBottom: '1px solid var(--vora-border-subtle)' }}
                    >
                        <div
                            className="sticky left-0 z-50 flex w-64 shrink-0 items-center justify-center text-xs font-semibold uppercase tracking-wider"
                            style={{ background: 'var(--vora-bg-surface)', borderRight: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-muted)' }}
                        >
                            Channels
                        </div>
                        <div className="relative flex-1">
                            {timeMarkers.map((time, i) => {
                                const isHourMark = time.getMinutes() === 0;
                                return (
                                    <div
                                        key={i}
                                        className="absolute h-full pl-2 pt-3 text-xs font-medium"
                                        style={{
                                            left: `${(time.getTime() - timelineStart.getTime()) / 60000 * PX_PER_MINUTE}px`,
                                            borderLeft: `1px solid ${isHourMark ? 'var(--vora-border-strong)' : 'var(--vora-border-subtle)'}`,
                                            color: isHourMark ? 'var(--vora-text-secondary)' : 'var(--vora-text-muted)',
                                        }}
                                    >
                                        {formatTime(time)}
                                    </div>
                                );
                            })}
                        </div>
                    </div>

                    <div style={{ height: `${totalHeight}px`, position: 'relative' }}>
                        <div style={{ transform: `translateY(${offsetY}px)`, position: 'absolute', top: 0, left: 0, right: 0 }}>
                            <div
                                className="pointer-events-none absolute bottom-0 top-0 z-30 w-0.5"
                                style={{
                                    left: `${256 + currentTimelineX}px`,
                                    height: `${visibleChannels.length * ROW_HEIGHT}px`,
                                    background: 'var(--vora-danger-500)',
                                    boxShadow: '0 0 12px color-mix(in srgb, var(--vora-danger-500) 70%, transparent)',
                                }}
                            />

                            {visibleChannels.map(channel => {
                                const rawPrograms = guideData[(channel.externalChannelId || '').toLowerCase()] || [];
                                const now = new Date();
                                const sorted = [...rawPrograms].sort((a, b) => new Date(a.startTime.endsWith('Z') ? a.startTime : a.startTime + 'Z').getTime() - new Date(b.startTime.endsWith('Z') ? b.startTime : b.startTime + 'Z').getTime());

                                const cleanPrograms: (IptvProgramDto & { _safeStart: number, _safeEnd: number })[] = [];
                                let lastEnd = 0;

                                for (const p of sorted) {
                                    let pStart = new Date(p.startTime.endsWith('Z') ? p.startTime : p.startTime + 'Z').getTime();
                                    const pEnd = new Date(p.endTime.endsWith('Z') ? p.endTime : p.endTime + 'Z').getTime();
                                    if (pEnd <= lastEnd) continue;
                                    if (pStart < lastEnd) pStart = lastEnd;
                                    if (pEnd > pStart) { cleanPrograms.push({ ...p, _safeStart: pStart, _safeEnd: pEnd }); lastEnd = pEnd; }
                                }

                                const currentProgram = cleanPrograms.find(p => p._safeStart <= now.getTime() && p._safeEnd > now.getTime());
                                const isCurrentlyPlaying = channel.id === currentPlayingChannelId;
                                const isFavorite = prefs.favoriteChannels.includes(channel.externalChannelId);

                                return (
                                    <div
                                        key={channel.id}
                                        onContextMenu={(e) => handleRightClick(e, channel.externalChannelId)}
                                        className="vora-guide-row group flex h-[80px] transition-colors"
                                        style={{
                                            background: isCurrentlyPlaying ? 'rgba(255, 255, 255, 0.04)' : 'transparent',
                                            borderBottom: '1px solid var(--vora-border-subtle)',
                                        }}
                                    >
                                        <div
                                            onClick={() => handlePlayChannel(channel, currentProgram)}
                                            onMouseEnter={() => {
                                                if (onHoverProgram) onHoverProgram(channel, currentProgram || null);
                                                else setHoveredProgram({ channel, program: currentProgram || null });
                                            }}
                                            className="vora-guide-channel sticky left-0 z-20 flex w-64 shrink-0 cursor-pointer items-center gap-3 p-3 transition-colors"
                                            style={{
                                                background: isCurrentlyPlaying ? 'var(--vora-bg-raised)' : 'var(--vora-bg-surface)',
                                                borderLeft: `3px solid ${isCurrentlyPlaying ? 'var(--vora-accent-500)' : 'transparent'}`,
                                                borderRight: '1px solid var(--vora-border-subtle)',
                                            }}
                                        >
                                            <div
                                                className="relative flex h-14 w-14 shrink-0 items-center justify-center overflow-hidden rounded-full"
                                                style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)' }}
                                            >
                                                {channel.logoUrl ? (
                                                    <img src={channel.logoUrl} alt="" className="max-h-[78%] max-w-[78%] object-contain" />
                                                ) : (
                                                    <span className="text-[9px]" style={{ color: 'var(--vora-text-disabled)' }}>No logo</span>
                                                )}
                                                {isFavorite && (
                                                    <span
                                                        className="absolute -top-0.5 -right-0.5 flex h-4 w-4 items-center justify-center rounded-full"
                                                        style={{ background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)' }}
                                                    >
                                                        <svg width="9" height="9" viewBox="0 0 20 20" fill="currentColor"><path fillRule="evenodd" d="M3.172 5.172a4 4 0 015.656 0L10 6.343l1.172-1.171a4 4 0 115.656 5.656L10 17.657l-6.828-6.829a4 4 0 010-5.656z" clipRule="evenodd" /></svg>
                                                    </span>
                                                )}
                                            </div>
                                            <div className="min-w-0 flex-1">
                                                <div className="flex items-center justify-between gap-1.5">
                                                    <h3
                                                        className="m-0 truncate text-sm font-semibold"
                                                        style={{ color: isCurrentlyPlaying ? 'var(--vora-accent-text)' : 'var(--vora-text-primary)' }}
                                                    >
                                                        {channel.name}
                                                    </h3>
                                                    {channel.resolution && channel.resolution !== 'Unknown' && (
                                                        <span
                                                            className="shrink-0 rounded px-1.5 py-0.5 text-[9px] font-bold"
                                                            style={{ background: 'var(--vora-accent-soft)', color: 'var(--vora-accent-text)' }}
                                                        >
                                                            {channel.resolution}
                                                        </span>
                                                    )}
                                                </div>
                                                <div className="mt-0.5 flex items-center gap-2">
                                                    {channel.countryCode && channel.countryCode !== 'Unknown' && (
                                                        <span className="text-[9px] font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>
                                                            {channel.countryCode}
                                                        </span>
                                                    )}
                                                    {channel.groupTitle && (
                                                        <span className="truncate text-[10px] font-medium uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>
                                                            {channel.groupTitle.replace(/;/g, ' • ')}
                                                        </span>
                                                    )}
                                                </div>
                                            </div>
                                        </div>

                                        <div className="flex-1 relative overflow-hidden" style={{ width: `${HOURS_TO_SHOW * 60 * PX_PER_MINUTE}px` }}>
                                            {cleanPrograms.map(program => {
                                                const start = new Date(program._safeStart);
                                                const end = new Date(program._safeEnd);
                                                if (end <= timelineStart || start >= timelineEnd) return null;

                                                const startDiffMins = (start.getTime() - timelineStart.getTime()) / 60000;
                                                const durationMins = (end.getTime() - start.getTime()) / 60000;
                                                let left = startDiffMins * PX_PER_MINUTE;
                                                let width = durationMins * PX_PER_MINUTE;

                                                if (left < 0) { width += left; left = 0; }
                                                if (width <= 0 || isNaN(width) || isNaN(left)) return null;

                                                const isPlayingNow = start <= now && end > now;
                                                const isRestricted = program.title === "Restricted Content";

                                                const isScheduled = recordingSessions.some(s => {
                                                    if (s.status !== 'Pending' && s.status !== 'Recording') return false;

                                                    if (s.externalProgramId && s.externalProgramId === program.id) return true;

                                                    if (s.title !== program.title || s.schedule?.channel?.name !== channel.name) return false;

                                                    const sStart = new Date(s.startTime).getTime();
                                                    const sEnd = new Date(s.endTime).getTime();
                                                    const pStart = new Date(program._safeStart).getTime();
                                                    const pEnd = new Date(program._safeEnd).getTime();

                                                    const overlapStart = Math.max(sStart, pStart);
                                                    const overlapEnd = Math.min(sEnd, pEnd);

                                                    return (overlapEnd - overlapStart) > 360000;
                                                });

                                                const isActivePlayback = isCurrentlyPlaying && isPlayingNow;

                                                let tileBg: string;
                                                let tileBorder: string;
                                                let tileShadow: string;
                                                let tileZ: number;
                                                if (isActivePlayback) {
                                                    tileBg = 'var(--vora-accent-soft)';
                                                    tileBorder = 'var(--vora-accent-500)';
                                                    tileShadow = '0 0 14px color-mix(in srgb, var(--vora-accent-500) 35%, transparent)';
                                                    tileZ = 20;
                                                } else if (isPlayingNow) {
                                                    tileBg = 'var(--vora-bg-raised)';
                                                    tileBorder = 'var(--vora-border-strong)';
                                                    tileShadow = 'var(--vora-shadow-sm)';
                                                    tileZ = 10;
                                                } else {
                                                    tileBg = 'var(--vora-bg-surface)';
                                                    tileBorder = 'var(--vora-border-subtle)';
                                                    tileShadow = 'none';
                                                    tileZ = 0;
                                                }
                                                if (isScheduled) {
                                                    tileBorder = 'var(--vora-danger-500)';
                                                }

                                                return (
                                                    <div
                                                        key={program.id}
                                                        onClick={(e) => {
                                                            e.stopPropagation();
                                                            handlePlayChannel(channel, program);
                                                        }}
                                                        onContextMenu={(e) => {
                                                            e.preventDefault();
                                                            e.stopPropagation();
                                                            setProgramModal({ isOpen: true, channel, program });
                                                        }}
                                                        onMouseEnter={() => {
                                                            if (onHoverProgram) onHoverProgram(channel, program);
                                                            else setHoveredProgram({ channel, program });
                                                        }}
                                                        className={`vora-guide-tile absolute bottom-1 top-1 cursor-pointer overflow-hidden transition-all duration-200 ${isRestricted ? 'opacity-50 grayscale' : ''}`}
                                                        style={{
                                                            left: `${left}px`,
                                                            width: `${width}px`,
                                                            background: tileBg,
                                                            border: `1px solid ${tileBorder}`,
                                                            borderRadius: 'var(--vora-radius-md)',
                                                            boxShadow: tileShadow,
                                                            zIndex: tileZ,
                                                        }}
                                                    >
                                                        <div className="flex h-full flex-col justify-center p-2">
                                                            <div className="flex items-center gap-1.5">
                                                                {isScheduled && (
                                                                    <span
                                                                        className="h-2 w-2 shrink-0 rounded-full"
                                                                        style={{
                                                                            background: 'var(--vora-danger-500)',
                                                                            boxShadow: '0 0 6px color-mix(in srgb, var(--vora-danger-500) 70%, transparent)',
                                                                        }}
                                                                    />
                                                                )}
                                                                <h4
                                                                    className="m-0 truncate text-sm font-semibold"
                                                                    style={{ color: isPlayingNow || isActivePlayback ? 'var(--vora-text-primary)' : 'var(--vora-text-secondary)' }}
                                                                >
                                                                    {program.title}
                                                                </h4>
                                                            </div>
                                                        </div>
                                                    </div>
                                                );
                                            })}
                                        </div>
                                    </div>
                                );
                            })}
                        </div>
                    </div>
                </div>
            </div>

            {/* NEW: Program Action Modal */}
            {programModal && programModal.isOpen && (
                <GuideProgramModal
                    channel={programModal.channel}
                    program={programModal.program}
                    canRecord={canRecord}
                    formatTimeRange={(p) => `${formatTime(parseDate(p.startTime))} - ${formatTime(parseDate(p.endTime))}`}
                    onPlay={() => {
                        handlePlayChannel(programModal.channel, programModal.program);
                        setProgramModal(null);
                    }}
                    onRecordEpisode={() => handleScheduleRecording(programModal.channel, programModal.program, false)}
                    onRecordSeries={(retention) => handleScheduleRecording(programModal.channel, programModal.program, true, retention)}
                    onCancel={() => setProgramModal(null)}
                />
            )}

        </div>
    );
}