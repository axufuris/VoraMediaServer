import { useEffect, useMemo, useState, useCallback } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { libraryService, type LibrarySummary } from '../../../api/Media/libraryService';
import { collectionService, type CollectionSummary } from '../../../api/Collections/collectionService';
import CreateCollectionModal from '../../../components/Collections/CreateCollectionModal';
import EmptyState from '../../../components/Client/Primitives/EmptyState';
import MediaGrid from '../../../components/Client/Primitives/MediaGrid';
import { StorageKeys } from '../../../utils/storageKeys';
import LibraryCollectionGrid from '../../../components/Collections/LibraryCollectionGrid';
import { isLibraryUnpinned, useSidebarItems } from '../../../layouts/sidebarItems';

interface CollectionTab {
    key: string;
    label: string;
    libraries: LibrarySummary[];
}

const GLOBAL_TAB: CollectionTab = { key: 'global', label: 'Global', libraries: [] };

const tabRank = (type: string): number => {
    const t = (type || '').toLowerCase();
    if (t === 'movie' || t === 'homevideo') return 0;
    if (t === 'tvshow' || t === 'show' || t === 'tv') return 1;
    if (t === 'music' || t === 'audio') return 2;
    return 3;
};

// The sidebar opens every music library through its one Music entry, so their
// collections share one Music tab too.
function collectionTabs(libraries: LibrarySummary[]): CollectionTab[] {
    const music = libraries.filter(lib => lib.type === 'Music');
    const tabs: CollectionTab[] = libraries
        .filter(lib => lib.type !== 'Music')
        .map(lib => ({ key: lib.id, label: lib.name, libraries: [lib] }));
    if (music.length > 0) tabs.push({ key: 'music', label: 'Music', libraries: music });
    return tabs.sort((a, b) => tabRank(a.libraries[0].type) - tabRank(b.libraries[0].type) || a.label.localeCompare(b.label));
}

const listTitle = (collection: CollectionSummary): string =>
    (collection.sortTitle && collection.sortTitle.trim() ? collection.sortTitle : collection.title).toUpperCase();

const ordinal = (a: string, b: string): number => (a < b ? -1 : a > b ? 1 : 0);

// Each library's list comes back in the server's order (sort title, then title,
// ignoring case), so a tab spanning several libraries puts theirs back in that
// order once they're together.
async function tabCollections(tab: CollectionTab, serverId: string | undefined, isAdmin: boolean): Promise<CollectionSummary[]> {
    if (tab.libraries.length === 0) return collectionService.getGlobalCollections(serverId);
    if (tab.libraries.length === 1) return collectionService.getLibraryCollections(tab.libraries[0].id, serverId, isAdmin);
    const lists = await Promise.all(tab.libraries.map(lib => collectionService.getLibraryCollections(lib.id, serverId, isAdmin)));
    const byId = new Map(lists.flat().map(collection => [collection.id, collection]));
    return [...byId.values()].sort((a, b) => ordinal(listTitle(a), listTitle(b)) || ordinal(a.title.toUpperCase(), b.title.toUpperCase()));
}

