import { Link, useParams } from 'react-router-dom';
import FeatureToggle from '../../../components/Admin/Features/FeatureToggle';
import PageHeader from '../../../components/Admin/Primitives/PageHeader';

export default function ReleaseCalendarPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const settingsHref = serverId ? `/admin/server/${serverId}/settings` : '/admin/settings';
    const pluginsHref = serverId ? `/admin/server/${serverId}/plugins` : '/admin/plugins';

    return (
        <div data-vora-page="">
            <PageHeader
                title="Release Calendar"
                description="Upcoming releases sourced from calendar providers."
            />

            <div className="px-8 pt-6 pb-10 max-w-6xl mx-auto">
                <FeatureToggle
                    featureKey="releaseCalendar"
                    label="Enable Release Calendar"
                    description="When off, the Release Calendar nav entry is hidden from clients and the /api/calendar endpoints return 403."
                    serverId={serverId}
                />

                <div className="vora-card p-4 mb-4 text-sm text-[var(--vora-text-secondary)] bg-[var(--vora-info-soft)]/30 border border-[var(--vora-info-500)]/30">
                    <div className="font-semibold text-[var(--vora-text-primary)] mb-1">Radarr &amp; Sonarr credentials moved</div>
                    The Radarr and Sonarr calendar providers now read directly from the request servers you configure in
                    {' '}
                    <Link to={settingsHref} className="text-[var(--vora-accent-text)] hover:underline">System Settings → Request Servers</Link>.
                    {' '}
                    Tick <span className="font-semibold">Use for Release Calendar</span> on any Radarr or Sonarr instance you want this page to pull from. You can use the same instance for requests and calendar, or set up a calendar-only server with requests disabled.
                </div>

                <p className="text-sm text-[var(--vora-text-muted)]">
                    Enable or disable individual calendar sources on the <Link to={pluginsHref} className="text-[var(--vora-accent-text)] hover:underline">Plugins</Link> page.
                </p>
            </div>
        </div>
    );
}
