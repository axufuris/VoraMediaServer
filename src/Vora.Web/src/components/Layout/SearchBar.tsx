import { useState, useEffect, useRef } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { searchService, type AggregatedGlobalSearchResponse } from '../../api/Discovery/searchService';
import { discoveryService, type DiscoveryItem } from '../../api/Discovery/discoveryService';

export default function SearchBar() {
    const { serverId } = useParams<{ serverId?: string }>();
    const navigate = useNavigate();
    const [query, setQuery] = useState('');
    const [localResults, setLocalResults] = useState<AggregatedGlobalSearchResponse | null>(null);
    const [discoveryResults, setDiscoveryResults] = useState<DiscoveryItem[]>([]);

    const [isLoading, setIsLoading] = useState(false);
    const [isOpen, setIsOpen] = useState(false);
    const dropdownRef = useRef<HTMLDivElement>(null);

    useEffect(() => {
        if (query.trim().length < 3) {
            setLocalResults(null);
            setDiscoveryResults([]);
            setIsOpen(false);
            return;
        }

        const timer = setTimeout(async () => {
            setIsLoading(true);
            try {
                const [localData, discData] = await Promise.all([
                    searchService.searchAllServers(query),
                    discoveryService.search(query, serverId)
                ]);

                setLocalResults(localData);

                const localTitles = new Set([
                    ...localData.movies.map(m => m.title.toLowerCase()),
                    ...localData.tvShows.map(s => s.title.toLowerCase())
                ]);

                setDiscoveryResults(discData.filter(d => !localTitles.has(d.title.toLowerCase())));
                setIsOpen(true);
            } catch (error) {
                console.error("Search failed", error);
            } finally {
                setIsLoading(false);
            }
        }, 400);

        return () => clearTimeout(timer);
    }, [query, serverId]);

    useEffect(() => {
        const handleClickOutside = (event: MouseEvent) => {
            if (dropdownRef.current && !dropdownRef.current.contains(event.target as Node)) {
                setIsOpen(false);
            }
        };
        document.addEventListener('mousedown', handleClickOutside);
        return () => document.removeEventListener('mousedown', handleClickOutside);
    }, []);

    const handleViewMore = () => {
        setIsOpen(false);
        navigate(`/search?q=${encodeURIComponent(query)}`);
    };

    const handleItemClick = (path: string) => {
        setIsOpen(false);
        setQuery('');
        navigate(path);
    };

    const formatReleaseDisplay = (releaseDate?: string, fallbackYear?: number | null) => {
        if (!releaseDate) return fallbackYear ? fallbackYear.toString() : 'Unknown';
        const date = new Date(releaseDate);
        if (date > new Date()) {
            return date.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
        }
        return date.getFullYear().toString();
    };

    const totalLocalResults = localResults ?
        localResults.movies.length + localResults.tvShows.length + localResults.actors.length + localResults.collections.length + (localResults.music?.length ?? 0)
        : 0;

    const showServerLabels = (localResults?.serverCount ?? 0) > 1;
    const failedServerCount = localResults?.failedServerIds.length ?? 0;
    const appendServer = (base: string, serverName: string): string =>
        showServerLabels ? `${base} • ${serverName}` : base;

    const navigateToMusic = (targetServerId: string, artistId?: string, albumId?: string) => {
        try {
            const navState = albumId
                ? { view: 'album', albumId, artistId }
                : { view: 'artist', artistId };
            sessionStorage.setItem('music_nav_state', JSON.stringify(navState));
            sessionStorage.setItem('audio_active_tab', 'Music');
            const token = localStorage.getItem('profile_token');
            const profileId = token ? JSON.parse(atob(token.split('.')[1])).sub : '';
            sessionStorage.setItem('music_nav_profile', profileId || '');
        } catch { /* ignore */ }
        handleItemClick(`/server/${targetServerId}/audio`);
    };

    const totalResults = totalLocalResults + discoveryResults.length;

    return (
        <div className="relative w-full max-w-xl" ref={dropdownRef}>
            <div className="relative flex items-center">
                <svg
                    className="absolute left-3 h-5 w-5"
                    fill="none"
                    stroke="currentColor"
                    viewBox="0 0 24 24"
                    style={{ color: 'var(--vora-text-muted)' }}
                >
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                </svg>
                <input
                    type="text"
                    className="w-full rounded-full py-2 pl-10 pr-10 transition-all focus:outline-none"
                    style={{
                        background: 'var(--vora-bg-surface)',
                        border: '1px solid var(--vora-border-subtle)',
                        color: 'var(--vora-text-primary)',
                    }}
                    placeholder="Search movies, shows, actors..."
                    value={query}
                    onChange={(e) => setQuery(e.target.value)}
                    onFocus={(e) => {
                        e.currentTarget.style.borderColor = 'var(--vora-accent-500)';
                        e.currentTarget.style.boxShadow = '0 0 0 3px var(--vora-accent-focus-ring)';
                        if (query.trim().length >= 3) setIsOpen(true);
                    }}
                    onBlur={(e) => {
                        e.currentTarget.style.borderColor = 'var(--vora-border-subtle)';
                        e.currentTarget.style.boxShadow = 'none';
                    }}
                />
                {query && (
                    <button
                        type="button"
                        onClick={() => { setQuery(''); setIsOpen(false); setLocalResults(null); setDiscoveryResults([]); }}
                        className="absolute right-3 cursor-pointer transition-colors"
                        style={{ color: 'var(--vora-text-muted)' }}
                    >
                        <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                        </svg>
                    </button>
                )}
            </div>

            {isOpen && query.length >= 3 && (
                <div
                    className="absolute left-0 right-0 top-full z-50 mt-2 overflow-hidden rounded-xl"
                    style={{
                        background: 'var(--vora-bg-raised)',
                        border: '1px solid var(--vora-border-strong)',
                        boxShadow: 'var(--vora-shadow-overlay)',
                    }}
                >
                    <div
                        className="flex items-center justify-between p-3"
                        style={{ borderBottom: '1px solid var(--vora-border-subtle)', background: 'color-mix(in srgb, var(--vora-bg-canvas) 50%, transparent)' }}
                    >
                        <h3 className="m-0 text-sm font-semibold" style={{ color: 'var(--vora-text-secondary)' }}>Top results for "{query}"</h3>
                        {isLoading && <div className="h-4 w-4 animate-spin rounded-full border-2 border-current border-t-transparent" style={{ color: 'var(--vora-accent-500)' }} />}
                    </div>

                    {failedServerCount > 0 && (
                        <div
                            className="px-3 py-2 text-xs"
                            style={{
                                background: 'var(--vora-warning-soft)',
                                borderBottom: '1px solid color-mix(in srgb, var(--vora-warning-500) 35%, transparent)',
                                color: 'var(--vora-warning-text)',
                            }}
                        >
                            Couldn't reach {failedServerCount} server{failedServerCount === 1 ? '' : 's'} — showing partial results.
                        </div>
                    )}

                    <div className="max-h-[60vh] overflow-y-auto">
                        {totalResults === 0 && !isLoading ? (
                            <div className="p-4 text-center text-sm" style={{ color: 'var(--vora-text-muted)' }}>No results found.</div>
                        ) : (
                            <ul className="py-1">
                                {localResults?.movies.slice(0, 3).map(m => (
                                    <DropdownItem key={`m-${m.serverId}-${m.id}`} title={m.title} subtitle={appendServer(`${formatReleaseDisplay(m.releaseDate)} • Movie`, m.serverName)} imageUrl={m.posterUrl} onClick={() => handleItemClick(`/server/${m.serverId}/media/${m.id}`)} />
                                ))}
                                {localResults?.tvShows.slice(0, 3).map(s => (
                                    <DropdownItem key={`s-${s.serverId}-${s.id}`} title={s.title} subtitle={appendServer('Show', s.serverName)} imageUrl={s.posterUrl} onClick={() => handleItemClick(`/server/${s.serverId}/media/${s.id}`)} />
                                ))}
                                {localResults?.actors.slice(0, 3).map(a => (
                                    <DropdownItem key={`a-${a.serverId}-${a.id}`} title={a.name} subtitle={appendServer('Actor', a.serverName)} imageUrl={a.profileImageUrl} isRound onClick={() => handleItemClick(`/server/${a.serverId}/actor/${a.id}`)} />
                                ))}
                                {localResults?.collections.slice(0, 3).map(c => (
                                    <DropdownItem key={`c-${c.serverId}-${c.id}`} title={c.title} subtitle={appendServer('Collection', c.serverName)} imageUrl={c.posterUrl} onClick={() => handleItemClick(`/server/${c.serverId}/collection/${c.id}`)} />
                                ))}
                                {localResults?.music?.slice(0, 3).map(m => (
                                    <DropdownItem
                                        key={`music-${m.serverId}-${m.type}-${m.id}`}
                                        title={m.title}
                                        subtitle={appendServer(m.subtitle ? `${m.type} • ${m.subtitle}` : m.type, m.serverName)}
                                        imageUrl={m.artworkUrl}
                                        isRound={m.type === 'Artist'}
                                        onClick={() => navigateToMusic(m.serverId, m.artistId, m.albumId)}
                                    />
                                ))}

                                {discoveryResults.length > 0 && (
                                    <>
                                        {totalLocalResults > 0 && <div className="mx-4 my-2" style={{ borderTop: '1px solid var(--vora-border-subtle)' }} />}
                                        <div
                                            className="flex items-center gap-2 px-4 py-1.5 text-xs font-bold uppercase tracking-widest"
                                            style={{ background: 'color-mix(in srgb, var(--vora-bg-surface) 50%, transparent)', color: 'var(--vora-text-muted)' }}
                                        >
                                            <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24" style={{ color: 'var(--vora-accent-500)' }}>
                                                <circle cx="12" cy="12" r="9" />
                                                <path d="m15 9-2 6-6 2 2-6 6-2z" />
                                            </svg>
                                            Discovery
                                        </div>
                                        {discoveryResults.map(d => (
                                            <DropdownItem
                                                key={`disc-${d.externalId}`}
                                                title={d.title}
                                                subtitle={`${formatReleaseDisplay(d.releaseDate, d.year)} • ${d.type === 'TvShow' ? 'Show' : 'Movie'} • Not in Library`}
                                                imageUrl={d.posterUrl}
                                                onClick={() => handleItemClick(serverId ? `/server/${serverId}/discovery/${d.providerId}/${d.type}/${d.externalId}` : `/discovery/${d.providerId}/${d.type}/${d.externalId}`)}
                                            />
                                        ))}
                                    </>
                                )}
                            </ul>
                        )}
                    </div>

                    {totalLocalResults > 0 && (
                        <div
                            className="p-3"
                            style={{ background: 'color-mix(in srgb, var(--vora-bg-canvas) 70%, transparent)', borderTop: '1px solid var(--vora-border-subtle)' }}
                        >
                            <button
                                type="button"
                                onClick={handleViewMore}
                                className="w-full cursor-pointer rounded-md py-2 text-sm font-semibold transition-colors"
                                style={{ background: 'var(--vora-accent-soft)', color: 'var(--vora-accent-text)', border: '1px solid var(--vora-accent-soft-hover)' }}
                            >
                                View more results
                            </button>
                        </div>
                    )}
                </div>
            )}
        </div>
    );
}

