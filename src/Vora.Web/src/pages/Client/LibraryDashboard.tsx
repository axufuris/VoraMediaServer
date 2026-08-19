import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { libraryService, type LibrarySummary } from '../../api/Media/libraryService';
import PageHeader from '../../components/Admin/Primitives/PageHeader';
import EntityCard from '../../components/Admin/Primitives/EntityCard';
import HealthBadge from '../../components/Admin/Primitives/HealthBadge';
import EmptyState from '../../components/Admin/Primitives/EmptyState';

const LIBRARY_TYPE_LABEL: Record<string, string> = {
    Movie: 'Movies',
    TvShow: 'TV Shows',
    Music: 'Music',
    HomeVideo: 'Home Videos',
    AudioBook: 'Audiobooks',
    Photo: 'Photos',
};

export default function LibraryDashboard() {
    const { serverId } = useParams<{ serverId?: string }>();
    const navigate = useNavigate();
    const [libraries, setLibraries] = useState<LibrarySummary[]>([]);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        libraryService.getLibraries(serverId)
            .then(setLibraries)
            .catch(console.error)
            .finally(() => setLoading(false));
    }, [serverId]);

    return (
        <div data-vora-page="">
            <PageHeader
                title="Library Management"
                description="Configure media sources, scanners, and real-time watchers."
                actions={
                    <button
                        type="button"
                        onClick={() => navigate(serverId ? `/admin/server/${serverId}/libraries/new` : '/admin/libraries/new')}
                        className="vora-button-primary flex items-center gap-2"
                    >
                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 6v6m0 0v6m0-6h6m-6 0H6" /></svg>
                        Add library
                    </button>
                }
            />

            <div className="px-8 pt-6 pb-10 max-w-7xl mx-auto">
                {loading ? (
                    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
                        {[1, 2, 3].map(i => <div key={i} className="vora-skeleton h-32" />)}
                    </div>
                ) : (
                    libraries.length === 0 ? (
                        <div className="vora-card">
                            <EmptyState
                                title="No libraries yet"
                                description="Add a library to start indexing your media."
                                actionLabel="Add library"
                                onAction={() => navigate(serverId ? `/admin/server/${serverId}/libraries/new` : '/admin/libraries/new')}
                            />
                        </div>
                    ) : (
                        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
                            {[...libraries].sort((a, b) => a.name.localeCompare(b.name)).map(lib => (
                                <EntityCard
                                    key={lib.id}
                                    title={lib.name}
                                    subtitle={LIBRARY_TYPE_LABEL[lib.type] ?? lib.type}
                                    badge={<HealthBadge tone={lib.isBeingWatched ? 'ok' : 'neutral'}>{lib.isBeingWatched ? 'Watching' : 'Idle'}</HealthBadge>}
                                    onClick={() => navigate(serverId ? `/admin/server/${serverId}/libraries/${lib.id}/manage` : `/admin/libraries/${lib.id}/manage`)}
                                    footer={
                                        <div className="text-xs font-semibold text-[var(--vora-accent-text)] text-right">
                                            Manage settings →
                                        </div>
                                    }
                                />
                            ))}
                        </div>
                    )
                )}
            </div>
        </div>
    );
}
