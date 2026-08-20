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
import { posterCaption } from '../../utils/posterCaption';
import LetterRail from '../../components/Client/Primitives/LetterRail';
import { StorageKeys } from '../../utils/storageKeys';

type LibraryTabKey = 'library' | 'collections' | 'recommendations';

type SortKey =
    | 'title'
    | 'releaseDate'
    | 'dateAdded'
    | 'adminRating'
    | 'myRating'
    | 'criticRating'
    | 'audienceRating'
    | 'contentRating'
    | 'duration'
    | 'resolution';

type FilterPreset = 'all' | 'unwatched' | 'inProgress' | 'watched';
type FilterCategory = 'genre' | 'year' | 'decade' | 'contentRating';
type Filter =
    | { kind: 'preset'; preset: FilterPreset }
    | { kind: 'category'; category: FilterCategory; value: string };
type SortDir = 'asc' | 'desc';

const SORT_LABELS: Record<SortKey, string> = {
    title: 'Title',
    releaseDate: 'Release Date',
    dateAdded: 'Date Added',
    adminRating: 'Admin Rating',
    myRating: 'My Rating',
    criticRating: 'Critic Rating',
    audienceRating: 'Audience Rating',
    contentRating: 'Content Rating',
    duration: 'Duration',
    resolution: 'Resolution',
};

const FILTER_PRESET_LABELS: Record<FilterPreset, string> = {
    all: 'All',
    unwatched: 'Unwatched',
    inProgress: 'In Progress',
    watched: 'Watched',
};

const FILTER_CATEGORY_LABELS: Record<FilterCategory, string> = {
    genre: 'Genre',
    year: 'Year',
    decade: 'Decade',
    contentRating: 'Content Rating',
};

const RESOLUTION_ORDER = ['8K', '4K', '2160p', '1080p', '720p', '576p', '480p', 'SD'];
function resolutionRank(res: string | undefined): number {
    if (!res) return -1;
    const r = res.toUpperCase();
    for (let i = 0; i < RESOLUTION_ORDER.length; i++) {
        if (r.includes(RESOLUTION_ORDER[i].toUpperCase())) return RESOLUTION_ORDER.length - i;
    }
    return 0;
}

function filterLabel(f: Filter): string {
    if (f.kind === 'preset') return FILTER_PRESET_LABELS[f.preset];
    return `${FILTER_CATEGORY_LABELS[f.category]}: ${f.value}`;
}

