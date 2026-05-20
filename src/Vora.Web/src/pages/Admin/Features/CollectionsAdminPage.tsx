import { useMemo } from 'react';
import { useParams } from 'react-router-dom';
import FeaturePluginList from '../../../components/Admin/Features/FeaturePluginList';
import PageHeader from '../../../components/Admin/Primitives/PageHeader';

export default function CollectionsAdminPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const pluginTypes = useMemo(() => ['Collection_Sync'], []);

    return (
        <div data-vora-page="">
            <PageHeader
                title="Collections"
                description="Sync collections from external sources (Trakt lists, custom feeds, etc.) into Vora."
            />

            <div className="px-8 pb-10 max-w-6xl mx-auto pt-6">
                <p className="text-sm text-[var(--vora-text-muted)] mb-4">Collection sync providers that pull list contents from external services.</p>
                <FeaturePluginList
                    serverId={serverId}
                    pluginTypes={pluginTypes}
                    emptyLabel="No Collection Sync plugins are installed."
                />
            </div>
        </div>
    );
}
