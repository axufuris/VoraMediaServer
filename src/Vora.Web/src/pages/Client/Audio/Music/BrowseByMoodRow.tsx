import { type MoodSummaryVM } from '../../../../api/Music/musicService';
import MediaRow, { MediaRowItem } from '../../../../components/Client/Primitives/MediaRow';
import BrowseTile from './BrowseTile';

interface BrowseByMoodRowProps {
    moods: MoodSummaryVM[];
    onOpen: (mood: string) => void;
}

const songCountLabel = (count: number) => `${count.toLocaleString()} ${count === 1 ? 'song' : 'songs'}`;

export default function BrowseByMoodRow({ moods, onOpen }: BrowseByMoodRowProps) {
    if (moods.length === 0) return null;

    return (
        <MediaRow title="Browse by Mood">
            {moods.map(m => (
                <MediaRowItem key={m.mood}>
                    <div style={{ width: 'var(--vora-card-w-xs)' }}>
                        <BrowseTile
                            name={m.name}
                            artworkUrl={m.sampleArtworkUrl}
                            detail={songCountLabel(m.trackCount)}
                            onClick={() => onOpen(m.mood)}
                        />
                    </div>
                </MediaRowItem>
            ))}
        </MediaRow>
    );
}
