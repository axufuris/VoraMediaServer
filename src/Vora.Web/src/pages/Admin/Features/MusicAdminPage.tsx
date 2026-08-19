import { Link, useParams } from 'react-router-dom';
import PageHeader from '../../../components/Admin/Primitives/PageHeader';

export default function MusicAdminPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const pluginsHref = serverId ? `/admin/server/${serverId}/plugins` : '/admin/plugins';

    return (
        <div data-vora-page="">
            <PageHeader
                title="Music"
                description="Configure providers for lyrics fetching and listening data (Last.fm scrobbling). For listening history and admin stats, see Music History under the Server section."
            />

            <div className="px-8 pb-10 max-w-6xl mx-auto pt-6">
                <div className="vora-card p-6 text-sm text-[var(--vora-text-secondary)] leading-relaxed">
                    Lyrics providers and listening-data providers (e.g. Last.fm) are configured on the{' '}
                    <Link to={pluginsHref} className="text-[var(--vora-accent-text)] hover:underline font-medium">Plugins</Link>{' '}
                    page — open the <span className="font-semibold">Lyrics</span> and <span className="font-semibold">Listening Data</span> categories to add API keys and enable a source.
                </div>
            </div>
        </div>
    );
}
