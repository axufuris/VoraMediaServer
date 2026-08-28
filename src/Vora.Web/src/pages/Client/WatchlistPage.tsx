import { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { watchlistService, type WatchlistItem } from '../../api/Watchlist/watchlistService';
import PageHeader from '../../components/Client/Primitives/PageHeader';
import EmptyState from '../../components/Client/Primitives/EmptyState';
import MediaCard from '../../components/Client/Primitives/MediaCard';
import MediaGrid from '../../components/Client/Primitives/MediaGrid';
import { StorageKeys, getProfileIdFromToken } from '../../utils/storageKeys';

interface WatchlistPageProps {
    embedded?: boolean;
}

export default function WatchlistPage({ embedded = false }: WatchlistPageProps = {}) {
    const { serverId } = useParams<{ serverId?: string }>();
    const navigate = useNavigate();
    const [items, setItems] = useState<WatchlistItem[]>([]);
    const [isLoading, setIsLoading] = useState(true);

    const profileToken = localStorage.getItem(StorageKeys.profileToken);
    const activeProfileId = getProfileIdFromToken(profileToken) ?? '';

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

    // A bookmarked title that is in the library opens the local item, where it
    // can actually be played; anything else opens the provider page.
    const openPath = (item: WatchlistItem) => {
        const prefix = serverId ? `/server/${serverId}` : '';
        return item.mediaItemId
            ? `${prefix}/media/${item.mediaItemId}`
            : `${prefix}/discovery/${item.providerId}/${item.type}/${item.externalId}`;
    };

    return (
        <div className="min-h-full pb-20">
            {!embedded && (
                <PageHeader
                    title="My Watchlist"
                    subtitle={items.length > 0 ? `${items.length} item${items.length === 1 ? '' : 's'} you saved for later.` : 'Anything you bookmark from Discovery lands here.'}
                />
            )}

            <div className="px-8 pt-2">
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
                    <MediaGrid>
                        {items.map(item => (
                            <MediaCard
                                key={item.id}
                                imageUrl={item.posterUrl}
                                title={item.title}
                                captionLines={[item.type === 'TvShow' ? 'TV Series' : 'Movie']}
                                inWatchlist
                                onClick={() => navigate(openPath(item))}
                                fill
                            />
                        ))}
                    </MediaGrid>
                )}
            </div>
        </div>
    );
}
