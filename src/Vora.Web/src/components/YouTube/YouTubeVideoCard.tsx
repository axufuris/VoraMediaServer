import { useNavigate } from 'react-router-dom';

interface YouTubeVideoCardProps {
    videoId: string;
    title: string;
    thumbnailUrl: string;
    channelId: string;
    channelName: string;
    durationSeconds?: number;
    viewCount?: number;
    publishedAt?: string;
    progressPercent?: number;
    watched?: boolean;
    serverId?: string;
    width?: number;
}

export default function YouTubeVideoCard({
    videoId,
    title,
    thumbnailUrl,
    channelId,
    channelName,
    durationSeconds,
    viewCount,
    publishedAt,
    progressPercent,
    watched,
    serverId,
    width = 280,
}: YouTubeVideoCardProps) {
    const navigate = useNavigate();

    const playerHref = serverId ? `/server/${serverId}/youtube/watch/${videoId}` : `/youtube/watch/${videoId}`;
    const channelHref = channelId
        ? (serverId ? `/server/${serverId}/youtube/channel/${channelId}` : `/youtube/channel/${channelId}`)
        : null;

    const goToPlayer = () => navigate(playerHref);
    const goToChannel = (e: React.MouseEvent | React.KeyboardEvent) => {
        e.stopPropagation();
        if (channelHref) navigate(channelHref);
    };

    const onThumbnailKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
        if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            goToPlayer();
        }
    };

    return (
        <div style={{ width, scrollSnapAlign: 'start' }} className="group block text-left">
            <div
                role="button"
                tabIndex={0}
                onClick={goToPlayer}
                onKeyDown={onThumbnailKeyDown}
                className="relative overflow-hidden cursor-pointer"
                style={{
                    aspectRatio: '16 / 9',
                    borderRadius: 'var(--vora-radius-md)',
                    boxShadow: 'var(--vora-shadow-md)',
                    background: 'var(--vora-bg-surface)',
                    border: '1px solid var(--vora-border-subtle)',
                    transition: 'transform var(--vora-duration-med, 240ms) var(--vora-ease-out)',
                }}
            >
                {thumbnailUrl ? (
                    <img
                        src={thumbnailUrl}
                        alt={title}
                        loading="lazy"
                        className="h-full w-full object-cover transition-opacity"
                        style={{ opacity: watched ? 0.5 : 1 }}
                    />
                ) : (
                    <div className="flex h-full w-full items-center justify-center" style={{ color: 'var(--vora-text-muted)', opacity: watched ? 0.5 : 1 }}>
                        <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                            <rect x="3" y="3" width="18" height="18" rx="2" />
                            <polygon points="10 8 16 12 10 16 10 8" />
                        </svg>
                    </div>
                )}
                {watched && (
                    <span
                        className="absolute right-2 top-2 inline-flex h-6 w-6 items-center justify-center rounded-full"
                        style={{ background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)', boxShadow: '0 2px 8px rgba(0, 0, 0, 0.4)' }}
                        title="Watched"
                    >
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3"><polyline points="20 6 9 17 4 12" /></svg>
                    </span>
                )}
                {durationSeconds != null && durationSeconds > 0 && (
                    <span
                        className="absolute left-2 bottom-2 rounded px-1.5 py-0.5 text-[11px] font-medium"
                        style={{ background: 'rgba(0, 0, 0, 0.78)', color: 'var(--vora-text-primary)' }}
                    >
                        {formatDuration(durationSeconds)}
                    </span>
                )}
                {progressPercent != null && progressPercent > 0 && (
                    <div className="absolute bottom-0 left-0 right-0 h-1 overflow-hidden" style={{ background: 'rgba(255, 255, 255, 0.2)' }}>
                        <div className="h-full" style={{ width: `${Math.min(100, Math.max(0, progressPercent))}%`, background: 'var(--vora-accent-500)' }} />
                    </div>
                )}
            </div>
            <div className="mt-2.5">
                <div
                    role="button"
                    tabIndex={0}
                    onClick={goToPlayer}
                    onKeyDown={onThumbnailKeyDown}
                    className="truncate text-sm font-medium cursor-pointer"
                    style={{ color: 'var(--vora-text-primary)' }}
                    title={title}
                >
                    {title}
                </div>
                <div className="mt-0.5 truncate text-xs" style={{ color: 'var(--vora-text-muted)' }}>
                    {channelHref ? (
                        <span
                            role="button"
                            tabIndex={0}
                            onClick={goToChannel}
                            onKeyDown={(e) => {
                                if (e.key === 'Enter' || e.key === ' ') {
                                    e.preventDefault();
                                    goToChannel(e);
                                }
                            }}
                            className="cursor-pointer hover:underline"
                            style={{ color: 'var(--vora-text-secondary)' }}
                            title={channelName}
                        >
                            {channelName}
                        </span>
                    ) : (
                        <span>{channelName}</span>
                    )}
                    {viewCount != null && <span> • {formatViewCount(viewCount)}</span>}
                    {publishedAt && (
                        <span title={formatAbsoluteDate(publishedAt)}> • {formatRelativeDate(publishedAt)}</span>
                    )}
                </div>
            </div>
        </div>
    );
}

function formatDuration(totalSeconds: number): string {
    const hours = Math.floor(totalSeconds / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);
    const seconds = totalSeconds % 60;

    if (hours > 0) {
        return `${hours}:${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`;
    }
    return `${minutes}:${String(seconds).padStart(2, '0')}`;
}

function formatViewCount(views: number): string {
    if (views >= 1_000_000_000) return `${(views / 1_000_000_000).toFixed(1).replace(/\.0$/, '')}B views`;
    if (views >= 1_000_000) return `${(views / 1_000_000).toFixed(1).replace(/\.0$/, '')}M views`;
    if (views >= 1_000) return `${(views / 1_000).toFixed(1).replace(/\.0$/, '')}K views`;
    return `${views} views`;
}

function formatRelativeDate(iso: string): string {
    const then = new Date(iso).getTime();
    if (Number.isNaN(then)) return '';
    const diffMs = Date.now() - then;
    const diffMinutes = Math.floor(diffMs / 60_000);
    if (diffMinutes < 1) return 'just now';
    if (diffMinutes < 60) return `${diffMinutes} min${diffMinutes === 1 ? '' : 's'} ago`;
    const diffHours = Math.floor(diffMinutes / 60);
    if (diffHours < 24) return `${diffHours} hour${diffHours === 1 ? '' : 's'} ago`;
    const diffDays = Math.floor(diffHours / 24);
    if (diffDays < 7) return `${diffDays} day${diffDays === 1 ? '' : 's'} ago`;
    if (diffDays < 30) {
        const weeks = Math.floor(diffDays / 7);
        return `${weeks} week${weeks === 1 ? '' : 's'} ago`;
    }
    const diffMonths = Math.floor(diffDays / 30);
    if (diffMonths < 12) return `${diffMonths} month${diffMonths === 1 ? '' : 's'} ago`;
    const diffYears = Math.floor(diffDays / 365);
    return `${diffYears} year${diffYears === 1 ? '' : 's'} ago`;
}

function formatAbsoluteDate(iso: string): string {
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return iso;
    return d.toLocaleString(undefined, {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        hour: 'numeric',
        minute: '2-digit',
    });
}
