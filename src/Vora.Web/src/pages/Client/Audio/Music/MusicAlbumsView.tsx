import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { musicService, type AlbumSortOrder, type AlbumVM } from '../../../../api/Music/musicService';
import MediaCard from '../../../../components/Client/Primitives/MediaCard';
import MediaGrid from '../../../../components/Client/Primitives/MediaGrid';
import EmptyState from '../../../../components/Client/Primitives/EmptyState';
import { albumCaption } from './musicCaptions';

const ALBUM_PAGE_SIZE = 60;

// A-Z first, and the default: a library of hundreds of albums is browsed by
// looking for a name far more often than by what arrived last.
const SORTS: { key: AlbumSortOrder; label: string }[] = [
    { key: 'Alphabetical', label: 'A–Z' },
    { key: 'RecentlyAdded', label: 'Recently added' },
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
            <MediaGrid
                size="xs"
                title="All Albums"
                subtitle={total === null ? undefined : `${total} ${total === 1 ? 'album' : 'albums'}`}
                actions={sortControl}
            >
                {albums.map(album => (
                    <MediaCard
                        key={album.id}
                        item={albumCaption(album)}
                        imageUrl={album.artworkUrl}
                        shape="square"
                        size="xs"
                        fill
                        onClick={() => onOpenAlbum(album)}
                    />
                ))}
                {isLoading && Array.from({ length: albums.length === 0 ? 18 : 6 }, (_, i) => (
                    <div key={`skeleton-${i}`} className="vora-skeleton aspect-square" aria-hidden="true" />
                ))}
            </MediaGrid>

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
