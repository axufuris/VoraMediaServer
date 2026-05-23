import { useEffect, useState, type FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import { youtubeService, type YouTubeHomeFeed, type YouTubeVideo, type YouTubeContinueWatching } from '../../../api/YouTube/youtubeService';
import PageHeader from '../../../components/Client/Primitives/PageHeader';
import MediaRail from '../../../components/Client/Primitives/MediaRail';
import EmptyState from '../../../components/Client/Primitives/EmptyState';
import YouTubeVideoCard from '../../../components/YouTube/YouTubeVideoCard';
import { useDialog } from '../../../dialogs';
import { useYouTubeWatchedSet } from '../../../hooks/useYouTubeWatchedSet';

export default function YouTubePage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const dialog = useDialog();
    const watchedSet = useYouTubeWatchedSet(serverId);
    const isWatched = (videoId: string) => watchedSet.has(videoId);

    const [feed, setFeed] = useState<YouTubeHomeFeed | null>(null);
    const [isLoadingFeed, setIsLoadingFeed] = useState(true);
    const [feedError, setFeedError] = useState<string | null>(null);

    const [searchQuery, setSearchQuery] = useState('');
    const [searchResults, setSearchResults] = useState<YouTubeVideo[] | null>(null);
    const [searchNextPageToken, setSearchNextPageToken] = useState<string | undefined>(undefined);
    const [activeSearchQuery, setActiveSearchQuery] = useState('');
    const [isSearching, setIsSearching] = useState(false);
    const [isLoadingMore, setIsLoadingMore] = useState(false);

    useEffect(() => {
        let active = true;
        setIsLoadingFeed(true);
        setFeedError(null);

        youtubeService.getHomeFeed(serverId)
            .then((data) => {
                if (active) setFeed(data);
            })
            .catch((err: unknown) => {
                if (!active) return;
                const message = err instanceof Error ? err.message : 'Could not load your YouTube feed.';
                setFeedError(message);
            })
            .finally(() => {
                if (active) setIsLoadingFeed(false);
            });

        return () => { active = false; };
    }, [serverId]);

    const handleSearch = async (event: FormEvent<HTMLFormElement>) => {
        event.preventDefault();
        const query = searchQuery.trim();
        if (query.length === 0) {
            setSearchResults(null);
            setSearchNextPageToken(undefined);
            setActiveSearchQuery('');
            return;
        }

        setIsSearching(true);
        try {
            const page = await youtubeService.search(query, undefined, serverId);
            setSearchResults(page.videos);
            setSearchNextPageToken(page.nextPageToken);
            setActiveSearchQuery(query);
        } catch (err) {
            const message = err instanceof Error ? err.message : 'Search failed.';
            await dialog.alert({ title: 'YouTube', message });
        } finally {
            setIsSearching(false);
        }
    };

    const handleLoadMore = async () => {
        if (!searchNextPageToken || !activeSearchQuery) return;
        setIsLoadingMore(true);
        try {
            const page = await youtubeService.search(activeSearchQuery, searchNextPageToken, serverId);
            setSearchResults((prev) => [...(prev ?? []), ...page.videos]);
            setSearchNextPageToken(page.nextPageToken);
        } catch (err) {
            const message = err instanceof Error ? err.message : 'Could not load more results.';
            await dialog.alert({ title: 'YouTube', message });
        } finally {
            setIsLoadingMore(false);
        }
    };

    const clearSearch = () => {
        setSearchQuery('');
        setSearchResults(null);
        setSearchNextPageToken(undefined);
        setActiveSearchQuery('');
    };

    return (
        <div data-vora-page="" className="min-h-full pb-20">
            <PageHeader
                title="YouTube"
                subtitle="Browse trending, search, and watch — your subscriptions and history live in Vora, not in your Google account."
            />

            <div className="px-8 pt-2">
                <div className="mb-6 flex flex-wrap items-center justify-between gap-3">
                    <form onSubmit={handleSearch} className="flex flex-1 max-w-2xl items-center gap-2">
                        <input
                            type="search"
                            value={searchQuery}
                            onChange={(e) => setSearchQuery(e.target.value)}
                            placeholder="Search YouTube…"
                            className="vora-input flex-1"
                            aria-label="Search YouTube"
                        />
                        <button type="submit" className="vora-button-primary cursor-pointer" disabled={isSearching}>
                            {isSearching ? 'Searching…' : 'Search'}
                        </button>
                        {searchResults !== null && (
                            <button type="button" className="vora-button-secondary cursor-pointer" onClick={clearSearch}>
                                Clear
                            </button>
                        )}
                    </form>
                    <Link
                        to={serverId ? `/server/${serverId}/youtube/subscriptions` : '/youtube/subscriptions'}
                        className="vora-button-secondary cursor-pointer"
                    >
                        My subscriptions
                    </Link>
                </div>
            </div>

            {searchResults !== null ? (
                <SearchResultsGrid
                    results={searchResults}
                    serverId={serverId}
                    query={activeSearchQuery}
                    hasMore={Boolean(searchNextPageToken)}
                    isLoadingMore={isLoadingMore}
                    onLoadMore={handleLoadMore}
                    isWatched={isWatched}
                />
            ) : (
                <FeedSections feed={feed} isLoading={isLoadingFeed} error={feedError} serverId={serverId} isWatched={isWatched} />
            )}
        </div>
    );
}

interface FeedSectionsProps {
    feed: YouTubeHomeFeed | null;
    isLoading: boolean;
    error: string | null;
    serverId?: string;
    isWatched: (videoId: string) => boolean;
}

