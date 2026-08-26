import { useMemo } from 'react';
import MediaRow, { MediaRowItem } from './MediaRow';
import PersonCard from './PersonCard';

export interface CastRowMember {
    id: string;
    name: string;
    role: string;
    characterName?: string | null;
    profileImageUrl?: string | null;
    order?: number;
}

interface CastRowProps {
    cast: CastRowMember[];
    onSelect?: (member: CastRowMember) => void;
    title?: string;
}

// Actors lead the row; crew (producers, directors, writers) follow. The incoming
// list is billing-ordered, so a stable sort keeps that order within each group
// and only lifts the actors ahead of the crew. Someone credited as both (e.g.
// "Actor, Producer") counts as an actor.
const isActor = (member: CastRowMember) => /actor/i.test(member.role);

// Shared by the media details page and the discovery details page so an actor
// tile looks the same whether the title is in the library or not.
export default function CastRow({ cast, onSelect, title = 'Cast & Crew' }: CastRowProps) {
    const ordered = useMemo(
        () => [...(cast ?? [])].sort(
            (a, b) => (Number(isActor(b)) - Number(isActor(a))) || ((a.order ?? 0) - (b.order ?? 0))
        ),
        [cast]
    );

    if (ordered.length === 0) return null;

    return (
        <MediaRow title={title} variant="section">
            {ordered.map((member, index) => (
                <MediaRowItem key={`${member.id}-${index}`}>
                    <PersonCard
                        name={member.name}
                        role={member.role}
                        characterName={member.characterName}
                        imageUrl={member.profileImageUrl}
                        onClick={onSelect ? () => onSelect(member) : undefined}
                    />
                </MediaRowItem>
            ))}
        </MediaRow>
    );
}
