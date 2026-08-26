import { useEffect, useState } from 'react';
import { useSearchParams, useNavigate, useParams } from 'react-router-dom';
import { searchService, type AggregatedGlobalSearchResponse } from '../../api/Discovery/searchService';
import { discoveryService, type DiscoveryItem } from '../../api/Discovery/discoveryService';
import { StorageKeys, SessionKeys, getProfileIdFromToken } from '../../utils/storageKeys';
import MediaCard from '../../components/Client/Primitives/MediaCard';
import PersonCard from '../../components/Client/Primitives/PersonCard';
import MediaGrid from '../../components/Client/Primitives/MediaGrid';
import PageHeader from '../../components/Client/Primitives/PageHeader';
import EmptyState from '../../components/Client/Primitives/EmptyState';

export default function SearchPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const [searchParams] = useSearchParams();
    const query = searchParams.get('q') || '';
    const navigate = useNavigate();

    const [results, setResults] = useState<AggregatedGlobalSearchResponse | null>(null);
    const [discoveryResults, setDiscoveryResults] = useState<DiscoveryItem[]>([]);
    const [isLoading, setIsLoading] = useState(false);

    useEffect(() => {
        if (!query || query.length < 3) return;

        const fetchResults = async () => {
            setIsLoading(true);
            try {
                const [localData, discData] = await Promise.all([
                    searchService.searchAllServers(query),
                    discoveryService.search(query, serverId)
                ]);

                setResults(localData);

                const localTitles = new Set([
                    ...localData.movies.map(m => m.title.toLowerCase()),
                    ...localData.tvShows.map(s => s.title.toLowerCase())
                ]);

                setDiscoveryResults(discData.filter(d => !localTitles.has(d.title.toLowerCase())));
            } catch {
                console.error("Failed to fetch search results");
            } finally {
                setIsLoading(false);
            }
        };

        fetchResults();
    }, [query, serverId]);

    const formatReleaseDisplay = (releaseDate?: string, fallbackYear?: number | null) => {
        if (!releaseDate) return fallbackYear ? fallbackYear.toString() : null;
        const date = new Date(releaseDate);
        if (date > new Date()) {
            return date.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
        }
        return date.getFullYear().toString();
    };

    if (isLoading) {
        return (
            <div className="flex min-h-full items-center justify-center pb-16">
                <div className="h-12 w-12 animate-spin rounded-full border-2 border-current border-t-transparent" style={{ color: 'var(--vora-accent-500)' }} />
            </div>
        );
    }

    if (!results) {
        return (
            <div className="min-h-full pb-16">
                <PageHeader title="Search" subtitle="Type at least 3 characters to search across all libraries." />
                <EmptyState
                    title="What are you looking for?"
                    description="Try a title, an actor, or a collection name."
                    icon={(
                        <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                            <circle cx="11" cy="11" r="8" />
                            <path d="m21 21-4.3-4.3" />
                        </svg>
                    )}
                />
            </div>
        );
    }

    const showServerLabels = results.serverCount > 1;
    const failedServerCount = results.failedServerIds.length;

    const navigateToMedia = (targetServerId: string, id: string) => {
        navigate(`/server/${targetServerId}/media/${id}`);
    };

    const navigateToActor = (targetServerId: string, id: string) => {
        navigate(`/server/${targetServerId}/actor/${id}`);
    };

    const navigateToCollection = (targetServerId: string, id: string) => {
        navigate(`/server/${targetServerId}/collection/${id}`);
    };

    const handleMusicNavigate = (targetServerId: string, artistId?: string, albumId?: string) => {
        try {
            const navState = albumId
                ? { view: 'album', albumId, artistId }
                : { view: 'artist', artistId };
            sessionStorage.setItem(SessionKeys.musicNavState, JSON.stringify(navState));
            const token = localStorage.getItem(StorageKeys.profileToken);
            const profileId = getProfileIdFromToken(token) ?? '';
            sessionStorage.setItem(SessionKeys.musicNavProfile, profileId || '');
        } catch { /* ignore */ }
        navigate(`/server/${targetServerId}/music`);
    };

    const navigateToDiscovery = (providerId: string, type: string, externalId: string) => {
        const basePath = serverId ? `/server/${serverId}` : '';
        navigate(`${basePath}/discovery/${providerId}/${type}/${externalId}`);
    };

    const hasResults = results.movies.length > 0 || results.tvShows.length > 0 || results.actors.length > 0 || results.collections.length > 0 || (results.music?.length ?? 0) > 0 || discoveryResults.length > 0;

    return (
        <div className="min-h-full pb-16">
            <PageHeader title={`Search results for "${query}"`} subtitle={hasResults ? 'Tap any result to open it.' : undefined} />

            <div className="px-8">
                {failedServerCount > 0 && (
                    <div
                        className="mb-6 rounded-md px-3 py-2 text-xs font-medium"
                        style={{ background: 'var(--vora-warning-soft)', border: '1px solid color-mix(in srgb, var(--vora-warning-500) 35%, transparent)', color: 'var(--vora-warning-text)' }}
                    >
                        Couldn't reach {failedServerCount} server{failedServerCount === 1 ? '' : 's'} — showing partial results.
                    </div>
                )}

                {!hasResults && (
                    <EmptyState
                        title="No results"
                        description="Nothing matched your query. Try a different spelling or shorter term."
                    />
                )}

                <div className="space-y-10">
                {results.movies.length > 0 && (
                    <MediaGrid title="Movies">
                            {results.movies.map(m => {
                                const release = formatReleaseDisplay(m.releaseDate);
                                const subtitle = showServerLabels
                                    ? (release ? `${release} • ${m.serverName}` : m.serverName)
                                    : release;
                                return (
                                    <MediaCard
                                        key={`${m.serverId}-${m.id}`}
                                        title={m.title}
                                        captionLines={subtitle ? [subtitle] : []}
                                        imageUrl={m.posterUrl}
                                        onClick={() => navigateToMedia(m.serverId, m.id)}
                                        fill
                                    />
                                );
                            })}
                    </MediaGrid>
                )}

                {results.tvShows.length > 0 && (
                    <MediaGrid title="TV Shows">
                            {results.tvShows.map(m => {
                                const release = formatReleaseDisplay(m.releaseDate);
                                const subtitle = showServerLabels
                                    ? (release ? `${release} • ${m.serverName}` : m.serverName)
                                    : release;
                                return (
                                    <MediaCard
                                        key={`${m.serverId}-${m.id}`}
                                        title={m.title}
                                        captionLines={subtitle ? [subtitle] : []}
                                        imageUrl={m.posterUrl}
                                        onClick={() => navigateToMedia(m.serverId, m.id)}
                                        fill
                                    />
                                );
                            })}
                    </MediaGrid>
                )}

                {results.actors.length > 0 && (
                    <MediaGrid title="People">
                            {results.actors.map(a => (
                                <PersonCard
                                    key={`${a.serverId}-${a.id}`}
                                    name={a.name}
                                    role={showServerLabels ? a.serverName : undefined}
                                    imageUrl={a.profileImageUrl}
                                    onClick={() => navigateToActor(a.serverId, a.id)}
                                />
                            ))}
                    </MediaGrid>
                )}

                {results.collections.length > 0 && (
                    <MediaGrid title="Collections">
                            {results.collections.map(c => (
                                <MediaCard
                                    key={`${c.serverId}-${c.id}`}
                                    title={c.title}
                                    captionLines={[showServerLabels ? `Collection • ${c.serverName}` : 'Collection']}
                                    imageUrl={c.posterUrl}
                                    onClick={() => navigateToCollection(c.serverId, c.id)}
                                    fill
                                />
                            ))}
                    </MediaGrid>
                )}

                {results.music && results.music.length > 0 && (
                    <MediaGrid title="Music">
                            {results.music.map(r => {
                                const baseSubtitle = `${r.type}${r.subtitle ? ` • ${r.subtitle}` : ''}`;
                                return (
                                    <MediaCard
                                        key={`${r.serverId}-${r.type}-${r.id}`}
                                        title={r.title}
                                        captionLines={[showServerLabels ? `${baseSubtitle} • ${r.serverName}` : baseSubtitle]}
                                        imageUrl={r.artworkUrl}
                                        shape={r.type === 'Artist' ? 'circle' : 'square'}
                                        onClick={() => handleMusicNavigate(r.serverId, r.artistId, r.albumId)}
                                        fill
                                    />
                                );
                            })}
                    </MediaGrid>
                )}

                {discoveryResults.length > 0 && (
                    <MediaGrid
                        title={(
                            <span className="inline-flex items-center gap-2" style={{ color: 'var(--vora-accent-text)' }}>
                                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75"><circle cx="12" cy="12" r="9" /><path d="m15 9-2 6-6 2 2-6 6-2z" /></svg>
                                Discovery (not in library)
                            </span>
                        )}
                    >
                            {discoveryResults.map(d => {
                                const release = formatReleaseDisplay(d.releaseDate, d.year);
                                return (
                                    <MediaCard
                                        key={d.externalId}
                                        title={d.title}
                                        captionLines={release ? [release] : []}
                                        imageUrl={d.posterUrl}
                                        onClick={() => navigateToDiscovery(d.providerId, d.type, d.externalId)}
                                        fill
                                    />
                                );
                            })}
                    </MediaGrid>
                )}
                </div>
            </div>
        </div>
    );
}
