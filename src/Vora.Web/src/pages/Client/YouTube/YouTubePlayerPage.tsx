import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { youtubeService, type YouTubeVideo } from '../../../api/YouTube/youtubeService';
import YouTubePlayerEmbed from '../../../components/YouTube/YouTubePlayerEmbed';
import EmptyState from '../../../components/Client/Primitives/EmptyState';
import { useDialog } from '../../../dialogs';

export default function YouTubePlayerPage() {
    const { serverId, videoId } = useParams<{ serverId?: string; videoId: string }>();
    const navigate = useNavigate();
    const dialog = useDialog();

    const lastReportedRef = useRef<{ duration: number; current: number }>({ duration: 0, current: 0 });

    const [video, setVideo] = useState<YouTubeVideo | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [isSubscribed, setIsSubscribed] = useState<boolean | null>(null);
    const [isTogglingSub, setIsTogglingSub] = useState(false);

    useEffect(() => {
        if (!videoId) return;
        let active = true;

        youtubeService.getVideo(videoId, serverId)
            .then((data) => { if (active) setVideo(data); })
            .catch(() => { if (active) setError('Could not load video metadata.'); });

        return () => { active = false; };
    }, [videoId, serverId]);

    useEffect(() => {
        if (!video?.channelId) {
            setIsSubscribed(null);
            return;
        }
        let active = true;
        youtubeService.getSubscriptions(serverId)
            .then((subs) => {
                if (!active) return;
                setIsSubscribed(subs.some(s => s.channelId === video.channelId));
            })
            .catch(() => { if (active) setIsSubscribed(false); });
        return () => { active = false; };
    }, [video?.channelId, serverId]);

    useEffect(() => {
        return () => {
            const { duration, current } = lastReportedRef.current;
            if (!videoId || duration <= 0 || !video) return;

            void youtubeService.recordWatch({
                videoId,
                videoTitle: video.title,
                thumbnailUrl: video.thumbnailUrl,
                channelId: video.channelId,
                channelName: video.channelName,
                durationWatched: Math.floor(current),
                totalDuration: Math.floor(duration),
            }, serverId).catch(() => undefined);
        };
    }, [videoId, video, serverId]);

    const handleToggleSubscribe = useCallback(async () => {
        if (!video?.channelId || isSubscribed === null) return;
        setIsTogglingSub(true);
        try {
            if (isSubscribed) {
                await youtubeService.unsubscribe(video.channelId, serverId);
                setIsSubscribed(false);
            } else {
                await youtubeService.subscribe(video.channelId, serverId);
                setIsSubscribed(true);
            }
        } catch (err) {
            const message = err instanceof Error ? err.message : 'Could not update subscription.';
            await dialog.alert({ title: 'YouTube', message });
        } finally {
            setIsTogglingSub(false);
        }
    }, [video?.channelId, isSubscribed, serverId, dialog]);

    if (!videoId) {
        return <EmptyState title="No video selected" description="Pick a video to watch." />;
    }

    const back = () => navigate(serverId ? `/server/${serverId}/youtube` : '/youtube');

    if (error) {
        return (
            <div data-vora-page="" className="min-h-full flex items-center justify-center">
                <EmptyState
                    title="Playback unavailable"
                    description={error}
                    action={<button type="button" className="vora-button-secondary cursor-pointer" onClick={back}>Back to YouTube</button>}
                />
            </div>
        );
    }

    const channelHref = video?.channelId
        ? (serverId ? `/server/${serverId}/youtube/channel/${video.channelId}` : `/youtube/channel/${video.channelId}`)
        : null;

    return (
        <div data-vora-page="" className="min-h-full pb-10">
            <div className="flex items-center justify-between gap-4 px-8 pt-6">
                <button
                    type="button"
                    onClick={back}
                    className="flex cursor-pointer items-center gap-2 text-sm"
                    style={{ color: 'var(--vora-text-muted)' }}
                >
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                        <polyline points="15 18 9 12 15 6" />
                    </svg>
                    Back to YouTube
                </button>
            </div>

            <div className="mx-auto mt-4 w-full max-w-4xl px-8">
                <PlayerFrame video={video}>
                    <YouTubePlayerEmbed
                        videoId={videoId}
                        onProgress={(currentTime, duration) => {
                            lastReportedRef.current = { current: currentTime, duration };
                        }}
                    />
                </PlayerFrame>

                {video && (
                    <div className="mt-6">
                        <h1 className="text-xl font-semibold" style={{ color: 'var(--vora-text-primary)' }}>
                            {video.title}
                        </h1>
                        <p className="mt-1 text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                            {video.viewCount != null ? formatViewCount(video.viewCount) : null}
                        </p>

                        {(channelHref || video.channelName) && (
                            <div className="mt-4 flex items-center justify-between gap-4 border-t border-b py-4" style={{ borderColor: 'var(--vora-border-subtle)' }}>
                                <div className="min-w-0 flex-1">
                                    {channelHref ? (
                                        <Link
                                            to={channelHref}
                                            className="cursor-pointer text-sm font-semibold hover:underline"
                                            style={{ color: 'var(--vora-text-primary)' }}
                                        >
                                            {video.channelName}
                                        </Link>
                                    ) : (
                                        <span className="text-sm font-semibold" style={{ color: 'var(--vora-text-primary)' }}>
                                            {video.channelName}
                                        </span>
                                    )}
                                </div>
                                {video.channelId && isSubscribed !== null && (
                                    <button
                                        type="button"
                                        onClick={handleToggleSubscribe}
                                        disabled={isTogglingSub}
                                        className={isSubscribed ? 'vora-button-secondary cursor-pointer' : 'vora-button-primary cursor-pointer'}
                                    >
                                        {isSubscribed ? 'Subscribed' : 'Subscribe'}
                                    </button>
                                )}
                            </div>
                        )}

                        {video.description && (
                            <p className="mt-4 whitespace-pre-line text-sm" style={{ color: 'var(--vora-text-secondary)' }}>
                                {video.description}
                            </p>
                        )}
                    </div>
                )}
            </div>
        </div>
    );
}

function formatViewCount(views: number): string {
    if (views >= 1_000_000_000) return `${(views / 1_000_000_000).toFixed(1).replace(/\.0$/, '')}B views`;
    if (views >= 1_000_000) return `${(views / 1_000_000).toFixed(1).replace(/\.0$/, '')}M views`;
    if (views >= 1_000) return `${(views / 1_000).toFixed(1).replace(/\.0$/, '')}K views`;
    return `${views} views`;
}

function PlayerFrame({ video, children }: { video: YouTubeVideo | null; children: React.ReactNode }) {
    const width = video?.embedWidth && video.embedWidth > 0 ? video.embedWidth : 960;
    const height = video?.embedHeight && video.embedHeight > 0 ? video.embedHeight : 540;

    return (
        <div
            className="relative mx-auto overflow-hidden"
            style={{
                width: '100%',
                maxWidth: width,
                aspectRatio: `${width} / ${height}`,
                background: '#000',
                borderRadius: 'var(--vora-radius-md)',
            }}
        >
            {children}
        </div>
    );
}