function FeedSections({ feed, isLoading, error, serverId, isWatched }: FeedSectionsProps) {
    if (isLoading) {
        return (
            <div className="space-y-8 px-8">
                {Array.from({ length: 3 }, (_, i) => (
                    <div key={i} className="space-y-3">
                        <div className="vora-skeleton h-5 w-40 rounded" />
                        <div className="flex gap-4 overflow-hidden">
                            {Array.from({ length: 6 }, (_, j) => (
                                <div key={j} className="vora-skeleton aspect-video rounded-md" style={{ width: 280, flex: '0 0 auto' }} />
                            ))}
                        </div>
                    </div>
                ))}
            </div>
        );
    }

    if (error) {
        return (
            <EmptyState
                title="YouTube isn’t available"
                description={error}
                icon={(
                    <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                        <circle cx="12" cy="12" r="10" />
                        <path d="M12 8v4" /><path d="M12 16h.01" />
                    </svg>
                )}
            />
        );
    }

    if (!feed) return null;

    if (feed.isFreshState && feed.trending.length === 0) {
        return (
            <EmptyState
                title="Nothing to show yet"
                description="Search for a creator or video above to get started — your subscriptions and watch history will build your home feed over time."
                icon={(
                    <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                        <rect x="3" y="3" width="18" height="18" rx="2" />
                        <polygon points="10 8 16 12 10 16 10 8" />
                    </svg>
                )}
            />
        );
    }

    return (
        <div className="space-y-10">
            {feed.continueWatching.length > 0 && (
                <ContinueWatchingRail items={feed.continueWatching} serverId={serverId} isWatched={isWatched} />
            )}

            {feed.fromSubscriptions.length > 0 && (
                <VideoRail title="From your subscriptions" videos={feed.fromSubscriptions} serverId={serverId} isWatched={isWatched} />
            )}

            {feed.recommendedForYou.length > 0 && (
                <VideoRail title="Recommended for you" videos={feed.recommendedForYou} serverId={serverId} isWatched={isWatched} />
            )}

            {feed.trending.length > 0 && (
                <VideoRail title="Trending" videos={feed.trending} serverId={serverId} isWatched={isWatched} />
            )}
        </div>
    );
}

interface ContinueWatchingRailProps {
    items: YouTubeContinueWatching[];
    serverId?: string;
    isWatched: (videoId: string) => boolean;
}

function ContinueWatchingRail({ items, serverId, isWatched }: ContinueWatchingRailProps) {
    return (
        <MediaRail title="Continue watching">
            {items.map((item) => (
                <YouTubeVideoCard
                    key={item.videoId}
                    videoId={item.videoId}
                    title={item.title}
                    thumbnailUrl={item.thumbnailUrl}
                    channelId={item.channelId}
                    channelName={item.channelName}
                    progressPercent={Math.round((item.percentComplete ?? 0) * 100)}
                    watched={isWatched(item.videoId)}
                    serverId={serverId}
                />
            ))}
        </MediaRail>
    );
}

interface VideoRailProps {
    title: string;
    videos: YouTubeVideo[];
    serverId?: string;
    isWatched: (videoId: string) => boolean;
}

function VideoRail({ title, videos, serverId, isWatched }: VideoRailProps) {
    return (
        <MediaRail title={title}>
            {videos.map((video) => (
                <YouTubeVideoCard
                    key={video.videoId}
                    videoId={video.videoId}
                    title={video.title}
                    thumbnailUrl={video.thumbnailUrl}
                    channelId={video.channelId}
                    channelName={video.channelName}
                    durationSeconds={video.durationSeconds}
                    viewCount={video.viewCount}
                    publishedAt={video.publishedAt}
                    watched={isWatched(video.videoId)}
                    serverId={serverId}
                />
            ))}
        </MediaRail>
    );
}

interface SearchResultsGridProps {
    results: YouTubeVideo[];
    serverId?: string;
    query: string;
    hasMore: boolean;
    isLoadingMore: boolean;
    onLoadMore: () => void;
    isWatched: (videoId: string) => boolean;
}

function SearchResultsGrid({ results, serverId, query, hasMore, isLoadingMore, onLoadMore, isWatched }: SearchResultsGridProps) {
    if (results.length === 0) {
        return (
            <EmptyState
                title="No results"
                description={`Nothing came back for “${query}”. Try a different phrase or check parental controls.`}
            />
        );
    }
    return (
        <div className="px-8">
            <h2 className="mb-4 text-lg font-semibold" style={{ color: 'var(--vora-text-primary)' }}>
                Results for “{query}”
            </h2>
            <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
                {results.map((video) => (
                    <YouTubeVideoCard
                        key={video.videoId}
                        videoId={video.videoId}
                        title={video.title}
                        thumbnailUrl={video.thumbnailUrl}
                        channelId={video.channelId}
                        channelName={video.channelName}
                        durationSeconds={video.durationSeconds}
                        viewCount={video.viewCount}
                        publishedAt={video.publishedAt}
                        watched={isWatched(video.videoId)}
                        serverId={serverId}
                    />
                ))}
            </div>
            {hasMore && (
                <div className="mt-8 flex justify-center">
                    <button
                        type="button"
                        onClick={onLoadMore}
                        disabled={isLoadingMore}
                        className="vora-button-secondary cursor-pointer"
                    >
                        {isLoadingMore ? 'Loading…' : 'Load more'}
                    </button>
                </div>
            )}
        </div>
    );
}
