import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { smartListService, type SmartListClientDto, type SmartListEntry, type SmartListSource } from '../../api/Collections/smartListService';
import type { IptvRecordingSessionVM } from '../../api/Iptv/dvrService';
import type { AlbumVM, MusicResumeItemVM } from '../../api/Music/musicService';
import { usePlayer } from '../../contexts/usePlayer';
import { useDialog } from '../../dialogs';
import { useResumeMusic } from '../../hooks/useResumeMusic';
import { useSignalREvent } from '../../hooks/useSignalREvent';
import { storeMusicNav } from '../../pages/Client/Audio/Music/storeMusicNav';
import { albumCaption } from '../../pages/Client/Audio/Music/musicCaptions';
import { albumCover } from '../../utils/albumCover';
import { formatAiredAt } from '../../utils/dvrDisplay';
import { channelPlayable, podcastPlayable, recordingPlayable, trackPlayable } from '../../utils/playables';
import MediaCard from '../Client/Primitives/MediaCard';
import MediaRow, { MediaRowItem } from '../Client/Primitives/MediaRow';
import { mediaMenuTarget } from '../Media/ItemMenu/mediaItemMenu';

interface SmartListRowProps {
    list: SmartListClientDto;
    serverId?: string;
    excludeLibraryIds?: string[] | null;
}

