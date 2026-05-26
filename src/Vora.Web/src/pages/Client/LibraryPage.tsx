import { useEffect, useState, useMemo, useCallback, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { libraryService, type LibraryItem, type MediaLibrary } from '../../api/Media/libraryService';
import { collectionService, type CollectionSummary } from '../../api/Collections/collectionService';
import { recommendationService, type RecommendationListVM } from '../../api/Discovery/recommendationService';
import { libraryAdminService } from '../../api/Media/libraryAdminService';
import { useSignalREvent } from '../../hooks/useSignalREvent';
import RecommendationRow from '../../components/Media/RecommendationRow';
import { useDialog } from '../../dialogs';
import EmptyState from '../../components/Client/Primitives/EmptyState';
import Tabs from '../../components/Client/Primitives/Tabs';
import MediaPoster from '../../components/Client/Primitives/MediaPoster';
import MediaStill from '../../components/Client/Primitives/MediaStill';
import LetterRail from '../../components/Client/Primitives/LetterRail';
import { StorageKeys } from '../../utils/storageKeys';

type LibraryTabKey = 'library' | 'collections' | 'recommendations';

function AsyncLibraryProviderBlock({ providerId, libraryId, serverId }: { providerId: string, libraryId: string, serverId?: string }) {
    const [lists, setLists] = useState<RecommendationListVM[]>([]);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        recommendationService.getLibraryRecommendations(libraryId, providerId, serverId)
            .then(setLists)
            .catch(console.error)
            .finally(() => setLoading(false));
    }, [providerId, libraryId, serverId]);

    if (loading) {
        return (
            <div className="mb-8 px-8">
                <div className="vora-skeleton mb-4 h-6 w-48" />
                <div className="flex gap-4 overflow-hidden">
                    {[1, 2, 3, 4, 5, 6].map(i => (
                        <div key={i} className="vora-skeleton h-72 w-48 flex-none" />
                    ))}
                </div>
            </div>
        );
    }

    if (lists.length === 0) return null;

    return (
        <>
            {[...lists].sort((a, b) => a.weight - b.weight).map((list, index) => (
                <RecommendationRow key={`${providerId}-${index}`} list={list} serverId={serverId} />
            ))}
        </>
    );
}

function LibraryHero({ library, totalItems, totalCollections, samplePosters }: { library: MediaLibrary, totalItems: number, totalCollections: number, samplePosters: string[] }) {
    return (
        <header className="relative isolate overflow-hidden" style={{ minHeight: 280 }}>
            <div className="absolute inset-0 flex">
                {samplePosters.length === 0 && <div className="flex-1" style={{ background: 'var(--vora-bg-sunken)' }} />}
                {samplePosters.slice(0, 6).map((url, i) => (
                    <div
                        key={`${url}-${i}`}
                        className="flex-1"
                        style={{
                            backgroundImage: `url("${url}")`,
                            backgroundSize: 'cover',
                            backgroundPosition: 'center',
                            opacity: 0.55,
                        }}
                    />
                ))}
            </div>
            <div
                className="absolute inset-0"
                style={{
                    backgroundImage: 'linear-gradient(180deg, rgba(0,0,0,0.25) 0%, rgba(0,0,0,0) 35%, var(--vora-bg-canvas) 96%), linear-gradient(90deg, rgba(0,0,0,0.55) 0%, rgba(0,0,0,0) 70%)',
                }}
            />
            <div className="relative px-8 pt-16 pb-14">
                <div className="inline-flex items-center gap-2 rounded-full px-3 py-1 text-xs font-medium" style={{ background: 'rgba(255,255,255,0.06)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-secondary)' }}>
                    <span style={{ width: 6, height: 6, borderRadius: '50%', background: 'var(--vora-accent-500)' }} />
                    Library · {library.type}
                </div>
                <h1 className="mt-3 m-0 text-5xl font-semibold" style={{ color: 'var(--vora-text-primary)', letterSpacing: '-0.02em' }}>{library.name}</h1>
                <div className="mt-3 flex flex-wrap gap-x-4 gap-y-1 text-sm" style={{ color: 'var(--vora-text-secondary)' }}>
                    <span>{totalItems.toLocaleString()} {totalItems === 1 ? 'item' : 'items'}</span>
                    {totalCollections > 0 && <span>·</span>}
                    {totalCollections > 0 && <span>{totalCollections} {totalCollections === 1 ? 'collection' : 'collections'}</span>}
                </div>
            </div>
        </header>
    );
}

