import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { musicService, type AlbumSortOrder, type AlbumVM } from '../../../../api/Music/musicService';
import EmptyState from '../../../../components/Client/Primitives/EmptyState';
import { thumbUrl } from '../../../../utils/thumbnails';

const ALBUM_PAGE_SIZE = 60;

// A-Z first, and the default: a library of hundreds of albums is browsed by
// looking for a name far more often than by what arrived last.
const SORTS: { key: AlbumSortOrder; label: string }[] = [
    { key: 'Alphabetical', label: 'A–Z' },
    { key: 'RecentlyAdded', label: 'Recently added' },
    { key: 'Popular', label: 'Popular' },
];

interface MusicAlbumsViewProps {
    serverId?: string;
    refreshKey: number;
    onOpenAlbum: (album: AlbumVM) => void;
}

// The sort pills stay put while the grid underneath is keyed on everything that
// invalidates the loaded pages, so a new sort or a library change starts from a
// fresh first page instead of resetting state inside an effect.
export default function MusicAlbumsView({ serverId, refreshKey, onOpenAlbum }: MusicAlbumsViewProps) {
    const [sort, setSort] = useState<AlbumSortOrder>('Alphabetical');

    const sortPills = (
        <div role="radiogroup" aria-label="Sort albums" className="flex items-center gap-2">
            {SORTS.map(option => (
                <button
                    key={option.key}
                    type="button"
                    role="radio"
                    aria-checked={sort === option.key}
                    data-active={sort === option.key}
                    onClick={() => setSort(option.key)}
                    className="vora-pill cursor-pointer rounded-full px-3 py-1 text-xs font-medium"
                >
                    {option.label}
                </button>
            ))}
        </div>
    );

    return (
        <AlbumPages
            key={`${sort}|${serverId ?? ''}|${refreshKey}`}
            sort={sort}
            serverId={serverId}
            sortControl={sortPills}
            onOpenAlbum={onOpenAlbum}
        />
    );
}

interface AlbumPagesProps {
    sort: AlbumSortOrder;
    serverId?: string;
    sortControl: ReactNode;
    onOpenAlbum: (album: AlbumVM) => void;
}

