import { Link, useLocation, useParams } from 'react-router-dom';

const LABELS: Record<string, string> = {
    admin: 'Admin',
    settings: 'System Settings',
    tasks: 'Background Tasks',
    plugins: 'Plugins',
    users: 'Users & Access',
    devices: 'Authorized Devices',
    history: 'Watch History',
    'music-history': 'Music History',
    'ai-stats': 'AI Usage & Stats',
    libraries: 'Libraries',
    collections: 'Collections',
    music: 'Music',
    overlays: 'Poster Overlays',
    dedupe: 'Media Deduplication',
    'smart-lists': 'Smart Lists',
    requests: 'Request Queue',
    discovery: 'Discover',
    'for-you': 'For You',
    'release-calendar': 'Release Calendar',
    'live-tv': 'Live TV',
    'dvr-settings': 'DVR',
    'internet-radio': 'Internet Radio',
    podcasts: 'Podcasts',
    appearance: 'Appearance',
    new: 'New',
    manage: 'Manage',
    server: 'Server',
};

function pretty(segment: string): string {
    if (LABELS[segment]) return LABELS[segment];
    return segment.replace(/[-_]/g, ' ').replace(/\b\w/g, c => c.toUpperCase());
}

export default function Breadcrumb() {
    const location = useLocation();
    const { serverId } = useParams<{ serverId?: string }>();
    const segments = location.pathname.split('/').filter(Boolean);

    const crumbs: { label: string, to: string }[] = [];
    let acc = '';
    for (const seg of segments) {
        acc += `/${seg}`;
        if (seg === serverId) continue;
        if (seg === 'server') continue;
        crumbs.push({ label: pretty(seg), to: acc });
    }

    if (crumbs.length === 0) return null;

    return (
        <nav aria-label="Breadcrumb" className="flex items-center gap-1.5 text-sm text-[var(--vora-text-muted)] min-w-0">
            {crumbs.map((c, i) => {
                const isLast = i === crumbs.length - 1;
                return (
                    <span key={c.to} className="flex items-center gap-1.5 min-w-0">
                        {i > 0 && (
                            <svg className="w-3 h-3 shrink-0 text-[var(--vora-text-disabled)]" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" /></svg>
                        )}
                        {isLast ? (
                            <span className="font-semibold text-[var(--vora-text-secondary)] truncate">{c.label}</span>
                        ) : (
                            <Link to={c.to} className="hover:text-[var(--vora-text-secondary)] transition-colors truncate">{c.label}</Link>
                        )}
                    </span>
                );
            })}
        </nav>
    );
}
