import { useCallback, useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import PageHeader from '../../../components/Admin/Primitives/PageHeader';
import { youtubeService, type YouTubeStatus } from '../../../api/YouTube/youtubeService';

export default function YouTubeAdminPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const pluginsHref = serverId ? `/admin/server/${serverId}/plugins` : '/admin/plugins';

    const [status, setStatus] = useState<YouTubeStatus | null>(null);
    const [isLoading, setIsLoading] = useState(true);

    const refreshStatus = useCallback(() => {
        youtubeService.getAdminStatus(serverId)
            .then(setStatus)
            .catch(() => setStatus(null))
            .finally(() => setIsLoading(false));
    }, [serverId]);

    useEffect(() => {
        refreshStatus();
    }, [refreshStatus]);

    return (
        <div data-vora-page="">
            <PageHeader
                title="YouTube"
                description="Browse, search, and play YouTube content from inside Vora using the official YouTube Data API and iframe player."
            />

            <div className="px-8 pt-6 pb-10 max-w-6xl mx-auto space-y-6">
                <StatusCard status={status} isLoading={isLoading} />

                <p className="text-sm text-[var(--vora-text-muted)]">
                    Configure the YouTube plugin — API key, trending region, and the master enable toggle — on the <Link to={pluginsHref} className="text-[var(--vora-accent-text)] hover:underline">Plugins</Link> page. The master toggle hides the YouTube nav item for every profile when off; even with it on, admins can disable YouTube per user from Users &amp; Access, and users can hide it on their own profile from Settings.
                </p>

                <ParentalControlsInfo />
            </div>
        </div>
    );
}

function StatusCard({ status, isLoading }: { status: YouTubeStatus | null; isLoading: boolean }) {
    if (isLoading) {
        return <div className="vora-skeleton h-24 rounded-md" />;
    }

    if (!status) {
        return (
            <div className="vora-card p-4 text-sm border border-[var(--vora-warning-500)]/30" style={{ background: 'var(--vora-warning-soft, transparent)', color: 'var(--vora-text-secondary)' }}>
                Could not reach the YouTube admin status endpoint. Check that the plugin is installed.
            </div>
        );
    }

    const dots: { label: string; ok: boolean }[] = [
        { label: 'Plugin installed', ok: status.pluginInstalled },
        { label: 'API key configured', ok: status.apiKeyConfigured },
        { label: 'Master toggle enabled', ok: status.serverEnabled },
    ];

    return (
        <div className="vora-card p-4">
            <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                <div className="flex flex-wrap items-center gap-4">
                    {dots.map((d) => (
                        <div key={d.label} className="flex items-center gap-2 text-sm" style={{ color: 'var(--vora-text-secondary)' }}>
                            <span
                                aria-hidden
                                className="inline-block h-2.5 w-2.5 rounded-full"
                                style={{ background: d.ok ? 'var(--vora-success-500)' : 'var(--vora-text-disabled)' }}
                            />
                            {d.label}
                        </div>
                    ))}
                </div>
                <div className="text-xs" style={{ color: 'var(--vora-text-muted)' }}>
                    Trending region: <span className="font-semibold" style={{ color: 'var(--vora-text-secondary)' }}>{status.trendingRegion}</span>
                </div>
            </div>
        </div>
    );
}

function ParentalControlsInfo() {
    return (
        <div className="vora-card p-4 text-sm space-y-2" style={{ color: 'var(--vora-text-secondary)' }}>
            <div className="font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Parental controls &amp; quota</div>
            <p className="m-0">
                Vora reads each profile&apos;s existing parental settings: if any rating limit is set or unrated content is blocked, every YouTube API call from that profile&apos;s session uses <code>safeSearch=strict</code>, age-restricted videos are filtered out, and the rating ceiling (where available) is enforced. No new per-profile YouTube settings are needed.
            </p>
            <p className="m-0">
                The free YouTube Data API tier provides 10,000 units/day. Vora caches trending (30 min), search (5 min), channel metadata (1 hour), subscription uploads (15 min via RSS), and recommendations (1 hour) to keep usage low.
            </p>
        </div>
    );
}
