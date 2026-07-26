import { useEffect, useState, useMemo } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { actorService, type ActorProfile } from '../../../api/Media/actorService';
import { discoveryService, type DiscoveryActor } from '../../../api/Discovery/discoveryService';
import { useFeatureFlags } from '../../../hooks/useFeatureFlags';
import CinematicBackdrop from '../../../components/Client/Primitives/CinematicBackdrop';
import MediaPoster from '../../../components/Client/Primitives/MediaPoster';
import InLibraryBadge from '../../../components/Client/Primitives/InLibraryBadge';
import EmptyState from '../../../components/Client/Primitives/EmptyState';

const DISCOVERY_PROVIDER = 'tmdb_discovery';

export default function ActorDetailsPage() {
    const { serverId, id } = useParams<{ serverId?: string, id: string }>();
    const navigate = useNavigate();
    const flags = useFeatureFlags();
    const [actor, setActor] = useState<ActorProfile | null>(null);
    const [knownFor, setKnownFor] = useState<DiscoveryActor | null>(null);
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

    // When Discover is enabled and the actor has a TMDB id, pull their wider
    // "Known For" list so the in-library filmography isn't the whole picture.
    useEffect(() => {
        let isMounted = true;
        setKnownFor(null);
        if (!flags.discover || !actor?.tmdbId) return;

        discoveryService.getActorDetails(DISCOVERY_PROVIDER, actor.tmdbId.toString(), serverId)
            .then(res => { if (isMounted) setKnownFor(res); })
            .catch(() => { /* discovery may be unavailable — just skip the row */ });

        return () => { isMounted = false; };
    }, [flags.discover, actor?.tmdbId, serverId]);

    const calculateAge = (birthday?: string, deathday?: string) => {
        if (!birthday) return null;
        const birth = new Date(birthday);
        const end = deathday ? new Date(deathday) : new Date();
        let age = end.getFullYear() - birth.getFullYear();
        const m = end.getMonth() - birth.getMonth();
        if (m < 0 || (m === 0 && end.getDate() < birth.getDate())) age--;
        return age;
    };

    const displayRole = useMemo(() => {
        if (!actor?.filmography) return 'Actor';
        const allRoles = actor.filmography.flatMap(f => (f.role || 'Actor').split(',').map(r => r.trim()));
        const uniqueRoles = Array.from(new Set(allRoles));
        return uniqueRoles.length > 0 ? uniqueRoles.join(', ') : 'Actor';
    }, [actor]);

    // Exclude Known For titles the actor already appears in locally (shown in
    // Filmography), matched by TMDB id + type.
    const knownForFiltered = useMemo(() => {
        if (!knownFor) return [];
        const owned = new Set(
            (actor?.filmography ?? [])
                .filter(f => f.tmdbId)
                .map(f => `${f.type}:${f.tmdbId}`)
        );
        return knownFor.filmography.filter(i => !owned.has(`${i.type}:${i.externalId}`));
    }, [knownFor, actor]);

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

    const age = calculateAge(actor.birthday, actor.deathday);
    const birthYear = actor.birthday ? new Date(actor.birthday).getFullYear() : null;
    const deathYear = actor.deathday ? new Date(actor.deathday).getFullYear() : null;

    return (
        <div className="relative min-h-full pb-20">
            <div className="absolute inset-x-0 top-0 z-0">
                <CinematicBackdrop src={actor.profileImageUrl} intensity="detail" parallax transitionKey={actor.id} />
            </div>

            <div className="relative z-10 pt-8">
                <div className="px-12">
                    <button
                        type="button"
                        onClick={() => navigate(-1)}
                        className="inline-flex cursor-pointer items-center gap-2 rounded-full px-3 py-1.5 text-sm font-medium backdrop-blur-md transition-colors hover:bg-[rgba(20,20,28,0.85)]"
                        style={{ background: 'rgba(20, 20, 28, 0.65)', border: '1px solid rgba(255, 255, 255, 0.14)', color: '#fafafa' }}
                    >
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="15 18 9 12 15 6" /></svg>
                        Back
                    </button>
                </div>

                <div className="mt-12 grid gap-10 px-12 md:grid-cols-[260px_1fr]">
                    <div className="shrink-0">
                        <div
                            className="relative aspect-[2/3] overflow-hidden"
                            style={{
                                borderRadius: 'var(--vora-radius-lg)',
                                boxShadow: 'var(--vora-shadow-lg)',
                                border: '1px solid var(--vora-border-subtle)',
                                background: 'var(--vora-bg-surface)',
                                maxWidth: 260,
                            }}
                        >
                            {actor.profileImageUrl ? (
                                <img src={actor.profileImageUrl} alt={actor.name} className="h-full w-full object-cover" />
                            ) : (
                                <div className="flex h-full w-full flex-col items-center justify-center" style={{ color: 'var(--vora-text-muted)' }}>
                                    <svg width="56" height="56" viewBox="0 0 24 24" fill="currentColor" className="mb-2">
                                        <path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 3c1.66 0 3 1.34 3 3s-1.34 3-3 3-3-1.34-3-3 1.34-3 3-3zm0 14.2c-2.5 0-4.71-1.28-6-3.22.03-1.99 4-3.08 6-3.08 1.99 0 5.97 1.09 6 3.08-1.29 1.94-3.5 3.22-6 3.22z" />
                                    </svg>
                                    <span className="text-sm">No image</span>
                                </div>
                            )}
                        </div>
                    </div>

                    <div className="min-w-0">
                        <div className="text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-accent-text)' }}>
                            {displayRole}
                        </div>
                        <h1 className="m-0 mt-2 font-semibold" style={{ color: 'var(--vora-text-primary)', fontSize: 'clamp(32px, 4vw, 48px)', lineHeight: 1.05, letterSpacing: '-0.02em' }}>
                            {actor.name}
                        </h1>

                        <div className="mt-3 flex flex-wrap items-center gap-3 text-sm" style={{ color: 'var(--vora-text-secondary)' }}>
                            {birthYear && (
                                <span>
                                    {birthYear}{deathYear ? ` – ${deathYear}` : ''}
                                    {age != null && <span style={{ color: 'var(--vora-text-muted)' }}> ({age} years old)</span>}
                                </span>
                            )}
                            {actor.placeOfBirth && <span style={{ color: 'var(--vora-text-muted)' }}>· Born in {actor.placeOfBirth}</span>}
                        </div>

                        <p
                            className="mt-6 line-clamp-[10] max-w-3xl text-base leading-relaxed transition-all hover:line-clamp-none"
                            style={{ color: 'var(--vora-text-secondary)' }}
                        >
                            {actor.biography || 'No biography available.'}
                        </p>
                    </div>
                </div>

                {actor.filmography && actor.filmography.length > 0 && (
                    <div className="mt-16 px-12">
                        <h2 className="m-0 mb-6 pb-2 text-xl font-semibold" style={{ color: 'var(--vora-text-primary)', borderBottom: '1px solid var(--vora-border-subtle)', letterSpacing: '-0.01em' }}>
                            Filmography
                        </h2>
                        <div className="grid gap-4 grid-cols-[repeat(auto-fill,minmax(140px,192px))]">
                            {actor.filmography.map(item => (
                                <MediaPoster
                                    key={item.id}
                                    imageUrl={item.posterUrl}
                                    title={item.title}
                                    subtitle={`${item.characterName || item.role} · ${item.releaseDate ? new Date(item.releaseDate).getFullYear() : 'Unknown'}`}
                                    onClick={() => navigate(serverId ? `/server/${serverId}/media/${item.id}` : `/media/${item.id}`)}
                                    fill
                                />
                            ))}
                        </div>
                    </div>
                )}

                {knownForFiltered.length > 0 && (
                    <div className="mt-16 px-12">
                        <h2 className="m-0 mb-6 pb-2 text-xl font-semibold" style={{ color: 'var(--vora-text-primary)', borderBottom: '1px solid var(--vora-border-subtle)', letterSpacing: '-0.01em' }}>
                            Known For
                        </h2>
                        <div className="grid gap-4 grid-cols-[repeat(auto-fill,minmax(140px,192px))]">
                            {knownForFiltered
                                .sort((a, b) => (b.year || 0) - (a.year || 0))
                                .map((item, idx) => (
                                    <MediaPoster
                                        key={`${item.externalId}-${idx}`}
                                        imageUrl={item.posterUrl}
                                        title={item.title}
                                        subtitle={item.year ? item.year.toString() : 'Unknown year'}
                                        badge={item.inLibrary ? <InLibraryBadge /> : undefined}
                                        onClick={() => navigate(serverId ? `/server/${serverId}/discovery/${DISCOVERY_PROVIDER}/${item.type}/${item.externalId}` : `/discovery/${DISCOVERY_PROVIDER}/${item.type}/${item.externalId}`)}
                                        fill
                                    />
                                ))}
                        </div>
                    </div>
                )}
            </div>
        </div>
    );
}
