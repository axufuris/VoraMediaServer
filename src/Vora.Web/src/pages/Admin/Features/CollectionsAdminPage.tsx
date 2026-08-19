import { Link, useParams } from 'react-router-dom';
import PageHeader from '../../../components/Admin/Primitives/PageHeader';

export default function CollectionsAdminPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const pluginsHref = serverId ? `/admin/server/${serverId}/plugins` : '/admin/plugins';

    return (
        <div data-vora-page="">
            <PageHeader
                title="Collections"
                description="Sync collections from external sources (Trakt lists, custom feeds, etc.) into Vora."
            />

            <div className="px-8 pb-10 max-w-6xl mx-auto pt-6">
                <div className="vora-card p-6 text-sm text-[var(--vora-text-secondary)] leading-relaxed">
                    Collection sync providers (Trakt, MDbList, IMDb, …) are configured on the{' '}
                    <Link to={pluginsHref} className="text-[var(--vora-accent-text)] hover:underline font-medium">Plugins</Link>{' '}
                    page — open the <span className="font-semibold">Collection Sync</span> category to add API keys and enable a source.
                </div>
            </div>
        </div>
    );
}