function DropdownItem({ title, subtitle, imageUrl, isRound, onClick }: { title: string, subtitle: string, imageUrl?: string, isRound?: boolean, onClick: () => void }) {
    return (
        <li
            onClick={onClick}
            className="flex cursor-pointer items-center gap-3 px-4 py-2 transition-colors hover:bg-white/5"
        >
            <div
                className={`flex shrink-0 items-center justify-center overflow-hidden ${isRound ? 'h-12 w-12 rounded-full' : 'h-14 w-10 rounded'}`}
                style={{ background: 'var(--vora-bg-sunken)', border: isRound ? 'none' : '1px solid var(--vora-border-subtle)' }}
            >
                {imageUrl ? (
                    <img src={imageUrl} alt={title} className="h-full w-full object-cover" />
                ) : (
                    <svg className="h-6 w-6" fill="currentColor" viewBox="0 0 20 20" style={{ color: 'var(--vora-text-disabled)' }}>
                        <path fillRule="evenodd" d="M4 3a2 2 0 00-2 2v10a2 2 0 002 2h12a2 2 0 002-2V5a2 2 0 00-2-2H4zm12 12H4l4-8 3 6 2-4 3 6z" clipRule="evenodd" />
                    </svg>
                )}
            </div>
            <div className="flex flex-col overflow-hidden">
                <span className="truncate text-sm font-medium" style={{ color: 'var(--vora-text-primary)' }}>{title}</span>
                <span className="truncate text-xs" style={{ color: 'var(--vora-text-muted)' }}>{subtitle}</span>
            </div>
        </li>
    );
}
