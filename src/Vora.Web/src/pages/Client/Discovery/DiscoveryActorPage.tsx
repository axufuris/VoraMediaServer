import { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { discoveryService, type DiscoveryActor } from '../../../api/Discovery/discoveryService';
import ActorProfile, { type ActorCredit } from '../../../components/Client/Primitives/ActorProfile';
import EmptyState from '../../../components/Client/Primitives/EmptyState';

export default function DiscoveryActorPage() {
    const { serverId, providerId, externalId } = useParams<{ serverId?: string, providerId: string, externalId: string }>();
    const navigate = useNavigate();
    const [actor, setActor] = useState<DiscoveryActor | null>(null);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        if (!providerId || !externalId) return;
        discoveryService.getActorDetails(providerId, externalId, serverId)
            .then(setActor)
            .catch(console.error)
            .finally(() => setLoading(false));
    }, [providerId, externalId, serverId]);

    const prefix = serverId ? `/server/${serverId}` : '';

    const byYearDesc = [...(actor?.filmography ?? [])].sort((a, b) => (b.year || 0) - (a.year || 0));

    // Same split as the library actor page: what this server holds, then
    // everything else. An owned title opens the local copy, not the provider
    // page, since that is the one you can actually watch.
    const onServer: ActorCredit[] = byYearDesc
        .filter(credit => credit.inLibrary && credit.mediaItemId)
        .map(credit => ({
            key: credit.mediaItemId!,
            title: credit.title,
            posterUrl: credit.posterUrl,
            caption: credit.year ? String(credit.year) : null,
            onOpen: () => navigate(`${prefix}/media/${credit.mediaItemId}`),
        }));

    const knownFor: ActorCredit[] = byYearDesc
        .filter(credit => !credit.inLibrary || !credit.mediaItemId)
        .map(credit => ({
            key: `${credit.type}-${credit.externalId}`,
            title: credit.title,
            posterUrl: credit.posterUrl,
            caption: credit.year ? String(credit.year) : null,
            onOpen: () => navigate(`${prefix}/discovery/${providerId}/${credit.type}/${credit.externalId}`),
        }));

    if (loading) {
        return (
            <div>
                <div className="vora-skeleton h-[70vh] min-h-[540px]" />
                <div className="px-12 pt-8">
                    <div className="vora-skeleton mb-3 h-10 w-2/3" />
                    <div className="vora-skeleton mb-6 h-6 w-1/2" />
                </div>
            </div>
        );
    }

    if (!actor) {
        return (
            <EmptyState title="Actor not found" description="This profile doesn't exist or you don't have access to it." />
        );
    }

    return (
        <ActorProfile
            name={actor.name}
            role="Actor"
            profileImageUrl={actor.profileImageUrl}
            biography={actor.biography}
            birthday={actor.birthday}
            deathday={actor.deathday}
            placeOfBirth={actor.placeOfBirth}
            onServer={onServer}
            knownFor={knownFor}
            onBack={() => navigate(-1)}
        />
    );
}
