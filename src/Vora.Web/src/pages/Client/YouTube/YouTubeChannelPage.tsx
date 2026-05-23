import { useCallback, useEffect, useRef, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { youtubeService, type YouTubeChannel, type YouTubeVideo, type YouTubePlaylist } from '../../../api/YouTube/youtubeService';
import PageHeader from '../../../components/Client/Primitives/PageHeader';
import EmptyState from '../../../components/Client/Primitives/EmptyState';
import YouTubeVideoCard from '../../../components/YouTube/YouTubeVideoCard';
import { useDialog } from '../../../dialogs';
import { useYouTubeWatchedSet } from '../../../hooks/useYouTubeWatchedSet';

export default function YouTubeChannelPage() {
    const { serverId, channelId } = useParams<{ serverId?: string; channelId: string }>();
    const navigate = useNavigate();
    const dialog = useDialog();
    const watchedSet = useYouTubeWatchedSet(serverId);
    const [channel, setChannel] = useState<YouTubeChannel | null>(null);
    const [videos, setVideos] = useState<YouTubeVideo[]>([]);
    const [nextPageToken, setNextPageToken] = useState<string | undefined>(undefined);
    const [playlists, setPlaylists] = useState<YouTubePlaylist[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [isLoadingMore, setIsLoadingMore] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [isToggling, setIsToggling] = useState(false);

    useEffect(() => {
        if (!channelId) return;
        let active = true;
        setIsLoading(true);
        setError(null);
        setVideos([]);
        setNextPageToken(undefined);
        setPlaylists([]);

        (async () => {
            try {
                const channelData = await youtubeService.getChannel(channelId, serverId);
                if (!active) return;
                setChannel(channelData);
                setVideos(channelData.recentUploads);

                try {
                    const uploadsPage = await youtubeService.getChannelUploads(channelId, undefined, serverId);
                    if (!active) return;
                    if (uploadsPage.videos.length > 0) {
                        setVideos(uploadsPage.videos);
                        setNextPageToken(uploadsPage.nextPageToken);
                    }
                } catch {
                    // Uploads paging is best-effort; the recentUploads fallback from channelData is already rendered.
                }

                try {
                    const playlistList = await youtubeService.getChannelPlaylists(channelId, serverId);
                    if (active) setPlaylists(playlistList);
                } catch {
                    // Playlists are optional; ignore failures.
                }
            } catch (err: unknown) {
                if (!active) return;
                setError(err instanceof Error ? err.message : 'Could not load channel.');
            } finally {
                if (active) setIsLoading(false);
            }
        })();

        return () => { active = false; };
    }, [channelId, serverId]);

    const handleLoadMore = useCallback(async () => {
        if (!channelId || !nextPageToken) return;
        setIsLoadingMore(true);
        try {
            const page = await youtubeService.getChannelUploads(channelId, nextPageToken, serverId);
            setVideos((prev) => [...prev, ...page.videos]);
            setNextPageToken(page.nextPageToken);
        } catch (err) {
            const message = err instanceof Error ? err.message : 'Could not load more videos.';
            await dialog.alert({ title: 'YouTube', message });
        } finally {
            setIsLoadingMore(false);
        }
    }, [channelId, nextPageToken, serverId, dialog]);

    const handleToggleSubscription = async () => {
        if (!channel) return;
        setIsToggling(true);
        try {
            if (channel.isSubscribed) {
                await youtubeService.unsubscribe(channel.channelId, serverId);
                setChannel({ ...channel, isSubscribed: false });
            } else {
                await youtubeService.subscribe(channel.channelId, serverId);
                setChannel({ ...channel, isSubscribed: true });
            }
        } catch (err) {
            const message = err instanceof Error ? err.message : 'Could not update subscription.';
            await dialog.alert({ title: 'YouTube', message });
        } finally {
            setIsToggling(false);
        }
    };

    if (isLoading) {
        return (
            <div data-vora-page="" className="min-h-full px-8 pt-16">
                <div className="vora-skeleton h-8 w-64 rounded" />
            </div>
        );
    }

    if (error || !channel) {
        return (
            <div data-vora-page="" className="min-h-full">
                <PageHeader title="Channel" subtitle="" />
                <EmptyState
                    title={error ? 'Channel unavailable' : 'Not found'}
                    description={error ?? 'This channel could not be loaded.'}
                    action={(
                        <button type="button" className="vora-button-secondary cursor-pointer" onClick={() => navigate(serverId ? `/server/${serverId}/youtube` : '/youtube')}>
                            Back to YouTube
                        </button>
                    )}
                />
            </div>
        );
    }

    const backToYouTube = () => navigate(serverId ? `/server/${serverId}/youtube` : '/youtube');

    return (
        <div data-vora-page="" className="min-h-full pb-20">
            <div className="flex items-center justify-between gap-4 px-8 pt-6">
                <button
                    type="button"
                    onClick={backToYouTube}
                    className="flex cursor-pointer items-center gap-2 text-sm"
                    style={{ color: 'var(--vora-text-muted)' }}
                >
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                        <polyline points="15 18 9 12 15 6" />
                    </svg>
                    Back to YouTube
                </button>
            </div>

            <PageHeader
                title={channel.title}
                subtitle={[
                    channel.subscriberCount != null ? `${formatCompact(channel.subscriberCount)} subscribers` : null,
                    channel.videoCount != null ? `${formatCompact(channel.videoCount)} videos` : null,
                ].filter(Boolean).join(' • ')}
                actions={(
                    <button
                        type="button"
                        onClick={handleToggleSubscription}
                        disabled={isToggling}
                        className={channel.isSubscribed ? 'vora-button-secondary cursor-pointer' : 'vora-button-primary cursor-pointer'}
                    >
                        {channel.isSubscribed ? 'Subscribed' : 'Subscribe'}
                    </button>
                )}
            />

            <div className="px-8 pt-2">
                {channel.description && (
                    <p className="mb-6 max-w-3xl text-sm" style={{ color: 'var(--vora-text-secondary)' }}>
                        {channel.description}
                    </p>
                )}

                {playlists.length > 0 && <PlaylistsRail playlists={playlists} />}

                <h2 className="mb-4 text-lg font-semibold" style={{ color: 'var(--vora-text-primary)' }}>
                    Uploads
                </h2>

                {videos.length === 0 ? (
                    <EmptyState title="No uploads" description="This channel hasn't published any videos." />
                ) : (
                    <>
                        <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
                            {videos.map((video) => (
                                <YouTubeVideoCard
                                    key={video.videoId}
                                    videoId={video.videoId}
                                    title={video.title}
                                    thumbnailUrl={video.thumbnailUrl}
                                    channelId={video.channelId || channel.channelId}
                                    channelName={video.channelName || channel.title}
                                    durationSeconds={video.durationSeconds}
                                    viewCount={video.viewCount}
                                    publishedAt={video.publishedAt}
                                    watched={watchedSet.has(video.videoId)}
                                    serverId={serverId}
                                />
                            ))}
                        </div>
                        {nextPageToken && (
                            <div className="mt-8 flex justify-center">
                                <button
                                    type="button"
                                    onClick={handleLoadMore}
                                    disabled={isLoadingMore}
                                    className="vora-button-secondary cursor-pointer"
                                >
                                    {isLoadingMore ? 'Loading…' : 'Load more'}
                                </button>
                            </div>
                        )}
                    </>
                )}
            </div>
        </div>
    );
}

function formatCompact(value: number): string {
    if (value >= 1_000_000_000) return `${(value / 1_000_000_000).toFixed(1).replace(/\.0$/, '')}B`;
    if (value >= 1_000_000) return `${(value / 1_000_000).toFixed(1).replace(/\.0$/, '')}M`;
    if (value >= 1_000) return `${(value / 1_000).toFixed(1).replace(/\.0$/, '')}K`;
    return String(value);
}

function PlaylistsRail({ playlists }: { playlists: YouTubePlaylist[] }) {
    const scrollerRef = useRef<HTMLDivElement | null>(null);

    const scrollBy = (delta: number) => {
        const el = scrollerRef.current;
        if (!el) return;
        el.scrollBy({ left: delta, behavior: 'smooth' });
    };

    return (
        <section className="mb-10">
            <div className="mb-4 flex items-center justify-between gap-3">
                <h2 className="m-0 text-lg font-semibold" style={{ color: 'var(--vora-text-primary)' }}>
                    Playlists
                </h2>
                <div className="flex items-center gap-2">
                    <button
                        type="button"
                        aria-label="Scroll playlists left"
                        onClick={() => scrollBy(-600)}
                        className="hidden cursor-pointer items-center justify-center rounded-full p-2 transition-colors hover:bg-white/5 md:inline-flex"
                        style={{ color: 'var(--vora-text-muted)' }}
                    >
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="15 18 9 12 15 6" /></svg>
                    </button>
                    <button
                        type="button"
                        aria-label="Scroll playlists right"
                        onClick={() => scrollBy(600)}
                        className="hidden cursor-pointer items-center justify-center rounded-full p-2 transition-colors hover:bg-white/5 md:inline-flex"
                        style={{ color: 'var(--vora-text-muted)' }}
                    >
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="9 18 15 12 9 6" /></svg>
                    </button>
                </div>
            </div>
            <div
                ref={scrollerRef}
                className="flex gap-4 overflow-x-auto pb-2"
                style={{ scrollSnapType: 'x mandatory', scrollbarWidth: 'none' }}
            >
                {playlists.map((playlist) => (
                    <a
                        key={playlist.playlistId}
                        href={playlist.youTubeUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="group block cursor-pointer text-left"
                        style={{ width: 280, flex: '0 0 auto', scrollSnapAlign: 'start', textDecoration: 'none', color: 'inherit' }}
                        title="Opens the playlist on YouTube"
                    >
                        <div
                            className="relative overflow-hidden"
                            style={{
                                aspectRatio: '16 / 9',
                                borderRadius: 'var(--vora-radius-md)',
                                boxShadow: 'var(--vora-shadow-md)',
                                background: 'var(--vora-bg-surface)',
                                border: '1px solid var(--vora-border-subtle)',
                            }}
                        >
                            {playlist.thumbnailUrl ? (
                                <img src={playlist.thumbnailUrl} alt={playlist.title} loading="lazy" className="h-full w-full object-cover" />
                            ) : (
                                <div className="flex h-full w-full items-center justify-center" style={{ color: 'var(--vora-text-muted)' }}>
                                    <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                                        <path d="M4 6h16M4 12h13M4 18h9" />
                                        <path d="M19 14v6m-3-3h6" />
                                    </svg>
                                </div>
                            )}
                            {playlist.itemCount != null && playlist.itemCount > 0 && (
                                <span
                                    className="absolute right-2 bottom-2 rounded px-1.5 py-0.5 text-[11px] font-medium"
                                    style={{ background: 'rgba(0, 0, 0, 0.78)', color: 'var(--vora-text-primary)' }}
                                >
                                    {playlist.itemCount} video{playlist.itemCount === 1 ? '' : 's'}
                                </span>
                            )}
                        </div>
                        <div className="mt-2.5 truncate text-sm font-medium" style={{ color: 'var(--vora-text-primary)' }} title={playlist.title}>
                            {playlist.title}
                        </div>
                    </a>
                ))}
            </div>
        </section>
    );
}