function AlbumPages({ sort, serverId, sortControl, onOpenAlbum }: AlbumPagesProps) {
    const [albums, setAlbums] = useState<AlbumVM[]>([]);
    const [total, setTotal] = useState<number | null>(null);
    const [isLoading, setIsLoading] = useState(true);
    const [failed, setFailed] = useState(false);
    const inFlight = useRef(false);
    const sentinelRef = useRef<HTMLDivElement | null>(null);

    const fetchPage = useCallback(async (offset: number) => {
        try {
            const page = await musicService.getAlbums({ offset, limit: ALBUM_PAGE_SIZE, sort }, serverId);
            setAlbums(prev => {
                const seen = new Set(prev.map(a => a.id));
                return [...prev, ...page.items.filter(a => !seen.has(a.id))];
            });
            setTotal(page.total);
            setFailed(false);
        } catch (err) {
            console.error('Failed to load albums', err);
            setFailed(true);
        } finally {
            inFlight.current = false;
            setIsLoading(false);
        }
    }, [sort, serverId]);

    useEffect(() => {
        inFlight.current = true;
        void fetchPage(0);
    }, [fetchPage]);

    const hasMore = total === null || albums.length < total;

    const loadMore = useCallback(() => {
        if (inFlight.current || failed || !hasMore) return;
        inFlight.current = true;
        setIsLoading(true);
        void fetchPage(albums.length);
    }, [failed, hasMore, fetchPage, albums.length]);

    useEffect(() => {
        const node = sentinelRef.current;
        if (!node || typeof IntersectionObserver === 'undefined') return;
        const observer = new IntersectionObserver(
            entries => { if (entries.some(e => e.isIntersecting)) loadMore(); },
            { rootMargin: '0px 0px 600px 0px' },
        );
        observer.observe(node);
        return () => observer.disconnect();
    }, [loadMore]);

    const retry = () => {
        setFailed(false);
        inFlight.current = true;
        setIsLoading(true);
        void fetchPage(albums.length);
    };

    if (!isLoading && !failed && total === 0) {
        return (
            <div className="px-8">
                <div className="flex justify-end">{sortControl}</div>
                <EmptyState
                    title="No albums yet"
                    description="Create a Music library in Server Settings, point it at a folder of audio files, then trigger a scan."
                />
            </div>
        );
    }

    return (
        <div className="px-8">
            <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
                <div>
                    <h2 className="text-2xl font-bold" style={{ color: 'var(--vora-text-primary)' }}>All Albums</h2>
                    {total !== null && (
                        <p className="text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                            {total} {total === 1 ? 'album' : 'albums'}
                        </p>
                    )}
                </div>
                {sortControl}
            </div>

            <div>
                {albums.map(album => (
                    <AlbumRow key={album.id} album={album} onOpen={onOpenAlbum} />
                ))}
                {isLoading && Array.from({ length: albums.length === 0 ? 12 : 4 }, (_, i) => (
                    <div key={`skeleton-${i}`} className="flex items-center gap-4 p-2" aria-hidden="true">
                        <div className="vora-skeleton h-14 w-14 shrink-0 rounded" />
                        <div className="flex-1 space-y-2">
                            <div className="vora-skeleton h-3.5 w-40 rounded" />
                            <div className="vora-skeleton h-3 w-64 rounded" />
                        </div>
                    </div>
                ))}
            </div>

            <div ref={sentinelRef} className="flex justify-center py-6" aria-live="polite">
                {failed ? (
                    <div className="flex items-center gap-3 text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                        Couldn't load more albums.
                        <button type="button" onClick={retry} className="vora-pill cursor-pointer rounded-full px-3 py-1 text-xs font-medium">Try again</button>
                    </div>
                ) : hasMore && !isLoading ? (
                    <button type="button" onClick={loadMore} className="vora-pill cursor-pointer rounded-full px-4 py-1.5 text-xs font-medium">
                        Load more
                    </button>
                ) : null}
            </div>
        </div>
    );
}

// A list rather than a grid of small tiles: an album name needs horizontal room,
// and A-Z reads far better down one column than left-to-right across a grid. The
// artist leads because that is what the A-Z sort orders by, so the first column
// runs in the order the page claims to be in.
function AlbumRow({ album, onOpen }: { album: AlbumVM; onOpen: (album: AlbumVM) => void }) {
    return (
        <button
            type="button"
            onClick={() => onOpen(album)}
            title={`${album.artistName} — ${album.title}`}
            className="vora-row-interactive group flex w-full cursor-pointer items-center gap-4 rounded border border-transparent p-2 text-left transition-all"
        >
            <div
                className="flex h-14 w-14 shrink-0 items-center justify-center overflow-hidden rounded"
                style={{ background: 'var(--vora-bg-canvas)' }}
            >
                {album.artworkUrl ? (
                    <img
                        src={thumbUrl(album.artworkUrl, 200) ?? album.artworkUrl}
                        alt=""
                        className="h-full w-full object-cover"
                        loading="lazy"
                    />
                ) : (
                    <svg className="h-6 w-6" style={{ color: 'var(--vora-text-disabled)' }} fill="currentColor" viewBox="0 0 24 24">
                        <path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" />
                    </svg>
                )}
            </div>

            <div className="flex min-w-0 flex-1 flex-col gap-0.5 sm:flex-row sm:items-baseline sm:gap-4">
                <span
                    className="truncate font-medium transition-colors group-hover:text-[var(--vora-accent-text)] sm:w-1/3 sm:shrink-0"
                    style={{ color: 'var(--vora-text-primary)' }}
                >
                    {album.artistName}
                </span>
                <span className="truncate" style={{ color: 'var(--vora-text-secondary)' }}>
                    {album.title}
                </span>
            </div>

            <span
                className="shrink-0 text-sm"
                style={{ color: 'var(--vora-text-muted)', fontVariantNumeric: 'tabular-nums' }}
            >
                {album.year || ''}
            </span>
        </button>
    );
}
