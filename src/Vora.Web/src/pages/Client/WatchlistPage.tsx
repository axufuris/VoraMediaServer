import { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { discoveryService, type WatchlistItem } from '../../api/Discovery/discoveryService';
import PageHeader from '../../components/Client/Primitives/PageHeader';
import EmptyState from '../../components/Client/Primitives/EmptyState';
import MediaPoster from '../../components/Client/Primitives/MediaPoster';
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
        discoveryService.getWatchlist(activeProfileId, serverId)
            .then(setItems)
            .catch(console.error)
            .finally(() => setIsLoading(false));
    }, [activeProfileId, serverId]);

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
                    <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 2xl:grid-cols-8">
                        {Array.from({ length: 12 }, (_, i) => <div key={i} className="vora-skeleton aspect-[2/3]" />)}
                    </div>
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
                    <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 2xl:grid-cols-8">
                        {items.map(item => (
                            <MediaPoster
                                key={item.id}
                                imageUrl={item.posterUrl}
                                title={item.title}
                                subtitle={item.type === 'TvShow' ? 'TV Series' : 'Movie'}
                                onClick={() => navigate(serverId ? `/server/${serverId}/discovery/${item.providerId}/${item.type}/${item.externalId}` : `/discovery/${item.providerId}/${item.type}/${item.externalId}`)}
                                fill
                            />
                        ))}
                    </div>
                )}
            </div>
        </div>
    );
}
