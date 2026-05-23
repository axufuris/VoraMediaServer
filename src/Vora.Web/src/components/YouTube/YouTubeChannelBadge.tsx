import { useNavigate } from 'react-router-dom';

interface YouTubeChannelBadgeProps {
    channelId: string;
    channelName: string;
    channelThumbnailUrl?: string;
    serverId?: string;
    size?: 'sm' | 'md';
}

const SIZE_PX: Record<'sm' | 'md', number> = { sm: 36, md: 56 };

export default function YouTubeChannelBadge({
    channelId,
    channelName,
    channelThumbnailUrl,
    serverId,
    size = 'md',
}: YouTubeChannelBadgeProps) {
    const navigate = useNavigate();
    const px = SIZE_PX[size];

    const handleClick = () => {
        const base = serverId ? `/server/${serverId}/youtube/channel/${channelId}` : `/youtube/channel/${channelId}`;
        navigate(base);
    };

    return (
        <button
            type="button"
            onClick={handleClick}
            className="flex cursor-pointer items-center gap-3 rounded-full p-1 pr-3 transition-colors hover:bg-white/5"
            style={{ color: 'var(--vora-text-primary)' }}
        >
            <div
                className="overflow-hidden rounded-full"
                style={{
                    width: px,
                    height: px,
                    background: 'var(--vora-bg-surface)',
                    border: '1px solid var(--vora-border-subtle)',
                }}
            >
                {channelThumbnailUrl ? (
                    <img src={channelThumbnailUrl} alt={channelName} className="h-full w-full object-cover" />
                ) : (
                    <div className="flex h-full w-full items-center justify-center" style={{ color: 'var(--vora-text-muted)' }}>
                        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                            <circle cx="12" cy="8" r="4" />
                            <path d="M4 21a8 8 0 0 1 16 0" />
                        </svg>
                    </div>
                )}
            </div>
            <span className="truncate text-sm font-medium" style={{ color: 'var(--vora-text-primary)' }}>
                {channelName}
            </span>
        </button>
    );
}
