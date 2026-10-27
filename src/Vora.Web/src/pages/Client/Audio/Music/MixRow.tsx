import type { ReactNode } from 'react';
import type { GeneratedMixSummaryVM } from '../../../../api/Music/musicService';
import MediaCard from '../../../../components/Client/Primitives/MediaCard';
import MediaRow, { MediaRowItem } from '../../../../components/Client/Primitives/MediaRow';

interface MixRowProps {
    title: string;
    mixes: GeneratedMixSummaryVM[];
    kicker: (mix: GeneratedMixSummaryVM) => string;
    onOpen: (mix: GeneratedMixSummaryVM) => void;
    badge?: ReactNode;
}

const trackCountLabel = (count: number) => `${count} ${count === 1 ? 'track' : 'tracks'}`;

export function MixRow({ title, mixes, kicker, onOpen, badge }: MixRowProps) {
    if (mixes.length === 0) return null;
    return (
        <MediaRow title={title}>
            {mixes.map(mix => (
                <MediaRowItem key={mix.id}>
                    <MediaCard
                        title={mix.name}
                        captionLines={[kicker(mix), trackCountLabel(mix.trackCount)]}
                        imageUrl={mix.artworkUrl}
                        shape="square"
                        size="xs"
                        badge={badge}
                        onClick={() => onOpen(mix)}
                    />
                </MediaRowItem>
            ))}
        </MediaRow>
    );
}

export function MadeForYouRow({ mixes, onOpen }: { mixes: GeneratedMixSummaryVM[]; onOpen: (mix: GeneratedMixSummaryVM) => void }) {
    return (
        <MixRow
            title="Made for You"
            mixes={mixes.filter(mix => mix.kind === 'DailyMix' || mix.kind === 'DiscoverMix')}
            kicker={mix => mix.kind === 'DiscoverMix' ? 'Discover' : `Daily Mix ${mix.slot}`}
            onOpen={onOpen}
        />
    );
}
