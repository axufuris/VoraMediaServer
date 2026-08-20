import { useEffect, useState, useCallback, useMemo } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { smartListService, type SmartListClientDto } from '../../api/Collections/smartListService';
import { type LibraryItem } from '../../api/Media/libraryService';
import { syncService, type ContinueWatchingItem } from '../../api/Media/syncService';
import { useSignalREvent } from '../../hooks/useSignalREvent';
import ClientHomeCustomizeModal, { type HomeLayoutItem } from '../../components/Home/HomeCustomizeModal';
import { profileDeviceSettingsService } from '../../api/Users/profileDeviceSettingsService';
import { StorageKeys, getProfileIdFromToken } from '../../utils/storageKeys';
import PageHeader from '../../components/Client/Primitives/PageHeader';
import Tabs from '../../components/Client/Primitives/Tabs';
import EmptyState from '../../components/Client/Primitives/EmptyState';
import PlaylistsPage from './Playlists/PlaylistsPage';
import MediaRail from '../../components/Client/Primitives/MediaRail';
import MediaPoster from '../../components/Client/Primitives/MediaPoster';
import PosterRemoveButton from '../../components/Client/Primitives/PosterRemoveButton';
import MediaStill from '../../components/Client/Primitives/MediaStill';
import { posterTitle } from '../../utils/posterTitle';
import Hero from '../../components/Client/Primitives/Hero';
import { useDialog } from '../../dialogs';

const SPOTLIGHT_CYCLE_MS = 8000;

type HomeTab = 'overview' | 'playlists';

const HOME_TAB_STORAGE_KEY = 'home_active_tab';

const readSavedHomeTab = (): HomeTab => {
    const saved = sessionStorage.getItem(HOME_TAB_STORAGE_KEY);
    return saved === 'playlists' ? 'playlists' : 'overview';
};

function HeroSpotlight({ items, serverId }: { items: LibraryItem[], serverId?: string }) {
    const navigate = useNavigate();
    const [index, setIndex] = useState(0);

    useEffect(() => {
        if (items.length <= 1) return;
        const interval = window.setInterval(() => {
            setIndex(prev => (prev + 1) % items.length);
        }, SPOTLIGHT_CYCLE_MS);
        return () => window.clearInterval(interval);
    }, [items.length]);

    if (items.length === 0) return null;
    const active = items[index];
    const year = active.releaseDate ? new Date(active.releaseDate).getFullYear() : undefined;
    const targetPath = serverId ? `/server/${serverId}/media/${active.id}` : `/media/${active.id}`;

    return (
        <Hero
            backdropSrc={active.backgroundUrl || active.posterUrl}
            transitionKey={active.id}
            eyebrow={(
                <>
                    <span style={{ width: 6, height: 6, borderRadius: '50%', background: 'var(--vora-accent-500)' }} />
                    <span>Spotlight</span>
                </>
            )}
            title={active.title}
            meta={(
                <>
                    {year && <span>{year}</span>}
                    {active.contentRating && <span style={{ padding: '3px 9px', border: '1px solid var(--vora-border-subtle)', borderRadius: 6, fontSize: 12 }}>{active.contentRating}</span>}
                    {active.resolution && <span style={{ padding: '3px 9px', border: '1px solid var(--vora-border-subtle)', borderRadius: 6, fontSize: 12 }}>{active.resolution}</span>}
                </>
            )}
            ctas={(
                <>
                    <button
                        type="button"
                        className="vora-button-primary cursor-pointer"
                        onClick={() => navigate(targetPath)}
                        style={{ display: 'inline-flex', alignItems: 'center', gap: 10, padding: '14px 28px', fontSize: 15 }}
                    >
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3" /></svg>
                        Play
                    </button>
                    <button
                        type="button"
                        className="vora-button-secondary cursor-pointer"
                        onClick={() => navigate(targetPath)}
                        style={{ padding: '14px 24px', fontSize: 15 }}
                    >
                        More info
                    </button>
                </>
            )}
            indicator={items.length > 1 ? (
                <div style={{ display: 'flex', gap: 8 }}>
                    {items.map((it, i) => (
                        <button
                            key={it.id}
                            type="button"
                            aria-label={`Show spotlight ${i + 1}`}
                            onClick={() => setIndex(i)}
                            className="cursor-pointer"
                            style={{
                                width: i === index ? 36 : 24,
                                height: 3,
                                borderRadius: 2,
                                border: 'none',
                                padding: 0,
                                background: i === index ? 'var(--vora-accent-500)' : 'rgba(255,255,255,0.2)',
                                transition: 'width 240ms var(--vora-ease-out, ease-out), background 240ms ease',
                            }}
                        />
                    ))}
                </div>
            ) : undefined}
        />
    );
}

