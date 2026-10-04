import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { smartListService, type SmartListClientDto, type SmartListEntry, type SmartListSource } from '../../api/Collections/smartListService';
import type { IptvRecordingSessionVM } from '../../api/Iptv/dvrService';
import type { AlbumVM } from '../../api/Music/musicService';
import { usePlayer } from '../../contexts/usePlayer';
import { useDialog } from '../../dialogs';
import { useSignalREvent } from '../../hooks/useSignalREvent';
import { storeMusicNav } from '../../pages/Client/Audio/Music/storeMusicNav';
import { albumCaption } from '../../pages/Client/Audio/Music/musicCaptions';
import { albumCover } from '../../utils/albumCover';
import { formatAiredAt } from '../../utils/dvrDisplay';
import { channelPlayable, podcastPlayable, recordingPlayable, trackPlayable } from '../../utils/playables';
import MediaCard from '../Client/Primitives/MediaCard';
import MediaRow, { MediaRowItem } from '../Client/Primitives/MediaRow';

interface SmartListRowProps {
    list: SmartListClientDto;
    serverId?: string;
}

// One home-screen row. Library lists are posters that open the details page;
// the other sources are channels, stations, episodes, albums and recordings
// that play or open where they live. A row with nothing in it is not drawn.
export default function SmartListRow({ list, serverId }: SmartListRowProps) {
    const navigate = useNavigate();
    const dialog = useDialog();
    const { playMedia } = usePlayer();
    const [entries, setEntries] = useState<SmartListEntry[]>([]);
    const [loading, setLoading] = useState(true);
    const source: SmartListSource = list.source ?? 'Library';

    const fetchEntries = useCallback((silent = false) => {
        smartListService.getListEntries(list.id, serverId)
            .then(setEntries)
            .catch(console.error)
            .finally(() => {
                if (!silent) setLoading(false);
            });
    }, [list.id, serverId]);

    useEffect(() => {
        fetchEntries();
    }, [fetchEntries]);

    const refreshFor = useCallback((...sources: SmartListSource[]) => {
        if (sources.includes(source)) fetchEntries(true);
    }, [source, fetchEntries]);

    useSignalREvent('LibraryUpdated', useCallback(() => refreshFor('Library', 'RecentlyAddedMusic'), [refreshFor]));
    useSignalREvent('MediaItemUpdated', useCallback(() => refreshFor('Library'), [refreshFor]));
    useSignalREvent('MusicAlbumUpdated', useCallback(() => refreshFor('RecentlyAddedMusic'), [refreshFor]));
    useSignalREvent('ChannelFavoritesUpdated', useCallback(() => refreshFor('FavoriteChannels', 'FavoriteStations'), [refreshFor]));
    useSignalREvent('PodcastEpisodesUpdated', useCallback(() => refreshFor('NewPodcastEpisodes'), [refreshFor]));
    useSignalREvent('DvrSessionsUpdated', useCallback(() => refreshFor('RecentRecordings'), [refreshFor]));

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
            return <MediaCard item={media} imageUrl={media.posterUrl} isPlayed={media.isPlayed} onClick={() => openMedia(media.id)} />;
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
