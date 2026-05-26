import { useEffect, useState } from 'react';
import { useSearchParams, useNavigate, useParams } from 'react-router-dom';
import { searchService, type AggregatedGlobalSearchResponse } from '../../api/Discovery/searchService';
import { discoveryService, type DiscoveryItem } from '../../api/Discovery/discoveryService';
import { StorageKeys, SessionKeys, getProfileIdFromToken } from '../../utils/storageKeys';
import MediaCard from '../../components/Media/MediaCard';
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
            sessionStorage.setItem('audio_active_tab', 'Music');
            const token = localStorage.getItem(StorageKeys.profileToken);
            const profileId = getProfileIdFromToken(token) ?? '';
            sessionStorage.setItem(SessionKeys.musicNavProfile, profileId || '');
        } catch { /* ignore */ }
        navigate(`/server/${targetServerId}/audio`);
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
                    <section>
                        <h2 className="m-0 mb-4 pb-2 text-lg font-semibold" style={{ color: 'var(--vora-text-primary)', borderBottom: '1px solid var(--vora-border-subtle)', letterSpacing: '-0.01em' }}>Movies</h2>
                        <div className="flex flex-wrap gap-4">
                            {results.movies.map(m => {
                                const release = formatReleaseDisplay(m.releaseDate);
                                const subtitle = showServerLabels
                                    ? (release ? `${release} • ${m.serverName}` : m.serverName)
                                    : (release ?? undefined);
                                return (
                                    <div key={`${m.serverId}-${m.id}`} className="w-32 sm:w-40 shrink-0">
                                        <MediaCard
                                            id={m.id}
                                            title={m.title}
                                            subtitle={subtitle}
                                            imageUrl={m.posterUrl}
                                            type={m.type}
                                            onClick={() => navigateToMedia(m.serverId, m.id)}
                                        />
                                    </div>
                                );
                            })}
                        </div>
                    </section>
                )}

                {results.tvShows.length > 0 && (
                    <section>
                        <h2 className="m-0 mb-4 pb-2 text-lg font-semibold" style={{ color: 'var(--vora-text-primary)', borderBottom: '1px solid var(--vora-border-subtle)', letterSpacing: '-0.01em' }}>TV Shows</h2>
                        <div className="flex flex-wrap gap-4">
                            {results.tvShows.map(m => {
                                const release = formatReleaseDisplay(m.releaseDate);
                                const subtitle = showServerLabels
                                    ? (release ? `${release} • ${m.serverName}` : m.serverName)
                                    : (release ?? undefined);
                                return (
                                    <div key={`${m.serverId}-${m.id}`} className="w-32 sm:w-40 shrink-0">
                                        <MediaCard
                                            id={m.id}
                                            title={m.title}
                                            subtitle={subtitle}
                                            imageUrl={m.posterUrl}
                                            type={m.type}
                                            onClick={() => navigateToMedia(m.serverId, m.id)}
                                        />
                                    </div>
                                );
                            })}
                        </div>
                    </section>
                )}

                {results.actors.length > 0 && (
                    <section>
                        <h2 className="m-0 mb-4 pb-2 text-lg font-semibold" style={{ color: 'var(--vora-text-primary)', borderBottom: '1px solid var(--vora-border-subtle)', letterSpacing: '-0.01em' }}>People</h2>
                        <div className="flex flex-wrap gap-4">
                            {results.actors.map(a => (
                                <div key={`${a.serverId}-${a.id}`} onClick={() => navigateToActor(a.serverId, a.id)} className="flex flex-col items-center cursor-pointer group w-28 sm:w-32 shrink-0">
                                    <div className="w-24 h-24 sm:w-28 sm:h-28 rounded-full overflow-hidden bg-[var(--vora-bg-sunken)] mb-3 border-2 border-transparent group-hover:border-[var(--vora-accent-500)] transition-all shadow-lg">
                                        {a.profileImageUrl ? (
                                            <img src={a.profileImageUrl} alt={a.name} className="w-full h-full object-cover" />
                                        ) : (
                                            <svg className="w-full h-full text-[var(--vora-text-muted)] p-4" fill="currentColor" viewBox="0 0 20 20"><path fillRule="evenodd" d="M10 9a3 3 0 100-6 3 3 0 000 6zm-7 9a7 7 0 1114 0H3z" clipRule="evenodd" /></svg>
                                        )}
                                    </div>
                                    <span className="text-[var(--vora-text-secondary)] font-medium text-center text-sm group-hover:text-[var(--vora-text-primary)] transition-colors">{a.name}</span>
                                    {showServerLabels && <span className="text-xs text-[var(--vora-text-muted)] mt-0.5">{a.serverName}</span>}
                                </div>
                            ))}
                        </div>
                    </section>
                )}

                {results.collections.length > 0 && (
                    <section>
                        <h2 className="m-0 mb-4 pb-2 text-lg font-semibold" style={{ color: 'var(--vora-text-primary)', borderBottom: '1px solid var(--vora-border-subtle)', letterSpacing: '-0.01em' }}>Collections</h2>
                        <div className="flex flex-wrap gap-4">
                            {results.collections.map(c => (
                                <div key={`${c.serverId}-${c.id}`} className="w-32 sm:w-40 shrink-0">
                                    <MediaCard
                                        id={c.id}
                                        title={c.title}
                                        subtitle={showServerLabels ? `Collection • ${c.serverName}` : 'Collection'}
                                        imageUrl={c.posterUrl}
                                        type="Collection"
                                        onClick={() => navigateToCollection(c.serverId, c.id)}
                                    />
                                </div>
                            ))}
                        </div>
                    </section>
                )}

                {results.music && results.music.length > 0 && (
                    <section>
                        <h2 className="m-0 mb-4 pb-2 text-lg font-semibold" style={{ color: 'var(--vora-text-primary)', borderBottom: '1px solid var(--vora-border-subtle)', letterSpacing: '-0.01em' }}>Music</h2>
                        <div className="flex flex-wrap gap-4">
                            {results.music.map(r => {
                                const baseSubtitle = `${r.type}${r.subtitle ? ` • ${r.subtitle}` : ''}`;
                                const subtitle = showServerLabels ? `${baseSubtitle} • ${r.serverName}` : baseSubtitle;
                                return (
                                    <div
                                        key={`${r.serverId}-${r.type}-${r.id}`}
                                        onClick={() => handleMusicNavigate(r.serverId, r.artistId, r.albumId)}
                                        className="w-32 sm:w-40 shrink-0 cursor-pointer group"
                                    >
                                        <div className={`w-full aspect-square bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] group-hover:border-[var(--vora-accent-500)] transition-all overflow-hidden flex items-center justify-center mb-3 ${r.type === 'Artist' ? 'rounded-full' : 'rounded'}`}>
                                            {r.artworkUrl
                                                ? <img src={r.artworkUrl} alt={r.title} className="w-full h-full object-cover" />
                                                : <svg className="w-10 h-10 text-[var(--vora-text-muted)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg>}
                                        </div>
                                        <div className="text-sm font-bold text-[var(--vora-text-secondary)] truncate" title={r.title}>{r.title}</div>
                                        <div className="text-xs text-[var(--vora-text-muted)] truncate">{subtitle}</div>
                                    </div>
                                );
                            })}
                        </div>
                    </section>
                )}

                {discoveryResults.length > 0 && (
                    <section>
                        <h2 className="m-0 mb-4 flex items-center gap-2 pb-2 text-lg font-semibold" style={{ color: 'var(--vora-accent-text)', borderBottom: '1px solid var(--vora-border-subtle)', letterSpacing: '-0.01em' }}>
                            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75"><circle cx="12" cy="12" r="9" /><path d="m15 9-2 6-6 2 2-6 6-2z" /></svg>
                            Discovery (not in library)
                        </h2>
                        <div className="flex flex-wrap gap-4">
                            {discoveryResults.map(d => (
                                <div key={d.externalId} className="w-32 sm:w-40 shrink-0">
                                    <MediaCard
                                        id={d.externalId}
                                        title={d.title}
                                        subtitle={formatReleaseDisplay(d.releaseDate, d.year) ?? undefined}
                                        imageUrl={d.posterUrl}
                                        type={d.type}
                                        onClick={() => navigateToDiscovery(d.providerId, d.type, d.externalId)}
                                    />
                                </div>
                            ))}
                        </div>
                    </section>
                )}
                </div>
            </div>
        </div>
    );
}
