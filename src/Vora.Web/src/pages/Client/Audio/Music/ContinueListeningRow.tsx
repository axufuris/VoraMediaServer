import type { MusicResumeItemVM } from '../../../../api/Music/musicService';
import MediaCard from '../../../../components/Client/Primitives/MediaCard';
import MediaRow, { MediaRowItem } from '../../../../components/Client/Primitives/MediaRow';

interface ContinueListeningRowProps {
    items: MusicResumeItemVM[];
    onResume: (item: MusicResumeItemVM) => void;
}

// Where each music queue the profile left off stopped. A tile starts that
// queue again at the song it stopped on, so the row has no other action.
export default function ContinueListeningRow({ items, onResume }: ContinueListeningRowProps) {
    if (items.length === 0) return null;

    return (
        <MediaRow title="Continue Listening">
            {items.map(item => (
                <MediaRowItem key={`${item.source}-${item.sourceId ?? item.id}`}>
                    <MediaCard
                        title={item.title}
                        captionLines={item.subtitle ? [item.subtitle] : []}
                        imageUrl={item.artworkUrl ?? undefined}
                        mosaicUrls={item.artworkUrls}
                        shape="square"
                        size="xs"
                        onClick={() => onResume(item)}
                    />
                </MediaRowItem>
            ))}
        </MediaRow>
    );
}