function ContinueWatchingRow({ profileId, serverId }: { profileId: string, serverId?: string }) {
    const navigate = useNavigate();
    const dialog = useDialog();
    const [items, setItems] = useState<ContinueWatchingItem[]>([]);
    const [loading, setLoading] = useState(true);

    const fetchItems = useCallback(async (silent = false) => {
        try {
            const data = await syncService.getContinueWatching(profileId, serverId);
            setItems(data);
        } catch (error) {
            console.error('Failed to fetch continue watching items', error);
        } finally {
            if (!silent) setLoading(false);
        }
    }, [profileId, serverId]);

    useEffect(() => {
        fetchItems();
    }, [fetchItems]);

    useSignalREvent('UserMediaStateUpdated', useCallback(() => {
        fetchItems(true);
    }, [fetchItems]));

    const handleHide = async (item: ContinueWatchingItem) => {
        const isEpisode = item.type === 'Episode';
        const displayName = isEpisode ? (item.tvShowTitle ?? item.title) : item.title;
        const ok = await dialog.confirm({
            title: 'Remove from Continue Watching?',
            message: isEpisode
                ? `"${displayName}" will be removed from this list. You can still find it in your library.`
                : `"${displayName}" will be removed from this list. You can still resume it from its detail page.`,
            confirmText: 'Remove',
            cancelText: 'Cancel',
        });
        if (!ok) return;
        const hideId = isEpisode && item.tvShowId ? item.tvShowId : item.id;
        try {
            await syncService.hideFromContinueWatching(profileId, hideId, serverId);
            setItems(prev => prev.filter(i => i.id !== item.id));
        } catch (error) {
            console.error('Failed to hide item', error);
            await dialog.alert({ title: 'Could not remove', message: 'Please try again.', tone: 'danger' });
        }
    };

    if (loading) return <div className="vora-skeleton mx-8 mb-8 h-48" />;
    if (items.length === 0) return null;

    return (
        <MediaRail title="Continue Watching">
            {items.map(item => {
                const percent = item.resumePositionSeconds && item.durationSeconds
                    ? Math.min(100, Math.max(0, (item.resumePositionSeconds / item.durationSeconds) * 100))
                    : 0;
                const isEpisode = item.type === 'Episode';
                const title = isEpisode ? item.tvShowTitle ?? item.title : item.title;
                const subtitle = isEpisode ? `S${item.seasonNumber} E${item.episodeNumber} · ${item.title}` : (item.releaseDate ? new Date(item.releaseDate).getFullYear().toString() : item.type);
                const imageUrl = item.posterUrl ?? item.backgroundUrl;
                const onOpen = () => navigate(serverId ? `/server/${serverId}/media/${item.id}` : `/media/${item.id}`);

                const hideBadge = (
                    <PosterRemoveButton onClick={() => handleHide(item)} title="Hide from Continue Watching" />
                );

                return (
                    <div key={item.id} style={{ scrollSnapAlign: 'start', flex: 'none' }}>
                        <MediaPoster imageUrl={imageUrl} title={title} subtitle={subtitle} progressPercent={percent} onClick={onOpen} hoverBadge={hideBadge} />
                    </div>
                );
            })}
        </MediaRail>
    );
}

function SmartListRow({ list, serverId }: { list: SmartListClientDto, serverId?: string }) {
    const navigate = useNavigate();
    const [items, setItems] = useState<LibraryItem[]>([]);
    const [loading, setLoading] = useState(true);

    const fetchItems = useCallback((silent = false) => {
        smartListService.getListItems(list.id, serverId)
            .then(setItems)
            .catch(console.error)
            .finally(() => {
                if (!silent) setLoading(false);
            });
    }, [list.id, serverId]);

    useEffect(() => {
        fetchItems();
    }, [fetchItems]);

    useSignalREvent('LibraryUpdated', useCallback(() => fetchItems(true), [fetchItems]));
    useSignalREvent('MediaItemUpdated', useCallback(() => fetchItems(true), [fetchItems]));

    if (loading) return <div className="vora-skeleton mx-8 mb-8 h-48" />;
    if (items.length === 0) return null;

    return (
        <MediaRail title={list.title}>
            {items.map(item => {
                const isEpisode = item.type === 'Episode';
                const subtitle = item.releaseDate ? new Date(item.releaseDate).getFullYear().toString() : item.type;
                const onOpen = () => navigate(serverId ? `/server/${serverId}/media/${item.id}` : `/media/${item.id}`);
                return (
                    <div key={item.id} style={{ scrollSnapAlign: 'start', flex: 'none' }}>
                        {isEpisode ? (
                            <MediaStill imageUrl={item.posterUrl} title={item.title} subtitle={subtitle} onClick={onOpen} />
                        ) : (
                            <MediaPoster imageUrl={item.posterUrl} title={posterTitle(item)} subtitle={subtitle} isPlayed={item.isPlayed} onClick={onOpen} />
                        )}
                    </div>
                );
            })}
        </MediaRail>
    );
}

