import { useMemo } from 'react';
import { useParams } from 'react-router-dom';
import FeaturePluginList from '../../../components/Admin/Features/FeaturePluginList';
import PageHeader from '../../../components/Admin/Primitives/PageHeader';

export default function MusicAdminPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const pluginTypes = useMemo(() => ['Lyrics', 'ListeningData'], []);

    return (
        <div data-vora-page="">
            <PageHeader
                title="Music"
                description="Configure providers for lyrics fetching and listening data (Last.fm scrobbling). For listening history and admin stats, see Music History under the Server section."
            />

            <div className="px-8 pb-10 max-w-6xl mx-auto pt-6">
                <p className="text-sm text-[var(--vora-text-muted)] mb-4">Lyrics providers and listening-data providers (e.g. Last.fm).</p>
                <FeaturePluginList
                    serverId={serverId}
                    pluginTypes={pluginTypes}
                    emptyLabel="No Lyrics or ListeningData plugins are installed."
                />
            </div>
        </div>
    );
}
