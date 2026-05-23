import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { youtubeService, type YouTubeSubscription } from '../../../api/YouTube/youtubeService';
import PageHeader from '../../../components/Client/Primitives/PageHeader';
import EmptyState from '../../../components/Client/Primitives/EmptyState';

export default function YouTubeSubscriptionsPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const navigate = useNavigate();

    const [subscriptions, setSubscriptions] = useState<YouTubeSubscription[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        let active = true;
        youtubeService.getSubscriptions(serverId)
            .then((data) => { if (active) setSubscriptions(data); })
            .catch((err: unknown) => {
                if (!active) return;
                setError(err instanceof Error ? err.message : 'Could not load your subscriptions.');
            })
            .finally(() => { if (active) setIsLoading(false); });
        return () => { active = false; };
    }, [serverId]);

    const channelHref = (channelId: string) =>
        serverId ? `/server/${serverId}/youtube/channel/${channelId}` : `/youtube/channel/${channelId}`;

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
                title="Your subscriptions"
                subtitle={subscriptions.length > 0
                    ? `${subscriptions.length} channel${subscriptions.length === 1 ? '' : 's'} you follow inside Vora.`
                    : 'Channels you subscribe to from any video card or channel page will show up here.'}
            />

            <div className="px-8 pt-2">
                {isLoading ? (
                    <div className="grid grid-cols-2 gap-5 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6">
                        {Array.from({ length: 10 }, (_, i) => <div key={i} className="vora-skeleton aspect-square rounded-full" />)}
                    </div>
                ) : error ? (
                    <EmptyState title="Subscriptions unavailable" description={error} />
                ) : subscriptions.length === 0 ? (
                    <EmptyState
                        title="No subscriptions yet"
                        description="Search for a creator, open their channel, and tap Subscribe — they'll show up here and feed into your home page."
                        icon={(
                            <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                                <circle cx="12" cy="8" r="4" />
                                <path d="M4 21a8 8 0 0 1 16 0" />
                            </svg>
                        )}
                    />
                ) : (
                    <div className="grid grid-cols-2 gap-6 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6">
                        {subscriptions.map((sub) => (
                            <Link
                                key={sub.channelId}
                                to={channelHref(sub.channelId)}
                                className="group block text-center cursor-pointer"
                            >
                                <div
                                    className="mx-auto overflow-hidden rounded-full transition-transform group-hover:scale-105"
                                    style={{
                                        width: 128,
                                        height: 128,
                                        background: 'var(--vora-bg-surface)',
                                        border: '1px solid var(--vora-border-subtle)',
                                        boxShadow: 'var(--vora-shadow-md)',
                                    }}
                                >
                                    {sub.channelThumbnailUrl ? (
                                        <img src={sub.channelThumbnailUrl} alt={sub.channelName} className="h-full w-full object-cover" />
                                    ) : (
                                        <div className="flex h-full w-full items-center justify-center" style={{ color: 'var(--vora-text-muted)' }}>
                                            <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                                                <circle cx="12" cy="8" r="4" />
                                                <path d="M4 21a8 8 0 0 1 16 0" />
                                            </svg>
                                        </div>
                                    )}
                                </div>
                                <div className="mt-3 truncate text-sm font-medium" style={{ color: 'var(--vora-text-primary)' }} title={sub.channelName}>
                                    {sub.channelName}
                                </div>
                                <div className="mt-0.5 text-[11px]" style={{ color: 'var(--vora-text-muted)' }}>
                                    Subscribed {formatRelative(sub.subscribedAt)}
                                </div>
                            </Link>
                        ))}
                    </div>
                )}
            </div>
        </div>
    );
}

function formatRelative(iso: string): string {
    const then = new Date(iso).getTime();
    if (Number.isNaN(then)) return '';
    const diffDays = Math.floor((Date.now() - then) / 86_400_000);
    if (diffDays <= 0) return 'today';
    if (diffDays === 1) return 'yesterday';
    if (diffDays < 30) return `${diffDays} days ago`;
    const months = Math.floor(diffDays / 30);
    if (months < 12) return `${months} month${months === 1 ? '' : 's'} ago`;
    const years = Math.floor(diffDays / 365);
    return `${years} year${years === 1 ? '' : 's'} ago`;
}