// One home-screen row. Library lists are posters that open the details page;
// the other sources are channels, stations, episodes, albums and recordings
// that play or open where they live. A row with nothing in it is not drawn.
export default function SmartListRow({ list, serverId, excludeLibraryIds }: SmartListRowProps) {
    const navigate = useNavigate();
    const dialog = useDialog();
    const { playMedia } = usePlayer();
    const resumeMusic = useResumeMusic(serverId);
    const [entries, setEntries] = useState<SmartListEntry[]>([]);
    const [loading, setLoading] = useState(true);
    const [refreshes, setRefreshes] = useState(0);
    const source: SmartListSource = list.source ?? 'Library';

    useEffect(() => {
        if (excludeLibraryIds === null) return;
        let current = true;
        smartListService.getListEntries(list.id, serverId, excludeLibraryIds)
            .then(data => {
                if (current) setEntries(data);
            })
            .catch(console.error)
            .finally(() => {
                if (current) setLoading(false);
            });
        return () => { current = false; };
    }, [list.id, serverId, excludeLibraryIds, refreshes]);

    const refresh = useCallback(() => setRefreshes(count => count + 1), []);

    const refreshFor = useCallback((...sources: SmartListSource[]) => {
        if (sources.includes(source)) refresh();
    }, [source, refresh]);

    useSignalREvent('LibraryUpdated', useCallback(() => refreshFor('Library', 'RecentlyAddedMusic'), [refreshFor]));
    useSignalREvent('MediaItemUpdated', useCallback(() => refreshFor('Library'), [refreshFor]));
    useSignalREvent('MusicAlbumUpdated', useCallback(() => refreshFor('RecentlyAddedMusic'), [refreshFor]));
    useSignalREvent('ChannelFavoritesUpdated', useCallback(() => refreshFor('FavoriteChannels', 'FavoriteStations'), [refreshFor]));
    useSignalREvent('PodcastEpisodesUpdated', useCallback(() => refreshFor('NewPodcastEpisodes'), [refreshFor]));
    useSignalREvent('DvrSessionsUpdated', useCallback(() => refreshFor('RecentRecordings'), [refreshFor]));
    useSignalREvent('MusicResumeUpdated', useCallback(() => refreshFor('ContinueListening'), [refreshFor]));

    const openMedia = (id: string) => navigate(serverId ? `/server/${serverId}/media/${id}` : `/media/${id}`);

    const openAlbum = (album: AlbumVM) => {
        storeMusicNav({ view: 'album', artistId: album.artistId, albumId: album.id });
        navigate(serverId ? `/server/${serverId}/music` : '/music');
    };

    const playRecording = async (recording: IptvRecordingSessionVM) => {
        try {
            playMedia(await recordingPlayable(recording, formatAiredAt(recording.startTime), serverId));
        } catch (error) {
            console.error('Failed to start the recording', error);
            await dialog.alert({ title: 'Playback error', message: 'The recording could not be started. The file may be missing.', tone: 'danger' });
        }
    };

    const continueListening = async (item: MusicResumeItemVM) => {
        try {
            if (await resumeMusic(item)) return;
        } catch (error) {
            console.error('Failed to continue listening', error);
        }
        refresh();
    };

    if (loading) return source === 'Library' ? <div className="vora-skeleton mx-8 mb-8 h-48" /> : null;
    if (entries.length === 0) return null;

    const renderEntry = (entry: SmartListEntry) => {
        const caption = entry.subtitle ? [entry.subtitle] : [];

        if (entry.kind === 'Media' && entry.media) {
            const media = entry.media;
            if (media.type === 'Track') {
                return (
                    <MediaCard
                        item={{ type: 'Track', title: media.title, artistName: media.artist }}
                        imageUrl={media.posterUrl}
                        shape="square"
                        size="xs"
                        onClick={() => playMedia(trackPlayable(media, serverId))}
                    />
                );
            }
            return (
                <MediaCard
                    item={media}
                    imageUrl={media.posterUrl}
                    isPlayed={media.isPlayed}
                    unplayedCount={media.unplayedItemCount}
                    onClick={() => openMedia(media.id)}
                    menu={mediaMenuTarget(media, serverId, { onChanged: refresh })}
                />
            );
        }

        if ((entry.kind === 'Channel' || entry.kind === 'Station') && entry.channel) {
            const channel = entry.channel;
            const isTv = entry.kind === 'Channel';
            return (
                <MediaCard
                    title={entry.title}
                    captionLines={caption}
                    imageUrl={entry.imageUrl}
                    imageFit="contain"
                    uncachedImage
                    shape={isTv ? 'still' : 'square'}
                    size={isTv ? 'sm' : 'xs'}
                    onClick={() => playMedia(channelPlayable(channel, entry.subtitle, serverId))}
                />
            );
        }

        if (entry.kind === 'PodcastEpisode' && entry.podcastEpisode) {
            const episode = entry.podcastEpisode;
            const progress = !episode.isPlayed && episode.durationSeconds && episode.positionSeconds > 5
                ? Math.min(100, (episode.positionSeconds / episode.durationSeconds) * 100)
                : undefined;
            return (
                <MediaCard
                    title={entry.title}
                    captionLines={caption}
                    imageUrl={entry.imageUrl}
                    uncachedImage
                    shape="square"
                    size="xs"
                    isPlayed={episode.isPlayed}
                    progressPercent={progress}
                    onClick={() => playMedia(podcastPlayable(episode, serverId))}
                />
            );
        }

        if (entry.kind === 'Album' && entry.album) {
            const album = entry.album;
            return <MediaCard item={albumCaption(album)} imageUrl={albumCover(album)} shape="square" size="xs" onClick={() => openAlbum(album)} />;
        }

        if (entry.kind === 'MusicResume' && entry.musicResume) {
            const item = entry.musicResume;
            return (
                <MediaCard
                    title={entry.title}
                    captionLines={caption}
                    imageUrl={entry.imageUrl ?? undefined}
                    mosaicUrls={item.artworkUrls}
                    shape="square"
                    size="xs"
                    onClick={() => { void continueListening(item); }}
                />
            );
        }

        if (entry.kind === 'Recording' && entry.recording) {
            const recording = entry.recording;
            return (
                <MediaCard
                    title={entry.title}
                    captionLines={[...caption, formatAiredAt(recording.startTime)]}
                    imageUrl={entry.imageUrl}
                    imageFit="contain"
                    uncachedImage
                    shape="still"
                    size="sm"
                    onClick={() => playRecording(recording)}
                />
            );
        }

        return null;
    };

    return (
        <MediaRow title={list.title}>
            {entries.map(entry => (
                <MediaRowItem key={`${entry.kind}-${entry.id}`}>
                    {renderEntry(entry)}
                </MediaRowItem>
            ))}
        </MediaRow>
    );
}
