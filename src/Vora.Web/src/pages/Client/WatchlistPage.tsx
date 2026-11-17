import { useCallback, useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { watchlistService, type WatchlistItem } from '../../api/Watchlist/watchlistService';
import PageHeader from '../../components/Client/Primitives/PageHeader';
import EmptyState from '../../components/Client/Primitives/EmptyState';
import MediaCard from '../../components/Client/Primitives/MediaCard';
import MediaGrid from '../../components/Client/Primitives/MediaGrid';
import { LibraryStatusBadge } from '../../components/Client/Primitives/WatchedBadge';
import { profileIdForServer } from '../../api/client';
import { discoveryTarget } from '../../utils/discoveryNavigation';
import { useWatchlistUpdated } from '../../hooks/useWatchlistUpdated';
import { groupWatchlist, isShowEntry } from '../../utils/watchlistGroups';
import { posterCaption } from '../../utils/posterCaption';

interface WatchlistPageProps {
    embedded?: boolean;
}

function badgeFor(item: WatchlistItem) {
    return !item.mediaItemId && item.isRequested ? <LibraryStatusBadge status="requested" /> : undefined;
}

export default function WatchlistPage({ embedded = false }: WatchlistPageProps = {}) {
    const { serverId } = useParams<{ serverId?: string }>();
    const navigate = useNavigate();
    const [items, setItems] = useState<WatchlistItem[]>([]);
    const [isLoading, setIsLoading] = useState(true);

    const activeProfileId = profileIdForServer(serverId) ?? '';
    const groups = groupWatchlist(items);

    useEffect(() => {
        if (!activeProfileId) {
            queueMicrotask(() => setIsLoading(false));
            return;
        }
        watchlistService.getWatchlist(serverId)
            .then(setItems)
            .catch(console.error)
            .finally(() => setIsLoading(false));
    }, [activeProfileId, serverId]);

    const reload = useCallback(() => {
        watchlistService.getWatchlist(serverId).then(setItems).catch(console.error);
    }, [serverId]);

    // A title added or removed anywhere on this profile, this tab included.
    useWatchlistUpdated(serverId, reload);

    // A bookmarked title that is in the library opens the local item, where it
    // can actually be played; anything else opens the provider page.
    const openPath = (item: WatchlistItem) => {
        return discoveryTarget(item, serverId);
    };

    return (
        <div className="min-h-full pb-20">
            {!embedded && (
                <PageHeader
                    title="My Watchlist"
                    subtitle={items.length > 0 ? `${items.length} item${items.length === 1 ? '' : 's'} you saved for later.` : 'Anything you bookmark from Discovery lands here.'}
                />
            )}

            <div className="px-8 max-md:px-4 pt-2">
                {isLoading ? (
                    <MediaGrid>
                        {Array.from({ length: 12 }, (_, i) => <div key={i} className="vora-skeleton aspect-[2/3]" />)}
                    </MediaGrid>
                ) : items.length === 0 ? (
                    <EmptyState
                        title="Your watchlist is empty"
                        description="Tap the bookmark on anything in Discovery and it will show up here."
                        icon={(
                            <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                                <path d="m19 21-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z" />
                            </svg>
                        )}
                    />
                ) : (
                    groups.map(group => (
                        <section key={group.key} className="mb-10">
                            <h2 className="m-0 mb-4 text-base font-semibold" style={{ color: 'var(--vora-text-muted)', letterSpacing: '0.04em' }}>{group.title}</h2>
                            <MediaGrid>
                                {group.items.map(item => (
                                    <MediaCard
                                        key={item.id}
                                        imageUrl={item.posterUrl}
                                        title={item.title}
                                        captionLines={posterCaption({ type: isShowEntry(item) ? 'TvShow' : 'Movie', title: item.title, releaseDate: item.releaseDate }).lines}
                                        badge={badgeFor(item)}
                                        onClick={() => navigate(openPath(item))}
                                        menu={item.mediaItemId ? { id: item.mediaItemId, serverId, onChanged: reload } : undefined}
                                        fill
                                    />
                                ))}
                            </MediaGrid>
                        </section>
                    ))
                )}
            </div>
        </div>
    );
}