export default function LibraryPage() {
    const dialog = useDialog();
    const { serverId, id } = useParams<{ serverId?: string, id: string }>();
    const navigate = useNavigate();
    const gridRef = useRef<HTMLDivElement | null>(null);

    const [activeTab, setActiveTab] = useState<LibraryTabKey>(() => {
        if (!id) return 'library';
        const saved = localStorage.getItem(`vora_library_tab_${id}`);
        return (saved as LibraryTabKey) || 'library';
    });

    useEffect(() => {
        if (!id) return;
        const saved = localStorage.getItem(`vora_library_tab_${id}`);
        setActiveTab((saved as LibraryTabKey) || 'library');
    }, [id]);

    useEffect(() => {
        if (!id) return;
        localStorage.setItem(`vora_library_tab_${id}`, activeTab);
    }, [id, activeTab]);
    const [items, setItems] = useState<LibraryItem[]>([]);
    const [collections, setCollections] = useState<CollectionSummary[]>([]);
    const [library, setLibrary] = useState<MediaLibrary | null>(null);
    const [loading, setLoading] = useState(true);

    const [providers, setProviders] = useState<string[]>([]);
    const [loadingProviders, setLoadingProviders] = useState(false);
    const [providersFetched, setProvidersFetched] = useState(false);

    const [showMenu, setShowMenu] = useState(false);
    const isAdmin = localStorage.getItem(StorageKeys.isServerAdmin) === 'true';

    const loadData = useCallback(async (silent = false) => {
        if (!id) return;
        if (!silent) setLoading(true);

        try {
            const [libData, mediaData, collectionData] = await Promise.all([
                libraryService.getLibraryById(id, serverId),
                libraryService.getLibraryMedia(id, serverId),
                collectionService.getLibraryCollections(id, serverId)
            ]);

            setLibrary(libData);
            setCollections(collectionData);

            const sorted = [...mediaData].sort((a, b) => {
                const titleA = a.sortTitle || a.title;
                const titleB = b.sortTitle || b.title;
                return titleA.localeCompare(titleB);
            });
            setItems(sorted);
        } catch (error) {
            console.error(error);
        } finally {
            if (!silent) setLoading(false);
        }
    }, [id, serverId]);

    useEffect(() => {
        loadData();
    }, [loadData]);

    useEffect(() => {
        if (activeTab === 'recommendations' && id && !providersFetched) {
            setLoadingProviders(true);
            recommendationService.getProviders(serverId)
                .then(data => {
                    setProviders(data);
                    setProvidersFetched(true);
                })
                .catch(console.error)
                .finally(() => setLoadingProviders(false));
        }
    }, [activeTab, id, serverId, providersFetched]);

    useSignalREvent('LibraryUpdated', useCallback((updatedId: string) => {
        if (id && updatedId.toLowerCase() === id.toLowerCase()) loadData(true);
    }, [id, loadData]));

    const groupedItems = useMemo(() => {
        const groups: Record<string, LibraryItem[]> = {};
        items.forEach(item => {
            let firstLetter = (item.sortTitle || item.title).charAt(0).toUpperCase();
            if (!/[A-Z]/.test(firstLetter)) firstLetter = '#';
            if (!groups[firstLetter]) groups[firstLetter] = [];
            groups[firstLetter].push(item);
        });
        return groups;
    }, [items]);

    const availableLetters = useMemo(() => Object.keys(groupedItems).sort(), [groupedItems]);

    const samplePosters = useMemo(() => {
        const withPoster = items.filter(i => !!i.posterUrl);
        if (withPoster.length <= 6) return withPoster.map(i => i.posterUrl!);
        const step = Math.floor(withPoster.length / 6);
        return Array.from({ length: 6 }, (_, i) => withPoster[i * step].posterUrl!);
    }, [items]);

    const scrollToLetter = (letter: string) => {
        const element = document.getElementById(`letter-${letter}`);
        if (element) element.scrollIntoView({ behavior: 'smooth', block: 'start' });
    };

    const handleScan = () => { if (id) { libraryAdminService.triggerScan(id); setShowMenu(false); } };
    const handleRefresh = () => { if (id) { libraryAdminService.refreshMetadata(id); setShowMenu(false); } };
    const handleAnalyze = () => { if (id) { libraryAdminService.analyzeLibrary(id); setShowMenu(false); } };
    const handleEmptyTrash = async () => { await dialog.alert('Empty Trash is not implemented yet.'); setShowMenu(false); };

    const handleDelete = async () => {
        if (!id) return;
        if (await dialog.confirm({
            title: 'Delete library?',
            message: 'This will remove all associated media records from Vora. Your physical files on disk will NOT be touched.',
            confirmText: 'Delete',
            tone: 'danger',
        })) {
            try {
                await libraryAdminService.deleteLibrary(id);
                navigate('/');
            } catch (err) {
                await dialog.alert('Failed to delete library. Please check the console.');
                console.error(err);
            }
        }
        setShowMenu(false);
    };

    const handleDeleteMedia = async (e: React.MouseEvent, mediaId: string) => {
        e.stopPropagation();
        if (await dialog.confirm({
            title: 'Delete media item?',
            message: 'This removes it from the database. Your physical files are safe.',
            confirmText: 'Delete',
            tone: 'danger',
        })) {
            try {
                await libraryAdminService.deleteMediaItem(mediaId);
            } catch (error) {
                console.error(error);
                await dialog.alert('Failed to delete media item.');
            }
        }
    };

    if (loading) {
        return (
            <div>
                <div className="vora-skeleton mb-8 h-[280px] w-full" />
                <div className="px-8">
                    <div className="vora-skeleton mb-6 h-10 w-64" />
                    <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 2xl:grid-cols-8">
                        {Array.from({ length: 18 }, (_, i) => <div key={i} className="vora-skeleton aspect-[2/3]" />)}
                    </div>
                </div>
            </div>
        );
    }
    if (!library) {
        return (
            <EmptyState
                title="Library not found"
                description="The library you tried to open doesn't exist or you don't have access to it."
            />
        );
    }

    const adminMenu = isAdmin && (
        <div className="relative inline-block">
            <button
                type="button"
                onClick={() => setShowMenu(s => !s)}
                aria-label="Library actions"
                className="vora-button-secondary inline-flex h-10 w-10 cursor-pointer items-center justify-center"
                style={{ padding: 0 }}
            >
                <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><path d="M12 8c1.1 0 2-.9 2-2s-.9-2-2-2-2 .9-2 2 .9 2 2 2zm0 2c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2zm0 6c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2z" /></svg>
            </button>
            {showMenu && (
                <>
                    <div className="fixed inset-0 z-40" onClick={() => setShowMenu(false)} />
                    <div
                        className="absolute right-0 mt-2 w-56 overflow-hidden rounded-xl z-50"
                        style={{
                            background: 'var(--vora-bg-raised)',
                            border: '1px solid var(--vora-border-strong)',
                            boxShadow: 'var(--vora-shadow-lg)',
                        }}
                    >
                        <div className="py-1">
                            <button type="button" onClick={() => { navigate(`/admin/libraries/${id}/manage`); setShowMenu(false); }} className="block w-full cursor-pointer px-4 py-2.5 text-left text-sm transition-colors hover:bg-white/5" style={{ color: 'var(--vora-text-primary)' }}>Edit</button>
                            <button type="button" onClick={handleScan} className="block w-full cursor-pointer px-4 py-2.5 text-left text-sm transition-colors hover:bg-white/5" style={{ color: 'var(--vora-text-primary)' }}>Scan library files</button>
                            <button type="button" onClick={handleRefresh} className="block w-full cursor-pointer px-4 py-2.5 text-left text-sm transition-colors hover:bg-white/5" style={{ color: 'var(--vora-text-primary)' }}>Refresh metadata</button>
                            <button type="button" onClick={handleAnalyze} className="block w-full cursor-pointer px-4 py-2.5 text-left text-sm transition-colors hover:bg-white/5" style={{ color: 'var(--vora-text-primary)' }}>Analyze</button>
                            <div className="border-t" style={{ borderColor: 'var(--vora-border-subtle)' }} />
                            <button type="button" onClick={handleEmptyTrash} className="block w-full cursor-pointer px-4 py-2.5 text-left text-sm transition-colors hover:bg-white/5" style={{ color: 'var(--vora-text-primary)' }}>Empty trash</button>
                            <button type="button" onClick={handleDelete} className="block w-full cursor-pointer px-4 py-2.5 text-left text-sm font-medium transition-colors hover:bg-white/5" style={{ color: 'var(--vora-danger-text)' }}>Delete library</button>
                        </div>
                    </div>
                </>
            )}
        </div>
    );

    return (
        <div className="min-h-full pb-20">
            <LibraryHero library={library} totalItems={items.length} totalCollections={collections.length} samplePosters={samplePosters} />

            <div className="-mt-2 flex items-center justify-between gap-4 px-8">
                <Tabs<LibraryTabKey>
                    tabs={[
                        { key: 'library', label: 'Library' },
                        { key: 'collections', label: 'Collections', badge: collections.length > 0 ? <span className="rounded-full px-1.5 py-0.5 text-[10px] font-semibold" style={{ background: 'var(--vora-accent-soft)', color: 'var(--vora-accent-text)' }}>{collections.length}</span> : undefined },
                        { key: 'recommendations', label: 'Recommendations' },
                    ]}
                    active={activeTab}
                    onChange={setActiveTab}
                />
                {adminMenu}
            </div>

            {activeTab === 'library' && (
                <div className="relative pl-8 pr-14 pt-6" ref={gridRef}>
                    {items.length === 0 ? (
                        <EmptyState
                            title="This library is empty"
                            description={isAdmin ? "Run a scan from the menu to import media from the configured folders." : "Ask your server admin to scan in some media."}
                        />
                    ) : (
                        availableLetters.map(letter => (
                            <section key={letter} id={`letter-${letter}`} className="mb-10 scroll-mt-24">
                                <h2 className="m-0 mb-4 text-base font-semibold" style={{ color: 'var(--vora-text-muted)', letterSpacing: '0.04em' }}>{letter}</h2>
                                <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 2xl:grid-cols-8">
                                    {groupedItems[letter].map(item => {
                                        const isEpisode = item.type === 'Episode';
                                        const subtitle = item.type === 'TvShow'
                                            ? `${item.numberOfSeasons || 1} season${item.numberOfSeasons !== 1 ? 's' : ''}`
                                            : item.releaseDate
                                                ? new Date(item.releaseDate).getFullYear().toString()
                                                : 'Unknown year';
                                        const onOpen = () => navigate(serverId ? `/server/${serverId}/media/${item.id}` : `/media/${item.id}`);
                                        const unplayedBadge = item.unplayedItemCount && item.unplayedItemCount > 0
                                            ? <span className="rounded-full px-2 py-0.5 text-[10px] font-bold" style={{ background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)' }}>{item.unplayedItemCount}</span>
                                            : item.isPlayed
                                                ? <span className="inline-flex h-5 w-5 items-center justify-center rounded-full" style={{ background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)' }}>
                                                    <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3"><polyline points="20 6 9 17 4 12" /></svg>
                                                </span>
                                                : undefined;

                                        const card = isEpisode ? (
                                            <MediaStill imageUrl={item.posterUrl} title={item.title} subtitle={subtitle} onClick={onOpen} badge={unplayedBadge} fill />
                                        ) : (
                                            <MediaPoster imageUrl={item.posterUrl} title={item.title} subtitle={subtitle} onClick={onOpen} badge={unplayedBadge} fill />
                                        );

                                        if (!isAdmin) return <div key={item.id}>{card}</div>;

                                        return (
                                            <div key={item.id} className="group relative">
                                                {card}
                                                <button
                                                    type="button"
                                                    aria-label="Delete media item"
                                                    onClick={(e) => handleDeleteMedia(e, item.id)}
                                                    title="Delete media item"
                                                    className="absolute left-2 top-2 inline-flex h-9 w-9 cursor-pointer items-center justify-center rounded-full opacity-0 backdrop-blur-md transition-all hover:scale-105 group-hover:opacity-100 group-focus-within:opacity-100"
                                                    style={{
                                                        background: 'var(--vora-danger-500)',
                                                        color: '#ffffff',
                                                        border: '2px solid rgba(255, 255, 255, 0.95)',
                                                        boxShadow: '0 4px 14px rgba(0, 0, 0, 0.55)',
                                                    }}
                                                >
                                                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
                                                        <polyline points="3 6 5 6 21 6" />
                                                        <path d="M19 6l-1 14a2 2 0 01-2 2H8a2 2 0 01-2-2L5 6" />
                                                        <line x1="10" y1="11" x2="10" y2="17" />
                                                        <line x1="14" y1="11" x2="14" y2="17" />
                                                    </svg>
                                                </button>
                                            </div>
                                        );
                                    })}
                                </div>
                            </section>
                        ))
                    )}
                    {availableLetters.length > 0 && (
                        <aside className="fixed right-2 top-1/2 z-20 -translate-y-1/2">
                            <LetterRail available={availableLetters} onJump={scrollToLetter} />
                        </aside>
                    )}
                </div>
            )}

            {activeTab === 'collections' && (
                <div className="px-8 pt-6">
                    {collections.length === 0 ? (
                        <EmptyState
                            title="No collections in this library yet"
                            description="Collections group related media — like a movie franchise or a curated set."
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
            )}

            {activeTab === 'recommendations' && (
                <div className="pt-6">
                    {loadingProviders ? (
                        <div className="px-8">
                            <div className="vora-skeleton mb-4 h-6 w-48" />
                            <div className="vora-skeleton h-72 w-full" />
                        </div>
                    ) : providers.length === 0 ? (
                        <EmptyState
                            title="No recommendation engines enabled"
                            description="Ask your server admin to enable a recommendation provider in the admin Plugins page."
                        />
                    ) : (
                        <div>
                            {[...providers].sort((a, b) => {
                                if (a === 'openai_recommendations') return -1;
                                if (b === 'openai_recommendations') return 1;
                                return 0;
                            }).map(providerId => (
                                <AsyncLibraryProviderBlock key={providerId} providerId={providerId} libraryId={id!} serverId={serverId} />
                            ))}
                        </div>
                    )}
                </div>
            )}
        </div>
    );
}
