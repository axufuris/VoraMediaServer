import { useState, useEffect, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { podcastService, type CatalogPodcastVM, type DiscoveredPodcastVM } from '../../../api/Podcasts/podcastService';
import { useDialog } from '../../../dialogs';
import FeatureToggle from '../../../components/Admin/Features/FeatureToggle';
import PageHeader from '../../../components/Admin/Primitives/PageHeader';
import EmptyState from '../../../components/Admin/Primitives/EmptyState';

function PodcastIcon({ className }: { className: string }) {
    return (
        <svg className={className} fill="currentColor" viewBox="0 0 24 24">
            <path d="M12 1a9 9 0 00-9 9v7c0 1.66 1.34 3 3 3h3v-8H5v-2a7 7 0 1114 0v2h-4v8h3c1.66 0 3-1.34 3-3v-7a9 9 0 00-9-9z" />
        </svg>
    );
}

export default function PodcastsAdminPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const dialog = useDialog();

    const [catalog, setCatalog] = useState<CatalogPodcastVM[]>([]);
    const [isLoading, setIsLoading] = useState(true);

    const [searchQuery, setSearchQuery] = useState('');
    const [searchResults, setSearchResults] = useState<DiscoveredPodcastVM[]>([]);
    const [isSearching, setIsSearching] = useState(false);
    const [hasSearched, setHasSearched] = useState(false);
    const [addingFeedUrl, setAddingFeedUrl] = useState<string | null>(null);
    const [addError, setAddError] = useState<string | null>(null);

    const loadCatalog = useCallback(async () => {
        setIsLoading(true);
        try {
            const list = await podcastService.getCatalog(serverId);
            setCatalog(list);
        } catch (error) {
            console.error('Failed to load podcast catalog', error);
        } finally {
            setIsLoading(false);
        }
    }, [serverId]);

    useEffect(() => {
        loadCatalog();
    }, [loadCatalog]);

    const isUrlLike = (value: string): boolean => /^(https?:|feed:)/i.test(value.trim());
    const trimmedQuery = searchQuery.trim();
    const queryIsUrl = isUrlLike(trimmedQuery);
    const isAddingDirect = addingFeedUrl !== null && addingFeedUrl === trimmedQuery;

    const addToCatalog = async (feedUrl: string) => {
        const url = feedUrl.trim();
        if (!url) return;

        setAddingFeedUrl(url);
        setAddError(null);
        try {
            await podcastService.addToCatalog(url, serverId);
            setSearchResults(prev => prev.filter(r => r.feedUrl.toLowerCase() !== url.toLowerCase()));
            if (queryIsUrl && trimmedQuery === url) setSearchQuery('');
            await loadCatalog();
        } catch (error: unknown) {
            const message = error instanceof Error ? error.message : 'Failed to add podcast.';
            setAddError(message);
        } finally {
            setAddingFeedUrl(null);
        }
    };

    useEffect(() => {
        if (!trimmedQuery || queryIsUrl) {
            setSearchResults([]);
            setHasSearched(false);
            return;
        }

        let cancelled = false;
        setIsSearching(true);
        const handle = setTimeout(async () => {
            try {
                const results = await podcastService.search(trimmedQuery, 25, serverId);
                if (!cancelled) {
                    setSearchResults(results);
                    setHasSearched(true);
                }
            } catch (error) {
                if (!cancelled) {
                    console.error('Search failed', error);
                    setSearchResults([]);
                    setHasSearched(true);
                }
            } finally {
                if (!cancelled) setIsSearching(false);
            }
        }, 400);

        return () => {
            cancelled = true;
            clearTimeout(handle);
            setIsSearching(false);
        };
    }, [trimmedQuery, queryIsUrl, serverId]);

    const removeFromCatalog = async (item: CatalogPodcastVM) => {
        const confirmed = await dialog.confirm(`Remove "${item.title}" from the curated catalog?`);
        if (!confirmed) return;

        try {
            await podcastService.removeFromCatalog(item.showId, serverId);
            setCatalog(prev => prev.filter(c => c.showId !== item.showId));
        } catch (error) {
            console.error('Failed to remove from catalog', error);
            await dialog.alert('Failed to remove from catalog.');
        }
    };

    const inCatalogUrls = new Set(catalog.map(c => c.feedUrl.toLowerCase()));

    return (
        <div data-vora-page="">
            <PageHeader
                title="Podcasts"
                description="Curate a catalog of approved shows and configure podcast-discovery plugins."
            />

            <div className="px-8 pt-6 pb-10 max-w-6xl mx-auto">
                <FeatureToggle
                    featureKey="podcasts"
                    label="Enable Podcasts"
                    description="When off, the Audio hub Podcasts tab is hidden from clients and the /api/podcasts endpoints return 403. The catalog below stays editable so admins can prep before turning it on."
                    serverId={serverId}
                />

                <section>
                        <p className="text-sm text-[var(--vora-text-muted)] mb-4">
                            Curate a list of approved podcasts. Profiles without the "Add custom podcast feeds" permission can only subscribe to shows in this catalog.
                        </p>

                        <div className="vora-card p-4 mb-6">
                            <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-accent-text)] mb-2">Add to catalog</label>
                            <div className="flex gap-2">
                                <input
                                    type="text"
                                    placeholder="Search by name or paste an RSS feed URL…"
                                    value={searchQuery}
                                    onChange={e => setSearchQuery(e.target.value)}
                                    onKeyDown={e => { if (e.key === 'Enter' && queryIsUrl) addToCatalog(trimmedQuery); }}
                                    className="vora-input flex-1"
                                />
                                {queryIsUrl && (
                                    <button
                                        type="button"
                                        onClick={() => addToCatalog(trimmedQuery)}
                                        disabled={isAddingDirect || !trimmedQuery}
                                        className="vora-button-primary"
                                    >
                                        {isAddingDirect ? 'Adding…' : 'Add to catalog'}
                                    </button>
                                )}
                            </div>
                            {addError && <p className="mt-2 text-sm text-[var(--vora-danger-text)]">{addError}</p>}

                            {!queryIsUrl && trimmedQuery.length > 0 && (
                                <div className="mt-4">
                                    {isSearching ? (
                                        <div className="text-sm text-[var(--vora-text-muted)] py-3">Searching iTunes…</div>
                                    ) : searchResults.length === 0 && hasSearched ? (
                                        <div className="text-sm text-[var(--vora-text-muted)] py-3">No matches.</div>
                                    ) : searchResults.length > 0 ? (
                                        <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 max-h-96 overflow-y-auto pr-1">
                                            {searchResults.map(result => {
                                                const isAdding = addingFeedUrl === result.feedUrl;
                                                const alreadyInCatalog = inCatalogUrls.has(result.feedUrl.toLowerCase());
                                                return (
                                                    <div
                                                        key={result.feedUrl}
                                                        className="flex items-center gap-3 p-2 bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-[var(--vora-radius-md)] hover:border-[var(--vora-border-strong)] transition-colors"
                                                    >
                                                        <div className="w-12 h-12 rounded bg-[var(--vora-bg-surface)] border border-[var(--vora-border-subtle)] flex items-center justify-center overflow-hidden shrink-0">
                                                            {result.artworkUrl
                                                                ? <img src={result.artworkUrl} alt={result.title} className="max-w-full max-h-full object-cover" />
                                                                : <PodcastIcon className="w-5 h-5 text-[var(--vora-text-disabled)]" />}
                                                        </div>
                                                        <div className="flex-1 min-w-0">
                                                            <div className="font-semibold text-sm text-[var(--vora-text-primary)] truncate" title={result.title}>{result.title}</div>
                                                            <div className="text-xs text-[var(--vora-text-muted)] truncate">{result.author || 'Unknown'}</div>
                                                        </div>
                                                        <button
                                                            type="button"
                                                            onClick={() => addToCatalog(result.feedUrl)}
                                                            disabled={isAdding || alreadyInCatalog}
                                                            className="text-xs px-3 py-1 vora-button-primary disabled:opacity-50 disabled:cursor-not-allowed shrink-0"
                                                        >
                                                            {alreadyInCatalog ? 'In catalog' : isAdding ? 'Adding…' : 'Add'}
                                                        </button>
                                                    </div>
                                                );
                                            })}
                                        </div>
                                    ) : null}
                                </div>
                            )}
                        </div>

                        <h2 className="text-xs font-bold uppercase tracking-widest text-[var(--vora-accent-text)] mb-3">
                            Curated catalog ({catalog.length})
                        </h2>
                        {isLoading ? (
                            <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                                {[1, 2, 3].map(i => <div key={i} className="vora-skeleton h-20" />)}
                            </div>
                        ) : catalog.length === 0 ? (
                            <div className="vora-card">
                                <EmptyState
                                    title="No podcasts yet"
                                    description="Use the search above to add shows to the curated catalog."
                                />
                            </div>
                        ) : (
                            <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                                {catalog.map(item => (
                                    <div
                                        key={item.showId}
                                        className="flex items-center gap-3 p-3 vora-card vora-card-interactive"
                                    >
                                        <div className="w-14 h-14 rounded bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] flex items-center justify-center overflow-hidden shrink-0">
                                            {item.artworkUrl
                                                ? <img src={item.artworkUrl} alt={item.title} className="max-w-full max-h-full object-cover" />
                                                : <PodcastIcon className="w-6 h-6 text-[var(--vora-text-disabled)]" />}
                                        </div>
                                        <div className="flex-1 min-w-0">
                                            <div className="font-semibold text-sm text-[var(--vora-text-primary)] truncate" title={item.title}>{item.title}</div>
                                            <div className="text-xs text-[var(--vora-text-muted)] truncate">{item.author || 'Unknown'}</div>
                                        </div>
                                        <button
                                            type="button"
                                            onClick={() => removeFromCatalog(item)}
                                            className="text-[var(--vora-text-muted)] hover:text-[var(--vora-danger-500)] transition-colors p-1 cursor-pointer"
                                            title="Remove from catalog"
                                        >
                                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6M1 7h22M9 7V4a1 1 0 011-1h4a1 1 0 011 1v3" /></svg>
                                        </button>
                                    </div>
                                ))}
                            </div>
                        )}
                </section>
            </div>
        </div>
    );
}
