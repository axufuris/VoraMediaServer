import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { recommendationService, type RecommendationListVM } from '../../api/Discovery/recommendationService';
import RecommendationRow from '../../components/Media/RecommendationRow';
import PageHeader from '../../components/Client/Primitives/PageHeader';
import EmptyState from '../../components/Client/Primitives/EmptyState';

function AsyncProviderBlock({ providerId, serverId }: { providerId: string, serverId?: string }) {
    const [lists, setLists] = useState<RecommendationListVM[]>([]);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        recommendationService.getGlobalRecommendations(providerId, serverId)
            .then(setLists)
            .catch(console.error)
            .finally(() => setLoading(false));
    }, [providerId, serverId]);

    if (loading) {
        return (
            <div className="mb-8 px-8">
                <div className="vora-skeleton mb-4 h-6 w-48" />
                <div className="flex gap-4 overflow-hidden">
                    {Array.from({ length: 6 }, (_, i) => <div key={i} className="vora-skeleton h-72 w-48 flex-none" />)}
                </div>
            </div>
        );
    }

    if (lists.length === 0) return null;

    return (
        <>
            {[...lists].sort((a, b) => a.weight - b.weight).map((list, index) => (
                <RecommendationRow key={`${providerId}-${index}`} list={list} serverId={serverId} />
            ))}
        </>
    );
}

interface RecommendationsPageProps {
    embedded?: boolean;
}

export default function RecommendationsPage({ embedded = false }: RecommendationsPageProps = {}) {
    const { serverId } = useParams<{ serverId?: string }>();
    const [providers, setProviders] = useState<string[]>([]);
    const [loadingProviders, setLoadingProviders] = useState(true);

    useEffect(() => {
        recommendationService.getProviders(serverId)
            .then(setProviders)
            .catch(console.error)
            .finally(() => setLoadingProviders(false));
    }, [serverId]);

    return (
        <div className="min-h-full pb-20">
            {!embedded && (
                <PageHeader
                    title="For you"
                    subtitle="Personalized recommendations based on what you've watched."
                />
            )}

            <div className="pt-2">
                {loadingProviders ? (
                    <div className="px-8">
                        <div className="vora-skeleton mb-4 h-6 w-48" />
                        <div className="vora-skeleton h-72 w-full" />
                    </div>
                ) : providers.length === 0 ? (
                    <EmptyState
                        title="No recommendation engines enabled"
                        description="Ask your server admin to enable a recommendation provider in the admin Plugins page."
                        icon={(
                            <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                                <path d="M11.05 3.72a1 1 0 011.9 0l2 5.27 5.62.4a1 1 0 01.57 1.76l-4.31 3.65 1.34 5.48a1 1 0 01-1.5 1.1L12 18.62l-4.67 2.76a1 1 0 01-1.5-1.1l1.34-5.48-4.31-3.65a1 1 0 01.57-1.76l5.62-.4 2-5.27z" />
                            </svg>
                        )}
                    />
                ) : (
                    [...providers].sort((a, b) => {
                        if (a === 'openai_recommendations') return -1;
                        if (b === 'openai_recommendations') return 1;
                        return 0;
                    }).map(providerId => (
                        <AsyncProviderBlock key={providerId} providerId={providerId} serverId={serverId} />
                    ))
                )}
            </div>
        </div>
    );
}
