import { useMemo } from 'react';
import { useParams } from 'react-router-dom';
import FeatureToggle from '../../../components/Admin/Features/FeatureToggle';
import FeaturePluginList from '../../../components/Admin/Features/FeaturePluginList';
import PageHeader from '../../../components/Admin/Primitives/PageHeader';

export default function ReleaseCalendarPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const pluginTypes = useMemo(() => ['Calendar'], []);

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

                <section>
                    <p className="text-sm text-[var(--vora-text-muted)] mb-4">
                        Calendar plugins supply upcoming releases. Configure API keys and per-source preferences below.
                    </p>
                    <FeaturePluginList
                        serverId={serverId}
                        pluginTypes={pluginTypes}
                        emptyLabel="No Calendar plugins are installed."
                    />
                </section>
            </div>
        </div>
    );
}
