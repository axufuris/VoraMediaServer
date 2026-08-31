import { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { actorService, type ActorProfile as ActorProfileData } from '../../../api/Media/actorService';
import { discoveryService, type DiscoveryActor } from '../../../api/Discovery/discoveryService';
import { useFeatureFlags } from '../../../hooks/useFeatureFlags';
import ActorProfile, { type ActorCredit } from '../../../components/Client/Primitives/ActorProfile';
import EmptyState from '../../../components/Client/Primitives/EmptyState';

const DISCOVERY_PROVIDER = 'tmdb_discovery';

export default function ActorDetailsPage() {
    const { serverId, id } = useParams<{ serverId?: string, id: string }>();
    const navigate = useNavigate();
    const flags = useFeatureFlags();
    const [actor, setActor] = useState<ActorProfileData | null>(null);
    const [credits, setCredits] = useState<DiscoveryActor | null>(null);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        let isMounted = true;
        if (!id) return;

        actorService.getActorProfile(id, serverId)
            .then(res => { if (isMounted) setActor(res); })
            .catch(console.error)
            .finally(() => { if (isMounted) setLoading(false); });

        return () => { isMounted = false; };
    }, [id, serverId]);

    // The scan only ever stores a person's name and photo, so the biography,
    // life dates and wider credit list all come from the provider.
    useEffect(() => {
        let isMounted = true;
        setCredits(null);
        if (!flags.discover || !actor?.tmdbId) return;

        discoveryService.getActorDetails(DISCOVERY_PROVIDER, actor.tmdbId.toString(), serverId)
            .then(res => { if (isMounted) setCredits(res); })
            .catch(() => { /* discovery may be unavailable — the local half still renders */ });

        return () => { isMounted = false; };
    }, [flags.discover, actor?.tmdbId, serverId]);

    const mediaPath = (mediaId: string) => serverId ? `/server/${serverId}/media/${mediaId}` : `/media/${mediaId}`;

    const roleNames = Array.from(new Set((actor?.filmography ?? []).flatMap(f => (f.role || 'Actor').split(',').map(r => r.trim()))));
    const displayRole = roleNames.length > 0 ? roleNames.join(', ') : 'Actor';

    // On Server is everything of theirs this server holds, from both directions:
    // the cast links the scan created, plus any provider credit the library
    // turns out to have. The second half matters because a recurring player is
    // often missing from the show-level cast the scan stored, which used to
    // strand those titles under Known For with an "In library" badge.
    const byMediaId = new Map<string, ActorCredit>();

    for (const local of actor?.filmography ?? []) {
        const year = local.releaseDate ? new Date(local.releaseDate).getFullYear() : null;
        byMediaId.set(local.id, {
            key: local.id,
            title: local.title,
            posterUrl: local.posterUrl,
            caption: [local.characterName || local.role, year].filter(Boolean).join(' · ') || null,
            onOpen: () => navigate(mediaPath(local.id)),
        });
    }

    for (const credit of credits?.filmography ?? []) {
        if (!credit.inLibrary || !credit.mediaItemId || byMediaId.has(credit.mediaItemId)) continue;
        byMediaId.set(credit.mediaItemId, {
            key: credit.mediaItemId,
            title: credit.title,
            posterUrl: credit.posterUrl,
            caption: credit.year ? String(credit.year) : null,
            onOpen: () => navigate(mediaPath(credit.mediaItemId!)),
        });
    }

    const onServer = [...byMediaId.values()];

    const knownFor: ActorCredit[] = (credits?.filmography ?? [])
        .filter(credit => !credit.inLibrary)
        .map(credit => ({
            key: `${credit.type}-${credit.externalId}`,
            title: credit.title,
            posterUrl: credit.posterUrl,
            caption: credit.year ? String(credit.year) : null,
            onOpen: () => navigate(serverId
                ? `/server/${serverId}/discovery/${DISCOVERY_PROVIDER}/${credit.type}/${credit.externalId}`
                : `/discovery/${DISCOVERY_PROVIDER}/${credit.type}/${credit.externalId}`),
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
            <EmptyState
                title="Actor not found"
                description="This profile doesn't exist or you don't have access to it."
            />
        );
    }

    return (
        <ActorProfile
            name={actor.name}
            role={displayRole}
            profileImageUrl={actor.profileImageUrl ?? credits?.profileImageUrl}
            biography={actor.biography || credits?.biography}
            birthday={actor.birthday || credits?.birthday}
            deathday={actor.deathday || credits?.deathday}
            placeOfBirth={actor.placeOfBirth || credits?.placeOfBirth}
            onServer={onServer}
            knownFor={knownFor}
            onBack={() => navigate(-1)}
        />
    );
}