function renderLibraryCard(
    item: LibraryItem,
    ctx: {
        isAdmin: boolean;
        navigate: (to: string) => void;
        serverId?: string;
        handleDeleteMedia: (e: React.MouseEvent, id: string) => void;
    }
) {
    const { isAdmin, navigate, serverId, handleDeleteMedia } = ctx;
    const isEpisode = item.type === 'Episode';
    const cap = posterCaption(item);
    const onOpen = () => navigate(serverId ? `/server/${serverId}/media/${item.id}` : `/media/${item.id}`);
    const unplayedBadge = item.unplayedItemCount && item.unplayedItemCount > 0
        ? <span className="rounded-full px-2 py-0.5 text-[10px] font-bold" style={{ background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)' }}>{item.unplayedItemCount}</span>
        : item.isPlayed
            ? <span className="inline-flex h-5 w-5 items-center justify-center rounded-full" style={{ background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)' }}>
                <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3"><polyline points="20 6 9 17 4 12" /></svg>
            </span>
            : undefined;

    const card = isEpisode ? (
        <MediaStill imageUrl={item.posterUrl} title={cap.title} captionLines={cap.lines} onClick={onOpen} badge={unplayedBadge} fill />
    ) : (
        <MediaPoster imageUrl={item.posterUrl} title={cap.title} captionLines={cap.lines} onClick={onOpen} badge={unplayedBadge} fill />
    );

    if (!isAdmin) return <div key={item.id} className="[content-visibility:auto] [contain-intrinsic-size:180px_320px]">{card}</div>;

    return (
        <div key={item.id} className="group relative [content-visibility:auto] [contain-intrinsic-size:180px_320px]">
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
}

function LibraryToolbar({
    filter, onFilterChange,
    sortBy, onSortByChange,
    sortDir, onSortDirToggle,
    count,
    showFilterMenu, onToggleFilterMenu,
    showSortMenu, onToggleSortMenu,
    onCloseMenus,
    filterValues,
    filterSubmenu, onFilterSubmenuChange,
}: {
    filter: Filter;
    onFilterChange: (f: Filter) => void;
    sortBy: SortKey;
    onSortByChange: (s: SortKey) => void;
    sortDir: SortDir;
    onSortDirToggle: () => void;
    count: number;
    showFilterMenu: boolean;
    onToggleFilterMenu: () => void;
    showSortMenu: boolean;
    onToggleSortMenu: () => void;
    onCloseMenus: () => void;
    filterValues: Record<FilterCategory, string[]>;
    filterSubmenu: FilterCategory | null;
    onFilterSubmenuChange: (c: FilterCategory | null) => void;
}) {
    const pillBase = "inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 text-sm font-medium cursor-pointer transition-colors";
    const pillStyle: React.CSSProperties = {
        background: 'var(--vora-bg-surface)',
        border: '1px solid var(--vora-border-subtle)',
        color: 'var(--vora-text-primary)',
    };
    const menuStyle: React.CSSProperties = {
        background: 'var(--vora-bg-raised)',
        border: '1px solid var(--vora-border-strong)',
        boxShadow: 'var(--vora-shadow-lg)',
    };
    const presetOptions: FilterPreset[] = ['all', 'unwatched', 'inProgress', 'watched'];
    const categoryOptions: FilterCategory[] = ['genre', 'year', 'decade', 'contentRating'];
    const sortOptions: SortKey[] = ['title', 'releaseDate', 'dateAdded', 'adminRating', 'myRating', 'criticRating', 'audienceRating', 'contentRating', 'duration', 'resolution'];

    const isPresetActive = (p: FilterPreset) => filter.kind === 'preset' && filter.preset === p;
    const isCategoryValueActive = (cat: FilterCategory, value: string) =>
        filter.kind === 'category' && filter.category === cat && filter.value === value;

    const closeAndReset = () => { onFilterSubmenuChange(null); onCloseMenus(); };

    return (
        <div className="mb-6 flex flex-wrap items-center gap-2">
            <div className="relative inline-block">
                <button type="button" className={pillBase} style={pillStyle} onClick={() => { onFilterSubmenuChange(null); onToggleFilterMenu(); }}>
                    {filterLabel(filter)}
                    <svg width="10" height="10" viewBox="0 0 24 24" fill="currentColor"><path d="M7 10l5 5 5-5z" /></svg>
                </button>
                {showFilterMenu && (
                    <>
                        <div className="fixed inset-0 z-[150]" onClick={closeAndReset} />
                        <div className="absolute left-0 mt-2 w-64 overflow-hidden rounded-xl z-[200]" style={menuStyle}>
                            {filterSubmenu === null ? (
                                <div className="py-1 max-h-96 overflow-y-auto">
                                    {presetOptions.map(opt => (
                                        <button
                                            key={opt}
                                            type="button"
                                            onClick={() => { onFilterChange({ kind: 'preset', preset: opt }); closeAndReset(); }}
                                            className="flex w-full cursor-pointer items-center justify-between px-4 py-2.5 text-left text-sm transition-colors hover:bg-white/5"
                                            style={{ color: isPresetActive(opt) ? 'var(--vora-accent-text)' : 'var(--vora-text-primary)' }}
                                        >
                                            <span>{FILTER_PRESET_LABELS[opt]}</span>
                                            {isPresetActive(opt) && (
                                                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3"><polyline points="20 6 9 17 4 12" /></svg>
                                            )}
                                        </button>
                                    ))}
                                    <div className="my-1 border-t" style={{ borderColor: 'var(--vora-border-subtle)' }} />
                                    {categoryOptions.map(cat => {
                                        const values = filterValues[cat];
                                        if (values.length === 0) return null;
                                        return (
                                            <button
                                                key={cat}
                                                type="button"
                                                onClick={() => onFilterSubmenuChange(cat)}
                                                className="flex w-full cursor-pointer items-center justify-between px-4 py-2.5 text-left text-sm transition-colors hover:bg-white/5"
                                                style={{ color: 'var(--vora-text-primary)' }}
                                            >
                                                <span>{FILTER_CATEGORY_LABELS[cat]}</span>
                                                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="9 18 15 12 9 6" /></svg>
                                            </button>
                                        );
                                    })}
                                </div>
                            ) : (
                                <div className="py-1 max-h-96 overflow-y-auto">
                                    <button
                                        type="button"
                                        onClick={() => onFilterSubmenuChange(null)}
                                        className="flex w-full cursor-pointer items-center gap-2 px-4 py-2.5 text-left text-xs font-semibold uppercase tracking-wider transition-colors hover:bg-white/5"
                                        style={{ color: 'var(--vora-text-muted)' }}
                                    >
                                        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="15 18 9 12 15 6" /></svg>
                                        {FILTER_CATEGORY_LABELS[filterSubmenu]}
                                    </button>
                                    <div className="my-1 border-t" style={{ borderColor: 'var(--vora-border-subtle)' }} />
                                    {filterValues[filterSubmenu].map(value => (
                                        <button
                                            key={value}
                                            type="button"
                                            onClick={() => { onFilterChange({ kind: 'category', category: filterSubmenu, value }); closeAndReset(); }}
                                            className="flex w-full cursor-pointer items-center justify-between px-4 py-2.5 text-left text-sm transition-colors hover:bg-white/5"
                                            style={{ color: isCategoryValueActive(filterSubmenu, value) ? 'var(--vora-accent-text)' : 'var(--vora-text-primary)' }}
                                        >
                                            <span>{value}</span>
                                            {isCategoryValueActive(filterSubmenu, value) && (
                                                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3"><polyline points="20 6 9 17 4 12" /></svg>
                                            )}
                                        </button>
                                    ))}
                                </div>
                            )}
                        </div>
                    </>
                )}
            </div>

            <div className="relative inline-block">
                <button type="button" className={pillBase} style={pillStyle} onClick={onToggleSortMenu}>
                    {SORT_LABELS[sortBy]}
                    <svg width="10" height="10" viewBox="0 0 24 24" fill="currentColor"><path d="M7 10l5 5 5-5z" /></svg>
                </button>
                {showSortMenu && (
                    <>
                        <div className="fixed inset-0 z-[150]" onClick={onCloseMenus} />
                        <div className="absolute left-0 mt-2 w-52 overflow-hidden rounded-xl z-[200]" style={menuStyle}>
                            <div className="py-1 max-h-96 overflow-y-auto">
                                {sortOptions.map(opt => (
                                    <button
                                        key={opt}
                                        type="button"
                                        onClick={() => { onSortByChange(opt); onCloseMenus(); }}
                                        className="flex w-full cursor-pointer items-center justify-between px-4 py-2.5 text-left text-sm transition-colors hover:bg-white/5"
                                        style={{ color: sortBy === opt ? 'var(--vora-accent-text)' : 'var(--vora-text-primary)' }}
                                    >
                                        <span>{SORT_LABELS[opt]}</span>
                                        {sortBy === opt && (
                                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3"><polyline points="20 6 9 17 4 12" /></svg>
                                        )}
                                    </button>
                                ))}
                            </div>
                        </div>
                    </>
                )}
            </div>

            <button
                type="button"
                className={pillBase}
                style={pillStyle}
                onClick={onSortDirToggle}
                aria-label={sortDir === 'asc' ? 'Sort ascending — click to reverse' : 'Sort descending — click to reverse'}
                title={sortDir === 'asc' ? 'Ascending' : 'Descending'}
            >
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                    {sortDir === 'asc'
                        ? <path d="M12 5v14M5 12l7-7 7 7" />
                        : <path d="M12 19V5M5 12l7 7 7-7" />}
                </svg>
            </button>

            <span className="ml-1 text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                {count.toLocaleString()} {count === 1 ? 'item' : 'items'}
            </span>
        </div>
    );
}

function RecommendationsPanel({ providers, libraryId, serverId }: { providers: string[], libraryId: string, serverId?: string }) {
    const [results, setResults] = useState<Record<string, RecommendationListVM[]>>({});
    const [loaded, setLoaded] = useState(false);

    const orderedProviders = [...providers].sort((a, b) => {
        if (a === 'openai_recommendations') return -1;
        if (b === 'openai_recommendations') return 1;
        return 0;
    });

    useEffect(() => {
        let cancelled = false;
        setLoaded(false);
        Promise.all(providers.map(providerId =>
            recommendationService.getLibraryRecommendations(libraryId, providerId, serverId)
                .then(lists => ({ providerId, lists }))
                .catch(() => ({ providerId, lists: [] as RecommendationListVM[] }))
        )).then(all => {
            if (cancelled) return;
            const map: Record<string, RecommendationListVM[]> = {};
            all.forEach(r => { map[r.providerId] = r.lists; });
            setResults(map);
            setLoaded(true);
        });
        return () => { cancelled = true; };
    }, [providers, libraryId, serverId]);

    if (!loaded) {
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

    const totalRows = Object.values(results).reduce((n, lists) => n + lists.length, 0);
    if (totalRows === 0) {
        return (
            <EmptyState
                title="No recommendations yet"
                description="Recommendations are personalized from your viewing. Mark some titles as watched — and, for AI-powered picks, ask your admin to enable AI and generate embeddings — to see rows here."
            />
        );
    }

    return (
        <div>
            {orderedProviders.flatMap(providerId =>
                [...(results[providerId] ?? [])]
                    .sort((a, b) => a.weight - b.weight)
                    .map((list, index) => (
                        <RecommendationRow key={`${providerId}-${index}`} list={list} serverId={serverId} />
                    ))
            )}
        </div>
    );
}

function LibraryHero({ library, totalItems, totalCollections, samplePosters, actions }: { library: MediaLibrary, totalItems: number, totalCollections: number, samplePosters: string[], actions?: React.ReactNode }) {
    return (
        <header className="relative" style={{ minHeight: 280 }}>
            {/* Backdrop and gradient layers are wrapped in their own overflow-hidden box so
                the actions dropdown can escape the header bounds without being clipped. */}
            <div className="absolute inset-0 overflow-hidden">
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
            </div>
            <div className="relative px-8 pt-16 pb-14">
                <div className="inline-flex items-center gap-2 rounded-full px-3 py-1 text-xs font-medium" style={{ background: 'rgba(255,255,255,0.06)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-secondary)' }}>
                    <span style={{ width: 6, height: 6, borderRadius: '50%', background: 'var(--vora-accent-500)' }} />
                    Library · {library.type}
                </div>
                <div className="mt-3 flex items-center gap-4">
                    <h1 className="m-0 text-5xl font-semibold" style={{ color: 'var(--vora-text-primary)', letterSpacing: '-0.02em' }}>{library.name}</h1>
                    {actions}
                </div>
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

    // Filter and sort state, persisted per-library.
    const filterKey = id ? `vora_library_filter_${id}` : '';
    const sortKey = id ? `vora_library_sort_${id}` : '';
    const sortDirKey = id ? `vora_library_sortdir_${id}` : '';
    const [filter, setFilter] = useState<Filter>(() => {
        const saved = filterKey ? localStorage.getItem(filterKey) : null;
        if (saved) {
            try { return JSON.parse(saved) as Filter; } catch { /* fall through */ }
        }
        return { kind: 'preset', preset: 'all' };
    });
    const [sortBy, setSortBy] = useState<SortKey>(() => {
        const saved = sortKey ? localStorage.getItem(sortKey) : null;
        return (saved as SortKey) || 'title';
    });
    const [sortDir, setSortDir] = useState<SortDir>(() => {
        const saved = sortDirKey ? localStorage.getItem(sortDirKey) : null;
        return (saved as SortDir) || 'asc';
    });
    useEffect(() => { if (filterKey) localStorage.setItem(filterKey, JSON.stringify(filter)); }, [filter, filterKey]);
    useEffect(() => { if (sortKey) localStorage.setItem(sortKey, sortBy); }, [sortBy, sortKey]);
    useEffect(() => { if (sortDirKey) localStorage.setItem(sortDirKey, sortDir); }, [sortDir, sortDirKey]);
    const [showFilterMenu, setShowFilterMenu] = useState(false);
    const [showSortMenu, setShowSortMenu] = useState(false);
    const [filterSubmenu, setFilterSubmenu] = useState<FilterCategory | null>(null);

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

    // Unique values for category-based filters, derived from the loaded library.
    const filterValues = useMemo(() => {
        const genres = new Set<string>();
        const years = new Set<number>();
        const decades = new Set<number>();
        const contentRatings = new Set<string>();
        for (const i of items) {
            i.genres?.forEach(g => genres.add(g));
            if (i.releaseDate) {
                const y = new Date(i.releaseDate).getFullYear();
                if (!isNaN(y)) {
                    years.add(y);
                    decades.add(Math.floor(y / 10) * 10);
                }
            }
            if (i.contentRating) contentRatings.add(i.contentRating);
        }
        return {
            genre: Array.from(genres).sort((a, b) => a.localeCompare(b)),
            year: Array.from(years).sort((a, b) => b - a).map(String),
            decade: Array.from(decades).sort((a, b) => b - a).map(d => `${d}s`),
            contentRating: Array.from(contentRatings).sort((a, b) => a.localeCompare(b)),
        };
    }, [items]);

    // Apply filter then sort to the raw items list. Letter grouping only makes sense
    // when sorting by title, so the rendering branch checks sortBy below.
    const visibleItems = useMemo(() => {
        const year = (i: LibraryItem) => i.releaseDate ? new Date(i.releaseDate).getFullYear() : null;
        const matchesFilter = (i: LibraryItem): boolean => {
            if (filter.kind === 'preset') {
                switch (filter.preset) {
                    case 'unwatched': return i.isPlayed === false || (i.unplayedItemCount ?? 0) > 0;
                    case 'inProgress': return (i.unplayedItemCount ?? 0) > 0 && i.isPlayed !== false;
                    case 'watched': return i.isPlayed === true && (i.unplayedItemCount ?? 0) === 0;
                    case 'all':
                    default: return true;
                }
            }
            // category filter
            switch (filter.category) {
                case 'genre':
                    return (i.genres ?? []).includes(filter.value);
                case 'year': {
                    const y = year(i);
                    return y !== null && String(y) === filter.value;
                }
                case 'decade': {
                    const y = year(i);
                    if (y === null) return false;
                    const decade = Math.floor(y / 10) * 10;
                    return `${decade}s` === filter.value;
                }
                case 'contentRating':
                    return i.contentRating === filter.value;
            }
        };
        const filtered = items.filter(matchesFilter);
        const arr = [...filtered];
        arr.sort((a, b) => {
            let cmp = 0;
            switch (sortBy) {
                case 'title':
                    cmp = (a.sortTitle || a.title).localeCompare(b.sortTitle || b.title);
                    break;
                case 'releaseDate':
                    cmp = (a.releaseDate || '').localeCompare(b.releaseDate || '');
                    break;
                case 'dateAdded':
                    cmp = (a.addedAt || '').localeCompare(b.addedAt || '');
                    break;
                case 'adminRating':
                    cmp = (a.serverAdminRating ?? -1) - (b.serverAdminRating ?? -1);
                    break;
                case 'myRating':
                    cmp = (a.myRating ?? -1) - (b.myRating ?? -1);
                    break;
                case 'criticRating':
                    cmp = (a.thirdPartyRating1 ?? -1) - (b.thirdPartyRating1 ?? -1);
                    break;
                case 'audienceRating':
                    cmp = (a.thirdPartyRating2 ?? -1) - (b.thirdPartyRating2 ?? -1);
                    break;
                case 'contentRating':
                    cmp = (a.contentRating || '').localeCompare(b.contentRating || '');
                    break;
                case 'duration':
                    cmp = (a.durationSeconds ?? -1) - (b.durationSeconds ?? -1);
                    break;
                case 'resolution':
                    cmp = resolutionRank(a.resolution) - resolutionRank(b.resolution);
                    break;
            }
            return sortDir === 'asc' ? cmp : -cmp;
        });
        return arr;
    }, [items, filter, sortBy, sortDir]);

    const groupedItems = useMemo(() => {
        const groups: Record<string, LibraryItem[]> = {};
        visibleItems.forEach(item => {
            let firstLetter = (item.sortTitle || item.title).charAt(0).toUpperCase();
            if (!/[A-Z]/.test(firstLetter)) firstLetter = '#';
            if (!groups[firstLetter]) groups[firstLetter] = [];
            groups[firstLetter].push(item);
        });
        return groups;
    }, [visibleItems]);

    const availableLetters = useMemo(() => Object.keys(groupedItems).sort(), [groupedItems]);

    // The rail stays A→Z for navigation, but the rendered sections must follow
    // the sort direction — descending reverses the group order too, not just the
    // items within each group.
    const sectionLetters = useMemo(
        () => (sortDir === 'desc' ? [...availableLetters].reverse() : availableLetters),
        [availableLetters, sortDir]
    );

    const showAsLetterRail = sortBy === 'title';

    // For the hero we prefer backdrops (item.backgroundUrl) since posters carry overlay
    // badges that look bad as a wash. Fall back to posterUrl only when no backdrop exists.
    const samplePosters = useMemo(() => {
        const backdropItems = items.filter(i => !!i.backgroundUrl);
        const pool = backdropItems.length > 0
            ? backdropItems.map(i => i.backgroundUrl!)
            : items.filter(i => !!i.posterUrl).map(i => i.posterUrl!);
        if (pool.length <= 6) return pool;
        const step = Math.floor(pool.length / 6);
        return Array.from({ length: 6 }, (_, i) => pool[i * step]);
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
                    <div className="grid gap-4 grid-cols-[repeat(auto-fill,minmax(140px,192px))]">
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
                    <div className="fixed inset-0 z-[150]" onClick={() => setShowMenu(false)} />
                    <div
                        className="absolute right-0 mt-2 w-56 overflow-hidden rounded-xl z-[200]"
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
            <LibraryHero
                library={library}
                totalItems={items.length}
                totalCollections={collections.length}
                samplePosters={samplePosters}
                actions={adminMenu}
            />

            <div className="-mt-2 px-8">
                <Tabs<LibraryTabKey>
                    tabs={[
                        { key: 'library', label: 'Library' },
                        { key: 'collections', label: 'Collections', badge: collections.length > 0 ? <span className="rounded-full px-1.5 py-0.5 text-[10px] font-semibold" style={{ background: 'var(--vora-accent-soft)', color: 'var(--vora-accent-text)' }}>{collections.length}</span> : undefined },
                        { key: 'recommendations', label: 'Recommendations' },
                    ]}
                    active={activeTab}
                    onChange={setActiveTab}
                />
            </div>

            {activeTab === 'library' && (
                <div className="relative pl-8 pr-14 pt-6" ref={gridRef}>
                    {items.length === 0 ? (
                        <EmptyState
                            title="This library is empty"
                            description={isAdmin ? "Run a scan from the menu to import media from the configured folders." : "Ask your server admin to scan in some media."}
                        />
                    ) : (
                        <>
                            <LibraryToolbar
                                filter={filter}
                                onFilterChange={setFilter}
                                sortBy={sortBy}
                                onSortByChange={setSortBy}
                                sortDir={sortDir}
                                onSortDirToggle={() => setSortDir(d => d === 'asc' ? 'desc' : 'asc')}
                                count={visibleItems.length}
                                showFilterMenu={showFilterMenu}
                                onToggleFilterMenu={() => { setShowFilterMenu(v => !v); setShowSortMenu(false); }}
                                showSortMenu={showSortMenu}
                                onToggleSortMenu={() => { setShowSortMenu(v => !v); setShowFilterMenu(false); }}
                                onCloseMenus={() => { setShowFilterMenu(false); setShowSortMenu(false); setFilterSubmenu(null); }}
                                filterValues={filterValues}
                                filterSubmenu={filterSubmenu}
                                onFilterSubmenuChange={setFilterSubmenu}
                            />
                            {visibleItems.length === 0 ? (
                                <EmptyState
                                    title="No items match this filter"
                                    description="Try a different filter, or switch back to 'All'."
                                />
                            ) : showAsLetterRail ? (
                                sectionLetters.map(letter => (
                                    <section key={letter} id={`letter-${letter}`} className="mb-10 scroll-mt-24">
                                        <h2 className="m-0 mb-4 text-base font-semibold" style={{ color: 'var(--vora-text-muted)', letterSpacing: '0.04em' }}>{letter}</h2>
                                        <div className="grid gap-4 grid-cols-[repeat(auto-fill,minmax(140px,192px))]">
                                            {groupedItems[letter].map(item => renderLibraryCard(item, { isAdmin, navigate, serverId, handleDeleteMedia }))}
                                        </div>
                                    </section>
                                ))
                            ) : (
                                <div className="grid gap-4 grid-cols-[repeat(auto-fill,minmax(140px,192px))]">
                                    {visibleItems.map(item => renderLibraryCard(item, { isAdmin, navigate, serverId, handleDeleteMedia }))}
                                </div>
                            )}
                        </>
                    )}
                    {showAsLetterRail && availableLetters.length > 0 && (
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
                        <div className="grid gap-4 grid-cols-[repeat(auto-fill,minmax(140px,192px))]">
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
                        <RecommendationsPanel providers={providers} libraryId={id!} serverId={serverId} />
                    )}
                </div>
            )}
        </div>
    );
}
