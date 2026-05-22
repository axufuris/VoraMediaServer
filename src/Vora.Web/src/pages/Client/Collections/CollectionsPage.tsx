import { useEffect, useMemo, useState, useCallback } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { libraryService, type LibrarySummary } from '../../../api/Media/libraryService';
import { collectionService, type CollectionSummary } from '../../../api/Collections/collectionService';
import CreateCollectionModal from '../../../components/Collections/CreateCollectionModal';
import PageHeader from '../../../components/Client/Primitives/PageHeader';
import EmptyState from '../../../components/Client/Primitives/EmptyState';
import MediaPoster from '../../../components/Client/Primitives/MediaPoster';

export default function CollectionsPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const navigate = useNavigate();

    const [libraries, setLibraries] = useState<LibrarySummary[]>([]);
    const [activeTab, setActiveTab] = useState<string>('global');
    const [collections, setCollections] = useState<CollectionSummary[]>([]);
    const [loading, setLoading] = useState(true);
    const [isCreateModalOpen, setIsCreateModalOpen] = useState(false);

    const isAdmin = localStorage.getItem('is_server_admin') === 'true';

    useEffect(() => {
        libraryService.getLibraries(serverId).then(setLibraries).catch(console.error);
    }, [serverId]);

    const sortedLibraries = useMemo(() => {
        const rank = (type: string): number => {
            const t = (type || '').toLowerCase();
            if (t === 'movie' || t === 'homevideo') return 0;
            if (t === 'tvshow' || t === 'show' || t === 'tv') return 1;
            if (t === 'music' || t === 'audio') return 2;
            return 3;
        };
        return [...libraries].sort((a, b) => {
            const diff = rank(a.type) - rank(b.type);
            if (diff !== 0) return diff;
            return a.name.localeCompare(b.name);
        });
    }, [libraries]);

    const fetchCollections = useCallback(async () => {
        try {
            const data = activeTab === 'global'
                ? await collectionService.getGlobalCollections(serverId)
                : await collectionService.getLibraryCollections(activeTab, serverId);
            setCollections(data);
        } catch (error) {
            console.error(error);
        } finally {
            setLoading(false);
        }
    }, [activeTab, serverId]);

    useEffect(() => {
        let isMounted = true;
        if (isMounted) fetchCollections();
        return () => { isMounted = false; };
    }, [fetchCollections]);

    const handleTabChange = (tabId: string) => {
        if (activeTab === tabId) return;
        setLoading(true);
        setActiveTab(tabId);
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
                activeTab={activeTab}
            />

            <PageHeader
                title="Collections"
                subtitle="Curated sets of media — global or scoped to a library."
                actions={createAction}
            />

            <div className="px-8">
                <nav
                    className="flex gap-1 overflow-x-auto"
                    style={{ borderBottom: '1px solid var(--vora-border-subtle)' }}
                >
                    <button
                        type="button"
                        onClick={() => handleTabChange('global')}
                        className="relative cursor-pointer whitespace-nowrap px-4 py-3 text-sm font-medium transition-colors"
                        style={{ color: activeTab === 'global' ? 'var(--vora-text-primary)' : 'var(--vora-text-muted)' }}
                    >
                        Global
                        {activeTab === 'global' && <span className="absolute -bottom-px left-0 right-0 h-0.5 rounded-full" style={{ background: 'var(--vora-accent-500)' }} />}
                    </button>
                    {sortedLibraries.map(lib => {
                        const isActive = activeTab === lib.id;
                        return (
                            <button
                                key={lib.id}
                                type="button"
                                onClick={() => handleTabChange(lib.id)}
                                className="relative cursor-pointer whitespace-nowrap px-4 py-3 text-sm font-medium transition-colors"
                                style={{ color: isActive ? 'var(--vora-text-primary)' : 'var(--vora-text-muted)' }}
                            >
                                {lib.name}
                                {isActive && <span className="absolute -bottom-px left-0 right-0 h-0.5 rounded-full" style={{ background: 'var(--vora-accent-500)' }} />}
                            </button>
                        );
                    })}
                </nav>
            </div>

            <div className="px-8 pt-6">
                {loading ? (
                    <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 2xl:grid-cols-8">
                        {Array.from({ length: 10 }, (_, i) => <div key={i} className="vora-skeleton aspect-[2/3]" />)}
                    </div>
                ) : collections.length === 0 ? (
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
                ) : (
                    <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 2xl:grid-cols-8">
                        {collections.map(collection => (
                            <MediaPoster
                                key={collection.id}
                                imageUrl={collection.posterUrl}
                                title={collection.title}
                                subtitle={`${collection.itemCount} item${collection.itemCount === 1 ? '' : 's'}`}
                                onClick={() => navigate(serverId ? `/server/${serverId}/collection/${collection.id}` : `/collection/${collection.id}`)}
                                fill
                            />
                        ))}
                    </div>
                )}
            </div>
        </div>
    );
}
