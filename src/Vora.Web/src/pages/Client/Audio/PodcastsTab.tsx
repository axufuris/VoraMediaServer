import { useEffect, useState, useCallback, useRef, useMemo } from 'react';
import { useParams } from 'react-router-dom';
import { podcastService, type PodcastSubscriptionVM, type PodcastEpisodeVM, type DiscoveredPodcastVM, type PodcastFeedEpisodeVM, type AggregatedCatalogPodcastVM, type CatalogServerAvailability } from '../../../api/Podcasts/podcastService';
import { serverVault } from '../../../utils/serverVault';
import { usePlayer } from '../../../contexts/usePlayer';
import { useDialog } from '../../../dialogs';
import { useSignalREvent } from '../../../hooks/useSignalREvent';

const SELECTED_SUB_STORAGE_KEY = 'podcast_selected_sub';
const VIEW_MODE_STORAGE_KEY = 'podcast_view_mode';

type PodcastViewMode = 'subscriptions' | 'recent' | 'catalog';

interface EpisodeStateChange {
    episodeId: string;
    positionSeconds: number;
    isPlayed: boolean;
}

export default function PodcastsTab() {
    const { serverId } = useParams<{ serverId?: string }>();
    const { playMedia } = usePlayer();
    const dialog = useDialog();

    const [subscriptions, setSubscriptions] = useState<PodcastSubscriptionVM[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [searchQuery, setSearchQuery] = useState('');
    const [searchResults, setSearchResults] = useState<DiscoveredPodcastVM[]>([]);
    const [isSearching, setIsSearching] = useState(false);
    const [hasSearched, setHasSearched] = useState(false);
    const [subscribingFeedUrl, setSubscribingFeedUrl] = useState<string | null>(null);
    const [subscribeError, setSubscribeError] = useState<string | null>(null);

    const [selectedSub, setSelectedSub] = useState<PodcastSubscriptionVM | null>(null);
    const [episodes, setEpisodes] = useState<PodcastEpisodeVM[]>([]);
    const [isLoadingEpisodes, setIsLoadingEpisodes] = useState(false);
    const [isRefreshing, setIsRefreshing] = useState(false);

    const canAddCustomFeeds = useMemo(() => {
        const token = localStorage.getItem('profile_token');
        if (!token) return false;
        try {
            const payload = JSON.parse(atob(token.split('.')[1]));
            return payload.canAddCustomPodcastFeeds === 'True';
        } catch {
            return false;
        }
    }, []);

    const [viewMode, setViewMode] = useState<PodcastViewMode>(() => {
        const stored = sessionStorage.getItem(VIEW_MODE_STORAGE_KEY);
        if (stored === 'recent' || stored === 'subscriptions' || stored === 'catalog') return stored;
        return canAddCustomFeeds ? 'subscriptions' : 'catalog';
    });
    const [recentEpisodes, setRecentEpisodes] = useState<PodcastFeedEpisodeVM[]>([]);
    const [isLoadingRecent, setIsLoadingRecent] = useState(false);

    const [catalog, setCatalog] = useState<AggregatedCatalogPodcastVM[]>([]);
    const [catalogFailedServerIds, setCatalogFailedServerIds] = useState<string[]>([]);
    const [isLoadingCatalog, setIsLoadingCatalog] = useState(false);
    const [catalogDropdownFor, setCatalogDropdownFor] = useState<string | null>(null);

    const loadCatalog = useCallback(async () => {
        setIsLoadingCatalog(true);
        try {
            const result = await podcastService.getAggregatedCatalog();
            setCatalog(result.items);
            setCatalogFailedServerIds(result.failedServerIds);
        } catch (error) {
            console.error('Failed to load catalog', error);
        } finally {
            setIsLoadingCatalog(false);
        }
    }, []);

    useEffect(() => {
        if (viewMode === 'catalog') {
            loadCatalog();
        }
    }, [viewMode, loadCatalog]);

    const [contextMenu, setContextMenu] = useState<{ x: number; y: number; episodeId: string; isPlayed: boolean; hasProgress: boolean } | null>(null);

    useEffect(() => {
        const close = () => setContextMenu(null);
        document.addEventListener('click', close);
        return () => document.removeEventListener('click', close);
    }, []);

    useEffect(() => {
        const close = () => setCatalogDropdownFor(null);
        document.addEventListener('click', close);
        return () => document.removeEventListener('click', close);
    }, []);

    const applyEpisodeStateChange = useCallback(async (episodeId: string, positionSeconds: number, isPlayed: boolean) => {
        try {
            await podcastService.saveEpisodeState(episodeId, positionSeconds, isPlayed, serverId);
            window.dispatchEvent(new CustomEvent<EpisodeStateChange>('podcast:episode-state-changed', {
                detail: { episodeId, positionSeconds, isPlayed }
            }));
        } catch (error) {
            console.error("Failed to update episode state", error);
            dialog.alert("Failed to update episode.");
        }
    }, [serverId, dialog]);

    const handleMarkPlayed = (episodeId: string) => applyEpisodeStateChange(episodeId, 0, true);
    const handleMarkUnplayed = (episodeId: string) => applyEpisodeStateChange(episodeId, 0, false);

    const subscribeFromCatalog = async (feedUrl: string, targetServerId: string) => {
        setSubscribingFeedUrl(feedUrl);
        setCatalogDropdownFor(null);
        try {
            await podcastService.subscribe(feedUrl, targetServerId);
            setCatalog(prev => prev.map(c => c.feedUrl.toLowerCase() === feedUrl.toLowerCase() ? {
                ...c,
                availableOn: c.availableOn.map(a => a.serverId === targetServerId ? { ...a, isSubscribed: true } : a)
            } : c));
            if (targetServerId === (serverId ?? serverVault.getActiveServerId())) {
                await loadSubscriptions();
            }
        } catch (error) {
            console.error('Failed to subscribe from catalog', error);
            await dialog.alert('Failed to subscribe.');
        } finally {
            setSubscribingFeedUrl(null);
        }
    };

    const pickDefaultTargetServer = (item: AggregatedCatalogPodcastVM): CatalogServerAvailability | null => {
        const activeId = serverId ?? serverVault.getActiveServerId() ?? null;
        const unsubscribed = item.availableOn.filter(a => !a.isSubscribed);
        if (unsubscribed.length === 0) return null;
        const activeMatch = unsubscribed.find(a => a.serverId === activeId);
        if (activeMatch) return activeMatch;
        return unsubscribed[0];
    };

    const restoredSubIdRef = useRef<string | null>(sessionStorage.getItem(SELECTED_SUB_STORAGE_KEY));

    const loadRecentEpisodes = useCallback(async () => {
        setIsLoadingRecent(true);
        try {
            const eps = await podcastService.getRecentEpisodes(50, 60, serverId);
            setRecentEpisodes(eps);
        } catch (error) {
            console.error("Failed to load recent episodes", error);
        } finally {
            setIsLoadingRecent(false);
        }
    }, [serverId]);

    useEffect(() => {
        if (viewMode === 'recent') {
            loadRecentEpisodes();
        }
    }, [viewMode, loadRecentEpisodes]);

    const switchViewMode = (mode: PodcastViewMode) => {
        setViewMode(mode);
        sessionStorage.setItem(VIEW_MODE_STORAGE_KEY, mode);
    };

    const loadSubscriptions = useCallback(async () => {
        try {
            const subs = await podcastService.getSubscriptions(serverId);
            setSubscriptions(subs);
        } catch (error) {
            console.error("Failed to load podcast subscriptions", error);
        } finally {
            setIsLoading(false);
        }
    }, [serverId]);

    useEffect(() => {
        loadSubscriptions();
    }, [loadSubscriptions]);

    const loadEpisodes = useCallback(async (subscription: PodcastSubscriptionVM) => {
        setSelectedSub(subscription);
        sessionStorage.setItem(SELECTED_SUB_STORAGE_KEY, subscription.id);
        setIsLoadingEpisodes(true);
        setEpisodes([]);
        try {
            const eps = await podcastService.getEpisodes(subscription.id, 100, serverId);
            setEpisodes(eps);
        } catch (error) {
            console.error("Failed to load episodes", error);
        } finally {
            setIsLoadingEpisodes(false);
        }
    }, [serverId]);

    useEffect(() => {
        const restoredId = restoredSubIdRef.current;
        if (!restoredId || subscriptions.length === 0 || selectedSub) return;
        const match = subscriptions.find(s => s.id === restoredId);
        if (match) {
            loadEpisodes(match);
        } else {
            sessionStorage.removeItem(SELECTED_SUB_STORAGE_KEY);
        }
        restoredSubIdRef.current = null;
    }, [subscriptions, selectedSub, loadEpisodes]);

    useEffect(() => {
        const handler = (e: Event) => {
            const detail = (e as CustomEvent<EpisodeStateChange>).detail;
            if (!detail) return;
            setEpisodes(prev => prev.map(ep =>
                ep.id === detail.episodeId
                    ? { ...ep, positionSeconds: detail.positionSeconds, isPlayed: detail.isPlayed }
                    : ep
            ));
            setRecentEpisodes(prev => prev.map(ep =>
                ep.id === detail.episodeId
                    ? { ...ep, positionSeconds: detail.positionSeconds, isPlayed: detail.isPlayed }
                    : ep
            ));
        };
        window.addEventListener('podcast:episode-state-changed', handler);
        return () => window.removeEventListener('podcast:episode-state-changed', handler);
    }, []);

    useSignalREvent<string>("PodcastEpisodesUpdated", useCallback((showId: string) => {
        if (!showId) return;
        loadSubscriptions();
        if (selectedSub && selectedSub.showId === showId) {
            podcastService.getEpisodes(selectedSub.id, 100, serverId)
                .then(setEpisodes)
                .catch(err => console.error("Failed to refresh episodes from SignalR", err));
        }
        if (viewMode === 'recent') {
            loadRecentEpisodes();
        }
        if (viewMode === 'catalog') {
            loadCatalog();
        }
    }, [selectedSub, serverId, loadSubscriptions, viewMode, loadRecentEpisodes, loadCatalog]));

    const isUrlLike = (value: string): boolean => /^(https?:|feed:)/i.test(value.trim());

    const subscribeToFeed = async (feedUrl: string) => {
        const url = feedUrl.trim();
        if (!url) return;

        setSubscribingFeedUrl(url);
        setSubscribeError(null);
        try {
            await podcastService.subscribe(url, serverId);
            await loadSubscriptions();
            if (isUrlLike(searchQuery) && searchQuery.trim() === url) {
                setSearchQuery('');
            }
            setSearchResults(prev => prev.filter(r => r.feedUrl.toLowerCase() !== url.toLowerCase()));
        } catch (error: unknown) {
            const message = error instanceof Error ? error.message : "Failed to subscribe.";
            setSubscribeError(message);
        } finally {
            setSubscribingFeedUrl(null);
        }
    };

    useEffect(() => {
        const trimmed = searchQuery.trim();
        if (!trimmed || isUrlLike(trimmed)) {
            setSearchResults([]);
            setHasSearched(false);
            return;
        }

        let cancelled = false;
        setIsSearching(true);
        const handle = setTimeout(async () => {
            try {
                const results = await podcastService.search(trimmed, 25, serverId);
                if (!cancelled) {
                    setSearchResults(results);
                    setHasSearched(true);
                }
            } catch (error) {
                if (!cancelled) {
                    console.error('Podcast search failed', error);
                    setSearchResults([]);
                    setHasSearched(true);
                }
            } finally {
                if (!cancelled) setIsSearching(false);
            }
        }, 400);

        return () => {
            cancelled = true;
            clearTimeout(handle);
            setIsSearching(false);
        };
    }, [searchQuery, serverId]);


    const handleUnsubscribe = async (sub: PodcastSubscriptionVM) => {
        const confirmed = await dialog.confirm(`Unsubscribe from "${sub.title}"?`);
        if (!confirmed) return;

        try {
            await podcastService.unsubscribe(sub.id, serverId);
            if (selectedSub?.id === sub.id) {
                setSelectedSub(null);
                setEpisodes([]);
                sessionStorage.removeItem(SELECTED_SUB_STORAGE_KEY);
            }
            await loadSubscriptions();
        } catch (error) {
            console.error("Failed to unsubscribe", error);
            await dialog.alert("Failed to unsubscribe.");
        }
    };

    const handleRefresh = async () => {
        if (!selectedSub) return;
        setIsRefreshing(true);
        try {
            await podcastService.refreshSubscription(selectedSub.id, serverId);
            const eps = await podcastService.getEpisodes(selectedSub.id, 100, serverId);
            setEpisodes(eps);
            await loadSubscriptions();
        } catch (error) {
            console.error("Failed to refresh subscription", error);
            await dialog.alert("Failed to refresh feed.");
        } finally {
            setIsRefreshing(false);
        }
    };

    const handlePlay = (episode: PodcastEpisodeVM) => {
        if (!selectedSub) return;
        const resumeFrom = episode.isPlayed ? 0 : (episode.positionSeconds > 5 ? episode.positionSeconds : 0);
        playMedia({
            id: episode.id,
            title: episode.title,
            subtitle: selectedSub.title,
            posterUrl: episode.artworkUrl || selectedSub.artworkUrl,
            streamUrl: episode.audioUrl,
            serverId: serverId ?? serverVault.getActiveServerId() ?? undefined,
            container: 'audio',
            playbackContextType: 'Podcast',
            startPosition: resumeFrom
        });
    };

    const handlePlayRecent = (episode: PodcastFeedEpisodeVM) => {
        const resumeFrom = episode.isPlayed ? 0 : (episode.positionSeconds > 5 ? episode.positionSeconds : 0);
        playMedia({
            id: episode.id,
            title: episode.title,
            subtitle: episode.showTitle,
            posterUrl: episode.artworkUrl || episode.showArtworkUrl,
            streamUrl: episode.audioUrl,
            serverId: serverId ?? serverVault.getActiveServerId() ?? undefined,
            container: 'audio',
            playbackContextType: 'Podcast',
            startPosition: resumeFrom
        });
    };

    const formatDuration = (seconds?: number): string => {
        if (!seconds || seconds <= 0) return '';
        const h = Math.floor(seconds / 3600);
        const m = Math.floor((seconds % 3600) / 60);
        const s = seconds % 60;
        if (h > 0) return `${h}h ${m}m`;
        if (m > 0) return `${m}m`;
        return `${s}s`;
    };

    const formatPublished = (iso?: string): string => {
        if (!iso) return '';
        const date = new Date(iso);
        const now = new Date();
        const diffMs = now.getTime() - date.getTime();
        const diffDays = Math.floor(diffMs / (1000 * 60 * 60 * 24));
        if (diffDays === 0) return 'Today';
        if (diffDays === 1) return 'Yesterday';
        if (diffDays < 7) return `${diffDays} days ago`;
        if (diffDays < 30) return `${Math.floor(diffDays / 7)}w ago`;
        if (diffDays < 365) return `${Math.floor(diffDays / 30)}mo ago`;
        return `${Math.floor(diffDays / 365)}y ago`;
    };

    const trimmedQuery = searchQuery.trim();
    const queryIsUrl = isUrlLike(trimmedQuery);
    const isSubscribingDirect = subscribingFeedUrl !== null && trimmedQuery === subscribingFeedUrl;

    return (
        <div>
            {contextMenu && (
                <div
                    style={{ top: contextMenu.y, left: contextMenu.x }}
                    className="fixed z-[9999] bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-md shadow-2xl py-1 w-52 text-sm"
                    onClick={e => e.stopPropagation()}
                >
                    {!contextMenu.isPlayed && (
                        <button
                            onClick={() => { handleMarkPlayed(contextMenu.episodeId); setContextMenu(null); }}
                            className="w-full text-left px-4 py-2 hover:bg-[var(--vora-bg-surface)] text-[var(--vora-text-primary)]"
                        >
                            Mark as played
                        </button>
                    )}
                    {(contextMenu.isPlayed || contextMenu.hasProgress) && (
                        <button
                            onClick={() => { handleMarkUnplayed(contextMenu.episodeId); setContextMenu(null); }}
                            className="w-full text-left px-4 py-2 hover:bg-[var(--vora-bg-surface)] text-[var(--vora-text-primary)]"
                        >
                            {contextMenu.isPlayed ? 'Mark as unplayed' : 'Reset progress'}
                        </button>
                    )}
                </div>
            )}

            {canAddCustomFeeds && (
            <div className="bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg p-4 mb-8">
                <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-accent-text)] mb-2">Discover podcasts</label>
                <div className="flex gap-3">
                    <input
                        type="text"
                        placeholder="Search by name or paste an RSS feed URL..."
                        value={searchQuery}
                        onChange={e => setSearchQuery(e.target.value)}
                        onKeyDown={e => { if (e.key === 'Enter' && queryIsUrl) subscribeToFeed(trimmedQuery); }}
                        className="flex-1 bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded p-3 text-[var(--vora-text-primary)] outline-none focus:border-[var(--vora-accent-500)]"
                    />
                    {queryIsUrl && (
                        <button
                            onClick={() => subscribeToFeed(trimmedQuery)}
                            disabled={isSubscribingDirect || !trimmedQuery}
                            className="px-6 py-2 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-500)] disabled:opacity-50 text-[var(--vora-text-primary)] font-bold rounded transition-colors cursor-pointer"
                        >
                            {isSubscribingDirect ? 'Adding...' : 'Subscribe'}
                        </button>
                    )}
                </div>
                {subscribeError && (
                    <p className="mt-2 text-sm text-[var(--vora-danger-text)]">{subscribeError}</p>
                )}

                {!queryIsUrl && trimmedQuery.length > 0 && (
                    <div className="mt-4">
                        {isSearching ? (
                            <div className="text-sm text-[var(--vora-text-muted)] py-3">Searching iTunes...</div>
                        ) : searchResults.length === 0 && hasSearched ? (
                            <div className="text-sm text-[var(--vora-text-muted)] py-3">No matches.</div>
                        ) : searchResults.length > 0 ? (
                            <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 max-h-96 overflow-y-auto pr-1">
                                {searchResults.map(result => {
                                    const isAdding = subscribingFeedUrl === result.feedUrl;
                                    return (
                                        <div
                                            key={result.feedUrl}
                                            className="flex items-center gap-3 p-2 bg-[var(--vora-bg-canvas)]/60 border border-[var(--vora-border-subtle)] rounded hover:border-[var(--vora-border-subtle)] transition-colors"
                                        >
                                            <div className="w-12 h-12 rounded bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] flex items-center justify-center overflow-hidden shrink-0">
                                                {result.artworkUrl
                                                    ? <img src={result.artworkUrl} alt={result.title} className="max-w-full max-h-full object-cover" />
                                                    : <svg className="w-6 h-6 text-[var(--vora-text-disabled)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 1a9 9 0 00-9 9v7c0 1.66 1.34 3 3 3h3v-8H5v-2a7 7 0 1114 0v2h-4v8h3c1.66 0 3-1.34 3-3v-7a9 9 0 00-9-9z" /></svg>}
                                            </div>
                                            <div className="flex-1 min-w-0">
                                                <div className="font-bold text-sm text-[var(--vora-text-primary)] truncate" title={result.title}>{result.title}</div>
                                                <div className="text-xs text-[var(--vora-text-muted)] truncate">{result.author || 'Unknown'}</div>
                                            </div>
                                            <button
                                                onClick={() => subscribeToFeed(result.feedUrl)}
                                                disabled={isAdding}
                                                className="text-xs px-3 py-1.5 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-500)] disabled:opacity-50 text-[var(--vora-text-primary)] font-bold rounded transition-colors cursor-pointer shrink-0"
                                            >
                                                {isAdding ? 'Adding...' : 'Subscribe'}
                                            </button>
                                        </div>
                                    );
                                })}
                            </div>
                        ) : null}
                    </div>
                )}
            </div>
            )}

            {isLoading ? (
                <div className="text-[var(--vora-text-muted)] py-12 text-center">Loading subscriptions...</div>
            ) : (
                <>
                    <div className="flex items-center gap-2 mb-6">
                        <button
                            onClick={() => switchViewMode('subscriptions')}
                            className={`px-4 py-2 text-sm font-bold rounded transition-colors cursor-pointer ${viewMode === 'subscriptions' ? 'bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)]' : 'bg-[var(--vora-bg-sunken)]/60 text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)] hover:bg-[var(--vora-bg-surface)]'}`}
                        >
                            Subscriptions
                        </button>
                        <button
                            onClick={() => switchViewMode('recent')}
                            className={`px-4 py-2 text-sm font-bold rounded transition-colors cursor-pointer ${viewMode === 'recent' ? 'bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)]' : 'bg-[var(--vora-bg-sunken)]/60 text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)] hover:bg-[var(--vora-bg-surface)]'}`}
                        >
                            New Episodes
                        </button>
                        <button
                            onClick={() => switchViewMode('catalog')}
                            className={`px-4 py-2 text-sm font-bold rounded transition-colors cursor-pointer ${viewMode === 'catalog' ? 'bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)]' : 'bg-[var(--vora-bg-sunken)]/60 text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)] hover:bg-[var(--vora-bg-surface)]'}`}
                        >
                            Browse Catalog
                        </button>
                        {viewMode === 'recent' && (
                            <button
                                onClick={loadRecentEpisodes}
                                disabled={isLoadingRecent}
                                className="ml-auto text-xs px-3 py-1.5 bg-[var(--vora-bg-surface)] hover:bg-[var(--vora-bg-raised)] disabled:opacity-50 text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer"
                            >
                                {isLoadingRecent ? 'Refreshing...' : 'Refresh'}
                            </button>
                        )}
                    </div>

                {viewMode === 'recent' ? (
                    isLoadingRecent ? (
                        <div className="text-[var(--vora-text-muted)] py-12 text-center">Loading new episodes...</div>
                    ) : recentEpisodes.length === 0 ? (
                        <div className="text-[var(--vora-text-muted)] py-12 text-center">
                            <p>No new episodes in the last 60 days.</p>
                        </div>
                    ) : (
                        <div className="space-y-2">
                            {recentEpisodes.map(ep => {
                                const hasProgress = !ep.isPlayed && ep.positionSeconds > 5;
                                const progressPct = hasProgress && ep.durationSeconds
                                    ? Math.min(100, (ep.positionSeconds / ep.durationSeconds) * 100)
                                    : 0;
                                const remainingSecs = hasProgress && ep.durationSeconds
                                    ? Math.max(0, ep.durationSeconds - ep.positionSeconds)
                                    : 0;
                                return (
                                    <button
                                        key={ep.id}
                                        onClick={() => handlePlayRecent(ep)}
                                        onContextMenu={(e) => {
                                            e.preventDefault();
                                            setContextMenu({ x: e.pageX, y: e.pageY, episodeId: ep.id, isPlayed: ep.isPlayed, hasProgress: !ep.isPlayed && ep.positionSeconds > 5 });
                                        }}
                                        className={`w-full text-left flex items-center gap-4 p-3 bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-sunken)]/80 border rounded transition-all cursor-pointer group ${ep.isPlayed ? 'border-[var(--vora-border-subtle)]/50 opacity-60' : 'border-[var(--vora-border-subtle)] hover:border-[var(--vora-accent-500)]'}`}
                                    >
                                        <div className="w-14 h-14 rounded bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] flex items-center justify-center overflow-hidden shrink-0">
                                            {ep.showArtworkUrl || ep.artworkUrl
                                                ? <img src={ep.artworkUrl || ep.showArtworkUrl} alt={ep.showTitle} className="max-w-full max-h-full object-cover" />
                                                : <svg className="w-6 h-6 text-[var(--vora-text-disabled)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 1a9 9 0 00-9 9v7c0 1.66 1.34 3 3 3h3v-8H5v-2a7 7 0 1114 0v2h-4v8h3c1.66 0 3-1.34 3-3v-7a9 9 0 00-9-9z" /></svg>}
                                        </div>
                                        {ep.isPlayed ? (
                                            <svg className="w-6 h-6 text-[var(--vora-success-text)] shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" /></svg>
                                        ) : (
                                            <svg className="w-6 h-6 text-[var(--vora-accent-text)] shrink-0 group-hover:scale-110 transition-transform" fill="currentColor" viewBox="0 0 24 24"><path d="M8 5v14l11-7z" /></svg>
                                        )}
                                        <div className="flex-1 min-w-0">
                                            <div className="text-xs text-[var(--vora-accent-text)] font-bold uppercase tracking-wider truncate">{ep.showTitle}</div>
                                            <div className={`font-bold text-sm truncate ${ep.isPlayed ? 'text-[var(--vora-text-secondary)] line-through decoration-gray-600' : 'text-[var(--vora-text-primary)] group-hover:text-[var(--vora-text-primary)]'}`}>{ep.title}</div>
                                            <div className="text-xs text-[var(--vora-text-muted)] mt-0.5">
                                                {formatPublished(ep.publishedAt)}
                                                {ep.durationSeconds ? ` • ${formatDuration(ep.durationSeconds)}` : ''}
                                                {hasProgress && remainingSecs > 0 && ` • ${formatDuration(remainingSecs)} left`}
                                                {ep.isPlayed && ' • Played'}
                                            </div>
                                            {hasProgress && (
                                                <div className="mt-2 h-1 bg-[var(--vora-bg-surface)] rounded overflow-hidden">
                                                    <div className="h-full bg-[var(--vora-accent-500)]" style={{ width: `${progressPct}%` }} />
                                                </div>
                                            )}
                                        </div>
                                    </button>
                                );
                            })}
                        </div>
                    )
                ) : viewMode === 'catalog' ? (
                    isLoadingCatalog ? (
                        <div className="text-[var(--vora-text-muted)] py-12 text-center">Loading catalog...</div>
                    ) : catalog.length === 0 ? (
                        <div className="text-[var(--vora-text-muted)] py-12 text-center bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg">
                            <p>No curated podcasts on any connected server.</p>
                            {!canAddCustomFeeds && (
                                <p className="text-xs mt-1">Ask your server admin to add some, or for permission to subscribe to your own.</p>
                            )}
                            {catalogFailedServerIds.length > 0 && (
                                <p className="text-xs mt-2 text-[var(--vora-accent-text)]">Couldn't reach {catalogFailedServerIds.length} server{catalogFailedServerIds.length === 1 ? '' : 's'}.</p>
                            )}
                        </div>
                    ) : (
                        <>
                            {catalogFailedServerIds.length > 0 && (
                                <div className="mb-4 px-3 py-2 bg-[var(--vora-accent-soft)] border border-[var(--vora-accent-soft-hover)] text-[var(--vora-accent-text)] text-xs rounded">
                                    Couldn't reach {catalogFailedServerIds.length} server{catalogFailedServerIds.length === 1 ? '' : 's'} — showing partial results.
                                </div>
                            )}
                            <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                                {catalog.map(item => {
                                    const isAdding = subscribingFeedUrl === item.feedUrl;
                                    const allSubscribed = item.availableOn.every(a => a.isSubscribed);
                                    const target = pickDefaultTargetServer(item);
                                    const showServerChoice = item.availableOn.length > 1;
                                    const dropdownOpen = catalogDropdownFor === item.feedUrl;
                                    return (
                                        <div
                                            key={item.feedUrl}
                                            className="relative flex items-center gap-3 p-3 bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg hover:border-[var(--vora-border-subtle)] transition-colors"
                                        >
                                            <div className="w-14 h-14 rounded bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] flex items-center justify-center overflow-hidden shrink-0">
                                                {item.artworkUrl
                                                    ? <img src={item.artworkUrl} alt={item.title} className="max-w-full max-h-full object-cover" />
                                                    : <svg className="w-7 h-7 text-[var(--vora-text-disabled)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 1a9 9 0 00-9 9v7c0 1.66 1.34 3 3 3h3v-8H5v-2a7 7 0 1114 0v2h-4v8h3c1.66 0 3-1.34 3-3v-7a9 9 0 00-9-9z" /></svg>}
                                            </div>
                                            <div className="flex-1 min-w-0">
                                                <div className="font-bold text-sm text-[var(--vora-text-primary)] truncate" title={item.title}>{item.title}</div>
                                                <div className="text-xs text-[var(--vora-text-muted)] truncate">{item.author || 'Unknown'}</div>
                                                {item.description && <div className="text-xs text-[var(--vora-text-disabled)] mt-1 line-clamp-2">{item.description}</div>}
                                                {showServerChoice && (
                                                    <div className="mt-2 flex flex-wrap items-center gap-1">
                                                        <span className="text-[10px] uppercase tracking-widest text-[var(--vora-text-disabled)] mr-1">On</span>
                                                        {item.availableOn.map(a => (
                                                            <span
                                                                key={a.serverId}
                                                                className={`text-[10px] px-1.5 py-0.5 rounded border ${a.isSubscribed ? 'border-[var(--vora-accent-500)]/40 text-[var(--vora-accent-text)]' : 'border-[var(--vora-border-subtle)] text-[var(--vora-text-muted)]'}`}
                                                                title={a.isSubscribed ? `Subscribed on ${a.serverName}` : `Available on ${a.serverName}`}
                                                            >
                                                                {a.serverName}
                                                            </span>
                                                        ))}
                                                    </div>
                                                )}
                                            </div>
                                            {allSubscribed ? (
                                                <button
                                                    disabled
                                                    className="text-xs px-3 py-1.5 font-bold rounded shrink-0 bg-[var(--vora-bg-surface)] text-[var(--vora-text-muted)] cursor-not-allowed"
                                                >
                                                    Subscribed
                                                </button>
                                            ) : !showServerChoice ? (
                                                <button
                                                    onClick={() => target && subscribeFromCatalog(item.feedUrl, target.serverId)}
                                                    disabled={isAdding || !target}
                                                    className="text-xs px-3 py-1.5 font-bold rounded transition-colors shrink-0 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-500)] disabled:opacity-50 text-[var(--vora-text-primary)] cursor-pointer"
                                                >
                                                    {isAdding ? 'Adding...' : 'Subscribe'}
                                                </button>
                                            ) : (
                                                <div className="relative shrink-0" onClick={(e) => e.stopPropagation()}>
                                                    <button
                                                        onClick={() => {
                                                            if (!target) return;
                                                            setCatalogDropdownFor(dropdownOpen ? null : item.feedUrl);
                                                        }}
                                                        disabled={isAdding || !target}
                                                        className="text-xs px-3 py-1.5 font-bold rounded transition-colors bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-500)] disabled:opacity-50 text-[var(--vora-text-primary)] cursor-pointer flex items-center gap-1.5"
                                                    >
                                                        {isAdding ? 'Adding...' : `Subscribe on ${target?.serverName ?? '...'}`}
                                                        <svg className="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" /></svg>
                                                    </button>
                                                    {dropdownOpen && (
                                                        <div className="absolute right-0 top-full mt-1 z-20 bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-md shadow-2xl py-1 min-w-[180px]">
                                                            {item.availableOn.map(a => (
                                                                <button
                                                                    key={a.serverId}
                                                                    onClick={() => {
                                                                        if (a.isSubscribed) return;
                                                                        subscribeFromCatalog(item.feedUrl, a.serverId);
                                                                    }}
                                                                    disabled={a.isSubscribed}
                                                                    className={`w-full text-left px-3 py-2 text-xs flex items-center justify-between gap-3 ${a.isSubscribed ? 'text-[var(--vora-text-muted)] cursor-not-allowed' : 'text-[var(--vora-text-primary)] hover:bg-[var(--vora-bg-surface)] cursor-pointer'}`}
                                                                >
                                                                    <span className="truncate">{a.serverName}</span>
                                                                    {a.isSubscribed && <span className="text-[10px] text-[var(--vora-accent-text)] shrink-0">Subscribed</span>}
                                                                </button>
                                                            ))}
                                                        </div>
                                                    )}
                                                </div>
                                            )}
                                        </div>
                                    );
                                })}
                            </div>
                        </>
                    )
                ) : subscriptions.length === 0 ? (
                    <div className="text-[var(--vora-text-muted)] py-12 text-center bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg">
                        <p className="mb-2">No subscriptions yet.</p>
                        <p className="text-xs">{canAddCustomFeeds ? 'Use the search above to find a podcast, or paste an RSS feed URL.' : 'Switch to the Browse Catalog tab to subscribe to a curated podcast.'}</p>
                    </div>
                ) : (
                <div className="grid grid-cols-1 lg:grid-cols-[280px_1fr] gap-8">
                    <div className="space-y-3">
                        <h2 className="text-xs font-bold uppercase tracking-widest text-[var(--vora-accent-text)] mb-2">Subscriptions ({subscriptions.length})</h2>
                        {subscriptions.map(sub => (
                            <div
                                key={sub.id}
                                onClick={() => loadEpisodes(sub)}
                                className={`flex items-center gap-3 p-3 rounded-lg border cursor-pointer transition-all ${selectedSub?.id === sub.id ? 'bg-[var(--vora-bg-surface)] border-[var(--vora-accent-500)]' : 'bg-[var(--vora-bg-sunken)] border-[var(--vora-border-subtle)] hover:border-[var(--vora-border-strong)]'}`}
                            >
                                <div className="w-12 h-12 rounded bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] flex items-center justify-center overflow-hidden shrink-0">
                                    {sub.artworkUrl
                                        ? <img src={sub.artworkUrl} alt={sub.title} className="max-w-full max-h-full object-cover" />
                                        : <svg className="w-6 h-6 text-[var(--vora-text-disabled)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 1a9 9 0 00-9 9v7c0 1.66 1.34 3 3 3h3v-8H5v-2a7 7 0 1114 0v2h-4v8h3c1.66 0 3-1.34 3-3v-7a9 9 0 00-9-9z" /></svg>}
                                </div>
                                <div className="flex-1 min-w-0">
                                    <div className="font-bold text-sm text-[var(--vora-text-primary)] truncate">{sub.title}</div>
                                    <div className="text-xs text-[var(--vora-text-muted)] truncate">{sub.author || 'Unknown'}</div>
                                </div>
                                <button
                                    onClick={(e) => { e.stopPropagation(); handleUnsubscribe(sub); }}
                                    className="text-[var(--vora-text-disabled)] hover:text-[var(--vora-danger-text)] transition-colors p-1 cursor-pointer"
                                    title="Unsubscribe"
                                >
                                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6M1 7h22M9 7V4a1 1 0 011-1h4a1 1 0 011 1v3" /></svg>
                                </button>
                            </div>
                        ))}
                    </div>

                    <div>
                        {selectedSub ? (
                            <>
                                <div className="flex items-start gap-4 mb-6 pb-6 border-b border-[var(--vora-border-subtle)]">
                                    <div className="w-24 h-24 rounded bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] flex items-center justify-center overflow-hidden shrink-0">
                                        {selectedSub.artworkUrl
                                            ? <img src={selectedSub.artworkUrl} alt={selectedSub.title} className="max-w-full max-h-full object-cover" />
                                            : <svg className="w-12 h-12 text-[var(--vora-text-disabled)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 1a9 9 0 00-9 9v7c0 1.66 1.34 3 3 3h3v-8H5v-2a7 7 0 1114 0v2h-4v8h3c1.66 0 3-1.34 3-3v-7a9 9 0 00-9-9z" /></svg>}
                                    </div>
                                    <div className="flex-1">
                                        <h2 className="text-xl font-bold text-[var(--vora-text-primary)]">{selectedSub.title}</h2>
                                        {selectedSub.author && <p className="text-sm text-[var(--vora-text-secondary)]">{selectedSub.author}</p>}
                                        {selectedSub.description && <p className="text-sm text-[var(--vora-text-muted)] mt-2 line-clamp-3">{selectedSub.description}</p>}
                                    </div>
                                    <button
                                        onClick={handleRefresh}
                                        disabled={isRefreshing}
                                        className="text-xs px-3 py-1.5 bg-[var(--vora-bg-surface)] hover:bg-[var(--vora-bg-raised)] disabled:opacity-50 text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer"
                                    >
                                        {isRefreshing ? 'Refreshing...' : 'Refresh'}
                                    </button>
                                </div>

                                {isLoadingEpisodes ? (
                                    <div className="text-[var(--vora-text-muted)] py-12 text-center">Loading episodes...</div>
                                ) : episodes.length === 0 ? (
                                    <div className="text-[var(--vora-text-muted)] py-12 text-center">No episodes found.</div>
                                ) : (
                                    <div className="space-y-2">
                                        {episodes.map(ep => {
                                            const hasProgress = !ep.isPlayed && ep.positionSeconds > 5;
                                            const progressPct = hasProgress && ep.durationSeconds
                                                ? Math.min(100, (ep.positionSeconds / ep.durationSeconds) * 100)
                                                : 0;
                                            const remainingSecs = hasProgress && ep.durationSeconds
                                                ? Math.max(0, ep.durationSeconds - ep.positionSeconds)
                                                : 0;
                                            return (
                                                <button
                                                    key={ep.id}
                                                    onClick={() => handlePlay(ep)}
                                                    onContextMenu={(e) => {
                                                        e.preventDefault();
                                                        setContextMenu({ x: e.pageX, y: e.pageY, episodeId: ep.id, isPlayed: ep.isPlayed, hasProgress: !ep.isPlayed && ep.positionSeconds > 5 });
                                                    }}
                                                    className={`w-full text-left flex items-center gap-4 p-3 bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-sunken)]/80 border rounded transition-all cursor-pointer group ${ep.isPlayed ? 'border-[var(--vora-border-subtle)]/50 opacity-60' : 'border-[var(--vora-border-subtle)] hover:border-[var(--vora-accent-500)]'}`}
                                                >
                                                    {ep.isPlayed ? (
                                                        <svg className="w-8 h-8 text-[var(--vora-success-text)] shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" /></svg>
                                                    ) : (
                                                        <svg className="w-8 h-8 text-[var(--vora-accent-text)] shrink-0 group-hover:scale-110 transition-transform" fill="currentColor" viewBox="0 0 24 24"><path d="M8 5v14l11-7z" /></svg>
                                                    )}
                                                    <div className="flex-1 min-w-0">
                                                        <div className={`font-bold text-sm truncate ${ep.isPlayed ? 'text-[var(--vora-text-secondary)] line-through decoration-gray-600' : 'text-[var(--vora-text-primary)] group-hover:text-[var(--vora-text-primary)]'}`}>{ep.title}</div>
                                                        <div className="text-xs text-[var(--vora-text-muted)] mt-0.5">
                                                            {formatPublished(ep.publishedAt)}
                                                            {ep.durationSeconds ? ` • ${formatDuration(ep.durationSeconds)}` : ''}
                                                            {hasProgress && remainingSecs > 0 && ` • ${formatDuration(remainingSecs)} left`}
                                                            {ep.isPlayed && ' • Played'}
                                                        </div>
                                                        {hasProgress && (
                                                            <div className="mt-2 h-1 bg-[var(--vora-bg-surface)] rounded overflow-hidden">
                                                                <div className="h-full bg-[var(--vora-accent-500)]" style={{ width: `${progressPct}%` }} />
                                                            </div>
                                                        )}
                                                    </div>
                                                </button>
                                            );
                                        })}
                                    </div>
                                )}
                            </>
                        ) : (
                            <div className="text-[var(--vora-text-muted)] py-12 text-center">
                                <p>Select a subscription to view episodes.</p>
                            </div>
                        )}
                    </div>
                </div>
                )}
                </>
            )}
        </div>
    );
}