function useSpotlightItems(lists: SmartListClientDto[], serverId?: string): { items: LibraryItem[], sourceListId: string | null } {
    const [items, setItems] = useState<LibraryItem[]>([]);
    const [sourceListId, setSourceListId] = useState<string | null>(null);

    const spotlightList = useMemo(() => {
        const candidates = lists.filter(l => l.isSpotlight);
        if (candidates.length === 0) return null;
        return [...candidates].sort((a, b) => b.displayOrder - a.displayOrder)[0];
    }, [lists]);

    useEffect(() => {
        if (!spotlightList) {
            queueMicrotask(() => {
                setItems([]);
                setSourceListId(null);
            });
            return;
        }
        let cancelled = false;
        smartListService.getListItems(spotlightList.id, serverId)
            .then(data => {
                if (cancelled) return;
                setItems(data.slice(0, 5));
                setSourceListId(spotlightList.id);
            })
            .catch(() => {
                if (!cancelled) {
                    setItems([]);
                    setSourceListId(null);
                }
            });
        return () => { cancelled = true; };
    }, [spotlightList, serverId]);

    return { items, sourceListId };
}

export default function HomePage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const [lists, setLists] = useState<SmartListClientDto[]>([]);
    const [clientLayout, setClientLayout] = useState<HomeLayoutItem[]>([]);
    const [loading, setLoading] = useState(true);
    const [isCustomizeOpen, setIsCustomizeOpen] = useState(false);
    const [activeTab, setActiveTab] = useState<HomeTab>(readSavedHomeTab);

    const handleTabChange = (tab: HomeTab) => {
        setActiveTab(tab);
        sessionStorage.setItem(HOME_TAB_STORAGE_KEY, tab);
    };

    const profileToken = localStorage.getItem(StorageKeys.profileToken);
    const activeProfileId = getProfileIdFromToken(profileToken) ?? '';
    const deviceId = localStorage.getItem(StorageKeys.deviceId) || 'unknown';

    useEffect(() => {
        const fetchData = async () => {
            try {
                const activeLists = await smartListService.getActiveLists(serverId);
                setLists(activeLists);

                let savedLayoutJson: string | null = null;
                if (activeProfileId && deviceId !== 'unknown') {
                    savedLayoutJson = await profileDeviceSettingsService.getHomeLayout(activeProfileId, deviceId, serverId);
                }

                if (!savedLayoutJson) {
                    const layoutKey = `client_home_layout_${activeProfileId}_${deviceId}`;
                    savedLayoutJson = localStorage.getItem(layoutKey);
                }

                if (savedLayoutJson) {
                    setClientLayout(JSON.parse(savedLayoutJson));
                }
            } catch (error) {
                console.error(error);
            } finally {
                setLoading(false);
            }
        };
        fetchData();
    }, [serverId, activeProfileId, deviceId]);

    const handleSaveLayout = async (newLayout: HomeLayoutItem[]) => {
        const layoutJson = JSON.stringify(newLayout);
        setClientLayout(newLayout);
        setIsCustomizeOpen(false);
        localStorage.setItem(`client_home_layout_${activeProfileId}_${deviceId}`, layoutJson);

        try {
            if (activeProfileId && deviceId !== 'unknown') {
                await profileDeviceSettingsService.saveHomeLayout(activeProfileId, deviceId, layoutJson, serverId);
            }
        } catch (error) {
            console.error('Failed to sync home layout to server', error);
        }
    };

    const { items: spotlightItems, sourceListId: spotlightListId } = useSpotlightItems(lists, serverId);

    const [showSpotlightPref, setShowSpotlightPref] = useState<boolean>(() => {
        if (!activeProfileId) return true;
        const stored = localStorage.getItem(StorageKeys.spotlight(activeProfileId));
        return stored === null ? true : stored === 'true';
    });

    useEffect(() => {
        const handler = () => {
            if (!activeProfileId) return;
            const stored = localStorage.getItem(StorageKeys.spotlight(activeProfileId));
            setShowSpotlightPref(stored === null ? true : stored === 'true');
        };
        handler();
        window.addEventListener('vora:home-prefs-changed', handler);
        return () => window.removeEventListener('vora:home-prefs-changed', handler);
    }, [activeProfileId]);

    const heroVisible = showSpotlightPref && spotlightItems.length > 0;

    const displayLists = useMemo(() => {
        // The spotlight list is featured in the hero AND still shown as a row so
        // it's browsable (the hero only rotates a few items at a time).
        const base = [...lists]
            .sort((a, b) => a.displayOrder - b.displayOrder);

        if (clientLayout.length === 0) {
            return base;
        }

        const prefById = new Map(clientLayout.map(p => [p.listId, p]));
        const withPref = base
            .filter(l => prefById.has(l.id))
            .sort((a, b) => {
                const pa = prefById.get(a.id);
                const pb = prefById.get(b.id);
                return (pa ? pa.orderIndex : 0) - (pb ? pb.orderIndex : 0);
            });
        const withoutPref = base.filter(l => !prefById.has(l.id));

        return [...withPref, ...withoutPref]
            .filter(l => {
                const pref = prefById.get(l.id);
                return pref ? pref.isEnabled : true;
            });
    }, [lists, clientLayout, spotlightListId]);

    const tabBar = (
        <div className="px-8 pt-4">
            <Tabs<HomeTab>
                tabs={[
                    { key: 'overview', label: 'Home' },
                    { key: 'playlists', label: 'Playlists' },
                ]}
                active={activeTab}
                onChange={handleTabChange}
            />
        </div>
    );

    if (activeTab === 'playlists') {
        return (
            <div className="min-h-full pb-20">
                {tabBar}
                <PlaylistsPage embedded />
            </div>
        );
    }

    if (loading) {
        return (
            <div className="min-h-full pb-20">
                {tabBar}
                <div className="p-8">
                    <div className="vora-skeleton mb-8 h-[50vh] min-h-[420px]" />
                    <div className="vora-skeleton mx-8 mb-8 h-48" />
                    <div className="vora-skeleton mx-8 mb-8 h-48" />
                </div>
            </div>
        );
    }

    const customizeAction = (
        <button
            type="button"
            onClick={() => setIsCustomizeOpen(true)}
            className="vora-button-secondary cursor-pointer inline-flex items-center gap-2"
        >
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75">
                <line x1="4" y1="21" x2="4" y2="14" />
                <line x1="4" y1="10" x2="4" y2="3" />
                <line x1="12" y1="21" x2="12" y2="12" />
                <line x1="12" y1="8" x2="12" y2="3" />
                <line x1="20" y1="21" x2="20" y2="16" />
                <line x1="20" y1="12" x2="20" y2="3" />
                <line x1="1" y1="14" x2="7" y2="14" />
                <line x1="9" y1="8" x2="15" y2="8" />
                <line x1="17" y1="16" x2="23" y2="16" />
            </svg>
            Customize
        </button>
    );

    return (
        <>
            <ClientHomeCustomizeModal
                isOpen={isCustomizeOpen}
                onClose={() => setIsCustomizeOpen(false)}
                activeLists={lists}
                savedLayout={clientLayout}
                onSave={handleSaveLayout}
            />

            <div className="min-h-full pb-20">
                {tabBar}
                {heroVisible ? (
                    <HeroSpotlight items={spotlightItems} serverId={serverId} />
                ) : (
                    <PageHeader title="Home" subtitle="Pick up where you left off, or wander somewhere new." actions={customizeAction} />
                )}

                {lists.length === 0 ? (
                    <EmptyState
                        title="Your home screen is empty"
                        description="Create Smart Lists in Server Settings to populate rows on this page. Flag one as Spotlight to fill the cinematic hero above."
                        icon={(
                            <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                                <rect x="3" y="3" width="18" height="18" rx="2" />
                                <path d="M3 9h18M9 21V9" />
                            </svg>
                        )}
                    />
                ) : (
                    <div className={heroVisible ? '-mt-12 space-y-10' : 'space-y-10 pt-2'}>
                        {heroVisible && (
                            <div className="relative z-10 flex justify-end px-8">
                                {customizeAction}
                            </div>
                        )}
                        {activeProfileId && <ContinueWatchingRow profileId={activeProfileId} serverId={serverId} />}
                        {displayLists.map(list => <SmartListRow key={list.id} list={list} serverId={serverId} />)}
                    </div>
                )}
            </div>
        </>
    );
}
