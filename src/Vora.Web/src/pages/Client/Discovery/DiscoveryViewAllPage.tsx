import { useEffect, useState, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { discoveryService, type DiscoveryItem, type DiscoveryRowConfig } from '../../../api/Discovery/discoveryService';
import PageHeader from '../../../components/Client/Primitives/PageHeader';
import MediaPoster from '../../../components/Client/Primitives/MediaPoster';

export default function DiscoveryViewAllPage() {
    const { serverId, providerId, rowId } = useParams<{ serverId?: string, providerId: string, rowId: string }>();
    const navigate = useNavigate();

    const [items, setItems] = useState<DiscoveryItem[]>([]);
    const [page, setPage] = useState(1);
    const [isLoading, setIsLoading] = useState(false);
    const [hasMore, setHasMore] = useState(true);
    const [rowName, setRowName] = useState<string>('Loading…');
    const [watchlistIds, setWatchlistIds] = useState<Set<string>>(new Set());

    const observerTarget = useRef<HTMLDivElement>(null);

    const profileToken = localStorage.getItem('profile_token');
    const activeProfileId = profileToken ? JSON.parse(atob(profileToken.split('.')[1])).sub : '';

    useEffect(() => {
        const fetchInitialData = async () => {
            if (!providerId || !rowId) return;
            try {
                const configs = await discoveryService.getAdminConfigs(serverId);
                const matchingConfig = configs.find((c: DiscoveryRowConfig) => c.providerId === providerId && c.rowId === rowId);
                if (matchingConfig) setRowName(matchingConfig.name);

                if (activeProfileId) {
                    const wItems = await discoveryService.getWatchlist(activeProfileId, serverId);
                    setWatchlistIds(new Set(wItems.map(i => i.externalId)));
                }
            } catch (error) {
                console.error('Failed to load initial data', error);
            }
        };
        fetchInitialData();
    }, [providerId, rowId, serverId, activeProfileId]);

    useEffect(() => {
        if (!providerId || !rowId || !hasMore) return;
        const loadPage = async () => {
            setIsLoading(true);
            try {
                const newItems = await discoveryService.getRowItems(providerId, rowId, page, serverId);
                if (newItems.length === 0) {
                    setHasMore(false);
                } else {
                    setItems(prev => {
                        const existingIds = new Set(prev.map(i => i.externalId));
                        const uniqueNew = newItems.filter(i => !existingIds.has(i.externalId));
                        return [...prev, ...uniqueNew];
                    });
                }
            } catch (error) {
                console.error('Failed to fetch discovery items', error);
                setHasMore(false);
            } finally {
                setIsLoading(false);
            }
        };
        loadPage();
    }, [providerId, rowId, page, serverId]);

    useEffect(() => {
        const observer = new IntersectionObserver(
            entries => {
                if (entries[0].isIntersecting && !isLoading && hasMore) {
                    setPage(prev => prev + 1);
                }
            },
            { threshold: 0.1 }
        );
        if (observerTarget.current) observer.observe(observerTarget.current);
        return () => observer.disconnect();
    }, [isLoading, hasMore]);

    const backAction = (
        <button
            type="button"
            onClick={() => navigate(-1)}
            className="vora-button-secondary cursor-pointer inline-flex items-center gap-2"
        >
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="15 18 9 12 15 6" /></svg>
            Back
        </button>
    );

    return (
        <div className="min-h-full pb-20">
            <PageHeader title={rowName} subtitle="Browse everything in this discovery row." actions={backAction} />

            <div className="px-8 pt-2">
                <div className="grid grid-cols-2 gap-5 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 2xl:grid-cols-8">
                    {items.map(item => {
                        const inWatchlist = watchlistIds.has(item.externalId);
                        const badge = inWatchlist ? (
                            <span
                                className="inline-flex h-6 w-6 items-center justify-center rounded-full"
                                style={{ background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)' }}
                            >
                                <svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor"><path d="m19 21-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z" /></svg>
                            </span>
                        ) : undefined;
                        return (
                            <MediaPoster
                                key={item.externalId}
                                imageUrl={item.posterUrl}
                                title={item.title}
                                subtitle={item.year?.toString()}
                                onClick={() => navigate(serverId ? `/server/${serverId}/discovery/${providerId}/${item.type}/${item.externalId}` : `/discovery/${providerId}/${item.type}/${item.externalId}`)}
                                badge={badge}
                                fill
                            />
                        );
                    })}
                </div>

                <div ref={observerTarget} className="mt-10 flex h-12 items-center justify-center">
                    {isLoading && (
                        <div className="flex items-center gap-3 text-sm font-semibold" style={{ color: 'var(--vora-accent-text)' }}>
                            <div className="h-5 w-5 animate-spin rounded-full border-2 border-current border-t-transparent" />
                            <span>Loading more…</span>
                        </div>
                    )}
                    {!hasMore && items.length > 0 && (
                        <div className="text-sm font-medium" style={{ color: 'var(--vora-text-muted)' }}>You've reached the end of the list.</div>
                    )}
                </div>
            </div>
        </div>
    );
}
