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
import CollectionsPage from './Collections/CollectionsPage';
import WatchlistPage from './WatchlistPage';
import MediaRow, { MediaRowItem } from '../../components/Client/Primitives/MediaRow';
import MediaCard from '../../components/Client/Primitives/MediaCard';
import PosterRemoveButton from '../../components/Client/Primitives/PosterRemoveButton';
import { useDialog } from '../../dialogs';

type HomeTab = 'overview' | 'watchlist' | 'collections' | 'playlists';

const HOME_TAB_STORAGE_KEY = 'home_active_tab';

const readSavedHomeTab = (): HomeTab => {
    const saved = sessionStorage.getItem(HOME_TAB_STORAGE_KEY);
    return saved === 'watchlist' || saved === 'collections' || saved === 'playlists' ? saved : 'overview';
};

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
        <MediaRow title="Continue Watching">
            {items.map(item => {
                const percent = item.resumePositionSeconds && item.durationSeconds
                    ? Math.min(100, Math.max(0, (item.resumePositionSeconds / item.durationSeconds) * 100))
                    : 0;
                return (
                    <MediaRowItem key={item.id}>
                        <MediaCard
                            item={item}
                            imageUrl={item.posterUrl ?? item.backgroundUrl}
                            progressPercent={percent}
                            onClick={() => navigate(serverId ? `/server/${serverId}/media/${item.id}` : `/media/${item.id}`)}
                            hoverBadge={<PosterRemoveButton onClick={() => handleHide(item)} title="Hide from Continue Watching" />}
                        />
                    </MediaRowItem>
                );
            })}
        </MediaRow>
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
        <MediaRow title={list.title}>
            {items.map(item => (
                <MediaRowItem key={item.id}>
                    <MediaCard
                        item={item}
                        imageUrl={item.posterUrl}
                        isPlayed={item.isPlayed}
                        onClick={() => navigate(serverId ? `/server/${serverId}/media/${item.id}` : `/media/${item.id}`)}
                    />
                </MediaRowItem>
            ))}
        </MediaRow>
    );
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

    const displayLists = useMemo(() => {
        const base = [...lists].sort((a, b) => a.displayOrder - b.displayOrder);

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
    }, [lists, clientLayout]);

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

    const tabBar = (
        <div className="px-8 pt-4">
            <Tabs<HomeTab>
                tabs={[
                    { key: 'overview', label: 'Home' },
                    { key: 'watchlist', label: 'Watchlist' },
                    { key: 'collections', label: 'Collections' },
                    { key: 'playlists', label: 'Playlists' },
                ]}
                active={activeTab}
                onChange={handleTabChange}
                actions={activeTab === 'overview' ? customizeAction : undefined}
            />
        </div>
    );

    if (activeTab !== 'overview') {
        return (
            <div className="min-h-full pb-20">
                {tabBar}
                {activeTab === 'watchlist' && <WatchlistPage embedded />}
                {activeTab === 'collections' && <CollectionsPage />}
                {activeTab === 'playlists' && <PlaylistsPage embedded />}
            </div>
        );
    }

    if (loading) {
        return (
            <div className="min-h-full pb-20">
                {tabBar}
                <div className="p-8">
                    <div className="vora-skeleton mb-8 h-24" />
                    <div className="vora-skeleton mb-8 h-48" />
                    <div className="vora-skeleton mb-8 h-48" />
                </div>
            </div>
        );
    }

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
                <PageHeader title="Home" subtitle="Pick up where you left off, or wander somewhere new." />

                {lists.length === 0 ? (
                    <EmptyState
                        title="Your home screen is empty"
                        description="Create Smart Lists in Server Settings to populate rows on this page."
                        icon={(
                            <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                                <rect x="3" y="3" width="18" height="18" rx="2" />
                                <path d="M3 9h18M9 21V9" />
                            </svg>
                        )}
                    />
                ) : (
                    <div className="space-y-10 pt-2">
                        {activeProfileId && <ContinueWatchingRow profileId={activeProfileId} serverId={serverId} />}
                        {displayLists.map(list => <SmartListRow key={list.id} list={list} serverId={serverId} />)}
                    </div>
                )}
            </div>
        </>
    );
}
