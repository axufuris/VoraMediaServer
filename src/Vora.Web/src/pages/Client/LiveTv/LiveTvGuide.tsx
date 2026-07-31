import { useState, useEffect, useMemo, useRef, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { type IptvChannelVM } from '../../../api/Iptv/iptvAdminService';
import { type IptvProgramDto } from '../../../api/Iptv/iptvClientService';
import { dvrService } from '../../../api/Iptv/dvrService';
import { usePlayer } from '../../../contexts/usePlayer';
import { serverVault } from '../../../utils/serverVault';
import { StorageKeys, decodeJwtPayload, getProfileIdFromToken } from '../../../utils/storageKeys';
import { useDialog } from '../../../dialogs';
import GuideProgramModal from '../../../components/Iptv/GuideProgramModal';
import { ROW_HEIGHT, PX_PER_MINUTE, HOURS_TO_SHOW, CHANNEL_COLUMN_WIDTH, parseDate } from './guideConstants';
import { useGuideData } from './hooks/useGuideData';
import { useGuideVirtualization } from './hooks/useGuideVirtualization';
import GuideRow, { type CleanedProgram } from './components/GuideRow';

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
        const token = localStorage.getItem(StorageKeys.profileToken);
        if (!token) return false;
        try {
            const payload = decodeJwtPayload(token);
            return payload?.canRecordLiveTv === 'True';
        } catch {
            return false;
        }
    }, []);

    const [containerWidth, setContainerWidth] = useState(0);

    const timelineStart = useMemo(() => {
        const now = new Date();
        const startMinutes = now.getMinutes() < 30 ? 0 : 30;
        const start = new Date(now.getFullYear(), now.getMonth(), now.getDate(), now.getHours(), startMinutes, 0);
        start.setMinutes(start.getMinutes() - 30);
        return start;
    }, []);

    const hoursToShow = useMemo(() => {
        if (containerWidth <= 0) return HOURS_TO_SHOW;
        const availableForTimeline = containerWidth - CHANNEL_COLUMN_WIDTH;
        const neededHours = Math.ceil(availableForTimeline / (PX_PER_MINUTE * 60));
        return Math.max(HOURS_TO_SHOW, neededHours);
    }, [containerWidth]);

    const { timelineEnd, timeMarkers } = useMemo(() => {
        const end = new Date(timelineStart.getTime() + hoursToShow * 60 * 60 * 1000);
        const markers = [];
        for (let i = 0; i <= hoursToShow * 2; i++) markers.push(new Date(timelineStart.getTime() + i * 30 * 60 * 1000));
        return { timelineEnd: end, timeMarkers: markers };
    }, [timelineStart, hoursToShow]);

    const { channels, guideData, recordingSessions, isLoading, prefs, updatePrefs } = useGuideData(serverId, timelineStart, timelineEnd);
    const [recordingSessionsLocal, setRecordingSessionsLocal] = useState(recordingSessions);
    useEffect(() => { setRecordingSessionsLocal(recordingSessions); }, [recordingSessions]);

    const [hoveredProgram, setHoveredProgram] = useState<{ channel: IptvChannelVM, program: IptvProgramDto | null } | null>(null);
    const [showRegionMenu, setShowRegionMenu] = useState(false);
    const [showResMenu, setShowResMenu] = useState(false);
    const [searchQuery, setSearchQuery] = useState('');
    const [activeCategory, setActiveCategory] = useState<string>('All');
    const [currentTimelineX, setCurrentTimelineX] = useState<number>(0);

    const [contextMenu, setContextMenu] = useState<{ x: number, y: number, channelId: string } | null>(null);
    const [programModal, setProgramModal] = useState<{ isOpen: boolean, channel: IptvChannelVM, program: IptvProgramDto } | null>(null);
    const [hasAutoScrolled, setHasAutoScrolled] = useState(false);

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

    const categoryRowRef = useRef<HTMLDivElement>(null);
    const [catCanLeft, setCatCanLeft] = useState(false);
    const [catCanRight, setCatCanRight] = useState(false);

    const updateCatArrows = useCallback(() => {
        const el = categoryRowRef.current;
        if (!el) return;
        setCatCanLeft(el.scrollLeft > 4);
        setCatCanRight(el.scrollLeft + el.clientWidth < el.scrollWidth - 4);
    }, []);

    useEffect(() => {
        const el = categoryRowRef.current;
        if (!el) return;
        updateCatArrows();
        const onWheel = (e: WheelEvent) => {
            if (e.deltaY === 0) return;
            e.preventDefault();
            el.scrollLeft += e.deltaY;
            updateCatArrows();
        };
        el.addEventListener('wheel', onWheel, { passive: false });
        const observer = new ResizeObserver(updateCatArrows);
        observer.observe(el);
        return () => { el.removeEventListener('wheel', onWheel); observer.disconnect(); };
    }, [updateCatArrows, categories.length]);

    const scrollCategories = (direction: number) => {
        const el = categoryRowRef.current;
        if (!el) return;
        el.scrollBy({ left: direction * Math.max(240, el.clientWidth * 0.7), behavior: 'smooth' });
    };

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

        const now = Date.now();
        const parseProgramTime = (t: string): number => new Date(t.endsWith('Z') ? t : t + 'Z').getTime();
        const hasCurrentProgram = (externalChannelId: string): boolean => {
            const programs = guideData[(externalChannelId || '').toLowerCase()] || [];
            return programs.some(p => {
                const start = parseProgramTime(p.startTime);
                const end = parseProgramTime(p.endTime);
                return start <= now && now < end;
            });
        };

        return filtered.sort((a, b) => {
            const aFav = prefs.favoriteChannels.includes(a.externalChannelId);
            const bFav = prefs.favoriteChannels.includes(b.externalChannelId);
            if (aFav && !bFav) return -1;
            if (!aFav && bFav) return 1;

            const aHasCurrent = hasCurrentProgram(a.externalChannelId);
            const bHasCurrent = hasCurrentProgram(b.externalChannelId);
            if (aHasCurrent && !bHasCurrent) return -1;
            if (!aHasCurrent && bHasCurrent) return 1;

            const aHasPrograms = (guideData[(a.externalChannelId || '').toLowerCase()] || []).length > 0;
            const bHasPrograms = (guideData[(b.externalChannelId || '').toLowerCase()] || []).length > 0;
            if (aHasPrograms && !bHasPrograms) return -1;
            if (!aHasPrograms && bHasPrograms) return 1;
            return a.name.localeCompare(b.name);
        });
    }, [channels, activeCategory, prefs, searchQuery, guideData]);

    const { setScrollTop, scrollContainerRef, handleScroll, startIndex, endIndex, offsetY, totalHeight, visibleCount } = useGuideVirtualization(filteredChannels.length);

    useEffect(() => {
        const el = scrollContainerRef.current;
        if (!el) return;
        const measure = () => setContainerWidth(el.clientWidth);
        measure();
        const observer = new ResizeObserver(measure);
        observer.observe(el);
        return () => observer.disconnect();
    }, [isLoading, scrollContainerRef]);

    useEffect(() => {
        if (!hasAutoScrolled && !isLoading && filteredChannels.length > 0 && currentPlayingChannelId && scrollContainerRef.current) {
            const activeIndex = filteredChannels.findIndex(c => c.id === currentPlayingChannelId);

            if (activeIndex !== -1) {
                const containerHeight = scrollContainerRef.current.clientHeight;
                const targetY = Math.max(0, (activeIndex * ROW_HEIGHT) - (containerHeight / 2) + (ROW_HEIGHT / 2));

                const targetX = Math.max(0, currentTimelineX - 100);

                scrollContainerRef.current.scrollTo({ top: targetY, left: targetX, behavior: 'smooth' });

                setScrollTop(targetY);
                setHasAutoScrolled(true);
            }
        }
    }, [filteredChannels, currentPlayingChannelId, isLoading, hasAutoScrolled, currentTimelineX, scrollContainerRef, setScrollTop]);

    const cleanedGuideData = useMemo(() => {
        const cleaned = new Map<string, CleanedProgram[]>();
        for (const [channelKey, rawPrograms] of Object.entries(guideData)) {
            const sorted = [...rawPrograms].sort((a, b) => new Date(a.startTime.endsWith('Z') ? a.startTime : a.startTime + 'Z').getTime() - new Date(b.startTime.endsWith('Z') ? b.startTime : b.startTime + 'Z').getTime());
            const cleanPrograms: CleanedProgram[] = [];
            let lastEnd = 0;
            for (const p of sorted) {
                let pStart = new Date(p.startTime.endsWith('Z') ? p.startTime : p.startTime + 'Z').getTime();
                const pEnd = new Date(p.endTime.endsWith('Z') ? p.endTime : p.endTime + 'Z').getTime();
                if (pEnd <= lastEnd) continue;
                if (pStart < lastEnd) pStart = lastEnd;
                if (pEnd > pStart) { cleanPrograms.push({ ...p, _safeStart: pStart, _safeEnd: pEnd }); lastEnd = pEnd; }
            }
            cleaned.set(channelKey, cleanPrograms);
        }
        return cleaned;
    }, [guideData]);

    const visibleChannels = filteredChannels.slice(startIndex, endIndex);
    const timelineWidth = hoursToShow * 60 * PX_PER_MINUTE;

    const handlePlayChannel = (channel: IptvChannelVM, program?: IptvProgramDto) => {
        if (onPlayChannel) { onPlayChannel(channel, program); return; }
        playMedia({ id: channel.id, title: channel.name, subtitle: program ? program.title : 'Live TV', posterUrl: channel.logoUrl, streamUrl: channel.streamUrl, serverId: serverId ?? undefined, container: 'hls', playbackContextType: 'LiveTv' });
    };

    const handleScheduleRecording = async (channel: IptvChannelVM, program: IptvProgramDto, isSeries: boolean, keepMaxEpisodes: number = 0) => {
        try {
            const activeServer = serverVault.getActiveServer();
            if (!activeServer) return;
            const profileToken = localStorage.getItem(StorageKeys.profileToken);
            const activeProfileId = getProfileIdFromToken(profileToken) ?? activeServer.profileId;

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
            setRecordingSessionsLocal(newSessions);

            await dialog.alert({ title: "Success", message: "Recording scheduled successfully!" });
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

    const handleHoverProgram = (channel: IptvChannelVM, program: IptvProgramDto | null) => {
        if (onHoverProgram) onHoverProgram(channel, program);
        else setHoveredProgram({ channel, program });
    };

    const handleProgramContextMenu = (channel: IptvChannelVM, program: IptvProgramDto) => {
        setProgramModal({ isOpen: true, channel, program });
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
                className="relative z-50 shrink-0"
                style={{ background: 'var(--vora-bg-surface)', borderBottom: '1px solid var(--vora-border-subtle)' }}
            >
                {catCanLeft && (
                    <button
                        type="button"
                        aria-label="Scroll categories left"
                        onClick={() => scrollCategories(-1)}
                        className="absolute left-1 top-1/2 z-10 flex h-8 w-8 -translate-y-1/2 cursor-pointer items-center justify-center rounded-full"
                        style={{ background: 'var(--vora-bg-raised)', border: '1px solid var(--vora-border-strong)', boxShadow: 'var(--vora-shadow-md)', color: 'var(--vora-text-primary)' }}
                    >
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><polyline points="15 18 9 12 15 6" /></svg>
                    </button>
                )}
                {catCanRight && (
                    <button
                        type="button"
                        aria-label="Scroll categories right"
                        onClick={() => scrollCategories(1)}
                        className="absolute right-1 top-1/2 z-10 flex h-8 w-8 -translate-y-1/2 cursor-pointer items-center justify-center rounded-full"
                        style={{ background: 'var(--vora-bg-raised)', border: '1px solid var(--vora-border-strong)', boxShadow: 'var(--vora-shadow-md)', color: 'var(--vora-text-primary)' }}
                    >
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><polyline points="9 18 15 12 9 6" /></svg>
                    </button>
                )}
                <div
                    ref={categoryRowRef}
                    onScroll={updateCatArrows}
                    className="flex gap-2 overflow-x-auto px-6 py-3"
                    style={{ scrollbarWidth: 'none' }}
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
                                    height: `${visibleCount * ROW_HEIGHT}px`,
                                    background: 'var(--vora-danger-500)',
                                    boxShadow: '0 0 12px color-mix(in srgb, var(--vora-danger-500) 70%, transparent)',
                                }}
                            />

                            {visibleChannels.map(channel => {
                                const cleanPrograms = cleanedGuideData.get((channel.externalChannelId || '').toLowerCase()) || [];
                                const isCurrentlyPlaying = channel.id === currentPlayingChannelId;
                                const isFavorite = prefs.favoriteChannels.includes(channel.externalChannelId);

                                return (
                                    <GuideRow
                                        key={channel.id}
                                        channel={channel}
                                        cleanPrograms={cleanPrograms}
                                        timelineStart={timelineStart}
                                        timelineEnd={timelineEnd}
                                        recordingSessions={recordingSessionsLocal}
                                        isCurrentlyPlaying={isCurrentlyPlaying}
                                        isFavorite={isFavorite}
                                        onPlay={handlePlayChannel}
                                        onHover={handleHoverProgram}
                                        onRightClick={handleRightClick}
                                        onProgramContextMenu={handleProgramContextMenu}
                                    />
                                );
                            })}
                        </div>
                    </div>
                </div>
            </div>

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
