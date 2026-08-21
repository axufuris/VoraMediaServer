import { useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import type { CastMember } from '../../api/Media/mediaService';
import MediaRow from '../Common/MediaRow';

interface Props {
    cast: CastMember[];
    serverId?: string;
}

// Actors lead the row; crew (producers, directors, writers) follow. The incoming
// list is billing-ordered, so a stable sort keeps that order within each group
// and only lifts the actors ahead of the crew. Someone credited as both (e.g.
// "Actor, Producer") counts as an actor.
const isActor = (member: CastMember) => /actor/i.test(member.role);

export default function MediaCastRow({ cast, serverId }: Props) {
    const navigate = useNavigate();

    const orderedCast = useMemo(
        () => [...(cast ?? [])].sort(
            (a, b) => (Number(isActor(b)) - Number(isActor(a))) || (a.order - b.order)
        ),
        [cast]
    );

    if (orderedCast.length === 0) return null;

    return (
        <MediaRow title="Cast & Crew" variant="detail" gap="5">
            {orderedCast.map(actor => (
                <div key={actor.actorId} onClick={() => navigate(serverId ? `/server/${serverId}/actor/${actor.actorId}` : `/actor/${actor.actorId}`)} className="w-32 shrink-0 flex flex-col items-center text-center group cursor-pointer">
                    <div className="w-32 h-40 rounded-lg overflow-hidden bg-[var(--vora-bg-sunken)] mb-3 border border-[var(--vora-border-subtle)] shadow-lg relative group-hover:border-[var(--vora-accent-500)] transition-colors">
                        {actor.profileImageUrl ? (
                            <img src={actor.profileImageUrl} alt={actor.name} className="w-full h-full object-cover" />
                        ) : (
                            <div className="w-full h-full flex items-center justify-center bg-[var(--vora-bg-sunken)]">
                                <svg className="w-16 h-16 text-[var(--vora-text-muted)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 3c1.66 0 3 1.34 3 3s-1.34 3-3 3-3-1.34-3-3 1.34-3 3-3zm0 14.2c-2.5 0-4.71-1.28-6-3.22.03-1.99 4-3.08 6-3.08 1.99 0 5.97 1.09 6 3.08-1.29 1.94-3.5 3.22-6 3.22z" /></svg>
                            </div>
                        )}
                        <div className="absolute inset-0 bg-black/40 opacity-0 group-hover:opacity-100 transition-opacity flex items-center justify-center"></div>
                    </div>
                    <h3 className="font-bold text-[var(--vora-text-secondary)] text-sm leading-tight max-w-full truncate group-hover:text-[var(--vora-text-primary)] transition-colors">
                        {actor.name}
                    </h3>
                    <p className="text-xs text-[var(--vora-accent-500)] font-bold uppercase tracking-wider mt-1 line-clamp-1">
                        {actor.role}
                    </p>
                    <p className="text-xs text-[var(--vora-text-muted)] font-medium leading-tight mt-1 line-clamp-2 max-w-full">
                        {actor.characterName || '---'}
                    </p>
                </div>
            ))}
        </MediaRow>
    );
}
