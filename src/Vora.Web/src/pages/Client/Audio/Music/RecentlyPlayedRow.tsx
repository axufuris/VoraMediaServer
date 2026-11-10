import type { ArtistTrackVM, RecentlyPlayedItemVM } from '../../../../api/Music/musicService';
import MediaCard from '../../../../components/Client/Primitives/MediaCard';
import MediaRow, { MediaRowItem } from '../../../../components/Client/Primitives/MediaRow';
import LastFmCount from '../../../../components/Media/LastFmCount';
import { trackCaption } from './musicCaptions';

export type RecentlyPlayedSource = Exclude<RecentlyPlayedItemVM['kind'], 'Track'>;

interface RecentlyPlayedRowProps {
    items: RecentlyPlayedItemVM[];
    onOpen: (kind: RecentlyPlayedSource, id: string) => void;
    onPlaySongs: (tracks: ArtistTrackVM[], startIndex: number) => void;
}

const SOURCES: ReadonlySet<string> = new Set<RecentlyPlayedSource>(['Mix', 'Playlist', 'SmartPlaylist']);

// What the profile last played from: a playlist, smart playlist or mix (AI
// playlists included) as one tile, any other play as its song and album. A
// song plays the row's songs from there, as this row always has.
export default function RecentlyPlayedRow({ items, onOpen, onPlaySongs }: RecentlyPlayedRowProps) {
    const shown = items.filter(i => (i.kind === 'Track' ? !!i.track : SOURCES.has(i.kind)));
    if (shown.length === 0) return null;

    const songs = shown.flatMap(i => (i.kind === 'Track' && i.track ? [i.track] : []));

    return (
        <MediaRow title="Recently Played">
            {shown.map(item => {
                if (item.kind === 'Track' && item.track) {
                    const track = item.track;
                    return (
                        <MediaRowItem key={`Track-${item.id}`}>
                            <MediaCard
                                item={trackCaption(track)}
                                captionExtra={<LastFmCount value={track.globalListeners} unit="listeners" />}
                                imageUrl={track.albumArtworkUrl}
                                shape="square"
                                size="xs"
                                onClick={() => onPlaySongs(songs, songs.indexOf(track))}
                            />
                        </MediaRowItem>
                    );
                }

                const kind = item.kind as RecentlyPlayedSource;
                return (
                    <MediaRowItem key={`${kind}-${item.id}`}>
                        <MediaCard
                            title={item.title}
                            captionLines={item.subtitle ? [item.subtitle] : []}
                            imageUrl={item.artworkUrl ?? undefined}
                            mosaicUrls={item.artworkUrls}
                            shape="square"
                            size="xs"
                            onClick={() => onOpen(kind, item.id)}
                        />
                    </MediaRowItem>
                );
            })}
        </MediaRow>
    );
}