export default function CollectionsPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const navigate = useNavigate();
    const [searchParams, setSearchParams] = useSearchParams();

    // The active tab lives in the URL (?tab=<libraryId|music|global>) so returning
    // from a collection (the browser's back) restores the tab you were on
    // instead of snapping back to Global.
    const requestedTab = searchParams.get('tab') ?? 'global';

    const [libraries, setLibraries] = useState<LibrarySummary[] | null>(null);
    const [collections, setCollections] = useState<CollectionSummary[]>([]);
    const [loading, setLoading] = useState(true);
    const [isCreateModalOpen, setIsCreateModalOpen] = useState(false);

    const isAdmin = localStorage.getItem(StorageKeys.isServerAdmin) === 'true';

    useEffect(() => {
        libraryService.getLibraries(serverId).then(setLibraries).catch(error => {
            console.error(error);
            setLibraries([]);
        });
    }, [serverId]);

    // A library the profile unpinned from the sidebar gets no tab here either,
    // and its collections aren't fetched, even from an old ?tab= link.
    const sidebar = useSidebarItems();
    const shownLibraries = useMemo(() => {
        if (!libraries || !sidebar.loaded) return null;
        return libraries.filter(lib => !isLibraryUnpinned(sidebar.items, lib));
    }, [libraries, sidebar]);

    const tabs = useMemo(() => shownLibraries && collectionTabs(shownLibraries), [shownLibraries]);

    // Null while a library tab is asked for and the tabs aren't known yet. A link
    // to one music library opens the Music tab.
    const activeTab = requestedTab === GLOBAL_TAB.key
        ? GLOBAL_TAB
        : tabs && (tabs.find(tab => tab.key === requestedTab || tab.libraries.some(lib => lib.id === requestedTab)) ?? GLOBAL_TAB);
    const activeKey = activeTab?.key ?? requestedTab;

    const fetchCollections = useCallback(async () => {
        if (!activeTab) return;
        try {
            setCollections(await tabCollections(activeTab, serverId, isAdmin));
        } catch (error) {
            console.error(error);
        } finally {
            setLoading(false);
        }
    }, [activeTab, serverId, isAdmin]);

    useEffect(() => {
        let isMounted = true;
        if (isMounted) fetchCollections();
        return () => { isMounted = false; };
    }, [fetchCollections]);

    const handleTabChange = (tabId: string) => {
        if (activeKey === tabId) return;
        setLoading(true);
        setSearchParams(prev => {
            const next = new URLSearchParams(prev);
            if (tabId === 'global') next.delete('tab');
            else next.set('tab', tabId);
            return next;
        }, { replace: true });
    };

    const createAction = isAdmin ? (
        <button
            type="button"
            onClick={() => setIsCreateModalOpen(true)}
            className="vora-button-primary cursor-pointer inline-flex items-center gap-1.5"
        >
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.25"><line x1="12" y1="5" x2="12" y2="19" /><line x1="5" y1="12" x2="19" y2="12" /></svg>
            Create collection
        </button>
    ) : undefined;

    return (
        <div className="min-h-full pb-16">
            <CreateCollectionModal
                isOpen={isCreateModalOpen}
                onClose={() => setIsCreateModalOpen(false)}
                onSaved={() => { setLoading(true); fetchCollections(); }}
                libraries={(activeTab ?? GLOBAL_TAB).libraries}
            />

            {createAction && <div className="flex justify-end px-8 max-md:px-4 pt-4">{createAction}</div>}

            <div className="px-8 max-md:px-4">
                <nav
                    className="flex gap-1 overflow-x-auto"
                    style={{ borderBottom: '1px solid var(--vora-border-subtle)' }}
                >
                    {[GLOBAL_TAB, ...(tabs ?? [])].map(tab => {
                        const isActive = activeKey === tab.key;
                        return (
                            <button
                                key={tab.key}
                                type="button"
                                onClick={() => handleTabChange(tab.key)}
                                className="relative cursor-pointer whitespace-nowrap px-4 py-3 text-sm font-medium transition-colors"
                                style={{ color: isActive ? 'var(--vora-text-primary)' : 'var(--vora-text-muted)' }}
                            >
                                {tab.label}
                                {isActive && <span className="absolute bottom-0 left-0 right-0 h-0.5 rounded-full" style={{ background: 'var(--vora-accent-500)' }} />}
                            </button>
                        );
                    })}
                </nav>
            </div>

            <div className="px-8 max-md:px-4 pt-6">
                {loading ? (
                    <MediaGrid>
                        {Array.from({ length: 10 }, (_, i) => <div key={i} className="vora-skeleton aspect-[2/3]" />)}
                    </MediaGrid>
                ) : (
                    <LibraryCollectionGrid
                        collections={collections}
                        isAdmin={isAdmin && (activeTab?.libraries.length ?? 0) > 0}
                        onOpen={collection => navigate(serverId ? `/server/${serverId}/collection/${collection.id}` : `/collection/${collection.id}`)}
                        empty={(
                    <EmptyState
                        title="No collections in this section"
                        description="Collections group related media — a film franchise, a curated set, a theme."
                        action={isAdmin ? (
                            <button type="button" onClick={() => setIsCreateModalOpen(true)} className="vora-button-primary cursor-pointer">Create the first one</button>
                        ) : undefined}
                        icon={(
                            <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                                <path d="M12 4l9 5-9 5-9-5 9-5z" />
                                <path d="M3 15l9 5 9-5" />
                                <path d="M3 19l9 5 9-5" />
                            </svg>
                        )}
                    />
                        )}
                    />
                )}
            </div>
        </div>
    );
}
